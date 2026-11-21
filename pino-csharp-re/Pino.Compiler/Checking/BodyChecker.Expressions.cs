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

  private TypeSymbol Check(ExpressionSyntax expression, TypeSymbol expected, string targetDescription) {
    _cancellationToken.ThrowIfCancellationRequested();
    if (expected.IsError) {
      return Synthesize(expression);
    }

    switch (expression) {
      case ParenthesizedExpressionSyntax paren: {
          Check(paren.Expression, expected, targetDescription);
          _state.SetNodeType(paren, expected);
          return expected;
        }

      case IfExpressionSyntax ifExpression: {
          CheckCondition(ifExpression.Condition, "'if' condition");
          Check(ifExpression.ThenBranch, expected, targetDescription);
          Check(ifExpression.ElseBranch, expected, targetDescription);
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
    }

    TypeSymbol actual = Synthesize(expression);
    if (!TryConvert(expression, actual, expected)) {
      ReportCannotAssign(expression, actual, expected, targetDescription);
    }

    return expected;
  }

  private void ReportCannotAssign(ExpressionSyntax expression, TypeSymbol actual, TypeSymbol expected, string targetDescription) =>
      Report(
          DiagnosticCodes.TypeMismatch,
          expression.Span,
          $"Cannot assign type '{actual.DisplayName}' to {targetDescription} of type '{expected.DisplayName}'.");

  /// <summary>
  /// Whether a value of <paramref name="actual"/> can be stored where <paramref name="expected"/>
  /// is required, recording the implicit conversion (if any) on <paramref name="expression"/>.
  /// </summary>
  private bool TryConvert(ExpressionSyntax expression, TypeSymbol actual, TypeSymbol expected) {
    if (actual.IsError || expected.IsError || ReferenceEquals(actual, expected)) {
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
        && actual is StructType or InterfaceType
        && TypeCompatibility.SatisfiesInterface(actual, targetInterface, _types)) {
      _state.Conversions[expression.Id] = new ConversionInfo(
          ConversionKind.StructToInterface,
          expected,
          targetInterface);
      return true;
    }

    return false;
  }

  private void RecordIntToFloat(ExpressionSyntax expression) =>
      _state.Conversions[expression.Id] = new ConversionInfo(ConversionKind.IntToFloat, _types.Float);

  /// <summary>IF-E4 / D-032: a condition must be <c>bool</c>.</summary>
  private void CheckCondition(ExpressionSyntax condition, string what) {
    TypeSymbol type = Synthesize(condition);
    if (!IsSilent(type) && !ReferenceEquals(type, _types.Bool)) {
      Report(
          DiagnosticCodes.InvalidConditionType,
          condition.Span,
          $"The {what} must have type 'bool', but got '{type.DisplayName}'.");
    }
  }

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

      default:
        return SynthesizeDeferred(expression);
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
      if (operandType.IsError || operandType.IsNever) {
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
    if (!_scope.TryLookup(name, out Symbol? symbol)) {
      // `it` as an implicit lambda parameter in call arguments is typed by tickets 037/038.
      if (_tolerantNames == 0 && !string.Equals(name, "it", StringComparison.Ordinal)) {
        Report(
            DiagnosticCodes.UndeclaredIdentifier,
            identifier.Span,
            $"Identifier '{name}' is not declared.");
      }

      return _types.Error;
    }

    _state.SetReferencedSymbol(identifier, symbol);
    return symbol switch {
      LocalSymbol local => local.Type,
      ParameterSymbol parameter => parameter.Type,
      FunctionSymbol function => FunctionValueType(function),
      _ => _types.Error,
    };
  }

  private TypeSymbol FunctionValueType(FunctionSymbol function) {
    if (function.IsPrelude
        || function.Intrinsic is not null
        || function.ReturnState != ReturnInferenceState.Explicit
        || !function.TypeParameters.IsEmpty) {
      return _types.Error;
    }

    ImmutableArray<TypeSymbol>.Builder parameterTypes = ImmutableArray.CreateBuilder<TypeSymbol>(function.Parameters.Length);
    foreach (ParameterSymbol parameter in function.Parameters) {
      parameterTypes.Add(parameter.Type);
    }

    return _types.GetFunction(parameterTypes.ToImmutable(), function.ReturnType);
  }

  private TypeSymbol SynthesizeIfExpression(IfExpressionSyntax ifExpression) {
    CheckCondition(ifExpression.Condition, "'if' condition");
    TypeSymbol thenType = Synthesize(ifExpression.ThenBranch);
    TypeSymbol elseType = Synthesize(ifExpression.ElseBranch);
    return JoinBranches(ifExpression, thenType, elseType);
  }

  /// <summary>D-033 / IF-7 / IF-E5: branch types must agree, <c>int</c> + <c>float</c> → <c>float</c>.</summary>
  private TypeSymbol JoinBranches(IfExpressionSyntax ifExpression, TypeSymbol thenType, TypeSymbol elseType) {
    if (thenType.IsError || elseType.IsError) {
      return _types.Error;
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

  /// <summary>
  /// Expression forms owned by tickets 037–040 (calls, member access, struct and tuple literals,
  /// lambdas, <c>match</c>, <c>?</c>, <c>or</c>, <c>is</c>). Their sub-expressions are visited so that
  /// names resolve and independent errors are still found, and the node itself is
  /// <see cref="ErrorType"/> (or <c>bool</c> for <c>is</c>) without diagnostics.
  /// </summary>
  private TypeSymbol SynthesizeDeferred(ExpressionSyntax expression) {
    switch (expression) {
      case CallExpressionSyntax call:
        Synthesize(call.Callee);
        foreach (ArgumentSyntax argument in call.Arguments) {
          VisitDeferredOperand(argument.Expression);
        }

        return _types.Error;

      case MemberAccessExpressionSyntax member:
        // A2: `obj:` still types its target so completion works right after the colon.
        Synthesize(member.Target);
        return _types.Error;

      case StaticAccessExpressionSyntax staticAccess:
        if (staticAccess.Target is IdentifierExpressionSyntax { } targetIdentifier
            && !IsMissing(targetIdentifier.IdentifierToken)
            && _scope.TryLookup(NameOf(targetIdentifier.IdentifierToken), out Symbol? targetSymbol)) {
          _state.SetReferencedSymbol(targetIdentifier, targetSymbol);
        }

        return _types.Error;

      case StructLiteralExpressionSyntax structLiteral:
        foreach (StructFieldInitSyntax field in structLiteral.Fields) {
          if (field.Value is { } value) {
            VisitDeferredOperand(value);
          }
        }

        return _types.Error;

      case TupleLiteralExpressionSyntax tupleLiteral:
        foreach (TupleFieldSyntax field in tupleLiteral.Fields) {
          if (field.Value is { } value) {
            VisitDeferredOperand(value);
          }
        }

        return _types.Error;

      case BubbleExpressionSyntax bubble:
        Synthesize(bubble.Expression);
        return _types.Error;

      case RecoveryExpressionSyntax recovery:
        Synthesize(recovery.Expression);
        return _types.Error;

      case MatchExpressionSyntax match:
        Synthesize(match.Subject);
        return _types.Error;

      case IsPatternExpressionSyntax isPattern:
        Synthesize(isPattern.Expression);
        return _types.Bool;

      default:
        // LambdaExpressionSyntax and unknown future forms.
        return _types.Error;
    }
  }

  private void VisitDeferredOperand(ExpressionSyntax operand) {
    if (operand is not LambdaExpressionSyntax) {
      Synthesize(operand);
    } else {
      _state.SetNodeType(operand, _types.Error);
    }
  }
}
