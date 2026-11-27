using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.IR;

public sealed partial class IrLowerer {
  private readonly Dictionary<(IrTypeId StructType, IrTypeId IfaceType), IrVTableId> _vtables = [];
  private readonly Dictionary<IrTypeId, IrFuncId> _dropHelpers = [];
  private readonly Dictionary<IrTypeId, IrFuncId> _cloneHelpers = [];
  private readonly Dictionary<IrTypeId, IrFuncId> _toStrHelpers = [];

  // ---------------------------------------------------------------------------
  // StructToInterface coercion & VTable synthesis (IFACE-1..6, IQ-5, design §2.5)
  // ---------------------------------------------------------------------------

  private IrOperand EmitStructToInterface(
      FunctionBuilder fb,
      IrOperand value,
      TypeSymbol concreteSrcType,
      InterfaceType concreteTargetIface,
      SourceSpan span) {
    IrTypeId srcIrType = fb.GetOperandType(value);
    IrTypeId targetIrType = InternType(concreteTargetIface);
    if (srcIrType == targetIrType) {
      return value;
    }

    if (concreteSrcType is StructType concreteStruct) {
      IrVTableId vtId = GetOrSynthesizeVTable(concreteStruct, concreteTargetIface);
      IrTempId boxed = fb.AddTemp(targetIrType);
      fb.Emit(new IfaceBox(boxed, value, vtId, AllocRegion.Heap, span));
      return boxed;
    }

    throw new InvalidOperationException(
        $"Cannot coerce '{concreteSrcType.DisplayName}' to interface '{concreteTargetIface.DisplayName}'.");
  }

  /// <summary>
  /// Coerces an already-lowered operand to <paramref name="targetTypeId"/> when implicit
  /// structural conversions (such as <c>IFACE-5</c> covariant <c>map[K, S]</c> -&gt; <c>map[K, I]</c>,
  /// <c>S</c> -&gt; <c>I</c>, or <c>int</c> -&gt; <c>float</c>) are not explicitly recorded in
  /// <see cref="SemanticModel.GetConversion"/>.
  /// </summary>
  private IrOperand CoerceOperandToTargetType(
      FunctionBuilder fb,
      IrOperand val,
      IrTypeId targetTypeId,
      SourceSpan span) {
    IrTypeId srcTypeId = fb.GetOperandType(val);
    if (srcTypeId == targetTypeId) {
      return val;
    }

    if (srcTypeId == _types.Int && targetTypeId == _types.Float) {
      IrTempId widened = fb.AddTemp(_types.Float);
      fb.Emit(new CastI64ToF64(widened, val, span));
      return widened;
    }

    IrType srcIr = _types.GetType(srcTypeId);
    IrType dstIr = _types.GetType(targetTypeId);

    if (srcIr is IrStructType && dstIr is IrInterfaceType) {
      return EmitStructToInterface(
          fb,
          val,
          ReconstructTypeSymbol(srcTypeId),
          (InterfaceType) ReconstructTypeSymbol(targetTypeId),
          span);
    }

    if (srcIr is IrMapType srcMap && dstIr is IrMapType dstMap && srcMap.KeyType == dstMap.KeyType) {
      IrTypeId keysVecType = _types.GetVector(srcMap.KeyType);
      IrTempId keys = fb.AddTemp(keysVecType, "map_conv_keys");
      fb.Emit(new MapKeys(keys, val, AllocRegion.Heap, span));

      IrTempId len = fb.AddTemp(_types.Int, "map_conv_len");
      fb.Emit(new VecLen(len, keys, span));

      IrTempId emptyDst = fb.AddTemp(targetTypeId);
      fb.Emit(new MapNew(emptyDst, [], AllocRegion.Heap, span));
      IrSlotId dstSlot = fb.AddSlot("map_conv", targetTypeId, isMutSlot: true);
      fb.Emit(new StoreSlot(dstSlot, emptyDst, span));

      IrSlotId idxSlot = fb.AddSlot("map_conv_i", _types.Int, isMutSlot: true);
      fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0, span), span));

      IrBasicBlock bbHead = fb.NewBlock();
      IrBasicBlock bbBody = fb.NewBlock();
      IrBasicBlock bbExit = fb.NewBlock();
      fb.Terminate(new Br(bbHead.Id, span));

      fb.SwitchTo(bbHead);
      IrTempId curIdx = fb.AddTemp(_types.Int);
      fb.Emit(new LoadSlot(curIdx, idxSlot, span));
      IrTempId cond = fb.AddTemp(_types.Bool);
      fb.Emit(new Cmp(cond, IrCmpOp.Lt, curIdx, len, span));
      fb.Terminate(new BrIf(cond, bbBody.Id, bbExit.Id, span));

      fb.SwitchTo(bbBody);
      IrTempId keyTmp = fb.AddTemp(srcMap.KeyType);
      fb.Emit(new VecGet(keyTmp, keys, curIdx, Unchecked: true, span));
      IrTempId valTmp = fb.AddTemp(srcMap.ValueType);
      fb.Emit(new MapGet(valTmp, val, keyTmp, span));
      IrOperand coercedVal = CoerceOperandToTargetType(fb, valTmp, dstMap.ValueType, span);

      IrTempId dstPtr = fb.AddTemp(_types.GetPtr(targetTypeId));
      fb.Emit(new AddrLocal(dstPtr, dstSlot, span));
      fb.Emit(new MapSet(dstPtr, keyTmp, coercedVal, span));

      IrTempId nextIdx = fb.AddTemp(_types.Int);
      fb.Emit(new Add(nextIdx, curIdx, fb.EmitConstInt(1, span), span));
      fb.Emit(new StoreSlot(idxSlot, nextIdx, span));
      fb.Terminate(new Br(bbHead.Id, span));

      fb.SwitchTo(bbExit);
      IrTempId result = fb.AddTemp(targetTypeId);
      fb.Emit(new LoadSlot(result, dstSlot, span));
      return result;
    }

    return val;
  }

  private IrVTableId GetOrSynthesizeVTable(StructType concreteStruct, InterfaceType concreteIface) {
    IrTypeId structIrId = InternType(concreteStruct);
    IrTypeId ifaceIrId = InternType(concreteIface);
    var key = (structIrId, ifaceIrId);

    if (_vtables.TryGetValue(key, out IrVTableId existing)) {
      return existing;
    }

    StructSymbol structSym = concreteStruct.Definition;
    InterfaceSymbol ifaceSym = concreteIface.Definition;
    var irIface = (IrInterfaceType) _types.GetType(ifaceIrId);

    // 1. Lifecycle / built-in vtable helpers (drop, clone, equals, to_string).
    IrFuncId dropFnId = GetOrSynthesizeDropHelper(structIrId);
    IrFuncId cloneFnId = GetOrSynthesizeCloneHelper(structIrId);
    IrFuncId eqFnId = GetOrSynthesizeEqHelper(structIrId);
    IrFuncId toStrFnId = GetOrSynthesizeToStrHelper(structIrId);

    // 2. Method thunks in exact InterfaceSymbol.Methods order.
    var methodFuncIds = ImmutableArray.CreateBuilder<IrFuncId>(ifaceSym.Methods.Length);
    for (int m = 0; m < ifaceSym.Methods.Length; m++) {
      MethodSymbol ifaceMethod = ifaceSym.Methods[m];
      IrInterfaceMethod irIfaceMethod = irIface.Methods[m];
      IrFuncId thunkId = SynthesizeVTableMethodThunk(
          concreteStruct,
          structIrId,
          concreteIface,
          ifaceMethod,
          irIfaceMethod);
      methodFuncIds.Add(thunkId);
    }

    // 3. Property field indices + getter/setter thunks in exact InterfaceSymbol.Properties order.
    var propFieldIndices = ImmutableArray.CreateBuilder<int>(ifaceSym.Properties.Length);
    var propGetterIds = ImmutableArray.CreateBuilder<IrFuncId>(ifaceSym.Properties.Length);
    var propSetterIds = ImmutableArray.CreateBuilder<IrFuncId?>(ifaceSym.Properties.Length);

    for (int p = 0; p < ifaceSym.Properties.Length; p++) {
      InterfaceProperty ifaceProp = ifaceSym.Properties[p];
      IrInterfaceProperty irProp = irIface.Properties[p];
      FieldSymbol field = FindStructFieldByName(structSym, ifaceProp.Name)
          ?? throw new InvalidOperationException(
              $"Struct '{structSym.Name}' does not have field '{ifaceProp.Name}' for interface '{ifaceSym.Name}'.");

      int directIndex = field.IsPromotedFromEmbedded ? -1 : FindStructFieldIndex(structSym, field);
      propFieldIndices.Add(directIndex);

      propGetterIds.Add(SynthesizeVTablePropertyGetter(concreteStruct, structIrId, field, irProp.Type));
      propSetterIds.Add(ifaceProp.IsMutable
          ? SynthesizeVTablePropertySetter(concreteStruct, structIrId, field, irProp.Type)
          : null);
    }

    IrVTableId vtId = _module.AddVTable(
        ifaceIrId,
        structIrId,
        methodFuncIds.MoveToImmutable(),
        propFieldIndices.MoveToImmutable(),
        dropFnId,
        cloneFnId,
        eqFnId,
        toStrFnId,
        propGetterIds.MoveToImmutable(),
        propSetterIds.MoveToImmutable(),
        typeName: concreteStruct.DisplayName);

    _vtables[key] = vtId;
    return vtId;
  }

  private IrFuncId SynthesizeVTableMethodThunk(
      StructType concreteStruct,
      IrTypeId structIrId,
      InterfaceType concreteIface,
      MethodSymbol ifaceMethod,
      IrInterfaceMethod irIfaceMethod) {
    StructSymbol structSym = concreteStruct.Definition;
    string thunkName = ReserveFunctionName(
        $"__vt_{SanitizeTypeName(concreteStruct.DisplayName)}_as_{SanitizeTypeName(concreteIface.DisplayName)}_{ifaceMethod.Name}");

    var thunkParams = ImmutableArray.CreateBuilder<IrParam>(irIfaceMethod.Parameters.Length + 1);
    if (ifaceMethod.IsMutating) {
      thunkParams.Add(new IrParam("self", _types.GetPtr(structIrId), IrParamOwnership.Mut));
    } else {
      thunkParams.Add(new IrParam("self", structIrId, IrParamOwnership.Owned));
    }

    for (int i = 0; i < irIfaceMethod.Parameters.Length; i++) {
      IrClosureParam p = irIfaceMethod.Parameters[i];
      string pName = i < ifaceMethod.Parameters.Length ? ifaceMethod.Parameters[i].Name : $"arg{i}";
      thunkParams.Add(new IrParam(pName, p.Type, p.Ownership));
    }

    MethodSymbol? concreteMethod = FindStructMethodByName(structSym, ifaceMethod.Name);
    ModuleStateEffects effects = concreteMethod is not null
        ? _compilation.GetModuleStateEffects(concreteMethod.PromotedFrom ?? concreteMethod)
        : ModuleStateEffects.ReadWrite;

    IrFunction thunkFn = _module.AddFunction(
        thunkName,
        thunkParams.MoveToImmutable(),
        irIfaceMethod.ReturnType,
        effects);
    var fb = new FunctionBuilder(this, thunkFn, _compilation.RootModel);

    if (concreteMethod is not null) {
      IrFuncId targetMethodId = ResolveConcreteStructMethodId(concreteStruct, concreteMethod);
      IrOperand selfArg;
      if (concreteMethod.IsMutating) {
        selfArg = PromoteSelfPtr(fb, IrOperand.FromParam(0), concreteMethod, default);
      } else if (ifaceMethod.IsMutating) {
        // Interface declared `mut fn`, concrete struct implemented non-mutating `fn`.
        IrTempId loadedSelf = fb.AddTemp(structIrId, "self_val");
        fb.Emit(new LoadPtr(loadedSelf, IrOperand.FromParam(0)));
        selfArg = PromoteSelfValue(fb, loadedSelf, concreteMethod, default);
      } else {
        selfArg = PromoteSelfValue(fb, IrOperand.FromParam(0), concreteMethod, default);
      }

      var callArgs = ImmutableArray.CreateBuilder<IrOperand>(irIfaceMethod.Parameters.Length + 1);
      callArgs.Add(selfArg);
      for (int i = 0; i < irIfaceMethod.Parameters.Length; i++) {
        callArgs.Add(IrOperand.FromParam(i + 1));
      }

      IrOperand? res = EmitDirectCall(fb, targetMethodId, callArgs.MoveToImmutable(), default);
      if (irIfaceMethod.ReturnType == _types.Void) {
        fb.Terminate(new RetVoid());
      } else {
        fb.Terminate(new Ret(res!.Value));
      }

      return thunkFn.Id;
    }

    // STRUCT-12: interface method satisfied by a callable field on the struct.
    FieldSymbol callableField = FindStructFieldByName(structSym, ifaceMethod.Name)
        ?? throw new InvalidOperationException(
            $"Struct '{structSym.Name}' has neither method nor callable field '{ifaceMethod.Name}'.");

    IrOperand selfValue;
    if (ifaceMethod.IsMutating) {
      IrTempId loaded = fb.AddTemp(structIrId);
      fb.Emit(new LoadPtr(loaded, IrOperand.FromParam(0)));
      selfValue = loaded;
    } else {
      selfValue = IrOperand.FromParam(0);
    }

    IrOperand clos = EmitStructFieldGet(fb, selfValue, structSym, callableField, default);
    var closArgs = ImmutableArray.CreateBuilder<IrOperand>(irIfaceMethod.Parameters.Length);
    for (int i = 0; i < irIfaceMethod.Parameters.Length; i++) {
      closArgs.Add(IrOperand.FromParam(i + 1));
    }

    IrOperand? closRes = EmitClosureCall(fb, clos, closArgs.MoveToImmutable(), default);
    if (irIfaceMethod.ReturnType == _types.Void) {
      fb.Terminate(new RetVoid());
    } else {
      fb.Terminate(new Ret(closRes!.Value));
    }

    return thunkFn.Id;
  }

  private IrFuncId SynthesizeVTablePropertyGetter(
      StructType concreteStruct,
      IrTypeId structIrId,
      FieldSymbol field,
      IrTypeId propIrType) {
    string fnName = ReserveFunctionName(
        $"__vt_get_{SanitizeTypeName(concreteStruct.DisplayName)}_{field.Name}");
    IrFunction fn = _module.AddFunction(
        fnName,
        [new IrParam("self", structIrId, IrParamOwnership.Owned)],
        propIrType,
        ModuleStateEffects.Pure);
    var fb = new FunctionBuilder(this, fn, _compilation.RootModel);
    IrOperand val = EmitStructFieldGet(fb, IrOperand.FromParam(0), concreteStruct.Definition, field, default);
    fb.Terminate(new Ret(val));
    return fn.Id;
  }

  private IrFuncId SynthesizeVTablePropertySetter(
      StructType concreteStruct,
      IrTypeId structIrId,
      FieldSymbol field,
      IrTypeId propIrType) {
    string fnName = ReserveFunctionName(
        $"__vt_set_{SanitizeTypeName(concreteStruct.DisplayName)}_{field.Name}");
    IrFunction fn = _module.AddFunction(
        fnName,
        [
          new IrParam("self", _types.GetPtr(structIrId), IrParamOwnership.Mut),
          new IrParam("value", propIrType, IrParamOwnership.Owned),
        ],
        _types.Void,
        ModuleStateEffects.Pure);
    var fb = new FunctionBuilder(this, fn, _compilation.RootModel);
    IrOperand addr = EmitStructFieldAddr(fb, IrOperand.FromParam(0), concreteStruct.Definition, field, default);
    fb.Emit(new StorePtr(addr, IrOperand.FromParam(1)));
    fb.Terminate(new RetVoid());
    return fn.Id;
  }

  private IrFuncId GetOrSynthesizeDropHelper(IrTypeId structIrId) {
    if (_dropHelpers.TryGetValue(structIrId, out IrFuncId existing)) {
      return existing;
    }

    IrType irType = _types.GetType(structIrId);
    string fnName = ReserveFunctionName($"__vt_drop_{SanitizeTypeName(irType.Name)}");
    IrFunction fn = _module.AddFunction(
        fnName,
        [new IrParam("self", structIrId, IrParamOwnership.Owned)],
        _types.Void,
        ModuleStateEffects.Pure);
    IrBasicBlock bb0 = fn.AddBlock();
    bb0.Terminator = new RetVoid();
    _dropHelpers[structIrId] = fn.Id;
    return fn.Id;
  }

  private IrFuncId GetOrSynthesizeCloneHelper(IrTypeId structIrId) {
    if (_cloneHelpers.TryGetValue(structIrId, out IrFuncId existing)) {
      return existing;
    }

    IrType irType = _types.GetType(structIrId);
    string fnName = ReserveFunctionName($"__vt_clone_{SanitizeTypeName(irType.Name)}");
    IrFunction fn = _module.AddFunction(
        fnName,
        [new IrParam("self", structIrId, IrParamOwnership.Owned)],
        structIrId,
        ModuleStateEffects.Pure);
    var fb = new FunctionBuilder(this, fn, _compilation.RootModel);
    IrTempId copy = fb.AddTemp(structIrId, "cloned");
    fb.Emit(new Copy(copy, IrOperand.FromParam(0)));
    fb.Terminate(new Ret(copy));
    _cloneHelpers[structIrId] = fn.Id;
    return fn.Id;
  }

  private IrFuncId GetOrSynthesizeToStrHelper(IrTypeId structIrId) {
    if (_toStrHelpers.TryGetValue(structIrId, out IrFuncId existing)) {
      return existing;
    }

    IrType irType = _types.GetType(structIrId);
    string fnName = ReserveFunctionName($"__vt_tostr_{SanitizeTypeName(irType.Name)}");
    IrFunction fn = _module.AddFunction(
        fnName,
        [new IrParam("self", structIrId, IrParamOwnership.Owned)],
        _types.String,
        ModuleStateEffects.Pure);
    var fb = new FunctionBuilder(this, fn, _compilation.RootModel);
    IrTempId str = fb.AddTemp(_types.String);
    fb.Emit(new CallRuntime(str, IrRuntimeOp.ToStr, [IrOperand.FromParam(0)], AllocRegion.Heap));
    fb.Terminate(new Ret(str));
    _toStrHelpers[structIrId] = fn.Id;
    return fn.Id;
  }

  // ---------------------------------------------------------------------------
  // Interface member read, mutable property place, & method call
  // ---------------------------------------------------------------------------

  private IrOperand LowerInterfaceMemberRead(
      FunctionBuilder fb,
      MemberAccessExpressionSyntax member,
      InterfaceType ifaceType,
      IrOperand target) {
    string memberName = NameOf(fb.Model, member.MemberToken);
    int propIdx = FindInterfacePropertyIndex(ifaceType.Definition, memberName);
    if (propIdx >= 0) {
      var irIface = (IrInterfaceType) _types.GetType(fb.GetOperandType(target));
      IrTempId dest = fb.AddTemp(irIface.Properties[propIdx].Type);
      fb.Emit(new IfaceFieldGet(dest, target, propIdx, member.Span));
      return dest;
    }

    // Bound non-mutating interface method (STRUCT-14 on interface).
    int methodIdx = FindInterfaceMethodIndex(ifaceType.Definition, memberName);
    if (methodIdx >= 0) {
      MethodSymbol ifaceMethod = ifaceType.Definition.Methods[methodIdx];
      var fnType = (FunctionType) fb.GetNodeType(member);
      return LowerBoundMethodValue(fb, target, ifaceType, ifaceMethod, fnType, member.Span);
    }

    throw new InvalidOperationException(
        $"Interface '{ifaceType.DisplayName}' has no property or method '{memberName}'.");
  }

  private IrOperand? LowerInterfacePropertyAddr(
      FunctionBuilder fb,
      MemberAccessExpressionSyntax member,
      InterfaceType ifaceType,
      Dictionary<IndexExpressionSyntax, IrOperand>? recordedIndexSlots,
      Dictionary<IndexExpressionSyntax, IrOperand>? preEvaluatedIndices = null) {
    // CoW EnsureUnique on the interface place before taking property address (IFACE-4 / IQ-5).
    IrOperand? ifacePtrOpt = ResolvePlaceAddr(fb, member.Target, ensureUniqueLeaf: true, recordedIndexSlots, preEvaluatedIndices);
    if (fb.IsTerminated || ifacePtrOpt is not { } ifacePtr) {
      return null;
    }

    string memberName = NameOf(fb.Model, member.MemberToken);
    int propIdx = FindInterfacePropertyIndex(ifaceType.Definition, memberName);
    if (propIdx < 0) {
      throw new InvalidOperationException(
          $"Interface '{ifaceType.DisplayName}' has no mutable property '{memberName}'.");
    }

    var ptrType = (IrPtrType) _types.GetType(fb.GetOperandType(ifacePtr));
    var irIface = (IrInterfaceType) _types.GetType(ptrType.TargetType);
    IrTempId propPtr = fb.AddTemp(_types.GetPtr(irIface.Properties[propIdx].Type));
    fb.Emit(new IfaceFieldAddr(propPtr, ifacePtr, propIdx, member.Span));
    return propPtr;
  }

  private IrOperand? LowerInterfaceMethodCall(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      InterfaceType ifaceType,
      MethodSymbol ifaceMethod,
      ExpressionSyntax receiverExpr) {
    int methodIdx = FindInterfaceMethodIndex(ifaceType.Definition, ifaceMethod.Name);
    if (methodIdx < 0) {
      throw new InvalidOperationException(
          $"Method '{ifaceMethod.Name}' not found on interface '{ifaceType.DisplayName}'.");
    }

    IrTypeId ifaceIrId = InternType(ifaceType);
    var irIface = (IrInterfaceType) _types.GetType(ifaceIrId);
    IrInterfaceMethod irIfaceMethod = irIface.Methods[methodIdx];
    IrTypeId retIrType = irIfaceMethod.ReturnType;

    IrOperand receiverOp;
    ImmutableArray<IrOperand> argOps;

    if (ifaceMethod.IsMutating) {
      // LowerCallArgsWithExclusivity calls ResolvePlaceAddr(..., ensureUniqueLeaf: true),
      // emitting EnsureUnique on the ptr<Interface> before CallIface!
      ImmutableArray<IrOperand>? loweredArgs = LowerCallArgsWithExclusivity(
          fb,
          call,
          mutatingReceiverExpr: receiverExpr,
          hasImplicitMutSelf: false,
          out IrOperand? recvPtr);
      if (fb.IsTerminated || loweredArgs is not { } aOps || recvPtr is not { } rp) {
        return null;
      }

      receiverOp = rp;
      argOps = aOps;
    } else {
      IrOperand? recvOpt = LowerExpression(fb, receiverExpr);
      if (fb.IsTerminated || recvOpt is not { } rv) {
        return null;
      }

      ImmutableArray<IrOperand>? loweredArgs = LowerCallArgsWithExclusivity(
          fb,
          call,
          mutatingReceiverExpr: null,
          hasImplicitMutSelf: false,
          out _);
      if (fb.IsTerminated || loweredArgs is not { } aOps) {
        return null;
      }

      receiverOp = rv;
      argOps = aOps;
    }

    var coercedArgs = ImmutableArray.CreateBuilder<IrOperand>(argOps.Length);
    for (int i = 0; i < argOps.Length; i++) {
      IrClosureParam p = irIfaceMethod.Parameters[i];
      coercedArgs.Add(p.Ownership == IrParamOwnership.Mut
          ? argOps[i]
          : CoerceOperandToTargetType(fb, argOps[i], p.Type, call.Span));
    }

    ImmutableArray<IrOperand> finalArgs = coercedArgs.MoveToImmutable();
    if (retIrType == _types.Void) {
      fb.Emit(new CallIface(null, receiverOp, methodIdx, ifaceMethod.IsMutating, finalArgs, call.Span));
      return null;
    }

    IrTempId dest = fb.AddTemp(retIrType);
    fb.Emit(new CallIface(dest, receiverOp, methodIdx, ifaceMethod.IsMutating, finalArgs, call.Span));
    return dest;
  }

  private static int FindInterfacePropertyIndex(InterfaceSymbol iface, string name) {
    for (int i = 0; i < iface.Properties.Length; i++) {
      if (string.Equals(iface.Properties[i].Name, name, StringComparison.Ordinal)) {
        return i;
      }
    }

    return -1;
  }

  private static int FindInterfaceMethodIndex(InterfaceSymbol iface, string name) {
    for (int i = 0; i < iface.Methods.Length; i++) {
      if (string.Equals(iface.Methods[i].Name, name, StringComparison.Ordinal)) {
        return i;
      }
    }

    return -1;
  }

  private static FieldSymbol? FindStructFieldByName(StructSymbol structSym, string name) {
    ImmutableArray<FieldSymbol> fields = structSym.AllFields.IsEmpty ? structSym.Fields : structSym.AllFields;
    foreach (FieldSymbol f in fields) {
      if (string.Equals(f.Name, name, StringComparison.Ordinal)) {
        return f;
      }
    }

    return null;
  }

  private static MethodSymbol? FindStructMethodByName(StructSymbol structSym, string name) {
    foreach (MethodSymbol m in structSym.Methods) {
      if (!m.IsStatic && string.Equals(m.Name, name, StringComparison.Ordinal)) {
        return m;
      }
    }

    return null;
  }
}
