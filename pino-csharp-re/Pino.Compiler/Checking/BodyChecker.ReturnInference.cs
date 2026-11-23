using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  private readonly Dictionary<Symbol, CallableEntry> _callablesBySymbol = new(ReferenceEqualityComparer.Instance);
  private readonly List<CallableEntry> _sccStack = [];
  private readonly DiagnosticBag _rootDiagnostics;
  private readonly HashSet<Symbol> _rootAssignedSymbols;
  private ReturnCollector? _activeReturnCollector;
  private int _inPhase3a;

  private sealed class CallableEntry {
    public CallableEntry(
        Symbol symbol,
        SyntaxNode declarationSyntax,
        BlockStatementSyntax body,
        Scope callableScope,
        StructSymbol? enclosingStruct,
        bool isStaticMethod,
        bool hasExplicitReturn,
        bool isNestedFunction,
        bool hasMissingHeader) {
      Symbol = symbol;
      DeclarationSyntax = declarationSyntax;
      Body = body;
      CallableScope = callableScope;
      EnclosingStruct = enclosingStruct;
      IsStaticMethod = isStaticMethod;
      HasExplicitReturn = hasExplicitReturn;
      IsNestedFunction = isNestedFunction;
      HasMissingHeader = hasMissingHeader;
    }

    public Symbol Symbol { get; }
    public SyntaxNode DeclarationSyntax { get; }
    public BlockStatementSyntax Body { get; }
    public Scope CallableScope { get; }
    public StructSymbol? EnclosingStruct { get; }
    public bool IsStaticMethod { get; }
    public bool HasExplicitReturn { get; }
    public bool IsNestedFunction { get; }
    public bool HasMissingHeader { get; }

    public string KindName => Symbol is MethodSymbol ? "method" : "function";
    public string CapitalizedKindName => Symbol is MethodSymbol ? "Method" : "Function";

    public bool BodyFinalized { get; set; }
    public bool OnSccStack { get; set; }
    public bool IsInSolvingScc { get; set; }
    public int SccIndex { get; set; } = -1;
    public int SccLow { get; set; } = -1;
    public bool SawPending { get; set; }
    public TypeSymbol? ProvisionalReturn { get; set; }
    public bool HasValueReturn { get; set; }
    public bool HasPendingReturn { get; set; }
    public bool HasErrorReturn { get; set; }
    public bool HasOnlyNeverReturns { get; set; }
    public bool HasConflictingReturns { get; set; }
    public bool ReportedCycleError { get; set; }

    public ReturnInferenceState GetReturnState() => Symbol switch {
      FunctionSymbol fn => fn.ReturnState,
      MethodSymbol method => method.ReturnState,
      _ => ReturnInferenceState.Explicit,
    };

    public TypeSymbol GetReturnType() => Symbol switch {
      FunctionSymbol fn => fn.ReturnType,
      MethodSymbol method => method.ReturnType,
      _ => ErrorType.Instance,
    };

    public void SetReturnState(ReturnInferenceState state) {
      if (Symbol is FunctionSymbol fn) {
        fn.ReturnState = state;
      } else if (Symbol is MethodSymbol method) {
        method.ReturnState = state;
      }
    }

    public void SetReturnType(TypeSymbol type, ReturnInferenceState state) {
      if (Symbol is FunctionSymbol fn) {
        fn.ReturnType = type;
        fn.ReturnState = state;
      } else if (Symbol is MethodSymbol method) {
        method.ReturnType = type;
        method.ReturnState = state;
      }
    }
  }

  private sealed class ReturnCollector {
    public ReturnCollector(
        string kindName,
        string capitalizedKindName,
        string callableName,
        TypeSymbol? expectedReturn,
        bool isLambda) {
      KindName = kindName;
      CapitalizedKindName = capitalizedKindName;
      CallableName = callableName;
      ExpectedReturn = expectedReturn;
      IsLambda = isLambda;
    }

    public string KindName { get; }
    public string CapitalizedKindName { get; }
    public string CallableName { get; }
    public TypeSymbol? ExpectedReturn { get; }
    public bool IsLambda { get; }

    public List<(ReturnStatementSyntax Statement, ExpressionSyntax Expression, TypeSymbol Type)> ValueReturns { get; } = [];
    public List<ReturnStatementSyntax> BareReturns { get; } = [];
  }

  private void InitializeCallableRegistry() {
    foreach ((FunctionDeclarationSyntax decl, FunctionSymbol sym, Scope fnScope) in _state.Functions) {
      bool isNested = fnScope.Parent?.Kind != ScopeKind.Module;
      bool hasMissingHeader = HasMissingHeaderParts(decl.OpenParenToken, decl.CloseParenToken, decl.Parameters);
      var entry = new CallableEntry(
          sym,
          decl,
          decl.Body,
          fnScope,
          enclosingStruct: null,
          isStaticMethod: false,
          hasExplicitReturn: decl.ReturnType is not null,
          isNestedFunction: isNested,
          hasMissingHeader: hasMissingHeader);
      _callablesBySymbol[sym] = entry;
    }

    foreach ((StructDeclarationSyntax structDecl, StructSymbol structSym, _, _) in _state.Structs) {
      foreach (SyntaxNode member in structDecl.Members) {
        if (member is MethodDeclarationSyntax methodDecl
            && _state.DeclaredSymbols[methodDecl.Id] is MethodSymbol methodSym) {
          Scope methodScope = ScopeOf(methodDecl, ScopeKind.Method);
          bool hasMissingHeader = HasMissingHeaderParts(methodDecl.OpenParenToken, methodDecl.CloseParenToken, methodDecl.Parameters);
          var entry = new CallableEntry(
              methodSym,
              methodDecl,
              methodDecl.Body,
              methodScope,
              enclosingStruct: structSym,
              isStaticMethod: methodSym.IsStatic,
              hasExplicitReturn: methodDecl.ReturnType is not null,
              isNestedFunction: false,
              hasMissingHeader: hasMissingHeader);
          _callablesBySymbol[methodSym] = entry;
        }
      }
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Two-phase SCC fixed-point return inference (D-050, FN-27..29, FN-E4, FN-E5, FN-E7, OQ-12/13)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol EnsureCallableReturnType(FunctionSymbol fn) {
    if (fn.ReturnState is ReturnInferenceState.Explicit or ReturnInferenceState.Resolved) {
      return fn.ReturnType;
    }

    if (!_callablesBySymbol.TryGetValue(fn, out CallableEntry? entry)) {
      return fn.ReturnType;
    }

    return EnsureCallableEntryReturnType(entry);
  }

  private TypeSymbol EnsureCallableReturnType(MethodSymbol method) {
    if (method.PromotedFrom is { } original) {
      TypeSymbol resolved = EnsureCallableReturnType(original);
      if (original.ReturnState is ReturnInferenceState.Explicit or ReturnInferenceState.Resolved) {
        method.ReturnType = original.ReturnType;
        method.ReturnState = original.ReturnState;
      }

      return resolved;
    }

    if (method.ReturnState is ReturnInferenceState.Explicit or ReturnInferenceState.Resolved) {
      return method.ReturnType;
    }

    if (!_callablesBySymbol.TryGetValue(method, out CallableEntry? entry)) {
      return method.ReturnType;
    }

    return EnsureCallableEntryReturnType(entry);
  }

  private void EnsureStructMethodsResolved(StructSymbol structSymbol) {
    foreach (MethodSymbol method in structSymbol.Methods) {
      if (!method.IsStatic && method.ReturnState == ReturnInferenceState.Unchecked) {
        EnsureCallableReturnType(method);
      }
    }
  }

  private void EnsureCallableBodyChecked(Symbol symbol) {
    if (!_callablesBySymbol.TryGetValue(symbol, out CallableEntry? entry)) {
      return;
    }

    if (entry.BodyFinalized) {
      return;
    }

    if (entry.HasExplicitReturn) {
      if (_inPhase3a > 0) {
        return;
      }

      entry.BodyFinalized = true;
      CheckExplicitCallableBody(entry);
      return;
    }

    if (entry.GetReturnState() == ReturnInferenceState.Resolved && _inPhase3a == 0) {
      ResetCallableBodyState(entry.Body);
      var finalCollector = new ReturnCollector(
          entry.KindName,
          entry.CapitalizedKindName,
          entry.Symbol.Name,
          expectedReturn: null,
          isLambda: false);
      CheckUnannotatedBodyOnce(entry, finalCollector);
      if (!entry.ReportedCycleError) {
        TypeSymbol verified = FinalizeUnannotatedReturns(entry, finalCollector, emitDiagnostics: true);
        if (!ReferenceEquals(verified, entry.GetReturnType())) {
          entry.SetReturnType(verified, ReturnInferenceState.Resolved);
        }
      }

      entry.BodyFinalized = true;
      return;
    }

    EnsureCallableEntryReturnType(entry);
  }

  private TypeSymbol EnsureCallableEntryReturnType(CallableEntry entry) {
    ReturnInferenceState state = entry.GetReturnState();
    if (state is ReturnInferenceState.Explicit or ReturnInferenceState.Resolved) {
      return entry.GetReturnType();
    }

    // Active SCC solving round (Phase 3a): return current provisional return or PendingReturnType.
    if (entry.IsInSolvingScc) {
      if (_sccStack.Count > 0) {
        _sccStack[^1].SawPending = true;
      }

      return entry.ProvisionalReturn ?? new PendingReturnType(entry.Symbol);
    }

    // Back-edge to an InProgress callable on the discovery stack: link into the active SCC!
    if (entry.OnSccStack) {
      if (_sccStack.Count > 0) {
        CallableEntry caller = _sccStack[^1];
        if (entry.SccIndex < caller.SccLow) {
          caller.SccLow = entry.SccIndex;
        }

        caller.SawPending = true;
      }

      return entry.ProvisionalReturn ?? new PendingReturnType(entry.Symbol);
    }

    // Unchecked unannotated callable: start discovery.
    entry.SetReturnState(ReturnInferenceState.InProgress);
    entry.SccIndex = _sccStack.Count;
    entry.SccLow = entry.SccIndex;
    entry.OnSccStack = true;
    entry.SawPending = false;
    entry.ProvisionalReturn = null;
    entry.HasValueReturn = false;
    entry.HasPendingReturn = false;
    entry.HasErrorReturn = false;
    entry.HasOnlyNeverReturns = false;
    entry.HasConflictingReturns = false;
    entry.ReportedCycleError = false;
    _sccStack.Add(entry);

    bool isNestedInSpeculativeCaller = entry.IsNestedFunction && _sccStack.Count > 1;
    var speculativeDiags = new DiagnosticBag();
    var speculativeAssigned = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
    DiagnosticBag prevDiags = _state.SwapDiagnostics(speculativeDiags);
    HashSet<Symbol> prevAssigned = _state.SwapAssignedSymbols(speculativeAssigned);

    var round1Collector = new ReturnCollector(
        entry.KindName,
        entry.CapitalizedKindName,
        entry.Symbol.Name,
        expectedReturn: null,
        isLambda: false);

    try {
      CheckUnannotatedBodyOnce(entry, round1Collector);
    } finally {
      _state.SwapDiagnostics(prevDiags);
      _state.SwapAssignedSymbols(prevAssigned);
    }

    // Case 1: Fast path — single pass, no PendingReturnType encountered, closed SCC of size 1.
    if (!entry.SawPending && entry.SccLow == entry.SccIndex && _inPhase3a == 0) {
      _sccStack.RemoveAt(_sccStack.Count - 1);
      entry.OnSccStack = false;

      if (isNestedInSpeculativeCaller) {
        prevDiags.AddRange(speculativeDiags);
        foreach (Symbol s in speculativeAssigned) {
          prevAssigned.Add(s);
        }
      } else {
        _rootDiagnostics.AddRange(speculativeDiags);
        foreach (Symbol s in speculativeAssigned) {
          _rootAssignedSymbols.Add(s);
        }
      }

      // Finalize returns onto the appropriate diagnostic bag.
      DiagnosticBag targetBag = isNestedInSpeculativeCaller ? prevDiags : _rootDiagnostics;
      DiagnosticBag saved = _state.SwapDiagnostics(targetBag);
      try {
        TypeSymbol finalType = FinalizeUnannotatedReturns(entry, round1Collector, emitDiagnostics: true);
        entry.SetReturnType(finalType, ReturnInferenceState.Resolved);
      } finally {
        _state.SwapDiagnostics(saved);
      }

      entry.BodyFinalized = true;
      return entry.GetReturnType();
    }

    // Update provisional return from Round 1.
    UpdateProvisionalFromCollector(entry, round1Collector);

    // Case 2: Non-root member of a larger SCC — stay on stack and propagate lowlink to caller.
    if (entry.SccLow < entry.SccIndex) {
      // Discard speculative diagnostics; Phase 3b will re-check cleanly.
      ResetNestedFinalizedState(entry.Body);

      // Propagate SCC lowlink and SawPending to our caller on the stack.
      if (entry.SccIndex > 0) {
        CallableEntry caller = _sccStack[entry.SccIndex - 1];
        if (entry.SccLow < caller.SccLow) {
          caller.SccLow = entry.SccLow;
        }

        caller.SawPending = true;
      }

      return entry.ProvisionalReturn ?? new PendingReturnType(entry.Symbol);
    }

    // Case 3: Root of a closed recursive / mutually-recursive SCC — run Phase 3a then Phase 3b!
    int sccStart = entry.SccIndex;
    var scc = new List<CallableEntry>(_sccStack.Count - sccStart);
    for (int i = sccStart; i < _sccStack.Count; i++) {
      CallableEntry member = _sccStack[i];
      member.OnSccStack = false;
      member.IsInSolvingScc = true;
      scc.Add(member);
    }
    _sccStack.RemoveRange(sccStart, _sccStack.Count - sccStart);

    // Discard Round-1 speculative state on all SCC members.
    foreach (CallableEntry member in scc) {
      ResetNestedFinalizedState(member.Body);
    }

    // Phase 3a: Fixed-point iteration (up to |SCC| + 1 rounds total; Round 1 already ran).
    _inPhase3a++;
    _state.SuppressInstantiationRecording = true;
    try {
      int maxAdditionalRounds = scc.Count;
      for (int round = 0; round < maxAdditionalRounds; round++) {
        bool changed = false;
        foreach (CallableEntry member in scc) {
          _cancellationToken.ThrowIfCancellationRequested();
          ResetCallableBodyState(member.Body);

          var throwawayDiags = new DiagnosticBag();
          var throwawayAssigned = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
          DiagnosticBag savedDiags = _state.SwapDiagnostics(throwawayDiags);
          HashSet<Symbol> savedAssigned = _state.SwapAssignedSymbols(throwawayAssigned);
          var collector = new ReturnCollector(
              member.KindName,
              member.CapitalizedKindName,
              member.Symbol.Name,
              expectedReturn: null,
              isLambda: false);
          try {
            CheckUnannotatedBodyOnce(member, collector);
          } finally {
            _state.SwapDiagnostics(savedDiags);
            _state.SwapAssignedSymbols(savedAssigned);
          }

          if (UpdateProvisionalFromCollector(member, collector)) {
            changed = true;
          }
        }

        if (!changed) {
          break;
        }
      }
    } finally {
      _inPhase3a--;
      _state.SuppressInstantiationRecording = _inPhase3a > 0;
    }

    // Commit resolved concrete return types for every member in the SCC.
    DiagnosticBag commitDiagBag = isNestedInSpeculativeCaller ? prevDiags : _rootDiagnostics;
    foreach (CallableEntry member in scc) {
      member.IsInSolvingScc = false;
      TypeSymbol committedType;
      if (member.HasConflictingReturns) {
        // Phase 3b will report PN3105 with exact RelatedLocation.
        committedType = _types.Error;
      } else if (member.ProvisionalReturn is { } concrete) {
        committedType = concrete;
      } else if (!member.HasValueReturn) {
        committedType = _types.Void;
      } else if (member.HasOnlyNeverReturns && !member.HasPendingReturn) {
        committedType = _types.Never;
      } else if (member.HasErrorReturn && !member.HasPendingReturn) {
        committedType = _types.Error;
      } else {
        // FN-E4: unresolved recursion cycle with value return(s) and no base case.
        commitDiagBag.ReportError(
            DiagnosticCodes.CannotInferReturnType,
            member.Symbol.NameSpan,
            $"Cannot infer return type of recursive {member.KindName} '{member.Symbol.Name}'; add an explicit return type.");
        member.ReportedCycleError = true;
        committedType = _types.Error;
      }

      member.SetReturnType(committedType, ReturnInferenceState.Resolved);
    }

    // Phase 3b: Final body check with diagnostics enabled and concrete return types committed.
    if (_inPhase3a == 0) {
      scc.Sort(static (a, b) => a.DeclarationSyntax.Span.Start.CompareTo(b.DeclarationSyntax.Span.Start));
      DiagnosticBag targetDiags = isNestedInSpeculativeCaller ? prevDiags : _rootDiagnostics;
      HashSet<Symbol> targetAssigned = isNestedInSpeculativeCaller ? prevAssigned : _rootAssignedSymbols;
      DiagnosticBag savedDiags = _state.SwapDiagnostics(targetDiags);
      HashSet<Symbol> savedAssigned = _state.SwapAssignedSymbols(targetAssigned);
      try {
        foreach (CallableEntry member in scc) {
          ResetCallableBodyState(member.Body);
          var finalCollector = new ReturnCollector(
              member.KindName,
              member.CapitalizedKindName,
              member.Symbol.Name,
              expectedReturn: null,
              isLambda: false);
          CheckUnannotatedBodyOnce(member, finalCollector);
          if (!member.ReportedCycleError) {
            TypeSymbol verified = FinalizeUnannotatedReturns(member, finalCollector, emitDiagnostics: true);
            if (!ReferenceEquals(verified, member.GetReturnType())) {
              member.SetReturnType(verified, ReturnInferenceState.Resolved);
            }
          }

          member.BodyFinalized = true;
        }
      } finally {
        _state.SwapDiagnostics(savedDiags);
        _state.SwapAssignedSymbols(savedAssigned);
      }
    }

    return entry.GetReturnType();
  }

  private void CheckExplicitCallableBody(CallableEntry entry) {
    int tolerant = entry.HasMissingHeader ? 1 : 0;
    _tolerantNames += tolerant;
    Symbol? prevCallable = _currentCallable;
    StructSymbol? prevStruct = _currentStruct;
    bool prevStatic = _isStaticMethod;
    ReturnCollector? prevCollector = _activeReturnCollector;
    YieldContext? prevYield = _activeYieldContext;
    int prevLoopDepth = _loopDepth;
    int prevFactBase = _inequalityFactBase;
    _currentCallable = entry.Symbol;
    _currentStruct = entry.EnclosingStruct;
    _isStaticMethod = entry.IsStaticMethod;
    _activeReturnCollector = null;
    _activeYieldContext = null;
    _loopDepth = 0;
    _inequalityFactBase = _inequalityFacts.Count;

    CaptureFrame? captureFrame = entry.IsNestedFunction
        ? PushCaptureFrame(entry.DeclarationSyntax, entry.CallableScope)
        : null;
    try {
      using ScopeRestore _ = EnterScope(entry.CallableScope);
      CheckBlock(entry.Body);
    } finally {
      if (captureFrame is not null) {
        PopCaptureFrame(captureFrame);
      }

      _inequalityFactBase = prevFactBase;
      _loopDepth = prevLoopDepth;
      _activeYieldContext = prevYield;
      _activeReturnCollector = prevCollector;
      _isStaticMethod = prevStatic;
      _currentStruct = prevStruct;
      _currentCallable = prevCallable;
      _tolerantNames -= tolerant;
    }
  }

  private void CheckUnannotatedBodyOnce(CallableEntry entry, ReturnCollector collector) {
    int tolerant = entry.HasMissingHeader ? 1 : 0;
    _tolerantNames += tolerant;
    Symbol? prevCallable = _currentCallable;
    StructSymbol? prevStruct = _currentStruct;
    bool prevStatic = _isStaticMethod;
    ReturnCollector? prevCollector = _activeReturnCollector;
    YieldContext? prevYield = _activeYieldContext;
    int prevLoopDepth = _loopDepth;
    int prevFactBase = _inequalityFactBase;
    _currentCallable = entry.Symbol;
    _currentStruct = entry.EnclosingStruct;
    _isStaticMethod = entry.IsStaticMethod;
    _activeReturnCollector = collector;
    _activeYieldContext = null;
    _loopDepth = 0;
    _inequalityFactBase = _inequalityFacts.Count;

    CaptureFrame? captureFrame = entry.IsNestedFunction && _inPhase3a == 0
        ? PushCaptureFrame(entry.DeclarationSyntax, entry.CallableScope)
        : null;
    try {
      using ScopeRestore _ = EnterScope(entry.CallableScope);
      CheckBlock(entry.Body);
    } finally {
      if (captureFrame is not null) {
        PopCaptureFrame(captureFrame);
      }

      _inequalityFactBase = prevFactBase;
      _loopDepth = prevLoopDepth;
      _activeYieldContext = prevYield;
      _activeReturnCollector = prevCollector;
      _isStaticMethod = prevStatic;
      _currentStruct = prevStruct;
      _currentCallable = prevCallable;
      _tolerantNames -= tolerant;
    }
  }

  private bool UpdateProvisionalFromCollector(CallableEntry entry, ReturnCollector collector) {
    TypeSymbol? prevProvisional = entry.ProvisionalReturn;
    bool prevConflict = entry.HasConflictingReturns;

    entry.HasValueReturn = collector.ValueReturns.Count > 0;
    entry.HasPendingReturn = false;
    entry.HasErrorReturn = false;
    entry.HasConflictingReturns = false;

    TypeSymbol? joined = null;
    bool sawNever = false;

    foreach ((_, _, TypeSymbol type) in collector.ValueReturns) {
      if (type is PendingReturnType) {
        entry.HasPendingReturn = true;
        continue;
      }

      if (type.IsNever) {
        sawNever = true;
        continue;
      }

      if (type.IsError) {
        entry.HasErrorReturn = true;
        continue;
      }

      if (joined is null) {
        joined = type;
      } else if (ReferenceEquals(joined, type)) {
        // identical
      } else if ((ReferenceEquals(joined, _types.Int) && ReferenceEquals(type, _types.Float))
          || (ReferenceEquals(joined, _types.Float) && ReferenceEquals(type, _types.Int))) {
        joined = _types.Float;
      } else {
        entry.HasConflictingReturns = true;
        joined = _types.Error;
      }
    }

    entry.HasOnlyNeverReturns = sawNever && joined is null && !entry.HasErrorReturn && !entry.HasPendingReturn;
    entry.ProvisionalReturn = joined;

    return !ReferenceEquals(prevProvisional, entry.ProvisionalReturn)
        || prevConflict != entry.HasConflictingReturns;
  }

  private TypeSymbol FinalizeUnannotatedReturns(
      CallableEntry entry,
      ReturnCollector collector,
      bool emitDiagnostics) =>
      FinalizeCollectedReturns(collector, emitDiagnostics);

  private TypeSymbol FinalizeCollectedReturns(ReturnCollector collector, bool emitDiagnostics) {
    if (collector.ValueReturns.Count == 0) {
      return _types.Void;
    }

    TypeSymbol? joined = null;
    ReturnStatementSyntax? firstConcreteReturn = null;
    bool sawError = false;
    bool sawNever = false;
    bool reportedConflict = false;

    foreach ((ReturnStatementSyntax stmt, ExpressionSyntax expr, TypeSymbol type) in collector.ValueReturns) {
      if (type is PendingReturnType) {
        sawError = true;
        continue;
      }

      if (type.IsNever) {
        sawNever = true;
        continue;
      }

      if (type.IsError) {
        sawError = true;
        continue;
      }

      if (joined is null) {
        joined = type;
        firstConcreteReturn = stmt;
      } else if (ReferenceEquals(joined, type)) {
        // identical
      } else if (ReferenceEquals(joined, _types.Int) && ReferenceEquals(type, _types.Float)) {
        joined = _types.Float;
      } else if (ReferenceEquals(joined, _types.Float) && ReferenceEquals(type, _types.Int)) {
        // already float
      } else {
        if (emitDiagnostics && !reportedConflict) {
          reportedConflict = true;
          ImmutableArray<RelatedLocation> related = firstConcreteReturn is not null
              ? [new RelatedLocation(firstConcreteReturn.Span, $"first return here has type '{joined.DisplayName}'", _state.FilePath)]
              : [];
          string? help = (joined is StructType or InterfaceType || type is StructType or InterfaceType)
              ? "declare an explicit interface return type if both structs share one"
              : null;
          string subject = collector.IsLambda
              ? "Lambda"
              : $"{collector.CapitalizedKindName} '{collector.CallableName}'";
          Report(
              DiagnosticCodes.ConflictingReturnTypes,
              stmt.Span,
              $"{subject} has conflicting return types '{joined.DisplayName}' and '{type.DisplayName}'.",
              related,
              help: help);
        }

        sawError = true;
      }
    }

    if (reportedConflict) {
      return _types.Error;
    }

    TypeSymbol result;
    if (joined is not null) {
      result = joined;
    } else if (sawError) {
      return _types.Error;
    } else if (sawNever) {
      return _types.Never;
    } else {
      return _types.Void;
    }

    // D-050 / FN-29: widen every `int` return expression when inferred return is `float`.
    if (ReferenceEquals(result, _types.Float)) {
      foreach ((_, ExpressionSyntax expr, TypeSymbol type) in collector.ValueReturns) {
        if (ReferenceEquals(type, _types.Int)) {
          RecordIntToFloat(expr);
        }
      }
    }

    // D-050 / OQ-13 Option 1 / FN-E7: bare `return` in a function that also returns values.
    if (emitDiagnostics && !result.IsVoid && !result.IsError && collector.BareReturns.Count > 0) {
      string subject = collector.IsLambda
          ? "Lambda"
          : $"{collector.CapitalizedKindName} '{collector.CallableName}'";
      foreach (ReturnStatementSyntax bare in collector.BareReturns) {
        Report(
            DiagnosticCodes.BareReturnInNonVoidFunction,
            bare.Span,
            $"{subject} must return a value of type '{result.DisplayName}'.");
      }
    }

    return result;
  }

  /// <summary>
  /// Clears non-hoisted local bindings and body side-table entries inside <paramref name="body"/>
  /// before re-running a Phase 3a round or Phase 3b final pass.
  /// </summary>
  private void ResetCallableBodyState(BlockStatementSyntax body) {
    var stack = new Stack<SyntaxNode>();
    stack.Push(body);
    while (stack.Count > 0) {
      SyntaxNode node = stack.Pop();

      if (node is FunctionDeclarationSyntax nestedFn) {
        if (_state.DeclaredSymbols[nestedFn.Id] is FunctionSymbol nestedSym
            && _callablesBySymbol.TryGetValue(nestedSym, out CallableEntry? nestedEntry)) {
          nestedEntry.BodyFinalized = false;
          if (!nestedEntry.HasExplicitReturn && !nestedEntry.IsInSolvingScc && !nestedEntry.OnSccStack) {
            nestedEntry.SetReturnType(_types.Void, ReturnInferenceState.Unchecked);
          }
          ResetCallableBodyState(nestedEntry.Body);
        }
        continue;
      }

      if (node is StructDeclarationSyntax or InterfaceDeclarationSyntax or EnumDeclarationSyntax or UnionDeclarationSyntax) {
        continue;
      }

      if ((uint) node.Id < (uint) _state.ScopesByBlockId.Length
          && _state.ScopesByBlockId[node.Id] is { } scope) {
        scope.ResetNonHoisted();
      }

      if ((uint) node.Id < (uint) _state.NodeTypes.Length) {
        _state.NodeTypes[node.Id] = null;
        _state.ReferencedSymbols[node.Id] = null;
        _state.Conversions[node.Id] = null;
        _state.CallResolutions[node.Id] = null;
        _state.CapturesByCallableId[node.Id] = null;
        _state.LoopInfos[node.Id] = null;
        _state.ShorthandValueSymbols[node.Id] = null;
        _state.ConstantValues[node.Id] = null;
        _state.PatternBindings[node.Id] = null;
        _state.MutationPlaces[node.Id] = null;
        _state.RuntimeDisjointnessChecks[node.Id] = default;

        if (node is VariableDeclarationSyntax or DestructureFieldSyntax
            || (node is ParameterSyntax && _state.ParentsByNodeId[node.Id] is LambdaExpressionSyntax)) {
          _state.DeclaredSymbols[node.Id] = null;
        }
      }

      foreach (SyntaxElement child in node.GetChildren()) {
        if (child.IsNode) {
          stack.Push(child.AsNode());
        }
      }
    }
  }

  private void ResetNestedFinalizedState(BlockStatementSyntax body) {
    var stack = new Stack<SyntaxNode>();
    stack.Push(body);
    while (stack.Count > 0) {
      SyntaxNode node = stack.Pop();
      if (node is FunctionDeclarationSyntax nestedFn) {
        if (_state.DeclaredSymbols[nestedFn.Id] is FunctionSymbol nestedSym
            && _callablesBySymbol.TryGetValue(nestedSym, out CallableEntry? nestedEntry)) {
          nestedEntry.BodyFinalized = false;
          if (!nestedEntry.HasExplicitReturn && !nestedEntry.IsInSolvingScc && !nestedEntry.OnSccStack) {
            nestedEntry.SetReturnType(_types.Void, ReturnInferenceState.Unchecked);
          }
          ResetNestedFinalizedState(nestedEntry.Body);
        }
        continue;
      }

      if (node is StructDeclarationSyntax or InterfaceDeclarationSyntax or EnumDeclarationSyntax or UnionDeclarationSyntax) {
        continue;
      }

      foreach (SyntaxElement child in node.GetChildren()) {
        if (child.IsNode) {
          stack.Push(child.AsNode());
        }
      }
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Return statements (explicit, inferred D-050, and block lambdas D-053)
  // ---------------------------------------------------------------------------------------------

  private void CheckReturn(ReturnStatementSyntax returnStatement) {
    if (_activeReturnCollector is { } collector) {
      if (returnStatement.Expression is null) {
        collector.BareReturns.Add(returnStatement);
        return;
      }

      ExpressionSyntax valueExpr = returnStatement.Expression;
      TypeSymbol exprType;
      if (valueExpr is TupleLiteralExpressionSyntax tupleLit) {
        exprType = SynthesizeTupleReturnForInference(tupleLit);
      } else if (collector.ExpectedReturn is { } expectedRet && !expectedRet.IsVoid && !expectedRet.IsError) {
        exprType = CheckWithContextualExpectation(valueExpr, expectedRet);
        if (TryConvert(valueExpr, exprType, expectedRet)) {
          exprType = exprType.IsError ? _types.Error : expectedRet;
        }
      } else {
        exprType = Synthesize(valueExpr);
      }

      collector.ValueReturns.Add((returnStatement, valueExpr, exprType));
      return;
    }

    if (_currentCallable is null) {
      if (returnStatement.Expression is { } orphanValue) {
        if (orphanValue is TupleLiteralExpressionSyntax orphanTuple) {
          SynthesizeTupleLiteralOutsideReturn(orphanTuple);
        } else {
          Synthesize(orphanValue);
        }
      }

      Report(
          DiagnosticCodes.ReturnOutsideFunction,
          returnStatement.Span,
          "Return statement is only allowed inside a function or method.");
      return;
    }

    (TypeSymbol declaredReturnType, string callableName) = _currentCallable switch {
      FunctionSymbol fn => (fn.ReturnType, fn.Name),
      MethodSymbol method => (method.ReturnType, method.Name),
      _ => (_types.Error, _currentCallable.Name),
    };

    if (returnStatement.Expression is null) {
      if (!declaredReturnType.IsVoid && !declaredReturnType.IsError) {
        Report(
            DiagnosticCodes.BareReturnInNonVoidFunction,
            returnStatement.Span,
            $"Function '{callableName}' must return a value of type '{declaredReturnType.DisplayName}'.");
      }
      return;
    }

    ExpressionSyntax returnValue = returnStatement.Expression;
    if (returnValue is TupleLiteralExpressionSyntax tupleLiteral) {
      CheckTupleReturn(tupleLiteral, declaredReturnType, callableName);
      return;
    }

    if (declaredReturnType.IsVoid) {
      TypeSymbol actualVoid = Synthesize(returnValue);
      if (!IsSilent(actualVoid) && actualVoid is not PendingReturnType) {
        Report(
            DiagnosticCodes.ReturnTypeMismatch,
            returnValue.Span,
            $"Cannot return a value of type '{actualVoid.DisplayName}' from '{callableName}' with declared return type 'void'.");
      }
      return;
    }

    if (declaredReturnType.IsError) {
      Synthesize(returnValue);
      return;
    }

    TypeSymbol actual = CheckWithContextualExpectation(returnValue, declaredReturnType);
    if (!TryConvert(returnValue, actual, declaredReturnType)) {
      if (declaredReturnType is InterfaceType expectedIface) {
        string detail = FormatInterfaceMismatchDetail(actual, expectedIface);
        Report(
            DiagnosticCodes.InterfaceNotSatisfied,
            returnValue.Span,
            $"'{callableName}' should return '{declaredReturnType.DisplayName}', but this returns '{actual.DisplayName}'{detail}.");
      } else {
        Report(
            DiagnosticCodes.ReturnTypeMismatch,
            returnValue.Span,
            $"'{callableName}' should return '{declaredReturnType.DisplayName}', but this returns '{actual.DisplayName}'.");
      }
    }
  }
}
