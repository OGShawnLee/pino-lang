using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  private YieldContext? _activeYieldContext;

  private sealed class YieldContext {
    public YieldContext(TypeSymbol? expectedType, bool isMatchArm = false) {
      ExpectedType = expectedType;
      IsMatchArm = isMatchArm;
    }

    public TypeSymbol? ExpectedType { get; }

    public bool IsMatchArm { get; }

    public List<(YieldStatementSyntax Statement, ExpressionSyntax Expression, TypeSymbol Type)> Yields { get; } = [];
  }

  // ---------------------------------------------------------------------------------------------
  // Bubble operator `?` (ERR-7..9, ERR-15..17, ERR-E1..E4, ERR-E7, PN3407..PN3409)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol SynthesizeBubble(BubbleExpressionSyntax bubble) {
    TypeSymbol? enclosingReturn = GetEnclosingDeclaredReturnTypeForBubble();
    TypeSymbol? expectedError = enclosingReturn is UnionType er
        && ReferenceEquals(er.Definition, _prelude.ResultUnion)
        && er.TypeArguments.Length == 2
            ? er.TypeArguments[1]
            : null;

    TypeSymbol operandType = SynthesizeResultOrOptionOperand(
        bubble.Expression,
        expectedSuccess: null,
        expectedError: expectedError,
        out _,
        out _);

    if (operandType is PendingReturnType) {
      return operandType;
    }

    if (IsSilent(operandType)) {
      return _types.Error;
    }

    if (operandType is not UnionType operandUnion) {
      Report(
          DiagnosticCodes.BubbleOnNonResultOrOption,
          bubble.Span,
          $"Cannot use '?' operator on non-union type '{operandType.DisplayName}'.");
      return _types.Error;
    }

    bool isResult = ReferenceEquals(operandUnion.Definition, _prelude.ResultUnion)
        && operandUnion.TypeArguments.Length == 2;
    bool isOption = ReferenceEquals(operandUnion.Definition, _prelude.OptionUnion)
        && operandUnion.TypeArguments.Length == 1;

    if (!isResult && !isOption) {
      Report(
          DiagnosticCodes.BubbleOnNonResultOrOption,
          bubble.Span,
          $"Cannot use '?' operator on non-union type or unsupported union '{operandType.DisplayName}'; expected 'Result' or 'Option'.");
      return _types.Error;
    }

    TypeSymbol successType = operandUnion.TypeArguments[0];
    TypeSymbol? errorType = isResult ? operandUnion.TypeArguments[1] : null;

    if (enclosingReturn is null) {
      Report(
          DiagnosticCodes.BubbleOutsideCompatibleFunction,
          bubble.Span,
          "Cannot use '?' operator in a function without a declared Result or Option return type.");
      return successType;
    }

    if (enclosingReturn.IsError) {
      return successType;
    }

    if (isResult) {
      if (enclosingReturn is not UnionType enclosingResult
          || !ReferenceEquals(enclosingResult.Definition, _prelude.ResultUnion)
          || enclosingResult.TypeArguments.Length != 2) {
        Report(
            DiagnosticCodes.BubbleOutsideCompatibleFunction,
            bubble.Span,
            $"Cannot use '?' bubble operator in a function returning '{enclosingReturn.DisplayName}'. Enclosing function must return a Result.");
        return successType;
      }

      TypeSymbol expectedErr = enclosingResult.TypeArguments[1];
      if (errorType is not null && !IsBubbleErrorCompatible(errorType, expectedErr)) {
        Report(
            DiagnosticCodes.BubbleErrorTypeMismatch,
            bubble.Span,
            $"Cannot bubble error of type '{errorType.DisplayName}' in a function expecting error of type '{expectedErr.DisplayName}'.");
      }

      return successType;
    }

    // isOption
    if (enclosingReturn is not UnionType enclosingOption
        || !ReferenceEquals(enclosingOption.Definition, _prelude.OptionUnion)) {
      string suffix = enclosingReturn is UnionType r && ReferenceEquals(r.Definition, _prelude.ResultUnion)
          ? "Enclosing function must return an Option."
          : "Enclosing function must return a Result or Option matching the bubbled value.";
      Report(
          DiagnosticCodes.BubbleOutsideCompatibleFunction,
          bubble.Span,
          $"Cannot use '?' bubble operator in a function returning '{enclosingReturn.DisplayName}'. {suffix}");
      return successType;
    }

    return successType;
  }

  private TypeSymbol? GetEnclosingDeclaredReturnTypeForBubble() {
    if (_activeReturnCollector is { } collector) {
      // Lambdas with an explicit expected Result/Option return can bubble; unannotated functions cannot (ERR-E1).
      if (collector.IsLambda
          && collector.ExpectedReturn is UnionType expectedUnion
          && (ReferenceEquals(expectedUnion.Definition, _prelude.ResultUnion)
              || ReferenceEquals(expectedUnion.Definition, _prelude.OptionUnion))) {
        return expectedUnion;
      }

      return null;
    }

    return _currentCallable switch {
      FunctionSymbol { ReturnState: ReturnInferenceState.Explicit } fn => fn.ReturnType,
      MethodSymbol { ReturnState: ReturnInferenceState.Explicit } method => method.ReturnType,
      _ => null,
    };
  }

  private bool IsBubbleErrorCompatible(TypeSymbol actualError, TypeSymbol expectedError) {
    if (actualError.IsError || expectedError.IsError || actualError.IsNever || ReferenceEquals(actualError, expectedError)) {
      return true;
    }

    if (expectedError is InterfaceType targetIface
        && actualError is StructType or InterfaceType or TypeParameterType) {
      if (actualError is StructType st) {
        EnsureStructMethodsResolved(st.Definition);
      }

      return TypeCompatibility.SatisfiesInterface(
          actualError,
          targetIface,
          _types,
          _state.Module,
          _session.InterfaceSatisfactionCache);
    }

    return false;
  }

  // ---------------------------------------------------------------------------------------------
  // Recovery blocks `or { ... }` and `yield` (ERR-10..14, ERR-22, ERR-E5, ERR-E6, D-140, D-143)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol CheckRecovery(RecoveryExpressionSyntax recovery, TypeSymbol? expectedType) {
    TypeSymbol? concreteExpected = expectedType is not null && !expectedType.IsError && !Unifier.ContainsTypeVariable(expectedType)
        ? expectedType
        : null;

    TypeSymbol operandType = SynthesizeResultOrOptionOperand(
        recovery.Expression,
        expectedSuccess: concreteExpected,
        expectedError: null,
        out bool successWasUnresolved,
        out bool operandIsOption);

    if (operandType is PendingReturnType) {
      return operandType;
    }

    TypeSymbol? knownSuccessType;
    TypeSymbol errType;

    if (IsSilent(operandType)) {
      knownSuccessType = concreteExpected ?? _types.Error;
      errType = _types.Error;
    } else if (operandType is UnionType operandUnion
        && ReferenceEquals(operandUnion.Definition, _prelude.ResultUnion)
        && operandUnion.TypeArguments.Length == 2) {
      knownSuccessType = successWasUnresolved ? concreteExpected : operandUnion.TypeArguments[0];
      errType = operandUnion.TypeArguments[1];
    } else if (operandType is UnionType optionUnion
        && ReferenceEquals(optionUnion.Definition, _prelude.OptionUnion)
        && optionUnion.TypeArguments.Length == 1) {
      knownSuccessType = successWasUnresolved ? concreteExpected : optionUnion.TypeArguments[0];
      errType = _types.String; // ERR-12: Option::None binds `err` as string ("None").
    } else {
      Report(
          DiagnosticCodes.BubbleOnNonResultOrOption,
          recovery.Expression.Span,
          $"Cannot use 'or' recovery block on non-union type '{operandType.DisplayName}'; expected 'Result' or 'Option'.");
      knownSuccessType = _types.Error;
      errType = _types.Error;
    }

    Scope recoveryScope = ScopeOf(recovery.Block, ScopeKind.Block);
    var errSymbol = new LocalSymbol(
        "err",
        errType,
        isMutable: false,
        LocalKind.RecoveryErr,
        recovery.Block.Id,
        recovery.OrKeyword.Span,
        _state.FilePath,
        recoveryScope.Owner);
    recoveryScope.TryDeclare(errSymbol, out _);

    YieldContext? prevYield = _activeYieldContext;
    var yieldCtx = new YieldContext(knownSuccessType);
    _activeYieldContext = yieldCtx;
    try {
      using ScopeRestore _ = EnterScope(recoveryScope);
      foreach (SyntaxNode item in recovery.Block.Items) {
        CheckItem(item);
      }
    } finally {
      _activeYieldContext = prevYield;
    }

    if (knownSuccessType is not null) {
      return knownSuccessType;
    }

    // Infer T from the `yield` statements inside `or { ... }` when the operand was `Result::Failure(e)`
    // or `Option::None` with no outer contextual type (e.g., conformance/errors/or-failure.pino).
    TypeSymbol inferredSuccess = JoinYieldTypes(yieldCtx);
    if (successWasUnresolved && !inferredSuccess.IsError) {
      BackpatchRecoveryOperand(recovery.Expression, operandIsOption, inferredSuccess, errType);
    }

    return inferredSuccess;
  }

  private void CheckYield(YieldStatementSyntax yieldStatement) {
    if (_activeYieldContext is null) {
      Synthesize(yieldStatement.Expression);
      Report(
          DiagnosticCodes.YieldOutsideValidContext,
          yieldStatement.Span,
          "'yield' is only allowed inside a recovery block or match expression arm.");
      return;
    }

    if (_activeYieldContext.IsMatchArm) {
      TypeSymbol armType = _activeYieldContext.ExpectedType is { } armExpected
          && !armExpected.IsError
          && !armExpected.IsVoid
          ? CheckWithContextualExpectation(yieldStatement.Expression, armExpected)
          : Synthesize(yieldStatement.Expression);
      _activeYieldContext.Yields.Add((yieldStatement, yieldStatement.Expression, armType));
      return;
    }

    if (_activeYieldContext.ExpectedType is { } expected && !expected.IsError) {
      if (expected.IsVoid) {
        TypeSymbol actualVoid = Synthesize(yieldStatement.Expression);
        if (!IsSilent(actualVoid) && actualVoid is not PendingReturnType) {
          Report(
              DiagnosticCodes.YieldTypeMismatch,
              yieldStatement.Expression.Span,
              $"Yield statement expected type 'void', but got '{actualVoid.DisplayName}'.");
        }
        return;
      }

      TypeSymbol actual = CheckWithContextualExpectation(yieldStatement.Expression, expected);
      if (!TryConvert(yieldStatement.Expression, actual, expected)) {
        Report(
            DiagnosticCodes.YieldTypeMismatch,
            yieldStatement.Expression.Span,
            $"Yield statement expected type '{expected.DisplayName}', but got '{actual.DisplayName}'.");
      }

      _activeYieldContext.Yields.Add((yieldStatement, yieldStatement.Expression, actual));
      return;
    }

    TypeSymbol synthesized = Synthesize(yieldStatement.Expression);
    _activeYieldContext.Yields.Add((yieldStatement, yieldStatement.Expression, synthesized));
  }

  private TypeSymbol JoinYieldTypes(YieldContext yieldCtx) {
    if (yieldCtx.Yields.Count == 0) {
      return _types.Never;
    }

    TypeSymbol? joined = null;
    bool sawError = false;
    foreach ((_, ExpressionSyntax expr, TypeSymbol type) in yieldCtx.Yields) {
      if (type is PendingReturnType) {
        return type;
      }

      if (type.IsNever) {
        continue;
      }

      if (type.IsError) {
        sawError = true;
        continue;
      }

      if (joined is null) {
        joined = type;
      } else if (ReferenceEquals(joined, type)) {
        // identical
      } else if (ReferenceEquals(joined, _types.Int) && ReferenceEquals(type, _types.Float)) {
        joined = _types.Float;
      } else if (ReferenceEquals(joined, _types.Float) && ReferenceEquals(type, _types.Int)) {
        // already float
      } else {
        Report(
            DiagnosticCodes.YieldTypeMismatch,
            expr.Span,
            $"Yield statement expected type '{joined.DisplayName}', but got '{type.DisplayName}'.");
        sawError = true;
      }
    }

    if (joined is null) {
      return sawError ? _types.Error : _types.Never;
    }

    if (ReferenceEquals(joined, _types.Float)) {
      foreach ((_, ExpressionSyntax expr, TypeSymbol type) in yieldCtx.Yields) {
        if (ReferenceEquals(type, _types.Int)) {
          RecordIntToFloat(expr);
        }
      }
    }

    return joined;
  }

  /// <summary>
  /// Synthesizes the operand of <c>?</c> or <c>or { ... }</c>, handling partial generic union
  /// constructors (<c>Result::Success(v)</c>, <c>Result::Failure(e)</c>, <c>Option::None</c>)
  /// without emitting spurious <c>PN3110</c> errors when the missing type parameter comes from
  /// the enclosing <c>?</c> / <c>or</c> context.
  /// </summary>
  private TypeSymbol SynthesizeResultOrOptionOperand(
      ExpressionSyntax operand,
      TypeSymbol? expectedSuccess,
      TypeSymbol? expectedError,
      out bool successWasUnresolved,
      out bool operandIsOption) {
    successWasUnresolved = false;
    operandIsOption = false;

    ExpressionSyntax unwrapped = UnwrapParens(operand);

    // Bare `Option::None` as operand of `or { ... }` or `?`.
    if (unwrapped is StaticAccessExpressionSyntax { TargetTypeArguments: null } staticAccess
        && !IsMissing(staticAccess.MemberToken)) {
      Symbol? targetSym = ResolveStaticTargetSymbol(staticAccess.Target, out _);
      if (ReferenceEquals(targetSym, _prelude.OptionUnion)
          && string.Equals(NameOf(staticAccess.MemberToken), "None", StringComparison.Ordinal)) {
        operandIsOption = true;
        _state.SetReferencedSymbol(staticAccess, _prelude.OptionNoneVariant);
        if (expectedSuccess is not null) {
          UnionType opt = _types.GetUnion(_prelude.OptionUnion, [expectedSuccess]);
          SetWrappedExpressionType(operand, opt);
          RecordGenericInstantiation(_prelude.OptionUnion, opt.TypeArguments);
          return opt;
        }

        successWasUnresolved = true;
        UnionType placeholderOpt = _types.GetUnion(_prelude.OptionUnion, [_types.Never]);
        SetWrappedExpressionType(operand, placeholderOpt);
        return placeholderOpt;
      }

      if (ReferenceEquals(targetSym, _prelude.ResultUnion)
          && string.Equals(NameOf(staticAccess.MemberToken), "Success", StringComparison.Ordinal)
          && expectedSuccess is { IsVoid: true }) {
        _state.SetReferencedSymbol(staticAccess, _prelude.ResultSuccessVariant);
        UnionType res = _types.GetUnion(_prelude.ResultUnion, [_types.Void, expectedError ?? _types.Never]);
        SetWrappedExpressionType(operand, res);
        RecordGenericInstantiation(_prelude.ResultUnion, res.TypeArguments);
        return res;
      }
    }

    // Call `Result::Success(v)` or `Result::Failure(e)` as operand of `or { ... }` or `?`.
    if (unwrapped is CallExpressionSyntax call
        && call.Callee is StaticAccessExpressionSyntax { TargetTypeArguments: null } callStatic
        && !IsMissing(callStatic.MemberToken)) {
      Symbol? targetSym = ResolveStaticTargetSymbol(callStatic.Target, out _);
      if (ReferenceEquals(targetSym, _prelude.ResultUnion)) {
        string variantName = NameOf(callStatic.MemberToken);
        bool isSuccess = string.Equals(variantName, "Success", StringComparison.Ordinal);
        bool isFailure = string.Equals(variantName, "Failure", StringComparison.Ordinal);
        if (isSuccess || isFailure) {
          TypeSymbol? hintType = null;
          if (expectedSuccess is not null || expectedError is not null) {
            hintType = _types.GetUnion(
                _prelude.ResultUnion,
                [expectedSuccess ?? _types.Error, expectedError ?? _types.Error]);
          }

          TypeSymbol resType = CheckUnionConstructorCall(
              call,
              callStatic,
              _prelude.ResultUnion,
              variantName,
              hintType,
              allowPartialInference: true);
          SetWrappedExpressionType(operand, resType);
          if (isFailure && expectedSuccess is null) {
            successWasUnresolved = true;
          }
          return resType;
        }
      }
    }

    return Synthesize(operand);
  }

  private void BackpatchRecoveryOperand(
      ExpressionSyntax operand,
      bool operandIsOption,
      TypeSymbol inferredSuccess,
      TypeSymbol errType) {
    ExpressionSyntax unwrapped = UnwrapParens(operand);
    if (operandIsOption) {
      UnionType concreteOpt = _types.GetUnion(_prelude.OptionUnion, [inferredSuccess]);
      SetWrappedExpressionType(operand, concreteOpt);
      RecordGenericInstantiation(_prelude.OptionUnion, concreteOpt.TypeArguments);
      return;
    }

    UnionType concreteRes = _types.GetUnion(_prelude.ResultUnion, [inferredSuccess, errType]);
    SetWrappedExpressionType(operand, concreteRes);
    if (unwrapped is CallExpressionSyntax call) {
      _state.CallResolutions[call.Id] = new CallResolution(
          CallKind.UnionVariantConstructor,
          TargetSymbol: _prelude.ResultFailureVariant,
          ConstructedUnionType: concreteRes);
    }
    RecordGenericInstantiation(_prelude.ResultUnion, concreteRes.TypeArguments);
  }
}
