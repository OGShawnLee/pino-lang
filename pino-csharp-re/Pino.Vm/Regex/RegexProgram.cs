using System.Text;

namespace Pino.Vm.Regex;

internal enum InstOp : byte {
  Rune = 0,
  RuneFold = 1,
  Any = 2,
  AnyNl = 3,
  Class = 4,
  Assert = 5,
  Split = 6,
  Jump = 7,
  Match = 8,
}

internal struct RegexInst {
  public InstOp Op;
  public int Arg;  // Rune, CanonicalRune, ClassIndex, AssertKind, or Jump/Split Out1
  public int Out2; // Split Out2 (lower priority branch)
}

/// <summary>
/// Compiled Thompson NFA program plus static analysis metadata for literal fast paths and the lazy DFA.
/// </summary>
internal sealed class RegexProgram {
  public RegexInst[] Instructions { get; }

  public RegexCharClass[] Classes { get; }

  public byte[] LiteralPrefix { get; }

  public bool IsExactLiteral { get; }

  public bool IsAnchoredStart { get; }

  public bool HasAsserts { get; }

  public bool CanUseDfa { get; }

  public bool IsUnambiguousGreedy { get; }

  /// <summary>Maps each ASCII byte 0..127 to its equivalence class index 0..NumAsciiClasses-1.</summary>
  public byte[] AsciiByteClass { get; }

  /// <summary>One representative ASCII byte for each equivalence class.</summary>
  public int[] AsciiClassRep { get; }

  public int NumAsciiClasses { get; }

  private RegexProgram(
      RegexInst[] instructions,
      RegexCharClass[] classes,
      byte[] literalPrefix,
      bool isExactLiteral,
      bool isAnchoredStart,
      bool hasAsserts,
      bool isUnambiguousGreedy,
      byte[] asciiByteClass,
      int[] asciiClassRep,
      int numAsciiClasses) {
    Instructions = instructions;
    Classes = classes;
    LiteralPrefix = literalPrefix;
    IsExactLiteral = isExactLiteral;
    IsAnchoredStart = isAnchoredStart;
    HasAsserts = hasAsserts;
    CanUseDfa = !hasAsserts && instructions.Length <= 4096;
    IsUnambiguousGreedy = isUnambiguousGreedy && !hasAsserts;
    AsciiByteClass = asciiByteClass;
    AsciiClassRep = asciiClassRep;
    NumAsciiClasses = numAsciiClasses;
  }

  public static RegexProgram Compile(RegexNode root) {
    RegexNode simplified = Simplify(root);
    (byte[] prefix, bool isExact) = ExtractLiteralPrefix(simplified);
    bool anchoredStart = CheckAnchoredStart(simplified);
    bool unambiguousGreedy = CheckUnambiguousGreedy(simplified);

    var insts = new List<RegexInst>();
    var classes = new List<RegexCharClass>();
    bool hasAsserts = false;

    Emit(simplified, insts, classes, ref hasAsserts);
    insts.Add(new RegexInst { Op = InstOp.Match });

    RegexInst[] instArray = insts.ToArray();
    RegexCharClass[] classArray = classes.ToArray();

    (byte[] byteClass, int[] classRep, int numClasses) = !hasAsserts && instArray.Length <= 4096
        ? ComputeAsciiByteClasses(instArray, classArray)
        : ([], [], 0);

    return new RegexProgram(
        instArray,
        classArray,
        prefix,
        isExact,
        anchoredStart,
        hasAsserts,
        unambiguousGreedy,
        byteClass,
        classRep,
        numClasses);
  }

  private static RegexNode Simplify(RegexNode node) {
    switch (node) {
      case ConcatNode concat: {
          var flat = new List<RegexNode>(concat.Items.Count);
          foreach (RegexNode item in concat.Items) {
            RegexNode s = Simplify(item);
            if (s is EmptyNode) {
              continue;
            }

            if (s is ConcatNode inner) {
              flat.AddRange(inner.Items);
            } else {
              flat.Add(s);
            }
          }

          return flat.Count switch {
            0 => EmptyNode.Instance,
            1 => flat[0],
            _ => new ConcatNode(flat),
          };
        }

      case AltNode alt: {
          var flat = new List<RegexNode>(alt.Branches.Count);
          foreach (RegexNode branch in alt.Branches) {
            RegexNode s = Simplify(branch);
            if (s is AltNode inner) {
              flat.AddRange(inner.Branches);
            } else {
              flat.Add(s);
            }
          }

          return flat.Count == 1 ? flat[0] : new AltNode(flat);
        }

      case RepeatNode rep: {
          RegexNode child = Simplify(rep.Child);
          if (rep.Max == 0) {
            return EmptyNode.Instance;
          }

          if (rep.Min == 1 && rep.Max == 1) {
            return child;
          }

          return new RepeatNode(child, rep.Min, rep.Max, rep.Greedy);
        }

      default:
        return node;
    }
  }

  private static (byte[] Prefix, bool IsExact) ExtractLiteralPrefix(RegexNode node) {
    if (node is LiteralNode { FoldCase: false } single) {
      return (EncodeRune(single.Rune), true);
    }

    if (node is not ConcatNode concat) {
      return ([], false);
    }

    var bytes = new List<byte>();
    int litCount = 0;
    foreach (RegexNode item in concat.Items) {
      if (item is LiteralNode { FoldCase: false } lit) {
        AppendRune(bytes, lit.Rune);
        litCount++;
      } else {
        break;
      }
    }

    return (bytes.ToArray(), litCount == concat.Items.Count && bytes.Count > 0);
  }

  private static byte[] EncodeRune(int rune) {
    var list = new List<byte>(4);
    AppendRune(list, rune);
    return list.ToArray();
  }

  private static void AppendRune(List<byte> target, int rune) {
    Span<byte> buf = stackalloc byte[4];
    int n = new Rune(rune is < 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF) ? 0xFFFD : rune).EncodeToUtf8(buf);
    for (int i = 0; i < n; i++) {
      target.Add(buf[i]);
    }
  }

  private static bool CheckAnchoredStart(RegexNode node) => node switch {
    AssertNode a => a.Kind == RegexAssertKind.TextStart,
    ConcatNode c => c.Items.Count > 0 && CheckAnchoredStart(c.Items[0]),
    AltNode a => a.Branches.Count > 0 && a.Branches.TrueForAll(CheckAnchoredStart),
    RepeatNode r => r.Min > 0 && CheckAnchoredStart(r.Child),
    _ => false,
  };

  /// <summary>
  /// Conservative, mathematically sound check that leftmost-first and leftmost-longest agree for
  /// every match starting at any fixed position <c>start</c>.
  /// </summary>
  private static bool CheckUnambiguousGreedy(RegexNode node) => node switch {
    EmptyNode or LiteralNode or AnyNode or ClassNode => true,
    AssertNode => false,
    RepeatNode r => r.Greedy && IsSingleRuneNode(r.Child),
    ConcatNode c => CheckUnambiguousConcat(c.Items),
    AltNode a => CheckUnambiguousAlt(a.Branches),
    _ => false,
  };

  private static bool CheckUnambiguousConcat(List<RegexNode> items) {
    for (int i = 0; i < items.Count; i++) {
      RegexNode item = items[i];
      bool isLast = i == items.Count - 1;
      if (item is LiteralNode or AnyNode or ClassNode) {
        continue;
      }

      if (item is RepeatNode rep) {
        if (!rep.Greedy) {
          return false;
        }

        // Fixed-count single-rune or unambiguous repeat is fine when min == max.
        if (rep.Min == rep.Max && CheckUnambiguousGreedy(rep.Child)) {
          continue;
        }

        if (isLast) {
          if (IsSingleRuneNode(rep.Child)) {
            continue;
          }

          if (rep.Min == 0 && rep.Max == 1 && !CanMatchEmpty(rep.Child) && CheckUnambiguousGreedy(rep.Child)) {
            continue;
          }

          return false;
        }

        // Variable-length repeat before a suffix: safe if single-rune and disjoint from the suffix's first-rune set.
        if (!IsSingleRuneNode(rep.Child)) {
          return false;
        }

        RegexCharClass repClass = NodeToCharClass(rep.Child);
        if (!TryComputeSuffixFirstClass(items, i + 1, out RegexCharClass? nextFirst) || repClass.Intersects(nextFirst!)) {
          return false;
        }

        continue;
      }

      if (isLast && CheckUnambiguousGreedy(item)) {
        continue;
      }

      return false;
    }

    return true;
  }

  private static bool CheckUnambiguousAlt(List<RegexNode> branches) {
    var firsts = new List<RegexCharClass>(branches.Count);
    foreach (RegexNode branch in branches) {
      if (CanMatchEmpty(branch) || !CheckUnambiguousGreedy(branch)) {
        return false;
      }

      if (!TryComputeFirstClass(branch, out RegexCharClass? fc)) {
        return false;
      }

      foreach (RegexCharClass existing in firsts) {
        if (existing.Intersects(fc!)) {
          return false;
        }
      }

      firsts.Add(fc!);
    }

    return true;
  }

  private static bool IsSingleRuneNode(RegexNode node) => node is LiteralNode or AnyNode or ClassNode;

  private static bool CanMatchEmpty(RegexNode node) => node switch {
    EmptyNode => true,
    LiteralNode or AnyNode or ClassNode => false,
    AssertNode => true,
    ConcatNode c => c.Items.TrueForAll(CanMatchEmpty),
    AltNode a => a.Branches.Exists(CanMatchEmpty),
    RepeatNode r => r.Min == 0 || CanMatchEmpty(r.Child),
    _ => true,
  };

  private static RegexCharClass NodeToCharClass(RegexNode node) {
    switch (node) {
      case LiteralNode lit:
        if (!lit.FoldCase) {
          return RegexCharClass.FromSortedRanges([new(lit.Rune, lit.Rune)]);
        }

        var folded = new List<RuneRange>();
        RegexCharClass.AddFoldedRange(folded, lit.Rune, lit.Rune);
        return RegexCharClass.Build(folded, negate: false);

      case AnyNode any:
        return any.DotAll
            ? RegexCharClass.FromSortedRanges([new(0, RegexCharClass.MaxRune)])
            : RegexCharClass.FromSortedRanges([new(0, '\n' - 1), new('\n' + 1, RegexCharClass.MaxRune)]);

      case ClassNode cls:
        return cls.CharClass;

      default:
        throw new InvalidOperationException("Not a single-rune node.");
    }
  }

  private static bool TryComputeFirstClass(RegexNode node, out RegexCharClass? first) {
    switch (node) {
      case LiteralNode or AnyNode or ClassNode:
        first = NodeToCharClass(node);
        return true;

      case ConcatNode c:
        return TryComputeSuffixFirstClass(c.Items, 0, out first);

      case RepeatNode r:
        return TryComputeFirstClass(r.Child, out first);

      case AltNode a: {
          var combined = new List<RuneRange>();
          foreach (RegexNode b in a.Branches) {
            if (!TryComputeFirstClass(b, out RegexCharClass? bf)) {
              first = null;
              return false;
            }

            combined.AddRange(bf!.Ranges);
          }

          first = RegexCharClass.Build(combined, negate: false);
          return true;
        }

      default:
        first = null;
        return false;
    }
  }

  private static bool TryComputeSuffixFirstClass(List<RegexNode> items, int startIndex, out RegexCharClass? first) {
    var combined = new List<RuneRange>();
    for (int i = startIndex; i < items.Count; i++) {
      if (!TryComputeFirstClass(items[i], out RegexCharClass? fc)) {
        first = null;
        return false;
      }

      combined.AddRange(fc!.Ranges);
      if (!CanMatchEmpty(items[i])) {
        break;
      }
    }

    if (combined.Count == 0) {
      first = null;
      return false;
    }

    first = RegexCharClass.Build(combined, negate: false);
    return true;
  }

  private static void Emit(RegexNode node, List<RegexInst> insts, List<RegexCharClass> classes, ref bool hasAsserts) {
    switch (node) {
      case EmptyNode:
        break;

      case LiteralNode lit:
        insts.Add(new RegexInst {
          Op = lit.FoldCase ? InstOp.RuneFold : InstOp.Rune,
          Arg = lit.Rune,
        });
        break;

      case AnyNode any:
        insts.Add(new RegexInst { Op = any.DotAll ? InstOp.AnyNl : InstOp.Any });
        break;

      case ClassNode cls: {
          int idx = classes.Count;
          classes.Add(cls.CharClass);
          insts.Add(new RegexInst { Op = InstOp.Class, Arg = idx });
          break;
        }

      case AssertNode assert:
        hasAsserts = true;
        insts.Add(new RegexInst { Op = InstOp.Assert, Arg = (int) assert.Kind });
        break;

      case ConcatNode concat:
        foreach (RegexNode item in concat.Items) {
          Emit(item, insts, classes, ref hasAsserts);
        }

        break;

      case AltNode alt: {
          int n = alt.Branches.Count;
          int[] jumpFixups = new int[n - 1];
          for (int i = 0; i < n - 1; i++) {
            int splitIdx = insts.Count;
            insts.Add(new RegexInst { Op = InstOp.Split, Arg = splitIdx + 1, Out2 = 0 });
            Emit(alt.Branches[i], insts, classes, ref hasAsserts);
            jumpFixups[i] = insts.Count;
            insts.Add(new RegexInst { Op = InstOp.Jump, Arg = 0 });
            RegexInst sp = insts[splitIdx];
            sp.Out2 = insts.Count;
            insts[splitIdx] = sp;
          }

          Emit(alt.Branches[n - 1], insts, classes, ref hasAsserts);
          int endPc = insts.Count;
          for (int i = 0; i < jumpFixups.Length; i++) {
            RegexInst jmp = insts[jumpFixups[i]];
            jmp.Arg = endPc;
            insts[jumpFixups[i]] = jmp;
          }

          break;
        }

      case RepeatNode rep: {
          // Emit `min` mandatory copies.
          for (int i = 0; i < rep.Min; i++) {
            Emit(rep.Child, insts, classes, ref hasAsserts);
          }

          if (rep.Max < 0) {
            // Emit `*` or `*?`
            int splitIdx = insts.Count;
            insts.Add(new RegexInst { Op = InstOp.Split });
            int bodyStart = insts.Count;
            Emit(rep.Child, insts, classes, ref hasAsserts);
            insts.Add(new RegexInst { Op = InstOp.Jump, Arg = splitIdx });
            int endPc = insts.Count;
            insts[splitIdx] = new RegexInst {
              Op = InstOp.Split,
              Arg = rep.Greedy ? bodyStart : endPc,
              Out2 = rep.Greedy ? endPc : bodyStart,
            };
          } else {
            // Emit `max - min` optional copies (`?` or `??`).
            int optCount = rep.Max - rep.Min;
            for (int i = 0; i < optCount; i++) {
              int splitIdx = insts.Count;
              insts.Add(new RegexInst { Op = InstOp.Split });
              int bodyStart = insts.Count;
              Emit(rep.Child, insts, classes, ref hasAsserts);
              int endPc = insts.Count;
              insts[splitIdx] = new RegexInst {
                Op = InstOp.Split,
                Arg = rep.Greedy ? bodyStart : endPc,
                Out2 = rep.Greedy ? endPc : bodyStart,
              };
            }
          }

          break;
        }
    }
  }

  /// <summary>
  /// Partitions ASCII bytes 0..127 into equivalence classes where every NFA instruction treats all bytes
  /// in a class identically, shrinking DFA transition tables by ~10-30x.
  /// </summary>
  private static (byte[] ByteClass, int[] ClassRep, int NumClasses) ComputeAsciiByteClasses(
      RegexInst[] insts,
      RegexCharClass[] classes) {
    byte[] cls = new byte[128];
    int numClasses = 1;
    int[] splitMap = new int[128];

    void Refine(ulong maskLo, ulong maskHi) {
      if ((maskLo == 0 && maskHi == 0) || (maskLo == ulong.MaxValue && maskHi == ulong.MaxValue)) {
        return;
      }

      // Check which existing classes are split (have both matching and non-matching bytes).
      Span<byte> flags = stackalloc byte[128];
      for (int b = 0; b < 128; b++) {
        bool m = b < 64 ? ((maskLo >> b) & 1UL) != 0 : ((maskHi >> (b - 64)) & 1UL) != 0;
        flags[cls[b]] |= (byte) (m ? 1 : 2);
      }

      Array.Fill(splitMap, -1, 0, numClasses);
      for (int c = 0; c < numClasses; c++) {
        if (flags[c] == 3) {
          splitMap[c] = numClasses++;
        }
      }

      for (int b = 0; b < 128; b++) {
        int newC = splitMap[cls[b]];
        if (newC >= 0) {
          bool m = b < 64 ? ((maskLo >> b) & 1UL) != 0 : ((maskHi >> (b - 64)) & 1UL) != 0;
          if (m) {
            cls[b] = (byte) newC;
          }
        }
      }
    }

    foreach (RegexInst inst in insts) {
      switch (inst.Op) {
        case InstOp.Rune when (uint) inst.Arg < 128u:
          if (inst.Arg < 64) {
            Refine(1UL << inst.Arg, 0);
          } else {
            Refine(0, 1UL << (inst.Arg - 64));
          }

          break;

        case InstOp.RuneFold when (uint) inst.Arg < 128u: {
            ulong lo = 0;
            ulong hi = 0;
            int c = inst.Arg;
            if (c < 64) {
              lo |= 1UL << c;
            } else {
              hi |= 1UL << (c - 64);
            }

            if (c is >= 'a' and <= 'z') {
              int upper = c - 32;
              hi |= 1UL << (upper - 64);
            }

            Refine(lo, hi);
            break;
          }

        case InstOp.Any:
          Refine(1UL << '\n', 0);
          break;

        case InstOp.Class: {
            RegexCharClass cc = classes[inst.Arg];
            Refine(cc.AsciiLo, cc.AsciiHi);
            break;
          }
      }
    }

    int[] reps = new int[numClasses];
    Array.Fill(reps, -1);
    for (int b = 0; b < 128; b++) {
      int c = cls[b];
      if (reps[c] < 0) {
        reps[c] = b;
      }
    }

    return (cls, reps, numClasses);
  }
}
