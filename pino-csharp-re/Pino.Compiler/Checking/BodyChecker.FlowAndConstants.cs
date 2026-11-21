using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  private int _loopDepth;

  // ---------------------------------------------------------------------------------------------
  // Loop Control Flow Scoping (OQ-27 / LOOP-E1 / PN3401)
  // ---------------------------------------------------------------------------------------------

  private void CheckBreak(BreakStatementSyntax breakStatement) {
    if (_loopDepth <= 0) {
      Report(
          DiagnosticCodes.BreakOrContinueOutsideLoop,
          breakStatement.Span,
          "'break' is only allowed inside a loop (cannot be used outside of a loop).");
    }
  }

  private void CheckContinue(ContinueStatementSyntax continueStatement) {
    if (_loopDepth <= 0) {
      Report(
          DiagnosticCodes.BreakOrContinueOutsideLoop,
          continueStatement.Span,
          "'continue' is only allowed inside a loop (cannot be used outside of a loop).");
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Definite Return Analysis (D-050, FN-E6, PN3404, design.md §4.4)
  // ---------------------------------------------------------------------------------------------

  private void RunPhase4DefiniteReturns() {
    foreach ((FunctionDeclarationSyntax decl, FunctionSymbol fnSym, _) in _state.Functions) {
      if (!_callablesBySymbol.TryGetValue(fnSym, out CallableEntry? entry) || !entry.BodyFinalized) {
        continue;
      }

      CheckDefiniteReturn(
          fnSym,
          "function",
          "Function",
          fnSym.Name,
          fnSym.NameSpan,
          decl.Body,
          fnSym.ReturnType,
          entry.HasMissingHeader);
    }

    foreach ((StructDeclarationSyntax structDecl, _, _, _) in _state.Structs) {
      foreach (SyntaxNode member in structDecl.Members) {
        if (member is MethodDeclarationSyntax methodDecl
            && _state.DeclaredSymbols[methodDecl.Id] is MethodSymbol methodSym
            && _callablesBySymbol.TryGetValue(methodSym, out CallableEntry? entry)
            && entry.BodyFinalized) {
          CheckDefiniteReturn(
              methodSym,
              "method",
              "Method",
              methodSym.Name,
              methodSym.NameSpan,
              methodDecl.Body,
              methodSym.ReturnType,
              entry.HasMissingHeader);
        }
      }
    }
  }

  private void CheckDefiniteReturn(
      Symbol? callableSymbol,
      string kindName,
      string capitalizedKindName,
      string callableName,
      SourceSpan reportSpan,
      BlockStatementSyntax body,
      TypeSymbol returnType,
      bool hasMissingHeader) {
    _ = callableSymbol;
    if (returnType.IsVoid || returnType.IsError || returnType.IsNever || returnType is PendingReturnType) {
      return;
    }

    if (hasMissingHeader || ContainsSyntaxRecovery(body)) {
      return;
    }

    if (AlwaysReturns(body)) {
      return;
    }

    string subject = string.IsNullOrEmpty(callableName)
        ? capitalizedKindName
        : $"{capitalizedKindName} '{callableName}'";
    Report(
        DiagnosticCodes.MissingReturnPath,
        reportSpan,
        $"{subject} does not always return a value of type '{returnType.DisplayName}' on all code paths.");
    _ = kindName;
  }

  private bool AlwaysReturns(SyntaxNode node) {
    switch (node) {
      case ReturnStatementSyntax:
        return true;

      case ExpressionStatementSyntax exprStmt:
        return (uint) exprStmt.Expression.Id < (uint) _state.NodeTypes.Length
            && _state.NodeTypes[exprStmt.Expression.Id] is { IsNever: true };

      case BlockStatementSyntax block:
        foreach (SyntaxNode item in block.Items) {
          if (item is FunctionDeclarationSyntax or StructDeclarationSyntax or InterfaceDeclarationSyntax
              or EnumDeclarationSyntax or UnionDeclarationSyntax) {
            continue;
          }

          if (AlwaysReturns(item)) {
            return true;
          }
        }
        return false;

      case IfStatementSyntax ifStmt: {
          ConstantValue? condConst = TryEvalConstant(ifStmt.Condition);
          if (condConst is { Kind: ConstantValueKind.Bool, BoolValue: true }) {
            return AlwaysReturns(ifStmt.ThenBranch);
          }

          if (condConst is { Kind: ConstantValueKind.Bool, BoolValue: false }) {
            return ifStmt.ElseBranch is not null && AlwaysReturns(ifStmt.ElseBranch);
          }

          return ifStmt.ElseBranch is not null
              && AlwaysReturns(ifStmt.ThenBranch)
              && AlwaysReturns(ifStmt.ElseBranch);
        }

      case MatchStatementSyntax matchStmt:
        return MatchStatementAlwaysReturns(matchStmt);

      case ForStatementSyntax forStmt:
        // Infinite loop `for { ... }` with no `break` targeting it never exits normally.
        return forStmt.Header is null && !ContainsBreakTargetingLoop(forStmt.Body);

      default:
        return false;
    }
  }

  private bool MatchStatementAlwaysReturns(MatchStatementSyntax matchStmt) {
    if ((uint) matchStmt.Subject.Id >= (uint) _state.NodeTypes.Length) {
      return false;
    }

    TypeSymbol? subjectType = _state.NodeTypes[matchStmt.Subject.Id];
    if (subjectType is null || subjectType.IsError) {
      return false;
    }

    var coveredEnumMembers = new HashSet<EnumMemberSymbol>(ReferenceEqualityComparer.Instance);
    var coveredUnionVariants = new HashSet<UnionVariantSymbol>(ReferenceEqualityComparer.Instance);
    bool hasElse = false;

    foreach (SyntaxNode armNode in matchStmt.Arms) {
      if (armNode is ErrorMatchArmSyntax) {
        return false;
      }

      bool alreadyExhaustive = hasElse
          || IsAllEnumCovered(subjectType, coveredEnumMembers)
          || IsAllUnionCovered(subjectType, coveredUnionVariants);
      if (alreadyExhaustive) {
        return true;
      }

      if (armNode is ElseArmSyntax elseArm) {
        if (elseArm.BlockBody is null || !AlwaysReturns(elseArm.BlockBody)) {
          return false;
        }

        hasElse = true;
        continue;
      }

      if (armNode is WhenArmSyntax whenArm) {
        if (whenArm.BlockBody is null || !AlwaysReturns(whenArm.BlockBody)) {
          return false;
        }

        foreach (PatternSyntax pattern in whenArm.Patterns) {
          if (pattern is VariantPatternSyntax varPat
              && (uint) varPat.Id < (uint) _state.ReferencedSymbols.Length) {
            Symbol? target = _state.ReferencedSymbols[varPat.Id];
            if (target is EnumMemberSymbol enumMember) {
              coveredEnumMembers.Add(enumMember);
            } else if (target is UnionVariantSymbol unionVariant && IsUnconditionalVariantPattern(varPat)) {
              coveredUnionVariants.Add(unionVariant);
            }
          }
        }
      }
    }

    return hasElse
        || IsAllEnumCovered(subjectType, coveredEnumMembers)
        || IsAllUnionCovered(subjectType, coveredUnionVariants);
  }

  private static bool IsUnconditionalVariantPattern(VariantPatternSyntax varPat) {
    foreach (PatternSyntax sub in varPat.SubPatterns) {
      if (sub is not (IdentifierPatternSyntax or LabelBindingPatternSyntax)) {
        return false;
      }
    }
    return true;
  }

  private static bool ContainsBreakTargetingLoop(BlockStatementSyntax loopBody) {
    var stack = new Stack<SyntaxNode>();
    stack.Push(loopBody);
    while (stack.Count > 0) {
      SyntaxNode current = stack.Pop();
      if (current is BreakStatementSyntax) {
        return true;
      }

      // Inner loops, nested functions, and lambdas catch their own `break` (or reject it).
      if (current is ForStatementSyntax
          or FunctionDeclarationSyntax
          or LambdaExpressionSyntax
          or StructDeclarationSyntax) {
        continue;
      }

      foreach (SyntaxElement elem in current.GetChildren()) {
        if (elem.IsNode) {
          stack.Push(elem.AsNode());
        }
      }
    }

    return false;
  }

  private static bool ContainsSyntaxRecovery(SyntaxNode root) {
    var stack = new Stack<SyntaxNode>();
    stack.Push(root);
    while (stack.Count > 0) {
      SyntaxNode current = stack.Pop();
      if (current.IsMissing
          || current is ErrorExpressionSyntax or ErrorStatementSyntax or ErrorMatchArmSyntax or ErrorMemberSyntax) {
        return true;
      }

      foreach (SyntaxElement elem in current.GetChildren()) {
        if (elem.IsToken) {
          if ((elem.AsToken().Flags & TokenFlags.Missing) != 0) {
            return true;
          }
        } else if (elem.IsNode) {
          stack.Push(elem.AsNode());
        }
      }
    }

    return false;
  }

  // ---------------------------------------------------------------------------------------------
  // Constant Evaluation & D-166 Diagnostics (TYPE-E1, VEC-E1..E3, LOOP-16, MATCH-11, OQ-7)
  // ---------------------------------------------------------------------------------------------

  private ConstantValue? TryEvalConstant(ExpressionSyntax expr) {
    if ((uint) expr.Id < (uint) _state.ConstantValues.Length
        && _state.ConstantValues[expr.Id] is { } cached) {
      return cached;
    }

    ConstantValue? evaluated = TryEvalConstantCore(expr);
    if (evaluated is not null && (uint) expr.Id < (uint) _state.ConstantValues.Length) {
      _state.ConstantValues[expr.Id] = evaluated;
    }

    return evaluated;
  }

  private ConstantValue? TryEvalConstantCore(ExpressionSyntax expr) {
    switch (expr) {
      case ParenthesizedExpressionSyntax paren:
        return TryEvalConstant(paren.Expression);

      case LiteralExpressionSyntax lit:
        return TryEvalLiteralConstant(lit);

      case InterpolatedStringExpressionSyntax interp:
        return TryEvalStringLiteralConstant(interp);

      case IdentifierExpressionSyntax id:
        // OQ-7 Option 1: fold immutable `val`s initialized with constant expressions, never `var`/param/loop var.
        if ((uint) id.Id < (uint) _state.ReferencedSymbols.Length
            && _state.ReferencedSymbols[id.Id] is LocalSymbol { IsMutable: false, Kind: LocalKind.Variable } valSym) {
          if (valSym.ConstantValue is { } localConst) {
            return localConst;
          }

          if ((uint) valSym.DeclaringNodeId < (uint) _state.ConstantValues.Length
              && _state.ConstantValues[valSym.DeclaringNodeId] is { } sameModuleConst) {
            return sameModuleConst;
          }
        }
        return null;

      case StaticAccessExpressionSyntax staticAccess:
        if ((uint) staticAccess.Id < (uint) _state.ReferencedSymbols.Length
            && _state.ReferencedSymbols[staticAccess.Id] is LocalSymbol { IsMutable: false, Kind: LocalKind.Variable, ConstantValue: { } modConst }) {
          return modConst;
        }
        return null;

      case UnaryExpressionSyntax unary: {
          ConstantValue? operand = TryEvalConstant(unary.Operand);
          if (operand is null) {
            return null;
          }

          if (unary.OperatorToken.Kind == TokenKind.Minus) {
            return operand.Kind switch {
              ConstantValueKind.Int => ConstantValue.FromInt(unchecked(-operand.IntValue)),
              ConstantValueKind.Float => ConstantValue.FromFloat(-operand.FloatValue),
              _ => null,
            };
          }

          if (unary.OperatorToken.Kind == TokenKind.Not && operand.Kind == ConstantValueKind.Bool) {
            return ConstantValue.FromBool(!operand.BoolValue);
          }

          return null;
        }

      case BinaryExpressionSyntax bin:
        return TryEvalBinaryConstant(bin);

      default:
        return null;
    }
  }

  private ConstantValue? TryEvalLiteralConstant(LiteralExpressionSyntax lit) {
    if (IsMissing(lit.Token)) {
      return null;
    }

    switch (lit.Token.Kind) {
      case TokenKind.TrueLiteral:
        return ConstantValue.FromBool(true);

      case TokenKind.FalseLiteral:
        return ConstantValue.FromBool(false);

      case TokenKind.IntLiteral: {
          string raw = NameOf(lit.Token).Replace("_", string.Empty, StringComparison.Ordinal);
          return long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out long val)
              ? ConstantValue.FromInt(val)
              : null;
        }

      case TokenKind.FloatLiteral: {
          string raw = NameOf(lit.Token).Replace("_", string.Empty, StringComparison.Ordinal);
          return double.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double val)
              ? ConstantValue.FromFloat(val)
              : null;
        }

      case TokenKind.RuneLiteral:
        return TryDecodeRuneLiteral(NameOf(lit.Token), out int cp)
            ? ConstantValue.FromRune(cp)
            : null;

      default:
        return null;
    }
  }

  private ConstantValue? TryEvalStringLiteralConstant(InterpolatedStringExpressionSyntax interp) {
    if (IsMissing(interp.StringEndToken)) {
      return null;
    }

    var sb = new StringBuilder();
    foreach (StringPartSyntax part in interp.Parts) {
      if (part is not StringTextPartSyntax textPart) {
        return null;
      }

      if (!TryUnescapeStringText(NameOf(textPart.TextToken), sb)) {
        return null;
      }
    }

    return ConstantValue.FromString(sb.ToString());
  }

  private ConstantValue? TryEvalBinaryConstant(BinaryExpressionSyntax bin) {
    ConstantValue? left = TryEvalConstant(bin.Left);
    ConstantValue? right = TryEvalConstant(bin.Right);
    if (left is null || right is null) {
      return null;
    }

    TokenKind op = bin.OperatorToken.Kind;

    if (op is TokenKind.And or TokenKind.Or) {
      if (left.Kind == ConstantValueKind.Bool && right.Kind == ConstantValueKind.Bool) {
        return ConstantValue.FromBool(op == TokenKind.And
            ? left.BoolValue && right.BoolValue
            : left.BoolValue || right.BoolValue);
      }
      return null;
    }

    if (op == TokenKind.Plus && left.Kind == ConstantValueKind.String && right.Kind == ConstantValueKind.String) {
      return ConstantValue.FromString((left.StringValue ?? string.Empty) + (right.StringValue ?? string.Empty));
    }

    if (op == TokenKind.Plus && left.Kind == ConstantValueKind.String && right.Kind == ConstantValueKind.Rune) {
      string suffix = Rune.IsValid(right.RuneValue) ? new Rune(right.RuneValue).ToString() : string.Empty;
      return ConstantValue.FromString((left.StringValue ?? string.Empty) + suffix);
    }

    if (left.Kind == ConstantValueKind.Int && right.Kind == ConstantValueKind.Int) {
      long l = left.IntValue;
      long r = right.IntValue;
      switch (op) {
        case TokenKind.Plus:
          return ConstantValue.FromInt(unchecked(l + r));
        case TokenKind.Minus:
          return ConstantValue.FromInt(unchecked(l - r));
        case TokenKind.Star:
          return ConstantValue.FromInt(unchecked(l * r));
        case TokenKind.Slash:
          return r == 0 ? null : ConstantValue.FromFloat((double) l / r);
        case TokenKind.SlashSlash:
          return r == 0 || (l == long.MinValue && r == -1) ? null : ConstantValue.FromInt(FloorDiv(l, r));
        case TokenKind.Percent:
          return r == 0 || (l == long.MinValue && r == -1) ? null : ConstantValue.FromInt(FloorMod(l, r));
        case TokenKind.EqualEqual or TokenKind.EqualEqualEqual:
          return ConstantValue.FromBool(l == r);
        case TokenKind.BangEqual or TokenKind.BangEqualEqual:
          return ConstantValue.FromBool(l != r);
        case TokenKind.Less:
          return ConstantValue.FromBool(l < r);
        case TokenKind.LessEqual:
          return ConstantValue.FromBool(l <= r);
        case TokenKind.Greater:
          return ConstantValue.FromBool(l > r);
        case TokenKind.GreaterEqual:
          return ConstantValue.FromBool(l >= r);
      }
    }

    if ((left.Kind is ConstantValueKind.Int or ConstantValueKind.Float)
        && (right.Kind is ConstantValueKind.Int or ConstantValueKind.Float)) {
      double ld = left.Kind == ConstantValueKind.Int ? left.IntValue : left.FloatValue;
      double rd = right.Kind == ConstantValueKind.Int ? right.IntValue : right.FloatValue;
      switch (op) {
        case TokenKind.Plus:
          return ConstantValue.FromFloat(ld + rd);
        case TokenKind.Minus:
          return ConstantValue.FromFloat(ld - rd);
        case TokenKind.Star:
          return ConstantValue.FromFloat(ld * rd);
        case TokenKind.Slash:
          return rd == 0.0 ? null : ConstantValue.FromFloat(ld / rd);
        case TokenKind.SlashSlash:
          return rd == 0.0 ? null : ConstantValue.FromFloat(Math.Floor(ld / rd));
        case TokenKind.Percent:
          return rd == 0.0 ? null : ConstantValue.FromFloat(ld - (Math.Floor(ld / rd) * rd));
        case TokenKind.EqualEqual or TokenKind.EqualEqualEqual:
          return ConstantValue.FromBool(ld == rd);
        case TokenKind.BangEqual or TokenKind.BangEqualEqual:
          return ConstantValue.FromBool(ld != rd);
        case TokenKind.Less:
          return ConstantValue.FromBool(ld < rd);
        case TokenKind.LessEqual:
          return ConstantValue.FromBool(ld <= rd);
        case TokenKind.Greater:
          return ConstantValue.FromBool(ld > rd);
        case TokenKind.GreaterEqual:
          return ConstantValue.FromBool(ld >= rd);
      }
    }

    return null;
  }

  private void CheckConstantDivisionByZero(SyntaxNode reportNode, ExpressionSyntax divisor) {
    ConstantValue? divisorConst = TryEvalConstant(divisor);
    if (divisorConst is { IsZero: true }) {
      Report(
          DiagnosticCodes.ConstantDivisionByZero,
          reportNode.Span,
          "Division by Zero: divisor is statically zero.");
    }
  }

  private void CheckVectorInitializerConstants(
      VectorInitializerExpressionSyntax initializer,
      ExpressionSyntax? lenExpr,
      ExpressionSyntax? capExpr) {
    ConstantValue? lenConst = lenExpr is not null ? TryEvalConstant(lenExpr) : null;
    ConstantValue? capConst = capExpr is not null ? TryEvalConstant(capExpr) : null;

    if (lenConst is { Kind: ConstantValueKind.Int, IntValue: < 0 }) {
      Report(
          DiagnosticCodes.NegativeVectorLength,
          lenExpr!.Span,
          $"Vector length 'len' cannot be negative (got {lenConst.IntValue}).");
    }

    if (capConst is { Kind: ConstantValueKind.Int, IntValue: < 0 }) {
      Report(
          DiagnosticCodes.NegativeVectorCapacity,
          capExpr!.Span,
          $"Vector capacity 'cap' cannot be negative (got {capConst.IntValue}).");
    }

    if (lenConst is { Kind: ConstantValueKind.Int, IntValue: >= 0 }
        && capConst is { Kind: ConstantValueKind.Int, IntValue: >= 0 }
        && capConst.IntValue < lenConst.IntValue) {
      Report(
          DiagnosticCodes.VectorCapacityBelowLength,
          initializer.Span,
          $"Vector capacity 'cap' ({capConst.IntValue}) cannot be less than length 'len' ({lenConst.IntValue}).");
    }
  }

  private void CheckConstantFalseLoop(ForConditionHeaderSyntax header) {
    if (_inPhase3a == 0
        && TryEvalConstant(header.Condition) is { Kind: ConstantValueKind.Bool, BoolValue: false }) {
      ReportUnnecessaryWarning(
          DiagnosticCodes.ConstantFalseLoop,
          header.Condition.Span,
          "Loop condition is always 'false'; the loop body will never execute.");
    }
  }

  private void ReportUnnecessaryWarning(string code, SourceSpan span, string message) =>
      _state.Diagnostics.ReportWarning(code, span, message, tags: DiagnosticTags.Unnecessary);

  private static bool ConstantValuesEqual(ConstantValue a, ConstantValue b) {
    if (a.Kind == b.Kind) {
      return a.Kind switch {
        ConstantValueKind.Int => a.IntValue == b.IntValue,
        ConstantValueKind.Float => a.FloatValue == b.FloatValue,
        ConstantValueKind.Bool => a.BoolValue == b.BoolValue,
        ConstantValueKind.String => string.Equals(a.StringValue, b.StringValue, StringComparison.Ordinal),
        ConstantValueKind.Rune => a.RuneValue == b.RuneValue,
        _ => false,
      };
    }

    if (a.Kind == ConstantValueKind.Int && b.Kind == ConstantValueKind.Float) {
      return a.IntValue == b.FloatValue;
    }

    if (a.Kind == ConstantValueKind.Float && b.Kind == ConstantValueKind.Int) {
      return a.FloatValue == b.IntValue;
    }

    return false;
  }

  private static long FloorDiv(long a, long b) {
    long q = a / b;
    long r = a % b;
    if (r != 0 && ((r < 0) != (b < 0))) {
      q--;
    }
    return q;
  }

  private static long FloorMod(long a, long b) {
    long r = a % b;
    if (r != 0 && ((r < 0) != (b < 0))) {
      r += b;
    }
    return r;
  }

  private static bool TryDecodeRuneLiteral(string text, out int codePoint) {
    codePoint = 0;
    if (text.Length < 3 || text[0] != '\'' || text[^1] != '\'') {
      return false;
    }

    string inner = text[1..^1];
    if (inner.StartsWith('\\')) {
      if (inner.Length != 2) {
        return false;
      }

      codePoint = inner[1] switch {
        'n' => '\n',
        't' => '\t',
        'r' => '\r',
        '\'' => '\'',
        '\\' => '\\',
        _ => -1,
      };
      return codePoint >= 0;
    }

    if (Rune.DecodeFromUtf16(inner, out Rune rune, out int consumed) == System.Buffers.OperationStatus.Done
        && consumed == inner.Length) {
      codePoint = rune.Value;
      return true;
    }

    return false;
  }

  private static bool TryUnescapeStringText(string text, StringBuilder sb) {
    for (int i = 0; i < text.Length; i++) {
      char ch = text[i];
      if (ch == '\\' && i + 1 < text.Length) {
        char next = text[++i];
        sb.Append(next switch {
          'n' => '\n',
          't' => '\t',
          'r' => '\r',
          '"' => '"',
          '\\' => '\\',
          '$' => '$',
          _ => next,
        });
      } else {
        sb.Append(ch);
      }
    }

    return true;
  }
}
