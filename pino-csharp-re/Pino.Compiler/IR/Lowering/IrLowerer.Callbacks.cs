using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.IR;

public sealed partial class IrLowerer {
  // ---------------------------------------------------------------------------
  // Inline IR Loop Lowering for All 19 Callback Intrinsics (design.md §5.9)
  // ---------------------------------------------------------------------------

  private bool TryResolveUnrecordedBuiltinCallback(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      out IntrinsicId intrinsic) {
    intrinsic = default;
    if (call.Arguments.Length != 1
        || call.Callee is not MemberAccessExpressionSyntax memCall) {
      return false;
    }

    if (UnwrapParens(call.Arguments[0].Expression) is not IdentifierExpressionSyntax id
        || fb.Model.GetReferencedSymbol(id) is not FunctionSymbol { Intrinsic: not null }) {
      return false;
    }

    TypeSymbol? targetType = fb.Model.GetType(memCall.Target) is { } rawTarget
        ? fb.ResolveType(rawTarget)
        : null;
    string methodName = NameOf(fb.Model, memCall.MemberToken);

    if (targetType is VectorType) {
      intrinsic = methodName switch {
        "each" => IntrinsicId.VecEach1,
        "map" => IntrinsicId.VecMap1,
        "filter" => IntrinsicId.VecFilter1,
        "find" => IntrinsicId.VecFind1,
        "find_index" => IntrinsicId.VecFindIndex1,
        "any" => IntrinsicId.VecAny1,
        "all" => IntrinsicId.VecAll1,
        "count" => IntrinsicId.VecCount1,
        _ => default,
      };
      return intrinsic != default;
    }

    if (targetType is SetType) {
      intrinsic = methodName switch {
        "each" => IntrinsicId.SetEach,
        "any" => IntrinsicId.SetAny,
        "all" => IntrinsicId.SetAll,
        _ => default,
      };
      return intrinsic != default;
    }

    return false;
  }

  private IrOperand? LowerCallbackIntrinsic(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      IntrinsicId intrinsic) {
    SourceSpan span = call.Span;
    var memCallee = (MemberAccessExpressionSyntax) call.Callee;
    ExpressionSyntax cbArgExpr = call.Arguments[0].Expression;

    // 1. Evaluate receiver first (left-to-right).
    IrOperand? recvOpt = LowerExpression(fb, memCallee.Target);
    if (fb.IsTerminated || recvOpt is not { } recvVal) {
      return null;
    }

    TypeSymbol concreteRecvType = fb.GetNodeType(memCallee.Target);
    IrTypeId recvIrType = fb.GetOperandType(recvVal);

    // 2. Classify callback form:
    //    (a) Implicit `it` expression -> inline directly (0 EnvNew/ClosureMake)
    //    (b) Inline LambdaExpressionSyntax -> inline directly (0 EnvNew/ClosureMake)
    //    (c) Built-in function name (e.g. `println`, `str`) -> emit direct built-in inside loop (0 EnvNew/ClosureMake)
    //    (d) General function value expression -> evaluate once before loop, invoke via CallClosure
    ConversionInfo? cbConv = fb.Model.GetConversion(cbArgExpr);
    bool isImplicitIt = cbConv?.Kind == ConversionKind.ImplicitItLambda;
    LambdaExpressionSyntax? inlineLambda = !isImplicitIt
        ? UnwrapParens(cbArgExpr) as LambdaExpressionSyntax
        : null;
    FunctionSymbol? builtinCallback = !isImplicitIt && inlineLambda is null
        && UnwrapParens(cbArgExpr) is IdentifierExpressionSyntax cbId
        && fb.Model.GetReferencedSymbol(cbId) is FunctionSymbol { Intrinsic: not null } bFn
            ? bFn
            : null;

    IrOperand? closureVal = null;
    if (!isImplicitIt && inlineLambda is null && builtinCallback is null) {
      IrOperand? closOpt = LowerExpression(fb, cbArgExpr);
      if (fb.IsTerminated || closOpt is not { } cv) {
        return null;
      }

      closureVal = cv;
    }

    // Determine expected callback return type for coercion / result vector type.
    TypeSymbol elemSymType = concreteRecvType switch {
      VectorType vt => vt.ElementType,
      SetType st => st.ElementType,
      _ => throw new InvalidOperationException($"Unexpected callback receiver type '{concreteRecvType.DisplayName}'."),
    };
    TypeSymbol? callResultType = ResolveCallbackCallResultType(fb, call, intrinsic, elemSymType, builtinCallback);
    TypeSymbol? expectedCbRetType = intrinsic switch {
      IntrinsicId.VecEach1 or IntrinsicId.VecEach2 or IntrinsicId.SetEach => _compilation.Types.Void,
      IntrinsicId.VecMap1 or IntrinsicId.VecMap2 => ((VectorType) callResultType!).ElementType,
      _ => _compilation.Types.Bool,
    };

    if (intrinsic is IntrinsicId.SetAny or IntrinsicId.SetAll or IntrinsicId.SetEach) {
      return LowerSetCallbackLoop(
          fb,
          intrinsic,
          recvVal,
          recvIrType,
          elemSymType,
          cbArgExpr,
          isImplicitIt ? cbConv : null,
          inlineLambda,
          builtinCallback,
          closureVal,
          expectedCbRetType,
          span);
    }

    return LowerVecCallbackLoop(
        fb,
        intrinsic,
        recvVal,
        recvIrType,
        elemSymType,
        callResultType,
        cbArgExpr,
        isImplicitIt ? cbConv : null,
        inlineLambda,
        builtinCallback,
        closureVal,
        expectedCbRetType,
        span);
  }

  private IrOperand? LowerVecCallbackLoop(
      FunctionBuilder fb,
      IntrinsicId intrinsic,
      IrOperand vecVal,
      IrTypeId vecIrType,
      TypeSymbol elemSymType,
      TypeSymbol? callResultType,
      ExpressionSyntax cbArgExpr,
      ConversionInfo? implicitItConv,
      LambdaExpressionSyntax? inlineLambda,
      FunctionSymbol? builtinCallback,
      IrOperand? closureVal,
      TypeSymbol? expectedCbRetType,
      SourceSpan span) {
    var irVec = (IrVectorType) _types.GetType(vecIrType);
    bool passIndex = intrinsic is IntrinsicId.VecEach2 or IntrinsicId.VecMap2 or IntrinsicId.VecFilter2
        or IntrinsicId.VecFind2 or IntrinsicId.VecFindIndex2 or IntrinsicId.VecAny2
        or IntrinsicId.VecAll2 or IntrinsicId.VecCount2;

    // Snapshot iteration (D-177).
    IrSlotId snapSlot = fb.AddSlot("cb_snap", vecIrType);
    IrSlotId lenSlot = fb.AddSlot("cb_len", _types.Int);
    IrSlotId idxSlot = fb.AddSlot("cb_i", _types.Int, isMutSlot: true);
    fb.Emit(new StoreSlot(snapSlot, vecVal, span));

    IrTempId initSnap = fb.AddTemp(vecIrType);
    fb.Emit(new LoadSlot(initSnap, snapSlot, span));
    IrTempId initLen = fb.AddTemp(_types.Int);
    fb.Emit(new VecLen(initLen, initSnap, span));
    fb.Emit(new StoreSlot(lenSlot, initLen, span));
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0, span), span));

    // Allocate accumulator / result slot per intrinsic kind.
    IrSlotId? resSlot = null;
    IrTypeId? resIrType = null;
    switch (intrinsic) {
      case IntrinsicId.VecMap1:
      case IntrinsicId.VecMap2: {
          IrTypeId outVecType = InternType(callResultType!);
          resIrType = outVecType;
          resSlot = fb.AddSlot("map_out", outVecType, isMutSlot: true);
          IrTempId outVec = fb.AddTemp(outVecType);
          fb.Emit(new VecWithCap(outVec, initLen, AllocRegion.Heap, span));
          fb.Emit(new StoreSlot(resSlot.Value, outVec, span));
          break;
        }

      case IntrinsicId.VecFilter1:
      case IntrinsicId.VecFilter2: {
          resIrType = vecIrType;
          resSlot = fb.AddSlot("filter_out", vecIrType, isMutSlot: true);
          IrTempId outVec = fb.AddTemp(vecIrType);
          fb.Emit(new VecNew(outVec, [], AllocRegion.Heap, span));
          fb.Emit(new StoreSlot(resSlot.Value, outVec, span));
          break;
        }

      case IntrinsicId.VecFind1:
      case IntrinsicId.VecFind2: {
          IrTypeId optType = InternType(callResultType!);
          resIrType = optType;
          resSlot = fb.AddSlot("find_res", optType, isMutSlot: true);
          IrTempId noneVal = fb.AddTemp(optType);
          fb.Emit(new UnionNew(noneVal, VariantIndex: 1, [], UnionAllocRegion(optType), span));
          fb.Emit(new StoreSlot(resSlot.Value, noneVal, span));
          break;
        }

      case IntrinsicId.VecFindIndex1:
      case IntrinsicId.VecFindIndex2: {
          resIrType = _types.Int;
          resSlot = fb.AddSlot("find_idx", _types.Int, isMutSlot: true);
          fb.Emit(new StoreSlot(resSlot.Value, fb.EmitConstInt(-1, span), span));
          break;
        }

      case IntrinsicId.VecAny1:
      case IntrinsicId.VecAny2: {
          resIrType = _types.Bool;
          resSlot = fb.AddSlot("any_res", _types.Bool, isMutSlot: true);
          fb.Emit(new StoreSlot(resSlot.Value, fb.EmitConstBool(false, span), span));
          break;
        }

      case IntrinsicId.VecAll1:
      case IntrinsicId.VecAll2: {
          resIrType = _types.Bool;
          resSlot = fb.AddSlot("all_res", _types.Bool, isMutSlot: true);
          fb.Emit(new StoreSlot(resSlot.Value, fb.EmitConstBool(true, span), span));
          break;
        }

      case IntrinsicId.VecCount1:
      case IntrinsicId.VecCount2: {
          resIrType = _types.Int;
          resSlot = fb.AddSlot("count_res", _types.Int, isMutSlot: true);
          fb.Emit(new StoreSlot(resSlot.Value, fb.EmitConstInt(0, span), span));
          break;
        }
    }

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbLatch = fb.NewBlock();
    IrBasicBlock bbExit = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id, span));

    fb.SwitchTo(bbHead);
    IrTempId curI = fb.AddTemp(_types.Int);
    IrTempId curLen = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curI, idxSlot, span));
    fb.Emit(new LoadSlot(curLen, lenSlot, span));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curI, curLen, span));
    fb.Terminate(new BrIf(cond, bbBody.Id, bbExit.Id, span));

    fb.SwitchTo(bbBody);
    IrTempId bodySnap = fb.AddTemp(vecIrType);
    fb.Emit(new LoadSlot(bodySnap, snapSlot, span));
    IrTempId elem = fb.AddTemp(irVec.ElementType);
    fb.Emit(new VecGet(elem, bodySnap, curI, Unchecked: true, span));

    IrOperand? cbResult = EmitCallbackInvocation(
        fb,
        cbArgExpr,
        implicitItConv,
        inlineLambda,
        builtinCallback,
        closureVal,
        elem,
        elemSymType,
        passIndex ? curI : null,
        expectedCbRetType,
        span);

    if (!fb.IsTerminated) {
      EmitCallbackLoopBodyAction(fb, intrinsic, elem, curI, cbResult, resSlot, resIrType, bbLatch.Id, bbExit.Id, span);
    }

    fb.SwitchTo(bbLatch);
    if (HasPredecessor(fb.Function, bbLatch.Id)) {
      IrTempId latchI = fb.AddTemp(_types.Int);
      fb.Emit(new LoadSlot(latchI, idxSlot, span));
      IrTempId nextI = fb.AddTemp(_types.Int);
      fb.Emit(new Add(nextI, latchI, fb.EmitConstInt(1, span), span));
      fb.Emit(new StoreSlot(idxSlot, nextI, span));
      fb.Terminate(new Br(bbHead.Id, span));
    } else {
      fb.Terminate(new Unreachable(span));
    }

    fb.SwitchTo(bbExit);
    if (resSlot is null || resIrType is null) {
      return null;
    }

    IrTempId finalVal = fb.AddTemp(resIrType.Value);
    fb.Emit(new LoadSlot(finalVal, resSlot.Value, span));
    return finalVal;
  }

  private IrOperand? LowerSetCallbackLoop(
      FunctionBuilder fb,
      IntrinsicId intrinsic,
      IrOperand setVal,
      IrTypeId setIrType,
      TypeSymbol elemSymType,
      ExpressionSyntax cbArgExpr,
      ConversionInfo? implicitItConv,
      LambdaExpressionSyntax? inlineLambda,
      FunctionSymbol? builtinCallback,
      IrOperand? closureVal,
      TypeSymbol? expectedCbRetType,
      SourceSpan span) {
    var irSet = (IrSetType) _types.GetType(setIrType);

    IrSlotId snapSlot = fb.AddSlot("cb_snap", setIrType);
    IrSlotId slenSlot = fb.AddSlot("cb_slen", _types.Int);
    IrSlotId idxSlot = fb.AddSlot("cb_idx", _types.Int, isMutSlot: true);
    fb.Emit(new StoreSlot(snapSlot, setVal, span));

    IrTempId initSnap = fb.AddTemp(setIrType);
    fb.Emit(new LoadSlot(initSnap, snapSlot, span));
    IrTempId initSlen = fb.AddTemp(_types.Int);
    fb.Emit(new SetSlotLen(initSlen, initSnap, span));
    fb.Emit(new StoreSlot(slenSlot, initSlen, span));
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0, span), span));

    IrSlotId? resSlot = null;
    IrTypeId? resIrType = null;
    if (intrinsic == IntrinsicId.SetAny) {
      resIrType = _types.Bool;
      resSlot = fb.AddSlot("any_res", _types.Bool, isMutSlot: true);
      fb.Emit(new StoreSlot(resSlot.Value, fb.EmitConstBool(false, span), span));
    } else if (intrinsic == IntrinsicId.SetAll) {
      resIrType = _types.Bool;
      resSlot = fb.AddSlot("all_res", _types.Bool, isMutSlot: true);
      fb.Emit(new StoreSlot(resSlot.Value, fb.EmitConstBool(true, span), span));
    }

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbCheck = fb.NewBlock();
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbLatch = fb.NewBlock();
    IrBasicBlock bbExit = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id, span));

    fb.SwitchTo(bbHead);
    IrTempId curIdx = fb.AddTemp(_types.Int);
    IrTempId curSlen = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curIdx, idxSlot, span));
    fb.Emit(new LoadSlot(curSlen, slenSlot, span));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curIdx, curSlen, span));
    fb.Terminate(new BrIf(cond, bbCheck.Id, bbExit.Id, span));

    fb.SwitchTo(bbCheck);
    IrTempId checkSnap = fb.AddTemp(setIrType);
    fb.Emit(new LoadSlot(checkSnap, snapSlot, span));
    IrTempId isLive = fb.AddTemp(_types.Bool);
    fb.Emit(new SetSlotIsLive(isLive, checkSnap, curIdx, span));
    fb.Terminate(new BrIf(isLive, bbBody.Id, bbLatch.Id, span));

    fb.SwitchTo(bbBody);
    IrTempId elem = fb.AddTemp(irSet.ElementType);
    fb.Emit(new SetSlotElem(elem, checkSnap, curIdx, span));

    IrOperand? cbResult = EmitCallbackInvocation(
        fb,
        cbArgExpr,
        implicitItConv,
        inlineLambda,
        builtinCallback,
        closureVal,
        elem,
        elemSymType,
        indexArg: null,
        expectedCbRetType,
        span);

    if (!fb.IsTerminated) {
      EmitCallbackLoopBodyAction(fb, intrinsic, elem, curIdx, cbResult, resSlot, resIrType, bbLatch.Id, bbExit.Id, span);
    }

    fb.SwitchTo(bbLatch);
    IrTempId latchIdx = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(latchIdx, idxSlot, span));
    IrTempId nextIdx = fb.AddTemp(_types.Int);
    fb.Emit(new Add(nextIdx, latchIdx, fb.EmitConstInt(1, span), span));
    fb.Emit(new StoreSlot(idxSlot, nextIdx, span));
    fb.Terminate(new Br(bbHead.Id, span));

    fb.SwitchTo(bbExit);
    if (resSlot is null || resIrType is null) {
      return null;
    }

    IrTempId finalVal = fb.AddTemp(resIrType.Value);
    fb.Emit(new LoadSlot(finalVal, resSlot.Value, span));
    return finalVal;
  }

  private void EmitCallbackLoopBodyAction(
      FunctionBuilder fb,
      IntrinsicId intrinsic,
      IrOperand elem,
      IrOperand curI,
      IrOperand? cbResult,
      IrSlotId? resSlot,
      IrTypeId? resIrType,
      IrBlockId latchBlock,
      IrBlockId exitBlock,
      SourceSpan span) {
    switch (intrinsic) {
      case IntrinsicId.VecEach1:
      case IntrinsicId.VecEach2:
      case IntrinsicId.SetEach:
        fb.Terminate(new Br(latchBlock, span));
        break;

      case IntrinsicId.VecMap1:
      case IntrinsicId.VecMap2: {
          IrTempId outPtr = fb.AddTemp(_types.GetPtr(resIrType!.Value));
          fb.Emit(new AddrLocal(outPtr, resSlot!.Value, span));
          fb.Emit(new VecPush(outPtr, cbResult!.Value, span));
          fb.Terminate(new Br(latchBlock, span));
          break;
        }

      case IntrinsicId.VecFilter1:
      case IntrinsicId.VecFilter2: {
          IrBasicBlock bbKeep = fb.NewBlock();
          fb.Terminate(new BrIf(cbResult!.Value, bbKeep.Id, latchBlock, span));
          fb.SwitchTo(bbKeep);
          IrTempId outPtr = fb.AddTemp(_types.GetPtr(resIrType!.Value));
          fb.Emit(new AddrLocal(outPtr, resSlot!.Value, span));
          fb.Emit(new VecPush(outPtr, elem, span));
          fb.Terminate(new Br(latchBlock, span));
          break;
        }

      case IntrinsicId.VecFind1:
      case IntrinsicId.VecFind2: {
          IrBasicBlock bbFound = fb.NewBlock();
          fb.Terminate(new BrIf(cbResult!.Value, bbFound.Id, latchBlock, span));
          fb.SwitchTo(bbFound);
          IrTempId someVal = fb.AddTemp(resIrType!.Value);
          fb.Emit(new UnionNew(someVal, VariantIndex: 0, [elem], UnionAllocRegion(resIrType.Value), span));
          fb.Emit(new StoreSlot(resSlot!.Value, someVal, span));
          fb.Terminate(new Br(exitBlock, span));
          break;
        }

      case IntrinsicId.VecFindIndex1:
      case IntrinsicId.VecFindIndex2: {
          IrBasicBlock bbFound = fb.NewBlock();
          fb.Terminate(new BrIf(cbResult!.Value, bbFound.Id, latchBlock, span));
          fb.SwitchTo(bbFound);
          fb.Emit(new StoreSlot(resSlot!.Value, curI, span));
          fb.Terminate(new Br(exitBlock, span));
          break;
        }

      case IntrinsicId.VecAny1:
      case IntrinsicId.VecAny2:
      case IntrinsicId.SetAny: {
          IrBasicBlock bbFound = fb.NewBlock();
          fb.Terminate(new BrIf(cbResult!.Value, bbFound.Id, latchBlock, span));
          fb.SwitchTo(bbFound);
          fb.Emit(new StoreSlot(resSlot!.Value, fb.EmitConstBool(true, span), span));
          fb.Terminate(new Br(exitBlock, span));
          break;
        }

      case IntrinsicId.VecAll1:
      case IntrinsicId.VecAll2:
      case IntrinsicId.SetAll: {
          IrBasicBlock bbFail = fb.NewBlock();
          fb.Terminate(new BrIf(cbResult!.Value, latchBlock, bbFail.Id, span));
          fb.SwitchTo(bbFail);
          fb.Emit(new StoreSlot(resSlot!.Value, fb.EmitConstBool(false, span), span));
          fb.Terminate(new Br(exitBlock, span));
          break;
        }

      case IntrinsicId.VecCount1:
      case IntrinsicId.VecCount2: {
          IrBasicBlock bbInc = fb.NewBlock();
          fb.Terminate(new BrIf(cbResult!.Value, bbInc.Id, latchBlock, span));
          fb.SwitchTo(bbInc);
          IrTempId curCnt = fb.AddTemp(_types.Int);
          fb.Emit(new LoadSlot(curCnt, resSlot!.Value, span));
          IrTempId nextCnt = fb.AddTemp(_types.Int);
          fb.Emit(new Add(nextCnt, curCnt, fb.EmitConstInt(1, span), span));
          fb.Emit(new StoreSlot(resSlot.Value, nextCnt, span));
          fb.Terminate(new Br(latchBlock, span));
          break;
        }
    }
  }

  private IrOperand? EmitCallbackInvocation(
      FunctionBuilder fb,
      ExpressionSyntax cbArgExpr,
      ConversionInfo? implicitItConv,
      LambdaExpressionSyntax? inlineLambda,
      FunctionSymbol? builtinCallback,
      IrOperand? closureVal,
      IrOperand elemArg,
      TypeSymbol elemSymType,
      IrOperand? indexArg,
      TypeSymbol? expectedRetType,
      SourceSpan span) {
    // (a) Implicit `it` expression: inline directly with zero closure allocation!
    if (implicitItConv is not null) {
      LocalSymbol? itSym = implicitItConv.ImplicitItSymbol;
      IrOperand? savedIt = null;
      bool hadIt = false;
      if (itSym is not null) {
        hadIt = fb.LocalTemps.TryGetValue(itSym, out IrOperand prev);
        savedIt = prev;
        fb.LocalTemps[itSym] = elemArg;
      }

      IrOperand? rawRes = LowerExpressionCore(fb, cbArgExpr);

      if (itSym is not null) {
        if (hadIt) {
          fb.LocalTemps[itSym] = savedIt!.Value;
        } else {
          fb.LocalTemps.Remove(itSym);
        }
      }

      if (fb.IsTerminated || rawRes is not { } rv || expectedRetType is null || expectedRetType.IsVoid) {
        return null;
      }

      TypeSymbol actualType = fb.Model.GetType(cbArgExpr) is { } rawT ? fb.ResolveType(rawT) : expectedRetType;
      return CoerceValueToExpectedType(fb, rv, actualType, expectedRetType, cbArgExpr.Span);
    }

    // (b) Inline lambda expression (arrow or block): inline directly with zero closure allocation!
    if (inlineLambda is not null) {
      return EmitInlinedLambda(fb, inlineLambda, elemArg, indexArg, expectedRetType);
    }

    // (c) Built-in function passed by name (e.g. `[3, 4]:each(println)`): direct built-in call!
    if (builtinCallback is not null) {
      return EmitDirectBuiltinCallback(fb, builtinCallback, elemArg, elemSymType, expectedRetType, span);
    }

    // (d) Function value variable / general expression: invoke via CallClosure inside the loop!
    ImmutableArray<IrOperand> args = indexArg is { } idx ? [elemArg, idx] : [elemArg];
    return EmitClosureCall(fb, closureVal!.Value, args, span);
  }

  private IrOperand? EmitInlinedLambda(
      FunctionBuilder fb,
      LambdaExpressionSyntax lambda,
      IrOperand elemArg,
      IrOperand? indexArg,
      TypeSymbol? expectedRetType) {
    var boundParams = new List<(ParameterSymbol Sym, bool HadPrev, IrOperand Prev)>(lambda.Parameters.Length);
    for (int i = 0; i < lambda.Parameters.Length; i++) {
      if (fb.Model.GetDeclaredSymbol(lambda.Parameters[i]) is ParameterSymbol pSym) {
        bool had = fb.LocalTemps.TryGetValue(pSym, out IrOperand prev);
        boundParams.Add((pSym, had, prev));
        fb.LocalTemps[pSym] = i == 0 ? elemArg : indexArg!.Value;
      }
    }

    try {
      if (lambda.BodyExpression is { } bodyExpr) {
        IrOperand? val = LowerExpression(fb, bodyExpr);
        if (fb.IsTerminated || val is not { } v || expectedRetType is null || expectedRetType.IsVoid) {
          return null;
        }

        TypeSymbol actualType = fb.Model.GetType(bodyExpr) is { } rawT ? fb.ResolveType(rawT) : expectedRetType;
        return CoerceValueToExpectedType(fb, v, actualType, expectedRetType, bodyExpr.Span);
      }

      if (lambda.BodyBlock is { } bodyBlock) {
        bool hasVal = expectedRetType is not null && !expectedRetType.IsVoid && !expectedRetType.IsNever;
        IrTypeId? retIrType = hasVal ? InternType(expectedRetType!) : null;
        IrSlotId? retSlot = hasVal ? fb.AddSlot("cb_ret", retIrType!.Value, isMutSlot: true) : null;
        IrBasicBlock bbAfter = fb.NewBlock();

        fb.InlineReturnStack.Push(new InlineReturnTarget(retSlot, bbAfter.Id, hasVal ? expectedRetType : null));
        LowerBlockItems(fb, bodyBlock.Items);
        fb.InlineReturnStack.Pop();

        if (!fb.IsTerminated) {
          fb.Terminate(new Br(bbAfter.Id, bodyBlock.Span));
        }

        fb.SwitchTo(bbAfter);
        if (!HasPredecessor(fb.Function, bbAfter.Id)) {
          fb.Terminate(new Unreachable(bodyBlock.Span));
          return null;
        }

        if (!hasVal) {
          return null;
        }

        IrTempId loaded = fb.AddTemp(retIrType!.Value);
        fb.Emit(new LoadSlot(loaded, retSlot!.Value, bodyBlock.Span));
        return loaded;
      }

      return null;
    } finally {
      for (int i = boundParams.Count - 1; i >= 0; i--) {
        (ParameterSymbol sym, bool had, IrOperand prev) = boundParams[i];
        if (had) {
          fb.LocalTemps[sym] = prev;
        } else {
          fb.LocalTemps.Remove(sym);
        }
      }
    }
  }

  private IrOperand? EmitDirectBuiltinCallback(
      FunctionBuilder fb,
      FunctionSymbol builtinFn,
      IrOperand elemArg,
      TypeSymbol elemSymType,
      TypeSymbol? expectedRetType,
      SourceSpan span) {
    IntrinsicId id = builtinFn.Intrinsic!.Value;
    switch (builtinFn.Name) {
      case "println": {
          IrOperand strArg;
          if (fb.GetOperandType(elemArg) == _types.String) {
            strArg = elemArg;
          } else {
            IrTempId fmt = fb.AddTemp(_types.String);
            fb.Emit(new CallRuntime(fmt, IrRuntimeOp.ToStr, [elemArg], AllocRegion.Heap, span));
            strArg = fmt;
          }

          fb.Emit(new CallRuntime(null, IrRuntimeOp.PrintlnN, [strArg], AllocRegion.Heap, span));
          return null;
        }

      case "str": {
          if (fb.GetOperandType(elemArg) == _types.String) {
            return elemArg;
          }

          IrTempId fmt = fb.AddTemp(_types.String);
          fb.Emit(new CallRuntime(fmt, IrRuntimeOp.ToStr, [elemArg], AllocRegion.Heap, span));
          return fmt;
        }

      case "type": {
          if (elemSymType is InterfaceType) {
            IrTempId dyn = fb.AddTemp(_types.String);
            fb.Emit(new CallRuntime(dyn, IrRuntimeOp.IfaceTypeName, [elemArg], AllocRegion.Stack, span));
            return dyn;
          }

          return fb.EmitConstString(GetStaticTypeName(elemSymType), span);
        }

      case "int":
        id = elemSymType is PrimitiveType { Kind: PrimitiveKind.String }
            ? IntrinsicId.IntFromString
            : IntrinsicId.IntFromNumber;
        break;

      case "float":
        id = elemSymType is PrimitiveType { Kind: PrimitiveKind.String }
            ? IntrinsicId.FloatFromString
            : IntrinsicId.FloatFromNumber;
        break;

      case "rune":
        id = elemSymType is PrimitiveType { Kind: PrimitiveKind.String }
            ? IntrinsicId.RuneFromString
            : IntrinsicId.RuneFromInt;
        break;

      case "rand":
        id = IntrinsicId.RandInt;
        break;

      case "readline":
        id = IntrinsicId.ReadlineWithPrompt;
        break;
    }

    return EmitNonCallbackIntrinsicCore(
        fb,
        id,
        receiver: null,
        evaluatedArgs: [elemArg],
        callReturnType: expectedRetType ?? _compilation.Types.Void,
        span: span,
        firstArgSymType: elemSymType);
  }

  private TypeSymbol? ResolveCallbackCallResultType(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      IntrinsicId intrinsic,
      TypeSymbol elemSymType,
      FunctionSymbol? builtinCallback) {
    TypeSymbol? raw = fb.Model.GetType(call);
    if (raw is not null && !raw.IsError && raw is not (TypeVariableType or PendingReturnType)) {
      return fb.ResolveType(raw);
    }

    // Fallback when checker left call type as Error (e.g., `[3, 4]:each(println)`).
    return intrinsic switch {
      IntrinsicId.VecEach1 or IntrinsicId.VecEach2 or IntrinsicId.SetEach => _compilation.Types.Void,
      IntrinsicId.VecFilter1 or IntrinsicId.VecFilter2 => _compilation.Types.GetVector(elemSymType),
      IntrinsicId.VecFind1 or IntrinsicId.VecFind2 =>
          _compilation.Types.GetUnion(_compilation.Prelude.OptionUnion, [elemSymType]),
      IntrinsicId.VecFindIndex1 or IntrinsicId.VecFindIndex2 or IntrinsicId.VecCount1 or IntrinsicId.VecCount2 =>
          _compilation.Types.Int,
      IntrinsicId.VecAny1 or IntrinsicId.VecAny2 or IntrinsicId.VecAll1 or IntrinsicId.VecAll2
          or IntrinsicId.SetAny or IntrinsicId.SetAll => _compilation.Types.Bool,
      IntrinsicId.VecMap1 or IntrinsicId.VecMap2 when builtinCallback is not null =>
          _compilation.Types.GetVector(InferBuiltinReturnType(builtinCallback.Name, elemSymType)),
      _ => throw new InvalidOperationException($"Missing result type on callback intrinsic '{intrinsic}'."),
    };
  }

  private TypeSymbol InferBuiltinReturnType(string builtinName, TypeSymbol argType) => builtinName switch {
    "str" or "type" => _compilation.Types.String,
    "int" => argType is PrimitiveType { Kind: PrimitiveKind.String }
        ? _compilation.Types.GetUnion(_compilation.Prelude.ResultUnion, [_compilation.Types.Int, _compilation.Types.String])
        : _compilation.Types.Int,
    "float" => argType is PrimitiveType { Kind: PrimitiveKind.String }
        ? _compilation.Types.GetUnion(_compilation.Prelude.ResultUnion, [_compilation.Types.Float, _compilation.Types.String])
        : _compilation.Types.Float,
    "rune" => _compilation.Types.Rune,
    "file_exists" => _compilation.Types.Bool,
    "regex" => _compilation.Types.Regex,
    "rand" => _compilation.Types.Int,
    "read_file" => _compilation.Types.GetUnion(
        _compilation.Prelude.ResultUnion,
        [_compilation.Types.String, _compilation.Types.GetUnion(_compilation.Prelude.IOErrorUnion)]),
    "readline" => _compilation.Types.GetUnion(
        _compilation.Prelude.ResultUnion,
        [_compilation.Types.String, _compilation.Types.String]),
    _ => _compilation.Types.Void,
  };
}
