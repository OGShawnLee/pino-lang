using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  /// <summary>
  /// Computes the type of <paramref name="expression"/> bottom-up and records it in the semantic
  /// model. Amendment A4: missing and error nodes are <see cref="ErrorType"/> without diagnostics.
  /// </summary>
  public TypeSymbol Synthesize(ExpressionSyntax expression) {
    _cancellationToken.ThrowIfCancellationRequested();
    TypeSymbol type = SynthesizeCore(expression);
    _state.SetNodeType(expression, type);
    return type;
  }

  /// <summary>
  /// Checks <paramref name="expression"/> against <paramref name="expected"/>, pushing the
  /// expected type down into literals and conditionals and recording implicit conversions
  /// (<c>int</c> → <c>float</c>, D-172 / D-168). The declared type wins on mismatch so that a
  /// single error never cascades.
  /// </summary>
  public TypeSymbol Check(ExpressionSyntax expression, TypeSymbol expected) =>
      Check(expression, expected, "target");

  private TypeSymbol Check(
      ExpressionSyntax expression,
      TypeSymbol expected,
      string targetDescription,
      string? targetSuffix = null) {
    _cancellationToken.ThrowIfCancellationRequested();
    if (expected.IsError) {
      return Synthesize(expression);
    }

    switch (expression) {
      case ParenthesizedExpressionSyntax paren: {
          Check(paren.Expression, expected, targetDescription, targetSuffix);
          _state.SetNodeType(paren, expected);
          return expected;
        }

      case IfExpressionSyntax ifExpression: {
          CheckCondition(ifExpression.Condition, "'if' condition", out List<LocalSymbol> trueBindings);
          Scope thenScope = ScopeOf(ifExpression, ScopeKind.Synthetic);
          foreach (LocalSymbol binding in trueBindings) {
            if (!thenScope.TryDeclare(binding, out Symbol? existing)) {
              CheckerSession.ReportDuplicate(_state, binding.Name, binding.NameSpan, existing!);
            }
          }
          using (EnterScope(thenScope))
          using (EnterBranchInequalityScope(ifExpression.Condition)) {
            Check(ifExpression.ThenBranch, expected, targetDescription, targetSuffix);
          }
          Check(ifExpression.ElseBranch, expected, targetDescription, targetSuffix);
          _state.SetNodeType(ifExpression, expected);
          return expected;
        }

      case VectorLiteralExpressionSyntax vector when expected is VectorType expectedVector: {
          foreach (ExpressionSyntax element in vector.Elements) {
            Check(element, expectedVector.ElementType, "vector element");
          }

          _state.SetNodeType(vector, expected);
          return expected;
        }

      case StaticAccessExpressionSyntax staticAccess
          when expected is UnionType expectedUnion && !expectedUnion.Definition.TypeParameters.IsEmpty: {
          TypeSymbol unionAccessType = CheckGenericUnionStaticAccess(staticAccess, expectedUnion);
          _state.SetNodeType(staticAccess, unionAccessType);
          return expected;
        }
    }

    TypeSymbol actual = CheckWithContextualExpectation(expression, expected);
    if (!TryConvert(expression, actual, expected)) {
      ReportTargetTypeMismatch(expression, actual, expected, targetDescription, targetSuffix);
    }

    return expected;
  }

  private TypeSymbol CheckWithContextualExpectation(ExpressionSyntax expression, TypeSymbol expected) {
    TypeSymbol type;
    switch (expression) {
      case ParenthesizedExpressionSyntax paren:
        type = CheckWithContextualExpectation(paren.Expression, expected);
        break;

      case LambdaExpressionSyntax lambda:
        type = SynthesizeLambda(lambda, expected as FunctionType);
        break;

      case IfExpressionSyntax ifExpression: {
          CheckCondition(ifExpression.Condition, "'if' condition", out List<LocalSymbol> trueBindings);
          Scope thenScope = ScopeOf(ifExpression, ScopeKind.Synthetic);
          foreach (LocalSymbol binding in trueBindings) {
            if (!thenScope.TryDeclare(binding, out Symbol? existing)) {
              CheckerSession.ReportDuplicate(_state, binding.Name, binding.NameSpan, existing!);
            }
          }
          TypeSymbol thenType;
          using (EnterScope(thenScope))
          using (EnterBranchInequalityScope(ifExpression.Condition)) {
            thenType = CheckWithContextualExpectation(ifExpression.ThenBranch, expected);
          }
          TypeSymbol elseType = CheckWithContextualExpectation(ifExpression.ElseBranch, expected);
          bool thenOk = TryConvert(ifExpression.ThenBranch, thenType, expected);
          bool elseOk = TryConvert(ifExpression.ElseBranch, elseType, expected);
          type = thenOk && elseOk
              ? (thenType.IsError || elseType.IsError ? _types.Error : expected)
              : JoinBranches(ifExpression, thenType, elseType);
          break;
        }

      case MatchExpressionSyntax match:
        type = CheckMatchExpression(match, expected);
        break;

      case VectorLiteralExpressionSyntax vector when expected is VectorType expectedVector: {
          foreach (ExpressionSyntax element in vector.Elements) {
            Check(element, expectedVector.ElementType, "vector element");
          }

          type = expected;
          break;
        }

      case CallExpressionSyntax call:
        type = SynthesizeCall(call, expected);
        break;

      case StaticAccessExpressionSyntax staticAccess:
        type = SynthesizeStaticAccess(staticAccess, expected);
        break;

      case StructLiteralExpressionSyntax structLiteral:
        type = SynthesizeStructLiteral(structLiteral, expected);
        break;

      case RecoveryExpressionSyntax recovery:
        type = CheckRecovery(recovery, expected);
        break;

      default:
        type = SynthesizeCore(expression);
        break;
    }

    _state.SetNodeType(expression, type);
    return type;
  }

  private void ReportTargetTypeMismatch(
      ExpressionSyntax expression,
      TypeSymbol actual,
      TypeSymbol expected,
      string targetDescription,
      string? targetSuffix = null) {
    if (expected is InterfaceType expectedInterface) {
      string detail = FormatInterfaceMismatchDetail(actual, expectedInterface);
      Report(
          DiagnosticCodes.InterfaceNotSatisfied,
          expression.Span,
          $"Cannot assign type '{actual.DisplayName}' to {targetDescription}{targetSuffix}; expected type '{expected.DisplayName}', but got '{actual.DisplayName}'{detail}.");
      return;
    }

    ReportCannotAssign(expression, actual, expected, targetDescription, targetSuffix);
  }

  private string FormatInterfaceMismatchDetail(TypeSymbol actual, InterfaceType expected) {
    if (actual is StructType or InterfaceType or TypeParameterType
        && !TypeCompatibility.TrySatisfiesInterface(
            actual,
            expected,
            _types,
            out string? reason,
            _state.Module,
            _session.InterfaceSatisfactionCache)
        && reason is not null) {
      return $" ({reason})";
    }

    return string.Empty;
  }

  private void ReportCannotAssign(
      ExpressionSyntax expression,
      TypeSymbol actual,
      TypeSymbol expected,
      string targetDescription,
      string? targetSuffix = null) =>
      Report(
          DiagnosticCodes.TypeMismatch,
          expression.Span,
          $"Cannot assign type '{actual.DisplayName}' to {targetDescription} of type '{expected.DisplayName}'{targetSuffix}.");

  /// <summary>
  /// Whether a value of <paramref name="actual"/> can be stored where <paramref name="expected"/>
  /// is required, recording the implicit conversion (if any) on <paramref name="expression"/>.
  /// </summary>
  private bool TryConvert(SyntaxNode expression, TypeSymbol actual, TypeSymbol expected) {
    if (actual.IsError || expected.IsError || actual is PendingReturnType || expected is PendingReturnType || ReferenceEquals(actual, expected)) {
      return true;
    }

    if (actual.IsNever) {
      _state.Conversions[expression.Id] = new ConversionInfo(ConversionKind.NeverToTarget, expected);
      return true;
    }

    if (ReferenceEquals(actual, _types.Int) && ReferenceEquals(expected, _types.Float)) {
      RecordIntToFloat(expression);
      return true;
    }

    if (expected is InterfaceType targetInterface
        && actual is StructType or InterfaceType or TypeParameterType) {
      if (actual is StructType actualStruct) {
        EnsureStructMethodsResolved(actualStruct.Definition);
      }

      if (TypeCompatibility.SatisfiesInterface(
          actual,
          targetInterface,
          _types,
          _state.Module,
          _session.InterfaceSatisfactionCache)) {
        _state.Conversions[expression.Id] = new ConversionInfo(
            ConversionKind.StructToInterface,
            expected,
            targetInterface);
        return true;
      }
    }

    if (expected is MapType targetMap
        && actual is MapType actualMap
        && ReferenceEquals(actualMap.KeyType, targetMap.KeyType)
        && targetMap.ValueType is InterfaceType targetMapValueIface
        && actualMap.ValueType is StructType or InterfaceType or TypeParameterType) {
      if (actualMap.ValueType is StructType actualMapValueStruct) {
        EnsureStructMethodsResolved(actualMapValueStruct.Definition);
      }

      if (TypeCompatibility.SatisfiesInterface(
          actualMap.ValueType,
          targetMapValueIface,
          _types,
          _state.Module,
          _session.InterfaceSatisfactionCache)) {
        return true;
      }
    }

    // A4: if a FunctionType contains an ErrorType (for instance a broken body return) while all
    // non-error parameter/return components agree, suppress cascading mismatch errors.
    if (actual is FunctionType actualFn
        && expected is FunctionType expectedFn
        && (actualFn.ContainsError || expectedFn.ContainsError)
        && actualFn.Parameters.Length == expectedFn.Parameters.Length) {
      bool compatible = true;
      for (int i = 0; i < actualFn.Parameters.Length; i++) {
        FunctionParameter ap = actualFn.Parameters[i];
        FunctionParameter ep = expectedFn.Parameters[i];
        if (ap.IsMutable != ep.IsMutable
            || (!ap.Type.IsError && !ep.Type.IsError && !ReferenceEquals(ap.Type, ep.Type))) {
          compatible = false;
          break;
        }
      }

      if (compatible
          && (actualFn.ReturnType.IsError
              || expectedFn.ReturnType.IsError
              || ReferenceEquals(actualFn.ReturnType, expectedFn.ReturnType))) {
        return true;
      }
    }

    return false;
  }

  private void RecordIntToFloat(SyntaxNode expression) =>
      _state.Conversions[expression.Id] = new ConversionInfo(ConversionKind.IntToFloat, _types.Float);

  /// <summary>IF-E4 / D-032: a condition must be <c>bool</c>.</summary>
  private void CheckCondition(ExpressionSyntax condition, string what) =>
      CheckCondition(condition, what, out _);

  private TypeSymbol SynthesizeCore(ExpressionSyntax expression) {
    switch (expression) {
      case MissingExpressionSyntax or ErrorExpressionSyntax:
        return _types.Error;

      case LiteralExpressionSyntax literal:
        return SynthesizeLiteral(literal);

      case InterpolatedStringExpressionSyntax interpolated:
        return SynthesizeInterpolatedString(interpolated);

      case IdentifierExpressionSyntax identifier:
        return SynthesizeIdentifier(identifier);

      case ParenthesizedExpressionSyntax paren:
        return Synthesize(paren.Expression);

      case UnaryExpressionSyntax unary:
        return SynthesizeUnary(unary);

      case BinaryExpressionSyntax binary:
        return SynthesizeBinary(binary);

      case AssignmentExpressionSyntax assignment:
        return SynthesizeAssignment(assignment);

      case IndexExpressionSyntax index:
        return SynthesizeIndex(index);

      case IfExpressionSyntax ifExpression:
        return SynthesizeIfExpression(ifExpression);

      case VectorLiteralExpressionSyntax vector:
        return SynthesizeVectorLiteral(vector);

      case VectorInitializerExpressionSyntax initializer:
        return SynthesizeVectorInitializer(initializer);

      case MapLiteralExpressionSyntax map:
        return SynthesizeMapLiteral(map);

      case SetLiteralExpressionSyntax set:
        return SynthesizeSetLiteral(set);

      case PoolExpressionSyntax pool:
        return SynthesizePoolExpression(pool);

      case HandleExpressionSyntax handle:
        return SynthesizeHandleExpression(handle);

      case StructLiteralExpressionSyntax structLiteral:
        return SynthesizeStructLiteral(structLiteral, expectedType: null);

      case MemberAccessExpressionSyntax member:
        return SynthesizeMemberAccess(member);

      case StaticAccessExpressionSyntax staticAccess:
        return SynthesizeStaticAccess(staticAccess, expectedType: null);

      case CallExpressionSyntax call:
        return SynthesizeCall(call, expectedType: null);

      case BubbleExpressionSyntax bubble:
        return SynthesizeBubble(bubble);

      case RecoveryExpressionSyntax recovery:
        return CheckRecovery(recovery, expectedType: null);

      case TupleLiteralExpressionSyntax tupleLiteral:
        return SynthesizeTupleLiteralOutsideReturn(tupleLiteral);

      case LambdaExpressionSyntax lambda:
        return SynthesizeLambda(lambda);

      case MatchExpressionSyntax match:
        return CheckMatchExpression(match, expectedType: null);

      case IsPatternExpressionSyntax isPattern:
        return CheckIsPattern(isPattern, trueBindings: null);

      default:
        return _types.Error;
    }
  }

  private TypeSymbol SynthesizeLiteral(LiteralExpressionSyntax literal) {
    if (IsMissing(literal.Token)) {
      return _types.Error;
    }

    return literal.Token.Kind switch {
      TokenKind.IntLiteral => _types.Int,
      TokenKind.FloatLiteral => _types.Float,
      TokenKind.RuneLiteral => _types.Rune,
      TokenKind.TrueLiteral or TokenKind.FalseLiteral => _types.Bool,
      _ => _types.Error,
    };
  }

  private TypeSymbol SynthesizeInterpolatedString(InterpolatedStringExpressionSyntax interpolated) {
    foreach (StringPartSyntax part in interpolated.Parts) {
      if (part is not StringInterpolationPartSyntax interpolation) {
        continue;
      }

      TypeSymbol operandType = Synthesize(interpolation.Expression);
      if (operandType.IsError || operandType.IsNever || operandType is PendingReturnType) {
        continue;
      }

      if (operandType.IsVoid) {
        Report(
            DiagnosticCodes.TypeMismatch,
            interpolation.Expression.Span,
            "Cannot interpolate a value of type 'void' into a string.");
        continue;
      }

      _state.Conversions[interpolation.Id] = new ConversionInfo(
          ConversionKind.StringInterpolationFormat,
          operandType);
    }

    return _types.String;
  }

  private TypeSymbol SynthesizeIdentifier(IdentifierExpressionSyntax identifier) {
    if (IsMissing(identifier.IdentifierToken)) {
      return _types.Error;
    }

    string name = NameOf(identifier.IdentifierToken);
    if (!TryLookupAndRecordCapture(name, out Symbol? symbol, reportSpan: identifier.Span)) {
      if (_isStaticMethod && _currentStruct is not null) {
        if (string.Equals(name, "self", StringComparison.Ordinal)) {
          Report(
              DiagnosticCodes.InvalidStaticOrInstanceAccess,
              identifier.Span,
              $"Cannot access 'self' from static method in struct '{_currentStruct.Name}'.");
          return _types.Error;
        }

        if (_currentStruct.AllFields.Any(f => string.Equals(f.Name, name, StringComparison.Ordinal))) {
          Report(
              DiagnosticCodes.InvalidStaticOrInstanceAccess,
              identifier.Span,
              $"Cannot access instance field '{name}' from static method in struct '{_currentStruct.Name}'.");
          return _types.Error;
        }

        if (_currentStruct.Methods.Any(m => !m.IsStatic && string.Equals(m.Name, name, StringComparison.Ordinal))) {
          Report(
              DiagnosticCodes.InvalidStaticOrInstanceAccess,
              identifier.Span,
              $"Cannot access instance method '{name}' from static method in struct '{_currentStruct.Name}'.");
          return _types.Error;
        }
      }

      if (_tolerantNames == 0) {
        Report(
            DiagnosticCodes.UndeclaredIdentifier,
            identifier.Span,
            $"Identifier '{name}' is not declared.");
      }

      return _types.Error;
    }

    _state.SetReferencedSymbol(identifier, symbol);
    if (symbol is MethodSymbol { IsMutating: true } mutMethod) {
      string ownerName = _currentStruct?.Name ?? mutMethod.ContainingType.Name;
      ReportCannotBindMutatingMethod(identifier.Span, ownerName, mutMethod.Name, mutMethod, targetExpr: null);
      return _types.Error;
    }

    return SymbolValueType(symbol);
  }

  private TypeSymbol SymbolValueType(Symbol symbol) =>
      symbol switch {
        LocalSymbol local => local.Type,
        ParameterSymbol parameter => parameter.Type,
        FieldSymbol field => field.Type,
        FunctionSymbol function => FunctionValueType(function),
        MethodSymbol method => _currentStruct is not null
            ? MethodValueType(
                method,
                _currentStruct.TypeParameters,
                [.. _currentStruct.TypeParameters.Select(static tp => (TypeSymbol) tp.Type)])
            : _types.Error,
        _ => _types.Error,
      };

  private TypeSymbol FunctionValueType(FunctionSymbol function) {
    if (function.IsPrelude
        || function.Intrinsic is not null
        || !function.TypeParameters.IsEmpty) {
      return _types.Error;
    }

    TypeSymbol ret = EnsureCallableReturnType(function);
    ImmutableArray<FunctionParameter>.Builder parameterTypes = ImmutableArray.CreateBuilder<FunctionParameter>(function.Parameters.Length);
    foreach (ParameterSymbol parameter in function.Parameters) {
      parameterTypes.Add(new FunctionParameter(parameter.Type, parameter.IsMutable));
    }

    return _types.GetFunction(parameterTypes.ToImmutable(), ret);
  }

  private TypeSymbol MethodValueType(
      MethodSymbol method,
      ImmutableArray<TypeParameterSymbol> ownerTypeParams,
      ImmutableArray<TypeSymbol> ownerTypeArgs) {
    if (!method.TypeParameters.IsEmpty) {
      return _types.Error;
    }

    TypeSymbol rawRet = EnsureCallableReturnType(method);
    ImmutableArray<FunctionParameter>.Builder parameterTypes = ImmutableArray.CreateBuilder<FunctionParameter>(method.Parameters.Length);
    foreach (ParameterSymbol parameter in method.Parameters) {
      parameterTypes.Add(new FunctionParameter(
          TypeSubstitution.Substitute(parameter.Type, ownerTypeParams, ownerTypeArgs, _types),
          parameter.IsMutable));
    }

    TypeSymbol ret = TypeSubstitution.Substitute(rawRet, ownerTypeParams, ownerTypeArgs, _types);
    return _types.GetFunction(parameterTypes.ToImmutable(), ret);
  }

  private TypeSymbol SynthesizeIfExpression(IfExpressionSyntax ifExpression) {
    CheckCondition(ifExpression.Condition, "'if' condition", out List<LocalSymbol> trueBindings);
    Scope thenScope = ScopeOf(ifExpression, ScopeKind.Synthetic);
    foreach (LocalSymbol binding in trueBindings) {
      if (!thenScope.TryDeclare(binding, out Symbol? existing)) {
        CheckerSession.ReportDuplicate(_state, binding.Name, binding.NameSpan, existing!);
      }
    }
    TypeSymbol thenType;
    using (EnterScope(thenScope))
    using (EnterBranchInequalityScope(ifExpression.Condition)) {
      thenType = Synthesize(ifExpression.ThenBranch);
    }
    TypeSymbol elseType = Synthesize(ifExpression.ElseBranch);
    return JoinBranches(ifExpression, thenType, elseType);
  }

  /// <summary>D-033 / IF-7 / IF-E5: branch types must agree, <c>int</c> + <c>float</c> → <c>float</c>.</summary>
  private TypeSymbol JoinBranches(IfExpressionSyntax ifExpression, TypeSymbol thenType, TypeSymbol elseType) {
    if (thenType.IsError || elseType.IsError) {
      return _types.Error;
    }

    if (thenType is PendingReturnType && elseType is PendingReturnType) {
      return thenType;
    }

    if (thenType is PendingReturnType) {
      return elseType.IsNever ? thenType : elseType;
    }

    if (elseType is PendingReturnType) {
      return thenType.IsNever ? elseType : thenType;
    }

    if (thenType.IsNever) {
      return elseType;
    }

    if (elseType.IsNever || ReferenceEquals(thenType, elseType)) {
      return thenType;
    }

    if (ReferenceEquals(thenType, _types.Int) && ReferenceEquals(elseType, _types.Float)) {
      RecordIntToFloat(ifExpression.ThenBranch);
      return _types.Float;
    }

    if (ReferenceEquals(thenType, _types.Float) && ReferenceEquals(elseType, _types.Int)) {
      RecordIntToFloat(ifExpression.ElseBranch);
      return _types.Float;
    }

    Report(
        DiagnosticCodes.BranchTypeMismatch,
        ifExpression.Span,
        $"Conditional branches have incompatible types '{thenType.DisplayName}' and '{elseType.DisplayName}'.");
    return _types.Error;
  }
}
