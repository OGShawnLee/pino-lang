using System.Collections.Immutable;

namespace Pino.Compiler.IR;

public sealed record IrVerificationError(
    string RuleCode,
    string FunctionName,
    IrBlockId? BlockId,
    int? InstructionIndex,
    string Message) {
  public override string ToString() {
    string loc = BlockId is { } bb
        ? (InstructionIndex is { } idx ? $"bb{bb.Index}[{idx}]" : $"bb{bb.Index}")
        : "<module>";
    return $"[{RuleCode}] {FunctionName} {loc}: {Message}";
  }
}

public sealed record IrVerificationResult(ImmutableArray<IrVerificationError> Errors) {
  public bool IsValid => Errors.IsEmpty;
}

/// <summary>
/// Structural, CFG, dominance, type, RC, mutability, and region verifier for <see cref="IrModule"/>.
/// Never throws on malformed IR; returns structured <see cref="IrVerificationError"/> entries.
/// </summary>
public static class IrVerifier {
  public static IrVerificationResult Verify(IrModule module) {
    ArgumentNullException.ThrowIfNull(module);
    var errors = ImmutableArray.CreateBuilder<IrVerificationError>();

    VerifyModuleHeaders(module, errors);
    foreach (IrFunction fn in module.Functions) {
      VerifyFunctionCore(module, fn, errors);
    }

    return new IrVerificationResult(errors.ToImmutable());
  }

  public static IrVerificationResult VerifyFunction(IrModule module, IrFunction fn) {
    ArgumentNullException.ThrowIfNull(module);
    ArgumentNullException.ThrowIfNull(fn);
    var errors = ImmutableArray.CreateBuilder<IrVerificationError>();
    VerifyFunctionCore(module, fn, errors);
    return new IrVerificationResult(errors.ToImmutable());
  }

  private static void VerifyModuleHeaders(
      IrModule module,
      ImmutableArray<IrVerificationError>.Builder errors) {
    IrTypeTable types = module.Types;

    for (int i = 0; i < module.Globals.Count; i++) {
      IrGlobal g = module.Globals[i];
      if (!types.TryGetType(g.Type, out IrType? gType)) {
        errors.Add(new IrVerificationError("IR-TYPE-01", "<module>", null, null, $"Global '${g.Name}' has invalid type {g.Type}."));
      } else if (gType.Kind is IrTypeKind.Void or IrTypeKind.Never or IrTypeKind.Ptr) {
        errors.Add(new IrVerificationError("IR-TYPE-02", "<module>", null, null, $"Global '${g.Name}' cannot have type '{gType.Name}'."));
      }
    }

    for (int i = 0; i < module.VTables.Count; i++) {
      IrVTable vt = module.VTables[i];
      if (!types.TryGetType(vt.InterfaceType, out IrType? ifaceType) || ifaceType.Kind != IrTypeKind.Interface) {
        errors.Add(new IrVerificationError("IR-MOD-01", "<module>", null, null, $"VTable vt{i} interface type {vt.InterfaceType} is not an interface."));
      }

      if (!types.TryGetType(vt.ConcreteStructType, out IrType? structType) || structType.Kind != IrTypeKind.Struct) {
        errors.Add(new IrVerificationError("IR-MOD-01", "<module>", null, null, $"VTable vt{i} concrete type {vt.ConcreteStructType} is not a struct."));
      }

      foreach (IrFuncId mId in vt.MethodFuncIds) {
        if ((uint) mId.Index >= (uint) module.Functions.Count) {
          errors.Add(new IrVerificationError("IR-MOD-01", "<module>", null, null, $"VTable vt{i} references out-of-range function {mId}."));
        }
      }

      CheckOptionalFuncRef(module, vt.DropFuncId, $"VTable vt{i} drop", errors);
      CheckOptionalFuncRef(module, vt.CloneFuncId, $"VTable vt{i} clone", errors);
      CheckOptionalFuncRef(module, vt.EqFuncId, $"VTable vt{i} eq", errors);
      CheckOptionalFuncRef(module, vt.ToStrFuncId, $"VTable vt{i} to_str", errors);
    }

    foreach (IrFuncId initId in module.InitFuncIds) {
      CheckOptionalFuncRef(module, initId, "init", errors);
    }

    CheckOptionalFuncRef(module, module.EntryFuncId, "entry", errors);
    CheckOptionalFuncRef(module, module.ShutdownFuncId, "shutdown", errors);
  }

  private static void CheckOptionalFuncRef(
      IrModule module,
      IrFuncId? funcId,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (funcId is { } id && (uint) id.Index >= (uint) module.Functions.Count) {
      errors.Add(new IrVerificationError("IR-MOD-01", "<module>", null, null, $"Module {label} references out-of-range function {id}."));
    }
  }

  private static void VerifyFunctionCore(
      IrModule module,
      IrFunction fn,
      ImmutableArray<IrVerificationError>.Builder errors) {
    IrTypeTable types = module.Types;
    string fnName = fn.Name;

    // 1. Validate parameters, return type, slots, and temps.
    for (int i = 0; i < fn.Parameters.Length; i++) {
      IrParam param = fn.Parameters[i];
      if (!types.TryGetType(param.Type, out IrType? pType)) {
        errors.Add(new IrVerificationError("IR-TYPE-01", fnName, null, null, $"Parameter '%{param.Name}' has invalid type {param.Type}."));
        continue;
      }

      if (param.Ownership == IrParamOwnership.Mut) {
        if (pType.Kind != IrTypeKind.Ptr) {
          errors.Add(new IrVerificationError("IR-MUT-01", fnName, null, null, $"@mut parameter '%{param.Name}' must have type ptr<T>, got '{pType.Name}'."));
        }
      } else if (pType.Kind is IrTypeKind.Ptr or IrTypeKind.Void or IrTypeKind.Never) {
        errors.Add(new IrVerificationError("IR-TYPE-02", fnName, null, null, $"Parameter '%{param.Name}' cannot have non-value type '{pType.Name}'."));
      }
    }

    if (!types.TryGetType(fn.ReturnType, out IrType? retType)) {
      errors.Add(new IrVerificationError("IR-TYPE-01", fnName, null, null, $"Return type {fn.ReturnType} is invalid."));
    } else if (retType.Kind == IrTypeKind.Ptr) {
      errors.Add(new IrVerificationError("IR-TYPE-02", fnName, null, null, $"Function cannot return pointer type '{retType.Name}'."));
    }

    for (int i = 0; i < fn.Slots.Count; i++) {
      IrLocalSlot slot = fn.Slots[i];
      if (!types.TryGetType(slot.Type, out IrType? sType)) {
        errors.Add(new IrVerificationError("IR-TYPE-01", fnName, null, null, $"Slot '${slot.Name}' has invalid type {slot.Type}."));
      } else if (sType.Kind is IrTypeKind.Void or IrTypeKind.Never or IrTypeKind.Ptr) {
        errors.Add(new IrVerificationError("IR-TYPE-02", fnName, null, null, $"Slot '${slot.Name}' cannot have type '{sType.Name}'."));
      }
    }

    for (int i = 0; i < fn.Temps.Count; i++) {
      IrTemp temp = fn.Temps[i];
      if (!types.IsValidId(temp.Type)) {
        errors.Add(new IrVerificationError("IR-TYPE-01", fnName, null, null, $"Temp %t{i} has invalid type {temp.Type}."));
      }
    }

    // 2. Validate blocks, entry bb0, terminators, and CFG targets.
    if (fn.Blocks.Count == 0) {
      errors.Add(new IrVerificationError("IR-CFG-01", fnName, null, null, "Function must contain at least entry block bb0."));
      return;
    }

    if (fn.Blocks[0].Id.Index != 0) {
      errors.Add(new IrVerificationError("IR-CFG-01", fnName, fn.Blocks[0].Id, null, $"Entry block must be bb0, got bb{fn.Blocks[0].Id.Index}."));
    }

    int blockCount = fn.Blocks.Count;
    var targets = new List<IrBlockId>();
    var switchCases = new HashSet<int>();

    for (int b = 0; b < blockCount; b++) {
      IrBasicBlock block = fn.Blocks[b];
      if (block.Id.Index != b) {
        errors.Add(new IrVerificationError("IR-CFG-01", fnName, block.Id, null, $"Block at index {b} has mismatched id bb{block.Id.Index}."));
      }

      if (block.Terminator is null) {
        errors.Add(new IrVerificationError("IR-CFG-02", fnName, block.Id, block.Instructions.Count, $"Block bb{block.Id.Index} is missing a terminator."));
        continue;
      }

      targets.Clear();
      block.Terminator.CollectTargets(targets);
      foreach (IrBlockId target in targets) {
        if ((uint) target.Index >= (uint) blockCount) {
          errors.Add(new IrVerificationError("IR-CFG-03", fnName, block.Id, block.Instructions.Count, $"Terminator targets invalid block bb{target.Index} (valid: 0..{blockCount - 1})."));
        }
      }

      if (block.Terminator is Switch sw) {
        switchCases.Clear();
        foreach ((int caseVal, _) in sw.Cases) {
          if (!switchCases.Add(caseVal)) {
            errors.Add(new IrVerificationError("IR-CFG-03", fnName, block.Id, block.Instructions.Count, $"Switch has duplicate case value {caseVal}."));
          }
        }
      }
    }

    // 3. Single-definition tracking for %t temps.
    var tempDef = new (int Block, int Instr)?[fn.Temps.Count];
    for (int b = 0; b < blockCount; b++) {
      IrBasicBlock block = fn.Blocks[b];
      for (int i = 0; i < block.Instructions.Count; i++) {
        IrInstruction instr = block.Instructions[i];
        if (instr.DefinedTemp is { } defId) {
          if ((uint) defId.Index >= (uint) fn.Temps.Count) {
            errors.Add(new IrVerificationError("IR-SSA-01", fnName, block.Id, i, $"Instruction defines out-of-range temp {defId}."));
          } else if (tempDef[defId.Index] is { } prev) {
            errors.Add(new IrVerificationError("IR-SSA-02", fnName, block.Id, i, $"Temp {FormatTempName(fn, defId)} is multiply defined (first defined in bb{prev.Block}[{prev.Instr}])."));
          } else {
            tempDef[defId.Index] = (b, i);
          }
        }
      }
    }

    // 4. Compute reachability & exact dominators from bb0 (Cooper-Harvey-Kennedy).
    (bool[] reachable, int[] idom) = ComputeDominators(fn);

    // 5. Verify operand existence + dominance, types, RC rules, @mut rules, and arena scopes.
    var readOps = new List<IrOperand>();
    for (int b = 0; b < blockCount; b++) {
      IrBasicBlock block = fn.Blocks[b];
      for (int i = 0; i < block.Instructions.Count; i++) {
        IrInstruction instr = block.Instructions[i];
        readOps.Clear();
        instr.CollectReadOperands(readOps);
        foreach (IrOperand op in readOps) {
          VerifyOperandUse(fn, block.Id, i, op, reachable, tempDef, idom, errors);
        }

        VerifyInstructionSemantics(module, fn, block.Id, i, instr, errors);
      }

      if (block.Terminator is { } term) {
        readOps.Clear();
        term.CollectReadOperands(readOps);
        int termIndex = block.Instructions.Count;
        foreach (IrOperand op in readOps) {
          VerifyOperandUse(fn, block.Id, termIndex, op, reachable, tempDef, idom, errors);
        }

        VerifyTerminatorSemantics(module, fn, block.Id, termIndex, term, errors);
      }
    }

    VerifyArenaScopes(fn, reachable, errors);
  }

  private static void VerifyOperandUse(
      IrFunction fn,
      IrBlockId useBlock,
      int useIndex,
      IrOperand op,
      bool[] reachable,
      (int Block, int Instr)?[] tempDef,
      int[] idom,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (op.Kind == IrOperandKind.Param) {
      if ((uint) op.Index >= (uint) fn.Parameters.Length) {
        errors.Add(new IrVerificationError("IR-SSA-01", fn.Name, useBlock, useIndex, $"Operand references out-of-range parameter index {op.Index}."));
      }

      return;
    }

    if (op.Kind != IrOperandKind.Temp || (uint) op.Index >= (uint) fn.Temps.Count) {
      errors.Add(new IrVerificationError("IR-SSA-01", fn.Name, useBlock, useIndex, $"Operand references out-of-range temp {op.TempId}."));
      return;
    }

    if (tempDef[op.Index] is not { } def) {
      errors.Add(new IrVerificationError("IR-SSA-03", fn.Name, useBlock, useIndex, $"Use of undefined temp {FormatTempName(fn, op.TempId)}."));
      return;
    }

    if (def.Block == useBlock.Index) {
      if (def.Instr >= useIndex) {
        errors.Add(new IrVerificationError("IR-SSA-04", fn.Name, useBlock, useIndex, $"Use of temp {FormatTempName(fn, op.TempId)} precedes its definition at bb{def.Block}[{def.Instr}]."));
      }

      return;
    }

    if (reachable[useBlock.Index]) {
      if (!reachable[def.Block] || !Dominates(idom, def.Block, useBlock.Index)) {
        errors.Add(new IrVerificationError("IR-SSA-04", fn.Name, useBlock, useIndex, $"Use of temp {FormatTempName(fn, op.TempId)} in bb{useBlock.Index}[{useIndex}] is not dominated by definition in bb{def.Block}[{def.Instr}]."));
      }
    } else if (def.Block != useBlock.Index) {
      errors.Add(new IrVerificationError("IR-SSA-04", fn.Name, useBlock, useIndex, $"Use of temp {FormatTempName(fn, op.TempId)} in unreachable bb{useBlock.Index} is not dominated by bb{def.Block}."));
    }
  }

  private static void VerifyInstructionSemantics(
      IrModule module,
      IrFunction fn,
      IrBlockId bb,
      int idx,
      IrInstruction instr,
      ImmutableArray<IrVerificationError>.Builder errors) {
    IrTypeTable types = module.Types;
    string fnName = fn.Name;

    switch (instr) {
      case ConstInt c:
        ExpectTempType(fn, types, bb, idx, c.Dest, types.Int, "const.i64", errors);
        break;

      case ConstFloat c:
        ExpectTempType(fn, types, bb, idx, c.Dest, types.Float, "const.f64", errors);
        break;

      case ConstBool c:
        ExpectTempType(fn, types, bb, idx, c.Dest, types.Bool, "const.bool", errors);
        break;

      case ConstRune c:
        ExpectTempType(fn, types, bb, idx, c.Dest, types.Rune, "const.rune", errors);
        break;

      case ConstString c:
        if ((uint) c.StringPoolId >= (uint) module.StringPool.Count) {
          errors.Add(new IrVerificationError("IR-SSA-01", fnName, bb, idx, $"const.str references out-of-range string pool index {c.StringPoolId}."));
        }
        ExpectTempType(fn, types, bb, idx, c.Dest, types.String, "const.str", errors);
        break;

      case ConstEnum c:
        if (!types.TryGetType(c.EnumType, out IrType? enumType) || enumType is not IrEnumType et) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"const.enum requires an enum type, got {c.EnumType}."));
        } else {
          if ((uint) c.Ordinal >= (uint) et.MemberNames.Length) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"const.enum ordinal {c.Ordinal} is out of range for '{et.Name}'."));
          }
          ExpectTempType(fn, types, bb, idx, c.Dest, c.EnumType, "const.enum", errors);
        }
        break;

      case Copy c:
        ExpectSameType(fn, types, bb, idx, c.Dest, c.Source, "copy", errors);
        break;

      case Move m:
        ExpectSameType(fn, types, bb, idx, m.Dest, m.Source, "move", errors);
        break;

      case LoadSlot l:
        if (TryGetSlot(fn, bb, idx, l.Slot, "load", errors, out IrLocalSlot? loadSlot)) {
          ExpectTempType(fn, types, bb, idx, l.Dest, loadSlot!.Type, "load", errors);
        }
        break;

      case StoreSlot s:
        if (TryGetSlot(fn, bb, idx, s.Slot, "store", errors, out IrLocalSlot? storeSlot)) {
          ExpectOperandType(fn, types, bb, idx, s.Value, storeSlot!.Type, "store value", errors);
        }
        break;

      case AddrLocal a:
        if (TryGetSlot(fn, bb, idx, a.Slot, "addr_local", errors, out IrLocalSlot? addrSlot)) {
          ExpectPtrToType(fn, types, bb, idx, a.Dest, addrSlot!.Type, "addr_local", errors);
        }
        break;

      case AddrGlobal a:
        if ((uint) a.Global.Index >= (uint) module.Globals.Count) {
          errors.Add(new IrVerificationError("IR-SSA-01", fnName, bb, idx, $"addr_global references out-of-range global {a.Global}."));
        } else {
          ExpectPtrToType(fn, types, bb, idx, a.Dest, module.Globals[a.Global.Index].Type, "addr_global", errors);
        }
        break;

      case FieldGet f:
        if (TryGetOperandIrType(fn, types, f.Target, out IrType? fgTarget)) {
          if (fgTarget is not IrStructType st) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"field_get target must be a struct, got '{fgTarget.Name}'."));
          } else if ((uint) f.FieldIndex >= (uint) st.Fields.Length) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"field_get index {f.FieldIndex} is out of range for struct '{st.Name}'."));
          } else {
            ExpectTempType(fn, types, bb, idx, f.Dest, st.Fields[f.FieldIndex].Type, "field_get", errors);
          }
        }
        break;

      case FieldAddr f:
        if (TryGetPtrTarget(fn, types, bb, idx, f.TargetPtr, "field_addr", errors, out IrType? faStructType)) {
          if (faStructType is not IrStructType st) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"field_addr target must be ptr<struct>, got ptr<{faStructType!.Name}>."));
          } else if ((uint) f.FieldIndex >= (uint) st.Fields.Length) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"field_addr index {f.FieldIndex} is out of range for struct '{st.Name}'."));
          } else {
            ExpectPtrToType(fn, types, bb, idx, f.Dest, st.Fields[f.FieldIndex].Type, "field_addr", errors);
          }
        }
        break;

      case LoadPtr l:
        if (TryGetPtrTarget(fn, types, bb, idx, l.Ptr, "load_ptr", errors, out IrType? lpTarget)) {
          ExpectTempType(fn, types, bb, idx, l.Dest, lpTarget!.Id, "load_ptr", errors);
        }
        break;

      case StorePtr s:
        if (TryGetPtrTarget(fn, types, bb, idx, s.Ptr, "store_ptr", errors, out IrType? spTarget)) {
          ExpectOperandType(fn, types, bb, idx, s.Value, spTarget!.Id, "store_ptr value", errors);
        }
        break;

      case PtrEq p:
        if (TryGetPtrTarget(fn, types, bb, idx, p.LeftPtr, "ptr.eq", errors, out IrType? lp)
            && TryGetPtrTarget(fn, types, bb, idx, p.RightPtr, "ptr.eq", errors, out IrType? rp)) {
          if (lp!.Id != rp!.Id) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"ptr.eq operand types mismatch: ptr<{lp.Name}> vs ptr<{rp.Name}>."));
          }
        }
        ExpectTempType(fn, types, bb, idx, p.Dest, types.Bool, "ptr.eq", errors);
        break;

      case Add a:
        VerifyBinarySameType(fn, types, bb, idx, a.Dest, a.Left, a.Right, "add", allowRune: true, errors);
        break;

      case Sub s:
        VerifyBinarySameType(fn, types, bb, idx, s.Dest, s.Left, s.Right, "sub", allowRune: true, errors);
        break;

      case Mul m:
        VerifyBinarySameType(fn, types, bb, idx, m.Dest, m.Left, m.Right, "mul", allowRune: false, errors);
        break;

      case DivF64 d:
        ExpectOperandType(fn, types, bb, idx, d.Left, types.Float, "div.f64 left", errors);
        ExpectOperandType(fn, types, bb, idx, d.Right, types.Float, "div.f64 right", errors);
        ExpectTempType(fn, types, bb, idx, d.Dest, types.Float, "div.f64", errors);
        break;

      case FloorDiv d:
        VerifyBinarySameType(fn, types, bb, idx, d.Dest, d.Left, d.Right, "floordiv", allowRune: false, errors);
        break;

      case FloorMod m:
        VerifyBinarySameType(fn, types, bb, idx, m.Dest, m.Left, m.Right, "floormod", allowRune: false, errors);
        break;

      case Neg n:
        if (TryGetOperandIrType(fn, types, n.Operand, out IrType? negType)) {
          if (negType.Kind is not (IrTypeKind.Int or IrTypeKind.Float)) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"neg requires int or float, got '{negType.Name}'."));
          } else {
            ExpectTempType(fn, types, bb, idx, n.Dest, negType.Id, "neg", errors);
          }
        }
        break;

      case Not n:
        ExpectOperandType(fn, types, bb, idx, n.Operand, types.Bool, "not", errors);
        ExpectTempType(fn, types, bb, idx, n.Dest, types.Bool, "not", errors);
        break;

      case Cmp c:
        if (TryGetOperandIrType(fn, types, c.Left, out IrType? cLeft)
            && TryGetOperandIrType(fn, types, c.Right, out IrType? cRight)) {
          if (cLeft.Id != cRight.Id) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"cmp operand types mismatch: '{cLeft.Name}' vs '{cRight.Name}'."));
          } else if (c.Op is IrCmpOp.Eq or IrCmpOp.Ne) {
            if (cLeft.Kind is not (IrTypeKind.Bool or IrTypeKind.Int or IrTypeKind.Float or IrTypeKind.Rune or IrTypeKind.Enum or IrTypeKind.Handle)) {
              errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"cmp.{c.Op} does not support type '{cLeft.Name}'."));
            }
          } else if (cLeft.Kind is not (IrTypeKind.Int or IrTypeKind.Float or IrTypeKind.Rune)) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"cmp.{c.Op} requires ordered scalar, got '{cLeft.Name}'."));
          }
        }
        ExpectTempType(fn, types, bb, idx, c.Dest, types.Bool, "cmp", errors);
        break;

      case CastI64ToF64 c:
        ExpectOperandType(fn, types, bb, idx, c.Source, types.Int, "cast.i64_to_f64", errors);
        ExpectTempType(fn, types, bb, idx, c.Dest, types.Float, "cast.i64_to_f64", errors);
        break;

      case CastF64ToI64 c:
        ExpectOperandType(fn, types, bb, idx, c.Source, types.Float, "cast.f64_to_i64", errors);
        ExpectTempType(fn, types, bb, idx, c.Dest, types.Int, "cast.f64_to_i64", errors);
        break;

      case CastI64ToRune c:
        ExpectOperandType(fn, types, bb, idx, c.Source, types.Int, "cast.i64_to_rune", errors);
        ExpectTempType(fn, types, bb, idx, c.Dest, types.Rune, "cast.i64_to_rune", errors);
        break;

      case CastRuneToI64 c:
        ExpectOperandType(fn, types, bb, idx, c.Source, types.Rune, "cast.rune_to_i64", errors);
        ExpectTempType(fn, types, bb, idx, c.Dest, types.Int, "cast.rune_to_i64", errors);
        break;

      case StructEq s:
        if (TryGetOperandIrType(fn, types, s.Left, out IrType? seLeft)
            && TryGetOperandIrType(fn, types, s.Right, out IrType? seRight)
            && seLeft.Id != seRight.Id) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"struct.eq operand types mismatch: '{seLeft.Name}' vs '{seRight.Name}'."));
        }
        ExpectTempType(fn, types, bb, idx, s.Dest, types.Bool, "struct.eq", errors);
        break;

      case StructNew s:
        if (TryGetTempIrType(fn, types, s.Dest, out IrType? snDest)) {
          if (snDest is not IrStructType st) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"struct.new dest must be a struct, got '{snDest.Name}'."));
          } else if (s.Fields.Length != st.Fields.Length) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"struct.new for '{st.Name}' expected {st.Fields.Length} fields, got {s.Fields.Length}."));
          } else {
            for (int f = 0; f < st.Fields.Length; f++) {
              ExpectOperandType(fn, types, bb, idx, s.Fields[f], st.Fields[f].Type, $"struct.new field '{st.Fields[f].Name}'", errors);
            }
          }
        }
        break;

      case TupleNew t:
        if (TryGetTempIrType(fn, types, t.Dest, out IrType? tnDest)) {
          if (tnDest is not IrTupleType tt) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"tuple.new dest must be a tuple, got '{tnDest.Name}'."));
          } else if (t.Fields.Length != tt.Fields.Length) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"tuple.new expected {tt.Fields.Length} fields, got {t.Fields.Length}."));
          } else {
            for (int f = 0; f < tt.Fields.Length; f++) {
              ExpectOperandType(fn, types, bb, idx, t.Fields[f], tt.Fields[f].Type, $"tuple.new field '{tt.Fields[f].Label}'", errors);
            }
          }
        }
        break;

      case TupleField t:
        if (TryGetOperandIrType(fn, types, t.Tuple, out IrType? tfSrc)) {
          if (tfSrc is not IrTupleType tt) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"tuple.field target must be a tuple, got '{tfSrc.Name}'."));
          } else if ((uint) t.FieldIndex >= (uint) tt.Fields.Length) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"tuple.field index {t.FieldIndex} out of range for '{tt.Name}'."));
          } else {
            ExpectTempType(fn, types, bb, idx, t.Dest, tt.Fields[t.FieldIndex].Type, "tuple.field", errors);
          }
        }
        break;

      case UnionNew u:
        if (TryGetTempIrType(fn, types, u.Dest, out IrType? unDest)) {
          if (unDest is not IrUnionType ut) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"union.new dest must be a union, got '{unDest.Name}'."));
          } else if ((uint) u.VariantIndex >= (uint) ut.Variants.Length) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"union.new variant {u.VariantIndex} out of range for '{ut.Name}'."));
          } else {
            IrUnionVariant variant = ut.Variants[u.VariantIndex];
            if (u.Fields.Length != variant.Fields.Length) {
              errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"union.new {ut.Name}::{variant.Name} expected {variant.Fields.Length} payloads, got {u.Fields.Length}."));
            } else {
              for (int f = 0; f < variant.Fields.Length; f++) {
                ExpectOperandType(fn, types, bb, idx, u.Fields[f], variant.Fields[f].Type, $"union.new payload {f}", errors);
              }
            }
          }
        }
        break;

      case UnionTag u:
        if (TryGetOperandIrType(fn, types, u.Union, out IrType? utSrc) && utSrc is not IrUnionType) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"union.tag target must be a union, got '{utSrc.Name}'."));
        }
        ExpectTempType(fn, types, bb, idx, u.Dest, types.Int, "union.tag", errors);
        break;

      case UnionField u:
        if (TryGetOperandIrType(fn, types, u.Union, out IrType? ufSrc)) {
          if (ufSrc is not IrUnionType ut) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"union.field target must be a union, got '{ufSrc.Name}'."));
          } else if ((uint) u.VariantIndex >= (uint) ut.Variants.Length) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"union.field variant {u.VariantIndex} out of range for '{ut.Name}'."));
          } else if ((uint) u.FieldIndex >= (uint) ut.Variants[u.VariantIndex].Fields.Length) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"union.field payload index {u.FieldIndex} out of range."));
          } else {
            ExpectTempType(fn, types, bb, idx, u.Dest, ut.Variants[u.VariantIndex].Fields[u.FieldIndex].Type, "union.field", errors);
          }
        }
        break;

      case IfaceBox b:
        if ((uint) b.VTable.Index >= (uint) module.VTables.Count) {
          errors.Add(new IrVerificationError("IR-SSA-01", fnName, bb, idx, $"iface.box references out-of-range vtable {b.VTable}."));
        } else {
          IrVTable vt = module.VTables[b.VTable.Index];
          ExpectOperandType(fn, types, bb, idx, b.Value, vt.ConcreteStructType, "iface.box value", errors);
          ExpectTempType(fn, types, bb, idx, b.Dest, vt.InterfaceType, "iface.box", errors);
        }
        break;

      case IfaceFieldGet g:
        if (TryGetOperandIrType(fn, types, g.Iface, out IrType? ifgType)) {
          if (ifgType is not IrInterfaceType it) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"iface.field_get target must be an interface, got '{ifgType.Name}'."));
          } else if ((uint) g.PropertyIndex >= (uint) it.Properties.Length) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"iface.field_get property {g.PropertyIndex} out of range for '{it.Name}'."));
          } else {
            ExpectTempType(fn, types, bb, idx, g.Dest, it.Properties[g.PropertyIndex].Type, "iface.field_get", errors);
          }
        }
        break;

      case IfaceFieldAddr a:
        if (TryGetPtrTarget(fn, types, bb, idx, a.IfacePtr, "iface.field_addr", errors, out IrType? ifaType)) {
          if (ifaType is not IrInterfaceType it) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"iface.field_addr target must be ptr<interface>, got ptr<{ifaType!.Name}>."));
          } else if ((uint) a.PropertyIndex >= (uint) it.Properties.Length) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"iface.field_addr property {a.PropertyIndex} out of range for '{it.Name}'."));
          } else {
            ExpectPtrToType(fn, types, bb, idx, a.Dest, it.Properties[a.PropertyIndex].Type, "iface.field_addr", errors);
          }
        }
        break;

      case CellNew c:
        if (TryGetTempIrType(fn, types, c.Dest, out IrType? cnDest)) {
          if (cnDest is not IrCellType ct) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"cell.new dest must be cell<T>, got '{cnDest.Name}'."));
          } else {
            ExpectOperandType(fn, types, bb, idx, c.InitialValue, ct.ElementType, "cell.new", errors);
          }
        }
        break;

      case CellGet c:
        if (TryGetOperandIrType(fn, types, c.Cell, out IrType? cgSrc)) {
          if (cgSrc is not IrCellType ct) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"cell.get requires cell<T>, got '{cgSrc.Name}'."));
          } else {
            ExpectTempType(fn, types, bb, idx, c.Dest, ct.ElementType, "cell.get", errors);
          }
        }
        break;

      case CellSet c:
        if (TryGetOperandIrType(fn, types, c.Cell, out IrType? csSrc)) {
          if (csSrc is not IrCellType ct) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"cell.set requires cell<T>, got '{csSrc.Name}'."));
          } else {
            ExpectOperandType(fn, types, bb, idx, c.Value, ct.ElementType, "cell.set", errors);
          }
        }
        break;

      case CellAddr c:
        if (TryGetOperandIrType(fn, types, c.Cell, out IrType? caSrc)) {
          if (caSrc is not IrCellType ct) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"cell.addr requires cell<T>, got '{caSrc.Name}'."));
          } else {
            ExpectPtrToType(fn, types, bb, idx, c.Dest, ct.ElementType, "cell.addr", errors);
          }
        }
        break;

      case EnvNew e:
        if (TryGetTempIrType(fn, types, e.Dest, out IrType? enDest)) {
          if (enDest is not IrEnvType envNewType) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"env.new dest must be env<...>, got '{enDest.Name}'."));
          } else if (e.Captures.Length != envNewType.Fields.Length) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"env.new expected {envNewType.Fields.Length} captures, got {e.Captures.Length}."));
          } else {
            for (int f = 0; f < envNewType.Fields.Length; f++) {
              ExpectOperandType(fn, types, bb, idx, e.Captures[f], envNewType.Fields[f].Type, $"env.new capture '{envNewType.Fields[f].Name}'", errors);
            }
          }
        }
        break;

      case EnvFieldGet e:
        if (TryGetOperandIrType(fn, types, e.Env, out IrType? efSrc)) {
          if (efSrc is not IrEnvType envGetType) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"env.field_get requires env<...>, got '{efSrc.Name}'."));
          } else if ((uint) e.FieldIndex >= (uint) envGetType.Fields.Length) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"env.field_get index {e.FieldIndex} out of range."));
          } else {
            ExpectTempType(fn, types, bb, idx, e.Dest, envGetType.Fields[e.FieldIndex].Type, "env.field_get", errors);
          }
        }
        break;

      case ClosureMake c:
        if ((uint) c.Function.Index >= (uint) module.Functions.Count) {
          errors.Add(new IrVerificationError("IR-SSA-01", fnName, bb, idx, $"closure.make references out-of-range function {c.Function}."));
        }

        if (c.Env is { } envOp && TryGetOperandIrType(fn, types, envOp, out IrType? envOpType) && envOpType is not IrEnvType) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"closure.make env must be env<...>, got '{envOpType.Name}'."));
        }

        if (TryGetTempIrType(fn, types, c.Dest, out IrType? cmDest) && cmDest is not IrClosureType) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"closure.make dest must be a closure type, got '{cmDest.Name}'."));
        }
        break;

      case VecNew v:
        if (TryGetTempIrType(fn, types, v.Dest, out IrType? vnDest)) {
          if (vnDest is not IrVectorType vt) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"vec.new dest must be []T, got '{vnDest.Name}'."));
          } else {
            foreach (IrOperand elem in v.Elements) {
              ExpectOperandType(fn, types, bb, idx, elem, vt.ElementType, "vec.new element", errors);
            }
          }
        }
        break;

      case VecWithCap v:
        if (TryGetTempIrType(fn, types, v.Dest, out IrType? vwcDest) && vwcDest is not IrVectorType) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"vec.with_cap dest must be []T, got '{vwcDest.Name}'."));
        }
        ExpectOperandType(fn, types, bb, idx, v.Capacity, types.Int, "vec.with_cap capacity", errors);
        break;

      case VecLen v:
        if (TryGetVecType(fn, types, bb, idx, v.Vec, "vec.len", errors, out _)) {
          ExpectTempType(fn, types, bb, idx, v.Dest, types.Int, "vec.len", errors);
        }
        break;

      case VecCap v:
        if (TryGetVecType(fn, types, bb, idx, v.Vec, "vec.cap", errors, out _)) {
          ExpectTempType(fn, types, bb, idx, v.Dest, types.Int, "vec.cap", errors);
        }
        break;

      case VecGet v:
        if (TryGetVecType(fn, types, bb, idx, v.Vec, "vec.get", errors, out IrVectorType? vgVec)) {
          ExpectOperandType(fn, types, bb, idx, v.Index, types.Int, "vec.get index", errors);
          ExpectTempType(fn, types, bb, idx, v.Dest, vgVec!.ElementType, "vec.get", errors);
        }
        break;

      case VecElemAddr v:
        if (TryGetPtrTarget(fn, types, bb, idx, v.VecPtr, "vec.elem_addr", errors, out IrType? veaTarget)) {
          if (veaTarget is not IrVectorType vt) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"vec.elem_addr target must be ptr<[]T>, got ptr<{veaTarget!.Name}>."));
          } else {
            ExpectOperandType(fn, types, bb, idx, v.Index, types.Int, "vec.elem_addr index", errors);
            ExpectPtrToType(fn, types, bb, idx, v.Dest, vt.ElementType, "vec.elem_addr", errors);
          }
        }
        break;

      case VecPush v:
        if (TryGetPtrTarget(fn, types, bb, idx, v.VecPtr, "vec.push", errors, out IrType? vpTarget)) {
          if (vpTarget is not IrVectorType vt) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"vec.push target must be ptr<[]T>, got ptr<{vpTarget!.Name}>."));
          } else {
            ExpectOperandType(fn, types, bb, idx, v.Value, vt.ElementType, "vec.push value", errors);
          }
        }
        break;

      case VecPop v:
        if (TryGetPtrTarget(fn, types, bb, idx, v.VecPtr, "vec.pop", errors, out IrType? vpopTarget)) {
          if (vpopTarget is not IrVectorType vt) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"vec.pop target must be ptr<[]T>, got ptr<{vpopTarget!.Name}>."));
          } else {
            ExpectOptionOf(fn, types, bb, idx, v.Dest, vt.ElementType, "vec.pop", errors);
          }
        }
        break;

      case VecSwap v:
        if (TryGetPtrTarget(fn, types, bb, idx, v.VecPtr, "vec.swap", errors, out IrType? vsTarget) && vsTarget is not IrVectorType) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"vec.swap target must be ptr<[]T>, got ptr<{vsTarget!.Name}>."));
        }
        ExpectOperandType(fn, types, bb, idx, v.IndexA, types.Int, "vec.swap indexA", errors);
        ExpectOperandType(fn, types, bb, idx, v.IndexB, types.Int, "vec.swap indexB", errors);
        break;

      case VecContains v:
        if (TryGetVecType(fn, types, bb, idx, v.Vec, "vec.contains", errors, out IrVectorType? vcVec)) {
          ExpectOperandType(fn, types, bb, idx, v.Value, vcVec!.ElementType, "vec.contains value", errors);
          ExpectTempType(fn, types, bb, idx, v.Dest, types.Bool, "vec.contains", errors);
        }
        break;

      case MapNew m:
        if (TryGetTempIrType(fn, types, m.Dest, out IrType? mnDest)) {
          if (mnDest is not IrMapType mt) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"map.new dest must be map[K, V], got '{mnDest.Name}'."));
          } else {
            foreach ((IrOperand k, IrOperand v) in m.Entries) {
              ExpectOperandType(fn, types, bb, idx, k, mt.KeyType, "map.new key", errors);
              ExpectOperandType(fn, types, bb, idx, v, mt.ValueType, "map.new value", errors);
            }
          }
        }
        break;

      case MapLen m:
        if (TryGetMapType(fn, types, bb, idx, m.Map, "map.len", errors, out _)) {
          ExpectTempType(fn, types, bb, idx, m.Dest, types.Int, "map.len", errors);
        }
        break;

      case MapGet m:
        if (TryGetMapType(fn, types, bb, idx, m.Map, "map.get", errors, out IrMapType? mgMap)) {
          ExpectOperandType(fn, types, bb, idx, m.Key, mgMap!.KeyType, "map.get key", errors);
          ExpectTempType(fn, types, bb, idx, m.Dest, mgMap.ValueType, "map.get", errors);
        }
        break;

      case MapElemAddr m:
        if (TryGetPtrTarget(fn, types, bb, idx, m.MapPtr, "map.elem_addr", errors, out IrType? meaTarget)) {
          if (meaTarget is not IrMapType mt) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"map.elem_addr target must be ptr<map[K, V]>, got ptr<{meaTarget!.Name}>."));
          } else {
            ExpectOperandType(fn, types, bb, idx, m.Key, mt.KeyType, "map.elem_addr key", errors);
            ExpectPtrToType(fn, types, bb, idx, m.Dest, mt.ValueType, "map.elem_addr", errors);
          }
        }
        break;

      case MapSet m:
        if (TryGetPtrTarget(fn, types, bb, idx, m.MapPtr, "map.set", errors, out IrType? msTarget)) {
          if (msTarget is not IrMapType mt) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"map.set target must be ptr<map[K, V]>, got ptr<{msTarget!.Name}>."));
          } else {
            ExpectOperandType(fn, types, bb, idx, m.Key, mt.KeyType, "map.set key", errors);
            ExpectOperandType(fn, types, bb, idx, m.Value, mt.ValueType, "map.set value", errors);
          }
        }
        break;

      case MapRemove m:
        if (TryGetPtrTarget(fn, types, bb, idx, m.MapPtr, "map.remove", errors, out IrType? mrTarget)) {
          if (mrTarget is not IrMapType mt) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"map.remove target must be ptr<map[K, V]>, got ptr<{mrTarget!.Name}>."));
          } else {
            ExpectOperandType(fn, types, bb, idx, m.Key, mt.KeyType, "map.remove key", errors);
            ExpectOptionOf(fn, types, bb, idx, m.Dest, mt.ValueType, "map.remove", errors);
          }
        }
        break;

      case MapContains m:
        if (TryGetMapType(fn, types, bb, idx, m.Map, "map.contains", errors, out IrMapType? mcMap)) {
          ExpectOperandType(fn, types, bb, idx, m.Key, mcMap!.KeyType, "map.contains key", errors);
          ExpectTempType(fn, types, bb, idx, m.Dest, types.Bool, "map.contains", errors);
        }
        break;

      case MapKeys m:
        if (TryGetMapType(fn, types, bb, idx, m.Map, "map.keys", errors, out IrMapType? mkMap)) {
          ExpectVectorOf(fn, types, bb, idx, m.Dest, mkMap!.KeyType, "map.keys", errors);
        }
        break;

      case MapValues m:
        if (TryGetMapType(fn, types, bb, idx, m.Map, "map.values", errors, out IrMapType? mvMap)) {
          ExpectVectorOf(fn, types, bb, idx, m.Dest, mvMap!.ValueType, "map.values", errors);
        }
        break;

      case MapSlotLen m:
        if (TryGetMapType(fn, types, bb, idx, m.Map, "map.slot_len", errors, out _)) {
          ExpectTempType(fn, types, bb, idx, m.Dest, types.Int, "map.slot_len", errors);
        }
        break;

      case MapSlotIsLive m:
        if (TryGetMapType(fn, types, bb, idx, m.Map, "map.slot_is_live", errors, out _)) {
          ExpectOperandType(fn, types, bb, idx, m.SlotIndex, types.Int, "map.slot_is_live index", errors);
          ExpectTempType(fn, types, bb, idx, m.Dest, types.Bool, "map.slot_is_live", errors);
        }
        break;

      case MapSlotKey m:
        if (TryGetMapType(fn, types, bb, idx, m.Map, "map.slot_key", errors, out IrMapType? mskMap)) {
          ExpectOperandType(fn, types, bb, idx, m.SlotIndex, types.Int, "map.slot_key index", errors);
          ExpectTempType(fn, types, bb, idx, m.Dest, mskMap!.KeyType, "map.slot_key", errors);
        }
        break;

      case MapSlotValue m:
        if (TryGetMapType(fn, types, bb, idx, m.Map, "map.slot_value", errors, out IrMapType? msvMap)) {
          ExpectOperandType(fn, types, bb, idx, m.SlotIndex, types.Int, "map.slot_value index", errors);
          ExpectTempType(fn, types, bb, idx, m.Dest, msvMap!.ValueType, "map.slot_value", errors);
        }
        break;

      case SetNew s:
        if (TryGetTempIrType(fn, types, s.Dest, out IrType? setnDest)) {
          if (setnDest is not IrSetType st) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"set.new dest must be set[T], got '{setnDest.Name}'."));
          } else {
            foreach (IrOperand elem in s.Elements) {
              ExpectOperandType(fn, types, bb, idx, elem, st.ElementType, "set.new element", errors);
            }
          }
        }
        break;

      case SetLen s:
        if (TryGetSetType(fn, types, bb, idx, s.Set, "set.len", errors, out _)) {
          ExpectTempType(fn, types, bb, idx, s.Dest, types.Int, "set.len", errors);
        }
        break;

      case SetAdd s:
        if (TryGetPtrTarget(fn, types, bb, idx, s.SetPtr, "set.add", errors, out IrType? saTarget)) {
          if (saTarget is not IrSetType st) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"set.add target must be ptr<set[T]>, got ptr<{saTarget!.Name}>."));
          } else {
            ExpectOperandType(fn, types, bb, idx, s.Value, st.ElementType, "set.add value", errors);
          }
        }
        break;

      case SetRemove s:
        if (TryGetPtrTarget(fn, types, bb, idx, s.SetPtr, "set.remove", errors, out IrType? srTarget)) {
          if (srTarget is not IrSetType st) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"set.remove target must be ptr<set[T]>, got ptr<{srTarget!.Name}>."));
          } else {
            ExpectOperandType(fn, types, bb, idx, s.Value, st.ElementType, "set.remove value", errors);
          }
        }
        break;

      case SetContains s:
        if (TryGetSetType(fn, types, bb, idx, s.Set, "set.contains", errors, out IrSetType? scSet)) {
          ExpectOperandType(fn, types, bb, idx, s.Value, scSet!.ElementType, "set.contains value", errors);
          ExpectTempType(fn, types, bb, idx, s.Dest, types.Bool, "set.contains", errors);
        }
        break;

      case SetSlotLen s:
        if (TryGetSetType(fn, types, bb, idx, s.Set, "set.slot_len", errors, out _)) {
          ExpectTempType(fn, types, bb, idx, s.Dest, types.Int, "set.slot_len", errors);
        }
        break;

      case SetSlotIsLive s:
        if (TryGetSetType(fn, types, bb, idx, s.Set, "set.slot_is_live", errors, out _)) {
          ExpectOperandType(fn, types, bb, idx, s.SlotIndex, types.Int, "set.slot_is_live index", errors);
          ExpectTempType(fn, types, bb, idx, s.Dest, types.Bool, "set.slot_is_live", errors);
        }
        break;

      case SetSlotElem s:
        if (TryGetSetType(fn, types, bb, idx, s.Set, "set.slot_elem", errors, out IrSetType? sseSet)) {
          ExpectOperandType(fn, types, bb, idx, s.SlotIndex, types.Int, "set.slot_elem index", errors);
          ExpectTempType(fn, types, bb, idx, s.Dest, sseSet!.ElementType, "set.slot_elem", errors);
        }
        break;

      case PoolNew p:
        if (TryGetTempIrType(fn, types, p.Dest, out IrType? pnDest) && pnDest is not IrPoolType) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"pool.new dest must be Pool[T], got '{pnDest.Name}'."));
        }
        break;

      case PoolLen p:
        if (TryGetPoolType(fn, types, bb, idx, p.Pool, "pool.len", errors, out _)) {
          ExpectTempType(fn, types, bb, idx, p.Dest, types.Int, "pool.len", errors);
        }
        break;

      case PoolAdd p:
        if (TryGetPtrTarget(fn, types, bb, idx, p.PoolPtr, "pool.add", errors, out IrType? paTarget)) {
          if (paTarget is not IrPoolType pt) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"pool.add target must be ptr<Pool[T]>, got ptr<{paTarget!.Name}>."));
          } else {
            ExpectOperandType(fn, types, bb, idx, p.Value, pt.ElementType, "pool.add value", errors);
            ExpectHandleOf(fn, types, bb, idx, p.Dest, pt.ElementType, "pool.add", errors);
          }
        }
        break;

      case PoolRemove p:
        if (TryGetPtrTarget(fn, types, bb, idx, p.PoolPtr, "pool.remove", errors, out IrType? prTarget)) {
          if (prTarget is not IrPoolType pt) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"pool.remove target must be ptr<Pool[T]>, got ptr<{prTarget!.Name}>."));
          } else {
            ExpectHandleOperandOf(fn, types, bb, idx, p.Handle, pt.ElementType, "pool.remove", errors);
            ExpectOptionOf(fn, types, bb, idx, p.Dest, pt.ElementType, "pool.remove", errors);
          }
        }
        break;

      case PoolGet p:
        if (TryGetPoolType(fn, types, bb, idx, p.Pool, "pool.get", errors, out IrPoolType? pgPool)) {
          ExpectHandleOperandOf(fn, types, bb, idx, p.Handle, pgPool!.ElementType, "pool.get", errors);
          ExpectOptionOf(fn, types, bb, idx, p.Dest, pgPool.ElementType, "pool.get", errors);
        }
        break;

      case PoolGetOrPanic p:
        if (TryGetPoolType(fn, types, bb, idx, p.Pool, "pool.get_or_panic", errors, out IrPoolType? pgpPool)) {
          ExpectHandleOperandOf(fn, types, bb, idx, p.Handle, pgpPool!.ElementType, "pool.get_or_panic", errors);
          ExpectTempType(fn, types, bb, idx, p.Dest, pgpPool.ElementType, "pool.get_or_panic", errors);
        }
        break;

      case PoolElemAddr p:
        if (TryGetPtrTarget(fn, types, bb, idx, p.PoolPtr, "pool.elem_addr", errors, out IrType? peaTarget)) {
          if (peaTarget is not IrPoolType pt) {
            errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"pool.elem_addr target must be ptr<Pool[T]>, got ptr<{peaTarget!.Name}>."));
          } else {
            ExpectHandleOperandOf(fn, types, bb, idx, p.Handle, pt.ElementType, "pool.elem_addr", errors);
            ExpectPtrToType(fn, types, bb, idx, p.Dest, pt.ElementType, "pool.elem_addr", errors);
          }
        }
        break;

      case PoolHandles p:
        if (TryGetPoolType(fn, types, bb, idx, p.Pool, "pool.handles", errors, out IrPoolType? phPool)) {
          if (TryGetTempIrType(fn, types, p.Dest, out IrType? phDest)) {
            if (phDest is not IrVectorType vt || !types.TryGetType(vt.ElementType, out IrType? elemType)
                || elemType is not IrHandleType ht || ht.ElementType != phPool!.ElementType) {
              errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"pool.handles dest must be []Handle[{FormatTypeName(types, phPool!.ElementType)}], got '{phDest.Name}'."));
            }
          }
        }
        break;

      case PoolClear p:
        if (TryGetPtrTarget(fn, types, bb, idx, p.PoolPtr, "pool.clear", errors, out IrType? pcTarget) && pcTarget is not IrPoolType) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"pool.clear target must be ptr<Pool[T]>, got ptr<{pcTarget!.Name}>."));
        }
        break;

      case PoolContains p:
        if (TryGetPoolType(fn, types, bb, idx, p.Pool, "pool.contains", errors, out IrPoolType? pconPool)) {
          ExpectHandleOperandOf(fn, types, bb, idx, p.Handle, pconPool!.ElementType, "pool.contains", errors);
          ExpectTempType(fn, types, bb, idx, p.Dest, types.Bool, "pool.contains", errors);
        }
        break;

      case PoolSlotLen p:
        if (TryGetPoolType(fn, types, bb, idx, p.Pool, "pool.slot_len", errors, out _)) {
          ExpectTempType(fn, types, bb, idx, p.Dest, types.Int, "pool.slot_len", errors);
        }
        break;

      case PoolSlotIsLive p:
        if (TryGetPoolType(fn, types, bb, idx, p.Pool, "pool.slot_is_live", errors, out _)) {
          ExpectOperandType(fn, types, bb, idx, p.SlotIndex, types.Int, "pool.slot_is_live index", errors);
          ExpectTempType(fn, types, bb, idx, p.Dest, types.Bool, "pool.slot_is_live", errors);
        }
        break;

      case PoolSlotHandle p:
        if (TryGetPoolType(fn, types, bb, idx, p.Pool, "pool.slot_handle", errors, out IrPoolType? pshPool)) {
          ExpectOperandType(fn, types, bb, idx, p.SlotIndex, types.Int, "pool.slot_handle index", errors);
          ExpectHandleOf(fn, types, bb, idx, p.Dest, pshPool!.ElementType, "pool.slot_handle", errors);
        }
        break;

      case PoolSlotValue p:
        if (TryGetPoolType(fn, types, bb, idx, p.Pool, "pool.slot_value", errors, out IrPoolType? psvPool)) {
          ExpectOperandType(fn, types, bb, idx, p.SlotIndex, types.Int, "pool.slot_value index", errors);
          ExpectTempType(fn, types, bb, idx, p.Dest, psvPool!.ElementType, "pool.slot_value", errors);
        }
        break;

      case StrLen s:
        ExpectOperandType(fn, types, bb, idx, s.Str, types.String, "str.len", errors);
        ExpectTempType(fn, types, bb, idx, s.Dest, types.Int, "str.len", errors);
        break;

      case StrGetRune s:
        ExpectOperandType(fn, types, bb, idx, s.Str, types.String, "str.get_rune str", errors);
        ExpectOperandType(fn, types, bb, idx, s.Index, types.Int, "str.get_rune index", errors);
        ExpectTempType(fn, types, bb, idx, s.Dest, types.Rune, "str.get_rune", errors);
        break;

      case StrByteLen s:
        ExpectOperandType(fn, types, bb, idx, s.Str, types.String, "str.byte_len", errors);
        ExpectTempType(fn, types, bb, idx, s.Dest, types.Int, "str.byte_len", errors);
        break;

      case StrDecodeRune s:
        ExpectOperandType(fn, types, bb, idx, s.Str, types.String, "str.decode_rune str", errors);
        ExpectOperandType(fn, types, bb, idx, s.BytePos, types.Int, "str.decode_rune byte_pos", errors);
        ExpectTempType(fn, types, bb, idx, s.Dest, types.Rune, "str.decode_rune", errors);
        break;

      case StrNextBytePos s:
        ExpectOperandType(fn, types, bb, idx, s.Str, types.String, "str.next_byte_pos str", errors);
        ExpectOperandType(fn, types, bb, idx, s.BytePos, types.Int, "str.next_byte_pos byte_pos", errors);
        ExpectTempType(fn, types, bb, idx, s.Dest, types.Int, "str.next_byte_pos", errors);
        break;

      case StrEq s:
        ExpectOperandType(fn, types, bb, idx, s.Left, types.String, "str.eq left", errors);
        ExpectOperandType(fn, types, bb, idx, s.Right, types.String, "str.eq right", errors);
        ExpectTempType(fn, types, bb, idx, s.Dest, types.Bool, "str.eq", errors);
        break;

      case StrCmp s:
        ExpectOperandType(fn, types, bb, idx, s.Left, types.String, "str.cmp left", errors);
        ExpectOperandType(fn, types, bb, idx, s.Right, types.String, "str.cmp right", errors);
        ExpectTempType(fn, types, bb, idx, s.Dest, types.Int, "str.cmp", errors);
        break;

      case StrConcat s:
        ExpectOperandType(fn, types, bb, idx, s.Left, types.String, "str.concat left", errors);
        ExpectOperandType(fn, types, bb, idx, s.Right, types.String, "str.concat right", errors);
        ExpectTempType(fn, types, bb, idx, s.Dest, types.String, "str.concat", errors);
        break;

      case StrConcatN s:
        foreach (IrOperand part in s.Parts) {
          ExpectOperandType(fn, types, bb, idx, part, types.String, "str.concat_n part", errors);
        }
        ExpectTempType(fn, types, bb, idx, s.Dest, types.String, "str.concat_n", errors);
        break;

      case StrConcatRune s:
        ExpectOperandType(fn, types, bb, idx, s.Str, types.String, "str.concat_rune str", errors);
        ExpectOperandType(fn, types, bb, idx, s.RuneValue, types.Rune, "str.concat_rune rune", errors);
        ExpectTempType(fn, types, bb, idx, s.Dest, types.String, "str.concat_rune", errors);
        break;

      case RuneConcatStr s:
        ExpectOperandType(fn, types, bb, idx, s.RuneValue, types.Rune, "rune.concat_str rune", errors);
        ExpectOperandType(fn, types, bb, idx, s.Str, types.String, "rune.concat_str str", errors);
        ExpectTempType(fn, types, bb, idx, s.Dest, types.String, "rune.concat_str", errors);
        break;

      case CallDirect c:
        VerifyCallDirect(module, fn, bb, idx, c, errors);
        break;

      case CallClosure c:
        VerifyCallClosure(fn, types, bb, idx, c, errors);
        break;

      case CallIface c:
        VerifyCallIface(fn, types, bb, idx, c, errors);
        break;

      case CallRuntime c:
        VerifyCallRuntime(fn, types, bb, idx, c, errors);
        break;

      case RcInc r:
        if (TryGetOperandIrType(fn, types, r.Value, out IrType? incType) && !incType.IsRcManaged) {
          errors.Add(new IrVerificationError("IR-RC-01", fnName, bb, idx, $"rc_inc cannot be emitted on pointer-free or non-RC type '{incType.Name}'."));
        }
        break;

      case RcDec r:
        if (TryGetOperandIrType(fn, types, r.Value, out IrType? decType) && !decType.IsRcManaged) {
          errors.Add(new IrVerificationError("IR-RC-01", fnName, bb, idx, $"rc_dec cannot be emitted on pointer-free or non-RC type '{decType.Name}'."));
        }
        break;

      case EnsureUnique e:
        if (TryGetPtrTarget(fn, types, bb, idx, e.PlacePtr, "ensure_unique", errors, out IrType? euTarget)
            && !euTarget!.IsCoWContainer) {
          errors.Add(new IrVerificationError("IR-RC-02", fnName, bb, idx, $"ensure_unique requires ptr<CoW container>, got ptr<{euTarget.Name}>."));
        }
        break;

      case Reset r:
        if (TryGetOperandIrType(fn, types, r.Value, out IrType? rstType)) {
          if (rstType is not IrUnionType { IsRecursiveUnion: true }) {
            errors.Add(new IrVerificationError("IR-RC-02", fnName, bb, idx, $"reset requires a recursive union value, got '{rstType.Name}'."));
          } else {
            ExpectPtrToType(fn, types, bb, idx, r.Dest, rstType.Id, "reset", errors);
          }
        }
        break;

      case Reuse r:
        if (TryGetPtrTarget(fn, types, bb, idx, r.Token, "reuse", errors, out IrType? tokTarget)) {
          if (tokTarget is not IrUnionType { IsRecursiveUnion: true } ut) {
            errors.Add(new IrVerificationError("IR-RC-02", fnName, bb, idx, $"reuse token must be ptr<recursive union>, got ptr<{tokTarget!.Name}>."));
          } else {
            ExpectTempType(fn, types, bb, idx, r.Dest, ut.Id, "reuse", errors);
            if ((uint) r.VariantIndex >= (uint) ut.Variants.Length) {
              errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"reuse variant {r.VariantIndex} out of range for '{ut.Name}'."));
            } else {
              IrUnionVariant variant = ut.Variants[r.VariantIndex];
              if (r.Fields.Length != variant.Fields.Length) {
                errors.Add(new IrVerificationError("IR-TYPE-03", fnName, bb, idx, $"reuse {ut.Name}::{variant.Name} expected {variant.Fields.Length} payloads, got {r.Fields.Length}."));
              } else {
                for (int f = 0; f < variant.Fields.Length; f++) {
                  ExpectOperandType(fn, types, bb, idx, r.Fields[f], variant.Fields[f].Type, $"reuse payload {f}", errors);
                }
              }
            }
          }
        }
        break;

      case ArenaEnter a:
        if (a.ArenaId < 0) {
          errors.Add(new IrVerificationError("IR-REG-01", fnName, bb, idx, $"arena_enter id must be non-negative, got {a.ArenaId}."));
        }
        break;

      case ArenaExit a:
        if (a.ArenaId < 0) {
          errors.Add(new IrVerificationError("IR-REG-01", fnName, bb, idx, $"arena_exit id must be non-negative, got {a.ArenaId}."));
        }
        break;
    }

    if (instr.AllocationRegion is { Kind: AllocRegionKind.Arena, ArenaId: < 0 } reg) {
      errors.Add(new IrVerificationError("IR-REG-01", fnName, bb, idx, $"AllocRegion has invalid negative arena id {reg.ArenaId}."));
    }
  }

  private static void VerifyCallDirect(
      IrModule module,
      IrFunction fn,
      IrBlockId bb,
      int idx,
      CallDirect call,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if ((uint) call.Callee.Index >= (uint) module.Functions.Count) {
      errors.Add(new IrVerificationError("IR-SSA-01", fn.Name, bb, idx, $"call references out-of-range function {call.Callee}."));
      return;
    }

    IrFunction callee = module.Functions[call.Callee.Index];
    IrTypeTable types = module.Types;
    VerifyCallSignature(fn, types, bb, idx, $"call {callee.Name}", call.Dest, call.Arguments, callee.Parameters, callee.ReturnType, errors);
  }

  private static void VerifyCallClosure(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      CallClosure call,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (!TryGetOperandIrType(fn, types, call.Closure, out IrType? closType)) {
      return;
    }

    if (closType is not IrClosureType ct) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"call_closure target must be a closure, got '{closType.Name}'."));
      return;
    }

    if (call.Arguments.Length != ct.Parameters.Length) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"call_closure expected {ct.Parameters.Length} arguments, got {call.Arguments.Length}."));
    } else {
      for (int i = 0; i < ct.Parameters.Length; i++) {
        VerifyCallArg(fn, types, bb, idx, "call_closure", i, call.Arguments[i], ct.Parameters[i].Type, ct.Parameters[i].Ownership, errors);
      }
    }

    VerifyCallReturn(fn, types, bb, idx, "call_closure", call.Dest, ct.ReturnType, errors);
  }

  private static void VerifyCallIface(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      CallIface call,
      ImmutableArray<IrVerificationError>.Builder errors) {
    IrInterfaceType? iface = null;
    if (call.IsMutating) {
      if (TryGetPtrTarget(fn, types, bb, idx, call.Receiver, "call_iface", errors, out IrType? target)) {
        if (target is IrInterfaceType it) {
          iface = it;
        } else {
          errors.Add(new IrVerificationError("IR-MUT-01", fn.Name, bb, idx, $"Mutating call_iface receiver must be ptr<interface>, got ptr<{target!.Name}>."));
        }
      }
    } else if (TryGetOperandIrType(fn, types, call.Receiver, out IrType? recType)) {
      if (recType is IrInterfaceType it) {
        iface = it;
      } else {
        errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"call_iface receiver must be an interface, got '{recType.Name}'."));
      }
    }

    if (iface is null) {
      return;
    }

    if ((uint) call.MethodIndex >= (uint) iface.Methods.Length) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"call_iface method index {call.MethodIndex} out of range for '{iface.Name}'."));
      return;
    }

    IrInterfaceMethod method = iface.Methods[call.MethodIndex];
    if (call.IsMutating && !method.IsMutating) {
      errors.Add(new IrVerificationError("IR-MUT-01", fn.Name, bb, idx, $"call_iface marks non-mutating method '{method.Name}' as mutating."));
    } else if (!call.IsMutating && method.IsMutating) {
      errors.Add(new IrVerificationError("IR-MUT-01", fn.Name, bb, idx, $"Mutating interface method '{method.Name}' requires @mut receiver ptr<{iface.Name}>."));
    }

    if (call.Arguments.Length != method.Parameters.Length) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"call_iface '{method.Name}' expected {method.Parameters.Length} arguments, got {call.Arguments.Length}."));
    } else {
      for (int i = 0; i < method.Parameters.Length; i++) {
        VerifyCallArg(fn, types, bb, idx, $"call_iface {method.Name}", i, call.Arguments[i], method.Parameters[i].Type, method.Parameters[i].Ownership, errors);
      }
    }

    VerifyCallReturn(fn, types, bb, idx, $"call_iface {method.Name}", call.Dest, method.ReturnType, errors);
  }

  private static void VerifyCallSignature(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      string label,
      IrTempId? dest,
      ImmutableArray<IrOperand> args,
      ImmutableArray<IrParam> expectedParams,
      IrTypeId returnType,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (args.Length != expectedParams.Length) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} expected {expectedParams.Length} arguments, got {args.Length}."));
    } else {
      for (int i = 0; i < expectedParams.Length; i++) {
        VerifyCallArg(fn, types, bb, idx, label, i, args[i], expectedParams[i].Type, expectedParams[i].Ownership, errors);
      }
    }

    VerifyCallReturn(fn, types, bb, idx, label, dest, returnType, errors);
  }

  private static void VerifyCallArg(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      string label,
      int argIndex,
      IrOperand arg,
      IrTypeId expectedParamType,
      IrParamOwnership ownership,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (!TryGetOperandIrType(fn, types, arg, out IrType? actualType)) {
      return;
    }

    if (ownership == IrParamOwnership.Mut) {
      if (actualType.Kind != IrTypeKind.Ptr) {
        errors.Add(new IrVerificationError("IR-MUT-01", fn.Name, bb, idx, $"{label} argument {argIndex} passed to @mut parameter must be ptr<T>, got '{actualType.Name}'."));
        return;
      }

      if (actualType.Id != expectedParamType) {
        errors.Add(new IrVerificationError("IR-MUT-01", fn.Name, bb, idx, $"{label} @mut argument {argIndex} type mismatch: expected '{FormatTypeName(types, expectedParamType)}', got '{actualType.Name}'."));
      }
    } else if (actualType.Id != expectedParamType) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} argument {argIndex} type mismatch: expected '{FormatTypeName(types, expectedParamType)}', got '{actualType.Name}'."));
    }
  }

  private static void VerifyCallReturn(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      string label,
      IrTempId? dest,
      IrTypeId returnType,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (returnType == types.Void) {
      if (dest is { } d) {
        ExpectTempType(fn, types, bb, idx, d, types.Void, label, errors);
      }
    } else {
      if (dest is not { } d) {
        errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} returns '{FormatTypeName(types, returnType)}' and must assign to a destination temp."));
      } else {
        ExpectTempType(fn, types, bb, idx, d, returnType, label, errors);
      }
    }
  }

  private static void VerifyCallRuntime(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      CallRuntime call,
      ImmutableArray<IrVerificationError>.Builder errors) {
    string label = $"call.rt {call.Op}";
    switch (call.Op) {
      case IrRuntimeOp.PrintlnN:
        foreach (IrOperand arg in call.Arguments) {
          ExpectOperandType(fn, types, bb, idx, arg, types.String, label, errors);
        }
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.Void, errors);
        break;

      case IrRuntimeOp.Eprintln:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.String], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.Void, errors);
        break;

      case IrRuntimeOp.Exit:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.Int], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.Void, errors);
        break;

      case IrRuntimeOp.Readline:
      case IrRuntimeOp.ParseInt:
      case IrRuntimeOp.ParseFloat:
      case IrRuntimeOp.ReadFile:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.String], errors);
        ExpectUnionDest(fn, types, bb, idx, call.Dest, label, errors);
        break;

      case IrRuntimeOp.WriteFile:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.String, types.String], errors);
        ExpectUnionDest(fn, types, bb, idx, call.Dest, label, errors);
        break;

      case IrRuntimeOp.FileExists:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.String], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.Bool, errors);
        break;

      case IrRuntimeOp.StrFirstRune:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.String], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.Rune, errors);
        break;

      case IrRuntimeOp.ToStr:
        if (call.Arguments.Length != 1) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} expected 1 argument, got {call.Arguments.Length}."));
        } else if (TryGetOperandIrType(fn, types, call.Arguments[0], out IrType? argType)
            && argType.Kind is IrTypeKind.Void or IrTypeKind.Never or IrTypeKind.Ptr) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} cannot format '{argType.Name}'."));
        }
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.String, errors);
        break;

      case IrRuntimeOp.IfaceTypeName:
        if (call.Arguments.Length != 1) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} expected 1 argument, got {call.Arguments.Length}."));
        } else if (TryGetOperandIrType(fn, types, call.Arguments[0], out IrType? argType)
            && argType.Kind != IrTypeKind.Interface) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} requires an interface, got '{argType.Name}'."));
        }
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.String, errors);
        break;

      case IrRuntimeOp.RandFloat:
      case IrRuntimeOp.Time:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.Float, errors);
        break;

      case IrRuntimeOp.RandInt:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.Int], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.Int, errors);
        break;

      case IrRuntimeOp.Sleep:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.Int], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.Void, errors);
        break;

      case IrRuntimeOp.Clear:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.Void, errors);
        break;

      case IrRuntimeOp.RegexCompile:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.String], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.Regex, errors);
        break;

      case IrRuntimeOp.StrLower:
      case IrRuntimeOp.StrUpper:
      case IrRuntimeOp.StrTrim:
      case IrRuntimeOp.StrTrimStart:
      case IrRuntimeOp.StrTrimEnd:
      case IrRuntimeOp.StrClone:
      case IrRuntimeOp.StrToCString:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.String], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.String, errors);
        break;

      case IrRuntimeOp.StrContains:
      case IrRuntimeOp.StrStartsWith:
      case IrRuntimeOp.StrEndsWith:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.String, types.String], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.Bool, errors);
        break;

      case IrRuntimeOp.StrIndexOf:
      case IrRuntimeOp.StrLastIndexOf:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.String, types.String], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.Int, errors);
        break;

      case IrRuntimeOp.StrSplit:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.String, types.String], errors);
        ExpectVectorOf(fn, types, bb, idx, call.Dest ?? IrTempId.Invalid, types.String, label, errors);
        break;

      case IrRuntimeOp.StrReplace:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.String, types.String, types.String], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.String, errors);
        break;

      case IrRuntimeOp.StrSubstring:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.String, types.Int, types.Int], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.String, errors);
        break;

      case IrRuntimeOp.RegexPattern:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.Regex], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.String, errors);
        break;

      case IrRuntimeOp.RegexHasMatch:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.Regex, types.String], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.Bool, errors);
        break;

      case IrRuntimeOp.RegexMatchPrefix:
      case IrRuntimeOp.RegexFind:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.Regex, types.String], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.String, errors);
        break;

      case IrRuntimeOp.RegexFindAll:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.Regex, types.String], errors);
        ExpectVectorOf(fn, types, bb, idx, call.Dest ?? IrTempId.Invalid, types.String, label, errors);
        break;

      case IrRuntimeOp.RegexReplace:
        ExpectRuntimeArgs(fn, types, bb, idx, call, label, [types.Regex, types.String, types.String], errors);
        VerifyCallReturn(fn, types, bb, idx, label, call.Dest, types.String, errors);
        break;

      default:
        errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"Unknown runtime op {call.Op}."));
        break;
    }
  }

  private static void VerifyTerminatorSemantics(
      IrModule module,
      IrFunction fn,
      IrBlockId bb,
      int idx,
      IrTerminator term,
      ImmutableArray<IrVerificationError>.Builder errors) {
    IrTypeTable types = module.Types;
    switch (term) {
      case BrIf b:
        ExpectOperandType(fn, types, bb, idx, b.Condition, types.Bool, "br_if", errors);
        break;

      case Switch s:
        if (TryGetOperandIrType(fn, types, s.Scrutinee, out IrType? scrutType)
            && scrutType.Kind is not (IrTypeKind.Int or IrTypeKind.Enum or IrTypeKind.Rune)) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"switch scrutinee must be int, enum, or rune, got '{scrutType.Name}'."));
        }
        break;

      case Ret r:
        if (fn.ReturnType == types.Void) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, "Function returning void must use ret.void instead of ret."));
        } else {
          ExpectOperandType(fn, types, bb, idx, r.Value, fn.ReturnType, "ret", errors);
        }
        break;

      case RetVoid:
        if (fn.ReturnType != types.Void) {
          errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"Function returning '{FormatTypeName(types, fn.ReturnType)}' cannot use ret.void."));
        }
        break;

      case Panic p:
        ExpectOperandType(fn, types, bb, idx, p.Message, types.String, "panic", errors);
        break;
    }
  }

  private static void VerifyArenaScopes(
      IrFunction fn,
      bool[] reachable,
      ImmutableArray<IrVerificationError>.Builder errors) {
    int n = fn.Blocks.Count;
    var entryArenas = new HashSet<int>[n];
    var visited = new bool[n];
    var worklist = new Queue<int>();
    var targets = new List<IrBlockId>();

    entryArenas[0] = [];
    visited[0] = true;
    worklist.Enqueue(0);

    while (worklist.Count > 0) {
      int b = worklist.Dequeue();
      if (!reachable[b]) {
        continue;
      }

      IrBasicBlock block = fn.Blocks[b];
      var active = new HashSet<int>(entryArenas[b]);

      for (int i = 0; i < block.Instructions.Count; i++) {
        IrInstruction instr = block.Instructions[i];
        switch (instr) {
          case ArenaEnter enter:
            if (enter.ArenaId >= 0 && !active.Add(enter.ArenaId)) {
              errors.Add(new IrVerificationError("IR-REG-01", fn.Name, block.Id, i, $"Arena {enter.ArenaId} is already active on entry to arena_enter."));
            }
            break;

          case ArenaExit exit:
            if (exit.ArenaId >= 0 && !active.Remove(exit.ArenaId)) {
              errors.Add(new IrVerificationError("IR-REG-01", fn.Name, block.Id, i, $"arena_exit {exit.ArenaId} has no matching active arena_enter."));
            }
            break;
        }

        if (instr.AllocationRegion is { Kind: AllocRegionKind.Arena, ArenaId: int reqId and >= 0 }
            && !active.Contains(reqId)) {
          errors.Add(new IrVerificationError("IR-REG-01", fn.Name, block.Id, i, $"Instruction allocates in arena({reqId}) which is not currently active."));
        }
      }

      if (block.Terminator is Ret or RetVoid && active.Count > 0) {
        errors.Add(new IrVerificationError("IR-REG-01", fn.Name, block.Id, block.Instructions.Count, $"Function returns with unclosed arena(s): {string.Join(", ", active.Order())}."));
      }

      if (block.Terminator is null) {
        continue;
      }

      targets.Clear();
      block.Terminator.CollectTargets(targets);
      foreach (IrBlockId target in targets) {
        if ((uint) target.Index >= (uint) n) {
          continue;
        }

        if (!visited[target.Index]) {
          visited[target.Index] = true;
          entryArenas[target.Index] = [.. active];
          worklist.Enqueue(target.Index);
        } else if (!entryArenas[target.Index].SetEquals(active)) {
          errors.Add(new IrVerificationError("IR-REG-01", fn.Name, block.Id, block.Instructions.Count, $"Inconsistent active arena scope on edge bb{b} -> bb{target.Index}."));
        }
      }
    }
  }

  private static (bool[] Reachable, int[] Idom) ComputeDominators(IrFunction fn) {
    int n = fn.Blocks.Count;
    var reachable = new bool[n];
    var postOrder = new List<int>(n);
    var preds = new List<int>[n];
    for (int i = 0; i < n; i++) {
      preds[i] = [];
    }

    var visited = new bool[n];
    var targets = new List<IrBlockId>();
    var dfsStack = new Stack<(int Node, int TargetIdx, List<IrBlockId> Succs)>();

    List<IrBlockId> GetSuccs(int b) {
      var list = new List<IrBlockId>();
      fn.Blocks[b].Terminator?.CollectTargets(list);
      return list;
    }

    visited[0] = true;
    dfsStack.Push((0, 0, GetSuccs(0)));

    while (dfsStack.Count > 0) {
      (int u, int tIdx, List<IrBlockId> succs) = dfsStack.Pop();
      if (tIdx < succs.Count) {
        dfsStack.Push((u, tIdx + 1, succs));
        int v = succs[tIdx].Index;
        if ((uint) v < (uint) n) {
          preds[v].Add(u);
          if (!visited[v]) {
            visited[v] = true;
            dfsStack.Push((v, 0, GetSuccs(v)));
          }
        }
      } else {
        reachable[u] = true;
        postOrder.Add(u);
      }
    }

    var rpo = new int[postOrder.Count];
    var rpoIndex = new int[n];
    Array.Fill(rpoIndex, -1);
    for (int i = 0; i < postOrder.Count; i++) {
      int b = postOrder[postOrder.Count - 1 - i];
      rpo[i] = b;
      rpoIndex[b] = i;
    }

    var idom = new int[n];
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

          newIdom = newIdom == -1 ? p : Intersect(idom, rpoIndex, p, newIdom);
        }

        if (newIdom != -1 && idom[b] != newIdom) {
          idom[b] = newIdom;
          changed = true;
        }
      }
    }

    return (reachable, idom);
  }

  private static int Intersect(int[] idom, int[] rpoIndex, int b1, int b2) {
    int f1 = b1;
    int f2 = b2;
    while (f1 != f2) {
      while (rpoIndex[f1] > rpoIndex[f2]) {
        f1 = idom[f1];
      }

      while (rpoIndex[f2] > rpoIndex[f1]) {
        f2 = idom[f2];
      }
    }

    return f1;
  }

  private static bool Dominates(int[] idom, int dom, int node) {
    int cur = node;
    while (cur != dom) {
      int parent = idom[cur];
      if (parent < 0 || parent == cur) {
        return false;
      }

      cur = parent;
    }

    return true;
  }

  private static void VerifyBinarySameType(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrTempId dest,
      IrOperand left,
      IrOperand right,
      string opName,
      bool allowRune,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (!TryGetOperandIrType(fn, types, left, out IrType? lt)
        || !TryGetOperandIrType(fn, types, right, out IrType? rt)) {
      return;
    }

    if (lt.Id != rt.Id) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{opName} operand types mismatch: '{lt.Name}' vs '{rt.Name}'."));
      return;
    }

    bool valid = lt.Kind is IrTypeKind.Int or IrTypeKind.Float || (allowRune && lt.Kind == IrTypeKind.Rune);
    if (!valid) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{opName} does not support type '{lt.Name}'."));
      return;
    }

    ExpectTempType(fn, types, bb, idx, dest, lt.Id, opName, errors);
  }

  private static void ExpectRuntimeArgs(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      CallRuntime call,
      string label,
      ReadOnlySpan<IrTypeId> expectedArgs,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (call.Arguments.Length != expectedArgs.Length) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} expected {expectedArgs.Length} argument(s), got {call.Arguments.Length}."));
      return;
    }

    for (int i = 0; i < expectedArgs.Length; i++) {
      ExpectOperandType(fn, types, bb, idx, call.Arguments[i], expectedArgs[i], $"{label} arg {i}", errors);
    }
  }

  private static void ExpectUnionDest(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrTempId? dest,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (dest is not { } d) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} requires a destination temp."));
    } else if (TryGetTempIrType(fn, types, d, out IrType? t) && t is not IrUnionType) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} dest must be a union type, got '{t.Name}'."));
    }
  }

  private static void ExpectVectorOf(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrTempId dest,
      IrTypeId elemType,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (TryGetTempIrType(fn, types, dest, out IrType? t)
        && (t is not IrVectorType vt || vt.ElementType != elemType)) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} dest must be []{FormatTypeName(types, elemType)}, got '{t.Name}'."));
    }
  }

  private static void ExpectOptionOf(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrTempId dest,
      IrTypeId elemType,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (TryGetTempIrType(fn, types, dest, out IrType? t)) {
      if (t is not IrUnionType ut
          || ut.Variants.Length != 2
          || ut.Variants[0].Fields.Length != 1
          || ut.Variants[0].Fields[0].Type != elemType
          || ut.Variants[1].Fields.Length != 0) {
        errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} dest must be Option[{FormatTypeName(types, elemType)}], got '{t.Name}'."));
      }
    }
  }

  private static void ExpectHandleOf(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrTempId dest,
      IrTypeId elemType,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (TryGetTempIrType(fn, types, dest, out IrType? t)
        && (t is not IrHandleType ht || ht.ElementType != elemType)) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} dest must be Handle[{FormatTypeName(types, elemType)}], got '{t.Name}'."));
    }
  }

  private static void ExpectHandleOperandOf(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrOperand handle,
      IrTypeId elemType,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (TryGetOperandIrType(fn, types, handle, out IrType? t)
        && (t is not IrHandleType ht || ht.ElementType != elemType)) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} handle must be Handle[{FormatTypeName(types, elemType)}], got '{t.Name}'."));
    }
  }

  private static bool TryGetSlot(
      IrFunction fn,
      IrBlockId bb,
      int idx,
      IrSlotId slotId,
      string opName,
      ImmutableArray<IrVerificationError>.Builder errors,
      out IrLocalSlot? slot) {
    if ((uint) slotId.Index >= (uint) fn.Slots.Count) {
      errors.Add(new IrVerificationError("IR-SSA-01", fn.Name, bb, idx, $"{opName} references out-of-range slot {slotId}."));
      slot = null;
      return false;
    }

    slot = fn.Slots[slotId.Index];
    return true;
  }

  private static bool TryGetTempIrType(
      IrFunction fn,
      IrTypeTable types,
      IrTempId tempId,
      out IrType irType) {
    if ((uint) tempId.Index < (uint) fn.Temps.Count
        && types.TryGetType(fn.Temps[tempId.Index].Type, out IrType? resolved)) {
      irType = resolved;
      return true;
    }

    irType = null!;
    return false;
  }

  private static bool TryGetOperandIrType(
      IrFunction fn,
      IrTypeTable types,
      IrOperand op,
      out IrType irType) {
    IrTypeId typeId = IrTypeId.Invalid;
    if (op.Kind == IrOperandKind.Temp && (uint) op.Index < (uint) fn.Temps.Count) {
      typeId = fn.Temps[op.Index].Type;
    } else if (op.Kind == IrOperandKind.Param && (uint) op.Index < (uint) fn.Parameters.Length) {
      typeId = fn.Parameters[op.Index].Type;
    }

    if (types.TryGetType(typeId, out IrType? resolved)) {
      irType = resolved;
      return true;
    }

    irType = null!;
    return false;
  }

  private static bool TryGetPtrTarget(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrOperand op,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors,
      out IrType? targetType) {
    targetType = null;
    if (!TryGetOperandIrType(fn, types, op, out IrType? opType)) {
      return false;
    }

    if (opType is not IrPtrType ptrType) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} requires ptr<T>, got '{opType.Name}'."));
      return false;
    }

    if (!types.TryGetType(ptrType.TargetType, out targetType)) {
      errors.Add(new IrVerificationError("IR-TYPE-01", fn.Name, bb, idx, $"{label} pointer target type {ptrType.TargetType} is invalid."));
      return false;
    }

    return true;
  }

  private static bool TryGetVecType(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrOperand op,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors,
      out IrVectorType? vecType) {
    vecType = null;
    if (!TryGetOperandIrType(fn, types, op, out IrType? opType)) {
      return false;
    }

    if (opType is not IrVectorType vt) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} requires []T, got '{opType.Name}'."));
      return false;
    }

    vecType = vt;
    return true;
  }

  private static bool TryGetMapType(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrOperand op,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors,
      out IrMapType? mapType) {
    mapType = null;
    if (!TryGetOperandIrType(fn, types, op, out IrType? opType)) {
      return false;
    }

    if (opType is not IrMapType mt) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} requires map[K, V], got '{opType.Name}'."));
      return false;
    }

    mapType = mt;
    return true;
  }

  private static bool TryGetSetType(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrOperand op,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors,
      out IrSetType? setType) {
    setType = null;
    if (!TryGetOperandIrType(fn, types, op, out IrType? opType)) {
      return false;
    }

    if (opType is not IrSetType st) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} requires set[T], got '{opType.Name}'."));
      return false;
    }

    setType = st;
    return true;
  }

  private static bool TryGetPoolType(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrOperand op,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors,
      out IrPoolType? poolType) {
    poolType = null;
    if (!TryGetOperandIrType(fn, types, op, out IrType? opType)) {
      return false;
    }

    if (opType is not IrPoolType pt) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} requires Pool[T], got '{opType.Name}'."));
      return false;
    }

    poolType = pt;
    return true;
  }

  private static void ExpectTempType(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrTempId dest,
      IrTypeId expectedType,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (TryGetTempIrType(fn, types, dest, out IrType? actual) && actual.Id != expectedType) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} dest type mismatch: expected '{FormatTypeName(types, expectedType)}', got '{actual.Name}'."));
    }
  }

  private static void ExpectOperandType(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrOperand op,
      IrTypeId expectedType,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (TryGetOperandIrType(fn, types, op, out IrType? actual) && actual.Id != expectedType) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} operand type mismatch: expected '{FormatTypeName(types, expectedType)}', got '{actual.Name}'."));
    }
  }

  private static void ExpectSameType(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrTempId dest,
      IrOperand src,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (TryGetTempIrType(fn, types, dest, out IrType? dt)
        && TryGetOperandIrType(fn, types, src, out IrType? st)
        && dt.Id != st.Id) {
      errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} type mismatch: dest is '{dt.Name}', source is '{st.Name}'."));
    }
  }

  private static void ExpectPtrToType(
      IrFunction fn,
      IrTypeTable types,
      IrBlockId bb,
      int idx,
      IrTempId dest,
      IrTypeId targetType,
      string label,
      ImmutableArray<IrVerificationError>.Builder errors) {
    if (TryGetTempIrType(fn, types, dest, out IrType? actual)) {
      if (actual is not IrPtrType pt || pt.TargetType != targetType) {
        errors.Add(new IrVerificationError("IR-TYPE-03", fn.Name, bb, idx, $"{label} dest must be ptr<{FormatTypeName(types, targetType)}>, got '{actual.Name}'."));
      }
    }
  }

  private static string FormatTypeName(IrTypeTable types, IrTypeId id) =>
      types.TryGetType(id, out IrType? t) ? t.Name : id.ToString();

  private static string FormatTempName(IrFunction fn, IrTempId id) {
    if ((uint) id.Index < (uint) fn.Temps.Count) {
      string? name = fn.Temps[id.Index].Name;
      return string.IsNullOrEmpty(name) ? $"%t{id.Index}" : $"%{name}";
    }

    return id.ToString();
  }
}
