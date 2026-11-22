using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  private readonly List<CaptureFrame> _captureStack = [];

  private sealed class CaptureFrame {
    private readonly List<Symbol> _orderedSymbols = [];
    private readonly Dictionary<Symbol, (int Index, bool IsMutable, bool IsMutated)> _entries =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Symbol> _reportedMutCaptures =
        new(ReferenceEqualityComparer.Instance);

    public CaptureFrame(SyntaxNode callableNode, Scope boundaryScope) {
      CallableNode = callableNode;
      BoundaryScope = boundaryScope;
    }

    public SyntaxNode CallableNode { get; }
    public Scope BoundaryScope { get; }

    public bool TryMarkMutCaptureReported(Symbol symbol) => _reportedMutCaptures.Add(symbol);

    public void Record(Symbol symbol, bool isMutation) {
      if (_entries.TryGetValue(symbol, out (int Index, bool IsMutable, bool IsMutated) existing)) {
        if (isMutation && !existing.IsMutated) {
          _entries[symbol] = (existing.Index, existing.IsMutable, true);
        }
        return;
      }

      // `self` inside a `mut fn` is never a D-056 heap-boxed `var` (only read snapshots are valid).
      bool isMutable = symbol is LocalSymbol { IsMutable: true } local && !IsSelfLocal(local);
      int index = _orderedSymbols.Count;
      _orderedSymbols.Add(symbol);
      _entries.Add(symbol, (index, isMutable, isMutation));
    }

    public CaptureList ToCaptureList() {
      if (_orderedSymbols.Count == 0) {
        return CaptureList.Empty;
      }

      var builder = ImmutableArray.CreateBuilder<CaptureEntry>(_orderedSymbols.Count);
      foreach (Symbol sym in _orderedSymbols) {
        (_, bool isMutable, bool isMutated) = _entries[sym];
        builder.Add(new CaptureEntry(sym, isMutable, isMutated));
      }

      return new CaptureList(builder.MoveToImmutable());
    }
  }

  // ---------------------------------------------------------------------------------------------
  // AD-002 Closure Capture Tracking (D-056, ticket 032 §6)
  // ---------------------------------------------------------------------------------------------

  private CaptureFrame PushCaptureFrame(SyntaxNode callableNode, Scope boundaryScope) {
    var frame = new CaptureFrame(callableNode, boundaryScope);
    _captureStack.Add(frame);
    return frame;
  }

  private void PopCaptureFrame(CaptureFrame frame) {
    if (_captureStack.Count > 0 && ReferenceEquals(_captureStack[^1], frame)) {
      _captureStack.RemoveAt(_captureStack.Count - 1);
    } else {
      _captureStack.Remove(frame);
    }

    if (_inPhase3a == 0 && (uint) frame.CallableNode.Id < (uint) _state.CapturesByCallableId.Length) {
      _state.CapturesByCallableId[frame.CallableNode.Id] = frame.ToCaptureList();
    }
  }

  private bool TryLookupAndRecordCapture(
      string name,
      [NotNullWhen(true)] out Symbol? symbol,
      bool isMutation = false,
      SourceSpan reportSpan = default) {
    if (!_scope.TryLookupWithScope(name, out symbol, out Scope? declaringScope)) {
      return false;
    }

    RecordCaptureIfOuter(symbol, declaringScope, isMutation, reportSpan);
    return true;
  }

  private void RecordCaptureIfOuter(
      Symbol symbol,
      Scope declaringScope,
      bool isMutation,
      SourceSpan reportSpan = default) {
    if (_captureStack.Count == 0) {
      return;
    }

    Symbol? capturedSymbol = null;
    Scope effectiveDeclScope = declaringScope;
    if (symbol is LocalSymbol or ParameterSymbol) {
      capturedSymbol = symbol;
    } else if ((symbol is FieldSymbol || symbol is MethodSymbol { IsStatic: false })
        && declaringScope.Kind == ScopeKind.StructMember) {
      for (Scope? s = _scope; s is not null && !ReferenceEquals(s, declaringScope); s = s.Parent) {
        if (s.Kind == ScopeKind.Method && s.TryLookupLocal("self", out Symbol? methodSelf)) {
          capturedSymbol = methodSelf;
          effectiveDeclScope = s;
          break;
        }
      }

      if (capturedSymbol is null && declaringScope.TryLookupLocal("self", out Symbol? selfSym)) {
        capturedSymbol = selfSym;
      }
    }

    if (capturedSymbol is null) {
      return;
    }

    // Interior mutation on a `var` or bare field / bare `mut fn` call on implicit `self`
    // propagates `isMutation: true` to the captured root (D-056, D-180).
    bool effectiveMutation = isMutation;
    bool isForbiddenMutParam = capturedSymbol is ParameterSymbol { IsMutable: true };
    bool isForbiddenMutSelf = effectiveMutation
        && capturedSymbol is LocalSymbol selfLocal
        && IsSelfLocal(selfLocal)
        && selfLocal.IsMutable;
    bool shouldReportMutCapture = false;

    // Walk lexically from _scope up to effectiveDeclScope; every active CaptureFrame whose
    // BoundaryScope is crossed strictly before reaching effectiveDeclScope captures this symbol.
    for (Scope? current = _scope; current is not null && !ReferenceEquals(current, effectiveDeclScope); current = current.Parent) {
      for (int i = _captureStack.Count - 1; i >= 0; i--) {
        CaptureFrame frame = _captureStack[i];
        if (ReferenceEquals(frame.BoundaryScope, current)) {
          frame.Record(capturedSymbol, effectiveMutation);
          if ((isForbiddenMutParam || isForbiddenMutSelf) && frame.TryMarkMutCaptureReported(capturedSymbol)) {
            shouldReportMutCapture = true;
          }
        }
      }
    }

    if (shouldReportMutCapture) {
      SourceSpan span = reportSpan.Length > 0 ? reportSpan : capturedSymbol.NameSpan;
      if (isForbiddenMutParam) {
        ReportCannotCaptureMutParameter(span, (ParameterSymbol) capturedSymbol);
      } else if (isForbiddenMutSelf) {
        ReportCannotMutablyCaptureSelf(span, (LocalSymbol) capturedSymbol);
      }
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Contextual Lambda Inference & D-053 Return Inference (D-167, FN-30, FN-E11, OQ-14, FN-8, FN-9)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol SynthesizeLambda(
      LambdaExpressionSyntax lambda,
      FunctionType? expectedFunction = null,
      ImmutableArray<FunctionParameter>? expectedParamsOverride = null,
      TypeSymbol? expectedReturnOverride = null) {
    Scope lambdaScope = ScopeOf(lambda, ScopeKind.Lambda);
    ImmutableArray<FunctionParameter> expectedParams = expectedParamsOverride
        ?? expectedFunction?.Parameters
        ?? default;
    bool hasExpectedParams = !expectedParams.IsDefault;
    TypeSymbol? expectedReturn = expectedReturnOverride ?? expectedFunction?.ReturnType;

    var paramTypes = ImmutableArray.CreateBuilder<FunctionParameter>(lambda.Parameters.Length);

    for (int i = 0; i < lambda.Parameters.Length; i++) {
      ParameterSyntax paramSyntax = lambda.Parameters[i];
      if (IsMissing(paramSyntax.NameToken)) {
        paramTypes.Add(new FunctionParameter(_types.Error, paramSyntax.MutKeyword is not null));
        continue;
      }

      string paramName = NameOf(paramSyntax.NameToken);
      bool hasDefault = paramSyntax.DefaultValue is not null || paramSyntax.EqualsToken is not null;
      if (hasDefault) {
        Report(
            DiagnosticCodes.DefaultParameterNotSupported,
            paramSyntax.Span,
            $"Parameter '{paramName}' cannot have a default value; default parameter values are not supported.");
      }

      bool hasMutKeyword = paramSyntax.MutKeyword is not null;
      bool isMutable;
      TypeSymbol paramType;
      if (paramSyntax.Type is not null) {
        paramType = _session.ResolveType(
            _state,
            paramSyntax.Type,
            lambdaScope,
            TypePosition.ParameterType);
        isMutable = hasMutKeyword;
      } else if (hasExpectedParams && i < expectedParams.Length) {
        paramType = expectedParams[i].Type;
        isMutable = hasMutKeyword || expectedParams[i].IsMutable;
      } else {
        // OQ-14 Option 1 / FN-E11 / D-167: untyped lambda parameter with no typed context.
        Report(
            DiagnosticCodes.CannotInferLambdaParameter,
            paramSyntax.NameToken.Span,
            $"Cannot infer parameter type for '{paramName}'; add a type annotation.");
        paramType = _types.Error;
        isMutable = hasMutKeyword;
      }

      var paramSymbol = new ParameterSymbol(
          paramName,
          paramType,
          i,
          hasDefault,
          paramSyntax.Id,
          paramSyntax.NameToken.Span,
          _state.FilePath,
          lambdaScope.Owner,
          isMutable: isMutable);
      if (!lambdaScope.TryDeclare(paramSymbol, out Symbol? existing)) {
        CheckerSession.ReportDuplicate(_state, paramName, paramSyntax.NameToken.Span, existing!);
      }

      _state.SetDeclaredSymbol(paramSyntax, paramSymbol);
      _state.SetNodeType(paramSyntax, paramType);
      paramTypes.Add(new FunctionParameter(paramType, isMutable));
    }

    TypeSymbol returnType;
    CaptureFrame? captureFrame = _inPhase3a == 0
        ? PushCaptureFrame(lambda, lambdaScope)
        : null;
    int prevLoopDepth = _loopDepth;
    int prevFactBase = _inequalityFactBase;
    _loopDepth = 0;
    _inequalityFactBase = _inequalityFacts.Count;
    try {
      using (EnterScope(lambdaScope)) {
        if (lambda.BodyExpression is { } bodyExpr) {
          ReturnCollector? prevCollector = _activeReturnCollector;
          Symbol? prevCallable = _currentCallable;
          YieldContext? prevYield = _activeYieldContext;
          _activeReturnCollector = null;
          _currentCallable = null;
          _activeYieldContext = null;
          try {
            if (expectedReturn is not null && !expectedReturn.IsVoid && !expectedReturn.IsError) {
              TypeSymbol actualBody = CheckWithContextualExpectation(bodyExpr, expectedReturn);
              if (TryConvert(bodyExpr, actualBody, expectedReturn)) {
                returnType = actualBody.IsError ? _types.Error : expectedReturn;
              } else {
                returnType = actualBody;
              }
            } else {
              returnType = Synthesize(bodyExpr);
            }
          } finally {
            _activeYieldContext = prevYield;
            _currentCallable = prevCallable;
            _activeReturnCollector = prevCollector;
          }
        } else if (lambda.BodyBlock is { } bodyBlock) {
          ReturnCollector? prevCollector = _activeReturnCollector;
          Symbol? prevCallable = _currentCallable;
          YieldContext? prevYield = _activeYieldContext;
          var lambdaCollector = new ReturnCollector(
              "lambda",
              "Lambda",
              "<lambda>",
              expectedReturn,
              isLambda: true);
          _activeReturnCollector = lambdaCollector;
          _currentCallable = null;
          _activeYieldContext = null;
          try {
            CheckBlock(bodyBlock);
          } finally {
            _activeYieldContext = prevYield;
            _currentCallable = prevCallable;
            _activeReturnCollector = prevCollector;
          }

          returnType = FinalizeCollectedReturns(lambdaCollector, emitDiagnostics: _inPhase3a == 0);
          if (_inPhase3a == 0) {
            CheckDefiniteReturn(
                null,
                "lambda",
                "Lambda",
                string.Empty,
                lambda.FnKeyword.Span,
                bodyBlock,
                returnType,
                hasMissingHeader: false);
          }
        } else {
          returnType = _types.Void;
        }
      }
    } finally {
      _inequalityFactBase = prevFactBase;
      _loopDepth = prevLoopDepth;
      if (captureFrame is not null) {
        PopCaptureFrame(captureFrame);
      }
    }

    return _types.GetFunction(paramTypes.MoveToImmutable(), returnType);
  }

  // ---------------------------------------------------------------------------------------------
  // Implicit `it` Call-Argument Binding (FN-16, FN-17, VECLIB-24, D-002 OQ-17)
  // ---------------------------------------------------------------------------------------------

  private bool ShouldWrapImplicitIt(ExpressionSyntax argumentExpression) {
    if (UnwrapParens(argumentExpression) is LambdaExpressionSyntax) {
      return false;
    }

    // FN-17 & D-002 OQ-17: if `it` is already declared in lexical scope (e.g., parameter or `for N`),
    // do not turn the argument into an implicit lambda.
    if (_scope.TryLookup("it", out _)) {
      return false;
    }

    return ContainsFreeIt(argumentExpression);
  }

  private TypeSymbol CheckImplicitItArgument(
      ExpressionSyntax argExpression,
      FunctionType expectedFunction,
      int argIndex,
      string calleeKind,
      string calleeName) {
    if (expectedFunction.Parameters.Length != 1) {
      Report(
          DiagnosticCodes.InvalidImplicitIt,
          argExpression.Span,
          $"Implicit 'it' requires a 1-parameter function type, but {calleeKind} '{calleeName}' argument {argIndex + 1} expects '{expectedFunction.DisplayName}'.");
      return _types.Error;
    }

    TypeSymbol actualType = CheckImplicitItCore(
        argExpression,
        expectedFunction.Parameters[0].Type,
        expectedFunction.ReturnType,
        expectedFunction,
        argIndex,
        calleeKind,
        calleeName);
    if (expectedFunction.Parameters[0].IsMutable && actualType is FunctionType actualFn) {
      Report(
          DiagnosticCodes.TypeMismatch,
          argExpression.Span,
          $"Argument {argIndex + 1} for {calleeKind} '{calleeName}' expected type '{expectedFunction.DisplayName}', but got '{actualFn.DisplayName}'.");
    }

    return actualType;
  }

  private TypeSymbol CheckImplicitItCore(
      ExpressionSyntax argExpression,
      TypeSymbol itParamType,
      TypeSymbol? expectedReturn,
      FunctionType? expectedFunctionForMismatch,
      int argIndex,
      string calleeKind,
      string calleeName) {
    var itScope = new Scope(ScopeKind.Synthetic, _scope, _scope.Owner);
    var itSymbol = new LocalSymbol(
        "it",
        itParamType,
        isMutable: false,
        LocalKind.ImplicitIt,
        argExpression.Id,
        argExpression.Span,
        _state.FilePath,
        _scope.Owner);
    itScope.TryDeclare(itSymbol, out _);

    TypeSymbol bodyType;
    TypeSymbol resolvedReturn;
    CaptureFrame? captureFrame = _inPhase3a == 0
        ? PushCaptureFrame(argExpression, itScope)
        : null;
    YieldContext? prevYield = _activeYieldContext;
    int prevLoopDepth = _loopDepth;
    int prevFactBase = _inequalityFactBase;
    _activeYieldContext = null;
    _loopDepth = 0;
    _inequalityFactBase = _inequalityFacts.Count;
    try {
      using (EnterScope(itScope)) {
        if (expectedReturn is not null && !expectedReturn.IsVoid && !expectedReturn.IsError) {
          bodyType = CheckWithContextualExpectation(argExpression, expectedReturn);
          if (TryConvert(argExpression, bodyType, expectedReturn)) {
            resolvedReturn = bodyType.IsError ? _types.Error : expectedReturn;
          } else {
            resolvedReturn = bodyType;
            FunctionParameter itParam = new(itParamType, IsMutable: false);
            FunctionType actualFn = _types.GetFunction([itParam], bodyType);
            FunctionType expectedFn = expectedFunctionForMismatch ?? _types.GetFunction([itParam], expectedReturn);
            string code = expectedReturn is InterfaceType
                ? DiagnosticCodes.InterfaceNotSatisfied
                : DiagnosticCodes.TypeMismatch;
            Report(
                code,
                argExpression.Span,
                $"Argument {argIndex + 1} for {calleeKind} '{calleeName}' expected type '{expectedFn.DisplayName}', but got '{actualFn.DisplayName}'.");
          }
        } else if (expectedReturn is { IsVoid: true }) {
          bodyType = Synthesize(argExpression);
          resolvedReturn = bodyType;
          if (!IsSilent(bodyType) && !bodyType.IsVoid && bodyType is not PendingReturnType) {
            FunctionParameter itParam = new(itParamType, IsMutable: false);
            FunctionType actualFn = _types.GetFunction([itParam], bodyType);
            FunctionType expectedFn = expectedFunctionForMismatch ?? _types.GetFunction([itParam], _types.Void);
            Report(
                DiagnosticCodes.TypeMismatch,
                argExpression.Span,
                $"Argument {argIndex + 1} for {calleeKind} '{calleeName}' expected type '{expectedFn.DisplayName}', but got '{actualFn.DisplayName}'.");
          }
        } else {
          bodyType = Synthesize(argExpression);
          resolvedReturn = bodyType;
        }
      }
    } finally {
      _inequalityFactBase = prevFactBase;
      _loopDepth = prevLoopDepth;
      _activeYieldContext = prevYield;
      if (captureFrame is not null) {
        PopCaptureFrame(captureFrame);
      }
    }

    FunctionType implicitLambdaType = _types.GetFunction([new FunctionParameter(itParamType, IsMutable: false)], resolvedReturn);
    _state.Conversions[argExpression.Id] = new ConversionInfo(
        ConversionKind.ImplicitItLambda,
        implicitLambdaType,
        ImplicitItSymbol: itSymbol,
        ImplicitLambdaType: implicitLambdaType);
    return implicitLambdaType;
  }

  private bool ContainsFreeIt(SyntaxNode root) {
    switch (root) {
      case IdentifierExpressionSyntax id:
        return !IsMissing(id.IdentifierToken) && string.Equals(NameOf(id.IdentifierToken), "it", StringComparison.Ordinal);

      // Stop at nested lambdas and nested function/type declarations.
      case LambdaExpressionSyntax:
      case FunctionDeclarationSyntax:
      case StructDeclarationSyntax:
      case InterfaceDeclarationSyntax:
      case EnumDeclarationSyntax:
      case UnionDeclarationSyntax:
        return false;

      case MemberAccessExpressionSyntax member:
        return ContainsFreeIt(member.Target);

      case StaticAccessExpressionSyntax staticAccess:
        return ContainsFreeIt(staticAccess.Target);

      case VectorInitializerExpressionSyntax vecInit:
        foreach (VectorPropertySyntax prop in vecInit.Properties) {
          // `init:` introduces its own `it: int` binding (VEC-8).
          if (!IsMissing(prop.NameToken) && string.Equals(NameOf(prop.NameToken), "init", StringComparison.Ordinal)) {
            continue;
          }

          if (ContainsFreeIt(prop.Value)) {
            return true;
          }
        }
        return false;

      case StructFieldInitSyntax fieldInit:
        if (fieldInit.Value is { } fieldVal) {
          return ContainsFreeIt(fieldVal);
        }

        return fieldInit.ColonToken is null
            && !IsMissing(fieldInit.NameToken)
            && string.Equals(NameOf(fieldInit.NameToken), "it", StringComparison.Ordinal);

      case TupleFieldSyntax tupleField:
        if (tupleField.Value is { } tupleVal) {
          return ContainsFreeIt(tupleVal);
        }

        return tupleField.ColonToken is null
            && !IsMissing(tupleField.NameToken)
            && string.Equals(NameOf(tupleField.NameToken), "it", StringComparison.Ordinal);

      case CallExpressionSyntax call:
        if (ContainsFreeIt(call.Callee)) {
          return true;
        }

        // D-055: fns[it](9) parses `it` as a single NamedTypeSyntax type argument.
        if (call.TypeArguments is { Arguments.Length: 1 } typeArgs
            && typeArgs.Arguments[0] is NamedTypeSyntax namedArg
            && !IsMissing(namedArg.NameToken)
            && string.Equals(NameOf(namedArg.NameToken), "it", StringComparison.Ordinal)) {
          return true;
        }

        foreach (ArgumentSyntax arg in call.Arguments) {
          if (ContainsFreeIt(arg.Expression)) {
            return true;
          }
        }
        return false;

      case BlockStatementSyntax block:
        return ContainsFreeItInStatements(block.Items);

      case RecoveryBlockSyntax recBlock:
        return ContainsFreeItInStatements(recBlock.Items);

      case ForStatementSyntax forStmt:
        if (forStmt.Header is ForConditionHeaderSyntax condHeader) {
          // Condition itself may reference outer `it`; count loops bind `it` in body.
          return ContainsFreeIt(condHeader.Condition);
        }

        if (forStmt.Header is ForInHeaderSyntax inHeader) {
          if (ContainsFreeIt(inHeader.Collection)) {
            return true;
          }

          bool bindsIt = (!IsMissing(inHeader.FirstBindingToken)
                  && string.Equals(NameOf(inHeader.FirstBindingToken), "it", StringComparison.Ordinal))
              || (inHeader.SecondBindingToken is { } sec && !IsMissing(sec)
                  && string.Equals(NameOf(sec), "it", StringComparison.Ordinal));
          return !bindsIt && ContainsFreeIt(forStmt.Body);
        }

        return ContainsFreeIt(forStmt.Body);

      default:
        foreach (SyntaxElement child in root.GetChildren()) {
          if (child.IsNode && ContainsFreeIt(child.AsNode())) {
            return true;
          }
        }
        return false;
    }
  }

  private bool ContainsFreeItInStatements(ImmutableArray<SyntaxNode> items) {
    foreach (SyntaxNode item in items) {
      if (item is VariableDeclarationSyntax varDecl) {
        if (ContainsFreeIt(varDecl.Initializer)) {
          return true;
        }

        if (!IsMissing(varDecl.NameToken) && string.Equals(NameOf(varDecl.NameToken), "it", StringComparison.Ordinal)) {
          return false;
        }
        continue;
      }

      if (item is TupleDestructuringStatementSyntax destruct) {
        if (ContainsFreeIt(destruct.Initializer)) {
          return true;
        }

        foreach (DestructureFieldSyntax field in destruct.Fields) {
          Token tok = field.VariableToken ?? field.LabelToken;
          if (!IsMissing(tok) && string.Equals(NameOf(tok), "it", StringComparison.Ordinal)) {
            return false;
          }
        }
        continue;
      }

      if (ContainsFreeIt(item)) {
        return true;
      }
    }

    return false;
  }

  private static ExpressionSyntax UnwrapParens(ExpressionSyntax expression) {
    while (expression is ParenthesizedExpressionSyntax paren) {
      expression = paren.Expression;
    }

    return expression;
  }
}
