using System.Text;

namespace Pino.Vm.Regex;

/// <summary>
/// Recursive-descent parser for Pino's RE2-style regular expression syntax (D-190).
/// Operates directly on UTF-8 bytes so the C# and C engines share identical decoding and errors.
/// </summary>
internal ref struct RegexParser {
  private const int MaxRepetitionCount = 1000;

  private readonly ReadOnlySpan<byte> _src;
  private readonly string _patternText;
  private int _pos;

  private RegexParser(ReadOnlySpan<byte> src) {
    _src = src;
    _patternText = Encoding.UTF8.GetString(src);
    _pos = 0;
  }

  public static RegexNode Parse(ReadOnlySpan<byte> patternUtf8) {
    var parser = new RegexParser(patternUtf8);
    RegexFlags flags = RegexFlags.None;
    RegexNode node = parser.ParseAlt(ref flags, insideGroup: false);
    if (parser._pos < parser._src.Length) {
      if (parser._src[parser._pos] == (byte) ')') {
        throw parser.Error("unexpected )");
      }

      throw parser.Error("unexpected )");
    }

    return node;
  }

  private PinoRegexParseException Error(string reason) => new(_patternText, reason);

  private bool AtEnd => _pos >= _src.Length;

  private byte PeekByte() => _src[_pos];

  private int ReadRune() {
    int r = RegexUtf8.Decode(_src, _pos, out int width);
    _pos += width;
    return r;
  }

  private RegexNode ParseAlt(ref RegexFlags flags, bool insideGroup) {
    List<RegexNode>? branches = null;
    RegexNode first = ParseConcat(ref flags, insideGroup);
    while (!AtEnd && PeekByte() == (byte) '|') {
      _pos++;
      branches ??= [first];
      branches.Add(ParseConcat(ref flags, insideGroup));
    }

    return branches is null ? first : new AltNode(branches);
  }

  private RegexNode ParseConcat(ref RegexFlags flags, bool insideGroup) {
    var items = new List<RegexNode>();
    bool canRepeat = false;
    bool lastWasRepeat = false;
    bool lastLazySet = false;

    while (!AtEnd) {
      byte b = PeekByte();
      if (b == (byte) '|') {
        break;
      }

      if (b == (byte) ')') {
        if (insideGroup) {
          break;
        }

        throw Error("unexpected )");
      }

      // Quantifiers
      if (b is (byte) '*' or (byte) '+' or (byte) '?') {
        _pos++;
        // Check lazy modifier '?' immediately after an existing quantifier.
        if (b == (byte) '?' && lastWasRepeat && !lastLazySet) {
          var prev = (RepeatNode) items[^1];
          items[^1] = new RepeatNode(prev.Child, prev.Min, prev.Max, greedy: false);
          lastLazySet = true;
          continue;
        }

        if (!canRepeat || lastWasRepeat) {
          throw Error($"nothing to repeat before '{(char) b}'");
        }

        (int min, int max) = b switch {
          (byte) '*' => (0, -1),
          (byte) '+' => (1, -1),
          _ => (0, 1),
        };
        items[^1] = new RepeatNode(items[^1], min, max, greedy: true);
        lastWasRepeat = true;
        lastLazySet = false;
        continue;
      }

      if (b == (byte) '{') {
        (int min, int max, string rawText) = ParseBraceQuantifier();
        if (!canRepeat || lastWasRepeat) {
          throw Error($"nothing to repeat before '{{{rawText}}}'");
        }

        items[^1] = new RepeatNode(items[^1], min, max, greedy: true);
        lastWasRepeat = true;
        lastLazySet = false;
        continue;
      }

      // Atoms
      if (b == (byte) '(') {
        RegexNode? group = ParseGroup(ref flags);
        if (group is null) {
          // Flag-only group like (?i): not a repeatable atom.
          canRepeat = false;
          lastWasRepeat = false;
          lastLazySet = false;
        } else {
          items.Add(group);
          canRepeat = true;
          lastWasRepeat = false;
          lastLazySet = false;
        }

        continue;
      }

      if (b == (byte) '[') {
        items.Add(ParseCharClass(flags));
        canRepeat = true;
        lastWasRepeat = false;
        lastLazySet = false;
        continue;
      }

      if (b == (byte) '.') {
        _pos++;
        items.Add(new AnyNode((flags & RegexFlags.DotAll) != 0));
        canRepeat = true;
        lastWasRepeat = false;
        lastLazySet = false;
        continue;
      }

      if (b == (byte) '^') {
        _pos++;
        items.Add(new AssertNode((flags & RegexFlags.Multiline) != 0 ? RegexAssertKind.LineStart : RegexAssertKind.TextStart));
        canRepeat = true;
        lastWasRepeat = false;
        lastLazySet = false;
        continue;
      }

      if (b == (byte) '$') {
        _pos++;
        items.Add(new AssertNode((flags & RegexFlags.Multiline) != 0 ? RegexAssertKind.LineEnd : RegexAssertKind.TextEnd));
        canRepeat = true;
        lastWasRepeat = false;
        lastLazySet = false;
        continue;
      }

      if (b == (byte) '\\') {
        items.Add(ParseEscapeAtom(flags));
        canRepeat = true;
        lastWasRepeat = false;
        lastLazySet = false;
        continue;
      }

      // Literal rune (including bare ']' and bare '}' per RE2/Go)
      int rune = ReadRune();
      items.Add(new LiteralNode(rune, (flags & RegexFlags.IgnoreCase) != 0));
      canRepeat = true;
      lastWasRepeat = false;
      lastLazySet = false;
    }

    return items.Count switch {
      0 => EmptyNode.Instance,
      1 => items[0],
      _ => new ConcatNode(items),
    };
  }

  private (int Min, int Max, string RawText) ParseBraceQuantifier() {
    _pos++; // consume '{'
    int start = _pos;
    int close = -1;
    for (int i = _pos; i < _src.Length; i++) {
      if (_src[i] == (byte) '}') {
        close = i;
        break;
      }
    }

    if (close < 0) {
      string rest = Encoding.UTF8.GetString(_src[start..]);
      throw Error($"invalid repetition count {{{rest}}}");
    }

    ReadOnlySpan<byte> inner = _src[start..close];
    string rawText = Encoding.UTF8.GetString(inner);
    _pos = close + 1;

    if (inner.Length == 0) {
      throw Error($"invalid repetition count {{{rawText}}}");
    }

    int idx = 0;
    if (!TryParseCount(inner, ref idx, out int min)) {
      throw Error($"invalid repetition count {{{rawText}}}");
    }

    int max;
    if (idx == inner.Length) {
      if (min > MaxRepetitionCount) {
        throw Error("repetition count above 1000");
      }

      max = min;
    } else if (inner[idx] == (byte) ',') {
      idx++;
      if (idx == inner.Length) {
        if (min > MaxRepetitionCount) {
          throw Error("repetition count above 1000");
        }

        max = -1;
      } else {
        if (!TryParseCount(inner, ref idx, out max) || idx != inner.Length) {
          throw Error($"invalid repetition count {{{rawText}}}");
        }

        if (min > MaxRepetitionCount || max > MaxRepetitionCount) {
          throw Error("repetition count above 1000");
        }

        if (min > max) {
          throw Error($"invalid repetition count {{{rawText}}}");
        }
      }
    } else {
      throw Error($"invalid repetition count {{{rawText}}}");
    }

    return (min, max, rawText);
  }

  private static bool TryParseCount(ReadOnlySpan<byte> span, ref int idx, out int value) {
    value = 0;
    int start = idx;
    while (idx < span.Length && span[idx] is >= (byte) '0' and <= (byte) '9') {
      int digit = span[idx] - '0';
      if (value <= MaxRepetitionCount) {
        value = value * 10 + digit;
      }

      idx++;
    }

    return idx > start;
  }

  /// <summary>Returns <see langword="null"/> for a standalone flag directive like <c>(?i)</c>.</summary>
  private RegexNode? ParseGroup(ref RegexFlags flags) {
    _pos++; // consume '('
    if (AtEnd) {
      throw Error("missing )");
    }

    if (PeekByte() != (byte) '?') {
      RegexFlags saved = flags;
      RegexNode inner = ParseAlt(ref flags, insideGroup: true);
      if (AtEnd || PeekByte() != (byte) ')') {
        throw Error("missing )");
      }

      _pos++;
      flags = saved;
      return inner;
    }

    _pos++; // consume '?'
    if (AtEnd) {
      throw Error("missing )");
    }

    byte next = PeekByte();
    // Lookaround rejection with exact prefix quoted
    if (next == (byte) '=') {
      throw Error("lookaround like '(?=' isn't supported; Pino's regex always runs in linear time");
    }

    if (next == (byte) '!') {
      throw Error("lookaround like '(?!' isn't supported; Pino's regex always runs in linear time");
    }

    if (next == (byte) '<' && _pos + 1 < _src.Length) {
      byte after = _src[_pos + 1];
      if (after == (byte) '=') {
        throw Error("lookaround like '(?<=' isn't supported; Pino's regex always runs in linear time");
      }

      if (after == (byte) '!') {
        throw Error("lookaround like '(?<!' isn't supported; Pino's regex always runs in linear time");
      }
    }

    // Non-capturing group (?:...)
    if (next == (byte) ':') {
      _pos++;
      RegexFlags saved = flags;
      RegexNode inner = ParseAlt(ref flags, insideGroup: true);
      if (AtEnd || PeekByte() != (byte) ')') {
        throw Error("missing )");
      }

      _pos++;
      flags = saved;
      return inner;
    }

    // Inline flags (?ims-ims) or scoped flags (?ims-ims:...)
    RegexFlags add = RegexFlags.None;
    RegexFlags clear = RegexFlags.None;
    bool negating = false;
    bool seenAny = false;
    bool seenAfterDash = false;

    while (!AtEnd) {
      int r = ReadRune();
      if (r == ')') {
        if (!seenAny || (negating && !seenAfterDash)) {
          throw Error("unknown flag ')'");
        }

        flags = (flags | add) & ~clear;
        return null;
      }

      if (r == ':') {
        if (!seenAny || (negating && !seenAfterDash)) {
          throw Error("unknown flag ':'");
        }

        RegexFlags saved = flags;
        flags = (flags | add) & ~clear;
        RegexNode inner = ParseAlt(ref flags, insideGroup: true);
        if (AtEnd || PeekByte() != (byte) ')') {
          throw Error("missing )");
        }

        _pos++;
        flags = saved;
        return inner;
      }

      if (r == '-') {
        if (negating) {
          throw Error("unknown flag '-'");
        }

        negating = true;
        continue;
      }

      RegexFlags bit = r switch {
        'i' => RegexFlags.IgnoreCase,
        'm' => RegexFlags.Multiline,
        's' => RegexFlags.DotAll,
        _ => throw Error($"unknown flag '{RegexUtf8.FormatRune(r)}'"),
      };
      if (negating) {
        clear |= bit;
        add &= ~bit;
        seenAfterDash = true;
      } else {
        add |= bit;
        clear &= ~bit;
      }

      seenAny = true;
    }

    throw Error("missing )");
  }

  private RegexNode ParseEscapeAtom(RegexFlags flags) {
    _pos++; // consume '\'
    if (AtEnd) {
      throw Error("invalid escape '\\'");
    }

    int r = ReadRune();
    if (r is >= '1' and <= '9') {
      throw Error($"backreferences like '\\{(char) r}' aren't supported; Pino's regex always runs in linear time");
    }

    return r switch {
      'b' => new AssertNode(RegexAssertKind.WordBoundary),
      'B' => new AssertNode(RegexAssertKind.NotWordBoundary),
      'A' => new AssertNode(RegexAssertKind.TextStart),
      'z' => new AssertNode(RegexAssertKind.TextEnd),
      'd' => new ClassNode(RegexCharClass.Digits),
      'D' => new ClassNode(RegexCharClass.NotDigits),
      'w' => new ClassNode(RegexCharClass.Word),
      'W' => new ClassNode(RegexCharClass.NotWord),
      's' => new ClassNode(RegexCharClass.Re2Space),
      'S' => new ClassNode(RegexCharClass.Re2NotSpace),
      _ => new LiteralNode(ParseSingleEscapeRune(r), (flags & RegexFlags.IgnoreCase) != 0),
    };
  }

  private int ParseSingleEscapeRune(int r) => r switch {
    'n' => '\n',
    't' => '\t',
    'r' => '\r',
    'f' => '\f',
    'v' => '\v',
    '0' => '\0',
    'x' => ParseHexEscape(),
    '\\' or '.' or '+' or '*' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '^' or '$' or '|' or '-' => r,
    _ => throw Error($"invalid escape '\\{RegexUtf8.FormatRune(r)}'"),
  };

  private int ParseHexEscape() {
    if (!AtEnd && PeekByte() == (byte) '{') {
      _pos++; // consume '{'
      int value = 0;
      int digits = 0;
      while (!AtEnd && PeekByte() != (byte) '}') {
        int d = HexDigit(PeekByte());
        if (d < 0) {
          throw Error("invalid escape '\\x'");
        }

        _pos++;
        digits++;
        if (value <= 0x10FFFF) {
          value = (value << 4) | d;
        }
      }

      if (AtEnd || PeekByte() != (byte) '}' || digits == 0 || value > 0x10FFFF || (value is >= 0xD800 and <= 0xDFFF)) {
        throw Error("invalid escape '\\x'");
      }

      _pos++; // consume '}'
      return value;
    }

    if (_pos + 2 > _src.Length) {
      throw Error("invalid escape '\\x'");
    }

    int hi = HexDigit(_src[_pos]);
    int lo = HexDigit(_src[_pos + 1]);
    if (hi < 0 || lo < 0) {
      throw Error("invalid escape '\\x'");
    }

    _pos += 2;
    return (hi << 4) | lo;
  }

  private static int HexDigit(byte b) => b switch {
    >= (byte) '0' and <= (byte) '9' => b - '0',
    >= (byte) 'a' and <= (byte) 'f' => b - 'a' + 10,
    >= (byte) 'A' and <= (byte) 'F' => b - 'A' + 10,
    _ => -1,
  };

  private ClassNode ParseCharClass(RegexFlags flags) {
    _pos++; // consume '['
    if (AtEnd) {
      throw Error("missing ]");
    }

    bool negate = false;
    if (PeekByte() == (byte) '^') {
      negate = true;
      _pos++;
    }

    bool fold = (flags & RegexFlags.IgnoreCase) != 0;
    var ranges = new List<RuneRange>();
    bool first = true;

    while (!AtEnd) {
      if (!first && PeekByte() == (byte) ']') {
        _pos++;
        return new ClassNode(RegexCharClass.Build(ranges, negate));
      }

      first = false;
      (bool isShorthand, int singleRune, RegexCharClass? shorthandClass) = ParseClassItem();
      if (isShorthand) {
        ranges.AddRange(shorthandClass!.Ranges);
        continue;
      }

      // Check for a-b range (only if '-' is NOT immediately followed by ']').
      if (!AtEnd && PeekByte() == (byte) '-' && _pos + 1 < _src.Length && _src[_pos + 1] != (byte) ']') {
        _pos++; // consume '-'
        (bool endIsShorthand, int endRune, _) = ParseClassItem();
        if (endIsShorthand) {
          throw Error($"invalid character range {RegexUtf8.FormatRune(singleRune)}-\\");
        }

        if (singleRune > endRune) {
          throw Error($"invalid character range {RegexUtf8.FormatRune(singleRune)}-{RegexUtf8.FormatRune(endRune)}");
        }

        if (fold) {
          RegexCharClass.AddFoldedRange(ranges, singleRune, endRune);
        } else {
          ranges.Add(new RuneRange(singleRune, endRune));
        }
      } else if (fold) {
        RegexCharClass.AddFoldedRange(ranges, singleRune, singleRune);
      } else {
        ranges.Add(new RuneRange(singleRune, singleRune));
      }
    }

    throw Error("missing ]");
  }

  private (bool IsShorthand, int Rune, RegexCharClass? Shorthand) ParseClassItem() {
    if (PeekByte() == (byte) '\\') {
      _pos++;
      if (AtEnd) {
        throw Error("invalid escape '\\'");
      }

      int r = ReadRune();
      return r switch {
        'd' => (true, 0, RegexCharClass.Digits),
        'D' => (true, 0, RegexCharClass.NotDigits),
        'w' => (true, 0, RegexCharClass.Word),
        'W' => (true, 0, RegexCharClass.NotWord),
        's' => (true, 0, RegexCharClass.Re2Space),
        'S' => (true, 0, RegexCharClass.Re2NotSpace),
        _ => (false, ParseSingleEscapeRune(r), null),
      };
    }

    return (false, ReadRune(), null);
  }
}
