using System.Text;

namespace Pino.Vm.Regex;

/// <summary>
/// Pino's linear-time regular expression engine (D-190):
/// Thompson NFA + Pike VM (Russ Cox 2009), literal/prefix fast paths, and an ASCII byte-class
/// lazy DFA (Russ Cox 2007/2010) with sound <c>TRANS_RESET</c> window tracking.
/// Written from scratch for Pino Lang (MIT).
/// </summary>
public sealed class PinoRegex {
  private const int MaxDfaStates = 1024;
  private const int TransUnknown = -1;
  private const int TransDead = -2;
  private const int TransResetBit = 0x40000000;
  private const int TransStateMask = 0x3FFFFFFF;

  private readonly RegexProgram _prog;
  private readonly LazyDfa? _unanchoredDfa;
  private readonly LazyDfa? _anchoredDfa;

  private PinoRegex(RegexProgram prog) {
    _prog = prog;
    if (prog.CanUseDfa && !prog.IsExactLiteral) {
      _unanchoredDfa = new LazyDfa(prog, anchored: false);
      if (prog.IsUnambiguousGreedy) {
        _anchoredDfa = new LazyDfa(prog, anchored: true);
      }
    }
  }

  public static PinoRegex Compile(string pattern) => Compile(Encoding.UTF8.GetBytes(pattern));

  public static PinoRegex Compile(ReadOnlySpan<byte> patternUtf8) {
    RegexNode ast = RegexParser.Parse(patternUtf8);
    RegexProgram prog = RegexProgram.Compile(ast);
    return new PinoRegex(prog);
  }

  public bool HasMatch(string text) => HasMatch(Encoding.UTF8.GetBytes(text));

  public bool HasMatch(ReadOnlySpan<byte> text) {
    if (_prog.IsExactLiteral) {
      return text.IndexOf(_prog.LiteralPrefix) >= 0;
    }

    if (_prog.IsAnchoredStart) {
      return RunPikeVm(text, 0, anchored: true, out _, out _);
    }

    if (_unanchoredDfa is not null) {
      int dfaResult = _unanchoredDfa.HasMatchAscii(text, 0);
      if (dfaResult >= 0) {
        return dfaResult != 0;
      }
    }

    return FindFrom(text, 0, out _, out _);
  }

  public bool MatchPrefix(string text, out int endByte) => MatchPrefix(Encoding.UTF8.GetBytes(text), out endByte);

  public bool MatchPrefix(ReadOnlySpan<byte> text, out int endByte) {
    if (_prog.IsExactLiteral) {
      if (text.StartsWith(_prog.LiteralPrefix)) {
        endByte = _prog.LiteralPrefix.Length;
        return true;
      }

      endByte = 0;
      return false;
    }

    if (_prog.LiteralPrefix.Length > 0 && !text.StartsWith(_prog.LiteralPrefix)) {
      endByte = 0;
      return false;
    }

    if (_anchoredDfa is not null) {
      int end = _anchoredDfa.FindAnchoredLongestAscii(text, 0);
      if (end >= -1) {
        if (end >= 0) {
          endByte = end;
          return true;
        }

        endByte = 0;
        return false;
      }
    }

    if (RunPikeVm(text, 0, anchored: true, out _, out int mEnd)) {
      endByte = mEnd;
      return true;
    }

    endByte = 0;
    return false;
  }

  public bool Find(string text, out int startByte, out int endByte) =>
      Find(Encoding.UTF8.GetBytes(text), out startByte, out endByte);

  public bool Find(ReadOnlySpan<byte> text, out int startByte, out int endByte) =>
      FindFrom(text, 0, out startByte, out endByte);

  public List<(int Start, int End)> FindAll(string text) => FindAll(Encoding.UTF8.GetBytes(text));

  public List<(int Start, int End)> FindAll(ReadOnlySpan<byte> text) {
    var results = new List<(int Start, int End)>();
    if (_prog.IsExactLiteral) {
      int litLen = _prog.LiteralPrefix.Length;
      int p = 0;
      while (p <= text.Length - litLen) {
        int idx = text[p..].IndexOf(_prog.LiteralPrefix);
        if (idx < 0) {
          break;
        }

        int mStart = p + idx;
        results.Add((mStart, mStart + litLen));
        p = mStart + litLen;
      }

      return results;
    }

    int pos = 0;
    int prevMatchEnd = -1;
    while (pos <= text.Length) {
      if (!FindFrom(text, pos, out int mStart, out int mEnd)) {
        break;
      }

      if (mStart == mEnd && mStart == prevMatchEnd) {
        if (pos >= text.Length) {
          break;
        }

        pos = RegexUtf8.NextRunePos(text, pos);
        continue;
      }

      results.Add((mStart, mEnd));
      prevMatchEnd = mEnd;
      if (mEnd > mStart) {
        pos = mEnd;
      } else {
        if (mEnd >= text.Length) {
          break;
        }

        pos = RegexUtf8.NextRunePos(text, mEnd);
      }

      if (_prog.IsAnchoredStart) {
        break;
      }
    }

    return results;
  }

  public string Replace(string text, string repl) =>
      Encoding.UTF8.GetString(Replace(Encoding.UTF8.GetBytes(text), Encoding.UTF8.GetBytes(repl)));

  public byte[] Replace(ReadOnlySpan<byte> text, ReadOnlySpan<byte> repl) {
    if (_prog.IsExactLiteral) {
      int litLen = _prog.LiteralPrefix.Length;
      int first = text.IndexOf(_prog.LiteralPrefix);
      if (first < 0) {
        return text.ToArray();
      }

      var litOut = new List<byte>(text.Length + repl.Length);
      int litPos = 0;
      while (true) {
        int idx = text[litPos..].IndexOf(_prog.LiteralPrefix);
        if (idx < 0) {
          AppendSpan(litOut, text[litPos..]);
          break;
        }

        AppendSpan(litOut, text.Slice(litPos, idx));
        AppendSpan(litOut, repl);
        litPos += idx + litLen;
      }

      return litOut.ToArray();
    }

    int pos = 0;
    int copyFrom = 0;
    int prevMatchEnd = -1;
    List<byte>? output = null;

    while (pos <= text.Length) {
      if (!FindFrom(text, pos, out int mStart, out int mEnd)) {
        break;
      }

      if (mStart == mEnd && mStart == prevMatchEnd) {
        if (pos >= text.Length) {
          break;
        }

        pos = RegexUtf8.NextRunePos(text, pos);
        continue;
      }

      output ??= new List<byte>(text.Length + repl.Length);
      AppendSpan(output, text.Slice(copyFrom, mStart - copyFrom));
      AppendSpan(output, repl);
      copyFrom = mEnd;
      prevMatchEnd = mEnd;

      if (mEnd > mStart) {
        pos = mEnd;
      } else {
        if (mEnd >= text.Length) {
          break;
        }

        pos = RegexUtf8.NextRunePos(text, mEnd);
      }

      if (_prog.IsAnchoredStart) {
        break;
      }
    }

    if (output is null) {
      return text.ToArray();
    }

    AppendSpan(output, text[copyFrom..]);
    return output.ToArray();
  }

  private static void AppendSpan(List<byte> target, ReadOnlySpan<byte> span) {
    if (span.IsEmpty) {
      return;
    }

    int oldLen = target.Count;
    System.Runtime.InteropServices.CollectionsMarshal.SetCount(target, oldLen + span.Length);
    span.CopyTo(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(target)[oldLen..]);
  }

  private bool FindFrom(ReadOnlySpan<byte> text, int fromPos, out int startByte, out int endByte) {
    if (_prog.IsExactLiteral) {
      int idx = text[fromPos..].IndexOf(_prog.LiteralPrefix);
      if (idx >= 0) {
        startByte = fromPos + idx;
        endByte = startByte + _prog.LiteralPrefix.Length;
        return true;
      }

      startByte = 0;
      endByte = 0;
      return false;
    }

    if (_prog.IsAnchoredStart) {
      if (fromPos > 0) {
        startByte = 0;
        endByte = 0;
        return false;
      }

      return RunPikeVm(text, 0, anchored: true, out startByte, out endByte);
    }

    // Literal prefix jump when the pattern begins with exact case-sensitive bytes.
    if (_prog.LiteralPrefix.Length > 0) {
      int searchPos = fromPos;
      while (searchPos <= text.Length - _prog.LiteralPrefix.Length) {
        int idx = text[searchPos..].IndexOf(_prog.LiteralPrefix);
        if (idx < 0) {
          startByte = 0;
          endByte = 0;
          return false;
        }

        int cand = searchPos + idx;
        if (_anchoredDfa is not null) {
          int end = _anchoredDfa.FindAnchoredLongestAscii(text, cand);
          if (end >= 0) {
            startByte = cand;
            endByte = end;
            return true;
          }

          if (end == -1) {
            searchPos = RegexUtf8.NextRunePos(text, cand);
            continue;
          }
        }

        if (RunPikeVm(text, cand, anchored: true, out startByte, out endByte)) {
          return true;
        }

        searchPos = RegexUtf8.NextRunePos(text, cand);
      }

      startByte = 0;
      endByte = 0;
      return false;
    }

    // Lazy DFA fast path with sound TRANS_RESET windowStart tracking.
    if (_unanchoredDfa is not null) {
      int scanStatus = _unanchoredDfa.ScanForMatchAscii(text, fromPos, out int windowStart, out int firstMatchEnd);
      if (scanStatus == 0) {
        // Definitive no match in [fromPos..text.Length].
        startByte = 0;
        endByte = 0;
        return false;
      }

      if (scanStatus == 1) {
        // Match found; leftmost match is guaranteed to start at >= windowStart.
        // Fast path for unambiguous greedy patterns when match started right at windowStart
        // (e.g. lexer patterns like [A-Za-z_]\w*|[0-9]+): run anchored DFA directly with zero Pike VM calls.
        if (_anchoredDfa is not null && windowStart == firstMatchEnd - 1) {
          int end = _anchoredDfa.FindAnchoredLongestAscii(text, windowStart);
          if (end >= 0) {
            startByte = windowStart;
            endByte = end;
            return true;
          }
        }

        return RunPikeVm(text, windowStart, anchored: false, out startByte, out endByte);
      }

      // scanStatus < 0: non-ASCII byte or cache cap hit; windowStart is still a sound lower bound!
      return RunPikeVm(text, windowStart, anchored: false, out startByte, out endByte);
    }

    return RunPikeVm(text, fromPos, anchored: false, out startByte, out endByte);
  }

  // -------------------------------------------------------------------------
  // Pike VM (Russ Cox 2009) — guaranteed O(M * N), exact leftmost-first
  // -------------------------------------------------------------------------

  private struct Thread {
    public int Pc;
    public int StartPos;
  }

  private bool RunPikeVm(ReadOnlySpan<byte> text, int fromPos, bool anchored, out int matchStart, out int matchEnd) {
    RegexInst[] insts = _prog.Instructions;
    int ninst = insts.Length;

    Thread[] clist = new Thread[ninst];
    Thread[] nlist = new Thread[ninst];
    int[] visited = new int[ninst];
    int[] stack = new int[ninst];
    int ccount = 0;
    int ncount = 0;
    int gen = 0;

    bool matched = false;
    int bestStart = -1;
    int bestEnd = -1;

    int len = text.Length;
    int bytePos = fromPos;
    int prevRune = RegexUtf8.DecodeBefore(text, bytePos);

    while (true) {
      int curRune;
      int width;
      if (bytePos < len) {
        curRune = RegexUtf8.Decode(text, bytePos, out width);
      } else {
        curRune = -1;
        width = 0;
      }

      if (!matched && (!anchored || bytePos == fromPos)) {
        gen++;
        // Preserve already-active threads' visited bits so earlier-starting threads keep priority.
        for (int i = 0; i < ccount; i++) {
          visited[clist[i].Pc] = gen;
        }

        AddThread(insts, clist, ref ccount, visited, stack, gen, 0, bytePos, bytePos, len, prevRune, curRune);
      }

      if (ccount == 0) {
        if (matched || anchored || bytePos >= len) {
          break;
        }
      }

      int nextBytePos = bytePos + width;
      int nextRune = nextBytePos < len ? RegexUtf8.Decode(text, nextBytePos, out _) : -1;

      gen++;
      ncount = 0;

      for (int i = 0; i < ccount; i++) {
        Thread t = clist[i];
        RegexInst inst = insts[t.Pc];
        if (inst.Op == InstOp.Match) {
          matched = true;
          bestStart = t.StartPos;
          bestEnd = bytePos;
          // Discard all lower-priority threads in clist.
          break;
        }

        if (curRune < 0) {
          continue;
        }

        if (MatchesRune(inst, curRune)) {
          AddThread(insts, nlist, ref ncount, visited, stack, gen, t.Pc + 1, t.StartPos, nextBytePos, len, curRune, nextRune);
        }
      }

      // Swap clist and nlist.
      (clist, nlist) = (nlist, clist);
      ccount = ncount;

      if (bytePos >= len) {
        break;
      }

      if (ccount == 0 && (matched || anchored)) {
        break;
      }

      prevRune = curRune;
      bytePos = nextBytePos;
    }

    if (matched) {
      matchStart = bestStart;
      matchEnd = bestEnd;
      return true;
    }

    matchStart = 0;
    matchEnd = 0;
    return false;
  }

  private void AddThread(
      RegexInst[] insts,
      Thread[] list,
      ref int count,
      int[] visited,
      int[] stack,
      int gen,
      int startPc,
      int startPos,
      int bytePos,
      int textLen,
      int prevRune,
      int curRune) {
    int sp = 0;
    stack[sp++] = startPc;
    while (sp > 0) {
      int pc = stack[--sp];
      if (visited[pc] == gen) {
        continue;
      }

      visited[pc] = gen;
      RegexInst inst = insts[pc];
      switch (inst.Op) {
        case InstOp.Jump:
          stack[sp++] = inst.Arg;
          break;

        case InstOp.Split:
          // Push Out2 first so Out1 (higher priority) is popped first.
          stack[sp++] = inst.Out2;
          stack[sp++] = inst.Arg;
          break;

        case InstOp.Assert:
          if (CheckAssert((RegexAssertKind) inst.Arg, bytePos, textLen, prevRune, curRune)) {
            stack[sp++] = pc + 1;
          }

          break;

        default:
          list[count++] = new Thread { Pc = pc, StartPos = startPos };
          break;
      }
    }
  }

  private bool MatchesRune(in RegexInst inst, int rune) => inst.Op switch {
    InstOp.Rune => rune == inst.Arg,
    InstOp.RuneFold => UnicodeCaseFold.Canonical(rune) == inst.Arg,
    InstOp.Any => rune != '\n',
    InstOp.AnyNl => true,
    InstOp.Class => _prog.Classes[inst.Arg].Matches(rune),
    _ => false,
  };

  private static bool CheckAssert(RegexAssertKind kind, int bytePos, int textLen, int prevRune, int curRune) => kind switch {
    RegexAssertKind.TextStart => bytePos == 0,
    RegexAssertKind.TextEnd => bytePos == textLen,
    RegexAssertKind.LineStart => bytePos == 0 || prevRune == '\n',
    RegexAssertKind.LineEnd => bytePos == textLen || curRune == '\n',
    RegexAssertKind.WordBoundary => RegexUtf8.IsAsciiWord(prevRune) != RegexUtf8.IsAsciiWord(curRune),
    RegexAssertKind.NotWordBoundary => RegexUtf8.IsAsciiWord(prevRune) == RegexUtf8.IsAsciiWord(curRune),
    _ => false,
  };

  // -------------------------------------------------------------------------
  // ASCII Byte-Class Lazy DFA (Russ Cox 2007/2010)
  // -------------------------------------------------------------------------

  private sealed class LazyDfa {
    private sealed class DfaState(int[] nfaPcs, bool isMatch, int numClasses) {
      public int[] NfaPcs { get; } = nfaPcs;

      public bool IsMatch { get; } = isMatch;

      public int[] Next { get; } = CreateUnknownTransitions(numClasses);

      private static int[] CreateUnknownTransitions(int n) {
        int[] arr = new int[n];
        Array.Fill(arr, TransUnknown);
        return arr;
      }
    }

    private readonly RegexProgram _prog;
    private readonly bool _anchored;
    private readonly int _numClasses;
    private readonly object _lock = new();
    private readonly Dictionary<NfaSetKey, int> _stateMap = new();
    private DfaState[] _states = new DfaState[32];
    private int _stateCount;

    public LazyDfa(RegexProgram prog, bool anchored) {
      _prog = prog;
      _anchored = anchored;
      _numClasses = prog.NumAsciiClasses;
      InitStartState();
    }

    private void InitStartState() {
      _stateMap.Clear();
      _stateCount = 0;
      int[] startPcs = EpsilonClosure([0], out bool isMatch);
      _ = GetOrAddState(startPcs, isMatch);
    }

    /// <summary>
    /// Returns <c>1</c> if matched, <c>0</c> if definitively not matched, or <c>-1</c> to fall back to Pike VM
    /// (non-ASCII byte encountered or DFA state cap reached).
    /// </summary>
    public int HasMatchAscii(ReadOnlySpan<byte> text, int fromPos) {
      byte[] byteClass = _prog.AsciiByteClass;
      DfaState[] states = Volatile.Read(ref _states);
      int state = 0;
      if (states[0].IsMatch) {
        return 1;
      }

      for (int i = fromPos; i < text.Length; i++) {
        byte b = text[i];
        if (b >= 0x80) {
          return -1;
        }

        int cls = byteClass[b];
        int next = states[state].Next[cls];
        if (next == TransUnknown) {
          next = ComputeTransition(state, cls);
          if (next == TransUnknown) {
            return -1;
          }

          states = Volatile.Read(ref _states);
        }

        state = next & TransStateMask;
        if (states[state].IsMatch) {
          return 1;
        }
      }

      return 0;
    }

    /// <summary>
    /// Unanchored scan tracking sound <paramref name="windowStart"/> via <c>TRANS_RESET</c>.
    /// Returns <c>1</c> if match found, <c>0</c> if definitively no match, or <c>-1</c> to fall back to Pike VM from <paramref name="windowStart"/>.
    /// </summary>
    public int ScanForMatchAscii(ReadOnlySpan<byte> text, int fromPos, out int windowStart, out int firstMatchEnd) {
      byte[] byteClass = _prog.AsciiByteClass;
      DfaState[] states = Volatile.Read(ref _states);
      windowStart = fromPos;
      firstMatchEnd = fromPos;

      int state = 0;
      if (states[0].IsMatch) {
        return 1;
      }

      for (int i = fromPos; i < text.Length; i++) {
        byte b = text[i];
        if (b >= 0x80) {
          return -1;
        }

        int cls = byteClass[b];
        int next = states[state].Next[cls];
        if (next == TransUnknown) {
          next = ComputeTransition(state, cls);
          if (next == TransUnknown) {
            return -1;
          }

          states = Volatile.Read(ref _states);
        }

        if ((next & TransResetBit) != 0) {
          windowStart = i + 1;
        }

        state = next & TransStateMask;
        if (states[state].IsMatch) {
          firstMatchEnd = i + 1;
          return 1;
        }
      }

      return 0;
    }

    /// <summary>
    /// Anchored forward DFA scan for unambiguous greedy patterns.
    /// Returns match end byte (&gt;= startPos), <c>-1</c> if definitively no match at <paramref name="startPos"/>,
    /// or <c>-2</c> to fall back to Pike VM.
    /// </summary>
    public int FindAnchoredLongestAscii(ReadOnlySpan<byte> text, int startPos) {
      byte[] byteClass = _prog.AsciiByteClass;
      DfaState[] states = Volatile.Read(ref _states);
      int state = 0;
      int lastMatch = states[0].IsMatch ? startPos : -1;

      for (int i = startPos; i < text.Length; i++) {
        byte b = text[i];
        if (b >= 0x80) {
          return -2;
        }

        int cls = byteClass[b];
        int next = states[state].Next[cls];
        if (next == TransUnknown) {
          next = ComputeTransition(state, cls);
          if (next == TransUnknown) {
            return -2;
          }

          states = Volatile.Read(ref _states);
        }

        if (next == TransDead) {
          return lastMatch;
        }

        state = next & TransStateMask;
        if (states[state].IsMatch) {
          lastMatch = i + 1;
        }
      }

      return lastMatch;
    }

    private int ComputeTransition(int stateId, int byteCls) {
      lock (_lock) {
        DfaState st = _states[stateId];
        int existing = st.Next[byteCls];
        if (existing != TransUnknown) {
          return existing;
        }

        if (_stateCount >= MaxDfaStates) {
          return TransUnknown;
        }

        int repByte = _prog.AsciiClassRep[byteCls];
        RegexInst[] insts = _prog.Instructions;
        RegexCharClass[] classes = _prog.Classes;

        var seeds = new List<int>(st.NfaPcs.Length + 1);
        foreach (int pc in st.NfaPcs) {
          RegexInst inst = insts[pc];
          bool matches = inst.Op switch {
            InstOp.Rune => repByte == inst.Arg,
            InstOp.RuneFold => (repByte is >= 'A' and <= 'Z' ? repByte + 32 : repByte) == inst.Arg,
            InstOp.Any => repByte != '\n',
            InstOp.AnyNl => true,
            InstOp.Class => classes[inst.Arg].MatchesAscii(repByte),
            _ => false,
          };
          if (matches) {
            seeds.Add(pc + 1);
          }
        }

        bool stepWasEmpty = seeds.Count == 0;
        if (_anchored) {
          if (stepWasEmpty) {
            st.Next[byteCls] = TransDead;
            return TransDead;
          }
        } else {
          if (stepWasEmpty) {
            // Every in-flight thread died; next state is start state 0 with TransResetBit set.
            int res = TransResetBit; // state 0 | TransResetBit
            st.Next[byteCls] = res;
            return res;
          }

          // Unanchored: also seed a fresh thread at pc 0.
          seeds.Add(0);
        }

        int[] nextPcs = EpsilonClosure(seeds, out bool isMatch);
        int nextId = GetOrAddState(nextPcs, isMatch);
        if (nextId < 0) {
          return TransUnknown;
        }

        st.Next[byteCls] = nextId;
        return nextId;
      }
    }

    private int GetOrAddState(int[] sortedPcs, bool isMatch) {
      var key = new NfaSetKey(sortedPcs, isMatch);
      if (_stateMap.TryGetValue(key, out int existing)) {
        return existing;
      }

      if (_stateCount >= MaxDfaStates) {
        return -1;
      }

      int id = _stateCount;
      if (id == _states.Length) {
        var bigger = new DfaState[Math.Min(MaxDfaStates, _states.Length * 2)];
        Array.Copy(_states, bigger, _states.Length);
        Volatile.Write(ref _states, bigger);
      }

      _states[id] = new DfaState(sortedPcs, isMatch, _numClasses);
      _stateCount = id + 1;
      _stateMap[key] = id;
      return id;
    }

    private int[] EpsilonClosure(IReadOnlyList<int> seeds, out bool isMatch) {
      RegexInst[] insts = _prog.Instructions;
      int ninst = insts.Length;
      bool[] seen = new bool[ninst];
      int[] stack = new int[ninst];
      int sp = 0;

      for (int i = 0; i < seeds.Count; i++) {
        stack[sp++] = seeds[i];
      }

      var result = new List<int>();
      isMatch = false;

      while (sp > 0) {
        int pc = stack[--sp];
        if (seen[pc]) {
          continue;
        }

        seen[pc] = true;
        RegexInst inst = insts[pc];
        switch (inst.Op) {
          case InstOp.Jump:
            stack[sp++] = inst.Arg;
            break;

          case InstOp.Split:
            stack[sp++] = inst.Out2;
            stack[sp++] = inst.Arg;
            break;

          case InstOp.Match:
            isMatch = true;
            break;

          default:
            result.Add(pc);
            break;
        }
      }

      result.Sort();
      return result.ToArray();
    }

    private readonly struct NfaSetKey : IEquatable<NfaSetKey> {
      private readonly int[] _pcs;
      private readonly bool _isMatch;
      private readonly int _hash;

      public NfaSetKey(int[] pcs, bool isMatch) {
        _pcs = pcs;
        _isMatch = isMatch;
        int h = isMatch ? 37 : 17;
        for (int i = 0; i < pcs.Length; i++) {
          h = h * 31 + pcs[i];
        }

        _hash = h;
      }

      public bool Equals(NfaSetKey other) {
        if (_hash != other._hash || _isMatch != other._isMatch || _pcs.Length != other._pcs.Length) {
          return false;
        }

        for (int i = 0; i < _pcs.Length; i++) {
          if (_pcs[i] != other._pcs[i]) {
            return false;
          }
        }

        return true;
      }

      public override bool Equals(object? obj) => obj is NfaSetKey other && Equals(other);

      public override int GetHashCode() => _hash;
    }
  }
}
