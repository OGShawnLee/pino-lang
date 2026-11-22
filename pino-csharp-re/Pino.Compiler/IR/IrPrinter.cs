using System.Globalization;
using System.Text;

using Pino.Compiler.Checking;

namespace Pino.Compiler.IR;

/// <summary>
/// Deterministic canonical textual printer for <see cref="IrModule"/> and <see cref="IrFunction"/>
/// matching <c>056-re-ir-design</c> §1.2 and §4.2.
/// </summary>
public static class IrPrinter {
  public static string Print(IrModule module) {
    ArgumentNullException.ThrowIfNull(module);
    var sb = new StringBuilder();
    bool wroteSection = false;

    if (module.Globals.Count > 0) {
      foreach (IrGlobal global in module.Globals) {
        string mutKw = global.IsMutable ? "mut " : "";
        sb.Append("global ").Append(mutKw).Append('$').Append(global.Name).Append(": ");
        sb.Append(FormatType(module.Types, global.Type)).Append('\n');
      }

      wroteSection = true;
    }

    if (module.VTables.Count > 0) {
      if (wroteSection) {
        sb.Append('\n');
      }

      foreach (IrVTable vt in module.VTables) {
        sb.Append("vtable vt").Append(vt.Id.Index).Append(": ");
        sb.Append(FormatType(module.Types, vt.ConcreteStructType));
        sb.Append(" as ").Append(FormatType(module.Types, vt.InterfaceType));
        sb.Append(" [");
        for (int i = 0; i < vt.MethodFuncIds.Length; i++) {
          if (i > 0) {
            sb.Append(", ");
          }

          sb.Append(FormatFuncRef(module, vt.MethodFuncIds[i]));
        }

        sb.Append("]\n");
      }

      wroteSection = true;
    }

    foreach (IrFunction fn in module.Functions) {
      if (wroteSection) {
        sb.Append('\n');
      }

      AppendFunction(sb, module, fn);
      wroteSection = true;
    }

    if (module.InitFuncIds.Count > 0 || module.EntryFuncId is not null || module.ShutdownFuncId is not null) {
      if (wroteSection) {
        sb.Append('\n');
      }

      sb.Append("entry_points: mode=").Append(module.IsProgramMode ? "program" : "script");
      if (module.InitFuncIds.Count > 0) {
        sb.Append(" init=[");
        for (int i = 0; i < module.InitFuncIds.Count; i++) {
          if (i > 0) {
            sb.Append(", ");
          }

          sb.Append(FormatFuncRef(module, module.InitFuncIds[i]));
        }

        sb.Append(']');
      }

      if (module.EntryFuncId is { } entryId) {
        sb.Append(" entry=").Append(FormatFuncRef(module, entryId));
      }

      if (module.ShutdownFuncId is { } shutdownId) {
        sb.Append(" shutdown=").Append(FormatFuncRef(module, shutdownId));
      }

      sb.Append('\n');
    }

    return sb.ToString();
  }

  public static string PrintFunction(IrModule module, IrFunction fn) {
    ArgumentNullException.ThrowIfNull(module);
    ArgumentNullException.ThrowIfNull(fn);
    var sb = new StringBuilder();
    AppendFunction(sb, module, fn);
    return sb.ToString();
  }

  private static void AppendFunction(StringBuilder sb, IrModule module, IrFunction fn) {
    sb.Append("fn ").Append(fn.Name).Append('(');
    for (int i = 0; i < fn.Parameters.Length; i++) {
      if (i > 0) {
        sb.Append(", ");
      }

      IrParam p = fn.Parameters[i];
      sb.Append(FormatOwnership(p.Ownership)).Append(' ');
      sb.Append('%').Append(p.Name).Append(": ");
      sb.Append(FormatType(module.Types, p.Type));
    }

    sb.Append(") -> ").Append(FormatType(module.Types, fn.ReturnType));
    sb.Append(' ').Append(FormatEffects(fn.ModuleStateEffects));
    sb.Append(" {\n");

    if (fn.Slots.Count > 0) {
      sb.Append("  slots: ");
      for (int i = 0; i < fn.Slots.Count; i++) {
        if (i > 0) {
          sb.Append(", ");
        }

        IrLocalSlot slot = fn.Slots[i];
        sb.Append('$').Append(slot.Name).Append(": ").Append(FormatType(module.Types, slot.Type));
        if (slot.IsMutSlot) {
          sb.Append(" [mut]");
        }

        if (slot.IsBoxedCell) {
          sb.Append(" [cell]");
        }
      }

      sb.Append('\n');
    }

    foreach (IrBasicBlock block in fn.Blocks) {
      sb.Append("  bb").Append(block.Id.Index).Append(":\n");
      foreach (IrInstruction instr in block.Instructions) {
        sb.Append("    ");
        AppendInstruction(sb, module, fn, instr);
        sb.Append('\n');
      }

      sb.Append("    ");
      if (block.Terminator is { } term) {
        AppendTerminator(sb, fn, term);
      } else {
        sb.Append("<missing-terminator>");
      }

      sb.Append('\n');
    }

    sb.Append("}\n");
  }

  private static void AppendInstruction(StringBuilder sb, IrModule module, IrFunction fn, IrInstruction instr) {
    switch (instr) {
      case ConstInt c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("const.i64 ").Append(c.Value.ToString(CultureInfo.InvariantCulture));
        break;

      case ConstFloat c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("const.f64 ").Append(FormatFloat(c.Value));
        break;

      case ConstBool c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("const.bool ").Append(c.Value ? "true" : "false");
        break;

      case ConstRune c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("const.rune ").Append(c.Value.ToString(CultureInfo.InvariantCulture));
        break;

      case ConstString c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("const.str ");
        if ((uint) c.StringPoolId < (uint) module.StringPool.Count) {
          AppendQuotedString(sb, module.StringPool[c.StringPoolId]);
        } else {
          sb.Append($"<invalid-str#{c.StringPoolId}>");
        }
        break;

      case ConstEnum c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("const.enum ").Append(FormatEnumMember(module.Types, c.EnumType, c.Ordinal));
        break;

      case Copy c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("copy ").Append(FormatOperand(fn, c.Source));
        break;

      case Move m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("move ").Append(FormatOperand(fn, m.Source));
        break;

      case LoadSlot l:
        AppendDest(sb, fn, l.Dest);
        sb.Append("load ").Append(FormatSlot(fn, l.Slot));
        break;

      case StoreSlot s:
        sb.Append("store ").Append(FormatSlot(fn, s.Slot)).Append(", ").Append(FormatOperand(fn, s.Value));
        break;

      case AddrLocal a:
        AppendDest(sb, fn, a.Dest);
        sb.Append("addr_local ").Append(FormatSlot(fn, a.Slot));
        break;

      case AddrGlobal a:
        AppendDest(sb, fn, a.Dest);
        sb.Append("addr_global ").Append(FormatGlobal(module, a.Global));
        break;

      case FieldGet f:
        AppendDest(sb, fn, f.Dest);
        sb.Append("field_get ").Append(FormatOperand(fn, f.Target)).Append(", ");
        sb.Append(FormatStructField(module.Types, GetOperandType(fn, f.Target), f.FieldIndex));
        break;

      case FieldAddr f:
        AppendDest(sb, fn, f.Dest);
        sb.Append("field_addr ").Append(FormatOperand(fn, f.TargetPtr)).Append(", ");
        sb.Append(FormatPtrStructField(module.Types, GetOperandType(fn, f.TargetPtr), f.FieldIndex));
        break;

      case LoadPtr l:
        AppendDest(sb, fn, l.Dest);
        sb.Append("load_ptr ").Append(FormatOperand(fn, l.Ptr));
        break;

      case StorePtr s:
        sb.Append("store_ptr ").Append(FormatOperand(fn, s.Ptr)).Append(", ").Append(FormatOperand(fn, s.Value));
        break;

      case PtrEq p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("ptr.eq ").Append(FormatOperand(fn, p.LeftPtr)).Append(", ").Append(FormatOperand(fn, p.RightPtr));
        break;

      case Add a:
        AppendDest(sb, fn, a.Dest);
        sb.Append("add.").Append(FormatNumericSuffix(module.Types, GetOperandType(fn, a.Left)));
        sb.Append(' ').Append(FormatOperand(fn, a.Left)).Append(", ").Append(FormatOperand(fn, a.Right));
        break;

      case Sub s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("sub.").Append(FormatNumericSuffix(module.Types, GetOperandType(fn, s.Left)));
        sb.Append(' ').Append(FormatOperand(fn, s.Left)).Append(", ").Append(FormatOperand(fn, s.Right));
        break;

      case Mul m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("mul.").Append(FormatNumericSuffix(module.Types, GetOperandType(fn, m.Left)));
        sb.Append(' ').Append(FormatOperand(fn, m.Left)).Append(", ").Append(FormatOperand(fn, m.Right));
        break;

      case DivF64 d:
        AppendDest(sb, fn, d.Dest);
        sb.Append("div.f64 ").Append(FormatOperand(fn, d.Left)).Append(", ").Append(FormatOperand(fn, d.Right));
        break;

      case FloorDiv d:
        AppendDest(sb, fn, d.Dest);
        sb.Append("floordiv.").Append(FormatNumericSuffix(module.Types, GetOperandType(fn, d.Left)));
        sb.Append(' ').Append(FormatOperand(fn, d.Left)).Append(", ").Append(FormatOperand(fn, d.Right));
        break;

      case FloorMod m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("floormod.").Append(FormatNumericSuffix(module.Types, GetOperandType(fn, m.Left)));
        sb.Append(' ').Append(FormatOperand(fn, m.Left)).Append(", ").Append(FormatOperand(fn, m.Right));
        break;

      case Neg n:
        AppendDest(sb, fn, n.Dest);
        sb.Append("neg.").Append(FormatNumericSuffix(module.Types, GetOperandType(fn, n.Operand)));
        sb.Append(' ').Append(FormatOperand(fn, n.Operand));
        break;

      case Not n:
        AppendDest(sb, fn, n.Dest);
        sb.Append("not ").Append(FormatOperand(fn, n.Operand));
        break;

      case Cmp c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("cmp.").Append(FormatCmpOp(c.Op)).Append('.');
        sb.Append(FormatNumericSuffix(module.Types, GetOperandType(fn, c.Left)));
        sb.Append(' ').Append(FormatOperand(fn, c.Left)).Append(", ").Append(FormatOperand(fn, c.Right));
        break;

      case CastI64ToF64 c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("cast.i64_to_f64 ").Append(FormatOperand(fn, c.Source));
        break;

      case CastF64ToI64 c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("cast.f64_to_i64 ").Append(FormatOperand(fn, c.Source));
        break;

      case CastI64ToRune c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("cast.i64_to_rune ").Append(FormatOperand(fn, c.Source));
        break;

      case CastRuneToI64 c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("cast.rune_to_i64 ").Append(FormatOperand(fn, c.Source));
        break;

      case StructEq s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("struct.eq ").Append(FormatOperand(fn, s.Left)).Append(", ").Append(FormatOperand(fn, s.Right));
        break;

      case StructNew s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("struct.new ").Append(FormatType(module.Types, GetTempType(fn, s.Dest)));
        AppendOperandList(sb, fn, s.Fields);
        break;

      case TupleNew t:
        AppendDest(sb, fn, t.Dest);
        sb.Append("tuple.new ").Append(FormatType(module.Types, GetTempType(fn, t.Dest)));
        AppendOperandList(sb, fn, t.Fields);
        break;

      case TupleField t:
        AppendDest(sb, fn, t.Dest);
        sb.Append("tuple.field ").Append(FormatOperand(fn, t.Tuple)).Append(", ").Append(t.FieldIndex);
        break;

      case UnionNew u:
        AppendDest(sb, fn, u.Dest);
        sb.Append("union.new ").Append(FormatUnionVariant(module.Types, GetTempType(fn, u.Dest), u.VariantIndex));
        AppendOperandList(sb, fn, u.Fields);
        sb.Append(" [region=").Append(u.Region).Append(']');
        break;

      case UnionTag u:
        AppendDest(sb, fn, u.Dest);
        sb.Append("union.tag ").Append(FormatOperand(fn, u.Union));
        break;

      case UnionField u:
        AppendDest(sb, fn, u.Dest);
        sb.Append("union.field ").Append(FormatOperand(fn, u.Union));
        sb.Append(", ").Append(u.VariantIndex).Append(", ").Append(u.FieldIndex);
        break;

      case IfaceBox b:
        AppendDest(sb, fn, b.Dest);
        sb.Append("iface.box ").Append(FormatOperand(fn, b.Value)).Append(", vt").Append(b.VTable.Index);
        sb.Append(" [region=").Append(b.Region).Append(']');
        break;

      case IfaceFieldGet g:
        AppendDest(sb, fn, g.Dest);
        sb.Append("iface.field_get ").Append(FormatOperand(fn, g.Iface)).Append(", ").Append(g.PropertyIndex);
        break;

      case IfaceFieldAddr a:
        AppendDest(sb, fn, a.Dest);
        sb.Append("iface.field_addr ").Append(FormatOperand(fn, a.IfacePtr)).Append(", ").Append(a.PropertyIndex);
        break;

      case CellNew c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("cell.new ").Append(FormatType(module.Types, GetTempType(fn, c.Dest)));
        sb.Append('(').Append(FormatOperand(fn, c.InitialValue)).Append(')');
        sb.Append(" [region=").Append(c.Region).Append(']');
        break;

      case CellGet c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("cell.get ").Append(FormatOperand(fn, c.Cell));
        break;

      case CellSet c:
        sb.Append("cell.set ").Append(FormatOperand(fn, c.Cell)).Append(", ").Append(FormatOperand(fn, c.Value));
        break;

      case CellAddr c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("cell.addr ").Append(FormatOperand(fn, c.Cell));
        break;

      case EnvNew e:
        AppendDest(sb, fn, e.Dest);
        sb.Append("env.new ").Append(FormatType(module.Types, GetTempType(fn, e.Dest)));
        AppendOperandList(sb, fn, e.Captures);
        sb.Append(" [region=").Append(e.Region).Append(']');
        break;

      case EnvFieldGet e:
        AppendDest(sb, fn, e.Dest);
        sb.Append("env.field_get ").Append(FormatOperand(fn, e.Env)).Append(", ").Append(e.FieldIndex);
        break;

      case ClosureMake c:
        AppendDest(sb, fn, c.Dest);
        sb.Append("closure.make ").Append(FormatFuncRef(module, c.Function));
        sb.Append(", ").Append(c.Env is { } envOp ? FormatOperand(fn, envOp) : "null");
        break;

      case VecNew v:
        AppendDest(sb, fn, v.Dest);
        sb.Append("vec.new ").Append(FormatType(module.Types, GetTempType(fn, v.Dest)));
        AppendOperandList(sb, fn, v.Elements);
        sb.Append(" [region=").Append(v.Region).Append(']');
        break;

      case VecLen v:
        AppendDest(sb, fn, v.Dest);
        sb.Append("vec.len ").Append(FormatOperand(fn, v.Vec));
        break;

      case VecCap v:
        AppendDest(sb, fn, v.Dest);
        sb.Append("vec.cap ").Append(FormatOperand(fn, v.Vec));
        break;

      case VecGet v:
        AppendDest(sb, fn, v.Dest);
        sb.Append(v.Unchecked ? "vec.get.unchecked " : "vec.get ");
        sb.Append(FormatOperand(fn, v.Vec)).Append(", ").Append(FormatOperand(fn, v.Index));
        break;

      case VecElemAddr v:
        AppendDest(sb, fn, v.Dest);
        sb.Append("vec.elem_addr ").Append(FormatOperand(fn, v.VecPtr)).Append(", ").Append(FormatOperand(fn, v.Index));
        break;

      case VecPush v:
        sb.Append("vec.push ").Append(FormatOperand(fn, v.VecPtr)).Append(", ").Append(FormatOperand(fn, v.Value));
        break;

      case VecPop v:
        AppendDest(sb, fn, v.Dest);
        sb.Append("vec.pop ").Append(FormatOperand(fn, v.VecPtr));
        break;

      case VecSwap v:
        sb.Append("vec.swap ").Append(FormatOperand(fn, v.VecPtr));
        sb.Append(", ").Append(FormatOperand(fn, v.IndexA)).Append(", ").Append(FormatOperand(fn, v.IndexB));
        break;

      case VecContains v:
        AppendDest(sb, fn, v.Dest);
        sb.Append("vec.contains ").Append(FormatOperand(fn, v.Vec)).Append(", ").Append(FormatOperand(fn, v.Value));
        break;

      case MapNew m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("map.new ").Append(FormatType(module.Types, GetTempType(fn, m.Dest))).Append('(');
        for (int i = 0; i < m.Entries.Length; i++) {
          if (i > 0) {
            sb.Append(", ");
          }

          sb.Append(FormatOperand(fn, m.Entries[i].Key)).Append(" -> ").Append(FormatOperand(fn, m.Entries[i].Value));
        }

        sb.Append(") [region=").Append(m.Region).Append(']');
        break;

      case MapLen m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("map.len ").Append(FormatOperand(fn, m.Map));
        break;

      case MapGet m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("map.get ").Append(FormatOperand(fn, m.Map)).Append(", ").Append(FormatOperand(fn, m.Key));
        break;

      case MapElemAddr m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("map.elem_addr ").Append(FormatOperand(fn, m.MapPtr)).Append(", ").Append(FormatOperand(fn, m.Key));
        break;

      case MapSet m:
        sb.Append("map.set ").Append(FormatOperand(fn, m.MapPtr));
        sb.Append(", ").Append(FormatOperand(fn, m.Key)).Append(", ").Append(FormatOperand(fn, m.Value));
        break;

      case MapRemove m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("map.remove ").Append(FormatOperand(fn, m.MapPtr)).Append(", ").Append(FormatOperand(fn, m.Key));
        break;

      case MapContains m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("map.contains ").Append(FormatOperand(fn, m.Map)).Append(", ").Append(FormatOperand(fn, m.Key));
        break;

      case MapKeys m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("map.keys ").Append(FormatOperand(fn, m.Map)).Append(" [region=").Append(m.Region).Append(']');
        break;

      case MapValues m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("map.values ").Append(FormatOperand(fn, m.Map)).Append(" [region=").Append(m.Region).Append(']');
        break;

      case MapSlotLen m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("map.slot_len ").Append(FormatOperand(fn, m.Map));
        break;

      case MapSlotIsLive m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("map.slot_is_live ").Append(FormatOperand(fn, m.Map)).Append(", ").Append(FormatOperand(fn, m.SlotIndex));
        break;

      case MapSlotKey m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("map.slot_key ").Append(FormatOperand(fn, m.Map)).Append(", ").Append(FormatOperand(fn, m.SlotIndex));
        break;

      case MapSlotValue m:
        AppendDest(sb, fn, m.Dest);
        sb.Append("map.slot_value ").Append(FormatOperand(fn, m.Map)).Append(", ").Append(FormatOperand(fn, m.SlotIndex));
        break;

      case SetNew s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("set.new ").Append(FormatType(module.Types, GetTempType(fn, s.Dest)));
        AppendOperandList(sb, fn, s.Elements);
        sb.Append(" [region=").Append(s.Region).Append(']');
        break;

      case SetLen s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("set.len ").Append(FormatOperand(fn, s.Set));
        break;

      case SetAdd s:
        sb.Append("set.add ").Append(FormatOperand(fn, s.SetPtr)).Append(", ").Append(FormatOperand(fn, s.Value));
        break;

      case SetRemove s:
        sb.Append("set.remove ").Append(FormatOperand(fn, s.SetPtr)).Append(", ").Append(FormatOperand(fn, s.Value));
        break;

      case SetContains s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("set.contains ").Append(FormatOperand(fn, s.Set)).Append(", ").Append(FormatOperand(fn, s.Value));
        break;

      case SetSlotLen s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("set.slot_len ").Append(FormatOperand(fn, s.Set));
        break;

      case SetSlotIsLive s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("set.slot_is_live ").Append(FormatOperand(fn, s.Set)).Append(", ").Append(FormatOperand(fn, s.SlotIndex));
        break;

      case SetSlotElem s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("set.slot_elem ").Append(FormatOperand(fn, s.Set)).Append(", ").Append(FormatOperand(fn, s.SlotIndex));
        break;

      case PoolNew p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("pool.new ").Append(FormatType(module.Types, GetTempType(fn, p.Dest)));
        sb.Append(" [region=").Append(p.Region).Append(']');
        break;

      case PoolLen p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("pool.len ").Append(FormatOperand(fn, p.Pool));
        break;

      case PoolAdd p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("pool.add ").Append(FormatOperand(fn, p.PoolPtr)).Append(", ").Append(FormatOperand(fn, p.Value));
        break;

      case PoolRemove p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("pool.remove ").Append(FormatOperand(fn, p.PoolPtr)).Append(", ").Append(FormatOperand(fn, p.Handle));
        break;

      case PoolGet p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("pool.get ").Append(FormatOperand(fn, p.Pool)).Append(", ").Append(FormatOperand(fn, p.Handle));
        break;

      case PoolGetOrPanic p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("pool.get_or_panic ").Append(FormatOperand(fn, p.Pool));
        sb.Append(", ").Append(FormatOperand(fn, p.Handle)).Append(", ");
        AppendQuotedString(sb, p.PoolName);
        break;

      case PoolElemAddr p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("pool.elem_addr ").Append(FormatOperand(fn, p.PoolPtr));
        sb.Append(", ").Append(FormatOperand(fn, p.Handle)).Append(", ");
        AppendQuotedString(sb, p.PoolName);
        break;

      case PoolHandles p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("pool.handles ").Append(FormatOperand(fn, p.Pool)).Append(" [region=").Append(p.Region).Append(']');
        break;

      case PoolClear p:
        sb.Append("pool.clear ").Append(FormatOperand(fn, p.PoolPtr));
        break;

      case PoolContains p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("pool.contains ").Append(FormatOperand(fn, p.Pool)).Append(", ").Append(FormatOperand(fn, p.Handle));
        break;

      case PoolSlotLen p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("pool.slot_len ").Append(FormatOperand(fn, p.Pool));
        break;

      case PoolSlotIsLive p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("pool.slot_is_live ").Append(FormatOperand(fn, p.Pool)).Append(", ").Append(FormatOperand(fn, p.SlotIndex));
        break;

      case PoolSlotHandle p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("pool.slot_handle ").Append(FormatOperand(fn, p.Pool)).Append(", ").Append(FormatOperand(fn, p.SlotIndex));
        break;

      case PoolSlotValue p:
        AppendDest(sb, fn, p.Dest);
        sb.Append("pool.slot_value ").Append(FormatOperand(fn, p.Pool)).Append(", ").Append(FormatOperand(fn, p.SlotIndex));
        break;

      case StrLen s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("str.len ").Append(FormatOperand(fn, s.Str));
        break;

      case StrGetRune s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("str.get_rune ").Append(FormatOperand(fn, s.Str)).Append(", ").Append(FormatOperand(fn, s.Index));
        break;

      case StrEq s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("str.eq ").Append(FormatOperand(fn, s.Left)).Append(", ").Append(FormatOperand(fn, s.Right));
        break;

      case StrCmp s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("str.cmp ").Append(FormatOperand(fn, s.Left)).Append(", ").Append(FormatOperand(fn, s.Right));
        break;

      case StrConcat s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("str.concat ").Append(FormatOperand(fn, s.Left)).Append(", ").Append(FormatOperand(fn, s.Right));
        sb.Append(" [region=").Append(s.Region).Append(']');
        break;

      case StrConcatN s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("str.concat_n");
        AppendOperandList(sb, fn, s.Parts);
        sb.Append(" [region=").Append(s.Region).Append(']');
        break;

      case StrConcatRune s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("str.concat_rune ").Append(FormatOperand(fn, s.Str)).Append(", ").Append(FormatOperand(fn, s.RuneValue));
        sb.Append(" [region=").Append(s.Region).Append(']');
        break;

      case RuneConcatStr s:
        AppendDest(sb, fn, s.Dest);
        sb.Append("rune.concat_str ").Append(FormatOperand(fn, s.RuneValue)).Append(", ").Append(FormatOperand(fn, s.Str));
        sb.Append(" [region=").Append(s.Region).Append(']');
        break;

      case CallDirect c:
        if (c.Dest is { } cd) {
          AppendDest(sb, fn, cd);
        }

        sb.Append("call ").Append(FormatFuncRef(module, c.Callee)).Append('(');
        AppendCallArgs(sb, module, fn, c.Callee, c.Arguments);
        sb.Append(')');
        break;

      case CallClosure c:
        if (c.Dest is { } ccd) {
          AppendDest(sb, fn, ccd);
        }

        sb.Append("call_closure ").Append(FormatOperand(fn, c.Closure));
        AppendOperandList(sb, fn, c.Arguments);
        break;

      case CallIface c:
        if (c.Dest is { } cid) {
          AppendDest(sb, fn, cid);
        }

        sb.Append("call_iface ").Append(FormatOperand(fn, c.Receiver));
        sb.Append('#').Append(c.MethodIndex);
        if (c.IsMutating) {
          sb.Append(" [mut]");
        }

        AppendOperandList(sb, fn, c.Arguments);
        break;

      case CallRuntime c:
        if (c.Dest is { } crd) {
          AppendDest(sb, fn, crd);
        }

        sb.Append("call.rt ").Append(FormatRuntimeOp(c.Op));
        AppendOperandList(sb, fn, c.Arguments);
        sb.Append(" [region=").Append(c.Region).Append(']');
        break;

      case RcInc r:
        sb.Append("rc_inc ").Append(FormatOperand(fn, r.Value));
        break;

      case RcDec r:
        sb.Append("rc_dec ").Append(FormatOperand(fn, r.Value));
        break;

      case EnsureUnique e:
        sb.Append("ensure_unique ").Append(FormatOperand(fn, e.PlacePtr));
        break;

      case Reset r:
        AppendDest(sb, fn, r.Dest);
        sb.Append("reset ").Append(FormatOperand(fn, r.Value));
        break;

      case Reuse r:
        AppendDest(sb, fn, r.Dest);
        sb.Append("reuse ").Append(FormatOperand(fn, r.Token)).Append(", ");
        sb.Append(FormatUnionVariant(module.Types, GetTempType(fn, r.Dest), r.VariantIndex));
        AppendOperandList(sb, fn, r.Fields);
        sb.Append(" [region=").Append(r.Region).Append(']');
        break;

      case ArenaEnter a:
        sb.Append("arena_enter ").Append(a.ArenaId.ToString(CultureInfo.InvariantCulture));
        break;

      case ArenaExit a:
        sb.Append("arena_exit ").Append(a.ArenaId.ToString(CultureInfo.InvariantCulture));
        break;

      default:
        sb.Append($"<unknown-instr:{instr.GetType().Name}>");
        break;
    }
  }

  private static void AppendTerminator(StringBuilder sb, IrFunction fn, IrTerminator term) {
    switch (term) {
      case Br b:
        sb.Append("br bb").Append(b.Target.Index);
        break;

      case BrIf b:
        sb.Append("br_if ").Append(FormatOperand(fn, b.Condition));
        sb.Append(", bb").Append(b.TrueTarget.Index);
        sb.Append(", bb").Append(b.FalseTarget.Index);
        break;

      case Switch s:
        sb.Append("switch ").Append(FormatOperand(fn, s.Scrutinee)).Append(", [");
        for (int i = 0; i < s.Cases.Length; i++) {
          if (i > 0) {
            sb.Append(", ");
          }

          sb.Append(s.Cases[i].CaseValue.ToString(CultureInfo.InvariantCulture));
          sb.Append(" -> bb").Append(s.Cases[i].Target.Index);
        }

        sb.Append(']');
        if (s.DefaultTarget is { } def) {
          sb.Append(", default -> bb").Append(def.Index);
        }
        break;

      case Ret r:
        sb.Append("ret ").Append(FormatOperand(fn, r.Value));
        break;

      case RetVoid:
        sb.Append("ret.void");
        break;

      case Panic p:
        sb.Append("panic ").Append(FormatOperand(fn, p.Message));
        break;

      case Unreachable:
        sb.Append("unreachable");
        break;

      default:
        sb.Append($"<unknown-term:{term.GetType().Name}>");
        break;
    }
  }

  private static void AppendDest(StringBuilder sb, IrFunction fn, IrTempId dest) =>
      sb.Append(FormatTemp(fn, dest)).Append(" = ");

  private static void AppendOperandList(StringBuilder sb, IrFunction fn, IReadOnlyList<IrOperand> ops) {
    sb.Append('(');
    for (int i = 0; i < ops.Count; i++) {
      if (i > 0) {
        sb.Append(", ");
      }

      sb.Append(FormatOperand(fn, ops[i]));
    }

    sb.Append(')');
  }

  private static void AppendCallArgs(
      StringBuilder sb,
      IrModule module,
      IrFunction fn,
      IrFuncId calleeId,
      IReadOnlyList<IrOperand> args) {
    IrFunction? callee = (uint) calleeId.Index < (uint) module.Functions.Count
        ? module.Functions[calleeId.Index]
        : null;
    for (int i = 0; i < args.Count; i++) {
      if (i > 0) {
        sb.Append(", ");
      }

      if (callee is not null && i < callee.Parameters.Length) {
        sb.Append(FormatOwnership(callee.Parameters[i].Ownership)).Append(' ');
      }

      sb.Append(FormatOperand(fn, args[i]));
    }
  }

  private static string FormatOwnership(IrParamOwnership ownership) => ownership switch {
    IrParamOwnership.Owned => "@owned",
    IrParamOwnership.Borrowed => "@borrowed",
    IrParamOwnership.Mut => "@mut",
    _ => "@unknown",
  };

  private static string FormatEffects(ModuleStateEffects effects) =>
      (effects.ReadsModuleState, effects.WritesModuleState) switch {
        (false, false) => "[pure]",
        (true, false) => "[reads_module_state]",
        (false, true) => "[writes_module_state]",
        (true, true) => "[reads_module_state, writes_module_state]",
      };

  private static string FormatType(IrTypeTable table, IrTypeId id) =>
      table.TryGetType(id, out IrType? type) ? type.Name : id.ToString();

  private static string FormatTemp(IrFunction fn, IrTempId id) {
    if ((uint) id.Index < (uint) fn.Temps.Count) {
      string? name = fn.Temps[id.Index].Name;
      return string.IsNullOrEmpty(name) ? $"%t{id.Index}" : $"%{name}";
    }

    return id.ToString();
  }

  private static string FormatOperand(IrFunction fn, IrOperand op) => op.Kind switch {
    IrOperandKind.Temp => FormatTemp(fn, op.TempId),
    IrOperandKind.Param => (uint) op.Index < (uint) fn.Parameters.Length
        ? $"%{fn.Parameters[op.Index].Name}"
        : $"%arg{op.Index}",
    _ => $"<invalid-op:{op.Index}>",
  };

  private static string FormatSlot(IrFunction fn, IrSlotId id) =>
      (uint) id.Index < (uint) fn.Slots.Count
          ? $"${fn.Slots[id.Index].Name}"
          : id.ToString();

  private static string FormatGlobal(IrModule module, IrGlobalId id) =>
      (uint) id.Index < (uint) module.Globals.Count
          ? $"${module.Globals[id.Index].Name}"
          : id.ToString();

  private static string FormatFuncRef(IrModule module, IrFuncId id) =>
      (uint) id.Index < (uint) module.Functions.Count
          ? module.Functions[id.Index].Name
          : id.ToString();

  private static IrTypeId GetTempType(IrFunction fn, IrTempId id) =>
      (uint) id.Index < (uint) fn.Temps.Count ? fn.Temps[id.Index].Type : IrTypeId.Invalid;

  private static IrTypeId GetOperandType(IrFunction fn, IrOperand op) => op.Kind switch {
    IrOperandKind.Temp => GetTempType(fn, op.TempId),
    IrOperandKind.Param => (uint) op.Index < (uint) fn.Parameters.Length
        ? fn.Parameters[op.Index].Type
        : IrTypeId.Invalid,
    _ => IrTypeId.Invalid,
  };

  private static string FormatNumericSuffix(IrTypeTable table, IrTypeId id) =>
      table.TryGetType(id, out IrType? type)
          ? type.Kind switch {
            IrTypeKind.Int => "i64",
            IrTypeKind.Float => "f64",
            IrTypeKind.Bool => "bool",
            IrTypeKind.Rune => "i32",
            IrTypeKind.Enum => "i32",
            IrTypeKind.Handle => "handle",
            _ => type.Name,
          }
          : id.ToString();

  private static string FormatCmpOp(IrCmpOp op) => op switch {
    IrCmpOp.Eq => "eq",
    IrCmpOp.Ne => "ne",
    IrCmpOp.Lt => "lt",
    IrCmpOp.Le => "le",
    IrCmpOp.Gt => "gt",
    IrCmpOp.Ge => "ge",
    _ => "unknown",
  };

  private static string FormatEnumMember(IrTypeTable table, IrTypeId enumTypeId, int ordinal) {
    if (table.TryGetType(enumTypeId, out IrType? type) && type is IrEnumType e) {
      if ((uint) ordinal < (uint) e.MemberNames.Length) {
        return $"{e.Name}::{e.MemberNames[ordinal]}";
      }

      return $"{e.Name}#{ordinal}";
    }

    return $"{FormatType(table, enumTypeId)}#{ordinal}";
  }

  private static string FormatStructField(IrTypeTable table, IrTypeId structTypeId, int fieldIndex) {
    if (table.TryGetType(structTypeId, out IrType? type)
        && type is IrStructType st
        && (uint) fieldIndex < (uint) st.Fields.Length) {
      return $"{st.Name}.{st.Fields[fieldIndex].Name}";
    }

    return $"{FormatType(table, structTypeId)}#{fieldIndex}";
  }

  private static string FormatPtrStructField(IrTypeTable table, IrTypeId ptrTypeId, int fieldIndex) {
    if (table.TryGetType(ptrTypeId, out IrType? type) && type is IrPtrType ptr) {
      return FormatStructField(table, ptr.TargetType, fieldIndex);
    }

    return $"{FormatType(table, ptrTypeId)}#{fieldIndex}";
  }

  private static string FormatUnionVariant(IrTypeTable table, IrTypeId unionTypeId, int variantIndex) {
    if (table.TryGetType(unionTypeId, out IrType? type)
        && type is IrUnionType ut
        && (uint) variantIndex < (uint) ut.Variants.Length) {
      return $"{ut.Name}::{ut.Variants[variantIndex].Name}";
    }

    return $"{FormatType(table, unionTypeId)}#{variantIndex}";
  }

  private static string FormatRuntimeOp(IrRuntimeOp op) => op switch {
    IrRuntimeOp.PrintlnN => "pino_println_n",
    IrRuntimeOp.Readline => "pino_readline",
    IrRuntimeOp.ParseInt => "pino_parse_int",
    IrRuntimeOp.ParseFloat => "pino_parse_float",
    IrRuntimeOp.StrFirstRune => "pino_str_first_rune",
    IrRuntimeOp.ToStr => "pino_to_str",
    IrRuntimeOp.IfaceTypeName => "pino_iface_type_name",
    IrRuntimeOp.RandFloat => "pino_rand_float",
    IrRuntimeOp.RandInt => "pino_rand_int",
    IrRuntimeOp.Time => "pino_time",
    IrRuntimeOp.Sleep => "pino_sleep",
    IrRuntimeOp.Clear => "pino_clear",
    IrRuntimeOp.RegexCompile => "pino_regex_compile",
    IrRuntimeOp.ReadFile => "pino_read_file",
    IrRuntimeOp.WriteFile => "pino_write_file",
    IrRuntimeOp.FileExists => "pino_file_exists",
    IrRuntimeOp.StrLower => "pino_str_lower",
    IrRuntimeOp.StrUpper => "pino_str_upper",
    IrRuntimeOp.StrTrim => "pino_str_trim",
    IrRuntimeOp.StrTrimStart => "pino_str_trim_start",
    IrRuntimeOp.StrTrimEnd => "pino_str_trim_end",
    IrRuntimeOp.StrClone => "pino_str_to_owned",
    IrRuntimeOp.StrToCString => "pino_str_to_cstring",
    IrRuntimeOp.StrContains => "pino_str_contains",
    IrRuntimeOp.StrStartsWith => "pino_str_starts_with",
    IrRuntimeOp.StrEndsWith => "pino_str_ends_with",
    IrRuntimeOp.StrIndexOf => "pino_str_index_of",
    IrRuntimeOp.StrLastIndexOf => "pino_str_last_index_of",
    IrRuntimeOp.StrSplit => "pino_str_split",
    IrRuntimeOp.StrReplace => "pino_str_replace",
    IrRuntimeOp.StrSubstring => "pino_str_substring",
    IrRuntimeOp.RegexPattern => "pino_regex_pattern",
    IrRuntimeOp.RegexHasMatch => "pino_regex_has_match",
    IrRuntimeOp.RegexMatchPrefix => "pino_regex_match_prefix",
    IrRuntimeOp.RegexFind => "pino_regex_find",
    IrRuntimeOp.RegexFindAll => "pino_regex_find_all",
    IrRuntimeOp.RegexReplace => "pino_regex_replace",
    _ => op.ToString(),
  };

  private static string FormatFloat(double value) {
    if (double.IsNaN(value)) {
      return "NaN";
    }

    if (double.IsPositiveInfinity(value)) {
      return "Inf";
    }

    if (double.IsNegativeInfinity(value)) {
      return "-Inf";
    }

    string text = value.ToString(CultureInfo.InvariantCulture);
    if (!text.Contains('.', StringComparison.Ordinal)
        && !text.Contains('e', StringComparison.OrdinalIgnoreCase)) {
      return text + ".0";
    }

    return text;
  }

  private static void AppendQuotedString(StringBuilder sb, string value) {
    sb.Append('"');
    foreach (char ch in value) {
      switch (ch) {
        case '\\':
          sb.Append("\\\\");
          break;
        case '"':
          sb.Append("\\\"");
          break;
        case '\n':
          sb.Append("\\n");
          break;
        case '\r':
          sb.Append("\\r");
          break;
        case '\t':
          sb.Append("\\t");
          break;
        default:
          sb.Append(ch);
          break;
      }
    }

    sb.Append('"');
  }
}
