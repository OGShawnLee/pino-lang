using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  private enum ArithmeticFailure : byte {
    None,
    StringWithNonString,
    RuneWithRune,
    Operands,
  }

  /// <summary>The outcome of typing <c>left op right</c> without touching the side tables.</summary>
  private readonly record struct ArithmeticResult(
      TypeSymbol? Type,
      bool WidenLeft,
      bool WidenRight,
      ArithmeticFailure Failure);

  private TypeSymbol SynthesizeUnary(UnaryExpressionSyntax unary) {
    TypeSymbol operand = Synthesize(unary.Operand);
    if (IsSilent(operand)) {
      return _types.Error;
    }

    if (unary.OperatorToken.Kind == TokenKind.Not) {
      if (ReferenceEquals(operand, _types.Bool)) {
        return _types.Bool;
      }

      Report(
          DiagnosticCodes.InvalidOperatorOperands,
          unary.Span,
          $"Operator 'not' requires a 'bool' operand, but got '{operand.DisplayName}'.");
      return _types.Error;
    }

    if (ReferenceEquals(operand, _types.Int) || ReferenceEquals(operand, _types.Float)) {
      return operand;
    }

    Report(
        DiagnosticCodes.InvalidOperatorOperands,
        unary.Span,
        $"Operator '-' requires an 'int' or 'float' operand, but got '{operand.DisplayName}'.");
    return _types.Error;
  }

  private TypeSymbol SynthesizeBinary(BinaryExpressionSyntax binary) {
    TypeSymbol left = Synthesize(binary.Left);
    TypeSymbol right = Synthesize(binary.Right);
    TokenKind kind = binary.OperatorToken.Kind;

    switch (kind) {
      case TokenKind.And:
      case TokenKind.Or:
        return CheckLogical(binary, left, right);

      case TokenKind.InKeyword:
        return CheckMembership(binary, left, right, negated: false);

      case TokenKind.Not when binary.SecondaryOperatorToken is { Kind: TokenKind.InKeyword }:
        return CheckMembership(binary, left, right, negated: true);

      case TokenKind.EqualEqual:
      case TokenKind.BangEqual:
      case TokenKind.EqualEqualEqual:
      case TokenKind.BangEqualEqual:
        return CheckEquality(binary, left, right);

      case TokenKind.Less:
      case TokenKind.LessEqual:
      case TokenKind.Greater:
      case TokenKind.GreaterEqual:
        return CheckComparison(binary, left, right);

      case TokenKind.Plus:
      case TokenKind.Minus:
      case TokenKind.Star:
      case TokenKind.Slash:
      case TokenKind.SlashSlash:
      case TokenKind.Percent: {
          if (IsSilent(left) || IsSilent(right)) {
            return _types.Error;
          }

          ArithmeticResult result = ComputeArithmetic(kind, left, right);
          if (result.Type is null) {
            ReportArithmeticFailure(binary, kind, left, right, result.Failure);
            return _types.Error;
          }

          if (result.WidenLeft) {
            RecordIntToFloat(binary.Left);
          }

          if (result.WidenRight) {
            RecordIntToFloat(binary.Right);
          }

          return result.Type;
        }

      default:
        return _types.Error;
    }
  }

  private TypeSymbol CheckLogical(BinaryExpressionSyntax binary, TypeSymbol left, TypeSymbol right) {
    string op = binary.OperatorToken.Kind == TokenKind.And ? "and" : "or";
    foreach ((TypeSymbol type, ExpressionSyntax operand) in new[] { (left, binary.Left), (right, binary.Right) }) {
      if (!IsSilent(type) && !ReferenceEquals(type, _types.Bool)) {
        Report(
            DiagnosticCodes.InvalidOperatorOperands,
            operand.Span,
            $"Operator '{op}' requires 'bool' operands, but got '{type.DisplayName}'.");
      }
    }

    return _types.Bool;
  }

  /// <summary>EQ-1–EQ-13 / D-019: structural and identity equality, <c>int</c> widens to <c>float</c>.</summary>
  private TypeSymbol CheckEquality(BinaryExpressionSyntax binary, TypeSymbol left, TypeSymbol right) {
    if (IsSilent(left) || IsSilent(right)) {
      return _types.Bool;
    }

    string op = OperatorText(binary);
    if (left is TupleType || right is TupleType) {
      Report(
          DiagnosticCodes.InvalidOperatorOperands,
          binary.Span,
          $"Tuples cannot be compared with '{op}'.");
      return _types.Bool;
    }

    if (left.IsVoid || right.IsVoid) {
      Report(
          DiagnosticCodes.InvalidOperatorOperands,
          binary.Span,
          $"Operator '{op}' cannot be applied to a 'void' value.");
      return _types.Bool;
    }

    if (ReferenceEquals(left, right)) {
      return _types.Bool;
    }

    if (ReferenceEquals(left, _types.Int) && ReferenceEquals(right, _types.Float)) {
      RecordIntToFloat(binary.Left);
      return _types.Bool;
    }

    if (ReferenceEquals(left, _types.Float) && ReferenceEquals(right, _types.Int)) {
      RecordIntToFloat(binary.Right);
      return _types.Bool;
    }

    if (left is StructType && right is InterfaceType rightIface
        && TypeCompatibility.SatisfiesInterface(left, rightIface, _types, _state.Module, _session.InterfaceSatisfactionCache)) {
      return _types.Bool;
    }

    if (right is StructType && left is InterfaceType leftIface
        && TypeCompatibility.SatisfiesInterface(right, leftIface, _types, _state.Module, _session.InterfaceSatisfactionCache)) {
      return _types.Bool;
    }

    if (left is InterfaceType && right is InterfaceType
        && (TypeCompatibility.SatisfiesInterface(left, (InterfaceType) right, _types, _state.Module, _session.InterfaceSatisfactionCache)
            || TypeCompatibility.SatisfiesInterface(right, (InterfaceType) left, _types, _state.Module, _session.InterfaceSatisfactionCache))) {
      return _types.Bool;
    }

    // Generic parameter comparisons are refined by ticket 039.
    if (left is TypeParameterType || right is TypeParameterType) {
      return _types.Bool;
    }

    Report(
        DiagnosticCodes.InvalidOperatorOperands,
        binary.Span,
        $"Cannot compare '{left.DisplayName}' with '{right.DisplayName}' using '{op}'.");
    return _types.Bool;
  }

  private TypeSymbol CheckComparison(BinaryExpressionSyntax binary, TypeSymbol left, TypeSymbol right) {
    if (IsSilent(left) || IsSilent(right)) {
      return _types.Bool;
    }

    bool leftNumeric = IsNumeric(left);
    bool rightNumeric = IsNumeric(right);
    if (leftNumeric && rightNumeric) {
      if (ReferenceEquals(left, _types.Int) && ReferenceEquals(right, _types.Float)) {
        RecordIntToFloat(binary.Left);
      } else if (ReferenceEquals(left, _types.Float) && ReferenceEquals(right, _types.Int)) {
        RecordIntToFloat(binary.Right);
      }

      return _types.Bool;
    }

    // RUNE-4 / STR-10: runes compare with runes, strings with strings (lexicographically).
    if (ReferenceEquals(left, right)
        && (ReferenceEquals(left, _types.Rune) || ReferenceEquals(left, _types.String))) {
      return _types.Bool;
    }

    Report(
        DiagnosticCodes.InvalidOperatorOperands,
        binary.Span,
        $"Operator '{OperatorText(binary)}' cannot be applied to operands of type '{left.DisplayName}' and '{right.DisplayName}'.");
    return _types.Bool;
  }

  /// <summary><c>in</c> / <c>not in</c> over <c>[]T</c>, <c>map[K, V]</c>, <c>set[T]</c> and <c>string</c> (VEC-10, MAP-7, SETLIB-8, STRLIB-17).</summary>
  private TypeSymbol CheckMembership(BinaryExpressionSyntax binary, TypeSymbol left, TypeSymbol right, bool negated) {
    if (IsSilent(right)) {
      return _types.Bool;
    }

    string op = negated ? "not in" : "in";
    TypeSymbol? expectedElement = right switch {
      VectorType vector => vector.ElementType,
      MapType map => map.KeyType,
      SetType set => set.ElementType,
      PrimitiveType { Kind: PrimitiveKind.String } => _types.String,
      _ => null,
    };

    if (expectedElement is null) {
      Report(
          DiagnosticCodes.InvalidOperatorOperands,
          binary.Right.Span,
          $"Operator '{op}' requires a vector, map, set or string on its right, but got '{right.DisplayName}'.");
      return _types.Bool;
    }

    if (!IsSilent(left) && !TryConvert(binary.Left, left, expectedElement)) {
      Report(
          DiagnosticCodes.InvalidOperatorOperands,
          binary.Left.Span,
          $"Cannot test a '{left.DisplayName}' for membership with '{op}' in '{right.DisplayName}'; expected type '{expectedElement.DisplayName}'.");
    }

    return _types.Bool;
  }

  private bool IsNumeric(TypeSymbol type) =>
      ReferenceEquals(type, _types.Int) || ReferenceEquals(type, _types.Float);

  private static string OperatorText(BinaryExpressionSyntax binary) => binary.OperatorToken.Kind switch {
    TokenKind.EqualEqual => "==",
    TokenKind.BangEqual => "!=",
    TokenKind.EqualEqualEqual => "===",
    TokenKind.BangEqualEqual => "!==",
    TokenKind.Less => "<",
    TokenKind.LessEqual => "<=",
    TokenKind.Greater => ">",
    TokenKind.GreaterEqual => ">=",
    _ => "?",
  };

  private static string ArithmeticOperatorText(TokenKind kind) => kind switch {
    TokenKind.Plus or TokenKind.PlusAssign => "+",
    TokenKind.Minus or TokenKind.MinusAssign => "-",
    TokenKind.Star or TokenKind.StarAssign => "*",
    TokenKind.Slash or TokenKind.SlashAssign => "/",
    TokenKind.SlashSlash or TokenKind.SlashSlashAssign => "//",
    TokenKind.Percent or TokenKind.PercentAssign => "%",
    _ => "?",
  };

  /// <summary>
  /// Types <c>left op right</c> for the arithmetic operators (D-019, D-164, D-171, D-011, D-025):
  /// <c>/</c> is always <c>float</c>, <c>//</c> and <c>%</c> are floored on <c>int</c> and
  /// <c>float</c>, <c>rune ± int</c> is a <c>rune</c>, <c>rune - rune</c> is an <c>int</c>.
  /// </summary>
  private ArithmeticResult ComputeArithmetic(TokenKind op, TypeSymbol left, TypeSymbol right) {
    bool leftInt = ReferenceEquals(left, _types.Int);
    bool rightInt = ReferenceEquals(right, _types.Int);
    bool leftFloat = ReferenceEquals(left, _types.Float);
    bool rightFloat = ReferenceEquals(right, _types.Float);
    bool leftString = ReferenceEquals(left, _types.String);
    bool rightString = ReferenceEquals(right, _types.String);
    bool leftRune = ReferenceEquals(left, _types.Rune);
    bool rightRune = ReferenceEquals(right, _types.Rune);
    bool numeric = (leftInt || leftFloat) && (rightInt || rightFloat);

    TokenKind baseOp = op switch {
      TokenKind.PlusAssign => TokenKind.Plus,
      TokenKind.MinusAssign => TokenKind.Minus,
      TokenKind.StarAssign => TokenKind.Star,
      TokenKind.SlashAssign => TokenKind.Slash,
      TokenKind.SlashSlashAssign => TokenKind.SlashSlash,
      TokenKind.PercentAssign => TokenKind.Percent,
      _ => op,
    };

    switch (baseOp) {
      case TokenKind.Plus:
        if (leftString && rightString) {
          return new ArithmeticResult(_types.String, false, false, ArithmeticFailure.None);
        }

        if (leftString || rightString) {
          return Failed(ArithmeticFailure.StringWithNonString);
        }

        if (leftRune && rightRune) {
          return Failed(ArithmeticFailure.RuneWithRune);
        }

        if (leftRune && rightInt) {
          return new ArithmeticResult(_types.Rune, false, false, ArithmeticFailure.None);
        }

        return numeric ? Numeric(leftInt, rightInt) : Failed(ArithmeticFailure.Operands);

      case TokenKind.Minus:
        if (leftRune && rightInt) {
          return new ArithmeticResult(_types.Rune, false, false, ArithmeticFailure.None);
        }

        if (leftRune && rightRune) {
          return new ArithmeticResult(_types.Int, false, false, ArithmeticFailure.None);
        }

        return numeric ? Numeric(leftInt, rightInt) : Failed(ArithmeticFailure.Operands);

      case TokenKind.Star:
        return numeric ? Numeric(leftInt, rightInt) : Failed(ArithmeticFailure.Operands);

      case TokenKind.Slash:
        // TYPE-9 / D-019: `/` always yields a float; `int` operands widen.
        return numeric
            ? new ArithmeticResult(_types.Float, leftInt, rightInt, ArithmeticFailure.None)
            : Failed(ArithmeticFailure.Operands);

      case TokenKind.SlashSlash:
      case TokenKind.Percent:
        // TYPE-14 / TYPE-15 / TYPE-18 (D-164, D-171): floored on `int` and on `float`/mixed operands.
        return numeric ? Numeric(leftInt, rightInt) : Failed(ArithmeticFailure.Operands);

      default:
        return Failed(ArithmeticFailure.Operands);
    }

    ArithmeticResult Numeric(bool leftIsInt, bool rightIsInt) =>
        leftIsInt && rightIsInt
            ? new ArithmeticResult(_types.Int, false, false, ArithmeticFailure.None)
            : new ArithmeticResult(_types.Float, leftIsInt, rightIsInt, ArithmeticFailure.None);

    static ArithmeticResult Failed(ArithmeticFailure failure) => new(null, false, false, failure);
  }

  private void ReportArithmeticFailure(
      SyntaxNode node,
      TokenKind op,
      TypeSymbol left,
      TypeSymbol right,
      ArithmeticFailure failure) {
    string opText = ArithmeticOperatorText(op);
    switch (failure) {
      case ArithmeticFailure.StringWithNonString:
        // STR-E5 / D-025
        Report(
            DiagnosticCodes.InvalidOperatorOperands,
            node.Span,
            $"Cannot use '{opText}' with a 'string' and a non-string ('{left.DisplayName}' {opText} '{right.DisplayName}'); use string interpolation or str(...) instead.",
            help: "write \"$a$b\" or str(a) + str(b) to build a string from other values");
        break;

      case ArithmeticFailure.RuneWithRune:
        // RUNE-E2 / D-011
        Report(
            DiagnosticCodes.InvalidOperatorOperands,
            node.Span,
            "Cannot add two runes with '+'; use \"$a$b\" or str(a) + str(b) to join them into a string.",
            help: "write \"$a$b\" or str(a) + str(b) to join two runes into a string");
        break;

      default:
        Report(
            DiagnosticCodes.InvalidOperatorOperands,
            node.Span,
            $"Operator '{opText}' cannot be applied to operands of type '{left.DisplayName}' and '{right.DisplayName}'.");
        break;
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Assignment (D-017, VAR-E1, VAR-E2, VAR-E5, STR-E1, AD-002 AssignedBindings)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol SynthesizeAssignment(AssignmentExpressionSyntax assignment) {
    TokenKind op = assignment.OperatorToken.Kind;
    ExpressionSyntax target = assignment.Target;

    switch (target) {
      case MissingExpressionSyntax or ErrorExpressionSyntax:
        Synthesize(target);
        Synthesize(assignment.Value);
        return _types.Error;

      case IdentifierExpressionSyntax identifier:
        return AssignToIdentifier(assignment, identifier, op);

      case IndexExpressionSyntax index:
        return AssignToIndex(assignment, index, op);

      case MemberAccessExpressionSyntax member:
        return AssignToMember(assignment, member, op);

      default:
        Synthesize(target);
        Synthesize(assignment.Value);
        Report(
            DiagnosticCodes.InvalidAssignmentTarget,
            target.Span,
            "Cannot assign to this expression; the target of an assignment must be a variable, parameter, field or element.");
        return _types.Error;
    }
  }

  private TypeSymbol AssignToIdentifier(AssignmentExpressionSyntax assignment, IdentifierExpressionSyntax identifier, TokenKind op) {
    TypeSymbol targetType = Synthesize(identifier);
    Symbol? symbol = _state.ReferencedSymbols[identifier.Id];

    switch (symbol) {
      case null:
        // Undeclared (already reported) or silently tolerated.
        Synthesize(assignment.Value);
        return _types.Error;

      case LocalSymbol local:
        // AD-002: every local or parameter that is a target of `=` / `op=` is recorded.
        _state.AssignedSymbols.Add(local);
        if (!local.IsMutable) {
          ReportImmutableAssignment(identifier, local);
        }

        break;

      case ParameterSymbol parameter:
        _state.AssignedSymbols.Add(parameter);
        break;

      case FieldSymbol:
        // STRUCT-4: bare field assignment updates the receiver's field (does not rebind a local/param).
        break;

      case FunctionSymbol function:
        Synthesize(assignment.Value);
        Report(
            DiagnosticCodes.InvalidAssignmentTarget,
            identifier.Span,
            $"Cannot assign to function '{function.Name}'.");
        return _types.Error;

      case MethodSymbol method:
        Synthesize(assignment.Value);
        Report(
            DiagnosticCodes.InvalidAssignmentTarget,
            identifier.Span,
            $"Cannot assign to method '{method.Name}'.");
        return _types.Error;

      default:
        Synthesize(assignment.Value);
        return _types.Error;
    }

    return CheckAssignedValue(assignment, identifier, targetType, op);
  }

  private void ReportImmutableAssignment(IdentifierExpressionSyntax identifier, LocalSymbol local) {
    bool isVal = local.Kind == LocalKind.Variable;
    ImmutableArray<RelatedLocation> related = isVal && local.NameSpan.Length > 0
        ? [new RelatedLocation(local.NameSpan, $"'{local.Name}' is declared with 'val' here", local.FilePath)]
        : [];
    Report(
        DiagnosticCodes.CannotAssignToImmutable,
        identifier.Span,
        $"Cannot reassign constant variable '{local.Name}'.",
        related,
        isVal ? "change 'val' to 'var' if this binding should be mutable" : null);
  }

  private TypeSymbol AssignToIndex(AssignmentExpressionSyntax assignment, IndexExpressionSyntax index, TokenKind op) {
    TypeSymbol elementType = Synthesize(index);
    TypeSymbol? containerType = _state.NodeTypes[index.Target.Id];

    if (containerType is PrimitiveType { Kind: PrimitiveKind.String }) {
      // STR-E1 / D-023: strings are immutable.
      Synthesize(assignment.Value);
      Report(
          DiagnosticCodes.InvalidAssignmentTarget,
          index.Span,
          "Cannot assign to index of a 'string'; strings are immutable.");
      return _types.Error;
    }

    if (elementType.IsError) {
      Synthesize(assignment.Value);
      return _types.Error;
    }

    return CheckAssignedValue(assignment, index, elementType, op);
  }

  /// <summary>
  /// Types the right-hand side of <c>target op= value</c>: plain assignment checks the value
  /// against the target type (declared type wins), compound assignment types
  /// <c>target op value</c> and checks that it can be stored back (VAR-4, D-164, VAR-E5).
  /// </summary>
  private TypeSymbol CheckAssignedValue(
      AssignmentExpressionSyntax assignment,
      ExpressionSyntax target,
      TypeSymbol targetType,
      TokenKind op) {
    if (targetType.IsError) {
      Synthesize(assignment.Value);
      return _types.Error;
    }

    if (op == TokenKind.Assign) {
      Check(assignment.Value, targetType, "target");
      return targetType;
    }

    TypeSymbol valueType = Synthesize(assignment.Value);
    if (IsSilent(valueType)) {
      return targetType;
    }

    ArithmeticResult result = ComputeArithmetic(op, targetType, valueType);
    if (result.Type is null) {
      ReportArithmeticFailure(assignment, op, targetType, valueType, result.Failure);
      return targetType;
    }

    if (!ReferenceEquals(result.Type, targetType)) {
      ReportCannotAssign(assignment.Value, result.Type, targetType, "target");
      return targetType;
    }

    if (result.WidenRight) {
      RecordIntToFloat(assignment.Value);
    }

    return targetType;
  }

  // ---------------------------------------------------------------------------------------------
  // Indexing (VEC-3, MAP-4, STRLIB-19 / D-023)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol SynthesizeIndex(IndexExpressionSyntax index) {
    TypeSymbol target = Synthesize(index.Target);

    switch (target) {
      case var t when t.IsError || t.IsNever:
        Synthesize(index.Index);
        return _types.Error;

      case VectorType vector:
        CheckIntegerIndex(index, "Vector");
        return vector.ElementType;

      case PrimitiveType { Kind: PrimitiveKind.String }:
        CheckIntegerIndex(index, "String");
        return _types.Rune;

      case MapType map:
        Check(index.Index, map.KeyType, "map key");
        return map.ValueType;

      case TypeParameterType or InterfaceType:
        Synthesize(index.Index);
        return _types.Error;

      default:
        Synthesize(index.Index);
        Report(
            DiagnosticCodes.InvalidIndexOperation,
            index.Span,
            $"Cannot index a value of type '{target.DisplayName}'.");
        return _types.Error;
    }
  }

  private void CheckIntegerIndex(IndexExpressionSyntax index, string what) {
    TypeSymbol indexType = Synthesize(index.Index);
    if (!IsSilent(indexType) && !ReferenceEquals(indexType, _types.Int)) {
      Report(
          DiagnosticCodes.InvalidIndexOperation,
          index.Index.Span,
          $"{what} index must have type 'int', but got '{indexType.DisplayName}'.");
    }
  }
}
