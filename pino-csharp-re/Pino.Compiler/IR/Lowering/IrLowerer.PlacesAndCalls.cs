using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.IR;

public sealed partial class IrLowerer {
  // ---------------------------------------------------------------------------
  // Place-path address resolution with top-down EnsureUnique & D-184 slot capture
  // ---------------------------------------------------------------------------

  private IrOperand? ResolvePlaceAddr(
      FunctionBuilder fb,
      ExpressionSyntax place,
      bool ensureUniqueLeaf,
      Dictionary<IndexExpressionSyntax, IrOperand>? recordedIndexSlots = null) {
    ExpressionSyntax unwrapped = UnwrapParens(place);
    IrOperand? resolved = null;

    switch (unwrapped) {
      case IdentifierExpressionSyntax id: {
          Symbol sym = fb.Model.GetReferencedSymbol(id)
              ?? throw new InvalidOperationException($"Unresolved place identifier '{NameOf(fb.Model, id.IdentifierToken)}'.");
          resolved = ResolveRootSymbolAddr(fb, sym, id.Span);
          break;
        }

      case MemberAccessExpressionSyntax member: {
          TypeSymbol targetType = fb.Model.GetType(member.Target)
              ?? throw new InvalidOperationException("Missing target type on mutable member place.");
          if (targetType is InterfaceType) {
            throw new IrLoweringUnsupportedException("Interface mutable property write is deferred to ticket 061.");
          }

          IrOperand? basePtrOpt = ResolvePlaceAddr(fb, member.Target, ensureUniqueLeaf: false, recordedIndexSlots);
          if (fb.IsTerminated || basePtrOpt is not { } basePtr) {
            return null;
          }

          var structType = (StructType) targetType;
          var field = (FieldSymbol) (fb.Model.GetReferencedSymbol(member)
              ?? throw new InvalidOperationException($"Unresolved field '{NameOf(fb.Model, member.MemberToken)}'."));
          resolved = EmitStructFieldAddr(fb, basePtr, structType.Definition, field, member.Span);
          break;
        }

      case IndexExpressionSyntax index: {
          // Top-down CoW uniqueness on the container before taking an element address.
          IrOperand? containerPtrOpt = ResolvePlaceAddr(fb, index.Target, ensureUniqueLeaf: true, recordedIndexSlots);
          if (fb.IsTerminated || containerPtrOpt is not { } containerPtr) {
            return null;
          }

          IrOperand? keyOpt = LowerExpression(fb, index.Index);
          if (fb.IsTerminated || keyOpt is not { } keyVal) {
            return null;
          }

          var ptrType = (IrPtrType) _types.GetType(fb.GetOperandType(containerPtr));
          IrType containerType = _types.GetType(ptrType.TargetType);

          IrTempId elemPtr;
          switch (containerType) {
            case IrVectorType vt:
              elemPtr = fb.AddTemp(_types.GetPtr(vt.ElementType));
              fb.Emit(new VecElemAddr(elemPtr, containerPtr, keyVal, index.Span));
              break;

            case IrMapType mt:
              elemPtr = fb.AddTemp(_types.GetPtr(mt.ValueType));
              fb.Emit(new MapElemAddr(elemPtr, containerPtr, keyVal, index.Span));
              break;

            case IrPoolType pt: {
                string poolName = ExtractFirstIdentifier(fb.Model, index.Target);
                elemPtr = fb.AddTemp(_types.GetPtr(pt.ElementType));
                fb.Emit(new PoolElemAddr(elemPtr, containerPtr, keyVal, poolName, index.Span));
                break;
              }

            default:
              throw new InvalidOperationException(
                  $"Cannot take mutable element address of '{containerType.Name}'.");
          }

          if (recordedIndexSlots is not null) {
            recordedIndexSlots[index] = elemPtr;
          }

          resolved = elemPtr;
          break;
        }

      default:
        throw new InvalidOperationException($"Unsupported mutable place expression '{unwrapped.GetType().Name}'.");
    }

    if (resolved is not { } ptr) {
      return null;
    }

    if (ensureUniqueLeaf) {
      var leafPtrType = (IrPtrType) _types.GetType(fb.GetOperandType(ptr));
      if (_types.GetType(leafPtrType.TargetType).IsCoWContainer) {
        fb.Emit(new EnsureUnique(ptr, unwrapped.Span));
      }
    }

    return ptr;
  }

  private IrOperand ResolveRootSymbolAddr(FunctionBuilder fb, Symbol sym, SourceSpan span) {
    switch (sym) {
      case LocalSymbol local:
        if (IsSelfLocal(local)) {
          return IrOperand.FromParam(0);
        }

        if (_globalIds.TryGetValue(local, out IrGlobalId globalId)) {
          IrTypeId gType = _module.Globals[globalId.Index].Type;
          IrTempId ptrTmp = fb.AddTemp(_types.GetPtr(gType));
          fb.Emit(new AddrGlobal(ptrTmp, globalId, span));
          return ptrTmp;
        }

        if (fb.LocalSlots.TryGetValue(local, out IrSlotId slotId)) {
          IrTypeId sType = fb.Function.Slots[slotId.Index].Type;
          IrTempId ptrTmp = fb.AddTemp(_types.GetPtr(sType));
          fb.Emit(new AddrLocal(ptrTmp, slotId, span));
          return ptrTmp;
        }

        throw new IrLoweringUnsupportedException(
            $"Captured mutable local '{local.Name}' cell address is deferred to ticket 061.");

      case ParameterSymbol param: {
          if (!fb.ParamIndices.TryGetValue(param, out int paramIdx)) {
            throw new IrLoweringUnsupportedException(
                $"Captured parameter '{param.Name}' is deferred to ticket 061.");
          }

          return IrOperand.FromParam(paramIdx);
        }

      case FieldSymbol field: {
          // Bare mutable field inside a `mut fn` (STRUCT-4, STRUCT-6).
          StructSymbol owner = fb.EnclosingStruct
              ?? throw new InvalidOperationException($"Bare field '{field.Name}' mutated outside a struct method.");
          return EmitStructFieldAddr(fb, IrOperand.FromParam(0), owner, field, span);
        }

      default:
        throw new InvalidOperationException($"Cannot take address of symbol '{sym.Name}' ({sym.GetType().Name}).");
    }
  }

  private IrOperand EmitStructFieldAddr(
      FunctionBuilder fb,
      IrOperand structPtr,
      StructSymbol structSym,
      FieldSymbol field,
      SourceSpan span) {
    IrOperand currentPtr = structPtr;
    StructSymbol currentStruct = structSym;

    foreach (FieldSymbol step in field.EmbeddedChain) {
      var targetStructType = (StructType) step.Type;
      int embIdx = FindEmbeddedStructFieldIndex(currentStruct, targetStructType.Definition);
      var curPtrType = (IrPtrType) _types.GetType(fb.GetOperandType(currentPtr));
      var irStruct = (IrStructType) _types.GetType(curPtrType.TargetType);
      IrTempId subPtr = fb.AddTemp(_types.GetPtr(irStruct.Fields[embIdx].Type));
      fb.Emit(new FieldAddr(subPtr, currentPtr, embIdx, span));
      currentPtr = subPtr;
      currentStruct = targetStructType.Definition;
    }

    int leafIdx = field.IsPromotedFromEmbedded
        ? FindDirectFieldIndexByName(currentStruct, field.Name)
        : FindStructFieldIndex(currentStruct, field);
    var leafPtrType = (IrPtrType) _types.GetType(fb.GetOperandType(currentPtr));
    var leafStruct = (IrStructType) _types.GetType(leafPtrType.TargetType);
    IrTempId fieldPtr = fb.AddTemp(_types.GetPtr(leafStruct.Fields[leafIdx].Type));
    fb.Emit(new FieldAddr(fieldPtr, currentPtr, leafIdx, span));
    return fieldPtr;
  }

  // ---------------------------------------------------------------------------
  // Call lowering: FN-31 read-before-mut order + D-184 per-step PtrEq guards
  // ---------------------------------------------------------------------------

  private IrOperand? LowerCall(FunctionBuilder fb, CallExpressionSyntax call) {
    CallResolution res = fb.Model.GetCallResolution(call)
        ?? throw new InvalidOperationException("Missing CallResolution on call expression.");

    switch (res.Kind) {
      case CallKind.Intrinsic:
        return LowerIntrinsicCall(fb, call, res.Intrinsic!.Value);

      case CallKind.UnionVariantConstructor:
        return LowerUnionConstructorCall(fb, call, res);

      case CallKind.DirectFunction:
        return LowerDirectFunctionCall(fb, call, (FunctionSymbol) res.TargetSymbol!);

      case CallKind.StaticMethod:
        return LowerStaticMethodCall(fb, call, (MethodSymbol) res.TargetSymbol!);

      case CallKind.InstanceMethod:
        return LowerInstanceMethodCall(fb, call, (MethodSymbol) res.TargetSymbol!, res.HasImplicitSelf);

      case CallKind.IndirectFunctionValue:
      case CallKind.CallableField:
      case CallKind.BoundMethodValue:
      case CallKind.IndexedElementCall:
        throw new IrLoweringUnsupportedException(
            $"Indirect/closure call kind '{res.Kind}' is deferred to ticket 061.");

      default:
        throw new InvalidOperationException($"Unsupported CallKind '{res.Kind}'.");
    }
  }

  private IrOperand? LowerUnionConstructorCall(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      CallResolution res) {
    var variant = (UnionVariantSymbol) res.TargetSymbol!;
    UnionType unionType = res.ConstructedUnionType
        ?? (UnionType) (fb.Model.GetType(call)
            ?? throw new InvalidOperationException("Missing constructed union type."));
    IrTypeId unionIrId = InternType(unionType);

    var payloads = ImmutableArray.CreateBuilder<IrOperand>(call.Arguments.Length);
    foreach (ArgumentSyntax arg in call.Arguments) {
      IrOperand? val = LowerExpression(fb, arg.Expression);
      if (fb.IsTerminated || val is not { } v) {
        return null;
      }

      payloads.Add(v);
    }

    IrTempId dest = fb.AddTemp(unionIrId);
    fb.Emit(new UnionNew(dest, variant.TagOrdinal, payloads.MoveToImmutable(), UnionAllocRegion(unionIrId), call.Span));
    return dest;
  }

  private IrOperand? LowerDirectFunctionCall(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      FunctionSymbol fnSym) {
    if (!fnSym.TypeParameters.IsEmpty) {
      throw new IrLoweringUnsupportedException(
          $"Generic function call '{fnSym.Name}' is deferred to ticket 061.");
    }

    if (!_funcIds.TryGetValue(fnSym, out IrFuncId calleeId)) {
      throw new InvalidOperationException($"Function '{fnSym.Name}' was not registered.");
    }

    ImmutableArray<IrOperand>? args = LowerCallArgsWithExclusivity(
        fb,
        call,
        mutatingReceiverExpr: null,
        hasImplicitMutSelf: false,
        out _);
    if (fb.IsTerminated || args is not { } argOps) {
      return null;
    }

    return EmitDirectCall(fb, calleeId, argOps, call.Span);
  }

  private IrOperand? LowerStaticMethodCall(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      MethodSymbol methodSym) {
    if (!methodSym.TypeParameters.IsEmpty
        || (methodSym.ContainingType is StructSymbol st && !st.TypeParameters.IsEmpty)) {
      throw new IrLoweringUnsupportedException(
          $"Generic static method call '{methodSym.Name}' is deferred to ticket 061.");
    }

    if (!_methodIds.TryGetValue(methodSym, out IrFuncId calleeId)) {
      throw new InvalidOperationException($"Static method '{methodSym.Name}' was not registered.");
    }

    ImmutableArray<IrOperand>? args = LowerCallArgsWithExclusivity(
        fb,
        call,
        mutatingReceiverExpr: null,
        hasImplicitMutSelf: false,
        out _);
    if (fb.IsTerminated || args is not { } argOps) {
      return null;
    }

    return EmitDirectCall(fb, calleeId, argOps, call.Span);
  }

  private IrOperand? LowerInstanceMethodCall(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      MethodSymbol methodSym,
      bool hasImplicitSelf) {
    if (methodSym.ContainingType is InterfaceSymbol) {
      throw new IrLoweringUnsupportedException(
          $"Interface method call '{methodSym.Name}' is deferred to ticket 061.");
    }

    if (!methodSym.TypeParameters.IsEmpty
        || (methodSym.ContainingType is StructSymbol ownerSt && !ownerSt.TypeParameters.IsEmpty)) {
      throw new IrLoweringUnsupportedException(
          $"Generic instance method call '{methodSym.Name}' is deferred to ticket 061.");
    }

    MethodSymbol canonicalMethod = methodSym.PromotedFrom ?? methodSym;
    if (!_methodIds.TryGetValue(canonicalMethod, out IrFuncId calleeId)) {
      throw new InvalidOperationException($"Instance method '{canonicalMethod.Name}' was not registered.");
    }

    ExpressionSyntax? receiverExpr = hasImplicitSelf
        ? null
        : ((MemberAccessExpressionSyntax) call.Callee).Target;

    IrOperand selfArg;
    ImmutableArray<IrOperand> argOps;

    if (methodSym.IsMutating) {
      ImmutableArray<IrOperand>? loweredArgs = LowerCallArgsWithExclusivity(
          fb,
          call,
          mutatingReceiverExpr: receiverExpr,
          hasImplicitMutSelf: hasImplicitSelf,
          out IrOperand? recvPtr);
      if (fb.IsTerminated || loweredArgs is not { } aOps || recvPtr is not { } rPtr) {
        return null;
      }

      selfArg = PromoteSelfPtr(fb, rPtr, methodSym, call.Span);
      argOps = aOps;
    } else {
      // Non-mutating method: evaluate receiver value, then read args, then mut args + D-184 checks.
      IrOperand recvVal;
      if (hasImplicitSelf) {
        recvVal = EmitSelfValueRead(fb, call.Span);
      } else {
        IrOperand? recvOpt = LowerExpression(fb, receiverExpr!);
        if (fb.IsTerminated || recvOpt is not { } rv) {
          return null;
        }

        recvVal = rv;
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

      selfArg = PromoteSelfValue(fb, recvVal, methodSym, call.Span);
      argOps = aOps;
    }

    var fullArgs = ImmutableArray.CreateBuilder<IrOperand>(argOps.Length + 1);
    fullArgs.Add(selfArg);
    fullArgs.AddRange(argOps);
    return EmitDirectCall(fb, calleeId, fullArgs.MoveToImmutable(), call.Span);
  }

  private IrOperand PromoteSelfPtr(
      FunctionBuilder fb,
      IrOperand structPtr,
      MethodSymbol method,
      SourceSpan span) {
    if (!method.IsPromotedFromEmbedded) {
      return structPtr;
    }

    IrOperand curPtr = structPtr;
    var curStruct = (StructSymbol) method.ContainingType;
    foreach (FieldSymbol step in method.EmbeddedChain) {
      var targetStruct = (StructType) step.Type;
      int embIdx = FindEmbeddedStructFieldIndex(curStruct, targetStruct.Definition);
      var curPtrType = (IrPtrType) _types.GetType(fb.GetOperandType(curPtr));
      var irStruct = (IrStructType) _types.GetType(curPtrType.TargetType);
      IrTempId subPtr = fb.AddTemp(_types.GetPtr(irStruct.Fields[embIdx].Type));
      fb.Emit(new FieldAddr(subPtr, curPtr, embIdx, span));
      curPtr = subPtr;
      curStruct = targetStruct.Definition;
    }

    return curPtr;
  }

  private IrOperand PromoteSelfValue(
      FunctionBuilder fb,
      IrOperand structVal,
      MethodSymbol method,
      SourceSpan span) {
    if (!method.IsPromotedFromEmbedded) {
      return structVal;
    }

    IrOperand curVal = structVal;
    var curStruct = (StructSymbol) method.ContainingType;
    foreach (FieldSymbol step in method.EmbeddedChain) {
      var targetStruct = (StructType) step.Type;
      int embIdx = FindEmbeddedStructFieldIndex(curStruct, targetStruct.Definition);
      var irStruct = (IrStructType) _types.GetType(fb.GetOperandType(curVal));
      IrTempId subVal = fb.AddTemp(irStruct.Fields[embIdx].Type);
      fb.Emit(new FieldGet(subVal, curVal, embIdx, span));
      curVal = subVal;
      curStruct = targetStruct.Definition;
    }

    return curVal;
  }

  /// <summary>
  /// Evaluates call arguments respecting FN-31 (all read-only arguments evaluated as values left-to-right
  /// before mutable access begins), resolves mutable receiver and <c>mut</c> argument places top-down
  /// with <c>EnsureUnique</c>, and emits D-184 per-step <c>PtrEq</c> exclusivity guards (FN-34).
  /// </summary>
  private ImmutableArray<IrOperand>? LowerCallArgsWithExclusivity(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      ExpressionSyntax? mutatingReceiverExpr,
      bool hasImplicitMutSelf,
      out IrOperand? resolvedMutReceiver) {
    resolvedMutReceiver = null;
    int argCount = call.Arguments.Length;
    var evalArgs = new IrOperand[argCount];
    ImmutableArray<RuntimeDisjointnessCheck> checks = fb.Model.GetRuntimeDisjointnessChecks(call);
    Dictionary<IndexExpressionSyntax, IrOperand>? recordedIndexSlots = checks.IsEmpty
        ? null
        : new Dictionary<IndexExpressionSyntax, IrOperand>(ReferenceEqualityComparer.Instance);

    // 1. FN-31: Evaluate all read-only arguments left-to-right BEFORE any mutable access begins.
    for (int i = 0; i < argCount; i++) {
      ArgumentSyntax arg = call.Arguments[i];
      if (arg.MutKeyword is not null) {
        continue;
      }

      IrOperand? val = LowerExpression(fb, arg.Expression);
      if (fb.IsTerminated || val is not { } v) {
        return null;
      }

      evalArgs[i] = v;
    }

    // 2. Resolve mutating receiver place (if any).
    if (hasImplicitMutSelf) {
      resolvedMutReceiver = IrOperand.FromParam(0);
    } else if (mutatingReceiverExpr is not null) {
      IrOperand? recvPtr = ResolvePlaceAddr(
          fb,
          mutatingReceiverExpr,
          ensureUniqueLeaf: true,
          recordedIndexSlots);
      if (fb.IsTerminated || recvPtr is not { } rp) {
        return null;
      }

      resolvedMutReceiver = rp;
    }

    // 3. Resolve all `mut` arguments left-to-right (capturing intermediate index slot pointers for D-184).
    for (int i = 0; i < argCount; i++) {
      ArgumentSyntax arg = call.Arguments[i];
      if (arg.MutKeyword is null) {
        continue;
      }

      MutationPlaceInfo? placeInfo = fb.Model.GetMutationPlace(arg);
      bool ensureLeaf = placeInfo?.RequiresCoWCheck ?? true;
      IrOperand? mutPtr = ResolvePlaceAddr(
          fb,
          arg.Expression,
          ensureUniqueLeaf: ensureLeaf,
          recordedIndexSlots);
      if (fb.IsTerminated || mutPtr is not { } mp) {
        return null;
      }

      evalArgs[i] = mp;
    }

    // 4. D-184 / FN-34: Emit per-step PtrEq exclusivity guards across every unknown step pair.
    if (!checks.IsEmpty) {
      EmitRuntimeDisjointnessGuards(fb, call, checks, recordedIndexSlots!);
      if (fb.IsTerminated) {
        return null;
      }
    }

    return [.. evalArgs];
  }

  private void EmitRuntimeDisjointnessGuards(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      ImmutableArray<RuntimeDisjointnessCheck> checks,
      Dictionary<IndexExpressionSyntax, IrOperand> recordedIndexSlots) {
    string targetDisplay = FormatDisjointnessTarget(fb.Model, call);

    foreach (RuntimeDisjointnessCheck check in checks) {
      if (check.UnknownSteps.IsEmpty) {
        continue;
      }

      string collectionName = ExtractFirstIdentifier(fb.Model, check.UnknownSteps[0].First);
      string panicText = FormatAliasedMutPanic(collectionName, targetDisplay);

      IrBasicBlock bbPanic = fb.NewBlock();
      IrBasicBlock bbNext = fb.NewBlock();

      // The two places alias iff EVERY unknown step pair resolves to the same slot address.
      for (int s = 0; s < check.UnknownSteps.Length; s++) {
        RuntimeDisjointnessStepPair pair = check.UnknownSteps[s];
        IrOperand slotA = recordedIndexSlots[pair.First];
        IrOperand slotB = recordedIndexSlots[pair.Second];
        IrTempId eq = fb.AddTemp(_types.Bool);
        fb.Emit(new PtrEq(eq, slotA, slotB, call.Span));

        if (s + 1 < check.UnknownSteps.Length) {
          IrBasicBlock bbStepNext = fb.NewBlock();
          fb.Terminate(new BrIf(eq, bbStepNext.Id, bbNext.Id, call.Span));
          fb.SwitchTo(bbStepNext);
        } else {
          fb.Terminate(new BrIf(eq, bbPanic.Id, bbNext.Id, call.Span));
        }
      }

      fb.SwitchTo(bbPanic);
      IrTempId msgTmp = fb.EmitConstString(panicText, call.Span);
      fb.Terminate(new Panic(msgTmp, call.Span));

      fb.SwitchTo(bbNext);
    }
  }

  private static string FormatDisjointnessTarget(SemanticModel model, CallExpressionSyntax call) {
    CallResolution? res = model.GetCallResolution(call);
    return call.Callee switch {
      IdentifierExpressionSyntax id when res is { Kind: CallKind.DirectFunction or CallKind.InstanceMethod } =>
        $"'{NameOf(model, id.IdentifierToken)}'",
      MemberAccessExpressionSyntax mem when res is { Kind: CallKind.InstanceMethod } =>
        $"'{NameOf(model, mem.MemberToken)}'",
      StaticAccessExpressionSyntax stat =>
        $"'{NameOf(model, stat.MemberToken)}'",
      _ => "this function",
    };
  }

  private IrOperand? EmitDirectCall(
      FunctionBuilder fb,
      IrFuncId calleeId,
      ImmutableArray<IrOperand> args,
      SourceSpan span) {
    IrTypeId retType = _module.Functions[calleeId.Index].ReturnType;
    if (retType == _types.Void) {
      fb.Emit(new CallDirect(null, calleeId, args, span));
      return null;
    }

    IrTempId dest = fb.AddTemp(retType);
    fb.Emit(new CallDirect(dest, calleeId, args, span));
    return dest;
  }
}
