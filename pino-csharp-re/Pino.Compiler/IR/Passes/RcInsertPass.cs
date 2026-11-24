using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Text;

namespace Pino.Compiler.IR.Passes;

/// <summary>
/// Step 1 naive, auditable Perceus-style reference-counting insertion pass (<c>056-re-ir-design</c>
/// §4.1–§4.2, amended AD-002 with no GC phase).
/// </summary>
/// <remarks>
/// Ownership ABI contract shared with <c>PinoVm</c> and the C emitter:
/// <list type="bullet">
///   <item>Pointer-free types (<see cref="IrType.IsRcManaged"/> == <c>false</c>) never receive <c>rc_inc</c>/<c>rc_dec</c>.</item>
///   <item>Non-<c>mut</c> RC-managed parameters default to <c>@owned</c> and are dropped before <c>ret</c>/<c>ret.void</c> unless returned directly.</item>
///   <item>Read/projection ops (<c>LoadSlot</c>, <c>LoadPtr</c>, <c>FieldGet</c>, <c>UnionField</c>, <c>TupleField</c>, <c>CellGet</c>, <c>EnvFieldGet</c>, <c>IfaceFieldGet</c>, <c>VecGet</c>, <c>MapGet</c>, <c>PoolGetOrPanic</c>, <c>PoolSlotValue</c>, <c>MapSlotKey</c>, <c>MapSlotValue</c>, <c>SetSlotElem</c>, <c>Copy</c>) emit <c>rc_inc</c> on their RC-managed destination.</item>
///   <item>Overwriting <c>StoreSlot</c>, <c>StorePtr</c>, and <c>CellSet</c> secure the new value first, then drop the old value (safe on <c>x = x</c> and <c>x = x:field</c>).</item>
///   <item><c>CallDirect</c>, <c>CallClosure</c>, and <c>CallIface</c> consume <c>@owned</c> arguments; <c>CallClosure.Closure</c>, non-mutating <c>CallIface.Receiver</c>, and all <c>CallRuntime</c> arguments are borrowed.</item>
///   <item><c>Panic</c> paths emit zero <c>rc_dec</c> instructions.</item>
///   <item><c>__pino_shutdown()</c> drops every RC-managed global slot.</item>
/// </list>
/// </remarks>
public static class RcInsertPass {
  private enum SlotInitLattice : byte {
    Bottom = 0,
    Uninit = 1,
    Init = 2,
    MaybeInit = 3,
  }

  private enum GlobalInitLattice : byte {
    Bottom = 0,
    Uninit = 1,
    Init = 2,
    MaybeInit = 3,
  }

  public static IrModule Run(IrModule module) {
    ArgumentNullException.ThrowIfNull(module);
    if (module.Stage == IrPassStage.PostRcStep1) {
      return module;
    }

    SynthesizeRcDescriptors(module);

    var initializedGlobalsBefore = new HashSet<IrGlobalId>();
    var initFuncSet = new HashSet<IrFuncId>(module.InitFuncIds);

    // 1. Process imported module initializers in dependency order so global init state flows cleanly.
    foreach (IrFuncId initId in module.InitFuncIds) {
      if ((uint) initId.Index < (uint) module.Functions.Count) {
        IrFunction initFn = module.Functions[initId.Index];
        HashSet<IrGlobalId> newlyInitialized = RewriteFunction(
            module,
            initFn,
            trackGlobalInit: true,
            initializedGlobalsBefore);
        initializedGlobalsBefore.UnionWith(newlyInitialized);
      }
    }

    // 2. Process entry function (root top-level initializers / script statements).
    if (module.EntryFuncId is { } entryId && (uint) entryId.Index < (uint) module.Functions.Count) {
      RewriteFunction(
          module,
          module.Functions[entryId.Index],
          trackGlobalInit: true,
          initializedGlobalsBefore);
    }

    if (module.TestSetupFuncId is { } testSetupId && (uint) testSetupId.Index < (uint) module.Functions.Count) {
      RewriteFunction(
          module,
          module.Functions[testSetupId.Index],
          trackGlobalInit: true,
          initializedGlobalsBefore);
    }

    // 3. Process all remaining functions (excluding __pino_shutdown, populated in step 4).
    for (int i = 0; i < module.Functions.Count; i++) {
      var funcId = new IrFuncId(i);
      if (initFuncSet.Contains(funcId)
          || module.EntryFuncId == funcId
          || module.TestSetupFuncId == funcId
          || module.ShutdownFuncId == funcId) {
        continue;
      }

      RewriteFunction(
          module,
          module.Functions[i],
          trackGlobalInit: false,
          previouslyInitializedGlobals: null);
    }

    // 4. Populate __pino_shutdown() to drop every RC-managed global.
    PopulateShutdownFunction(module);

    module.Stage = IrPassStage.PostRcStep1;
    return module;
  }

  /// <summary>
  /// Runs Step 1 RC insertion on only the newly appended functions of an incremental REPL module
  /// (starting at <paramref name="firstNewFunctionIndex"/>), treating <paramref name="previouslyInitializedGlobals"/>
  /// as already initialized on entry to <paramref name="chunkFuncId"/>, and refreshes <c>__pino_shutdown()</c>.
  /// </summary>
  public static IrModule RunIncremental(
      IrModule module,
      int firstNewFunctionIndex,
      IrFuncId chunkFuncId,
      IEnumerable<IrGlobalId> previouslyInitializedGlobals) {
    ArgumentNullException.ThrowIfNull(module);
    ArgumentNullException.ThrowIfNull(previouslyInitializedGlobals);

    SynthesizeRcDescriptors(module);

    var initializedBefore = new HashSet<IrGlobalId>(previouslyInitializedGlobals);
    if ((uint) chunkFuncId.Index < (uint) module.Functions.Count) {
      RewriteFunction(
          module,
          module.Functions[chunkFuncId.Index],
          trackGlobalInit: true,
          initializedBefore);
    }

    for (int i = Math.Max(0, firstNewFunctionIndex); i < module.Functions.Count; i++) {
      var funcId = new IrFuncId(i);
      if (funcId == chunkFuncId || module.ShutdownFuncId == funcId) {
        continue;
      }

      RewriteFunction(
          module,
          module.Functions[i],
          trackGlobalInit: false,
          previouslyInitializedGlobals: null);
    }

    PopulateShutdownFunction(module);
    module.Stage = IrPassStage.PostRcStep1;
    return module;
  }

  /// <summary>Rebuilds <c>__pino_shutdown()</c> to match the current <see cref="IrModule.Globals"/> list.</summary>
  public static void RefreshShutdown(IrModule module) {
    ArgumentNullException.ThrowIfNull(module);
    PopulateShutdownFunction(module);
  }

  // ---------------------------------------------------------------------------
  // Per-type inc_T / drop_T descriptor synthesis
  // ---------------------------------------------------------------------------

  private static void SynthesizeRcDescriptors(IrModule module) {
    IrTypeTable types = module.Types;
    var descriptors = new List<IrTypeRcDescriptor>();

    for (int i = 0; i < types.Count; i++) {
      IrType irType = types.Types[i];
      if (!irType.IsRcManaged) {
        continue;
      }

      switch (irType) {
        case IrPrimitiveType { Kind: IrTypeKind.String }:
          descriptors.Add(new IrTypeRcDescriptor(irType.Id, IrRcDescriptorKind.String));
          break;

        case IrPrimitiveType { Kind: IrTypeKind.Regex }:
          descriptors.Add(new IrTypeRcDescriptor(irType.Id, IrRcDescriptorKind.Regex));
          break;

        case IrStructType st: {
            var rcFields = ImmutableArray.CreateBuilder<IrRcFieldRef>();
            for (int f = 0; f < st.Fields.Length; f++) {
              IrStructField field = st.Fields[f];
              if (types.GetType(field.Type).IsRcManaged) {
                rcFields.Add(new IrRcFieldRef(f, field.Name, field.Type));
              }
            }

            descriptors.Add(new IrTypeRcDescriptor(
                st.Id,
                IrRcDescriptorKind.InlineStruct,
                IsCycleCapable: st.IsCycleCapable,
                RcFields: rcFields.ToImmutable()));
            break;
          }

        case IrTupleType tt: {
            var rcFields = ImmutableArray.CreateBuilder<IrRcFieldRef>();
            for (int f = 0; f < tt.Fields.Length; f++) {
              IrTupleField field = tt.Fields[f];
              if (types.GetType(field.Type).IsRcManaged) {
                rcFields.Add(new IrRcFieldRef(f, field.Label, field.Type));
              }
            }

            descriptors.Add(new IrTypeRcDescriptor(
                tt.Id,
                IrRcDescriptorKind.InlineTuple,
                IsCycleCapable: tt.IsCycleCapable,
                RcFields: rcFields.ToImmutable()));
            break;
          }

        case IrUnionType ut: {
            var variantDrops = ImmutableArray.CreateBuilder<IrRcVariantDrop>(ut.Variants.Length);
            foreach (IrUnionVariant variant in ut.Variants) {
              var rcFields = ImmutableArray.CreateBuilder<IrRcFieldRef>();
              for (int f = 0; f < variant.Fields.Length; f++) {
                IrUnionVariantField field = variant.Fields[f];
                if (types.GetType(field.Type).IsRcManaged) {
                  rcFields.Add(new IrRcFieldRef(f, field.Name ?? $"_{f}", field.Type));
                }
              }

              variantDrops.Add(new IrRcVariantDrop(variant.TagOrdinal, variant.Name, rcFields.ToImmutable()));
            }

            descriptors.Add(new IrTypeRcDescriptor(
                ut.Id,
                ut.IsRecursiveUnion ? IrRcDescriptorKind.BoxedRecursiveUnion : IrRcDescriptorKind.InlineUnion,
                IsCycleCapable: ut.IsCycleCapable,
                Variants: variantDrops.MoveToImmutable()));
            break;
          }

        case IrVectorType vt:
          descriptors.Add(new IrTypeRcDescriptor(
              vt.Id,
              IrRcDescriptorKind.Vector,
              IsCycleCapable: vt.IsCycleCapable,
              ElementType: vt.ElementType,
              NeedsElementDrop: types.GetType(vt.ElementType).IsRcManaged));
          break;

        case IrMapType mt:
          descriptors.Add(new IrTypeRcDescriptor(
              mt.Id,
              IrRcDescriptorKind.Map,
              IsCycleCapable: mt.IsCycleCapable,
              KeyType: mt.KeyType,
              NeedsKeyDrop: types.GetType(mt.KeyType).IsRcManaged,
              ValueType: mt.ValueType,
              NeedsValueDrop: types.GetType(mt.ValueType).IsRcManaged));
          break;

        case IrSetType setT:
          descriptors.Add(new IrTypeRcDescriptor(
              setT.Id,
              IrRcDescriptorKind.Set,
              IsCycleCapable: setT.IsCycleCapable,
              ElementType: setT.ElementType,
              NeedsElementDrop: types.GetType(setT.ElementType).IsRcManaged));
          break;

        case IrPoolType pt:
          descriptors.Add(new IrTypeRcDescriptor(
              pt.Id,
              IrRcDescriptorKind.Pool,
              IsCycleCapable: pt.IsCycleCapable,
              ElementType: pt.ElementType,
              NeedsElementDrop: types.GetType(pt.ElementType).IsRcManaged));
          break;

        case IrCellType ct:
          descriptors.Add(new IrTypeRcDescriptor(
              ct.Id,
              IrRcDescriptorKind.Cell,
              IsCycleCapable: ct.IsCycleCapable,
              ElementType: ct.ElementType,
              NeedsElementDrop: types.GetType(ct.ElementType).IsRcManaged));
          break;

        case IrEnvType et: {
            var rcFields = ImmutableArray.CreateBuilder<IrRcFieldRef>();
            for (int f = 0; f < et.Fields.Length; f++) {
              IrEnvField field = et.Fields[f];
              if (types.GetType(field.Type).IsRcManaged) {
                rcFields.Add(new IrRcFieldRef(f, field.Name, field.Type));
              }
            }

            descriptors.Add(new IrTypeRcDescriptor(
                et.Id,
                IrRcDescriptorKind.Env,
                IsCycleCapable: et.IsCycleCapable,
                RcFields: rcFields.ToImmutable()));
            break;
          }

        case IrClosureType closT:
          descriptors.Add(new IrTypeRcDescriptor(
              closT.Id,
              IrRcDescriptorKind.Closure,
              IsCycleCapable: closT.IsCycleCapable));
          break;

        case IrInterfaceType ifaceT:
          descriptors.Add(new IrTypeRcDescriptor(
              ifaceT.Id,
              IrRcDescriptorKind.Interface,
              IsCycleCapable: ifaceT.IsCycleCapable));
          break;
      }
    }

    module.SetRcDescriptors(descriptors);
  }

  // ---------------------------------------------------------------------------
  // __pino_shutdown() population
  // ---------------------------------------------------------------------------

  private static void PopulateShutdownFunction(IrModule module) {
    IrTypeTable types = module.Types;
    var rcGlobals = new List<IrGlobal>();
    foreach (IrGlobal g in module.Globals) {
      if (types.GetType(g.Type).IsRcManaged) {
        rcGlobals.Add(g);
      }
    }

    IrFunction? shutdownFn = null;
    if (module.ShutdownFuncId is { } sid && (uint) sid.Index < (uint) module.Functions.Count) {
      shutdownFn = module.Functions[sid.Index];
    } else if (rcGlobals.Count > 0) {
      shutdownFn = module.AddFunction("__pino_shutdown", [], types.Void, ModuleStateEffects.WriteOnly);
      shutdownFn.AddBlock().Terminator = new RetVoid();
      module.ShutdownFuncId = shutdownFn.Id;
    }

    if (shutdownFn is null) {
      return;
    }

    shutdownFn.Blocks.Clear();
    shutdownFn.Slots.Clear();
    shutdownFn.Temps.Clear();

    IrBasicBlock bb0 = shutdownFn.AddBlock();
    foreach (IrGlobal g in rcGlobals) {
      IrTempId ptrTmp = shutdownFn.AddTemp(types.GetPtr(g.Type));
      IrTempId valTmp = shutdownFn.AddTemp(g.Type);
      bb0.Instructions.Add(new AddrGlobal(ptrTmp, g.Id));
      bb0.Instructions.Add(new LoadPtr(valTmp, ptrTmp));
      bb0.Instructions.Add(new RcDec(valTmp));
    }

    bb0.Terminator = new RetVoid();
  }

  // ---------------------------------------------------------------------------
  // Per-function Step 1 RC insertion
  // ---------------------------------------------------------------------------

  private static HashSet<IrGlobalId> RewriteFunction(
      IrModule module,
      IrFunction fn,
      bool trackGlobalInit,
      HashSet<IrGlobalId>? previouslyInitializedGlobals) {
    var newlyInitializedGlobals = new HashSet<IrGlobalId>();
    if (fn.Blocks.Count == 0) {
      return newlyInitializedGlobals;
    }

    IrTypeTable types = module.Types;
    int origBlockCount = fn.Blocks.Count;
    int origTempCount = fn.Temps.Count;
    int origSlotCount = fn.Slots.Count;

    // 1. Build CFG reachability, predecessors, successors, dominators, and panic-only classification.
    BuildCfg(fn, out bool[] reachable, out List<int>[] preds, out List<int>[] succs, out int[] idom);
    bool[] panicOnly = ComputePanicOnlyBlocks(fn, reachable, succs);

    // 2. Identify RC-managed temps, slots, and pointer-alias pinning (AddrLocal -> slot, CellAddr -> cell).
    bool[] isRcTemp = new bool[origTempCount];
    for (int t = 0; t < origTempCount; t++) {
      isRcTemp[t] = types.GetType(fn.Temps[t].Type).IsRcManaged;
    }

    bool[] isRcSlot = new bool[origSlotCount];
    for (int s = 0; s < origSlotCount; s++) {
      isRcSlot[s] = types.GetType(fn.Slots[s].Type).IsRcManaged;
    }

    ComputePointerAliases(
        fn,
        origTempCount,
        out Dictionary<int, HashSet<int>> ptrToSlots,
        out Dictionary<int, HashSet<int>> ptrToPinnedCellTemps,
        out Dictionary<int, IrGlobalId> ptrToDirectGlobal);

    // 3. Optional global init tracking for __pino_init_* and __pino_entry.
    GlobalInitLattice[][]? globalInitIn = null;
    if (trackGlobalInit && module.Globals.Count > 0) {
      globalInitIn = ComputeGlobalInitState(
          fn,
          module.Globals.Count,
          reachable,
          preds,
          ptrToDirectGlobal,
          previouslyInitializedGlobals ?? [],
          newlyInitializedGlobals);
    }

    // 4. Backward liveness for RC-managed temps (including pinned CellAddr temps).
    ComputeTempLiveness(
        fn,
        origTempCount,
        isRcTemp,
        reachable,
        succs,
        ptrToPinnedCellTemps,
        out HashSet<int>[] tempLiveIn,
        out HashSet<int>[] tempLiveOut);

    // 5. Backward liveness for RC-managed local slots (including AddrLocal pointer aliases).
    ComputeSlotLiveness(
        fn,
        origSlotCount,
        isRcSlot,
        reachable,
        succs,
        ptrToSlots,
        out HashSet<int>[] slotLiveIn,
        out HashSet<int>[] slotLiveOut);

    // 6. Forward 3-state slot initialization analysis (Uninit / Init / MaybeInit).
    ComputeSlotInitState(
        fn,
        origSlotCount,
        isRcSlot,
        reachable,
        preds,
        panicOnly,
        slotLiveIn,
        slotLiveOut,
        out SlotInitLattice[][] slotInitIn,
        out SlotInitLattice[][] slotInitOut,
        out bool[] slotNeedsInitFlag);

    // Allocate bool init-flag slots ONLY for slots that ever hit MaybeInit at a store/drop site.
    var initFlagSlots = new IrSlotId?[origSlotCount];
    for (int s = 0; s < origSlotCount; s++) {
      if (slotNeedsInitFlag[s]) {
        IrSlotId flagId = fn.AddSlot($"__init_{fn.Slots[s].Name}", types.Bool, isMutSlot: true);
        initFlagSlots[s] = flagId;
        fn.Slots[s] = fn.Slots[s] with { InitFlagSlot = flagId };
      }
    }

    // 7. Compute per-block edge drops before rewriting blocks (using original CFG).
    var edgeDrops = new Dictionary<(int From, int To), (List<int> Temps, List<int> Slots)>();
    var deferredReturnSlotDrops = new Dictionary<int, List<int>>();
    var deferredReturnTempDrops = new Dictionary<int, List<int>>();

    for (int a = 0; a < origBlockCount; a++) {
      if (!reachable[a] || panicOnly[a]) {
        continue;
      }

      foreach (int b in succs[a]) {
        if (!reachable[b] || panicOnly[b]) {
          continue;
        }

        List<int>? dropTemps = null;
        foreach (int t in tempLiveOut[a]) {
          if (!tempLiveIn[b].Contains(t)) {
            dropTemps ??= [];
            dropTemps.Add(t);
          }
        }

        List<int>? dropSlots = null;
        foreach (int s in slotLiveOut[a]) {
          if (!slotLiveIn[b].Contains(s) && slotInitOut[a][s] != SlotInitLattice.Uninit) {
            dropSlots ??= [];
            dropSlots.Add(s);
          }
        }

        if (dropTemps is null && dropSlots is null) {
          continue;
        }

        dropTemps?.Sort();
        dropSlots?.Sort();

        // Cleanliness optimization (matches design.md §4.2 sum_vec):
        // If `b` has a single reachable predecessor `a`, ends in Ret/RetVoid, has no calls/mutations,
        // has no MaybeInit slots needing conditional diamonds, and does not touch the dropped slots/temps,
        // place the drop right before `b`'s Ret/RetVoid.
        if ((dropSlots is null || !HasMaybeInitSlot(dropSlots, slotInitOut[a]))
            && CanDeferEdgeDropsToReturn(fn, b, preds[b], reachable, dropTemps, dropSlots, ptrToSlots)) {
          if (dropSlots is not null) {
            deferredReturnSlotDrops[b] = dropSlots;
          }

          if (dropTemps is not null) {
            deferredReturnTempDrops[b] = dropTemps;
          }
        } else {
          edgeDrops[(a, b)] = (dropTemps ?? [], dropSlots ?? []);
        }
      }
    }

    // 8. Rewrite each reachable original block in place.
    // Track head/tail block IDs because init-flag conditional diamonds may split a block.
    var headBlockId = new IrBlockId[origBlockCount];
    var tailBlockId = new IrBlockId[origBlockCount];
    for (int b = 0; b < origBlockCount; b++) {
      headBlockId[b] = fn.Blocks[b].Id;
      tailBlockId[b] = fn.Blocks[b].Id;
    }

    var readOps = new List<IrOperand>();
    var consumingOps = new List<IrOperand>();
    var borrowingOps = new List<IrOperand>();

    for (int b = 0; b < origBlockCount; b++) {
      if (!reachable[b]) {
        continue;
      }

      bool isPanicBlock = panicOnly[b];
      IrBasicBlock block = fn.Blocks[b];
      IrInstruction[] origInstrs = [.. block.Instructions];
      IrTerminator? origTerm = block.Terminator;
      block.Instructions.Clear();
      block.Terminator = null;

      IrBasicBlock curBlock = block;

      // Initialize any bool init-flag slots to false at entry of bb0.
      if (b == 0) {
        for (int s = 0; s < origSlotCount; s++) {
          if (initFlagSlots[s] is { } flagId) {
            IrTempId cFalse = fn.AddTemp(types.Bool);
            curBlock.Instructions.Add(new ConstBool(cFalse, false));
            curBlock.Instructions.Add(new StoreSlot(flagId, cFalse));
          }
        }
      }

      // Track last-use instruction index in this block for every RC temp (including pinned CellAddr temps).
      // Value == origInstrs.Length means used in the terminator.
      var lastUseInBlock = ComputeLastUseIndexInBlock(origInstrs, origTerm, origTempCount, isRcTemp, ptrToPinnedCellTemps);

      var curSlotInit = new SlotInitLattice[origSlotCount];
      Array.Copy(slotInitIn[b], curSlotInit, origSlotCount);

      GlobalInitLattice[]? curGlobalInit = null;
      if (globalInitIn is not null) {
        curGlobalInit = new GlobalInitLattice[module.Globals.Count];
        Array.Copy(globalInitIn[b], curGlobalInit, module.Globals.Count);
      }

      for (int i = 0; i < origInstrs.Length; i++) {
        IrInstruction instr = origInstrs[i];
        SourceSpan span = instr.Span;

        // Classify consuming vs borrowing RC-managed operands for instruction `i`.
        consumingOps.Clear();
        borrowingOps.Clear();
        ClassifyInstructionRcOperands(module, fn, instr, consumingOps, borrowingOps);

        // Also include pinned CellAddr temps as borrowing uses.
        readOps.Clear();
        instr.CollectReadOperands(readOps);
        foreach (IrOperand ro in readOps) {
          if (ro.IsTemp && ptrToPinnedCellTemps.TryGetValue(ro.Index, out HashSet<int>? pinnedCells)) {
            foreach (int cellTemp in pinnedCells) {
              borrowingOps.Add(IrOperand.FromTemp(new IrTempId(cellTemp)));
            }
          }
        }

        // Count consuming & borrowing occurrences per operand in this instruction.
        var consumeCounts = new Dictionary<IrOperand, int>();
        var consumeOrder = new List<IrOperand>();
        foreach (IrOperand op in consumingOps) {
          if (!consumeCounts.TryGetValue(op, out int cnt)) {
            consumeOrder.Add(op);
          }

          consumeCounts[op] = cnt + 1;
        }

        var borrowSet = new HashSet<IrOperand>(borrowingOps);

        // Determine which operands need rc_inc before instruction `i`, and which dead temps need rc_dec after `i`.
        var movedTemps = new HashSet<int>();
        foreach (IrOperand op in consumeOrder) {
          int c = consumeCounts[op];
          if (op.IsParam) {
            // Parameters stay alive until Ret (or are borrowed); every consuming occurrence gets rc_inc.
            for (int k = 0; k < c; k++) {
              curBlock.Instructions.Add(new RcInc(op, span));
            }
          } else {
            int tIdx = op.Index;
            bool hasLaterUse = lastUseInBlock[tIdx] > i || tempLiveOut[b].Contains(tIdx);
            bool alsoBorrowedHere = borrowSet.Contains(op);
            if (!hasLaterUse && !alsoBorrowedHere) {
              // Transfer this temp's own ref to one consuming occurrence; rc_inc for any additional occurrences.
              for (int k = 0; k < c - 1; k++) {
                curBlock.Instructions.Add(new RcInc(op, span));
              }

              movedTemps.Add(tIdx);
            } else {
              for (int k = 0; k < c; k++) {
                curBlock.Instructions.Add(new RcInc(op, span));
              }
            }
          }
        }

        // Emit the instruction itself, handling overwriting stores (StoreSlot / StorePtr / CellSet)
        // and post-read RcInc for reads/projections.
        switch (instr) {
          case StoreSlot storeSlot when isRcSlot[storeSlot.Slot.Index]: {
              int sIdx = storeSlot.Slot.Index;
              SlotInitLattice sState = curSlotInit[sIdx];
              if (isPanicBlock || sState == SlotInitLattice.Uninit) {
                curBlock.Instructions.Add(storeSlot);
                if (!isPanicBlock && initFlagSlots[sIdx] is { } flagId) {
                  IrTempId cTrue = fn.AddTemp(types.Bool);
                  curBlock.Instructions.Add(new ConstBool(cTrue, true, span));
                  curBlock.Instructions.Add(new StoreSlot(flagId, cTrue, span));
                }
              } else if (sState == SlotInitLattice.Init) {
                IrTempId oldVal = fn.AddTemp(fn.Slots[sIdx].Type);
                curBlock.Instructions.Add(new LoadSlot(oldVal, storeSlot.Slot, span));
                curBlock.Instructions.Add(storeSlot);
                curBlock.Instructions.Add(new RcDec(oldVal, span));
              } else {
                // MaybeInit: guard old-value drop with the bool init flag!
                curBlock = EmitConditionalSlotDrop(fn, curBlock, storeSlot.Slot, initFlagSlots[sIdx]!.Value, setFalseAfterDrop: false, span);
                curBlock.Instructions.Add(storeSlot);
                IrTempId cTrue = fn.AddTemp(types.Bool);
                curBlock.Instructions.Add(new ConstBool(cTrue, true, span));
                curBlock.Instructions.Add(new StoreSlot(initFlagSlots[sIdx]!.Value, cTrue, span));
              }

              curSlotInit[sIdx] = SlotInitLattice.Init;
              break;
            }

          case StorePtr storePtr when IsRcStorePtr(fn, types, storePtr, out IrTypeId elemType): {
              bool isInitialGlobalStore = isPanicBlock;
              if (!isPanicBlock
                  && curGlobalInit is not null
                  && storePtr.Ptr.IsTemp
                  && ptrToDirectGlobal.TryGetValue(storePtr.Ptr.Index, out IrGlobalId gId)) {
                if (curGlobalInit[gId.Index] == GlobalInitLattice.Uninit) {
                  isInitialGlobalStore = true;
                }

                curGlobalInit[gId.Index] = GlobalInitLattice.Init;
              }

              if (isInitialGlobalStore) {
                curBlock.Instructions.Add(storePtr);
              } else {
                IrTempId oldVal = fn.AddTemp(elemType);
                curBlock.Instructions.Add(new LoadPtr(oldVal, storePtr.Ptr, span));
                curBlock.Instructions.Add(storePtr);
                curBlock.Instructions.Add(new RcDec(oldVal, span));
              }

              break;
            }

          case CellSet cellSet when IsRcCellSet(fn, types, cellSet, out IrTypeId elemType): {
              if (isPanicBlock) {
                curBlock.Instructions.Add(cellSet);
              } else {
                IrTempId oldVal = fn.AddTemp(elemType);
                curBlock.Instructions.Add(new CellGet(oldVal, cellSet.Cell, span));
                curBlock.Instructions.Add(cellSet);
                curBlock.Instructions.Add(new RcDec(oldVal, span));
              }

              break;
            }

          default: {
              curBlock.Instructions.Add(instr);
              if (instr.DefinedTemp is { } defId && isRcTemp[defId.Index]) {
                if (IsReadOrProjection(instr)) {
                  curBlock.Instructions.Add(new RcInc(defId, span));
                }

                // Unused RC-managed temp (e.g. ignored call result, ignored tuple value, unused val):
                // drop immediately after definition (never on panic-only paths).
                if (!isPanicBlock && lastUseInBlock[defId.Index] < 0 && !tempLiveOut[b].Contains(defId.Index)) {
                  curBlock.Instructions.Add(new RcDec(defId, span));
                }
              }

              break;
            }
        }

        // Drop any RC-managed temp whose last use was instruction `i` and which was NOT moved (never on panic-only paths).
        if (!isPanicBlock) {
          var droppedHere = new HashSet<int>();
          foreach (IrOperand op in borrowingOps) {
            if (op.IsTemp
                && isRcTemp[op.Index]
                && !movedTemps.Contains(op.Index)
                && lastUseInBlock[op.Index] == i
                && !tempLiveOut[b].Contains(op.Index)
                && droppedHere.Add(op.Index)) {
              curBlock.Instructions.Add(new RcDec(op, span));
            }
          }
        }
      }

      // Block exit: drop dead RC-managed local slots and (at Ret/RetVoid) @owned parameters.
      // Emit zero RcDec on panic-only paths (ERR-3, ERR-5).
      SourceSpan termSpan = origTerm?.Span ?? default;

      if (!isPanicBlock && origTerm is Ret or RetVoid) {
        // 1. If Ret returns a @borrowed RC-managed param, increment it to produce an owned return value.
        int returnedOwnedParam = -1;
        if (origTerm is Ret retTerm && retTerm.Value.IsParam) {
          int pIdx = retTerm.Value.Index;
          IrParam retParam = fn.Parameters[pIdx];
          if (types.GetType(retParam.Type).IsRcManaged) {
            if (retParam.Ownership == IrParamOwnership.Owned) {
              returnedOwnedParam = pIdx;
            } else if (retParam.Ownership == IrParamOwnership.Borrowed) {
              curBlock.Instructions.Add(new RcInc(retTerm.Value, termSpan));
            }
          }
        }

        // 2. Drop any deferred single-pred edge drops (e.g. sum_vec's $snap right before Ret).
        if (deferredReturnTempDrops.TryGetValue(b, out List<int>? defTemps)) {
          foreach (int tIdx in defTemps) {
            curBlock.Instructions.Add(new RcDec(IrOperand.FromTemp(new IrTempId(tIdx)), termSpan));
          }
        }

        if (deferredReturnSlotDrops.TryGetValue(b, out List<int>? defSlots)) {
          foreach (int sIdx in defSlots) {
            curBlock = EmitSlotDrop(fn, curBlock, new IrSlotId(sIdx), SlotInitLattice.Init, initFlagSlots[sIdx], termSpan);
          }
        }

        // 3. Drop all initialized RC-managed local slots.
        for (int s = 0; s < origSlotCount; s++) {
          if (!isRcSlot[s] || curSlotInit[s] == SlotInitLattice.Uninit) {
            continue;
          }

          curBlock = EmitSlotDrop(fn, curBlock, new IrSlotId(s), curSlotInit[s], initFlagSlots[s], termSpan);
          curSlotInit[s] = SlotInitLattice.Uninit;
        }

        // 4. Drop all @owned RC-managed parameters (except the one directly returned).
        for (int p = 0; p < fn.Parameters.Length; p++) {
          IrParam param = fn.Parameters[p];
          if (p == returnedOwnedParam
              || param.Ownership != IrParamOwnership.Owned
              || !types.GetType(param.Type).IsRcManaged) {
            continue;
          }

          curBlock.Instructions.Add(new RcDec(IrOperand.FromParam(p), termSpan));
        }
      } else if (!isPanicBlock && origTerm is not Panic) {
        // Non-return, non-panic block exit: drop any RC-managed local slot that is dead out of block `b`.
        for (int s = 0; s < origSlotCount; s++) {
          if (!isRcSlot[s] || slotLiveOut[b].Contains(s) || curSlotInit[s] == SlotInitLattice.Uninit) {
            continue;
          }

          curBlock = EmitSlotDrop(fn, curBlock, new IrSlotId(s), curSlotInit[s], initFlagSlots[s], termSpan);
          curSlotInit[s] = SlotInitLattice.Uninit;
        }
      }

      curBlock.Terminator = origTerm;
      tailBlockId[b] = curBlock.Id;
    }

    // 9. Insert edge drops for cross-block dead temps and dead slots.
    if (edgeDrops.Count > 0) {
      // Group incoming edges by target block `b` to detect shared drops across all predecessors.
      var incomingByTarget = new Dictionary<int, List<int>>();
      foreach ((int from, int to) in edgeDrops.Keys) {
        if (!incomingByTarget.TryGetValue(to, out List<int>? list)) {
          list = [];
          incomingByTarget[to] = list;
        }

        list.Add(from);
      }

      foreach ((int target, List<int> dropPreds) in incomingByTarget) {
        var reachablePreds = new List<int>();
        foreach (int p in preds[target]) {
          if (reachable[p] && !panicOnly[p]) {
            reachablePreds.Add(p);
          }
        }

        // Single reachable predecessor (and never the entry block bb0): prepend directly to headBlockId[target]
        // unless a slot needs a conditional init-flag diamond.
        if (target != 0 && reachablePreds.Count == 1 && dropPreds.Count == 1) {
          int from = dropPreds[0];
          (List<int> dropTemps, List<int> dropSlots) = edgeDrops[(from, target)];
          bool needsSplit = HasMaybeInitSlot(dropSlots, slotInitOut[from]);
          if (!needsSplit) {
            PrependSimpleDrops(fn, headBlockId[target], dropTemps, dropSlots, initFlagSlots);
            continue;
          }
        }

        // Otherwise split each edge (from -> target) cleanly via a dedicated drop trampoline block.
        foreach (int from in dropPreds) {
          (List<int> dropTemps, List<int> dropSlots) = edgeDrops[(from, target)];
          IrBasicBlock edgeHead = fn.AddBlock();
          IrBasicBlock edgeCur = edgeHead;

          foreach (int tIdx in dropTemps) {
            edgeCur.Instructions.Add(new RcDec(IrOperand.FromTemp(new IrTempId(tIdx))));
          }

          foreach (int sIdx in dropSlots) {
            edgeCur = EmitSlotDrop(fn, edgeCur, new IrSlotId(sIdx), slotInitOut[from][sIdx], initFlagSlots[sIdx], default);
          }

          edgeCur.Terminator = new Br(headBlockId[target]);
          RetargetTerminator(fn.Blocks[tailBlockId[from].Index], headBlockId[target], edgeHead.Id);
        }
      }
    }

    return newlyInitializedGlobals;
  }

  private static bool HasMaybeInitSlot(List<int> dropSlots, SlotInitLattice[] initOut) {
    foreach (int sIdx in dropSlots) {
      if (initOut[sIdx] == SlotInitLattice.MaybeInit) {
        return true;
      }
    }

    return false;
  }

  private static void PrependSimpleDrops(
      IrFunction fn,
      IrBlockId targetBlockId,
      List<int> dropTemps,
      List<int> dropSlots,
      IrSlotId?[] initFlagSlots) {
    IrBasicBlock targetBlock = fn.Blocks[targetBlockId.Index];
    var prefix = new List<IrInstruction>(dropTemps.Count + (dropSlots.Count * 2));
    foreach (int tIdx in dropTemps) {
      prefix.Add(new RcDec(IrOperand.FromTemp(new IrTempId(tIdx))));
    }

    foreach (int sIdx in dropSlots) {
      IrTempId dropTmp = fn.AddTemp(fn.Slots[sIdx].Type);
      prefix.Add(new LoadSlot(dropTmp, new IrSlotId(sIdx)));
      prefix.Add(new RcDec(dropTmp));
      if (initFlagSlots[sIdx] is { } fId) {
        IrTempId cFalse = fn.AddTemp(fn.Slots[fId.Index].Type);
        prefix.Add(new ConstBool(cFalse, false));
        prefix.Add(new StoreSlot(fId, cFalse));
      }
    }

    targetBlock.Instructions.InsertRange(0, prefix);
  }

  private static IrBasicBlock EmitSlotDrop(
      IrFunction fn,
      IrBasicBlock curBlock,
      IrSlotId slotId,
      SlotInitLattice state,
      IrSlotId? initFlagSlot,
      SourceSpan span) {
    if (state == SlotInitLattice.Uninit) {
      return curBlock;
    }

    if (state == SlotInitLattice.MaybeInit && initFlagSlot is { } flagId) {
      return EmitConditionalSlotDrop(fn, curBlock, slotId, flagId, setFalseAfterDrop: true, span);
    }

    IrTempId dropTmp = fn.AddTemp(fn.Slots[slotId.Index].Type);
    curBlock.Instructions.Add(new LoadSlot(dropTmp, slotId, span));
    curBlock.Instructions.Add(new RcDec(dropTmp, span));
    if (initFlagSlot is { } fId) {
      IrTempId cFalse = fn.AddTemp(fn.Slots[fId.Index].Type);
      curBlock.Instructions.Add(new ConstBool(cFalse, false, span));
      curBlock.Instructions.Add(new StoreSlot(fId, cFalse, span));
    }

    return curBlock;
  }

  private static IrBasicBlock EmitConditionalSlotDrop(
      IrFunction fn,
      IrBasicBlock curBlock,
      IrSlotId slotId,
      IrSlotId flagId,
      bool setFalseAfterDrop,
      SourceSpan span) {
    IrTypeId boolType = fn.Slots[flagId.Index].Type;
    IrTempId isInit = fn.AddTemp(boolType);
    curBlock.Instructions.Add(new LoadSlot(isInit, flagId, span));

    IrBasicBlock bbDrop = fn.AddBlock();
    IrBasicBlock bbAfter = fn.AddBlock();
    curBlock.Terminator = new BrIf(isInit, bbDrop.Id, bbAfter.Id, span);

    IrTempId dropTmp = fn.AddTemp(fn.Slots[slotId.Index].Type);
    bbDrop.Instructions.Add(new LoadSlot(dropTmp, slotId, span));
    bbDrop.Instructions.Add(new RcDec(dropTmp, span));
    if (setFalseAfterDrop) {
      IrTempId cFalse = fn.AddTemp(boolType);
      bbDrop.Instructions.Add(new ConstBool(cFalse, false, span));
      bbDrop.Instructions.Add(new StoreSlot(flagId, cFalse, span));
    }

    bbDrop.Terminator = new Br(bbAfter.Id, span);
    return bbAfter;
  }

  private static void RetargetTerminator(IrBasicBlock block, IrBlockId oldTarget, IrBlockId newTarget) {
    block.Terminator = block.Terminator switch {
      Br br when br.Target == oldTarget => br with { Target = newTarget },
      BrIf brIf => brIf with {
        TrueTarget = brIf.TrueTarget == oldTarget ? newTarget : brIf.TrueTarget,
        FalseTarget = brIf.FalseTarget == oldTarget ? newTarget : brIf.FalseTarget,
      },
      Switch sw => sw with {
        Cases = [.. sw.Cases.Select(c => c.Target == oldTarget ? (c.CaseValue, newTarget) : c)],
        DefaultTarget = sw.DefaultTarget == oldTarget ? newTarget : sw.DefaultTarget,
      },
      var other => other,
    };
  }

  private static bool CanDeferEdgeDropsToReturn(
      IrFunction fn,
      int targetBlockIdx,
      List<int> targetPreds,
      bool[] reachable,
      List<int>? dropTemps,
      List<int>? dropSlots,
      Dictionary<int, HashSet<int>> ptrToSlots) {
    int reachablePredCount = 0;
    foreach (int p in targetPreds) {
      if (reachable[p]) {
        reachablePredCount++;
      }
    }

    if (reachablePredCount != 1) {
      return false;
    }

    IrBasicBlock b = fn.Blocks[targetBlockIdx];
    if (b.Terminator is not (Ret or RetVoid)) {
      return false;
    }

    var dropSlotSet = dropSlots is not null ? new HashSet<int>(dropSlots) : null;
    var dropTempSet = dropTemps is not null ? new HashSet<int>(dropTemps) : null;
    var ops = new List<IrOperand>();

    foreach (IrInstruction instr in b.Instructions) {
      if (instr is CallDirect or CallClosure or CallIface or CallRuntime or StorePtr or CellSet) {
        return false;
      }

      if (dropSlotSet is not null) {
        if (instr is LoadSlot ls && dropSlotSet.Contains(ls.Slot.Index)) {
          return false;
        }

        if (instr is StoreSlot ss && dropSlotSet.Contains(ss.Slot.Index)) {
          return false;
        }

        if (instr is AddrLocal al && dropSlotSet.Contains(al.Slot.Index)) {
          return false;
        }
      }

      if (dropTempSet is not null || dropSlotSet is not null) {
        ops.Clear();
        instr.CollectReadOperands(ops);
        foreach (IrOperand op in ops) {
          if (op.IsTemp) {
            if (dropTempSet is not null && dropTempSet.Contains(op.Index)) {
              return false;
            }

            if (dropSlotSet is not null && ptrToSlots.TryGetValue(op.Index, out HashSet<int>? aliased)) {
              foreach (int s in aliased) {
                if (dropSlotSet.Contains(s)) {
                  return false;
                }
              }
            }
          }
        }
      }
    }

    return true;
  }

  // ---------------------------------------------------------------------------
  // Instruction RC classification helpers
  // ---------------------------------------------------------------------------

  internal static bool IsReadOrProjection(IrInstruction instr) => instr is
      LoadSlot
      or LoadPtr
      or FieldGet
      or UnionField
      or TupleField
      or CellGet
      or EnvFieldGet
      or IfaceFieldGet
      or VecGet
      or MapGet
      or PoolGetOrPanic
      or PoolSlotValue
      or MapSlotKey
      or MapSlotValue
      or SetSlotElem
      or Copy;

  internal static void ClassifyInstructionRcOperands(
      IrModule module,
      IrFunction fn,
      IrInstruction instr,
      List<IrOperand> consumingRcOps,
      List<IrOperand> borrowingRcOps) {
    IrTypeTable types = module.Types;

    void AddConsume(IrOperand op) {
      if (IsOperandRcManaged(fn, types, op)) {
        consumingRcOps.Add(op);
      }
    }

    void AddBorrow(IrOperand op) {
      if (IsOperandRcManaged(fn, types, op)) {
        borrowingRcOps.Add(op);
      }
    }

    switch (instr) {
      case StoreSlot s:
        AddConsume(s.Value);
        return;

      case StorePtr s:
        AddConsume(s.Value);
        return;

      case CellSet c:
        AddBorrow(c.Cell);
        AddConsume(c.Value);
        return;

      case Move m:
        AddConsume(m.Source);
        return;

      case StructNew s:
        foreach (IrOperand f in s.Fields) {
          AddConsume(f);
        }
        return;

      case TupleNew t:
        foreach (IrOperand f in t.Fields) {
          AddConsume(f);
        }
        return;

      case UnionNew u:
        foreach (IrOperand f in u.Fields) {
          AddConsume(f);
        }
        return;

      case Reuse r:
        foreach (IrOperand f in r.Fields) {
          AddConsume(f);
        }
        return;

      case IfaceBox b:
        AddConsume(b.Value);
        return;

      case CellNew c:
        AddConsume(c.InitialValue);
        return;

      case EnvNew e:
        foreach (IrOperand cap in e.Captures) {
          AddConsume(cap);
        }
        return;

      case ClosureMake c:
        if (c.Env is { } envOp) {
          AddConsume(envOp);
        }
        return;

      case VecNew v:
        foreach (IrOperand el in v.Elements) {
          AddConsume(el);
        }
        return;

      case VecPush v:
        AddConsume(v.Value);
        return;

      case MapNew m:
        foreach ((IrOperand k, IrOperand v) in m.Entries) {
          AddConsume(k);
          AddConsume(v);
        }
        return;

      case MapSet m:
        AddConsume(m.Key);
        AddConsume(m.Value);
        return;

      case SetNew s:
        foreach (IrOperand el in s.Elements) {
          AddConsume(el);
        }
        return;

      case SetAdd s:
        AddConsume(s.Value);
        return;

      case PoolAdd p:
        AddConsume(p.Value);
        return;

      case CallDirect c: {
          IrFunction callee = module.Functions[c.Callee.Index];
          for (int i = 0; i < c.Arguments.Length && i < callee.Parameters.Length; i++) {
            if (callee.Parameters[i].Ownership == IrParamOwnership.Owned) {
              AddConsume(c.Arguments[i]);
            } else {
              AddBorrow(c.Arguments[i]);
            }
          }
          return;
        }

      case CallClosure c: {
          AddBorrow(c.Closure);
          if (TryGetOperandType(fn, types, c.Closure, out IrType? closType) && closType is IrClosureType ct) {
            for (int i = 0; i < c.Arguments.Length && i < ct.Parameters.Length; i++) {
              if (ct.Parameters[i].Ownership == IrParamOwnership.Owned) {
                AddConsume(c.Arguments[i]);
              } else {
                AddBorrow(c.Arguments[i]);
              }
            }
          }
          return;
        }

      case CallIface c: {
          AddBorrow(c.Receiver);
          if (TryGetIfaceMethod(fn, types, c, out IrInterfaceMethod? method)) {
            for (int i = 0; i < c.Arguments.Length && i < method!.Parameters.Length; i++) {
              if (method.Parameters[i].Ownership == IrParamOwnership.Owned) {
                AddConsume(c.Arguments[i]);
              } else {
                AddBorrow(c.Arguments[i]);
              }
            }
          }
          return;
        }

      case Reset r:
        AddConsume(r.Value);
        return;

      case RcInc:
      case RcDec:
        return;

      default: {
          var ops = new List<IrOperand>();
          instr.CollectReadOperands(ops);
          foreach (IrOperand op in ops) {
            AddBorrow(op);
          }
          return;
        }
    }
  }

  private static bool IsRcStorePtr(IrFunction fn, IrTypeTable types, StorePtr storePtr, out IrTypeId targetTypeId) {
    if (TryGetOperandType(fn, types, storePtr.Ptr, out IrType? ptrType)
        && ptrType is IrPtrType pt
        && types.GetType(pt.TargetType).IsRcManaged) {
      targetTypeId = pt.TargetType;
      return true;
    }

    targetTypeId = IrTypeId.Invalid;
    return false;
  }

  private static bool IsRcCellSet(IrFunction fn, IrTypeTable types, CellSet cellSet, out IrTypeId elemTypeId) {
    if (TryGetOperandType(fn, types, cellSet.Cell, out IrType? cellType)
        && cellType is IrCellType ct
        && types.GetType(ct.ElementType).IsRcManaged) {
      elemTypeId = ct.ElementType;
      return true;
    }

    elemTypeId = IrTypeId.Invalid;
    return false;
  }

  private static bool IsOperandRcManaged(IrFunction fn, IrTypeTable types, IrOperand op) =>
      TryGetOperandType(fn, types, op, out IrType? irType) && irType.IsRcManaged;

  private static bool TryGetOperandType(IrFunction fn, IrTypeTable types, IrOperand op, out IrType irType) {
    IrTypeId typeId = op.Kind switch {
      IrOperandKind.Temp when (uint) op.Index < (uint) fn.Temps.Count => fn.Temps[op.Index].Type,
      IrOperandKind.Param when (uint) op.Index < (uint) fn.Parameters.Length => fn.Parameters[op.Index].Type,
      _ => IrTypeId.Invalid,
    };
    if (types.TryGetType(typeId, out IrType? resolved)) {
      irType = resolved;
      return true;
    }

    irType = null!;
    return false;
  }

  private static bool TryGetIfaceMethod(
      IrFunction fn,
      IrTypeTable types,
      CallIface call,
      out IrInterfaceMethod? method) {
    method = null;
    if (!TryGetOperandType(fn, types, call.Receiver, out IrType? recvType)) {
      return false;
    }

    IrInterfaceType? iface = call.IsMutating
        ? (recvType is IrPtrType pt && types.TryGetType(pt.TargetType, out IrType? target) ? target as IrInterfaceType : null)
        : recvType as IrInterfaceType;

    if (iface is null || (uint) call.MethodIndex >= (uint) iface.Methods.Length) {
      return false;
    }

    method = iface.Methods[call.MethodIndex];
    return true;
  }

  // ---------------------------------------------------------------------------
  // CFG, liveness, alias, and initialization dataflow analyses
  // ---------------------------------------------------------------------------

  private static void BuildCfg(
      IrFunction fn,
      out bool[] reachable,
      out List<int>[] preds,
      out List<int>[] succs,
      out int[] idom) {
    int n = fn.Blocks.Count;
    reachable = new bool[n];
    preds = new List<int>[n];
    succs = new List<int>[n];
    for (int i = 0; i < n; i++) {
      preds[i] = [];
      succs[i] = [];
    }

    var targets = new List<IrBlockId>();
    for (int b = 0; b < n; b++) {
      targets.Clear();
      fn.Blocks[b].Terminator?.CollectTargets(targets);
      var seen = new HashSet<int>();
      foreach (IrBlockId t in targets) {
        if ((uint) t.Index < (uint) n && seen.Add(t.Index)) {
          succs[b].Add(t.Index);
        }
      }
    }

    var postOrder = new List<int>(n);
    var visited = new bool[n];
    var stack = new Stack<(int Node, int EdgeIdx)>();
    visited[0] = true;
    stack.Push((0, 0));

    while (stack.Count > 0) {
      (int u, int eIdx) = stack.Pop();
      if (eIdx < succs[u].Count) {
        stack.Push((u, eIdx + 1));
        int v = succs[u][eIdx];
        preds[v].Add(u);
        if (!visited[v]) {
          visited[v] = true;
          stack.Push((v, 0));
        }
      } else {
        reachable[u] = true;
        postOrder.Add(u);
      }
    }

    var rpo = new int[postOrder.Count];
    var rpoIdx = new int[n];
    Array.Fill(rpoIdx, -1);
    for (int i = 0; i < postOrder.Count; i++) {
      int b = postOrder[postOrder.Count - 1 - i];
      rpo[i] = b;
      rpoIdx[b] = i;
    }

    idom = new int[n];
    Array.Fill(idom, -1);
    idom[0] = 0;

    bool changed = true;
    while (changed) {
      changed = false;
      for (int i = 1; i < rpo.Length; i++) {
        int b = rpo[i];
        int newIdom = -1;
        foreach (int p in preds[b]) {
          if (idom[p] == -1) {
            continue;
          }

          newIdom = newIdom == -1 ? p : IntersectDom(idom, rpoIdx, p, newIdom);
        }

        if (newIdom != -1 && idom[b] != newIdom) {
          idom[b] = newIdom;
          changed = true;
        }
      }
    }
  }

  private static int IntersectDom(int[] idom, int[] rpoIdx, int b1, int b2) {
    int f1 = b1;
    int f2 = b2;
    while (f1 != f2) {
      while (rpoIdx[f1] > rpoIdx[f2]) {
        f1 = idom[f1];
      }

      while (rpoIdx[f2] > rpoIdx[f1]) {
        f2 = idom[f2];
      }
    }

    return f1;
  }

  private static bool[] ComputePanicOnlyBlocks(IrFunction fn, bool[] reachable, List<int>[] succs) {
    int n = fn.Blocks.Count;
    var canReachNonPanic = new bool[n];
    var canReachPanic = new bool[n];

    for (int b = 0; b < n; b++) {
      if (!reachable[b]) {
        continue;
      }

      if (fn.Blocks[b].Terminator is Panic) {
        canReachPanic[b] = true;
      } else if (fn.Blocks[b].Terminator is Ret or RetVoid or Unreachable) {
        canReachNonPanic[b] = true;
      }
    }

    bool changed = true;
    while (changed) {
      changed = false;
      for (int b = 0; b < n; b++) {
        if (!reachable[b]) {
          continue;
        }

        foreach (int s in succs[b]) {
          if (canReachNonPanic[s] && !canReachNonPanic[b]) {
            canReachNonPanic[b] = true;
            changed = true;
          }

          if (canReachPanic[s] && !canReachPanic[b]) {
            canReachPanic[b] = true;
            changed = true;
          }
        }
      }
    }

    var panicOnly = new bool[n];
    for (int b = 0; b < n; b++) {
      panicOnly[b] = reachable[b] && canReachPanic[b] && !canReachNonPanic[b];
    }

    return panicOnly;
  }

  private static void ComputePointerAliases(
      IrFunction fn,
      int origTempCount,
      out Dictionary<int, HashSet<int>> ptrToSlots,
      out Dictionary<int, HashSet<int>> ptrToPinnedCellTemps,
      out Dictionary<int, IrGlobalId> ptrToDirectGlobal) {
    ptrToSlots = [];
    ptrToPinnedCellTemps = [];
    ptrToDirectGlobal = [];

    bool changed = true;
    while (changed) {
      changed = false;
      foreach (IrBasicBlock block in fn.Blocks) {
        foreach (IrInstruction instr in block.Instructions) {
          switch (instr) {
            case AddrLocal al when (uint) al.Dest.Index < (uint) origTempCount:
              if (AddAlias(ptrToSlots, al.Dest.Index, al.Slot.Index)) {
                changed = true;
              }
              break;

            case AddrGlobal ag when (uint) ag.Dest.Index < (uint) origTempCount:
              if (!ptrToDirectGlobal.ContainsKey(ag.Dest.Index)) {
                ptrToDirectGlobal[ag.Dest.Index] = ag.Global;
                changed = true;
              }
              break;

            case CellAddr ca when (uint) ca.Dest.Index < (uint) origTempCount && ca.Cell.IsTemp:
              if (AddAlias(ptrToPinnedCellTemps, ca.Dest.Index, ca.Cell.Index)) {
                changed = true;
              }
              break;

            case FieldAddr fa when fa.TargetPtr.IsTemp:
              changed |= PropagateAliases(ptrToSlots, fa.TargetPtr.Index, fa.Dest.Index);
              changed |= PropagateAliases(ptrToPinnedCellTemps, fa.TargetPtr.Index, fa.Dest.Index);
              break;

            case VecElemAddr va when va.VecPtr.IsTemp:
              changed |= PropagateAliases(ptrToSlots, va.VecPtr.Index, va.Dest.Index);
              changed |= PropagateAliases(ptrToPinnedCellTemps, va.VecPtr.Index, va.Dest.Index);
              break;

            case MapElemAddr ma when ma.MapPtr.IsTemp:
              changed |= PropagateAliases(ptrToSlots, ma.MapPtr.Index, ma.Dest.Index);
              changed |= PropagateAliases(ptrToPinnedCellTemps, ma.MapPtr.Index, ma.Dest.Index);
              break;

            case PoolElemAddr pa when pa.PoolPtr.IsTemp:
              changed |= PropagateAliases(ptrToSlots, pa.PoolPtr.Index, pa.Dest.Index);
              changed |= PropagateAliases(ptrToPinnedCellTemps, pa.PoolPtr.Index, pa.Dest.Index);
              break;

            case IfaceFieldAddr ia when ia.IfacePtr.IsTemp:
              changed |= PropagateAliases(ptrToSlots, ia.IfacePtr.Index, ia.Dest.Index);
              changed |= PropagateAliases(ptrToPinnedCellTemps, ia.IfacePtr.Index, ia.Dest.Index);
              break;

            case Copy cp when cp.Source.IsTemp:
              changed |= PropagateAliases(ptrToSlots, cp.Source.Index, cp.Dest.Index);
              changed |= PropagateAliases(ptrToPinnedCellTemps, cp.Source.Index, cp.Dest.Index);
              break;

            case Move mv when mv.Source.IsTemp:
              changed |= PropagateAliases(ptrToSlots, mv.Source.Index, mv.Dest.Index);
              changed |= PropagateAliases(ptrToPinnedCellTemps, mv.Source.Index, mv.Dest.Index);
              break;
          }
        }
      }
    }
  }

  private static bool AddAlias(Dictionary<int, HashSet<int>> map, int key, int value) {
    if (!map.TryGetValue(key, out HashSet<int>? set)) {
      set = [];
      map[key] = set;
    }

    return set.Add(value);
  }

  private static bool PropagateAliases(Dictionary<int, HashSet<int>> map, int srcTemp, int dstTemp) {
    if (!map.TryGetValue(srcTemp, out HashSet<int>? srcSet)) {
      return false;
    }

    if (!map.TryGetValue(dstTemp, out HashSet<int>? dstSet)) {
      dstSet = [];
      map[dstTemp] = dstSet;
    }

    bool changed = false;
    foreach (int item in srcSet) {
      if (dstSet.Add(item)) {
        changed = true;
      }
    }

    return changed;
  }

  private static void ComputeTempLiveness(
      IrFunction fn,
      int origTempCount,
      bool[] isRcTemp,
      bool[] reachable,
      List<int>[] succs,
      Dictionary<int, HashSet<int>> ptrToPinnedCellTemps,
      out HashSet<int>[] liveIn,
      out HashSet<int>[] liveOut) {
    int n = fn.Blocks.Count;
    var use = new HashSet<int>[n];
    var def = new HashSet<int>[n];
    liveIn = new HashSet<int>[n];
    liveOut = new HashSet<int>[n];

    var ops = new List<IrOperand>();
    for (int b = 0; b < n; b++) {
      use[b] = [];
      def[b] = [];
      liveIn[b] = [];
      liveOut[b] = [];
      if (!reachable[b]) {
        continue;
      }

      IrBasicBlock block = fn.Blocks[b];
      foreach (IrInstruction instr in block.Instructions) {
        ops.Clear();
        instr.CollectReadOperands(ops);
        foreach (IrOperand op in ops) {
          if (op.IsTemp && (uint) op.Index < (uint) origTempCount) {
            if (isRcTemp[op.Index] && !def[b].Contains(op.Index)) {
              use[b].Add(op.Index);
            }

            if (ptrToPinnedCellTemps.TryGetValue(op.Index, out HashSet<int>? pinned)) {
              foreach (int cellTemp in pinned) {
                if (isRcTemp[cellTemp] && !def[b].Contains(cellTemp)) {
                  use[b].Add(cellTemp);
                }
              }
            }
          }
        }

        if (instr.DefinedTemp is { } d && (uint) d.Index < (uint) origTempCount && isRcTemp[d.Index]) {
          def[b].Add(d.Index);
        }
      }

      if (block.Terminator is { } term) {
        ops.Clear();
        term.CollectReadOperands(ops);
        foreach (IrOperand op in ops) {
          if (op.IsTemp && (uint) op.Index < (uint) origTempCount && isRcTemp[op.Index] && !def[b].Contains(op.Index)) {
            use[b].Add(op.Index);
          }
        }
      }
    }

    bool changed = true;
    while (changed) {
      changed = false;
      for (int b = n - 1; b >= 0; b--) {
        if (!reachable[b]) {
          continue;
        }

        foreach (int s in succs[b]) {
          if (!reachable[s]) {
            continue;
          }

          foreach (int t in liveIn[s]) {
            if (liveOut[b].Add(t)) {
              changed = true;
            }
          }
        }

        foreach (int t in use[b]) {
          if (liveIn[b].Add(t)) {
            changed = true;
          }
        }

        foreach (int t in liveOut[b]) {
          if (!def[b].Contains(t) && liveIn[b].Add(t)) {
            changed = true;
          }
        }
      }
    }
  }

  private static void ComputeSlotLiveness(
      IrFunction fn,
      int origSlotCount,
      bool[] isRcSlot,
      bool[] reachable,
      List<int>[] succs,
      Dictionary<int, HashSet<int>> ptrToSlots,
      out HashSet<int>[] liveIn,
      out HashSet<int>[] liveOut) {
    int n = fn.Blocks.Count;
    var use = new HashSet<int>[n];
    var def = new HashSet<int>[n];
    liveIn = new HashSet<int>[n];
    liveOut = new HashSet<int>[n];

    var ops = new List<IrOperand>();
    for (int b = 0; b < n; b++) {
      use[b] = [];
      def[b] = [];
      liveIn[b] = [];
      liveOut[b] = [];
      if (!reachable[b]) {
        continue;
      }

      IrBasicBlock block = fn.Blocks[b];
      foreach (IrInstruction instr in block.Instructions) {
        if (instr is LoadSlot ls && (uint) ls.Slot.Index < (uint) origSlotCount && isRcSlot[ls.Slot.Index]) {
          if (!def[b].Contains(ls.Slot.Index)) {
            use[b].Add(ls.Slot.Index);
          }
        } else if (instr is AddrLocal al && (uint) al.Slot.Index < (uint) origSlotCount && isRcSlot[al.Slot.Index]) {
          if (!def[b].Contains(al.Slot.Index)) {
            use[b].Add(al.Slot.Index);
          }
        }

        ops.Clear();
        instr.CollectReadOperands(ops);
        foreach (IrOperand op in ops) {
          if (op.IsTemp && ptrToSlots.TryGetValue(op.Index, out HashSet<int>? aliased)) {
            foreach (int sIdx in aliased) {
              if ((uint) sIdx < (uint) origSlotCount && isRcSlot[sIdx] && !def[b].Contains(sIdx)) {
                use[b].Add(sIdx);
              }
            }
          }
        }

        if (instr is StoreSlot ss && (uint) ss.Slot.Index < (uint) origSlotCount && isRcSlot[ss.Slot.Index]) {
          def[b].Add(ss.Slot.Index);
        }
      }
    }

    bool changed = true;
    while (changed) {
      changed = false;
      for (int b = n - 1; b >= 0; b--) {
        if (!reachable[b]) {
          continue;
        }

        foreach (int s in succs[b]) {
          if (!reachable[s]) {
            continue;
          }

          foreach (int slot in liveIn[s]) {
            if (liveOut[b].Add(slot)) {
              changed = true;
            }
          }
        }

        foreach (int slot in use[b]) {
          if (liveIn[b].Add(slot)) {
            changed = true;
          }
        }

        foreach (int slot in liveOut[b]) {
          if (!def[b].Contains(slot) && liveIn[b].Add(slot)) {
            changed = true;
          }
        }
      }
    }
  }

  private static void ComputeSlotInitState(
      IrFunction fn,
      int origSlotCount,
      bool[] isRcSlot,
      bool[] reachable,
      List<int>[] preds,
      bool[] panicOnly,
      HashSet<int>[] slotLiveIn,
      HashSet<int>[] slotLiveOut,
      out SlotInitLattice[][] initIn,
      out SlotInitLattice[][] initOut,
      out bool[] needsInitFlag) {
    int n = fn.Blocks.Count;
    initIn = new SlotInitLattice[n][];
    initOut = new SlotInitLattice[n][];
    var storesSlot = new bool[n][];

    for (int b = 0; b < n; b++) {
      initIn[b] = new SlotInitLattice[origSlotCount];
      initOut[b] = new SlotInitLattice[origSlotCount];
      storesSlot[b] = new bool[origSlotCount];
      foreach (IrInstruction instr in fn.Blocks[b].Instructions) {
        if (instr is StoreSlot ss && (uint) ss.Slot.Index < (uint) origSlotCount && isRcSlot[ss.Slot.Index]) {
          storesSlot[b][ss.Slot.Index] = true;
        }
      }
    }

    for (int s = 0; s < origSlotCount; s++) {
      initIn[0][s] = SlotInitLattice.Uninit;
    }

    bool changed = true;
    while (changed) {
      changed = false;
      for (int b = 0; b < n; b++) {
        if (!reachable[b] || panicOnly[b]) {
          continue;
        }

        if (b != 0) {
          for (int s = 0; s < origSlotCount; s++) {
            if (!isRcSlot[s]) {
              continue;
            }

            SlotInitLattice joined = SlotInitLattice.Bottom;
            foreach (int p in preds[b]) {
              if (!reachable[p] || panicOnly[p]) {
                continue;
              }

              SlotInitLattice predOut = initOut[p][s];
              if (predOut == SlotInitLattice.Bottom) {
                continue;
              }

              // If edge p -> b drops slot `s`, `s` enters `b` as Uninit.
              SlotInitLattice edgeIn = slotLiveOut[p].Contains(s) && !slotLiveIn[b].Contains(s)
                  ? SlotInitLattice.Uninit
                  : predOut;
              joined = JoinSlotInit(joined, edgeIn);
            }

            if (joined != SlotInitLattice.Bottom && joined != initIn[b][s]) {
              initIn[b][s] = joined;
              changed = true;
            }
          }
        }

        for (int s = 0; s < origSlotCount; s++) {
          if (!isRcSlot[s]) {
            continue;
          }

          SlotInitLattice state = initIn[b][s];
          if (state == SlotInitLattice.Bottom) {
            continue;
          }

          if (storesSlot[b][s]) {
            state = SlotInitLattice.Init;
          }

          // Block-exit drop when `s` is dead out of block `b` (including Ret/RetVoid).
          if (!slotLiveOut[b].Contains(s)) {
            state = SlotInitLattice.Uninit;
          }

          if (state != initOut[b][s]) {
            initOut[b][s] = state;
            changed = true;
          }
        }
      }
    }

    // Any remaining Bottom (only possible in disconnected cycles) defaults to Uninit.
    for (int b = 0; b < n; b++) {
      for (int s = 0; s < origSlotCount; s++) {
        if (initIn[b][s] == SlotInitLattice.Bottom) {
          initIn[b][s] = SlotInitLattice.Uninit;
        }

        if (initOut[b][s] == SlotInitLattice.Bottom) {
          initOut[b][s] = SlotInitLattice.Uninit;
        }
      }
    }

    needsInitFlag = new bool[origSlotCount];
    for (int b = 0; b < n; b++) {
      if (!reachable[b] || panicOnly[b]) {
        continue;
      }

      for (int s = 0; s < origSlotCount; s++) {
        if (!isRcSlot[s]) {
          continue;
        }

        if (initIn[b][s] == SlotInitLattice.MaybeInit || initOut[b][s] == SlotInitLattice.MaybeInit) {
          needsInitFlag[s] = true;
        }
      }
    }
  }

  private static SlotInitLattice JoinSlotInit(SlotInitLattice a, SlotInitLattice b) {
    if (a == SlotInitLattice.Bottom) {
      return b;
    }

    if (b == SlotInitLattice.Bottom) {
      return a;
    }

    return a == b ? a : SlotInitLattice.MaybeInit;
  }

  private static GlobalInitLattice[][] ComputeGlobalInitState(
      IrFunction fn,
      int globalCount,
      bool[] reachable,
      List<int>[] preds,
      Dictionary<int, IrGlobalId> ptrToDirectGlobal,
      HashSet<IrGlobalId> previouslyInitialized,
      HashSet<IrGlobalId> newlyInitialized) {
    int n = fn.Blocks.Count;
    var initIn = new GlobalInitLattice[n][];
    var initOut = new GlobalInitLattice[n][];
    var storesGlobal = new bool[n][];

    for (int b = 0; b < n; b++) {
      initIn[b] = new GlobalInitLattice[globalCount];
      initOut[b] = new GlobalInitLattice[globalCount];
      storesGlobal[b] = new bool[globalCount];

      foreach (IrInstruction instr in fn.Blocks[b].Instructions) {
        if (instr is StorePtr sp
            && sp.Ptr.IsTemp
            && ptrToDirectGlobal.TryGetValue(sp.Ptr.Index, out IrGlobalId gId)
            && (uint) gId.Index < (uint) globalCount) {
          storesGlobal[b][gId.Index] = true;
        }
      }
    }

    for (int g = 0; g < globalCount; g++) {
      initIn[0][g] = previouslyInitialized.Contains(new IrGlobalId(g))
          ? GlobalInitLattice.Init
          : GlobalInitLattice.Uninit;
    }

    bool changed = true;
    while (changed) {
      changed = false;
      for (int b = 0; b < n; b++) {
        if (!reachable[b]) {
          continue;
        }

        if (b != 0) {
          for (int g = 0; g < globalCount; g++) {
            GlobalInitLattice joined = GlobalInitLattice.Bottom;
            foreach (int p in preds[b]) {
              if (!reachable[p]) {
                continue;
              }

              GlobalInitLattice po = initOut[p][g];
              if (po == GlobalInitLattice.Bottom) {
                continue;
              }

              joined = joined == GlobalInitLattice.Bottom
                  ? po
                  : (joined == po ? joined : GlobalInitLattice.MaybeInit);
            }

            if (joined != GlobalInitLattice.Bottom && joined != initIn[b][g]) {
              initIn[b][g] = joined;
              changed = true;
            }
          }
        }

        for (int g = 0; g < globalCount; g++) {
          GlobalInitLattice state = initIn[b][g];
          if (state == GlobalInitLattice.Bottom) {
            continue;
          }

          if (storesGlobal[b][g]) {
            state = GlobalInitLattice.Init;
          }

          if (state != initOut[b][g]) {
            initOut[b][g] = state;
            changed = true;
          }
        }
      }
    }

    // Record globals initialized along all normal return paths of this init function.
    for (int g = 0; g < globalCount; g++) {
      bool anyReturn = false;
      bool allInit = true;
      for (int b = 0; b < n; b++) {
        if (reachable[b] && fn.Blocks[b].Terminator is Ret or RetVoid) {
          anyReturn = true;
          if (initOut[b][g] != GlobalInitLattice.Init) {
            allInit = false;
            break;
          }
        }
      }

      if (anyReturn && allInit) {
        newlyInitialized.Add(new IrGlobalId(g));
      }
    }

    return initIn;
  }

  private static int[] ComputeLastUseIndexInBlock(
      IrInstruction[] origInstrs,
      IrTerminator? origTerm,
      int origTempCount,
      bool[] isRcTemp,
      Dictionary<int, HashSet<int>> ptrToPinnedCellTemps) {
    var lastUse = new int[origTempCount];
    Array.Fill(lastUse, -1);

    var ops = new List<IrOperand>();
    for (int i = 0; i < origInstrs.Length; i++) {
      ops.Clear();
      origInstrs[i].CollectReadOperands(ops);
      foreach (IrOperand op in ops) {
        if (op.IsTemp && (uint) op.Index < (uint) origTempCount) {
          if (isRcTemp[op.Index]) {
            lastUse[op.Index] = i;
          }

          if (ptrToPinnedCellTemps.TryGetValue(op.Index, out HashSet<int>? pinned)) {
            foreach (int cellTemp in pinned) {
              if (isRcTemp[cellTemp]) {
                lastUse[cellTemp] = i;
              }
            }
          }
        }
      }
    }

    if (origTerm is not null) {
      ops.Clear();
      origTerm.CollectReadOperands(ops);
      foreach (IrOperand op in ops) {
        if (op.IsTemp && (uint) op.Index < (uint) origTempCount && isRcTemp[op.Index]) {
          lastUse[op.Index] = origInstrs.Length;
        }
      }
    }

    return lastUse;
  }
}
