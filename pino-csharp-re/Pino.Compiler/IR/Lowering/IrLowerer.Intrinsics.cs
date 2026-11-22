using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.IR;

public sealed partial class IrLowerer {
  /// <summary>
  /// Lowers all 62 non-callback <see cref="IntrinsicId"/>s (<c>056-re-ir-design</c> §5.9).
  /// The 19 higher-order callback intrinsics are deferred to ticket 061.
  /// </summary>
  private IrOperand? LowerIntrinsicCall(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      IntrinsicId intrinsic) {
    // Reject the 19 callback intrinsics deferred to ticket 061 up front.
    if (IsCallbackIntrinsic(intrinsic)) {
      throw new IrLoweringUnsupportedException(
          $"Callback intrinsic '{intrinsic}' is deferred to ticket 061.");
    }

    SourceSpan span = call.Span;
    bool isMutatingReceiver = IsMutatingReceiverIntrinsic(intrinsic);

    // Non-mutating intrinsics evaluate receiver left-to-right before arguments;
    // mutating receiver intrinsics evaluate arguments BEFORE resolving the mutable receiver (FN-31).
    IrOperand? receiver = null;
    if (!isMutatingReceiver && call.Callee is MemberAccessExpressionSyntax nonMutMember) {
      receiver = LowerExpression(fb, nonMutMember.Target);
      if (fb.IsTerminated || receiver is null) {
        return null;
      }
    }

    var evaluatedArgs = new IrOperand[call.Arguments.Length];
    for (int i = 0; i < call.Arguments.Length; i++) {
      IrOperand? argVal = LowerExpression(fb, call.Arguments[i].Expression);
      if (fb.IsTerminated || argVal is not { } av) {
        return null;
      }

      evaluatedArgs[i] = av;
    }

    if (isMutatingReceiver && call.Callee is MemberAccessExpressionSyntax mutMember) {
      receiver = ResolvePlaceAddr(fb, mutMember.Target, ensureUniqueLeaf: true);
      if (fb.IsTerminated || receiver is null) {
        return null;
      }
    }

    switch (intrinsic) {
      // -----------------------------------------------------------------------
      // Global built-ins (#1..#21)
      // -----------------------------------------------------------------------

      case IntrinsicId.Println: {
          var strArgs = ImmutableArray.CreateBuilder<IrOperand>(evaluatedArgs.Length);
          for (int i = 0; i < evaluatedArgs.Length; i++) {
            IrOperand arg = evaluatedArgs[i];
            if (fb.GetOperandType(arg) == _types.String) {
              strArgs.Add(arg);
            } else {
              IrTempId formatted = fb.AddTemp(_types.String);
              fb.Emit(new CallRuntime(formatted, IrRuntimeOp.ToStr, [arg], AllocRegion.Heap, span));
              strArgs.Add(formatted);
            }
          }

          fb.Emit(new CallRuntime(null, IrRuntimeOp.PrintlnN, strArgs.MoveToImmutable(), AllocRegion.Heap, span));
          return null;
        }

      case IntrinsicId.ReadlineNoPrompt: {
          IrTempId empty = fb.EmitConstString(string.Empty, span);
          return EmitRuntimeCallWithResult(fb, call, IrRuntimeOp.Readline, [empty], AllocRegion.Heap);
        }

      case IntrinsicId.ReadlineWithPrompt:
        return EmitRuntimeCallWithResult(fb, call, IrRuntimeOp.Readline, [evaluatedArgs[0]], AllocRegion.Heap);

      case IntrinsicId.IntFromNumber: {
          IrOperand arg = evaluatedArgs[0];
          if (fb.GetOperandType(arg) == _types.Int) {
            return arg;
          }

          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new CastF64ToI64(d, arg, span));
          return d;
        }

      case IntrinsicId.IntFromString:
        return EmitRuntimeCallWithResult(fb, call, IrRuntimeOp.ParseInt, [evaluatedArgs[0]], AllocRegion.Heap);

      case IntrinsicId.FloatFromNumber: {
          IrOperand arg = evaluatedArgs[0];
          if (fb.GetOperandType(arg) == _types.Float) {
            return arg;
          }

          IrTempId d = fb.AddTemp(_types.Float);
          fb.Emit(new CastI64ToF64(d, arg, span));
          return d;
        }

      case IntrinsicId.FloatFromString:
        return EmitRuntimeCallWithResult(fb, call, IrRuntimeOp.ParseFloat, [evaluatedArgs[0]], AllocRegion.Heap);

      case IntrinsicId.RuneFromInt: {
          IrTempId d = fb.AddTemp(_types.Rune);
          fb.Emit(new CastI64ToRune(d, evaluatedArgs[0], span));
          return d;
        }

      case IntrinsicId.RuneFromString: {
          IrTempId d = fb.AddTemp(_types.Rune);
          fb.Emit(new CallRuntime(d, IrRuntimeOp.StrFirstRune, [evaluatedArgs[0]], AllocRegion.Stack, span));
          return d;
        }

      case IntrinsicId.Str: {
          IrOperand arg = evaluatedArgs[0];
          if (fb.GetOperandType(arg) == _types.String) {
            return arg;
          }

          IrTempId d = fb.AddTemp(_types.String);
          fb.Emit(new CallRuntime(d, IrRuntimeOp.ToStr, [arg], AllocRegion.Heap, span));
          return d;
        }

      case IntrinsicId.Type: {
          TypeSymbol argSymType = fb.Model.GetType(call.Arguments[0].Expression)
              ?? throw new InvalidOperationException("Missing argument type for 'type(...)'.");
          if (argSymType is InterfaceType) {
            throw new IrLoweringUnsupportedException("Interface 'type(...)' is deferred to ticket 061.");
          }

          return fb.EmitConstString(GetStaticTypeName(argSymType), span);
        }

      case IntrinsicId.RandFloat: {
          IrTempId d = fb.AddTemp(_types.Float);
          fb.Emit(new CallRuntime(d, IrRuntimeOp.RandFloat, [], AllocRegion.Stack, span));
          return d;
        }

      case IntrinsicId.RandInt: {
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new CallRuntime(d, IrRuntimeOp.RandInt, [evaluatedArgs[0]], AllocRegion.Stack, span));
          return d;
        }

      case IntrinsicId.Time: {
          IrTempId d = fb.AddTemp(_types.Float);
          fb.Emit(new CallRuntime(d, IrRuntimeOp.Time, [], AllocRegion.Stack, span));
          return d;
        }

      case IntrinsicId.Sleep:
        fb.Emit(new CallRuntime(null, IrRuntimeOp.Sleep, [evaluatedArgs[0]], AllocRegion.Stack, span));
        return null;

      case IntrinsicId.Clear:
        fb.Emit(new CallRuntime(null, IrRuntimeOp.Clear, [], AllocRegion.Stack, span));
        return null;

      case IntrinsicId.Panic:
        fb.Terminate(new Panic(evaluatedArgs[0], span));
        return null;

      case IntrinsicId.RegexCompile: {
          IrTempId d = fb.AddTemp(_types.Regex);
          fb.Emit(new CallRuntime(d, IrRuntimeOp.RegexCompile, [evaluatedArgs[0]], AllocRegion.Heap, span));
          return d;
        }

      case IntrinsicId.ReadFile:
        return EmitRuntimeCallWithResult(fb, call, IrRuntimeOp.ReadFile, [evaluatedArgs[0]], AllocRegion.Heap);

      case IntrinsicId.WriteFile:
        return EmitRuntimeCallWithResult(
            fb,
            call,
            IrRuntimeOp.WriteFile,
            [evaluatedArgs[0], evaluatedArgs[1]],
            AllocRegion.Heap);

      case IntrinsicId.FileExists: {
          IrTempId d = fb.AddTemp(_types.Bool);
          fb.Emit(new CallRuntime(d, IrRuntimeOp.FileExists, [evaluatedArgs[0]], AllocRegion.Stack, span));
          return d;
        }

      // -----------------------------------------------------------------------
      // String methods (#22..#37)
      // -----------------------------------------------------------------------

      case IntrinsicId.StrLen: {
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new StrLen(d, receiver!.Value, span));
          return d;
        }

      case IntrinsicId.StrLower:
        return EmitRuntimeOp1(fb, _types.String, IrRuntimeOp.StrLower, receiver!.Value, AllocRegion.Heap, span);

      case IntrinsicId.StrUpper:
        return EmitRuntimeOp1(fb, _types.String, IrRuntimeOp.StrUpper, receiver!.Value, AllocRegion.Heap, span);

      case IntrinsicId.StrTrim:
        return EmitRuntimeOp1(fb, _types.String, IrRuntimeOp.StrTrim, receiver!.Value, AllocRegion.Heap, span);

      case IntrinsicId.StrTrimStart:
        return EmitRuntimeOp1(fb, _types.String, IrRuntimeOp.StrTrimStart, receiver!.Value, AllocRegion.Heap, span);

      case IntrinsicId.StrTrimEnd:
        return EmitRuntimeOp1(fb, _types.String, IrRuntimeOp.StrTrimEnd, receiver!.Value, AllocRegion.Heap, span);

      case IntrinsicId.StrClone:
        return EmitRuntimeOp1(fb, _types.String, IrRuntimeOp.StrClone, receiver!.Value, AllocRegion.Heap, span);

      case IntrinsicId.StrToCString:
        return EmitRuntimeOp1(fb, _types.String, IrRuntimeOp.StrToCString, receiver!.Value, AllocRegion.Heap, span);

      case IntrinsicId.StrContains:
        return EmitRuntimeOp2(fb, _types.Bool, IrRuntimeOp.StrContains, receiver!.Value, evaluatedArgs[0], AllocRegion.Stack, span);

      case IntrinsicId.StrStartsWith:
        return EmitRuntimeOp2(fb, _types.Bool, IrRuntimeOp.StrStartsWith, receiver!.Value, evaluatedArgs[0], AllocRegion.Stack, span);

      case IntrinsicId.StrEndsWith:
        return EmitRuntimeOp2(fb, _types.Bool, IrRuntimeOp.StrEndsWith, receiver!.Value, evaluatedArgs[0], AllocRegion.Stack, span);

      case IntrinsicId.StrIndexOf:
        return EmitRuntimeOp2(fb, _types.Int, IrRuntimeOp.StrIndexOf, receiver!.Value, evaluatedArgs[0], AllocRegion.Stack, span);

      case IntrinsicId.StrLastIndexOf:
        return EmitRuntimeOp2(fb, _types.Int, IrRuntimeOp.StrLastIndexOf, receiver!.Value, evaluatedArgs[0], AllocRegion.Stack, span);

      case IntrinsicId.StrSplit: {
          IrTypeId vecStr = _types.GetVector(_types.String);
          return EmitRuntimeOp2(fb, vecStr, IrRuntimeOp.StrSplit, receiver!.Value, evaluatedArgs[0], AllocRegion.Heap, span);
        }

      case IntrinsicId.StrReplace: {
          IrTempId d = fb.AddTemp(_types.String);
          fb.Emit(new CallRuntime(
              d,
              IrRuntimeOp.StrReplace,
              [receiver!.Value, evaluatedArgs[0], evaluatedArgs[1]],
              AllocRegion.Heap,
              span));
          return d;
        }

      case IntrinsicId.StrSubstring: {
          IrTempId d = fb.AddTemp(_types.String);
          fb.Emit(new CallRuntime(
              d,
              IrRuntimeOp.StrSubstring,
              [receiver!.Value, evaluatedArgs[0], evaluatedArgs[1]],
              AllocRegion.Heap,
              span));
          return d;
        }

      // -----------------------------------------------------------------------
      // Vector methods (#38..#42, #59)
      // -----------------------------------------------------------------------

      case IntrinsicId.VecLen: {
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new VecLen(d, receiver!.Value, span));
          return d;
        }

      case IntrinsicId.VecCap: {
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new VecCap(d, receiver!.Value, span));
          return d;
        }

      case IntrinsicId.VecPush:
        fb.Emit(new VecPush(receiver!.Value, evaluatedArgs[0], span));
        return null;

      case IntrinsicId.VecPop: {
          IrTypeId retIr = InternType(fb.Model.GetType(call)!);
          IrTempId d = fb.AddTemp(retIr);
          fb.Emit(new VecPop(d, receiver!.Value, span));
          return d;
        }

      case IntrinsicId.VecSwap:
        fb.Emit(new VecSwap(receiver!.Value, evaluatedArgs[0], evaluatedArgs[1], span));
        return null;

      case IntrinsicId.VecJoin:
        return LowerVecJoin(fb, receiver!.Value, evaluatedArgs[0], span);

      // -----------------------------------------------------------------------
      // Map methods (#60..#63)
      // -----------------------------------------------------------------------

      case IntrinsicId.MapLen: {
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new MapLen(d, receiver!.Value, span));
          return d;
        }

      case IntrinsicId.MapKeys: {
          var mt = (IrMapType) _types.GetType(fb.GetOperandType(receiver!.Value));
          IrTempId d = fb.AddTemp(_types.GetVector(mt.KeyType));
          fb.Emit(new MapKeys(d, receiver.Value, AllocRegion.Heap, span));
          return d;
        }

      case IntrinsicId.MapValues: {
          var mt = (IrMapType) _types.GetType(fb.GetOperandType(receiver!.Value));
          IrTempId d = fb.AddTemp(_types.GetVector(mt.ValueType));
          fb.Emit(new MapValues(d, receiver.Value, AllocRegion.Heap, span));
          return d;
        }

      case IntrinsicId.MapRemove: {
          IrTypeId retIr = InternType(fb.Model.GetType(call)!);
          IrTempId d = fb.AddTemp(retIr);
          fb.Emit(new MapRemove(d, receiver!.Value, evaluatedArgs[0], span));
          return d;
        }

      // -----------------------------------------------------------------------
      // Set methods (#64..#66)
      // -----------------------------------------------------------------------

      case IntrinsicId.SetLen: {
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new SetLen(d, receiver!.Value, span));
          return d;
        }

      case IntrinsicId.SetAdd:
        fb.Emit(new SetAdd(receiver!.Value, evaluatedArgs[0], span));
        return null;

      case IntrinsicId.SetRemove:
        fb.Emit(new SetRemove(receiver!.Value, evaluatedArgs[0], span));
        return null;

      // -----------------------------------------------------------------------
      // Pool methods (#70..#75, D-185)
      // -----------------------------------------------------------------------

      case IntrinsicId.PoolLen: {
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new PoolLen(d, receiver!.Value, span));
          return d;
        }

      case IntrinsicId.PoolAdd: {
          var ptrT = (IrPtrType) _types.GetType(fb.GetOperandType(receiver!.Value));
          var pt = (IrPoolType) _types.GetType(ptrT.TargetType);
          IrTempId d = fb.AddTemp(_types.GetHandle(pt.ElementType));
          fb.Emit(new PoolAdd(d, receiver.Value, evaluatedArgs[0], span));
          return d;
        }

      case IntrinsicId.PoolRemove: {
          IrTypeId retIr = InternType(fb.Model.GetType(call)!);
          IrTempId d = fb.AddTemp(retIr);
          fb.Emit(new PoolRemove(d, receiver!.Value, evaluatedArgs[0], span));
          return d;
        }

      case IntrinsicId.PoolGet: {
          IrTypeId retIr = InternType(fb.Model.GetType(call)!);
          IrTempId d = fb.AddTemp(retIr);
          fb.Emit(new PoolGet(d, receiver!.Value, evaluatedArgs[0], span));
          return d;
        }

      case IntrinsicId.PoolHandles: {
          var pt = (IrPoolType) _types.GetType(fb.GetOperandType(receiver!.Value));
          IrTempId d = fb.AddTemp(_types.GetVector(_types.GetHandle(pt.ElementType)));
          fb.Emit(new PoolHandles(d, receiver.Value, AllocRegion.Heap, span));
          return d;
        }

      case IntrinsicId.PoolClear:
        fb.Emit(new PoolClear(receiver!.Value, span));
        return null;

      // -----------------------------------------------------------------------
      // Regex methods (#76..#81)
      // -----------------------------------------------------------------------

      case IntrinsicId.RegexPattern:
        return EmitRuntimeOp1(fb, _types.String, IrRuntimeOp.RegexPattern, receiver!.Value, AllocRegion.Stack, span);

      case IntrinsicId.RegexHasMatch:
        return EmitRuntimeOp2(fb, _types.Bool, IrRuntimeOp.RegexHasMatch, receiver!.Value, evaluatedArgs[0], AllocRegion.Stack, span);

      case IntrinsicId.RegexMatchPrefix:
        return EmitRuntimeOp2(fb, _types.String, IrRuntimeOp.RegexMatchPrefix, receiver!.Value, evaluatedArgs[0], AllocRegion.Heap, span);

      case IntrinsicId.RegexFind:
        return EmitRuntimeCallWithResult(fb, call, IrRuntimeOp.RegexFind, [receiver!.Value, evaluatedArgs[0]], AllocRegion.Heap);

      case IntrinsicId.RegexFindAll: {
          IrTypeId vecStr = _types.GetVector(_types.String);
          return EmitRuntimeOp2(fb, vecStr, IrRuntimeOp.RegexFindAll, receiver!.Value, evaluatedArgs[0], AllocRegion.Heap, span);
        }

      case IntrinsicId.RegexReplace: {
          IrTempId d = fb.AddTemp(_types.String);
          fb.Emit(new CallRuntime(
              d,
              IrRuntimeOp.RegexReplace,
              [receiver!.Value, evaluatedArgs[0], evaluatedArgs[1]],
              AllocRegion.Heap,
              span));
          return d;
        }

      default:
        throw new InvalidOperationException($"Unhandled intrinsic '{intrinsic}'.");
    }
  }

  private IrOperand LowerVecJoin(FunctionBuilder fb, IrOperand vecVal, IrOperand sepVal, SourceSpan span) {
    // VECLIB-27 / design.md §5.9 #59: lower vec:join(sep) in IR to a loop + to_str + str.concat.
    IrTypeId vecTypeId = fb.GetOperandType(vecVal);
    var vt = (IrVectorType) _types.GetType(vecTypeId);

    IrSlotId accSlot = fb.AddSlot("join_acc", _types.String, isMutSlot: true);
    IrSlotId idxSlot = fb.AddSlot("join_i", _types.Int, isMutSlot: true);
    fb.Emit(new StoreSlot(accSlot, fb.EmitConstString(string.Empty, span), span));
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0, span), span));

    IrTempId len = fb.AddTemp(_types.Int);
    fb.Emit(new VecLen(len, vecVal, span));

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbSep = fb.NewBlock();
    IrBasicBlock bbElem = fb.NewBlock();
    IrBasicBlock bbExit = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id, span));

    fb.SwitchTo(bbHead);
    IrTempId curI = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curI, idxSlot, span));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curI, len, span));
    fb.Terminate(new BrIf(cond, bbBody.Id, bbExit.Id, span));

    fb.SwitchTo(bbBody);
    IrTempId needSep = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(needSep, IrCmpOp.Gt, curI, fb.EmitConstInt(0, span), span));
    fb.Terminate(new BrIf(needSep, bbSep.Id, bbElem.Id, span));

    fb.SwitchTo(bbSep);
    IrTempId accBeforeSep = fb.AddTemp(_types.String);
    fb.Emit(new LoadSlot(accBeforeSep, accSlot, span));
    IrTempId accWithSep = fb.AddTemp(_types.String);
    fb.Emit(new StrConcat(accWithSep, accBeforeSep, sepVal, AllocRegion.Heap, span));
    fb.Emit(new StoreSlot(accSlot, accWithSep, span));
    fb.Terminate(new Br(bbElem.Id, span));

    fb.SwitchTo(bbElem);
    IrTempId elem = fb.AddTemp(vt.ElementType);
    fb.Emit(new VecGet(elem, vecVal, curI, Unchecked: true, span));
    IrOperand elemStr;
    if (vt.ElementType == _types.String) {
      elemStr = elem;
    } else {
      IrTempId fmt = fb.AddTemp(_types.String);
      fb.Emit(new CallRuntime(fmt, IrRuntimeOp.ToStr, [elem], AllocRegion.Heap, span));
      elemStr = fmt;
    }

    IrTempId accBeforeElem = fb.AddTemp(_types.String);
    fb.Emit(new LoadSlot(accBeforeElem, accSlot, span));
    IrTempId accWithElem = fb.AddTemp(_types.String);
    fb.Emit(new StrConcat(accWithElem, accBeforeElem, elemStr, AllocRegion.Heap, span));
    fb.Emit(new StoreSlot(accSlot, accWithElem, span));

    IrTempId nextI = fb.AddTemp(_types.Int);
    fb.Emit(new Add(nextI, curI, fb.EmitConstInt(1, span), span));
    fb.Emit(new StoreSlot(idxSlot, nextI, span));
    fb.Terminate(new Br(bbHead.Id, span));

    fb.SwitchTo(bbExit);
    IrTempId result = fb.AddTemp(_types.String);
    fb.Emit(new LoadSlot(result, accSlot, span));
    return result;
  }

  private IrOperand EmitRuntimeCallWithResult(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      IrRuntimeOp op,
      ImmutableArray<IrOperand> args,
      AllocRegion region) {
    IrTypeId retIr = InternType(fb.Model.GetType(call)!);
    IrTempId d = fb.AddTemp(retIr);
    fb.Emit(new CallRuntime(d, op, args, region, call.Span));
    return d;
  }

  private static IrOperand EmitRuntimeOp1(
      FunctionBuilder fb,
      IrTypeId retType,
      IrRuntimeOp op,
      IrOperand arg0,
      AllocRegion region,
      SourceSpan span) {
    IrTempId d = fb.AddTemp(retType);
    fb.Emit(new CallRuntime(d, op, [arg0], region, span));
    return d;
  }

  private static IrOperand EmitRuntimeOp2(
      FunctionBuilder fb,
      IrTypeId retType,
      IrRuntimeOp op,
      IrOperand arg0,
      IrOperand arg1,
      AllocRegion region,
      SourceSpan span) {
    IrTempId d = fb.AddTemp(retType);
    fb.Emit(new CallRuntime(d, op, [arg0, arg1], region, span));
    return d;
  }

  private static bool IsCallbackIntrinsic(IntrinsicId id) => id is
      IntrinsicId.VecEach1 or IntrinsicId.VecEach2
      or IntrinsicId.VecMap1 or IntrinsicId.VecMap2
      or IntrinsicId.VecFilter1 or IntrinsicId.VecFilter2
      or IntrinsicId.VecFind1 or IntrinsicId.VecFind2
      or IntrinsicId.VecFindIndex1 or IntrinsicId.VecFindIndex2
      or IntrinsicId.VecAny1 or IntrinsicId.VecAny2
      or IntrinsicId.VecAll1 or IntrinsicId.VecAll2
      or IntrinsicId.VecCount1 or IntrinsicId.VecCount2
      or IntrinsicId.SetAny or IntrinsicId.SetAll or IntrinsicId.SetEach;

  private static bool IsMutatingReceiverIntrinsic(IntrinsicId id) => id is
      IntrinsicId.VecPush or IntrinsicId.VecPop or IntrinsicId.VecSwap
      or IntrinsicId.MapRemove
      or IntrinsicId.SetAdd or IntrinsicId.SetRemove
      or IntrinsicId.PoolAdd or IntrinsicId.PoolRemove or IntrinsicId.PoolClear;

  private static string GetStaticTypeName(TypeSymbol type) => type switch {
    PrimitiveType p => p.DisplayName,
    VectorType => "vector",
    MapType => "map",
    SetType => "set",
    PoolType => "Pool",
    HandleType => "Handle",
    EnumType => "enum",
    StructType => "struct",
    UnionType => "union",
    FunctionType => "function",
    _ => type.DisplayName,
  };
}
