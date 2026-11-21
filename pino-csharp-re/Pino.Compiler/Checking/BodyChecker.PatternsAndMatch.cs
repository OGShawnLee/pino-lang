using System.Collections.Immutable;
using System.Text;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  // ---------------------------------------------------------------------------------------------
  // Match Statements & Expressions (MATCH-1..11, MATCH-E1..E8, D-031, D-034, D-143, D-166, D-169)
  // ---------------------------------------------------------------------------------------------

  private void CheckMatchStatement(MatchStatementSyntax match) {
    CheckMatchCore(
        match.Span,
        match.Subject,
        match.Arms,
        isExpression: false,
        expectedType: null);
  }

  private TypeSymbol CheckMatchExpression(MatchExpressionSyntax match, TypeSymbol? expectedType) {
    return CheckMatchCore(
        match.Span,
        match.Subject,
        match.Arms,
        isExpression: true,
        expectedType: expectedType);
  }

  private TypeSymbol CheckMatchCore(
      SourceSpan matchSpan,
      ExpressionSyntax subject,
      ImmutableArray<SyntaxNode> arms,
      bool isExpression,
      TypeSymbol? expectedType) {
    TypeSymbol rawSubjectType = Synthesize(subject);
    bool isPendingSubject = rawSubjectType is PendingReturnType;
    TypeSymbol subjectType = rawSubjectType;

    if (!isPendingSubject && subjectType.IsVoid) {
      Report(
          DiagnosticCodes.TypeMismatch,
          subject.Span,
          "Cannot match on a value of type 'void'.");
      subjectType = _types.Error;
    }

    bool subjectIsSilent = isPendingSubject || IsSilent(subjectType);
    ConstantValue? subjectConst = !subjectIsSilent ? TryEvalConstant(subject) : null;

    var coveredEnumMembers = new HashSet<EnumMemberSymbol>(ReferenceEqualityComparer.Instance);
    var coveredUnionVariants = new HashSet<UnionVariantSymbol>(ReferenceEqualityComparer.Instance);
    var whenVariantPatterns = new List<VariantPatternSyntax>();
    var seenLiterals = new List<ConstantValue>();
    var seenNestedVariantLiterals = new HashSet<string>(StringComparer.Ordinal);
    bool hasElse = false;
    bool hasRecovery = false;

    var armResults = isExpression
        ? new List<(SyntaxNode Arm, ExpressionSyntax? ExprBody, List<(ExpressionSyntax Expr, TypeSymbol Type)>? YieldExprs, TypeSymbol Type)>(arms.Length)
        : null;

    foreach (SyntaxNode armNode in arms) {
      if (armNode is ErrorMatchArmSyntax) {
        hasRecovery = true;
        continue;
      }

      if (armNode is ElseArmSyntax elseArm) {
        bool alreadyCovered = hasElse
            || IsAllEnumCovered(subjectType, coveredEnumMembers)
            || IsAllUnionCovered(subjectType, coveredUnionVariants, whenVariantPatterns);
        if (alreadyCovered && _inPhase3a == 0) {
          ReportUnnecessaryWarning(
              DiagnosticCodes.UnreachableMatchArm,
              elseArm.ElseKeyword.Span,
              "Match arm is statically unreachable because all cases are already covered.");
        }

        hasElse = true;
        Scope elseScope = ScopeOf(elseArm, ScopeKind.MatchArm);
        using ScopeRestore restore = EnterScope(elseScope);
        TypeSymbol elseType = CheckMatchArmBody(
            elseArm.ExpressionBody,
            elseArm.BlockBody,
            isExpression,
            expectedType,
            out List<(ExpressionSyntax Expr, TypeSymbol Type)>? yieldExprs);
        armResults?.Add((elseArm, elseArm.ExpressionBody, yieldExprs, elseType));
        continue;
      }

      if (armNode is not WhenArmSyntax whenArm) {
        continue;
      }

      bool allCoveredBeforeArm = hasElse
          || IsAllEnumCovered(subjectType, coveredEnumMembers)
          || IsAllUnionCovered(subjectType, coveredUnionVariants);

      Scope armScope = ScopeOf(whenArm, ScopeKind.MatchArm);
      var armBindings = new List<LocalSymbol>();
      bool allowBindings = whenArm.Patterns.Length == 1;
      bool armHasAnyPattern = false;
      bool armHadConstantSubjectMatch = false;
      bool armAllLiteralsConstant = true;
      bool armReportedUnreachable = false;

      if (allCoveredBeforeArm && _inPhase3a == 0) {
        ReportUnnecessaryWarning(
            DiagnosticCodes.UnreachableMatchArm,
            whenArm.WhenKeyword.Span,
            hasElse
                ? "Match arm is statically unreachable because it appears after an 'else' arm."
                : "Match arm is statically unreachable because all cases are already covered.");
        armReportedUnreachable = true;
      }

      foreach (PatternSyntax pattern in whenArm.Patterns) {
        armHasAnyPattern = true;
        if (pattern is MissingPatternSyntax) {
          hasRecovery = true;
          armAllLiteralsConstant = false;
          continue;
        }

        if (pattern is IdentifierPatternSyntax idPat) {
          armAllLiteralsConstant = false;
          if (IsMissing(idPat.IdentifierToken)) {
            hasRecovery = true;
          } else {
            string name = NameOf(idPat.IdentifierToken);
            Report(
                DiagnosticCodes.BareIdentifierPatternNotAllowed,
                idPat.Span,
                $"Cannot use bare identifier '{name}' as a 'when' pattern; use 'else' for a catch-all branch.",
                help: "replace this 'when' arm with 'else' if you intended a catch-all branch");
          }
          continue;
        }

        if (pattern is LiteralPatternSyntax litPat) {
          TypeSymbol litType = Synthesize(litPat.Literal);
          if (!subjectIsSilent && !IsSilent(litType)) {
            bool compatible = ReferenceEquals(litType, subjectType)
                || (ReferenceEquals(litType, _types.Int) && ReferenceEquals(subjectType, _types.Float));
            if (!compatible) {
              Report(
                  DiagnosticCodes.TypeMismatch,
                  litPat.Span,
                  $"Pattern of type '{litType.DisplayName}' cannot match subject of type '{subjectType.DisplayName}'.");
            }
          }

          _state.SetNodeType(litPat, litType);

          ConstantValue? patConst = TryEvalConstant(litPat.Literal);
          if (patConst is not null) {
            bool duplicate = false;
            foreach (ConstantValue seen in seenLiterals) {
              if (ConstantValuesEqual(seen, patConst)) {
                duplicate = true;
                break;
              }
            }

            if (duplicate) {
              if (!armReportedUnreachable && _inPhase3a == 0) {
                ReportUnnecessaryWarning(
                    DiagnosticCodes.UnreachableMatchArm,
                    litPat.Span,
                    "Match pattern is statically unreachable because it duplicates an earlier pattern.");
                armReportedUnreachable = true;
              }
            } else {
              seenLiterals.Add(patConst);
            }

            if (subjectConst is not null && ConstantValuesEqual(subjectConst, patConst)) {
              armHadConstantSubjectMatch = true;
            }
          } else {
            armAllLiteralsConstant = false;
          }

          continue;
        }

        if (pattern is LabelBindingPatternSyntax labelPat) {
          armAllLiteralsConstant = false;
          Report(
              DiagnosticCodes.MixedPatternBindings,
              labelPat.Span,
              "Label binding pattern is only allowed inside a union variant pattern.");
          continue;
        }

        if (pattern is VariantPatternSyntax varPat) {
          armAllLiteralsConstant = false;
          whenVariantPatterns.Add(varPat);
          CheckWhenVariantPattern(
              varPat,
              subjectType,
              subjectIsSilent,
              allowBindings,
              armScope,
              armBindings,
              coveredEnumMembers,
              coveredUnionVariants,
              seenNestedVariantLiterals,
              ref armReportedUnreachable,
              ref hasRecovery);
        }
      }

      if (!armReportedUnreachable
          && _inPhase3a == 0
          && subjectConst is not null
          && armHasAnyPattern
          && armAllLiteralsConstant
          && !armHadConstantSubjectMatch) {
        ReportUnnecessaryWarning(
            DiagnosticCodes.UnreachableMatchArm,
            whenArm.Span,
            "Match arm is statically unreachable because the constant subject never matches.");
      }

      if (!allowBindings && armBindings.Count > 0) {
        Report(
            DiagnosticCodes.MixedPatternBindings,
            whenArm.Span,
            "Pattern bindings are not allowed in multi-pattern 'when' arms.");
      } else {
        foreach (LocalSymbol binding in armBindings) {
          if (!armScope.TryDeclare(binding, out Symbol? existing)) {
            CheckerSession.ReportDuplicate(_state, binding.Name, binding.NameSpan, existing!);
          }
        }
      }

      using (EnterScope(armScope)) {
        TypeSymbol armType = CheckMatchArmBody(
            whenArm.ExpressionBody,
            whenArm.BlockBody,
            isExpression,
            expectedType,
            out List<(ExpressionSyntax Expr, TypeSymbol Type)>? yieldExprs);
        armResults?.Add((whenArm, whenArm.ExpressionBody, yieldExprs, armType));
      }
    }

    // D-031 exhaustiveness check (suppressed under A4 when subject is silent/error or recovery exists).
    if (!hasElse && !subjectIsSilent && !hasRecovery) {
      string stmtOrExpr = isExpression ? "expression" : "statement";
      switch (subjectType) {
        case EnumType enumType: {
            var missing = new List<string>();
            foreach (EnumMemberSymbol member in enumType.Definition.Members) {
              if (!coveredEnumMembers.Contains(member)) {
                missing.Add(member.Name);
              }
            }

            if (missing.Count > 0) {
              // Conformance MATCH-E1 / ENUM-E1 uses "Match statement on enum '...' is not exhaustive" even in expression form.
              Report(
                  DiagnosticCodes.NonExhaustiveMatch,
                  matchSpan,
                  $"Match statement on enum '{enumType.Definition.Name}' is not exhaustive. Missing member(s): {string.Join(", ", missing)}");
            }
            break;
          }

        case UnionType unionType: {
            var missing = new List<string>();
            foreach (UnionVariantSymbol variant in unionType.Definition.Variants) {
              if (!coveredUnionVariants.Contains(variant)
                  && !IsVariantRecursivelyCovered(unionType, variant, whenVariantPatterns)) {
                missing.Add(variant.Name);
              }
            }

            if (missing.Count > 0) {
              Report(
                  DiagnosticCodes.NonExhaustiveMatch,
                  matchSpan,
                  $"Match statement on union '{unionType.Definition.Name}' is not exhaustive. Missing variant(s): {string.Join(", ", missing)}");
            }
            break;
          }

        default:
          Report(
              DiagnosticCodes.NonExhaustiveMatch,
              matchSpan,
              $"Match {stmtOrExpr} on type '{subjectType.DisplayName}' is not exhaustive. Provide an 'else' fallback branch.");
          break;
      }
    }

    if (!isExpression) {
      return _types.Void;
    }

    if (isPendingSubject) {
      return rawSubjectType;
    }

    return JoinMatchExpressionArms(armResults!);
  }

  private bool IsAllUnionCovered(
      TypeSymbol subjectType,
      HashSet<UnionVariantSymbol> coveredUnionVariants,
      List<VariantPatternSyntax> whenVariantPatterns) {
    if (subjectType is not UnionType unionType || unionType.Definition.Variants.IsEmpty) {
      return false;
    }

    foreach (UnionVariantSymbol variant in unionType.Definition.Variants) {
      if (!coveredUnionVariants.Contains(variant)
          && !IsVariantRecursivelyCovered(unionType, variant, whenVariantPatterns)) {
        return false;
      }
    }

    return true;
  }

  private bool IsVariantRecursivelyCovered(
      UnionType unionType,
      UnionVariantSymbol variant,
      List<VariantPatternSyntax> topLevelPatterns) {
    var rows = new List<PatternSyntax?[]>();
    foreach (VariantPatternSyntax vp in topLevelPatterns) {
      if ((uint) vp.Id < (uint) _state.ReferencedSymbols.Length
          && ReferenceEquals(_state.ReferencedSymbols[vp.Id], variant)) {
        rows.Add(ExtractVariantFieldPatterns(vp, variant));
      }
    }

    if (rows.Count == 0) {
      return false;
    }

    var colTypes = ImmutableArray.CreateBuilder<TypeSymbol>(variant.Fields.Length);
    foreach (UnionVariantField field in variant.Fields) {
      colTypes.Add(TypeSubstitution.Substitute(
          field.Type,
          unionType.Definition.TypeParameters,
          unionType.TypeArguments,
          _types));
    }

    return IsPatternMatrixExhaustive(rows, colTypes.MoveToImmutable(), depth: 0);
  }

  private static PatternSyntax?[] ExtractVariantFieldPatterns(VariantPatternSyntax vp, UnionVariantSymbol variant) {
    var row = new PatternSyntax?[variant.Fields.Length];
    if (vp.SubPatterns.IsEmpty || vp.SubPatterns.Any(static s => s is LabelBindingPatternSyntax)) {
      return row;
    }

    for (int i = 0; i < variant.Fields.Length && i < vp.SubPatterns.Length; i++) {
      row[i] = vp.SubPatterns[i];
    }

    return row;
  }

  private bool IsPatternMatrixExhaustive(
      List<PatternSyntax?[]> rows,
      ImmutableArray<TypeSymbol> colTypes,
      int depth) {
    if (rows.Count == 0 || depth > 16) {
      return false;
    }

    foreach (PatternSyntax?[] row in rows) {
      bool allWild = true;
      for (int c = 0; c < colTypes.Length; c++) {
        if (row[c] is not (null or IdentifierPatternSyntax)) {
          allWild = false;
          break;
        }
      }

      if (allWild) {
        return true;
      }
    }

    int splitCol = -1;
    for (int c = 0; c < colTypes.Length; c++) {
      if (colTypes[c] is EnumType or UnionType
          && rows.Any(r => r[c] is VariantPatternSyntax)) {
        splitCol = c;
        break;
      }
    }

    if (splitCol < 0) {
      return false;
    }

    if (colTypes[splitCol] is EnumType enumCol) {
      if (enumCol.Definition.Members.IsEmpty) {
        return false;
      }

      ImmutableArray<TypeSymbol> nextTypes = RemoveColumn(colTypes, splitCol);
      foreach (EnumMemberSymbol member in enumCol.Definition.Members) {
        var subRows = new List<PatternSyntax?[]>();
        foreach (PatternSyntax?[] row in rows) {
          PatternSyntax? cell = row[splitCol];
          bool matches = cell is null or IdentifierPatternSyntax
              || (cell is VariantPatternSyntax vp
                  && (uint) vp.Id < (uint) _state.ReferencedSymbols.Length
                  && ReferenceEquals(_state.ReferencedSymbols[vp.Id], member));
          if (matches) {
            subRows.Add(ExpandRow(row, splitCol, []));
          }
        }

        if (!IsPatternMatrixExhaustive(subRows, nextTypes, depth + 1)) {
          return false;
        }
      }

      return true;
    }

    if (colTypes[splitCol] is UnionType unionCol) {
      if (unionCol.Definition.Variants.IsEmpty) {
        return false;
      }

      foreach (UnionVariantSymbol variant in unionCol.Definition.Variants) {
        var expandedFieldTypes = new TypeSymbol[variant.Fields.Length];
        for (int i = 0; i < variant.Fields.Length; i++) {
          expandedFieldTypes[i] = TypeSubstitution.Substitute(
              variant.Fields[i].Type,
              unionCol.Definition.TypeParameters,
              unionCol.TypeArguments,
              _types);
        }

        ImmutableArray<TypeSymbol> nextTypes = ReplaceColumn(colTypes, splitCol, expandedFieldTypes);
        var subRows = new List<PatternSyntax?[]>();
        foreach (PatternSyntax?[] row in rows) {
          PatternSyntax? cell = row[splitCol];
          if (cell is null or IdentifierPatternSyntax) {
            subRows.Add(ExpandRow(row, splitCol, new PatternSyntax?[variant.Fields.Length]));
          } else if (cell is VariantPatternSyntax vp
              && (uint) vp.Id < (uint) _state.ReferencedSymbols.Length
              && ReferenceEquals(_state.ReferencedSymbols[vp.Id], variant)) {
            subRows.Add(ExpandRow(row, splitCol, ExtractVariantFieldPatterns(vp, variant)));
          }
        }

        if (!IsPatternMatrixExhaustive(subRows, nextTypes, depth + 1)) {
          return false;
        }
      }

      return true;
    }

    return false;
  }

  private static PatternSyntax?[] ExpandRow(PatternSyntax?[] row, int col, PatternSyntax?[] inserted) {
    var result = new PatternSyntax?[row.Length - 1 + inserted.Length];
    for (int i = 0; i < col; i++) {
      result[i] = row[i];
    }

    for (int i = 0; i < inserted.Length; i++) {
      result[col + i] = inserted[i];
    }

    for (int i = col + 1; i < row.Length; i++) {
      result[i - 1 + inserted.Length] = row[i];
    }

    return result;
  }

  private static ImmutableArray<TypeSymbol> RemoveColumn(ImmutableArray<TypeSymbol> cols, int col) {
    var builder = ImmutableArray.CreateBuilder<TypeSymbol>(cols.Length - 1);
    for (int i = 0; i < cols.Length; i++) {
      if (i != col) {
        builder.Add(cols[i]);
      }
    }
    return builder.MoveToImmutable();
  }

  private static ImmutableArray<TypeSymbol> ReplaceColumn(
      ImmutableArray<TypeSymbol> cols,
      int col,
      TypeSymbol[] inserted) {
    var builder = ImmutableArray.CreateBuilder<TypeSymbol>(cols.Length - 1 + inserted.Length);
    for (int i = 0; i < col; i++) {
      builder.Add(cols[i]);
    }

    builder.AddRange(inserted);

    for (int i = col + 1; i < cols.Length; i++) {
      builder.Add(cols[i]);
    }
    return builder.MoveToImmutable();
  }

  private TypeSymbol CheckMatchArmBody(
      ExpressionSyntax? expressionBody,
      BlockStatementSyntax? blockBody,
      bool isExpression,
      TypeSymbol? expectedType,
      out List<(ExpressionSyntax Expr, TypeSymbol Type)>? yieldExprs) {
    yieldExprs = null;
    if (expressionBody is not null) {
      if (isExpression && expectedType is not null && !expectedType.IsError && !expectedType.IsVoid) {
        return CheckWithContextualExpectation(expressionBody, expectedType);
      }

      return Synthesize(expressionBody);
    }

    if (blockBody is null) {
      return _types.Error;
    }

    if (!isExpression) {
      CheckBlock(blockBody);
      return _types.Void;
    }

    YieldContext? prevYield = _activeYieldContext;
    TypeSymbol? contextual = expectedType is not null && !expectedType.IsError && !expectedType.IsVoid
        ? expectedType
        : null;
    var yieldCtx = new YieldContext(contextual, isMatchArm: true);
    _activeYieldContext = yieldCtx;
    try {
      CheckBlock(blockBody);
    } finally {
      _activeYieldContext = prevYield;
    }

    if (yieldCtx.Yields.Count == 0) {
      if (AlwaysReturns(blockBody)) {
        return _types.Never;
      }

      if (!ContainsSyntaxRecovery(blockBody)) {
        Report(
            DiagnosticCodes.YieldOutsideValidContext,
            blockBody.Span,
            "Match expression block arm must end with 'yield', 'return', or 'panic'.");
      }

      return _types.Error;
    }

    yieldExprs = new List<(ExpressionSyntax Expr, TypeSymbol Type)>(yieldCtx.Yields.Count);
    foreach ((_, ExpressionSyntax expr, TypeSymbol type) in yieldCtx.Yields) {
      yieldExprs.Add((expr, type));
    }

    return JoinMatchArmYields(yieldExprs);
  }

  private TypeSymbol JoinMatchArmYields(List<(ExpressionSyntax Expr, TypeSymbol Type)> yields) {
    TypeSymbol? joined = null;
    bool sawError = false;
    bool sawNever = false;

    foreach ((ExpressionSyntax expr, TypeSymbol type) in yields) {
      if (type is PendingReturnType) {
        return type;
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
      } else if (ReferenceEquals(joined, type)) {
        // identical
      } else if (ReferenceEquals(joined, _types.Int) && ReferenceEquals(type, _types.Float)) {
        joined = _types.Float;
      } else if (ReferenceEquals(joined, _types.Float) && ReferenceEquals(type, _types.Int)) {
        // already float
      } else {
        Report(
            DiagnosticCodes.BranchTypeMismatch,
            expr.Span,
            $"Match expression arms have incompatible types '{joined.DisplayName}' and '{type.DisplayName}'.");
        sawError = true;
      }
    }

    if (joined is null) {
      return sawError ? _types.Error : (sawNever ? _types.Never : _types.Error);
    }

    if (ReferenceEquals(joined, _types.Float)) {
      foreach ((ExpressionSyntax expr, TypeSymbol type) in yields) {
        if (ReferenceEquals(type, _types.Int)) {
          RecordIntToFloat(expr);
        }
      }
    }

    return joined;
  }

  private TypeSymbol JoinMatchExpressionArms(
      List<(SyntaxNode Arm, ExpressionSyntax? ExprBody, List<(ExpressionSyntax Expr, TypeSymbol Type)>? YieldExprs, TypeSymbol Type)> armResults) {
    if (armResults.Count == 0) {
      return _types.Error;
    }

    TypeSymbol? joined = null;
    TypeSymbol? pending = null;
    bool sawError = false;
    bool sawNever = false;

    foreach ((SyntaxNode arm, _, _, TypeSymbol type) in armResults) {
      if (type is PendingReturnType) {
        pending ??= type;
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
      } else if (ReferenceEquals(joined, type)) {
        // identical
      } else if (ReferenceEquals(joined, _types.Int) && ReferenceEquals(type, _types.Float)) {
        joined = _types.Float;
      } else if (ReferenceEquals(joined, _types.Float) && ReferenceEquals(type, _types.Int)) {
        // already float
      } else {
        Report(
            DiagnosticCodes.BranchTypeMismatch,
            arm.Span,
            $"Match expression arms have incompatible types '{joined.DisplayName}' and '{type.DisplayName}'.");
        sawError = true;
      }
    }

    if (joined is null) {
      return pending ?? (sawError ? _types.Error : (sawNever ? _types.Never : _types.Error));
    }

    if (ReferenceEquals(joined, _types.Float)) {
      foreach ((_, ExpressionSyntax? exprBody, List<(ExpressionSyntax Expr, TypeSymbol Type)>? yieldExprs, TypeSymbol type) in armResults) {
        if (ReferenceEquals(type, _types.Int)) {
          if (exprBody is not null) {
            RecordIntToFloat(exprBody);
          } else if (yieldExprs is not null) {
            foreach ((ExpressionSyntax yieldExpr, TypeSymbol yieldType) in yieldExprs) {
              if (ReferenceEquals(yieldType, _types.Int)) {
                RecordIntToFloat(yieldExpr);
              }
            }
          }
        }
      }
    }

    return joined;
  }

  private void CheckWhenVariantPattern(
      VariantPatternSyntax varPat,
      TypeSymbol subjectType,
      bool subjectIsSilent,
      bool allowBindings,
      Scope armScope,
      List<LocalSymbol> armBindings,
      HashSet<EnumMemberSymbol> coveredEnumMembers,
      HashSet<UnionVariantSymbol> coveredUnionVariants,
      HashSet<string> seenNestedVariantLiterals,
      ref bool armReportedUnreachable,
      ref bool hasRecovery) {
    if (IsMissing(varPat.TypeToken) || IsMissing(varPat.VariantToken)) {
      hasRecovery = true;
      VisitPatternSubExpressionsSilently(varPat);
      return;
    }

    string typeName = NameOf(varPat.TypeToken);
    string variantName = NameOf(varPat.VariantToken);
    _scope.TryLookup(typeName, out Symbol? resolvedTypeSym);

    if (subjectIsSilent) {
      if (resolvedTypeSym is UnionSymbol u) {
        UnionVariantSymbol? v = FindUnionVariant(u, variantName);
        if (v is not null) {
          _state.SetReferencedSymbol(varPat, v);
          BindSilentVariantSubPatterns(varPat, v, armScope, armBindings);
        }
      }
      VisitPatternSubExpressionsSilently(varPat);
      return;
    }

    if (subjectType is EnumType enumType) {
      EnumSymbol enumSym = enumType.Definition;
      if (!string.Equals(typeName, enumSym.Name, StringComparison.Ordinal)
          && (resolvedTypeSym is not EnumSymbol otherEnum || !ReferenceEquals(otherEnum, enumSym))) {
        Report(
            DiagnosticCodes.InvalidIsPattern,
            varPat.Span,
            $"Cannot match pattern '{typeName}::{variantName}' against subject of type '{enumSym.Name}'.");
        VisitPatternSubExpressionsSilently(varPat);
        return;
      }

      if (varPat.TypeArguments is not null) {
        Report(
            DiagnosticCodes.TypeMismatch,
            varPat.TypeArguments.Span,
            $"Enum '{enumSym.Name}' is not generic and does not accept type arguments.");
      }

      EnumMemberSymbol? member = FindEnumMember(enumSym, variantName);
      if (member is null) {
        Report(
            DiagnosticCodes.UnknownEnumMember,
            varPat.VariantToken.Span,
            $"Enum '{enumSym.Name}' has no member '{variantName}'.");
        VisitPatternSubExpressionsSilently(varPat);
        return;
      }

      _state.SetReferencedSymbol(varPat, member);
      _state.SetNodeType(varPat, subjectType);

      if (varPat.OpenParenToken is not null) {
        Report(
            DiagnosticCodes.InvalidIsPattern,
            varPat.Span,
            $"Enum member '{enumSym.Name}::{member.Name}' cannot take pattern bindings.");
        VisitPatternSubExpressionsSilently(varPat);
      }

      if (!coveredEnumMembers.Add(member) && !armReportedUnreachable && _inPhase3a == 0) {
        ReportUnnecessaryWarning(
            DiagnosticCodes.UnreachableMatchArm,
            varPat.Span,
            $"Match pattern '{enumSym.Name}::{member.Name}' is statically unreachable because it duplicates an earlier pattern.");
        armReportedUnreachable = true;
      }

      return;
    }

    if (subjectType is not UnionType unionType) {
      Report(
          DiagnosticCodes.InvalidIsPattern,
          varPat.Span,
          $"Cannot match union pattern of type '{FormatPatternTypeName(varPat)}' against subject of type '{subjectType.DisplayName}'.");
      VisitPatternSubExpressionsSilently(varPat);
      return;
    }

    UnionSymbol unionSym = unionType.Definition;
    if (!string.Equals(typeName, unionSym.Name, StringComparison.Ordinal)
        && (resolvedTypeSym is not UnionSymbol otherUnion || !ReferenceEquals(otherUnion, unionSym))) {
      Report(
          DiagnosticCodes.InvalidIsPattern,
          varPat.Span,
          $"Cannot match union pattern of type '{FormatPatternTypeName(varPat)}' against subject of type '{subjectType.DisplayName}'.");
      VisitPatternSubExpressionsSilently(varPat);
      return;
    }

    if (!CheckExplicitPatternTypeArguments(varPat, unionType)) {
      VisitPatternSubExpressionsSilently(varPat);
      return;
    }

    UnionVariantSymbol? variant = FindUnionVariant(unionSym, variantName);
    if (variant is null) {
      Report(
          DiagnosticCodes.InvalidIsPattern,
          varPat.VariantToken.Span,
          $"Union '{unionSym.Name}' has no variant '{variantName}'.");
      VisitPatternSubExpressionsSilently(varPat);
      return;
    }

    _state.SetReferencedSymbol(varPat, variant);
    _state.SetNodeType(varPat, subjectType);

    bool isUnconditional = CheckVariantSubPatterns(
        varPat,
        unionType,
        variant,
        allowLabels: true,
        armScope,
        armBindings,
        out bool patternHadError,
        ref hasRecovery);

    if (coveredUnionVariants.Contains(variant)) {
      if (!armReportedUnreachable && _inPhase3a == 0) {
        ReportUnnecessaryWarning(
            DiagnosticCodes.UnreachableMatchArm,
            varPat.Span,
            $"Match pattern '{unionSym.Name}::{variant.Name}' is statically unreachable because this variant is already covered.");
        armReportedUnreachable = true;
      }
    } else if (isUnconditional && !patternHadError && (allowBindings || varPat.SubPatterns.IsEmpty)) {
      coveredUnionVariants.Add(variant);
    } else if (!isUnconditional && TryBuildLiteralVariantPatternKey(varPat, variant, out string key)) {
      if (!seenNestedVariantLiterals.Add(key) && !armReportedUnreachable && _inPhase3a == 0) {
        ReportUnnecessaryWarning(
            DiagnosticCodes.UnreachableMatchArm,
            varPat.Span,
            $"Match pattern for '{unionSym.Name}::{variant.Name}' is statically unreachable because it duplicates an earlier pattern.");
        armReportedUnreachable = true;
      }
    }
  }

  private bool CheckVariantSubPatterns(
      VariantPatternSyntax varPat,
      UnionType unionType,
      UnionVariantSymbol variant,
      bool allowLabels,
      Scope bindingOwnerScope,
      List<LocalSymbol> collectedBindings,
      out bool hadError,
      ref bool hasRecovery) {
    hadError = false;
    UnionSymbol unionSym = unionType.Definition;
    ImmutableArray<TypeSymbol> typeArgs = unionType.TypeArguments;
    ImmutableArray<PatternSyntax> subs = varPat.SubPatterns;

    if (subs.IsEmpty) {
      _state.PatternBindings[varPat.Id] = new PatternBindingInfo(variant, []);
      return true;
    }

    bool hasLabel = false;
    bool hasNonLabel = false;
    foreach (PatternSyntax sub in subs) {
      if (sub is MissingPatternSyntax) {
        hasRecovery = true;
        continue;
      }

      if (sub is LabelBindingPatternSyntax) {
        hasLabel = true;
      } else {
        hasNonLabel = true;
      }
    }

    // UNION-E9: cannot mix positional and label bindings in one pattern.
    if (hasLabel && hasNonLabel) {
      hadError = true;
      Report(
          DiagnosticCodes.MixedPatternBindings,
          varPat.Span,
          $"Cannot mix positional and label bindings in pattern for '{unionSym.Name}::{variant.Name}'.");

      // A4: bind every named identifier/label to ErrorType so the arm body doesn't cascade.
      foreach (PatternSyntax sub in subs) {
        BindPatternNamesAsError(sub, bindingOwnerScope, collectedBindings);
      }

      VisitPatternSubExpressionsSilently(varPat);
      _state.PatternBindings[varPat.Id] = new PatternBindingInfo(variant, []);
      return false;
    }

    var bindingEntries = ImmutableArray.CreateBuilder<PatternBindingEntry>(subs.Length);

    if (hasLabel) {
      // UNION-E11: label bindings require a variant declared with named fields.
      if (!unionSym.UsesNamedFields) {
        hadError = true;
        Report(
            DiagnosticCodes.LabelPatternOnPositionalVariant,
            varPat.Span,
            $"Label bindings for '{unionSym.Name}::{variant.Name}' require a variant declared with named fields.");
      }

      var seenLabels = new HashSet<string>(StringComparer.Ordinal);
      foreach (PatternSyntax sub in subs) {
        if (sub is MissingPatternSyntax) {
          hasRecovery = true;
          continue;
        }

        if (sub is not LabelBindingPatternSyntax labelPat) {
          continue;
        }

        if (IsMissing(labelPat.FieldToken)) {
          hasRecovery = true;
          continue;
        }

        string fieldName = NameOf(labelPat.FieldToken);
        Token bindToken = labelPat.NameToken ?? labelPat.FieldToken;
        bool bindNameMissing = IsMissing(bindToken);
        if (bindNameMissing) {
          hasRecovery = true;
        }

        if (!seenLabels.Add(fieldName)) {
          // UNION-E13 / OQ-22: duplicate label in same pattern.
          hadError = true;
          Report(
              DiagnosticCodes.DuplicatePatternFieldLabel,
              labelPat.Span,
              $"Pattern for '{unionSym.Name}::{variant.Name}' has duplicate label '@{fieldName}'.");
          if (!bindNameMissing) {
            AddFallbackPatternBinding(bindToken, bindingOwnerScope, collectedBindings, labelPat.Id);
          }
          continue;
        }

        UnionVariantField? matchedField = FindUnionVariantField(variant, fieldName);
        if (matchedField is null) {
          // UNION-E10: label is not a field of this variant.
          hadError = true;
          if (unionSym.UsesNamedFields) {
            Report(
                DiagnosticCodes.UnknownPatternFieldLabel,
                labelPat.Span,
                $"'{fieldName}' is not a field of variant '{unionSym.Name}::{variant.Name}'.");
          }
          if (!bindNameMissing) {
            AddFallbackPatternBinding(bindToken, bindingOwnerScope, collectedBindings, labelPat.Id);
          }
          continue;
        }

        TypeSymbol fieldType = unionSym.UsesNamedFields
            ? SubstituteUnionFieldType(matchedField.Type, unionSym, typeArgs)
            : _types.Error;
        _state.SetNodeType(labelPat, fieldType);

        if (!bindNameMissing) {
          string bindName = NameOf(bindToken);
          var localSym = new LocalSymbol(
              bindName,
              fieldType,
              isMutable: false,
              LocalKind.PatternBinding,
              labelPat.Id,
              bindToken.Span,
              _state.FilePath,
              bindingOwnerScope.Owner);
          _state.SetDeclaredSymbol(labelPat, localSym);
          collectedBindings.Add(localSym);
          if (unionSym.UsesNamedFields) {
            bindingEntries.Add(new PatternBindingEntry(matchedField.Ordinal, localSym));
          }
        }
      }

      _state.PatternBindings[varPat.Id] = new PatternBindingInfo(variant, bindingEntries.ToImmutable());
      return true;
    }

    // Positional sub-patterns (UNION-3, D-169, UNION-E12, UNION-E15).
    if (subs.Length > variant.Fields.Length) {
      hadError = true;
      Report(
          DiagnosticCodes.TooManyPatternBindings,
          varPat.Span,
          $"Pattern for '{unionSym.Name}::{variant.Name}' has too many bindings: expected at most {variant.Fields.Length}, got {subs.Length}.");
    }

    bool unconditional = true;
    for (int i = 0; i < subs.Length; i++) {
      PatternSyntax sub = subs[i];
      TypeSymbol fieldType = i < variant.Fields.Length
          ? SubstituteUnionFieldType(variant.Fields[i].Type, unionSym, typeArgs)
          : _types.Error;

      switch (sub) {
        case MissingPatternSyntax:
          hasRecovery = true;
          unconditional = false;
          break;

        case IdentifierPatternSyntax idPat:
          if (IsMissing(idPat.IdentifierToken)) {
            hasRecovery = true;
            unconditional = false;
            break;
          }

          string varName = NameOf(idPat.IdentifierToken);
          var localSym = new LocalSymbol(
              varName,
              fieldType,
              isMutable: false,
              LocalKind.PatternBinding,
              idPat.Id,
              idPat.IdentifierToken.Span,
              _state.FilePath,
              bindingOwnerScope.Owner);
          _state.SetDeclaredSymbol(idPat, localSym);
          _state.SetNodeType(idPat, fieldType);
          collectedBindings.Add(localSym);
          if (i < variant.Fields.Length) {
            bindingEntries.Add(new PatternBindingEntry(i, localSym));
          }
          break;

        case LiteralPatternSyntax litPat: {
            unconditional = false;
            TypeSymbol litType = Synthesize(litPat.Literal);
            _state.SetNodeType(litPat, litType);
            if (!IsSilent(fieldType) && !IsSilent(litType)) {
              bool compatible = ReferenceEquals(litType, fieldType)
                  || (ReferenceEquals(litType, _types.Int) && ReferenceEquals(fieldType, _types.Float));
              if (!compatible) {
                Report(
                    DiagnosticCodes.TypeMismatch,
                    litPat.Span,
                    $"Pattern of type '{litType.DisplayName}' cannot match field of type '{fieldType.DisplayName}'.");
              }
            }
            break;
          }

        case VariantPatternSyntax nestedVarPat: {
            unconditional = false;
            bool nestedUnconditional = CheckNestedVariantPattern(
                nestedVarPat,
                fieldType,
                allowLabels,
                bindingOwnerScope,
                collectedBindings,
                ref hasRecovery);
            _ = nestedUnconditional;
            break;
          }

        default:
          unconditional = false;
          break;
      }
    }

    _state.PatternBindings[varPat.Id] = new PatternBindingInfo(variant, bindingEntries.ToImmutable());
    return unconditional;
  }

  private bool CheckNestedVariantPattern(
      VariantPatternSyntax nestedVarPat,
      TypeSymbol fieldType,
      bool allowLabels,
      Scope bindingOwnerScope,
      List<LocalSymbol> collectedBindings,
      ref bool hasRecovery) {
    if (IsMissing(nestedVarPat.TypeToken) || IsMissing(nestedVarPat.VariantToken)) {
      hasRecovery = true;
      VisitPatternSubExpressionsSilently(nestedVarPat);
      return false;
    }

    if (IsSilent(fieldType)) {
      VisitPatternSubExpressionsSilently(nestedVarPat);
      return false;
    }

    string typeName = NameOf(nestedVarPat.TypeToken);
    string variantName = NameOf(nestedVarPat.VariantToken);
    _scope.TryLookup(typeName, out Symbol? resolvedTypeSym);

    if (fieldType is EnumType enumType) {
      EnumSymbol enumSym = enumType.Definition;
      if (!string.Equals(typeName, enumSym.Name, StringComparison.Ordinal)
          && (resolvedTypeSym is not EnumSymbol otherEnum || !ReferenceEquals(otherEnum, enumSym))) {
        Report(
            DiagnosticCodes.InvalidIsPattern,
            nestedVarPat.Span,
            $"Cannot match pattern '{typeName}::{variantName}' against field of type '{enumSym.Name}'.");
        VisitPatternSubExpressionsSilently(nestedVarPat);
        return false;
      }

      EnumMemberSymbol? member = FindEnumMember(enumSym, variantName);
      if (member is null) {
        Report(
            DiagnosticCodes.UnknownEnumMember,
            nestedVarPat.VariantToken.Span,
            $"Enum '{enumSym.Name}' has no member '{variantName}'.");
        return false;
      }

      _state.SetReferencedSymbol(nestedVarPat, member);
      _state.SetNodeType(nestedVarPat, fieldType);
      return true;
    }

    if (fieldType is not UnionType unionType) {
      Report(
          DiagnosticCodes.InvalidIsPattern,
          nestedVarPat.Span,
          $"Cannot match union pattern of type '{FormatPatternTypeName(nestedVarPat)}' against field of type '{fieldType.DisplayName}'.");
      VisitPatternSubExpressionsSilently(nestedVarPat);
      return false;
    }

    UnionSymbol unionSym = unionType.Definition;
    if (!string.Equals(typeName, unionSym.Name, StringComparison.Ordinal)
        && (resolvedTypeSym is not UnionSymbol otherUnion || !ReferenceEquals(otherUnion, unionSym))) {
      Report(
          DiagnosticCodes.InvalidIsPattern,
          nestedVarPat.Span,
          $"Cannot match union pattern of type '{FormatPatternTypeName(nestedVarPat)}' against field of type '{fieldType.DisplayName}'.");
      VisitPatternSubExpressionsSilently(nestedVarPat);
      return false;
    }

    if (!CheckExplicitPatternTypeArguments(nestedVarPat, unionType)) {
      VisitPatternSubExpressionsSilently(nestedVarPat);
      return false;
    }

    UnionVariantSymbol? variant = FindUnionVariant(unionSym, variantName);
    if (variant is null) {
      Report(
          DiagnosticCodes.InvalidIsPattern,
          nestedVarPat.VariantToken.Span,
          $"Union '{unionSym.Name}' has no variant '{variantName}'.");
      VisitPatternSubExpressionsSilently(nestedVarPat);
      return false;
    }

    _state.SetReferencedSymbol(nestedVarPat, variant);
    _state.SetNodeType(nestedVarPat, fieldType);

    return CheckVariantSubPatterns(
        nestedVarPat,
        unionType,
        variant,
        allowLabels,
        bindingOwnerScope,
        collectedBindings,
        out _,
        ref hasRecovery);
  }

  // ---------------------------------------------------------------------------------------------
  // `is` / `is not` Flow-Sensitive Bindings (UNION-15..19, UNION-23, ENUM-9, UNION-E5..E7, E15)
  // ---------------------------------------------------------------------------------------------

  /// <summary>IF-E4 / D-032: checks a condition and collects positive <c>is</c> bindings.</summary>
  private void CheckCondition(ExpressionSyntax condition, string what, out List<LocalSymbol> trueBindings) {
    trueBindings = [];
    TypeSymbol type = CheckConditionWithBindings(condition, trueBindings);
    if (!IsSilent(type) && type is not PendingReturnType && !ReferenceEquals(type, _types.Bool)) {
      Report(
          DiagnosticCodes.InvalidConditionType,
          condition.Span,
          $"The {what} must have type 'bool', but got '{type.DisplayName}'.");
    }
  }

  private TypeSymbol CheckConditionWithBindings(ExpressionSyntax expression, List<LocalSymbol>? trueBindings) {
    _cancellationToken.ThrowIfCancellationRequested();
    TypeSymbol resultType;
    switch (expression) {
      case ParenthesizedExpressionSyntax paren:
        resultType = CheckConditionWithBindings(paren.Expression, trueBindings);
        break;

      case IsPatternExpressionSyntax isPattern:
        resultType = CheckIsPattern(isPattern, isPattern.NotKeyword is null ? trueBindings : null);
        break;

      case BinaryExpressionSyntax { OperatorToken.Kind: TokenKind.And } andExpr: {
          var leftBindings = new List<LocalSymbol>();
          TypeSymbol left = CheckConditionWithBindings(andExpr.Left, leftBindings);

          TypeSymbol right;
          var rightBindings = new List<LocalSymbol>();
          if (leftBindings.Count > 0) {
            var andScope = new Scope(ScopeKind.Synthetic, _scope, _scope.Owner);
            foreach (LocalSymbol binding in leftBindings) {
              andScope.TryDeclare(binding, out _);
            }

            using (EnterScope(andScope)) {
              right = CheckConditionWithBindings(andExpr.Right, rightBindings);
            }
          } else {
            right = CheckConditionWithBindings(andExpr.Right, rightBindings);
          }

          resultType = CheckLogical(andExpr, left, right);
          if (trueBindings is not null) {
            trueBindings.AddRange(leftBindings);
            trueBindings.AddRange(rightBindings);
          }
          break;
        }

      case BinaryExpressionSyntax { OperatorToken.Kind: TokenKind.Or } orExpr: {
          TypeSymbol left = CheckConditionWithBindings(orExpr.Left, trueBindings: null);
          TypeSymbol right = CheckConditionWithBindings(orExpr.Right, trueBindings: null);
          resultType = CheckLogical(orExpr, left, right);
          break;
        }

      case UnaryExpressionSyntax { OperatorToken.Kind: TokenKind.Not } notExpr: {
          TypeSymbol operand = CheckConditionWithBindings(notExpr.Operand, trueBindings: null);
          if (operand is PendingReturnType) {
            resultType = operand;
          } else if (IsSilent(operand)) {
            resultType = _types.Error;
          } else if (ReferenceEquals(operand, _types.Bool)) {
            resultType = _types.Bool;
          } else {
            Report(
                DiagnosticCodes.InvalidOperatorOperands,
                notExpr.Span,
                $"Operator 'not' requires a 'bool' operand, but got '{operand.DisplayName}'.");
            resultType = _types.Error;
          }
          break;
        }

      default:
        return Synthesize(expression);
    }

    _state.SetNodeType(expression, resultType);
    return resultType;
  }

  private TypeSymbol CheckIsPattern(IsPatternExpressionSyntax isPat, List<LocalSymbol>? trueBindings) {
    TypeSymbol subjectType = Synthesize(isPat.Expression);
    if (subjectType is PendingReturnType) {
      VisitPatternSubExpressionsSilently(isPat.Pattern);
      return _types.Bool;
    }

    if (IsSilent(subjectType)) {
      VisitPatternSubExpressionsSilently(isPat.Pattern);
      return _types.Bool;
    }

    if (isPat.Pattern is MissingPatternSyntax) {
      return _types.Bool;
    }

    if (isPat.Pattern is not VariantPatternSyntax varPat) {
      Report(
          DiagnosticCodes.InvalidIsPattern,
          isPat.Pattern.Span,
          $"Cannot match union pattern against non-variant pattern.");
      VisitPatternSubExpressionsSilently(isPat.Pattern);
      return _types.Bool;
    }

    if (IsMissing(varPat.TypeToken) || IsMissing(varPat.VariantToken)) {
      VisitPatternSubExpressionsSilently(varPat);
      return _types.Bool;
    }

    string typeName = NameOf(varPat.TypeToken);
    string variantName = NameOf(varPat.VariantToken);
    _scope.TryLookup(typeName, out Symbol? resolvedTypeSym);

    if (subjectType is EnumType enumType) {
      EnumSymbol enumSym = enumType.Definition;
      if (!string.Equals(typeName, enumSym.Name, StringComparison.Ordinal)
          && (resolvedTypeSym is not EnumSymbol otherEnum || !ReferenceEquals(otherEnum, enumSym))) {
        Report(
            DiagnosticCodes.InvalidIsPattern,
            isPat.Span,
            $"Cannot match union pattern of type '{FormatPatternTypeName(varPat)}' against condition of type '{subjectType.DisplayName}'.");
        VisitPatternSubExpressionsSilently(varPat);
        return _types.Bool;
      }

      if (varPat.TypeArguments is not null) {
        Report(
            DiagnosticCodes.InvalidIsPattern,
            varPat.TypeArguments.Span,
            $"Enum '{enumSym.Name}' is not generic and does not accept type arguments.");
      }

      EnumMemberSymbol? member = FindEnumMember(enumSym, variantName);
      if (member is null) {
        Report(
            DiagnosticCodes.UnknownEnumMember,
            varPat.VariantToken.Span,
            $"Enum '{enumSym.Name}' has no member '{variantName}'.");
        VisitPatternSubExpressionsSilently(varPat);
        return _types.Bool;
      }

      _state.SetReferencedSymbol(varPat, member);
      _state.SetNodeType(varPat, subjectType);

      if (varPat.OpenParenToken is not null) {
        Report(
            DiagnosticCodes.InvalidIsPattern,
            varPat.Span,
            $"Enum member '{enumSym.Name}::{member.Name}' cannot take pattern bindings.");
        VisitPatternSubExpressionsSilently(varPat);
      }

      return _types.Bool;
    }

    if (subjectType is not UnionType unionType) {
      Report(
          DiagnosticCodes.InvalidIsPattern,
          isPat.Span,
          $"Cannot match union pattern of type '{FormatPatternTypeName(varPat)}' against condition of type '{subjectType.DisplayName}'.");
      VisitPatternSubExpressionsSilently(varPat);
      return _types.Bool;
    }

    UnionSymbol unionSym = unionType.Definition;
    if (!string.Equals(typeName, unionSym.Name, StringComparison.Ordinal)
        && (resolvedTypeSym is not UnionSymbol otherUnion || !ReferenceEquals(otherUnion, unionSym))) {
      Report(
          DiagnosticCodes.InvalidIsPattern,
          isPat.Span,
          $"Cannot match union pattern of type '{FormatPatternTypeName(varPat)}' against condition of type '{subjectType.DisplayName}'.");
      VisitPatternSubExpressionsSilently(varPat);
      return _types.Bool;
    }

    if (!CheckExplicitPatternTypeArguments(varPat, unionType, isConditionForm: true)) {
      VisitPatternSubExpressionsSilently(varPat);
      return _types.Bool;
    }

    UnionVariantSymbol? variant = FindUnionVariant(unionSym, variantName);
    if (variant is null) {
      Report(
          DiagnosticCodes.InvalidIsPattern,
          varPat.VariantToken.Span,
          $"Union '{unionSym.Name}' has no variant '{variantName}'.");
      VisitPatternSubExpressionsSilently(varPat);
      return _types.Bool;
    }

    _state.SetReferencedSymbol(varPat, variant);
    _state.SetNodeType(varPat, subjectType);

    var collected = new List<LocalSymbol>();
    bool dummyRecovery = false;
    CheckVariantSubPatterns(
        varPat,
        unionType,
        variant,
        allowLabels: false,
        _scope,
        collected,
        out _,
        ref dummyRecovery);

    if (isPat.NotKeyword is null && trueBindings is not null) {
      trueBindings.AddRange(collected);
    }

    return _types.Bool;
  }

  // ---------------------------------------------------------------------------------------------
  // Pattern helpers
  // ---------------------------------------------------------------------------------------------

  private bool CheckExplicitPatternTypeArguments(
      VariantPatternSyntax varPat,
      UnionType subjectUnion,
      bool isConditionForm = false) {
    if (varPat.TypeArguments is not { } typeArgsSyntax) {
      return true;
    }

    UnionSymbol unionSym = subjectUnion.Definition;
    bool isResult = ReferenceEquals(unionSym, _prelude.ResultUnion);
    var resolved = ImmutableArray.CreateBuilder<TypeSymbol>(typeArgsSyntax.Arguments.Length);
    for (int i = 0; i < typeArgsSyntax.Arguments.Length; i++) {
      TypePosition pos = isResult && i == 0
          ? TypePosition.ResultSuccessArg
          : TypePosition.GenericTypeArgument;
      resolved.Add(_session.ResolveType(_state, typeArgsSyntax.Arguments[i], _scope, pos));
    }

    if (resolved.Count != subjectUnion.TypeArguments.Length) {
      string targetLabel = isConditionForm ? "condition" : "subject";
      Report(
          DiagnosticCodes.InvalidIsPattern,
          varPat.Span,
          $"Cannot match union pattern of type '{FormatPatternTypeName(varPat)}' against {targetLabel} of type '{subjectUnion.DisplayName}'.");
      return false;
    }

    for (int i = 0; i < resolved.Count; i++) {
      TypeSymbol actualArg = resolved[i];
      TypeSymbol expectedArg = subjectUnion.TypeArguments[i];
      if (!actualArg.IsError && !expectedArg.IsError && !ReferenceEquals(actualArg, expectedArg)) {
        string targetLabel = isConditionForm ? "condition" : "subject";
        Report(
            DiagnosticCodes.InvalidIsPattern,
            varPat.Span,
            $"Cannot match union pattern of type '{FormatPatternTypeName(varPat)}' against {targetLabel} of type '{subjectUnion.DisplayName}'.");
        return false;
      }
    }

    return true;
  }

  private string FormatPatternTypeName(VariantPatternSyntax varPat) {
    string name = IsMissing(varPat.TypeToken) ? "<unknown>" : NameOf(varPat.TypeToken);
    if (varPat.TypeArguments is not { } typeArgs || typeArgs.Arguments.IsEmpty) {
      return name;
    }

    var sb = new StringBuilder(name);
    sb.Append('[');
    for (int i = 0; i < typeArgs.Arguments.Length; i++) {
      if (i > 0) {
        sb.Append(", ");
      }
      TypeSyntax arg = typeArgs.Arguments[i];
      TypeSymbol? resolved = (uint) arg.Id < (uint) _state.NodeTypes.Length ? _state.NodeTypes[arg.Id] : null;
      sb.Append(resolved is not null && !resolved.IsError ? resolved.DisplayName : "?");
    }
    sb.Append(']');
    return sb.ToString();
  }

  private TypeSymbol SubstituteUnionFieldType(
      TypeSymbol fieldType,
      UnionSymbol unionSym,
      ImmutableArray<TypeSymbol> typeArgs) =>
      unionSym.TypeParameters.IsEmpty || typeArgs.Length != unionSym.TypeParameters.Length
          ? fieldType
          : TypeSubstitution.Substitute(fieldType, unionSym.TypeParameters, typeArgs, _types);

  private static bool IsAllEnumCovered(TypeSymbol subjectType, HashSet<EnumMemberSymbol> covered) =>
      subjectType is EnumType et
      && et.Definition.Members.Length > 0
      && covered.Count >= et.Definition.Members.Length;

  private static bool IsAllUnionCovered(TypeSymbol subjectType, HashSet<UnionVariantSymbol> covered) =>
      subjectType is UnionType ut
      && ut.Definition.Variants.Length > 0
      && covered.Count >= ut.Definition.Variants.Length;

  private static EnumMemberSymbol? FindEnumMember(EnumSymbol enumSym, string name) {
    foreach (EnumMemberSymbol member in enumSym.Members) {
      if (string.Equals(member.Name, name, StringComparison.Ordinal)) {
        return member;
      }
    }
    return null;
  }

  private static UnionVariantField? FindUnionVariantField(UnionVariantSymbol variant, string name) {
    foreach (UnionVariantField field in variant.Fields) {
      if (string.Equals(field.Name, name, StringComparison.Ordinal)) {
        return field;
      }
    }
    return null;
  }

  private void AddFallbackPatternBinding(
      Token bindToken,
      Scope bindingOwnerScope,
      List<LocalSymbol> collectedBindings,
      int declaringNodeId) {
    string bindName = NameOf(bindToken);
    foreach (LocalSymbol existing in collectedBindings) {
      if (string.Equals(existing.Name, bindName, StringComparison.Ordinal)) {
        return;
      }
    }

    var errLocal = new LocalSymbol(
        bindName,
        _types.Error,
        isMutable: false,
        LocalKind.PatternBinding,
        declaringNodeId,
        bindToken.Span,
        _state.FilePath,
        bindingOwnerScope.Owner);
    collectedBindings.Add(errLocal);
  }

  private void BindPatternNamesAsError(
      PatternSyntax pattern,
      Scope bindingOwnerScope,
      List<LocalSymbol> collectedBindings) {
    switch (pattern) {
      case IdentifierPatternSyntax idPat when !IsMissing(idPat.IdentifierToken):
        AddFallbackPatternBinding(idPat.IdentifierToken, bindingOwnerScope, collectedBindings, idPat.Id);
        break;

      case LabelBindingPatternSyntax labelPat: {
          Token tok = labelPat.NameToken ?? labelPat.FieldToken;
          if (!IsMissing(tok)) {
            AddFallbackPatternBinding(tok, bindingOwnerScope, collectedBindings, labelPat.Id);
          }
          break;
        }

      case VariantPatternSyntax varPat:
        foreach (PatternSyntax sub in varPat.SubPatterns) {
          BindPatternNamesAsError(sub, bindingOwnerScope, collectedBindings);
        }
        break;
    }
  }

  private void BindSilentVariantSubPatterns(
      VariantPatternSyntax varPat,
      UnionVariantSymbol variant,
      Scope bindingOwnerScope,
      List<LocalSymbol> collectedBindings) {
    if (varPat.SubPatterns.IsEmpty) {
      return;
    }

    foreach (PatternSyntax sub in varPat.SubPatterns) {
      if (sub is IdentifierPatternSyntax idPat && !IsMissing(idPat.IdentifierToken)) {
        AddFallbackPatternBinding(idPat.IdentifierToken, bindingOwnerScope, collectedBindings, idPat.Id);
      } else if (sub is LabelBindingPatternSyntax labelPat) {
        Token tok = labelPat.NameToken ?? labelPat.FieldToken;
        if (!IsMissing(tok)) {
          AddFallbackPatternBinding(tok, bindingOwnerScope, collectedBindings, labelPat.Id);
        }
      }
    }
    _ = variant;
  }

  private void VisitPatternSubExpressionsSilently(PatternSyntax pattern) {
    switch (pattern) {
      case LiteralPatternSyntax litPat: {
          TypeSymbol t = Synthesize(litPat.Literal);
          _state.SetNodeType(litPat, t);
          break;
        }

      case VariantPatternSyntax varPat:
        foreach (PatternSyntax sub in varPat.SubPatterns) {
          VisitPatternSubExpressionsSilently(sub);
        }
        break;
    }
  }

  private bool TryBuildLiteralVariantPatternKey(
      VariantPatternSyntax varPat,
      UnionVariantSymbol variant,
      out string key) {
    var sb = new StringBuilder(variant.Name);
    sb.Append('(');
    for (int i = 0; i < varPat.SubPatterns.Length; i++) {
      if (i > 0) {
        sb.Append(',');
      }

      if (varPat.SubPatterns[i] is LiteralPatternSyntax litPat
          && TryEvalConstant(litPat.Literal) is { } c) {
        sb.Append(FormatConstantKey(c));
      } else {
        key = string.Empty;
        return false;
      }
    }
    sb.Append(')');
    key = sb.ToString();
    return true;
  }

  private static string FormatConstantKey(ConstantValue c) => c.Kind switch {
    ConstantValueKind.Int => $"i:{c.IntValue}",
    ConstantValueKind.Float => $"f:{c.FloatValue:R}",
    ConstantValueKind.Bool => $"b:{c.BoolValue}",
    ConstantValueKind.String => $"s:{c.StringValue}",
    ConstantValueKind.Rune => $"r:{c.RuneValue}",
    _ => string.Empty,
  };
}
