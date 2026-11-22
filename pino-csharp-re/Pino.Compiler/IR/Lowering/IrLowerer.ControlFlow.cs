using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.IR;

public sealed partial class IrLowerer {
  // ---------------------------------------------------------------------------
  // Conditions & `is` / `is not` (IF-*, UNION-15..23, TYPE-6)
  // ---------------------------------------------------------------------------

  private void LowerCondition(
      FunctionBuilder fb,
      ExpressionSyntax condition,
      IrBlockId trueTarget,
      IrBlockId falseTarget) {
    if (fb.IsTerminated) {
      return;
    }

    ExpressionSyntax unwrapped = UnwrapParens(condition);
    switch (unwrapped) {
      case BinaryExpressionSyntax { OperatorToken.Kind: TokenKind.And } andExpr: {
          IrBasicBlock bbRhs = fb.NewBlock();
          LowerCondition(fb, andExpr.Left, bbRhs.Id, falseTarget);
          fb.SwitchTo(bbRhs);
          LowerCondition(fb, andExpr.Right, trueTarget, falseTarget);
          return;
        }

      case BinaryExpressionSyntax { OperatorToken.Kind: TokenKind.Or } orExpr: {
          IrBasicBlock bbRhs = fb.NewBlock();
          LowerCondition(fb, orExpr.Left, trueTarget, bbRhs.Id);
          fb.SwitchTo(bbRhs);
          LowerCondition(fb, orExpr.Right, trueTarget, falseTarget);
          return;
        }

      case UnaryExpressionSyntax { OperatorToken.Kind: TokenKind.Not } notExpr:
        LowerCondition(fb, notExpr.Operand, falseTarget, trueTarget);
        return;

      case IsPatternExpressionSyntax isPat:
        LowerIsCondition(fb, isPat, trueTarget, falseTarget);
        return;

      default: {
          IrOperand? condOpt = LowerExpression(fb, unwrapped);
          if (fb.IsTerminated || condOpt is not { } cond) {
            return;
          }

          fb.Terminate(new BrIf(cond, trueTarget, falseTarget, unwrapped.Span));
          return;
        }
    }
  }

  private void LowerIsCondition(
      FunctionBuilder fb,
      IsPatternExpressionSyntax isPat,
      IrBlockId trueTarget,
      IrBlockId falseTarget) {
    IrOperand? subjOpt = LowerExpression(fb, isPat.Expression);
    if (fb.IsTerminated || subjOpt is not { } subject) {
      return;
    }

    bool negated = isPat.NotKeyword is not null;
    IrBlockId matchTarget = negated ? falseTarget : trueTarget;
    IrBlockId failTarget = negated ? trueTarget : falseTarget;

    var varPat = (VariantPatternSyntax) isPat.Pattern;
    Symbol targetSym = fb.Model.GetReferencedSymbol(varPat)
        ?? throw new InvalidOperationException("Unresolved variant pattern in 'is' expression.");

    if (targetSym is EnumMemberSymbol enumMember) {
      IrTypeId enumIrType = fb.GetOperandType(subject);
      IrTempId expected = fb.AddTemp(enumIrType);
      fb.Emit(new ConstEnum(expected, enumIrType, enumMember.Ordinal, varPat.Span));
      IrTempId eq = fb.AddTemp(_types.Bool);
      fb.Emit(new Cmp(eq, IrCmpOp.Eq, subject, expected, isPat.Span));
      fb.Terminate(new BrIf(eq, matchTarget, failTarget, isPat.Span));
      return;
    }

    LowerVariantPatternTest(
        fb,
        subject,
        varPat,
        bindVariables: !negated,
        matchTarget,
        failTarget);
  }

  private IrOperand? LowerIsExpression(FunctionBuilder fb, IsPatternExpressionSyntax isPat) {
    IrSlotId resSlot = fb.AddSlot("is_res", _types.Bool, isMutSlot: true);
    IrBasicBlock bbTrue = fb.NewBlock();
    IrBasicBlock bbFalse = fb.NewBlock();
    IrBasicBlock bbMerge = fb.NewBlock();

    LowerIsCondition(fb, isPat, bbTrue.Id, bbFalse.Id);

    fb.SwitchTo(bbTrue);
    fb.Emit(new StoreSlot(resSlot, fb.EmitConstBool(true, isPat.Span), isPat.Span));
    fb.Terminate(new Br(bbMerge.Id, isPat.Span));

    fb.SwitchTo(bbFalse);
    fb.Emit(new StoreSlot(resSlot, fb.EmitConstBool(false, isPat.Span), isPat.Span));
    fb.Terminate(new Br(bbMerge.Id, isPat.Span));

    fb.SwitchTo(bbMerge);
    IrTempId loaded = fb.AddTemp(_types.Bool);
    fb.Emit(new LoadSlot(loaded, resSlot, isPat.Span));
    return loaded;
  }

  // ---------------------------------------------------------------------------
  // `if` statement & expression (IF-*)
  // ---------------------------------------------------------------------------

  private void LowerIfStatement(FunctionBuilder fb, IfStatementSyntax ifStmt) {
    IrBasicBlock bbThen = fb.NewBlock();
    IrBasicBlock bbMerge = fb.NewBlock();
    IrBasicBlock bbElse = ifStmt.ElseBranch is not null ? fb.NewBlock() : bbMerge;

    LowerCondition(fb, ifStmt.Condition, bbThen.Id, bbElse.Id);

    bool mergeReachable = ifStmt.ElseBranch is null;

    fb.SwitchTo(bbThen);
    LowerBlockItems(fb, ifStmt.ThenBranch.Items);
    if (!fb.IsTerminated) {
      fb.Terminate(new Br(bbMerge.Id, ifStmt.ThenBranch.Span));
      mergeReachable = true;
    }

    if (ifStmt.ElseBranch is not null) {
      fb.SwitchTo(bbElse);
      LowerStatement(fb, ifStmt.ElseBranch);
      if (!fb.IsTerminated) {
        fb.Terminate(new Br(bbMerge.Id, ifStmt.ElseBranch.Span));
        mergeReachable = true;
      }
    }

    fb.SwitchTo(bbMerge);
    if (!mergeReachable) {
      fb.Terminate(new Unreachable(ifStmt.Span));
    }
  }

  private IrOperand? LowerIfExpression(FunctionBuilder fb, IfExpressionSyntax ifExpr) {
    TypeSymbol resType = fb.Model.GetType(ifExpr)
        ?? throw new InvalidOperationException("Missing type on 'if' expression.");
    bool hasValue = !resType.IsVoid && !resType.IsNever;
    IrTypeId? resIrType = hasValue ? InternType(resType) : null;
    IrSlotId? resSlot = hasValue ? fb.AddSlot("if_res", resIrType!.Value, isMutSlot: true) : null;

    IrBasicBlock bbThen = fb.NewBlock();
    IrBasicBlock bbElse = fb.NewBlock();
    IrBasicBlock bbMerge = fb.NewBlock();

    LowerCondition(fb, ifExpr.Condition, bbThen.Id, bbElse.Id);

    bool mergeReachable = false;

    fb.SwitchTo(bbThen);
    IrOperand? thenVal = LowerExpression(fb, ifExpr.ThenBranch);
    if (!fb.IsTerminated) {
      if (hasValue && thenVal is { } tv) {
        fb.Emit(new StoreSlot(resSlot!.Value, tv, ifExpr.ThenBranch.Span));
      }

      fb.Terminate(new Br(bbMerge.Id, ifExpr.ThenBranch.Span));
      mergeReachable = true;
    }

    fb.SwitchTo(bbElse);
    IrOperand? elseVal = LowerExpression(fb, ifExpr.ElseBranch);
    if (!fb.IsTerminated) {
      if (hasValue && elseVal is { } ev) {
        fb.Emit(new StoreSlot(resSlot!.Value, ev, ifExpr.ElseBranch.Span));
      }

      fb.Terminate(new Br(bbMerge.Id, ifExpr.ElseBranch.Span));
      mergeReachable = true;
    }

    fb.SwitchTo(bbMerge);
    if (!mergeReachable) {
      fb.Terminate(new Unreachable(ifExpr.Span));
      return null;
    }

    if (!hasValue) {
      return null;
    }

    IrTempId loaded = fb.AddTemp(resIrType!.Value);
    fb.Emit(new LoadSlot(loaded, resSlot!.Value, ifExpr.Span));
    return loaded;
  }

  // ---------------------------------------------------------------------------
  // `match` statement & expression (MATCH-1..10, UNION-3..5, D-169)
  // ---------------------------------------------------------------------------

  private void LowerMatchStatement(FunctionBuilder fb, MatchStatementSyntax matchStmt) =>
      _ = LowerMatchCore(fb, matchStmt.Subject, matchStmt.Arms, resultType: null, matchStmt.Span);

  private IrOperand? LowerMatchExpression(FunctionBuilder fb, MatchExpressionSyntax matchExpr) {
    TypeSymbol resType = fb.Model.GetType(matchExpr)
        ?? throw new InvalidOperationException("Missing type on 'match' expression.");
    return LowerMatchCore(fb, matchExpr.Subject, matchExpr.Arms, resType, matchExpr.Span);
  }

  private IrOperand? LowerMatchCore(
      FunctionBuilder fb,
      ExpressionSyntax subjectExpr,
      ImmutableArray<SyntaxNode> arms,
      TypeSymbol? resultType,
      SourceSpan span) {
    IrOperand? subjOpt = LowerExpression(fb, subjectExpr);
    if (fb.IsTerminated || subjOpt is not { } subject) {
      return null;
    }

    bool isExpression = resultType is not null;
    bool hasValue = resultType is not null && !resultType.IsVoid && !resultType.IsNever;
    IrTypeId? resIrType = hasValue ? InternType(resultType!) : null;
    IrSlotId? resSlot = hasValue ? fb.AddSlot("res", resIrType!.Value, isMutSlot: true) : null;

    TypeSymbol subjectSymType = fb.Model.GetType(subjectExpr)
        ?? throw new InvalidOperationException("Missing subject type on 'match'.");

    // Fast path: flat enum or union switch with unique variants and no nested sub-patterns.
    if (CanUseDirectTagSwitch(fb.Model, subjectSymType, arms)) {
      return LowerDirectSwitchMatch(fb, subject, subjectSymType, arms, isExpression, hasValue, resIrType, resSlot, span);
    }

    // General sequential pattern test chain (handles literals, multi-patterns, nested variants, floats, strings).
    return LowerSequentialMatch(fb, subject, arms, isExpression, hasValue, resIrType, resSlot, span);
  }

  private static bool CanUseDirectTagSwitch(
      SemanticModel model,
      TypeSymbol subjectType,
      ImmutableArray<SyntaxNode> arms) {
    if (subjectType is not (EnumType or UnionType)) {
      return false;
    }

    var seenTags = new HashSet<int>();
    foreach (SyntaxNode arm in arms) {
      if (arm is ElseArmSyntax) {
        continue;
      }

      if (arm is not WhenArmSyntax whenArm) {
        return false;
      }

      foreach (PatternSyntax pat in whenArm.Patterns) {
        if (pat is not VariantPatternSyntax varPat) {
          return false;
        }

        // Nested literal or nested variant patterns need sequential tests.
        foreach (PatternSyntax sub in varPat.SubPatterns) {
          if (sub is not (IdentifierPatternSyntax or LabelBindingPatternSyntax)) {
            return false;
          }
        }

        Symbol? sym = model.GetReferencedSymbol(varPat);
        int tag = sym switch {
          EnumMemberSymbol em => em.Ordinal,
          UnionVariantSymbol uv => uv.TagOrdinal,
          _ => -1,
        };
        if (tag < 0 || !seenTags.Add(tag)) {
          return false;
        }
      }
    }

    return true;
  }

  private IrOperand? LowerDirectSwitchMatch(
      FunctionBuilder fb,
      IrOperand subject,
      TypeSymbol subjectSymType,
      ImmutableArray<SyntaxNode> arms,
      bool isExpression,
      bool hasValue,
      IrTypeId? resIrType,
      IrSlotId? resSlot,
      SourceSpan span) {
    IrOperand scrutinee;
    if (subjectSymType is UnionType) {
      IrTempId tag = fb.AddTemp(_types.Int, "tag");
      fb.Emit(new UnionTag(tag, subject, span));
      scrutinee = tag;
    } else {
      scrutinee = subject;
    }

    var cases = ImmutableArray.CreateBuilder<(int CaseValue, IrBlockId Target)>();
    var armPlan = new List<(SyntaxNode Arm, IrBasicBlock Block, VariantPatternSyntax? SingleBindPattern)>();
    IrBasicBlock? elseBlock = null;

    foreach (SyntaxNode arm in arms) {
      if (arm is WhenArmSyntax whenArm) {
        IrBasicBlock bbArm = fb.NewBlock();
        foreach (PatternSyntax pat in whenArm.Patterns) {
          var varPat = (VariantPatternSyntax) pat;
          Symbol sym = fb.Model.GetReferencedSymbol(varPat)!;
          int tagVal = sym is EnumMemberSymbol em ? em.Ordinal : ((UnionVariantSymbol) sym).TagOrdinal;
          cases.Add((tagVal, bbArm.Id));
        }

        VariantPatternSyntax? bindPat = whenArm.Patterns.Length == 1
            ? whenArm.Patterns[0] as VariantPatternSyntax
            : null;
        armPlan.Add((whenArm, bbArm, bindPat));
      } else if (arm is ElseArmSyntax elseArm) {
        elseBlock = fb.NewBlock();
        armPlan.Add((elseArm, elseBlock, null));
      }
    }

    // If non-exhaustive without an explicit else (should not happen in valid code, but safe guard):
    bool allVariantsCovered = subjectSymType switch {
      EnumType et => cases.Count >= et.Definition.Members.Length,
      UnionType ut => cases.Count >= ut.Definition.Variants.Length,
      _ => false,
    };
    IrBlockId? defaultTarget = elseBlock?.Id;
    if (defaultTarget is null && !allVariantsCovered) {
      IrBasicBlock bbUnreach = fb.NewBlock();
      bbUnreach.Terminator = new Unreachable(span);
      defaultTarget = bbUnreach.Id;
    }

    fb.Terminate(new Switch(scrutinee, cases.ToImmutable(), defaultTarget, span));

    IrBasicBlock bbMerge = fb.NewBlock();
    bool mergeReachable = false;

    foreach ((SyntaxNode arm, IrBasicBlock bbArm, VariantPatternSyntax? bindPat) in armPlan) {
      fb.SwitchTo(bbArm);
      var boundSyms = new List<LocalSymbol>();
      if (bindPat is not null && subjectSymType is UnionType) {
        EmitFlatPatternBindings(fb, subject, bindPat, boundSyms);
      }

      if (LowerMatchArmBody(fb, arm, isExpression, hasValue, resSlot, bbMerge.Id)) {
        mergeReachable = true;
      }

      foreach (LocalSymbol s in boundSyms) {
        fb.LocalTemps.Remove(s);
      }
    }

    fb.SwitchTo(bbMerge);
    if (!mergeReachable) {
      fb.Terminate(new Unreachable(span));
      return null;
    }

    if (!hasValue) {
      return null;
    }

    IrTempId loaded = fb.AddTemp(resIrType!.Value);
    fb.Emit(new LoadSlot(loaded, resSlot!.Value, span));
    return loaded;
  }

  private IrOperand? LowerSequentialMatch(
      FunctionBuilder fb,
      IrOperand subject,
      ImmutableArray<SyntaxNode> arms,
      bool isExpression,
      bool hasValue,
      IrTypeId? resIrType,
      IrSlotId? resSlot,
      SourceSpan span) {
    IrBasicBlock bbMerge = fb.NewBlock();
    bool mergeReachable = false;
    ElseArmSyntax? elseArm = null;
    var whenArms = new List<WhenArmSyntax>();

    foreach (SyntaxNode arm in arms) {
      if (arm is WhenArmSyntax w) {
        whenArms.Add(w);
      } else if (arm is ElseArmSyntax e) {
        elseArm = e;
      }
    }

    IrBasicBlock bbFallback = fb.NewBlock();

    for (int i = 0; i < whenArms.Count; i++) {
      WhenArmSyntax whenArm = whenArms[i];
      IrBasicBlock bbArmBody = fb.NewBlock();
      IrBasicBlock bbNextArm = i + 1 < whenArms.Count ? fb.NewBlock() : bbFallback;

      if (whenArm.Patterns.Length == 1) {
        var boundSyms = new List<LocalSymbol>();
        LowerSinglePatternTest(fb, subject, whenArm.Patterns[0], bbArmBody.Id, bbNextArm.Id, boundSyms);

        fb.SwitchTo(bbArmBody);
        if (LowerMatchArmBody(fb, whenArm, isExpression, hasValue, resSlot, bbMerge.Id)) {
          mergeReachable = true;
        }

        foreach (LocalSymbol s in boundSyms) {
          fb.LocalTemps.Remove(s);
        }
      } else {
        // Multi-pattern `when p1, p2 -> ...`: bindings are disallowed by checker; any matching pattern jumps to bbArmBody.
        for (int p = 0; p < whenArm.Patterns.Length; p++) {
          IrBasicBlock bbNextPat = p + 1 < whenArm.Patterns.Length ? fb.NewBlock() : bbNextArm;
          LowerSinglePatternTest(fb, subject, whenArm.Patterns[p], bbArmBody.Id, bbNextPat.Id, boundSyms: null);
          if (p + 1 < whenArm.Patterns.Length) {
            fb.SwitchTo(bbNextPat);
          }
        }

        fb.SwitchTo(bbArmBody);
        if (LowerMatchArmBody(fb, whenArm, isExpression, hasValue, resSlot, bbMerge.Id)) {
          mergeReachable = true;
        }
      }

      fb.SwitchTo(bbNextArm);
    }

    // Fallback / `else` arm.
    fb.SwitchTo(bbFallback);
    if (elseArm is not null) {
      if (LowerMatchArmBody(fb, elseArm, isExpression, hasValue, resSlot, bbMerge.Id)) {
        mergeReachable = true;
      }
    } else {
      fb.Terminate(new Unreachable(span));
    }

    fb.SwitchTo(bbMerge);
    if (!mergeReachable) {
      fb.Terminate(new Unreachable(span));
      return null;
    }

    if (!hasValue) {
      return null;
    }

    IrTempId loaded = fb.AddTemp(resIrType!.Value);
    fb.Emit(new LoadSlot(loaded, resSlot!.Value, span));
    return loaded;
  }

  private void LowerSinglePatternTest(
      FunctionBuilder fb,
      IrOperand subject,
      PatternSyntax pattern,
      IrBlockId matchTarget,
      IrBlockId failTarget,
      List<LocalSymbol>? boundSyms) {
    switch (pattern) {
      case LiteralPatternSyntax litPat: {
          IrOperand? litOpt = LowerExpression(fb, litPat.Literal);
          if (fb.IsTerminated || litOpt is not { } litVal) {
            return;
          }

          // Widen int literal to float if subject is float.
          if (fb.GetOperandType(subject) == _types.Float && fb.GetOperandType(litVal) == _types.Int) {
            IrTempId widened = fb.AddTemp(_types.Float);
            fb.Emit(new CastI64ToF64(widened, litVal, litPat.Span));
            litVal = widened;
          }

          IrOperand eq = EmitEquality(fb, subject, litVal, negated: false, litPat.Span);
          fb.Terminate(new BrIf(eq, matchTarget, failTarget, litPat.Span));
          return;
        }

      case VariantPatternSyntax varPat: {
          Symbol sym = fb.Model.GetReferencedSymbol(varPat)
              ?? throw new InvalidOperationException("Unresolved variant pattern in 'match'.");
          if (sym is EnumMemberSymbol em) {
            IrTypeId enumIrType = fb.GetOperandType(subject);
            IrTempId expected = fb.AddTemp(enumIrType);
            fb.Emit(new ConstEnum(expected, enumIrType, em.Ordinal, varPat.Span));
            IrTempId eq = fb.AddTemp(_types.Bool);
            fb.Emit(new Cmp(eq, IrCmpOp.Eq, subject, expected, varPat.Span));
            fb.Terminate(new BrIf(eq, matchTarget, failTarget, varPat.Span));
            return;
          }

          LowerVariantPatternTest(
              fb,
              subject,
              varPat,
              bindVariables: boundSyms is not null,
              matchTarget,
              failTarget,
              boundSyms);
          return;
        }

      default:
        fb.Terminate(new Br(matchTarget, pattern.Span));
        return;
    }
  }

  private void LowerVariantPatternTest(
      FunctionBuilder fb,
      IrOperand subject,
      VariantPatternSyntax varPat,
      bool bindVariables,
      IrBlockId matchTarget,
      IrBlockId failTarget,
      List<LocalSymbol>? boundSyms = null) {
    var variant = (UnionVariantSymbol) (fb.Model.GetReferencedSymbol(varPat)
        ?? throw new InvalidOperationException("Unresolved union variant pattern."));

    IrTempId tag = fb.AddTemp(_types.Int, "tag");
    fb.Emit(new UnionTag(tag, subject, varPat.Span));
    IrTempId expectedTag = fb.EmitConstInt(variant.TagOrdinal, varPat.Span);
    IrTempId tagEq = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(tagEq, IrCmpOp.Eq, tag, expectedTag, varPat.Span));

    PatternBindingInfo? bindingInfo = fb.Model.GetPatternBindings(varPat);
    bool hasBindings = bindVariables && bindingInfo is not null && !bindingInfo.Bindings.IsEmpty;
    bool hasNestedTests = false;
    foreach (PatternSyntax sub in varPat.SubPatterns) {
      if (sub is LiteralPatternSyntax or VariantPatternSyntax) {
        hasNestedTests = true;
        break;
      }
    }

    if (!hasBindings && !hasNestedTests) {
      fb.Terminate(new BrIf(tagEq, matchTarget, failTarget, varPat.Span));
      return;
    }

    IrBasicBlock bbMatched = fb.NewBlock();
    fb.Terminate(new BrIf(tagEq, bbMatched.Id, failTarget, varPat.Span));
    fb.SwitchTo(bbMatched);

    if (hasBindings) {
      EmitFlatPatternBindings(fb, subject, varPat, boundSyms);
    }

    if (hasNestedTests) {
      var irUnion = (IrUnionType) _types.GetType(fb.GetOperandType(subject));
      IrUnionVariant irVariant = irUnion.Variants[variant.TagOrdinal];
      for (int i = 0; i < varPat.SubPatterns.Length; i++) {
        PatternSyntax sub = varPat.SubPatterns[i];
        if (sub is not (LiteralPatternSyntax or VariantPatternSyntax)) {
          continue;
        }

        IrTempId fieldVal = fb.AddTemp(irVariant.Fields[i].Type);
        fb.Emit(new UnionField(fieldVal, subject, variant.TagOrdinal, i, sub.Span));
        IrBasicBlock bbSubOk = fb.NewBlock();
        LowerSinglePatternTest(fb, fieldVal, sub, bbSubOk.Id, failTarget, boundSyms);
        fb.SwitchTo(bbSubOk);
      }
    }

    fb.Terminate(new Br(matchTarget, varPat.Span));
  }

  private void EmitFlatPatternBindings(
      FunctionBuilder fb,
      IrOperand subject,
      VariantPatternSyntax varPat,
      List<LocalSymbol>? boundSyms) {
    PatternBindingInfo? info = fb.Model.GetPatternBindings(varPat);
    if (info is null || info.Bindings.IsEmpty) {
      return;
    }

    var irUnion = (IrUnionType) _types.GetType(fb.GetOperandType(subject));
    int variantIdx = info.Variant.TagOrdinal;
    IrUnionVariant irVariant = irUnion.Variants[variantIdx];

    foreach (PatternBindingEntry entry in info.Bindings) {
      IrTypeId fieldIrType = irVariant.Fields[entry.FieldOrdinal].Type;
      IrTempId boundTmp = fb.AddTemp(fieldIrType, entry.Binding.Name);
      fb.Emit(new UnionField(boundTmp, subject, variantIdx, entry.FieldOrdinal, entry.Binding.NameSpan));
      fb.LocalTemps[entry.Binding] = boundTmp;
      boundSyms?.Add(entry.Binding);
    }
  }

  private bool LowerMatchArmBody(
      FunctionBuilder fb,
      SyntaxNode arm,
      bool isExpression,
      bool hasValue,
      IrSlotId? resSlot,
      IrBlockId mergeBlockId) {
    ExpressionSyntax? exprBody = arm switch {
      WhenArmSyntax w => w.ExpressionBody,
      ElseArmSyntax e => e.ExpressionBody,
      _ => null,
    };
    BlockStatementSyntax? blockBody = arm switch {
      WhenArmSyntax w => w.BlockBody,
      ElseArmSyntax e => e.BlockBody,
      _ => null,
    };

    if (exprBody is not null) {
      IrOperand? val = LowerExpression(fb, exprBody);
      if (!fb.IsTerminated) {
        if (hasValue && val is { } v) {
          fb.Emit(new StoreSlot(resSlot!.Value, v, exprBody.Span));
        }

        fb.Terminate(new Br(mergeBlockId, exprBody.Span));
        return true;
      }

      return false;
    }

    if (blockBody is not null) {
      if (isExpression) {
        fb.YieldStack.Push(new YieldTarget(resSlot, mergeBlockId, null));
        LowerBlockItems(fb, blockBody.Items);
        fb.YieldStack.Pop();
        if (!fb.IsTerminated) {
          fb.Terminate(new Br(mergeBlockId, blockBody.Span));
          return true;
        }

        foreach (IrBasicBlock blk in fb.Function.Blocks) {
          if (blk.Terminator is Br { Target: var t } && t == mergeBlockId) {
            return true;
          }
        }

        return false;
      } else {
        LowerBlockItems(fb, blockBody.Items);
        if (!fb.IsTerminated) {
          fb.Terminate(new Br(mergeBlockId, blockBody.Span));
          return true;
        }

        return false;
      }
    }

    fb.Terminate(new Br(mergeBlockId, arm.Span));
    return true;
  }

  // ---------------------------------------------------------------------------
  // All 9 `LoopKind`s (LOOP-1..20, SideTables.cs:43-53)
  // ---------------------------------------------------------------------------

  private void LowerForStatement(FunctionBuilder fb, ForStatementSyntax forStmt) {
    LoopInfo info = fb.Model.GetLoopInfo(forStmt)
        ?? throw new InvalidOperationException("Missing LoopInfo for 'for' statement.");

    switch (info.Kind) {
      case LoopKind.Infinite:
        LowerInfiniteLoop(fb, forStmt);
        break;

      case LoopKind.ConditionLoop:
        LowerConditionLoop(fb, forStmt, (ForConditionHeaderSyntax) forStmt.Header!);
        break;

      case LoopKind.CountLoop:
        LowerCountLoop(fb, forStmt, (ForConditionHeaderSyntax) forStmt.Header!, info);
        break;

      case LoopKind.ForInRange:
        LowerForInRangeLoop(fb, forStmt, (ForInHeaderSyntax) forStmt.Header!, info);
        break;

      case LoopKind.ForInVector:
        LowerForInVectorLoop(fb, forStmt, (ForInHeaderSyntax) forStmt.Header!, info);
        break;

      case LoopKind.ForInString:
        LowerForInStringLoop(fb, forStmt, (ForInHeaderSyntax) forStmt.Header!, info);
        break;

      case LoopKind.ForInMap:
        LowerForInMapLoop(fb, forStmt, (ForInHeaderSyntax) forStmt.Header!, info);
        break;

      case LoopKind.ForInSet:
        LowerForInSetLoop(fb, forStmt, (ForInHeaderSyntax) forStmt.Header!, info);
        break;

      case LoopKind.ForInPool:
        LowerForInPoolLoop(fb, forStmt, (ForInHeaderSyntax) forStmt.Header!, info);
        break;

      default:
        throw new InvalidOperationException($"Unsupported LoopKind '{info.Kind}'.");
    }
  }

  private void LowerInfiniteLoop(FunctionBuilder fb, ForStatementSyntax forStmt) {
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbExit = fb.NewBlock();
    fb.Terminate(new Br(bbBody.Id, forStmt.Span));

    fb.LoopStack.Push(new LoopTarget(bbExit.Id, bbBody.Id));
    fb.SwitchTo(bbBody);
    LowerBlockItems(fb, forStmt.Body.Items);
    if (!fb.IsTerminated) {
      fb.Terminate(new Br(bbBody.Id, forStmt.Body.Span));
    }

    fb.LoopStack.Pop();

    bool exitReachable = HasPredecessor(fb.Function, bbExit.Id);
    fb.SwitchTo(bbExit);
    if (!exitReachable) {
      fb.Terminate(new Unreachable(forStmt.Span));
    }
  }

  private void LowerConditionLoop(
      FunctionBuilder fb,
      ForStatementSyntax forStmt,
      ForConditionHeaderSyntax header) {
    IrBasicBlock bbCond = fb.NewBlock();
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbExit = fb.NewBlock();

    fb.Terminate(new Br(bbCond.Id, forStmt.Span));
    fb.SwitchTo(bbCond);
    LowerCondition(fb, header.Condition, bbBody.Id, bbExit.Id);

    fb.LoopStack.Push(new LoopTarget(bbExit.Id, bbCond.Id));
    fb.SwitchTo(bbBody);
    LowerBlockItems(fb, forStmt.Body.Items);
    if (!fb.IsTerminated) {
      fb.Terminate(new Br(bbCond.Id, forStmt.Body.Span));
    }

    fb.LoopStack.Pop();
    fb.SwitchTo(bbExit);
  }

  private void LowerCountLoop(
      FunctionBuilder fb,
      ForStatementSyntax forStmt,
      ForConditionHeaderSyntax header,
      LoopInfo info) {
    IrOperand? countOpt = LowerExpression(fb, header.Condition);
    if (fb.IsTerminated || countOpt is not { } countVal) {
      return;
    }

    IrSlotId limitSlot = fb.AddSlot("limit", _types.Int);
    IrSlotId idxSlot = fb.AddSlot("it", _types.Int, isMutSlot: true);
    fb.Emit(new StoreSlot(limitSlot, countVal, header.Span));
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0, header.Span), header.Span));

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbLatch = fb.NewBlock();
    IrBasicBlock bbExit = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id, forStmt.Span));

    fb.SwitchTo(bbHead);
    IrTempId curI = fb.AddTemp(_types.Int, "it");
    IrTempId limit = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curI, idxSlot, header.Span));
    fb.Emit(new LoadSlot(limit, limitSlot, header.Span));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curI, limit, header.Span));
    fb.Terminate(new BrIf(cond, bbBody.Id, bbExit.Id, header.Span));

    fb.SwitchTo(bbBody);
    if (info.ImplicitItSymbol is { } itSym) {
      fb.LocalTemps[itSym] = curI;
    }

    fb.LoopStack.Push(new LoopTarget(bbExit.Id, bbLatch.Id));
    LowerBlockItems(fb, forStmt.Body.Items);
    fb.LoopStack.Pop();

    if (info.ImplicitItSymbol is { } itCleanup) {
      fb.LocalTemps.Remove(itCleanup);
    }

    if (!fb.IsTerminated) {
      fb.Terminate(new Br(bbLatch.Id, forStmt.Body.Span));
    }

    fb.SwitchTo(bbLatch);
    if (HasPredecessor(fb.Function, bbLatch.Id)) {
      IrTempId latchI = fb.AddTemp(_types.Int);
      fb.Emit(new LoadSlot(latchI, idxSlot, header.Span));
      IrTempId nextI = fb.AddTemp(_types.Int);
      fb.Emit(new Add(nextI, latchI, fb.EmitConstInt(1, header.Span), header.Span));
      fb.Emit(new StoreSlot(idxSlot, nextI, header.Span));
      fb.Terminate(new Br(bbHead.Id, header.Span));
    } else {
      fb.Terminate(new Unreachable(header.Span));
    }

    fb.SwitchTo(bbExit);
  }

  private void LowerForInRangeLoop(
      FunctionBuilder fb,
      ForStatementSyntax forStmt,
      ForInHeaderSyntax header,
      LoopInfo info) {
    IrOperand? rangeOpt = LowerExpression(fb, header.Collection);
    if (fb.IsTerminated || rangeOpt is not { } rangeVal) {
      return;
    }

    IrSlotId limitSlot = fb.AddSlot("limit", _types.Int);
    IrSlotId idxSlot = fb.AddSlot("i", _types.Int, isMutSlot: true);
    fb.Emit(new StoreSlot(limitSlot, rangeVal, header.Span));
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0, header.Span), header.Span));

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbLatch = fb.NewBlock();
    IrBasicBlock bbExit = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id, forStmt.Span));

    fb.SwitchTo(bbHead);
    IrTempId curI = fb.AddTemp(_types.Int);
    IrTempId limit = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curI, idxSlot, header.Span));
    fb.Emit(new LoadSlot(limit, limitSlot, header.Span));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curI, limit, header.Span));
    fb.Terminate(new BrIf(cond, bbBody.Id, bbExit.Id, header.Span));

    fb.SwitchTo(bbBody);
    if (info.FirstBinding is { } first) {
      fb.LocalTemps[first] = curI;
    }

    if (info.SecondBinding is { } second) {
      fb.LocalTemps[second] = curI;
    }

    fb.LoopStack.Push(new LoopTarget(bbExit.Id, bbLatch.Id));
    LowerBlockItems(fb, forStmt.Body.Items);
    fb.LoopStack.Pop();

    if (info.FirstBinding is { } f1) {
      fb.LocalTemps.Remove(f1);
    }

    if (info.SecondBinding is { } f2) {
      fb.LocalTemps.Remove(f2);
    }

    if (!fb.IsTerminated) {
      fb.Terminate(new Br(bbLatch.Id, forStmt.Body.Span));
    }

    fb.SwitchTo(bbLatch);
    if (HasPredecessor(fb.Function, bbLatch.Id)) {
      IrTempId latchI = fb.AddTemp(_types.Int);
      fb.Emit(new LoadSlot(latchI, idxSlot, header.Span));
      IrTempId nextI = fb.AddTemp(_types.Int);
      fb.Emit(new Add(nextI, latchI, fb.EmitConstInt(1, header.Span), header.Span));
      fb.Emit(new StoreSlot(idxSlot, nextI, header.Span));
      fb.Terminate(new Br(bbHead.Id, header.Span));
    } else {
      fb.Terminate(new Unreachable(header.Span));
    }

    fb.SwitchTo(bbExit);
  }

  private void LowerForInVectorLoop(
      FunctionBuilder fb,
      ForStatementSyntax forStmt,
      ForInHeaderSyntax header,
      LoopInfo info) {
    IrOperand? vecOpt = LowerExpression(fb, header.Collection);
    if (fb.IsTerminated || vecOpt is not { } vecVal) {
      return;
    }

    IrTypeId vecTypeId = fb.GetOperandType(vecVal);
    var vecType = (IrVectorType) _types.GetType(vecTypeId);

    // Snapshot iteration (D-177, LOOP-5, LOOP-7).
    IrSlotId snapSlot = fb.AddSlot("snap", vecTypeId);
    IrSlotId idxSlot = fb.AddSlot("i", _types.Int, isMutSlot: true);
    IrSlotId lenSlot = fb.AddSlot("len", _types.Int);
    fb.Emit(new StoreSlot(snapSlot, vecVal, header.Span));

    IrTempId snapInit = fb.AddTemp(vecTypeId);
    fb.Emit(new LoadSlot(snapInit, snapSlot, header.Span));
    IrTempId lenInit = fb.AddTemp(_types.Int);
    fb.Emit(new VecLen(lenInit, snapInit, header.Span));
    fb.Emit(new StoreSlot(lenSlot, lenInit, header.Span));
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0, header.Span), header.Span));

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbLatch = fb.NewBlock();
    IrBasicBlock bbExit = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id, forStmt.Span));

    fb.SwitchTo(bbHead);
    IrTempId curI = fb.AddTemp(_types.Int);
    IrTempId curLen = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curI, idxSlot, header.Span));
    fb.Emit(new LoadSlot(curLen, lenSlot, header.Span));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curI, curLen, header.Span));
    fb.Terminate(new BrIf(cond, bbBody.Id, bbExit.Id, header.Span));

    fb.SwitchTo(bbBody);
    IrTempId snapBody = fb.AddTemp(vecTypeId);
    fb.Emit(new LoadSlot(snapBody, snapSlot, header.Span));
    IrTempId idxBody = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(idxBody, idxSlot, header.Span));

    bool hasSecond = info.SecondBinding is not null;
    string elemName = hasSecond ? info.SecondBinding!.Name : (info.FirstBinding?.Name ?? "elem");
    IrTempId elem = fb.AddTemp(vecType.ElementType, elemName);
    fb.Emit(new VecGet(elem, snapBody, idxBody, Unchecked: true, header.Span));

    if (hasSecond) {
      if (info.FirstBinding is { } first) {
        fb.LocalTemps[first] = idxBody;
      }

      fb.LocalTemps[info.SecondBinding!] = elem;
    } else if (info.FirstBinding is { } firstOnly) {
      fb.LocalTemps[firstOnly] = elem;
    }

    fb.LoopStack.Push(new LoopTarget(bbExit.Id, bbLatch.Id));
    LowerBlockItems(fb, forStmt.Body.Items);
    fb.LoopStack.Pop();

    if (info.FirstBinding is { } f1) {
      fb.LocalTemps.Remove(f1);
    }

    if (info.SecondBinding is { } f2) {
      fb.LocalTemps.Remove(f2);
    }

    if (!fb.IsTerminated) {
      fb.Terminate(new Br(bbLatch.Id, forStmt.Body.Span));
    }

    fb.SwitchTo(bbLatch);
    if (HasPredecessor(fb.Function, bbLatch.Id)) {
      IrTempId latchI = fb.AddTemp(_types.Int);
      fb.Emit(new LoadSlot(latchI, idxSlot, header.Span));
      IrTempId nextI = fb.AddTemp(_types.Int);
      fb.Emit(new Add(nextI, latchI, fb.EmitConstInt(1, header.Span), header.Span));
      fb.Emit(new StoreSlot(idxSlot, nextI, header.Span));
      fb.Terminate(new Br(bbHead.Id, header.Span));
    } else {
      fb.Terminate(new Unreachable(header.Span));
    }

    fb.SwitchTo(bbExit);
  }

  private void LowerForInStringLoop(
      FunctionBuilder fb,
      ForStatementSyntax forStmt,
      ForInHeaderSyntax header,
      LoopInfo info) {
    // LOOP-6, LOOP-7, LOOP-12: UTF-8 rune loop with bb_latch advancing byte_pos & rune_idx on `continue`.
    IrOperand? strOpt = LowerExpression(fb, header.Collection);
    if (fb.IsTerminated || strOpt is not { } strVal) {
      return;
    }

    bool hasSecond = info.SecondBinding is not null;

    IrSlotId snapSlot = fb.AddSlot("snap", _types.String);
    IrSlotId byteLenSlot = fb.AddSlot("byte_len", _types.Int);
    IrSlotId bytePosSlot = fb.AddSlot("byte_pos", _types.Int, isMutSlot: true);
    IrSlotId? runeIdxSlot = hasSecond ? fb.AddSlot("rune_idx", _types.Int, isMutSlot: true) : null;

    fb.Emit(new StoreSlot(snapSlot, strVal, header.Span));
    IrTempId initStr = fb.AddTemp(_types.String);
    fb.Emit(new LoadSlot(initStr, snapSlot, header.Span));
    IrTempId initByteLen = fb.AddTemp(_types.Int);
    fb.Emit(new StrByteLen(initByteLen, initStr, header.Span));
    fb.Emit(new StoreSlot(byteLenSlot, initByteLen, header.Span));
    fb.Emit(new StoreSlot(bytePosSlot, fb.EmitConstInt(0, header.Span), header.Span));
    if (runeIdxSlot is { } rSlot) {
      fb.Emit(new StoreSlot(rSlot, fb.EmitConstInt(0, header.Span), header.Span));
    }

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbLatch = fb.NewBlock();
    IrBasicBlock bbExit = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id, forStmt.Span));

    fb.SwitchTo(bbHead);
    IrTempId curPos = fb.AddTemp(_types.Int);
    IrTempId curByteLen = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curPos, bytePosSlot, header.Span));
    fb.Emit(new LoadSlot(curByteLen, byteLenSlot, header.Span));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curPos, curByteLen, header.Span));
    fb.Terminate(new BrIf(cond, bbBody.Id, bbExit.Id, header.Span));

    fb.SwitchTo(bbBody);
    IrTempId bodyStr = fb.AddTemp(_types.String);
    fb.Emit(new LoadSlot(bodyStr, snapSlot, header.Span));
    string runeName = hasSecond ? info.SecondBinding!.Name : (info.FirstBinding?.Name ?? "ch");
    IrTempId ch = fb.AddTemp(_types.Rune, runeName);
    fb.Emit(new StrDecodeRune(ch, bodyStr, curPos, header.Span));

    if (hasSecond) {
      IrTempId curRuneIdx = fb.AddTemp(_types.Int, info.FirstBinding?.Name);
      fb.Emit(new LoadSlot(curRuneIdx, runeIdxSlot!.Value, header.Span));
      if (info.FirstBinding is { } first) {
        fb.LocalTemps[first] = curRuneIdx;
      }

      fb.LocalTemps[info.SecondBinding!] = ch;
    } else if (info.FirstBinding is { } firstOnly) {
      fb.LocalTemps[firstOnly] = ch;
    }

    fb.LoopStack.Push(new LoopTarget(bbExit.Id, bbLatch.Id));
    LowerBlockItems(fb, forStmt.Body.Items);
    fb.LoopStack.Pop();

    if (info.FirstBinding is { } f1) {
      fb.LocalTemps.Remove(f1);
    }

    if (info.SecondBinding is { } f2) {
      fb.LocalTemps.Remove(f2);
    }

    if (!fb.IsTerminated) {
      fb.Terminate(new Br(bbLatch.Id, forStmt.Body.Span));
    }

    fb.SwitchTo(bbLatch);
    if (HasPredecessor(fb.Function, bbLatch.Id)) {
      IrTempId latchStr = fb.AddTemp(_types.String);
      IrTempId latchPos = fb.AddTemp(_types.Int);
      fb.Emit(new LoadSlot(latchStr, snapSlot, header.Span));
      fb.Emit(new LoadSlot(latchPos, bytePosSlot, header.Span));
      IrTempId nextPos = fb.AddTemp(_types.Int);
      fb.Emit(new StrNextBytePos(nextPos, latchStr, latchPos, header.Span));
      fb.Emit(new StoreSlot(bytePosSlot, nextPos, header.Span));

      if (runeIdxSlot is { } rSlot2) {
        IrTempId latchRuneIdx = fb.AddTemp(_types.Int);
        fb.Emit(new LoadSlot(latchRuneIdx, rSlot2, header.Span));
        IrTempId nextRuneIdx = fb.AddTemp(_types.Int);
        fb.Emit(new Add(nextRuneIdx, latchRuneIdx, fb.EmitConstInt(1, header.Span), header.Span));
        fb.Emit(new StoreSlot(rSlot2, nextRuneIdx, header.Span));
      }

      fb.Terminate(new Br(bbHead.Id, header.Span));
    } else {
      fb.Terminate(new Unreachable(header.Span));
    }

    fb.SwitchTo(bbExit);
  }

  private void LowerForInMapLoop(
      FunctionBuilder fb,
      ForStatementSyntax forStmt,
      ForInHeaderSyntax header,
      LoopInfo info) {
    // LOOP-9, LOOP-10, D-120, D-004: insertion-order snapshot iteration.
    IrOperand? mapOpt = LowerExpression(fb, header.Collection);
    if (fb.IsTerminated || mapOpt is not { } mapVal) {
      return;
    }

    IrTypeId mapTypeId = fb.GetOperandType(mapVal);
    var mapType = (IrMapType) _types.GetType(mapTypeId);

    IrSlotId snapSlot = fb.AddSlot("snap", mapTypeId);
    IrSlotId slenSlot = fb.AddSlot("slen", _types.Int);
    IrSlotId idxSlot = fb.AddSlot("idx", _types.Int, isMutSlot: true);

    fb.Emit(new StoreSlot(snapSlot, mapVal, header.Span));
    IrTempId initMap = fb.AddTemp(mapTypeId);
    fb.Emit(new LoadSlot(initMap, snapSlot, header.Span));
    IrTempId initSlen = fb.AddTemp(_types.Int);
    fb.Emit(new MapSlotLen(initSlen, initMap, header.Span));
    fb.Emit(new StoreSlot(slenSlot, initSlen, header.Span));
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0, header.Span), header.Span));

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbCheck = fb.NewBlock();
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbLatch = fb.NewBlock();
    IrBasicBlock bbExit = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id, forStmt.Span));

    fb.SwitchTo(bbHead);
    IrTempId curIdx = fb.AddTemp(_types.Int);
    IrTempId curSlen = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curIdx, idxSlot, header.Span));
    fb.Emit(new LoadSlot(curSlen, slenSlot, header.Span));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curIdx, curSlen, header.Span));
    fb.Terminate(new BrIf(cond, bbCheck.Id, bbExit.Id, header.Span));

    fb.SwitchTo(bbCheck);
    IrTempId snapCheck = fb.AddTemp(mapTypeId);
    fb.Emit(new LoadSlot(snapCheck, snapSlot, header.Span));
    IrTempId isLive = fb.AddTemp(_types.Bool);
    fb.Emit(new MapSlotIsLive(isLive, snapCheck, curIdx, header.Span));
    fb.Terminate(new BrIf(isLive, bbBody.Id, bbLatch.Id, header.Span));

    fb.SwitchTo(bbBody);
    bool hasSecond = info.SecondBinding is not null;
    if (hasSecond) {
      IrTempId key = fb.AddTemp(mapType.KeyType, info.FirstBinding?.Name);
      IrTempId val = fb.AddTemp(mapType.ValueType, info.SecondBinding!.Name);
      fb.Emit(new MapSlotKey(key, snapCheck, curIdx, header.Span));
      fb.Emit(new MapSlotValue(val, snapCheck, curIdx, header.Span));
      if (info.FirstBinding is { } first) {
        fb.LocalTemps[first] = key;
      }

      fb.LocalTemps[info.SecondBinding!] = val;
    } else if (info.FirstBinding is { } firstOnly) {
      IrTempId val = fb.AddTemp(mapType.ValueType, firstOnly.Name);
      fb.Emit(new MapSlotValue(val, snapCheck, curIdx, header.Span));
      fb.LocalTemps[firstOnly] = val;
    }

    fb.LoopStack.Push(new LoopTarget(bbExit.Id, bbLatch.Id));
    LowerBlockItems(fb, forStmt.Body.Items);
    fb.LoopStack.Pop();

    if (info.FirstBinding is { } f1) {
      fb.LocalTemps.Remove(f1);
    }

    if (info.SecondBinding is { } f2) {
      fb.LocalTemps.Remove(f2);
    }

    if (!fb.IsTerminated) {
      fb.Terminate(new Br(bbLatch.Id, forStmt.Body.Span));
    }

    fb.SwitchTo(bbLatch);
    IrTempId latchIdx = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(latchIdx, idxSlot, header.Span));
    IrTempId nextIdx = fb.AddTemp(_types.Int);
    fb.Emit(new Add(nextIdx, latchIdx, fb.EmitConstInt(1, header.Span), header.Span));
    fb.Emit(new StoreSlot(idxSlot, nextIdx, header.Span));
    fb.Terminate(new Br(bbHead.Id, header.Span));

    fb.SwitchTo(bbExit);
  }

  private void LowerForInSetLoop(
      FunctionBuilder fb,
      ForStatementSyntax forStmt,
      ForInHeaderSyntax header,
      LoopInfo info) {
    // LOOP-18, D-175: insertion-order snapshot iteration.
    IrOperand? setOpt = LowerExpression(fb, header.Collection);
    if (fb.IsTerminated || setOpt is not { } setVal) {
      return;
    }

    IrTypeId setTypeId = fb.GetOperandType(setVal);
    var setType = (IrSetType) _types.GetType(setTypeId);

    IrSlotId snapSlot = fb.AddSlot("snap", setTypeId);
    IrSlotId slenSlot = fb.AddSlot("slen", _types.Int);
    IrSlotId idxSlot = fb.AddSlot("idx", _types.Int, isMutSlot: true);

    fb.Emit(new StoreSlot(snapSlot, setVal, header.Span));
    IrTempId initSet = fb.AddTemp(setTypeId);
    fb.Emit(new LoadSlot(initSet, snapSlot, header.Span));
    IrTempId initSlen = fb.AddTemp(_types.Int);
    fb.Emit(new SetSlotLen(initSlen, initSet, header.Span));
    fb.Emit(new StoreSlot(slenSlot, initSlen, header.Span));
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0, header.Span), header.Span));

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbCheck = fb.NewBlock();
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbLatch = fb.NewBlock();
    IrBasicBlock bbExit = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id, forStmt.Span));

    fb.SwitchTo(bbHead);
    IrTempId curIdx = fb.AddTemp(_types.Int);
    IrTempId curSlen = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curIdx, idxSlot, header.Span));
    fb.Emit(new LoadSlot(curSlen, slenSlot, header.Span));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curIdx, curSlen, header.Span));
    fb.Terminate(new BrIf(cond, bbCheck.Id, bbExit.Id, header.Span));

    fb.SwitchTo(bbCheck);
    IrTempId snapCheck = fb.AddTemp(setTypeId);
    fb.Emit(new LoadSlot(snapCheck, snapSlot, header.Span));
    IrTempId isLive = fb.AddTemp(_types.Bool);
    fb.Emit(new SetSlotIsLive(isLive, snapCheck, curIdx, header.Span));
    fb.Terminate(new BrIf(isLive, bbBody.Id, bbLatch.Id, header.Span));

    fb.SwitchTo(bbBody);
    if (info.FirstBinding is { } firstOnly) {
      IrTempId elem = fb.AddTemp(setType.ElementType, firstOnly.Name);
      fb.Emit(new SetSlotElem(elem, snapCheck, curIdx, header.Span));
      fb.LocalTemps[firstOnly] = elem;
    }

    fb.LoopStack.Push(new LoopTarget(bbExit.Id, bbLatch.Id));
    LowerBlockItems(fb, forStmt.Body.Items);
    fb.LoopStack.Pop();

    if (info.FirstBinding is { } f1) {
      fb.LocalTemps.Remove(f1);
    }

    if (!fb.IsTerminated) {
      fb.Terminate(new Br(bbLatch.Id, forStmt.Body.Span));
    }

    fb.SwitchTo(bbLatch);
    IrTempId latchIdx = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(latchIdx, idxSlot, header.Span));
    IrTempId nextIdx = fb.AddTemp(_types.Int);
    fb.Emit(new Add(nextIdx, latchIdx, fb.EmitConstInt(1, header.Span), header.Span));
    fb.Emit(new StoreSlot(idxSlot, nextIdx, header.Span));
    fb.Terminate(new Br(bbHead.Id, header.Span));

    fb.SwitchTo(bbExit);
  }

  private void LowerForInPoolLoop(
      FunctionBuilder fb,
      ForStatementSyntax forStmt,
      ForInHeaderSyntax header,
      LoopInfo info) {
    // LOOP-19, LOOP-20, POOL-11: deterministic increasing slot-index snapshot iteration.
    IrOperand? poolOpt = LowerExpression(fb, header.Collection);
    if (fb.IsTerminated || poolOpt is not { } poolVal) {
      return;
    }

    IrTypeId poolTypeId = fb.GetOperandType(poolVal);
    var poolType = (IrPoolType) _types.GetType(poolTypeId);

    IrSlotId snapSlot = fb.AddSlot("snap", poolTypeId);
    IrSlotId slenSlot = fb.AddSlot("slen", _types.Int);
    IrSlotId idxSlot = fb.AddSlot("idx", _types.Int, isMutSlot: true);

    fb.Emit(new StoreSlot(snapSlot, poolVal, header.Span));
    IrTempId initPool = fb.AddTemp(poolTypeId);
    fb.Emit(new LoadSlot(initPool, snapSlot, header.Span));
    IrTempId initSlen = fb.AddTemp(_types.Int);
    fb.Emit(new PoolSlotLen(initSlen, initPool, header.Span));
    fb.Emit(new StoreSlot(slenSlot, initSlen, header.Span));
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0, header.Span), header.Span));

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbCheck = fb.NewBlock();
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbLatch = fb.NewBlock();
    IrBasicBlock bbExit = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id, forStmt.Span));

    fb.SwitchTo(bbHead);
    IrTempId curIdx = fb.AddTemp(_types.Int);
    IrTempId curSlen = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curIdx, idxSlot, header.Span));
    fb.Emit(new LoadSlot(curSlen, slenSlot, header.Span));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curIdx, curSlen, header.Span));
    fb.Terminate(new BrIf(cond, bbCheck.Id, bbExit.Id, header.Span));

    fb.SwitchTo(bbCheck);
    IrTempId snapCheck = fb.AddTemp(poolTypeId);
    fb.Emit(new LoadSlot(snapCheck, snapSlot, header.Span));
    IrTempId isLive = fb.AddTemp(_types.Bool);
    fb.Emit(new PoolSlotIsLive(isLive, snapCheck, curIdx, header.Span));
    fb.Terminate(new BrIf(isLive, bbBody.Id, bbLatch.Id, header.Span));

    fb.SwitchTo(bbBody);
    bool hasSecond = info.SecondBinding is not null;
    if (hasSecond) {
      IrTypeId handleType = _types.GetHandle(poolType.ElementType);
      IrTempId handle = fb.AddTemp(handleType, info.FirstBinding?.Name);
      IrTempId val = fb.AddTemp(poolType.ElementType, info.SecondBinding!.Name);
      fb.Emit(new PoolSlotHandle(handle, snapCheck, curIdx, header.Span));
      fb.Emit(new PoolSlotValue(val, snapCheck, curIdx, header.Span));
      if (info.FirstBinding is { } first) {
        fb.LocalTemps[first] = handle;
      }

      fb.LocalTemps[info.SecondBinding!] = val;
    } else if (info.FirstBinding is { } firstOnly) {
      IrTempId val = fb.AddTemp(poolType.ElementType, firstOnly.Name);
      fb.Emit(new PoolSlotValue(val, snapCheck, curIdx, header.Span));
      fb.LocalTemps[firstOnly] = val;
    }

    fb.LoopStack.Push(new LoopTarget(bbExit.Id, bbLatch.Id));
    LowerBlockItems(fb, forStmt.Body.Items);
    fb.LoopStack.Pop();

    if (info.FirstBinding is { } f1) {
      fb.LocalTemps.Remove(f1);
    }

    if (info.SecondBinding is { } f2) {
      fb.LocalTemps.Remove(f2);
    }

    if (!fb.IsTerminated) {
      fb.Terminate(new Br(bbLatch.Id, forStmt.Body.Span));
    }

    fb.SwitchTo(bbLatch);
    IrTempId latchIdx = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(latchIdx, idxSlot, header.Span));
    IrTempId nextIdx = fb.AddTemp(_types.Int);
    fb.Emit(new Add(nextIdx, latchIdx, fb.EmitConstInt(1, header.Span), header.Span));
    fb.Emit(new StoreSlot(idxSlot, nextIdx, header.Span));
    fb.Terminate(new Br(bbHead.Id, header.Span));

    fb.SwitchTo(bbExit);
  }

  private static bool HasPredecessor(IrFunction fn, IrBlockId targetId) {
    var targets = new List<IrBlockId>(4);
    foreach (IrBasicBlock block in fn.Blocks) {
      if (block.Terminator is null) {
        continue;
      }

      targets.Clear();
      block.Terminator.CollectTargets(targets);
      foreach (IrBlockId t in targets) {
        if (t == targetId) {
          return true;
        }
      }
    }

    return false;
  }
}
