using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  private readonly List<CaptureFrame> _captureStack = [];

  private sealed class CaptureFrame {
    private readonly List<Symbol> _orderedSymbols = [];
    private readonly Dictionary<Symbol, (int Index, bool IsMutable, bool IsMutated)> _entries =
        new(ReferenceEqualityComparer.Instance);

    public CaptureFrame(SyntaxNode callableNode, Scope boundaryScope) {
      CallableNode = callableNode;
      BoundaryScope = boundaryScope;
    }

    public SyntaxNode CallableNode { get; }
    public Scope BoundaryScope { get; }

    public void Record(Symbol symbol, bool isMutation) {
      if (_entries.TryGetValue(symbol, out (int Index, bool IsMutable, bool IsMutated) existing)) {
        if (isMutation && !existing.IsMutated) {
          _entries[symbol] = (existing.Index, existing.IsMutable, true);
        }
        return;
      }

      bool isMutable = symbol is LocalSymbol { IsMutable: true };
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
      bool isMutation = false) {
    if (!_scope.TryLookupWithScope(name, out symbol, out Scope? declaringScope)) {
      return false;
    }

    RecordCaptureIfOuter(symbol, declaringScope, isMutation);
    return true;
  }

  private void RecordCaptureIfOuter(Symbol symbol, Scope declaringScope, bool isMutation) {
    if (_captureStack.Count == 0) {
      return;
    }

    Symbol? capturedSymbol = symbol switch {
      LocalSymbol or ParameterSymbol => symbol,
      FieldSymbol when declaringScope.Kind == ScopeKind.StructMember
          && declaringScope.TryLookupLocal("self", out Symbol? selfSym) => selfSym,
      MethodSymbol { IsStatic: false } when declaringScope.Kind == ScopeKind.StructMember
          && declaringScope.TryLookupLocal("self", out Symbol? selfSym) => selfSym,
      _ => null,
    };

    if (capturedSymbol is null) {
      return;
    }

    // Field/method access on implicit `self` reads `self`, never mutates the `self` binding itself.
    bool effectiveMutation = isMutation && ReferenceEquals(capturedSymbol, symbol);

    // Walk lexically from _scope up to declaringScope; every active CaptureFrame whose
    // BoundaryScope is crossed strictly before reaching declaringScope captures this symbol.
    for (Scope? current = _scope; current is not null && !ReferenceEquals(current, declaringScope); current = current.Parent) {
      for (int i = _captureStack.Count - 1; i >= 0; i--) {
        CaptureFrame frame = _captureStack[i];
        if (ReferenceEquals(frame.BoundaryScope, current)) {
          frame.Record(capturedSymbol, effectiveMutation);
        }
      }
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Contextual Lambda Inference & D-053 Return Inference (D-167, FN-30, FN-E11, OQ-14, FN-8, FN-9)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol SynthesizeLambda(
      LambdaExpressionSyntax lambda,
      FunctionType? expectedFunction = null,
      ImmutableArray<TypeSymbol>? expectedParamTypesOverride = null,
      TypeSymbol? expectedReturnOverride = null) {
    Scope lambdaScope = ScopeOf(lambda, ScopeKind.Lambda);
    ImmutableArray<TypeSymbol> expectedParams = expectedParamTypesOverride
        ?? expectedFunction?.ParameterTypes
        ?? default;
    bool hasExpectedParams = !expectedParams.IsDefault;
    TypeSymbol? expectedReturn = expectedReturnOverride ?? expectedFunction?.ReturnType;

    var paramTypes = ImmutableArray.CreateBuilder<TypeSymbol>(lambda.Parameters.Length);

    for (int i = 0; i < lambda.Parameters.Length; i++) {
      ParameterSyntax paramSyntax = lambda.Parameters[i];
      if (IsMissing(paramSyntax.NameToken)) {
        paramTypes.Add(_types.Error);
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

      TypeSymbol paramType;
      if (paramSyntax.Type is not null) {
        paramType = _session.ResolveType(
            _state,
            paramSyntax.Type,
            lambdaScope,
            TypePosition.ParameterType);
      } else if (hasExpectedParams && i < expectedParams.Length) {
        paramType = expectedParams[i];
      } else {
        // OQ-14 Option 1 / FN-E11 / D-167: untyped lambda parameter with no typed context.
        Report(
            DiagnosticCodes.CannotInferLambdaParameter,
            paramSyntax.NameToken.Span,
            $"Cannot infer parameter type for '{paramName}'; add a type annotation.");
        paramType = _types.Error;
      }

      var paramSymbol = new ParameterSymbol(
          paramName,
          paramType,
          i,
          hasDefault,
          paramSyntax.Id,
          paramSyntax.NameToken.Span,
          _state.FilePath,
          lambdaScope.Owner);
      if (!lambdaScope.TryDeclare(paramSymbol, out Symbol? existing)) {
        CheckerSession.ReportDuplicate(_state, paramName, paramSyntax.NameToken.Span, existing!);
      }

      _state.SetDeclaredSymbol(paramSyntax, paramSymbol);
      _state.SetNodeType(paramSyntax, paramType);
      paramTypes.Add(paramType);
    }

    TypeSymbol returnType;
    CaptureFrame? captureFrame = _inPhase3a == 0
        ? PushCaptureFrame(lambda, lambdaScope)
        : null;
    try {
      using (EnterScope(lambdaScope)) {
        if (lambda.BodyExpression is { } bodyExpr) {
          ReturnCollector? prevCollector = _activeReturnCollector;
          Symbol? prevCallable = _currentCallable;
          _activeReturnCollector = null;
          _currentCallable = null;
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
            _currentCallable = prevCallable;
            _activeReturnCollector = prevCollector;
          }
        } else if (lambda.BodyBlock is { } bodyBlock) {
          ReturnCollector? prevCollector = _activeReturnCollector;
          Symbol? prevCallable = _currentCallable;
          var lambdaCollector = new ReturnCollector(
              "lambda",
              "Lambda",
              "<lambda>",
              expectedReturn,
              isLambda: true);
          _activeReturnCollector = lambdaCollector;
          _currentCallable = null;
          try {
            CheckBlock(bodyBlock);
          } finally {
            _currentCallable = prevCallable;
            _activeReturnCollector = prevCollector;
          }

          returnType = FinalizeCollectedReturns(lambdaCollector, emitDiagnostics: _inPhase3a == 0);
        } else {
          returnType = _types.Void;
        }
      }
    } finally {
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
    if (expectedFunction.ParameterTypes.Length != 1) {
      Report(
          DiagnosticCodes.InvalidImplicitIt,
          argExpression.Span,
          $"Implicit 'it' requires a 1-parameter function type, but {calleeKind} '{calleeName}' argument {argIndex + 1} expects '{expectedFunction.DisplayName}'.");
      return _types.Error;
    }

    return CheckImplicitItCore(
        argExpression,
        expectedFunction.ParameterTypes[0],
        expectedFunction.ReturnType,
        expectedFunction,
        argIndex,
        calleeKind,
        calleeName);
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
    try {
      using (EnterScope(itScope)) {
        if (expectedReturn is not null && !expectedReturn.IsVoid && !expectedReturn.IsError) {
          bodyType = CheckWithContextualExpectation(argExpression, expectedReturn);
          if (TryConvert(argExpression, bodyType, expectedReturn)) {
            resolvedReturn = bodyType.IsError ? _types.Error : expectedReturn;
          } else {
            resolvedReturn = bodyType;
            FunctionType actualFn = _types.GetFunction([itParamType], bodyType);
            FunctionType expectedFn = expectedFunctionForMismatch ?? _types.GetFunction([itParamType], expectedReturn);
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
            FunctionType actualFn = _types.GetFunction([itParamType], bodyType);
            FunctionType expectedFn = expectedFunctionForMismatch ?? _types.GetFunction([itParamType], _types.Void);
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
      if (captureFrame is not null) {
        PopCaptureFrame(captureFrame);
      }
    }

    FunctionType implicitLambdaType = _types.GetFunction([itParamType], resolvedReturn);
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
