using System.Text;

namespace Pino.Vm.Regex;

[Flags]
internal enum RegexFlags : byte {
  None = 0,
  IgnoreCase = 1 << 0,
  Multiline = 1 << 1,
  DotAll = 1 << 2,
}

[Flags]
internal enum RegexAssertKind : byte {
  None = 0,
  TextStart = 1 << 0,       // \A, or ^ without (?m)
  TextEnd = 1 << 1,         // \z, or $ without (?m)
  LineStart = 1 << 2,       // ^ with (?m)
  LineEnd = 1 << 3,         // $ with (?m)
  WordBoundary = 1 << 4,    // \b
  NotWordBoundary = 1 << 5, // \B
}

internal readonly record struct RuneRange(int Lo, int Hi);

/// <summary>
/// Normalized, sorted, disjoint Unicode rune ranges with a precomputed 128-bit ASCII bitmap
/// for O(1) matching on ASCII bytes.
/// </summary>
internal sealed class RegexCharClass {
  public const int MaxRune = 0x10FFFF;

  public static readonly RegexCharClass Digits = FromSortedRanges([new('0', '9')]);
  public static readonly RegexCharClass NotDigits = Negate(Digits);
  public static readonly RegexCharClass Word = FromSortedRanges([new('0', '9'), new('A', 'Z'), new('_', '_'), new('a', 'z')]);
  public static readonly RegexCharClass NotWord = Negate(Word);
  // RE2/D-190 \s is [\t\n\f\r ] (ASCII; U+000B \v is excluded).
  public static readonly RegexCharClass Re2Space = FromSortedRanges([new('\t', '\n'), new('\f', '\r'), new(' ', ' ')]);
  public static readonly RegexCharClass Re2NotSpace = Negate(Re2Space);

  public ulong AsciiLo { get; }

  public ulong AsciiHi { get; }

  public RuneRange[] Ranges { get; }

  private RegexCharClass(RuneRange[] sortedDisjoint) {
    Ranges = sortedDisjoint;
    ulong lo = 0;
    ulong hi = 0;
    foreach (RuneRange r in sortedDisjoint) {
      if (r.Lo >= 128) {
        break;
      }

      int end = Math.Min(r.Hi, 127);
      for (int c = r.Lo; c <= end; c++) {
        if (c < 64) {
          lo |= 1UL << c;
        } else {
          hi |= 1UL << (c - 64);
        }
      }
    }

    AsciiLo = lo;
    AsciiHi = hi;
  }

  public bool MatchesAscii(int b) =>
      b < 64 ? ((AsciiLo >> b) & 1UL) != 0 : ((AsciiHi >> (b - 64)) & 1UL) != 0;

  public bool Matches(int rune) {
    if ((uint) rune < 128u) {
      return MatchesAscii(rune);
    }

    int lo = 0;
    int hi = Ranges.Length - 1;
    while (lo <= hi) {
      int mid = lo + ((hi - lo) >> 1);
      RuneRange r = Ranges[mid];
      if (rune < r.Lo) {
        hi = mid - 1;
      } else if (rune > r.Hi) {
        lo = mid + 1;
      } else {
        return true;
      }
    }

    return false;
  }

  public bool Intersects(RegexCharClass other) {
    if ((AsciiLo & other.AsciiLo) != 0 || (AsciiHi & other.AsciiHi) != 0) {
      return true;
    }

    int i = 0;
    int j = 0;
    while (i < Ranges.Length && j < other.Ranges.Length) {
      RuneRange a = Ranges[i];
      RuneRange b = other.Ranges[j];
      if (a.Hi < b.Lo) {
        i++;
      } else if (b.Hi < a.Lo) {
        j++;
      } else {
        return true;
      }
    }

    return false;
  }

  public static RegexCharClass FromSortedRanges(RuneRange[] ranges) => new(ranges);

  public static RegexCharClass Build(List<RuneRange> raw, bool negate) {
    RuneRange[] merged = Normalize(raw);
    var cc = new RegexCharClass(merged);
    return negate ? Negate(cc) : cc;
  }

  public static RegexCharClass Negate(RegexCharClass cc) {
    var inv = new List<RuneRange>(cc.Ranges.Length + 1);
    int cur = 0;
    foreach (RuneRange r in cc.Ranges) {
      if (cur < r.Lo) {
        inv.Add(new RuneRange(cur, r.Lo - 1));
      }

      if (r.Hi >= MaxRune) {
        cur = MaxRune + 1;
        break;
      }

      cur = r.Hi + 1;
    }

    if (cur <= MaxRune) {
      inv.Add(new RuneRange(cur, MaxRune));
    }

    return new RegexCharClass(inv.ToArray());
  }

  public static RuneRange[] Normalize(List<RuneRange> raw) {
    if (raw.Count == 0) {
      return [];
    }

    raw.Sort(static (a, b) => a.Lo != b.Lo ? a.Lo.CompareTo(b.Lo) : a.Hi.CompareTo(b.Hi));
    var merged = new List<RuneRange>(raw.Count);
    RuneRange cur = raw[0];
    for (int i = 1; i < raw.Count; i++) {
      RuneRange next = raw[i];
      if (next.Lo <= cur.Hi + 1) {
        if (next.Hi > cur.Hi) {
          cur = new RuneRange(cur.Lo, next.Hi);
        }
      } else {
        merged.Add(cur);
        cur = next;
      }
    }

    merged.Add(cur);
    return merged.ToArray();
  }

  /// <summary>
  /// Appends [lo, hi] and every rune in the simple Unicode case-folding equivalence class of any rune in [lo, hi].
  /// Visits only overlapping orbit runs so even [0, 0x10FFFF] runs in microseconds.
  /// </summary>
  public static void AddFoldedRange(List<RuneRange> target, int lo, int hi) {
    target.Add(new RuneRange(lo, hi));
    UnicodeCaseFold.FoldRun[] runs = UnicodeCaseFold.OrbitRuns;

    // Binary search for the first run with run.Hi >= lo.
    int left = 0;
    int right = runs.Length;
    while (left < right) {
      int mid = left + ((right - left) >> 1);
      if (runs[mid].Hi < lo) {
        left = mid + 1;
      } else {
        right = mid;
      }
    }

    for (int idx = left; idx < runs.Length && runs[idx].Lo <= hi; idx++) {
      UnicodeCaseFold.FoldRun run = runs[idx];
      int start = Math.Max(lo, run.Lo);
      int end = Math.Min(hi, run.Hi);
      if (run.Stride == 1) {
        // Every rune r in [start, end] maps to r + delta. Because orbits can have size > 2
        // (e.g. k/K/K, s/S/ſ, σ/Σ/ς), check if the range is small or walk orbits.
        target.Add(new RuneRange(start + run.Delta, end + run.Delta));
        // For orbits of length >= 3 (very rare: ~20 runes in all of Unicode), follow one more hop
        // on the mapped endpoints/elements when delta points into a 3+ cycle.
        // We handle 3+ cycles by iterating only over the small number of non-involution runs
        // (or stepping through the cycle for any rune whose next(next(r)) != r).
        AddMultiCycleMembers(target, start, end, run.Stride);
      } else {
        // Stride == 2: align start to run.Lo parity.
        if (((start - run.Lo) & 1) != 0) {
          start++;
        }

        for (int r = start; r <= end; r += 2) {
          int cur = UnicodeCaseFold.NextInOrbit(r);
          while (cur != r) {
            target.Add(new RuneRange(cur, cur));
            cur = UnicodeCaseFold.NextInOrbit(cur);
          }
        }
      }
    }
  }

  private static void AddMultiCycleMembers(List<RuneRange> target, int start, int end, byte stride) {
    // Check if the first element of the run is part of a 2-cycle or larger cycle.
    // Even if a contiguous run contains an element of a 3-cycle (like 'k' or 's' inside 'a'..'z'),
    // walk the few multi-element orbits directly.
    for (int r = start; r <= end; r += stride) {
      int n1 = UnicodeCaseFold.NextInOrbit(r);
      int n2 = UnicodeCaseFold.NextInOrbit(n1);
      if (n2 != r) {
        int cur = n2;
        while (cur != r) {
          target.Add(new RuneRange(cur, cur));
          cur = UnicodeCaseFold.NextInOrbit(cur);
        }
      }
    }
  }
}

internal abstract class RegexNode;

internal sealed class EmptyNode : RegexNode {
  public static readonly EmptyNode Instance = new();
}

internal sealed class LiteralNode(int rune, bool foldCase) : RegexNode {
  public int Rune { get; } = foldCase ? UnicodeCaseFold.Canonical(rune) : rune;

  public bool FoldCase { get; } = foldCase;
}

internal sealed class AnyNode(bool dotAll) : RegexNode {
  public bool DotAll { get; } = dotAll;
}

internal sealed class ClassNode(RegexCharClass charClass) : RegexNode {
  public RegexCharClass CharClass { get; } = charClass;
}

internal sealed class AssertNode(RegexAssertKind kind) : RegexNode {
  public RegexAssertKind Kind { get; } = kind;
}

internal sealed class ConcatNode(List<RegexNode> items) : RegexNode {
  public List<RegexNode> Items { get; } = items;
}

internal sealed class AltNode(List<RegexNode> branches) : RegexNode {
  public List<RegexNode> Branches { get; } = branches;
}

internal sealed class RepeatNode(RegexNode child, int min, int max, bool greedy) : RegexNode {
  public RegexNode Child { get; } = child;

  public int Min { get; } = min;

  /// <summary>Maximum repetitions, or <c>-1</c> for unbounded.</summary>
  public int Max { get; } = max;

  public bool Greedy { get; } = greedy;
}

/// <summary>Thrown when a regex pattern violates the D-190 syntax rules.</summary>
public sealed class PinoRegexParseException : Exception {
  public PinoRegexParseException(string pattern, string reason)
      : base($"Invalid pattern '{pattern}': {reason}.") {
    Pattern = pattern;
    Reason = reason;
  }

  public string Pattern { get; }

  public string Reason { get; }
}

internal static class RegexUtf8 {
  /// <summary>Decodes one UTF-8 rune matching <c>pino_utf8_decode</c> in <c>runtime-re/pino_str.c</c>.</summary>
  public static int Decode(ReadOnlySpan<byte> bytes, int pos, out int width) {
    byte c = bytes[pos];
    if (c < 0x80) {
      width = 1;
      return c;
    }

    int len = bytes.Length;
    if (c is >= 0xC2 and <= 0xDF && pos + 1 < len && (bytes[pos + 1] & 0xC0) == 0x80) {
      width = 2;
      return ((c & 0x1F) << 6) | (bytes[pos + 1] & 0x3F);
    }

    if (c is >= 0xE0 and <= 0xEF && pos + 2 < len
        && (bytes[pos + 1] & 0xC0) == 0x80 && (bytes[pos + 2] & 0xC0) == 0x80) {
      int v = ((c & 0x0F) << 12) | ((bytes[pos + 1] & 0x3F) << 6) | (bytes[pos + 2] & 0x3F);
      if (v >= 0x800 && !(v is >= 0xD800 and <= 0xDFFF)) {
        width = 3;
        return v;
      }
    }

    if (c is >= 0xF0 and <= 0xF4 && pos + 3 < len
        && (bytes[pos + 1] & 0xC0) == 0x80 && (bytes[pos + 2] & 0xC0) == 0x80 && (bytes[pos + 3] & 0xC0) == 0x80) {
      int v = ((c & 0x07) << 18) | ((bytes[pos + 1] & 0x3F) << 12)
          | ((bytes[pos + 2] & 0x3F) << 6) | (bytes[pos + 3] & 0x3F);
      if (v is >= 0x10000 and <= 0x10FFFF) {
        width = 4;
        return v;
      }
    }

    width = 1;
    return 0xFFFD;
  }

  /// <summary>Decodes the rune ending immediately before <paramref name="pos"/> (returns -1 if <paramref name="pos"/> == 0).</summary>
  public static int DecodeBefore(ReadOnlySpan<byte> bytes, int pos) {
    if (pos <= 0) {
      return -1;
    }

    byte prev = bytes[pos - 1];
    if (prev < 0x80) {
      return prev;
    }

    // Walk back up to 3 continuation bytes.
    int start = pos - 1;
    int limit = Math.Max(0, pos - 4);
    while (start > limit && (bytes[start] & 0xC0) == 0x80) {
      start--;
    }

    int r = Decode(bytes, start, out int width);
    return start + width == pos ? r : 0xFFFD;
  }

  /// <summary>Advances by one UTF-8 rune from <paramref name="pos"/>.</summary>
  public static int NextRunePos(ReadOnlySpan<byte> bytes, int pos) {
    if (pos >= bytes.Length) {
      return bytes.Length;
    }

    _ = Decode(bytes, pos, out int width);
    return pos + width;
  }

  public static string FormatRune(int rune) {
    if (rune is < 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF)) {
      rune = 0xFFFD;
    }

    return new Rune(rune).ToString();
  }

  public static bool IsAsciiWord(int rune) =>
      rune is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';
}
