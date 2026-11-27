using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.IR;

public sealed partial class IrLowerer {
  private void LowerBlockItems(FunctionBuilder fb, ImmutableArray<SyntaxNode> items) {
    foreach (SyntaxNode item in items) {
      if (fb.IsTerminated) {
        break;
      }

      LowerStatement(fb, item);
    }
  }

  private void LowerStatement(FunctionBuilder fb, SyntaxNode item) {
    if (fb.IsTerminated) {
      return;
    }

    switch (item) {
      case VariableDeclarationSyntax varDecl:
        LowerVariableDeclaration(fb, varDecl);
        break;

      case TupleDestructuringStatementSyntax destruct:
        LowerTupleDestructuring(fb, destruct);
        break;

      case ExpressionStatementSyntax exprStmt:
        if (UnwrapParens(exprStmt.Expression) is AssignmentExpressionSyntax assignStmt
            && fb.Model.GetConversion(exprStmt.Expression) is null or { Kind: ConversionKind.None }) {
          _ = LowerAssignment(fb, assignStmt, resultUsed: false);
        } else {
          _ = LowerExpression(fb, exprStmt.Expression);
        }
        break;

      case BlockStatementSyntax block:
        LowerBlockItems(fb, block.Items);
        break;

      case IfStatementSyntax ifStmt:
        LowerIfStatement(fb, ifStmt);
        break;

      case ForStatementSyntax forStmt:
        LowerForStatement(fb, forStmt);
        break;

      case MatchStatementSyntax matchStmt:
        LowerMatchStatement(fb, matchStmt);
        break;

      case ReturnStatementSyntax retStmt:
        LowerReturn(fb, retStmt);
        break;

      case YieldStatementSyntax yieldStmt:
        LowerYield(fb, yieldStmt);
        break;

      case BreakStatementSyntax breakStmt:
        LowerBreak(fb, breakStmt);
        break;

      case ContinueStatementSyntax contStmt:
        LowerContinue(fb, contStmt);
        break;

      case AssertStatementSyntax assertStmt:
        LowerAssert(fb, assertStmt);
        break;

      case FunctionDeclarationSyntax:
      case StructDeclarationSyntax:
      case InterfaceDeclarationSyntax:
      case EnumDeclarationSyntax:
      case UnionDeclarationSyntax:
      case ModuleDeclarationSyntax:
      case ImportStatementSyntax:
      case FromImportStatementSyntax:
      case TestDeclarationSyntax:
        // Hoisted declarations are lowered separately; imports/module headers emit no runtime code.
        break;

      default:
        throw new InvalidOperationException($"Unsupported statement node '{item.GetType().Name}'.");
    }
  }

  private void LowerVariableDeclaration(FunctionBuilder fb, VariableDeclarationSyntax decl) {
    // VAR-5: evaluate initializer before binding enters scope.
    IrOperand? initOpt = LowerExpression(fb, decl.Initializer);
    if (fb.IsTerminated || initOpt is not { } initVal) {
      return;
    }

    if (fb.Model.GetDeclaredSymbol(decl) is not LocalSymbol localSym) {
      return;
    }

    BindLocalSymbol(fb, localSym, initVal, decl.Span);
  }

  private void LowerTupleDestructuring(FunctionBuilder fb, TupleDestructuringStatementSyntax destruct) {
    IrOperand? initOpt = LowerExpression(fb, destruct.Initializer);
    if (fb.IsTerminated || initOpt is not { } tupleVal) {
      return;
    }

    var irTuple = (IrTupleType) _types.GetType(fb.GetOperandType(tupleVal));
    foreach (DestructureFieldSyntax field in destruct.Fields) {
      if (fb.Model.GetDeclaredSymbol(field) is not LocalSymbol localSym) {
        continue;
      }

      string label = NameOf(fb.Model, field.LabelToken);
      int fieldIdx = FindTupleFieldIndex(irTuple, label);
      IrTempId extracted = fb.AddTemp(irTuple.Fields[fieldIdx].Type, localSym.IsMutable ? null : localSym.Name);
      fb.Emit(new TupleField(extracted, tupleVal, fieldIdx, field.Span));
      BindLocalSymbol(fb, localSym, extracted, field.Span);
    }
  }

  private void BindLocalSymbol(FunctionBuilder fb, LocalSymbol localSym, IrOperand initVal, SourceSpan span) {
    if (_globalIds.TryGetValue(localSym, out IrGlobalId globalId)) {
      IrTypeId gType = _module.Globals[globalId.Index].Type;
      IrOperand coercedGlobal = CoerceOperandToTargetType(fb, initVal, gType, span);
      IrTempId ptrTmp = fb.AddTemp(_types.GetPtr(gType));
      fb.Emit(new AddrGlobal(ptrTmp, globalId, span));
      fb.Emit(new StorePtr(ptrTmp, coercedGlobal, span));
      return;
    }

    if (fb.BoxedCellSymbols.Contains(localSym)) {
      // D-056 / FN-21: mutable local captured by a closure lives in a heap PinoCell[T].
      TypeSymbol concreteType = fb.ResolveType(localSym.Type);
      IrTypeId elemIrType = InternType(concreteType);
      IrOperand coercedCellInit = CoerceOperandToTargetType(fb, initVal, elemIrType, span);
      bool isCycleCapable = _compilation.Reachability.IsCycleCapable(concreteType);
      IrTypeId cellIrType = _types.GetCell(elemIrType, isCycleCapable);
      IrTempId cellTmp = fb.AddTemp(cellIrType, $"{localSym.Name}_cell");
      fb.Emit(new CellNew(cellTmp, coercedCellInit, AllocRegion.Heap, span));
      IrSlotId slot = fb.AddSlot(localSym.Name, cellIrType, isMutSlot: false, isBoxedCell: true);
      fb.CellSlots[localSym] = slot;
      fb.Emit(new StoreSlot(slot, cellTmp, span));
      return;
    }

    IrTypeId targetLocalIrType = InternType(fb, localSym.Type);
    IrOperand coercedInit = CoerceOperandToTargetType(fb, initVal, targetLocalIrType, span);

    if (localSym.IsMutable) {
      IrSlotId slot = fb.AddSlot(localSym.Name, targetLocalIrType, isMutSlot: true);
      fb.LocalSlots[localSym] = slot;
      fb.Emit(new StoreSlot(slot, coercedInit, span));
      return;
    }

    fb.LocalTemps[localSym] = coercedInit;
  }

  private void LowerReturn(FunctionBuilder fb, ReturnStatementSyntax retStmt) {
    if (fb.InlineReturnStack.Count > 0) {
      InlineReturnTarget target = fb.InlineReturnStack.Peek();
      if (retStmt.Expression is { } expr) {
        IrOperand? valOpt = LowerExpression(fb, expr);
        if (fb.IsTerminated) {
          return;
        }

        if (target.ResultSlot is { } slot && valOpt is { } val) {
          IrOperand coerced = target.ExpectedReturnType is { } expRet
              ? CoerceValueToExpectedType(fb, val, fb.GetNodeType(expr), expRet, expr.Span)
              : val;
          fb.Emit(new StoreSlot(slot, coerced, retStmt.Span));
        }
      }

      fb.Terminate(new Br(target.ExitBlock, retStmt.Span));
      return;
    }

    if (retStmt.Expression is null) {
      fb.Terminate(new RetVoid(retStmt.Span));
      return;
    }

    IrOperand? retValOpt = LowerExpression(fb, retStmt.Expression);
    if (fb.IsTerminated) {
      return;
    }

    if (retValOpt is { } retVal) {
      IrOperand coercedRet = CoerceOperandToTargetType(fb, retVal, fb.Function.ReturnType, retStmt.Span);
      fb.Terminate(new Ret(coercedRet, retStmt.Span));
    } else {
      fb.Terminate(new RetVoid(retStmt.Span));
    }
  }

  private void LowerYield(FunctionBuilder fb, YieldStatementSyntax yieldStmt) {
    if (fb.YieldStack.Count == 0) {
      throw new InvalidOperationException("'yield' encountered outside a recovery block or match arm.");
    }

    YieldTarget target = fb.YieldStack.Peek();
    IrOperand? valOpt = LowerExpression(fb, yieldStmt.Expression);
    if (fb.IsTerminated) {
      return;
    }

    if (target.ResultSlot is { } slot && valOpt is { } val) {
      IrOperand coerced = CoerceOperandToTargetType(fb, val, fb.Function.Slots[slot.Index].Type, yieldStmt.Span);
      fb.Emit(new StoreSlot(slot, coerced, yieldStmt.Span));
    }

    fb.Terminate(new Br(target.MergeBlock, yieldStmt.Span));
  }

  private void LowerBreak(FunctionBuilder fb, BreakStatementSyntax breakStmt) {
    if (fb.LoopStack.Count == 0) {
      throw new InvalidOperationException("'break' encountered outside a loop.");
    }

    fb.Terminate(new Br(fb.LoopStack.Peek().BreakBlock, breakStmt.Span));
  }

  private void LowerContinue(FunctionBuilder fb, ContinueStatementSyntax contStmt) {
    if (fb.LoopStack.Count == 0) {
      throw new InvalidOperationException("'continue' encountered outside a loop.");
    }

    fb.Terminate(new Br(fb.LoopStack.Peek().ContinueBlock, contStmt.Span));
  }

  private void LowerAssert(FunctionBuilder fb, AssertStatementSyntax assertStmt) {
    string condSource = SourceSliceTrimmed(fb.Model, assertStmt.Condition.Span);
    ExpressionSyntax inner = UnwrapParens(assertStmt.Condition);

    if (inner is BinaryExpressionSyntax binary && IsAssertComparisonOp(binary.OperatorToken.Kind)) {
      if (!TryLowerAssertOperand(fb, binary.Left, out IrOperand rawLeft, out IrOperand left)) {
        return;
      }

      if (!TryLowerAssertOperand(fb, binary.Right, out IrOperand rawRight, out IrOperand right)) {
        return;
      }

      IrOperand cond = binary.OperatorToken.Kind switch {
        TokenKind.EqualEqual => EmitEquality(fb, left, right, negated: false, binary.Span),
        TokenKind.BangEqual => EmitEquality(fb, left, right, negated: true, binary.Span),
        _ => EmitOrderedComparison(fb, binary.OperatorToken.Kind, left, right, binary.Span),
      };

      IrBasicBlock bbOk = fb.NewBlock();
      IrBasicBlock bbFail = fb.NewBlock();
      fb.Terminate(new BrIf(cond, bbOk.Id, bbFail.Id, assertStmt.Span));

      fb.SwitchTo(bbFail);
      var parts = ImmutableArray.CreateBuilder<IrOperand>();
      if (assertStmt.Message is { } msgExpr) {
        IrOperand? msgOpt = LowerExpression(fb, msgExpr);
        if (fb.IsTerminated || msgOpt is not { } userMsg) {
          fb.SwitchTo(bbOk);
          return;
        }

        parts.Add(userMsg);
        parts.Add(fb.EmitConstString("\n", assertStmt.Span));
      }

      parts.Add(fb.EmitConstString($"`{condSource}` was false.", assertStmt.Span));

      if (!IsAssertLiteral(binary.Left)) {
        string leftSource = SourceSliceTrimmed(fb.Model, binary.Left.Span);
        parts.Add(fb.EmitConstString($"\n{leftSource} is ", assertStmt.Span));
        AppendAssertFormattedValue(fb, rawLeft, parts, assertStmt.Span);
      }

      if (!IsAssertLiteral(binary.Right)) {
        string rightSource = SourceSliceTrimmed(fb.Model, binary.Right.Span);
        parts.Add(fb.EmitConstString($"\n{rightSource} is ", assertStmt.Span));
        AppendAssertFormattedValue(fb, rawRight, parts, assertStmt.Span);
      }

      IrOperand finalMsg = CombineAssertParts(fb, parts, assertStmt.Span);
      fb.Terminate(new Panic(finalMsg, assertStmt.Span, IsAssert: true));
      fb.SwitchTo(bbOk);
      return;
    }

    IrOperand? condOpt = LowerExpression(fb, assertStmt.Condition);
    if (fb.IsTerminated || condOpt is not { } nonCmpCond) {
      return;
    }

    IrBasicBlock bbOkPlain = fb.NewBlock();
    IrBasicBlock bbFailPlain = fb.NewBlock();
    fb.Terminate(new BrIf(nonCmpCond, bbOkPlain.Id, bbFailPlain.Id, assertStmt.Span));

    fb.SwitchTo(bbFailPlain);
    var plainParts = ImmutableArray.CreateBuilder<IrOperand>();
    if (assertStmt.Message is { } plainMsgExpr) {
      IrOperand? msgOpt = LowerExpression(fb, plainMsgExpr);
      if (fb.IsTerminated || msgOpt is not { } userMsg) {
        fb.SwitchTo(bbOkPlain);
        return;
      }

      plainParts.Add(userMsg);
      plainParts.Add(fb.EmitConstString("\n", assertStmt.Span));
    }

    plainParts.Add(fb.EmitConstString($"`{condSource}` was false.", assertStmt.Span));
    IrOperand plainFinalMsg = CombineAssertParts(fb, plainParts, assertStmt.Span);
    fb.Terminate(new Panic(plainFinalMsg, assertStmt.Span, IsAssert: true));
    fb.SwitchTo(bbOkPlain);
  }

  private static bool IsAssertComparisonOp(TokenKind kind) =>
      kind is TokenKind.EqualEqual
          or TokenKind.BangEqual
          or TokenKind.Less
          or TokenKind.LessEqual
          or TokenKind.Greater
          or TokenKind.GreaterEqual;

  private static bool IsAssertLiteral(ExpressionSyntax expr) {
    ExpressionSyntax inner = UnwrapParens(expr);
    if (inner is LiteralExpressionSyntax) {
      return true;
    }

    if (inner is UnaryExpressionSyntax { OperatorToken.Kind: TokenKind.Minus } unary
        && UnwrapParens(unary.Operand) is LiteralExpressionSyntax { Token.Kind: TokenKind.IntLiteral or TokenKind.FloatLiteral }) {
      return true;
    }

    if (inner is InterpolatedStringExpressionSyntax interp) {
      foreach (StringPartSyntax part in interp.Parts) {
        if (part is StringInterpolationPartSyntax) {
          return false;
        }
      }

      return true;
    }

    return false;
  }

  private static string SourceSliceTrimmed(SemanticModel model, SourceSpan span) {
    SourceText source = model.Tree.Source;
    int start = Math.Clamp(span.Start, 0, source.Length);
    int end = Math.Clamp(span.End, start, source.Length);
    return source.Text[start..end].Trim();
  }

  private bool TryLowerAssertOperand(
      FunctionBuilder fb,
      ExpressionSyntax expr,
      out IrOperand rawVal,
      out IrOperand convertedVal) {
    rawVal = default;
    convertedVal = default;
    if (fb.Model.GetConversion(expr) is { Kind: ConversionKind.ImplicitItLambda }) {
      IrOperand? lowered = LowerExpression(fb, expr);
      if (fb.IsTerminated || lowered is not { } v) {
        return false;
      }

      rawVal = v;
      convertedVal = v;
      return true;
    }

    IrOperand? core = LowerExpressionCore(fb, expr);
    if (fb.IsTerminated || core is not { } raw) {
      return false;
    }

    IrOperand? conv = ApplyConversion(fb, expr, raw, expr.Span);
    if (fb.IsTerminated || conv is not { } converted) {
      return false;
    }

    rawVal = raw;
    convertedVal = converted;
    return true;
  }

  private void AppendAssertFormattedValue(
      FunctionBuilder fb,
      IrOperand value,
      ImmutableArray<IrOperand>.Builder parts,
      SourceSpan span) {
    if (fb.GetOperandType(value) == _types.String) {
      parts.Add(fb.EmitConstString("\"", span));
      parts.Add(value);
      parts.Add(fb.EmitConstString("\"", span));
      return;
    }

    IrTempId formatted = fb.AddTemp(_types.String);
    fb.Emit(new CallRuntime(formatted, IrRuntimeOp.ToStr, [value], AllocRegion.Heap, span));
    parts.Add(formatted);
  }

  private IrOperand CombineAssertParts(
      FunctionBuilder fb,
      ImmutableArray<IrOperand>.Builder parts,
      SourceSpan span) {
    if (parts.Count == 1) {
      return parts[0];
    }

    IrTempId combined = fb.AddTemp(_types.String);
    fb.Emit(new StrConcatN(combined, parts.ToImmutable(), AllocRegion.Heap, span));
    return combined;
  }

  private IrOperand? LowerAssignment(FunctionBuilder fb, AssignmentExpressionSyntax assign, bool resultUsed = true) {
    TokenKind op = assign.OperatorToken.Kind;
    ExpressionSyntax target = UnwrapParens(assign.Target);

    // Boxed-cell captured `var` direct assignment (D-056): route through CellGet / CellSet.
    if (target is IdentifierExpressionSyntax cellId
        && fb.Model.GetReferencedSymbol(cellId) is LocalSymbol cellLocal
        && !IsSelfLocal(cellLocal)
        && !_globalIds.ContainsKey(cellLocal)
        && IsSymbolBoxedCell(fb, cellLocal)) {
      if (op == TokenKind.Assign) {
        IrOperand? rhsOpt = LowerExpression(fb, assign.Value);
        if (fb.IsTerminated || rhsOpt is not { } rhs) {
          return null;
        }

        EmitCellWrite(fb, cellLocal, rhs, assign.Span);
        return rhs;
      } else {
        IrOperand oldVal = EmitCellRead(fb, cellLocal, assign.Span);
        IrOperand? rhsOpt = LowerExpression(fb, assign.Value);
        if (fb.IsTerminated || rhsOpt is not { } rhs) {
          return null;
        }

        IrOperand updated = EmitArithmeticOp(fb, op, oldVal, rhs, assign.Span);
        EmitCellWrite(fb, cellLocal, updated, assign.Span);
        return updated;
      }
    }

    // Direct local `var` fast path: avoids redundant addr_local/store_ptr for clean TACKY IR.
    if (target is IdentifierExpressionSyntax id
        && fb.Model.GetReferencedSymbol(id) is LocalSymbol localSym
        && !IsSelfLocal(localSym)
        && !_globalIds.ContainsKey(localSym)
        && fb.LocalSlots.TryGetValue(localSym, out IrSlotId localSlot)) {
      if (TryExtractLocalStringAppendRhs(fb, assign, localSym, localSlot, out ExpressionSyntax? appendRhsExpr)) {
        IrOperand? rhsOpt = LowerExpression(fb, appendRhsExpr!);
        if (fb.IsTerminated || rhsOpt is not { } rhs) {
          return null;
        }

        fb.Emit(new StrAppendSlot(localSlot, rhs, AllocRegion.Heap, assign.Span));
        if (!resultUsed) {
          return null;
        }

        IrTempId appended = fb.AddTemp(_types.String);
        fb.Emit(new LoadSlot(appended, localSlot, assign.Span));
        return appended;
      }

      if (op == TokenKind.Assign) {
        IrOperand? rhsOpt = LowerExpression(fb, assign.Value);
        if (fb.IsTerminated || rhsOpt is not { } rhs) {
          return null;
        }

        fb.Emit(new StoreSlot(localSlot, rhs, assign.Span));
        return rhs;
      } else {
        IrTempId oldVal = fb.AddTemp(fb.Function.Slots[localSlot.Index].Type);
        fb.Emit(new LoadSlot(oldVal, localSlot, assign.Span));
        IrOperand? rhsOpt = LowerExpression(fb, assign.Value);
        if (fb.IsTerminated || rhsOpt is not { } rhs) {
          return null;
        }

        IrOperand updated = EmitArithmeticOp(fb, op, oldVal, rhs, assign.Span);
        fb.Emit(new StoreSlot(localSlot, updated, assign.Span));
        return updated;
      }
    }

    // Map direct index assignment `m[k] = v` (MAP-5: inserts if key is absent, unlike MapElemAddr!).
    if (op == TokenKind.Assign
        && target is IndexExpressionSyntax mapIndex
        && fb.GetNodeType(mapIndex.Target) is MapType) {
      IrOperand? mapPtrOpt = ResolvePlaceAddr(fb, mapIndex.Target, ensureUniqueLeaf: true);
      if (fb.IsTerminated || mapPtrOpt is not { } mapPtr) {
        return null;
      }

      IrOperand? keyOpt = LowerExpression(fb, mapIndex.Index);
      if (fb.IsTerminated || keyOpt is not { } keyVal) {
        return null;
      }

      IrOperand? rhsOpt = LowerExpression(fb, assign.Value);
      if (fb.IsTerminated || rhsOpt is not { } rhsVal) {
        return null;
      }

      fb.Emit(new MapSet(mapPtr, keyVal, rhsVal, assign.Span));
      return rhsVal;
    }

    // General place path (VAR-4, D-182): evaluate path subexpressions ONCE left-to-right,
    // run top-down EnsureUnique, then store or read-modify-write through the resolved ptr<T>.
    if (TryExtractPlaceStringAppendRhs(fb, assign, target, out ExpressionSyntax? placeAppendRhs)) {
      return LowerPlaceStringAppend(fb, assign, target, placeAppendRhs!, resultUsed);
    }

    IrOperand? placePtrOpt = ResolvePlaceAddr(fb, target, ensureUniqueLeaf: false);
    if (fb.IsTerminated || placePtrOpt is not { } placePtr) {
      return null;
    }

    if (op == TokenKind.Assign) {
      IrOperand? rhsOpt = LowerExpression(fb, assign.Value);
      if (fb.IsTerminated || rhsOpt is not { } rhs) {
        return null;
      }

      var dstPtrType = (IrPtrType) _types.GetType(fb.GetOperandType(placePtr));
      IrOperand coercedRhs = CoerceOperandToTargetType(fb, rhs, dstPtrType.TargetType, assign.Span);
      fb.Emit(new StorePtr(placePtr, coercedRhs, assign.Span));
      return coercedRhs;
    }

    var ptrType = (IrPtrType) _types.GetType(fb.GetOperandType(placePtr));
    IrTempId curVal = fb.AddTemp(ptrType.TargetType);
    fb.Emit(new LoadPtr(curVal, placePtr, assign.Span));
    IrOperand? compRhsOpt = LowerExpression(fb, assign.Value);
    if (fb.IsTerminated || compRhsOpt is not { } compRhs) {
      return null;
    }

    IrOperand newVal = EmitArithmeticOp(fb, op, curVal, compRhs, assign.Span);
    fb.Emit(new StorePtr(placePtr, newVal, assign.Span));
    return newVal;
  }

  private IrOperand? LowerPlaceStringAppend(
      FunctionBuilder fb,
      AssignmentExpressionSyntax assign,
      ExpressionSyntax target,
      ExpressionSyntax rhsExpr,
      bool resultUsed) {
    Dictionary<IndexExpressionSyntax, IrOperand>? preEvaluatedIndices = null;
    var indexNodes = new List<IndexExpressionSyntax>();
    CollectPlaceIndexExpressionsLeftToRight(target, indexNodes);
    if (indexNodes.Count > 0) {
      preEvaluatedIndices = new Dictionary<IndexExpressionSyntax, IrOperand>(indexNodes.Count);
      foreach (IndexExpressionSyntax idxNode in indexNodes) {
        IrOperand? keyOpt = LowerExpression(fb, idxNode.Index);
        if (fb.IsTerminated || keyOpt is not { } keyVal) {
          return null;
        }

        preEvaluatedIndices[idxNode] = keyVal;
      }
    }

    IrOperand? rhsOpt = LowerExpression(fb, rhsExpr);
    if (fb.IsTerminated || rhsOpt is not { } rhs) {
      return null;
    }

    IrOperand? placePtrOpt = ResolvePlaceAddr(
        fb,
        target,
        ensureUniqueLeaf: false,
        preEvaluatedIndices: preEvaluatedIndices);
    if (fb.IsTerminated || placePtrOpt is not { } placePtr) {
      return null;
    }

    fb.Emit(new StrAppendPtr(placePtr, rhs, AllocRegion.Heap, assign.Span));
    if (!resultUsed) {
      return null;
    }

    IrTempId appended = fb.AddTemp(_types.String);
    fb.Emit(new LoadPtr(appended, placePtr, assign.Span));
    return appended;
  }

  private bool TryExtractPlaceStringAppendRhs(
      FunctionBuilder fb,
      AssignmentExpressionSyntax assign,
      ExpressionSyntax target,
      out ExpressionSyntax? rhsExpr) {
    rhsExpr = null;
    if (!ReferenceEquals(fb.GetNodeType(target), _compilation.Types.String)) {
      return false;
    }

    TokenKind op = assign.OperatorToken.Kind;
    ExpressionSyntax candidate;
    if (op == TokenKind.PlusAssign) {
      candidate = assign.Value;
    } else if (op == TokenKind.Assign
        && fb.Model.GetConversion(assign.Value) is null or { Kind: ConversionKind.None }
        && UnwrapParens(assign.Value) is BinaryExpressionSyntax { OperatorToken.Kind: TokenKind.Plus } bin
        && fb.Model.GetConversion(bin.Left) is null or { Kind: ConversionKind.None }
        && AreSamePurePlace(fb.Model, target, bin.Left)) {
      candidate = bin.Right;
    } else {
      return false;
    }

    if (fb.Model.GetType(candidate) is not { } rawRhsType
        || !ReferenceEquals(fb.ResolveType(rawRhsType), _compilation.Types.String)) {
      return false;
    }

    if (ExpressionMayMutatePlace(fb, candidate, target)) {
      return false;
    }

    rhsExpr = candidate;
    return true;
  }

  private static void CollectPlaceIndexExpressionsLeftToRight(
      ExpressionSyntax expr,
      List<IndexExpressionSyntax> indices) {
    switch (UnwrapParens(expr)) {
      case MemberAccessExpressionSyntax mem:
        CollectPlaceIndexExpressionsLeftToRight(mem.Target, indices);
        break;
      case IndexExpressionSyntax idx:
        CollectPlaceIndexExpressionsLeftToRight(idx.Target, indices);
        indices.Add(idx);
        break;
    }
  }

  private static bool AreSamePurePlace(SemanticModel model, ExpressionSyntax a, ExpressionSyntax b) {
    ExpressionSyntax ua = UnwrapParens(a);
    ExpressionSyntax ub = UnwrapParens(b);

    if (ua is IdentifierExpressionSyntax idA && ub is IdentifierExpressionSyntax idB) {
      return model.GetReferencedSymbol(idA) is { } symA
          && ReferenceEquals(symA, model.GetReferencedSymbol(idB));
    }

    if (IsSelfMemberMatchingBareField(model, ua, ub) || IsSelfMemberMatchingBareField(model, ub, ua)) {
      return true;
    }

    if (ua is MemberAccessExpressionSyntax memA && ub is MemberAccessExpressionSyntax memB) {
      return AreSamePurePlace(model, memA.Target, memB.Target)
          && NameOf(model, memA.MemberToken) == NameOf(model, memB.MemberToken);
    }

    if (ua is IndexExpressionSyntax idxA && ub is IndexExpressionSyntax idxB) {
      return AreSamePurePlace(model, idxA.Target, idxB.Target)
          && AreSamePureIndexExpr(model, idxA.Index, idxB.Index);
    }

    return false;
  }

  private static bool IsSelfMemberMatchingBareField(SemanticModel model, ExpressionSyntax memberCandidate, ExpressionSyntax identCandidate) =>
      memberCandidate is MemberAccessExpressionSyntax mem
          && UnwrapParens(mem.Target) is IdentifierExpressionSyntax selfId
          && model.GetReferencedSymbol(selfId) is LocalSymbol selfLoc
          && IsSelfLocal(selfLoc)
          && identCandidate is IdentifierExpressionSyntax bareId
          && model.GetReferencedSymbol(bareId) is FieldSymbol bareField
          && model.GetReferencedSymbol(mem) is FieldSymbol memField
          && ReferenceEquals(bareField, memField);

  private static bool AreSamePureIndexExpr(SemanticModel model, ExpressionSyntax a, ExpressionSyntax b) {
    ExpressionSyntax ua = UnwrapParens(a);
    ExpressionSyntax ub = UnwrapParens(b);

    if (ua is LiteralExpressionSyntax litA && ub is LiteralExpressionSyntax litB) {
      return litA.Token.Kind == litB.Token.Kind
          && NameOf(model, litA.Token) == NameOf(model, litB.Token);
    }

    if (ua is IdentifierExpressionSyntax idA && ub is IdentifierExpressionSyntax idB) {
      return model.GetReferencedSymbol(idA) is { } symA
          && ReferenceEquals(symA, model.GetReferencedSymbol(idB));
    }

    if (ua is MemberAccessExpressionSyntax memA && ub is MemberAccessExpressionSyntax memB) {
      return AreSamePureIndexExpr(model, memA.Target, memB.Target)
          && NameOf(model, memA.MemberToken) == NameOf(model, memB.MemberToken);
    }

    if (ua is UnaryExpressionSyntax unA && ub is UnaryExpressionSyntax unB) {
      return unA.OperatorToken.Kind == unB.OperatorToken.Kind
          && AreSamePureIndexExpr(model, unA.Operand, unB.Operand);
    }

    if (ua is BinaryExpressionSyntax binA && ub is BinaryExpressionSyntax binB) {
      return binA.OperatorToken.Kind == binB.OperatorToken.Kind
          && binA.OperatorToken.Kind is TokenKind.Plus or TokenKind.Minus or TokenKind.Star
          && AreSamePureIndexExpr(model, binA.Left, binB.Left)
          && AreSamePureIndexExpr(model, binA.Right, binB.Right);
    }

    return false;
  }

  private bool ExpressionMayMutatePlace(FunctionBuilder fb, SyntaxNode root, ExpressionSyntax targetPlace) {
    if (TryGetPlaceRootIdentifier(targetPlace) is not { } targetRootId
        || fb.Model.GetReferencedSymbol(targetRootId) is not { } targetRootSym) {
      return true;
    }

    bool targetIsSelf = targetRootSym is FieldSymbol
        || (targetRootSym is LocalSymbol selfLoc && IsSelfLocal(selfLoc));
    bool targetIsGlobalOrCell = targetRootSym is LocalSymbol localSym
        && (_globalIds.ContainsKey(localSym) || IsSymbolBoxedCell(fb, localSym));

    var stack = new Stack<SyntaxNode>();
    stack.Push(root);
    while (stack.Count > 0) {
      SyntaxNode node = stack.Pop();

      if (targetIsGlobalOrCell && node is CallExpressionSyntax) {
        return true;
      }

      if (node is AssignmentExpressionSyntax innerAssign
          && TryGetPlaceRootIdentifier(innerAssign.Target) is { } assignRoot
          && MatchesPlaceRoot(fb.Model.GetReferencedSymbol(assignRoot), targetRootSym, targetIsSelf)) {
        return true;
      }

      if (node is ArgumentSyntax { MutKeyword: not null } mutArg
          && TryGetPlaceRootIdentifier(mutArg.Expression) is { } mutRoot
          && MatchesPlaceRoot(fb.Model.GetReferencedSymbol(mutRoot), targetRootSym, targetIsSelf)) {
        return true;
      }

      if (node is CallExpressionSyntax call) {
        CallResolution? res = fb.Model.GetCallResolution(call);
        if (res is { Kind: CallKind.Intrinsic, Intrinsic: { } intr } && IsMutatingReceiverIntrinsic(intr)) {
          if (UnwrapParens(call.Callee) is MemberAccessExpressionSyntax mem
              && TryGetPlaceRootIdentifier(mem.Target) is { } recvRoot
              && MatchesPlaceRoot(fb.Model.GetReferencedSymbol(recvRoot), targetRootSym, targetIsSelf)) {
            return true;
          }
        } else if (res is { Kind: CallKind.InstanceMethod, TargetSymbol: MethodSymbol { IsMutating: true } }) {
          if (res.HasImplicitSelf && targetIsSelf) {
            return true;
          }

          if (!res.HasImplicitSelf
              && UnwrapParens(call.Callee) is MemberAccessExpressionSyntax mem
              && TryGetPlaceRootIdentifier(mem.Target) is { } recvRoot
              && MatchesPlaceRoot(fb.Model.GetReferencedSymbol(recvRoot), targetRootSym, targetIsSelf)) {
            return true;
          }
        }
      }

      foreach (SyntaxElement child in node.GetChildren()) {
        if (child.IsNode) {
          stack.Push(child.AsNode());
        }
      }
    }

    return false;
  }

  private static bool MatchesPlaceRoot(Symbol? candidateSym, Symbol targetRootSym, bool targetIsSelf) {
    if (candidateSym is null) {
      return false;
    }

    if (ReferenceEquals(candidateSym, targetRootSym)) {
      return true;
    }

    return targetIsSelf
        && (candidateSym is FieldSymbol || (candidateSym is LocalSymbol local && IsSelfLocal(local)));
  }

  private bool TryExtractLocalStringAppendRhs(
      FunctionBuilder fb,
      AssignmentExpressionSyntax assign,
      LocalSymbol localSym,
      IrSlotId localSlot,
      out ExpressionSyntax? rhsExpr) {
    rhsExpr = null;
    if (fb.Function.Slots[localSlot.Index].Type != _types.String) {
      return false;
    }

    TokenKind op = assign.OperatorToken.Kind;
    ExpressionSyntax candidate;
    if (op == TokenKind.PlusAssign) {
      candidate = assign.Value;
    } else if (op == TokenKind.Assign
        && fb.Model.GetConversion(assign.Value) is null or { Kind: ConversionKind.None }
        && UnwrapParens(assign.Value) is BinaryExpressionSyntax { OperatorToken.Kind: TokenKind.Plus } bin
        && fb.Model.GetConversion(bin.Left) is null or { Kind: ConversionKind.None }
        && UnwrapParens(bin.Left) is IdentifierExpressionSyntax leftId
        && ReferenceEquals(fb.Model.GetReferencedSymbol(leftId), localSym)) {
      candidate = bin.Right;
    } else {
      return false;
    }

    if (fb.Model.GetType(candidate) is not { } rawRhsType
        || !ReferenceEquals(fb.ResolveType(rawRhsType), _compilation.Types.String)) {
      return false;
    }

    if (ExpressionMayMutateLocal(fb.Model, candidate, localSym)) {
      return false;
    }

    rhsExpr = candidate;
    return true;
  }

  private static bool ExpressionMayMutateLocal(SemanticModel model, SyntaxNode root, LocalSymbol target) {
    var stack = new Stack<SyntaxNode>();
    stack.Push(root);
    while (stack.Count > 0) {
      SyntaxNode node = stack.Pop();
      if (node is AssignmentExpressionSyntax innerAssign
          && TryGetPlaceRootIdentifier(innerAssign.Target) is { } assignRoot
          && ReferenceEquals(model.GetReferencedSymbol(assignRoot), target)) {
        return true;
      }

      if (node is ArgumentSyntax { MutKeyword: not null } mutArg
          && TryGetPlaceRootIdentifier(mutArg.Expression) is { } mutRoot
          && ReferenceEquals(model.GetReferencedSymbol(mutRoot), target)) {
        return true;
      }

      foreach (SyntaxElement child in node.GetChildren()) {
        if (child.IsNode) {
          stack.Push(child.AsNode());
        }
      }
    }

    return false;
  }

  private static IdentifierExpressionSyntax? TryGetPlaceRootIdentifier(ExpressionSyntax expr) {
    ExpressionSyntax cur = UnwrapParens(expr);
    while (true) {
      switch (cur) {
        case IdentifierExpressionSyntax id:
          return id;
        case MemberAccessExpressionSyntax mem:
          cur = UnwrapParens(mem.Target);
          break;
        case IndexExpressionSyntax idx:
          cur = UnwrapParens(idx.Target);
          break;
        default:
          return null;
      }
    }
  }
}
