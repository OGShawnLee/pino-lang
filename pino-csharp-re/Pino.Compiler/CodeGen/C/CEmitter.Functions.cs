using Pino.Compiler.IR;
using Pino.Compiler.Text;

namespace Pino.Compiler.CodeGen.C;

public sealed partial class CEmitter {
  // ---------------------------------------------------------------------------
  // VTables, Globals, Prototypes, Closure Thunks, Functions, and main()
  // ---------------------------------------------------------------------------

  private void EmitVTableDefinitions() {
    if (_module.VTables.Count == 0) {
      return;
    }

    _sb.Append("/* Interface VTable helper prototypes & static tables */\n");
    for (int i = 0; i < _module.VTables.Count; i++) {
      IrVTable vt = _module.VTables[i];
      var iface = (IrInterfaceType) _types.GetType(vt.InterfaceType);

      _sb.Append("static void pvt_drop_").Append(i).Append("(PinoAlloc* a, void* raw);\n");
      _sb.Append("static PinoIfaceBox* pvt_clone_").Append(i).Append("(PinoAlloc* a, const PinoIfaceBox* src);\n");
      _sb.Append("static bool pvt_eq_").Append(i).Append("(const PinoIfaceBox* a, const PinoIfaceBox* b);\n");
      _sb.Append("static PinoString pvt_tostr_").Append(i).Append("(PinoAlloc* a, const PinoIfaceBox* src);\n");

      for (int m = 0; m < iface.Methods.Length; m++) {
        IrInterfaceMethod method = iface.Methods[m];
        _sb.Append("static ").Append(CType(method.ReturnType)).Append(" pvt_m_").Append(i).Append('_').Append(m)
            .Append("(PinoIfaceBox* _box");
        for (int p = 0; p < method.Parameters.Length; p++) {
          IrClosureParam cp = method.Parameters[p];
          _sb.Append(", ").Append(CParamType(cp.Type, cp.Ownership)).Append(" a").Append(p);
        }
        _sb.Append(");\n");
      }

      for (int p = 0; p < iface.Properties.Length; p++) {
        _sb.Append("static void* pvt_prop_").Append(i).Append('_').Append(p).Append("(PinoIfaceBox* _box);\n");
      }

      string typeName = vt.TypeName ?? _types.GetType(vt.ConcreteStructType).Name;
      _sb.Append("static const ").Append(VTableStructCName(vt.InterfaceType)).Append(' ')
          .Append(VTableGlobalCName(vt.Id)).Append(" = {\n");
      _sb.Append("  \"").Append(EscapeCString(typeName)).Append("\",\n");
      _sb.Append("  pvt_drop_").Append(i).Append(",\n");
      _sb.Append("  pvt_clone_").Append(i).Append(",\n");
      _sb.Append("  pvt_eq_").Append(i).Append(",\n");
      _sb.Append("  pvt_tostr_").Append(i);
      for (int m = 0; m < iface.Methods.Length; m++) {
        _sb.Append(",\n  pvt_m_").Append(i).Append('_').Append(m);
      }
      for (int p = 0; p < iface.Properties.Length; p++) {
        _sb.Append(",\n  pvt_prop_").Append(i).Append('_').Append(p);
      }
      _sb.Append("\n};\n\n");
    }
  }

  private void EmitVTableHelperBodies() {
    if (_module.VTables.Count == 0) {
      return;
    }

    _sb.Append("/* Interface VTable thunk implementations */\n");
    for (int i = 0; i < _module.VTables.Count; i++) {
      IrVTable vt = _module.VTables[i];
      var iface = (IrInterfaceType) _types.GetType(vt.InterfaceType);
      var concreteStruct = (IrStructType) _types.GetType(vt.ConcreteStructType);
      string boxName = IfaceBoxCName(vt.ConcreteStructType);
      bool structRc = concreteStruct.IsRcManaged;

      // drop(a, raw)
      _sb.Append("static void pvt_drop_").Append(i).Append("(PinoAlloc* a, void* raw) {\n");
      _sb.Append("  ").Append(boxName).Append("* b = (").Append(boxName).Append("*)raw;\n");
      if (structRc) {
        _sb.Append("  ").Append(DropFnName(vt.ConcreteStructType)).Append("(b->value);\n");
      }
      _sb.Append("  pino_dealloc(a, b, sizeof(").Append(boxName).Append("));\n");
      _sb.Append("}\n\n");

      // clone(a, src)
      _sb.Append("static PinoIfaceBox* pvt_clone_").Append(i).Append("(PinoAlloc* a, const PinoIfaceBox* src) {\n");
      _sb.Append("  const ").Append(boxName).Append("* s = (const ").Append(boxName).Append("*)src;\n");
      _sb.Append("  ").Append(boxName).Append("* c = (").Append(boxName).Append("*)PINO_RC_ALLOC(a, sizeof(")
          .Append(boxName).Append("), ").Append(vt.ConcreteStructType.Index).Append(", 0, \"interface box\");\n");
      _sb.Append("  c->value = s->value;\n");
      if (structRc) {
        _sb.Append("  ").Append(IncFnName(vt.ConcreteStructType)).Append("(c->value);\n");
      }
      _sb.Append("  return &c->base;\n");
      _sb.Append("}\n\n");

      // equals(a, b)
      _sb.Append("static bool pvt_eq_").Append(i).Append("(const PinoIfaceBox* a, const PinoIfaceBox* b) {\n");
      _sb.Append("  const ").Append(boxName).Append("* ba = (const ").Append(boxName).Append("*)a;\n");
      _sb.Append("  const ").Append(boxName).Append("* bb = (const ").Append(boxName).Append("*)b;\n");
      _sb.Append("  return ").Append(EqFnName(vt.ConcreteStructType)).Append("(ba->value, bb->value);\n");
      _sb.Append("}\n\n");

      // to_string(a, src)
      _sb.Append("static PinoString pvt_tostr_").Append(i).Append("(PinoAlloc* a, const PinoIfaceBox* src) {\n");
      _sb.Append("  (void)a;\n");
      _sb.Append("  const ").Append(boxName).Append("* s = (const ").Append(boxName).Append("*)src;\n");
      _sb.Append("  return ").Append(StrFnName(vt.ConcreteStructType)).Append("(s->value);\n");
      _sb.Append("}\n\n");

      // method thunks
      for (int m = 0; m < iface.Methods.Length; m++) {
        IrInterfaceMethod method = iface.Methods[m];
        IrFuncId thunkId = vt.MethodFuncIds[m];
        IrFunction thunkFn = _module.Functions[thunkId.Index];
        string retCType = CType(method.ReturnType);
        bool isVoid = method.ReturnType == _types.Void || method.ReturnType == _types.Never;

        _sb.Append("static ").Append(retCType).Append(" pvt_m_").Append(i).Append('_').Append(m)
            .Append("(PinoIfaceBox* _box");
        for (int p = 0; p < method.Parameters.Length; p++) {
          IrClosureParam cp = method.Parameters[p];
          _sb.Append(", ").Append(CParamType(cp.Type, cp.Ownership)).Append(" a").Append(p);
        }
        _sb.Append(") {\n");
        _sb.Append("  ").Append(boxName).Append("* b = (").Append(boxName).Append("*)_box;\n");

        if (method.IsMutating) {
          _sb.Append("  ").Append(isVoid ? string.Empty : "return ").Append(FuncCName(thunkId)).Append("(&b->value");
        } else {
          // Non-mutating CallIface borrows receiver; pass an rc_inc'd @owned copy of the boxed struct!
          _sb.Append("  ").Append(CType(vt.ConcreteStructType)).Append(" _self_copy = b->value;\n");
          if (structRc) {
            _sb.Append("  ").Append(IncFnName(vt.ConcreteStructType)).Append("(_self_copy);\n");
          }
          bool selfByConstPtr = IsPassByConstPtr(thunkFn.Parameters[0].Type, thunkFn.Parameters[0].Ownership);
          _sb.Append("  ").Append(isVoid ? string.Empty : "return ").Append(FuncCName(thunkId))
              .Append(selfByConstPtr ? "(&_self_copy" : "(_self_copy");
        }

        for (int p = 0; p < method.Parameters.Length; p++) {
          _sb.Append(", a").Append(p);
        }
        _sb.Append(");\n");
        _sb.Append("}\n\n");
      }

      // property address thunks (supports both direct fields and promoted embedded struct fields)
      for (int p = 0; p < iface.Properties.Length; p++) {
        IrInterfaceProperty prop = iface.Properties[p];
        _sb.Append("static void* pvt_prop_").Append(i).Append('_').Append(p).Append("(PinoIfaceBox* _box) {\n");
        _sb.Append("  ").Append(boxName).Append("* b = (").Append(boxName).Append("*)_box;\n");
        string accessPath = BuildFieldAccessPath(concreteStruct, prop.Name);
        _sb.Append("  return (void*)&b->value.").Append(accessPath).Append(";\n");
        _sb.Append("}\n\n");
      }
    }
  }

  private string BuildFieldAccessPath(IrStructType rootStruct, string propName) {
    // Direct field lookup first.
    for (int f = 0; f < rootStruct.Fields.Length; f++) {
      if (string.Equals(rootStruct.Fields[f].Name, propName, StringComparison.Ordinal)) {
        return FieldCName(f, rootStruct.Fields[f].Name);
      }
    }

    // BFS/DFS through embedded struct fields (promoted fields per STRUCT-8).
    var queue = new Queue<(IrStructType St, string Prefix)>();
    queue.Enqueue((rootStruct, string.Empty));
    var visited = new HashSet<IrTypeId> { rootStruct.Id };
    while (queue.Count > 0) {
      (IrStructType cur, string prefix) = queue.Dequeue();
      for (int f = 0; f < cur.Fields.Length; f++) {
        IrStructField fld = cur.Fields[f];
        string curPath = string.IsNullOrEmpty(prefix)
            ? FieldCName(f, fld.Name)
            : $"{prefix}.{FieldCName(f, fld.Name)}";
        if (string.Equals(fld.Name, propName, StringComparison.Ordinal)) {
          return curPath;
        }

        if (_types.GetType(fld.Type) is IrStructType sub && visited.Add(sub.Id)) {
          queue.Enqueue((sub, curPath));
        }
      }
    }

    throw new InvalidOperationException(
        $"Property '{propName}' not found on concrete struct '{rootStruct.Name}'.");
  }

  private void EmitGlobals() {
    if (_module.Globals.Count == 0) {
      return;
    }

    _sb.Append("/* Module globals */\n");
    for (int i = 0; i < _module.Globals.Count; i++) {
      IrGlobal g = _module.Globals[i];
      _sb.Append("static ").Append(CType(g.Type)).Append(' ').Append(GlobalCName(g.Id)).Append(";\n");
    }

    _sb.Append('\n');
  }

  private void EmitFunctionPrototypes() {
    _sb.Append("/* Function prototypes */\n");
    foreach (IrFunction fn in _module.Functions) {
      EmitFunctionSignature(fn);
      _sb.Append(";\n");
    }

    _sb.Append('\n');
  }

  private void EmitFunctionSignature(IrFunction fn) {
    string retCType = CType(fn.ReturnType);
    _sb.Append("static ").Append(retCType).Append(' ').Append(FuncCName(fn.Id)).Append('(');
    if (fn.Parameters.IsEmpty) {
      _sb.Append("void");
    } else {
      for (int p = 0; p < fn.Parameters.Length; p++) {
        if (p > 0) {
          _sb.Append(", ");
        }

        IrParam param = fn.Parameters[p];
        _sb.Append(CParamType(param.Type, param.Ownership)).Append(' ').Append(ParamCName(fn, p));
      }
    }

    _sb.Append(')');
  }

  private void EmitClosureThunks() {
    if (_orderedClosureThunks.Count == 0) {
      return;
    }

    _sb.Append("/* Closure uniform-ABI thunks */\n");
    foreach ((IrFuncId funcId, IrTypeId closTypeId, string thunkName) in _orderedClosureThunks) {
      IrFunction targetFn = _module.Functions[funcId.Index];
      var closType = (IrClosureType) _types.GetType(closTypeId);
      bool hasEnv = targetFn.Parameters.Length == closType.Parameters.Length + 1
          && _types.GetType(targetFn.Parameters[0].Type).Kind == IrTypeKind.Env;
      bool isVoid = closType.ReturnType == _types.Void || closType.ReturnType == _types.Never;

      _sb.Append("static ").Append(CType(closType.ReturnType)).Append(' ').Append(thunkName)
          .Append("(PinoEnvHdr* _env");
      for (int p = 0; p < closType.Parameters.Length; p++) {
        IrClosureParam cp = closType.Parameters[p];
        _sb.Append(", ").Append(CParamType(cp.Type, cp.Ownership)).Append(" a").Append(p);
      }
      _sb.Append(") {\n");
      if (!hasEnv) {
        _sb.Append("  (void)_env;\n");
      }

      _sb.Append("  ").Append(isVoid ? string.Empty : "return ").Append(FuncCName(funcId)).Append('(');
      bool needComma = false;
      if (hasEnv) {
        _sb.Append('(').Append(CType(targetFn.Parameters[0].Type)).Append(")_env");
        needComma = true;
      }

      for (int p = 0; p < closType.Parameters.Length; p++) {
        if (needComma) {
          _sb.Append(", ");
        }

        _sb.Append('a').Append(p);
        needComma = true;
      }

      _sb.Append(");\n");
      _sb.Append("}\n\n");
    }
  }

  // ---------------------------------------------------------------------------
  // Function bodies & 1-to-1 instruction/terminator emission
  // ---------------------------------------------------------------------------

  private void EmitFunctionBodies() {
    _sb.Append("/* Function bodies */\n");
    foreach (IrFunction fn in _module.Functions) {
      EmitFunctionBody(fn);
    }
  }

  private void EmitFunctionBody(IrFunction fn) {
    SourceText? source = ResolveFunctionSource(fn);
    bool pushFrame = _options.EmitFrames
        && !fn.Name.StartsWith("__", StringComparison.Ordinal);

    EmitFunctionSignature(fn);
    _sb.Append(" {\n");

    // Suppress unused-parameter warnings for GCC -Wall.
    for (int p = 0; p < fn.Parameters.Length; p++) {
      _sb.Append("  (void)").Append(ParamCName(fn, p)).Append(";\n");
    }

    if (pushFrame) {
      _sb.Append("  PINO_FRAME_PUSH(\"").Append(EscapeCString(fn.Name)).Append("\");\n");
    }

    // Declare all local slots (zero-initialized).
    for (int s = 0; s < fn.Slots.Count; s++) {
      IrLocalSlot slot = fn.Slots[s];
      IrType st = _types.GetType(slot.Type);
      if (st.Kind is IrTypeKind.Void or IrTypeKind.Never) {
        continue;
      }

      string sName = SlotCName(fn, slot.Id);
      _sb.Append("  ").Append(CType(slot.Type)).Append(' ').Append(sName)
          .Append(" = ").Append(ZeroInitFor(slot.Type)).Append(";\n");
      _sb.Append("  (void)").Append(sName).Append(";\n");
    }

    // Declare all single-def temps (zero-initialized).
    for (int t = 0; t < fn.Temps.Count; t++) {
      IrTemp tmp = fn.Temps[t];
      IrType tt = _types.GetType(tmp.Type);
      if (tt.Kind is IrTypeKind.Void or IrTypeKind.Never) {
        continue;
      }

      string tName = TempCName(tmp.Id);
      _sb.Append("  ").Append(CType(tmp.Type)).Append(' ').Append(tName)
          .Append(" = ").Append(ZeroInitFor(tmp.Type)).Append(";\n");
      _sb.Append("  (void)").Append(tName).Append(";\n");
    }

    int lastLine = -1;
    string? lastFile = null;

    for (int b = 0; b < fn.Blocks.Count; b++) {
      IrBasicBlock bb = fn.Blocks[b];
      _sb.Append(BlockLabel(bb.Id)).Append(":;\n");

      foreach (IrInstruction instr in bb.Instructions) {
        EmitLineDirectiveIfNeeded(source, instr.Span, ref lastLine, ref lastFile);
        EmitInstruction(fn, instr, source);
      }

      if (bb.Terminator is { } term) {
        EmitLineDirectiveIfNeeded(source, term.Span, ref lastLine, ref lastFile);
        EmitTerminator(fn, term, source, pushFrame);
      } else {
        _sb.Append("  pino_panic_cstr_at(\"unreachable\", ").Append(FormatPinoLoc(source, default)).Append(");\n");
      }
    }

    _sb.Append("}\n\n");
  }

  private string OperandExpr(IrFunction fn, IrOperand op) {
    if (op.IsTemp) {
      return TempCName(op.TempId);
    }

    IrParam param = fn.Parameters[op.Index];
    string name = ParamCName(fn, op.Index);
    return IsPassByConstPtr(param.Type, param.Ownership) ? $"(*{name})" : name;
  }

  private string OperandArgForParam(IrFunction fn, IrOperand op, IrTypeId paramTypeId, IrParamOwnership ownership) {
    if (!IsPassByConstPtr(paramTypeId, ownership)) {
      return OperandExpr(fn, op);
    }

    if (op.IsParam) {
      IrParam p = fn.Parameters[op.Index];
      if (IsPassByConstPtr(p.Type, p.Ownership)) {
        return ParamCName(fn, op.Index);
      }

      return $"&{ParamCName(fn, op.Index)}";
    }

    return $"&{TempCName(op.TempId)}";
  }

  private IrTypeId GetOperandType(IrFunction fn, IrOperand op) =>
      op.IsTemp ? fn.Temps[op.Index].Type : fn.Parameters[op.Index].Type;

  private void EmitInstruction(IrFunction fn, IrInstruction instr, SourceText? source) {
    switch (instr) {
      // -----------------------------------------------------------------------
      // Constants & moves
      // -----------------------------------------------------------------------
      case ConstInt ci:
        _sb.Append("  ").Append(TempCName(ci.Dest)).Append(" = ").Append(FormatCInt64Literal(ci.Value)).Append(";\n");
        break;

      case ConstFloat cf:
        _sb.Append("  ").Append(TempCName(cf.Dest)).Append(" = ").Append(FormatCDoubleLiteral(cf.Value)).Append(";\n");
        break;

      case ConstBool cb:
        _sb.Append("  ").Append(TempCName(cb.Dest)).Append(" = ").Append(cb.Value ? "1u" : "0u").Append(";\n");
        break;

      case ConstRune cr:
        _sb.Append("  ").Append(TempCName(cr.Dest)).Append(" = ").Append(cr.Value).Append(";\n");
        break;

      case ConstString cs:
        _sb.Append("  ").Append(TempCName(cs.Dest)).Append(" = ")
            .Append(FormatPinoStringPoolExpr(_module, cs.StringPoolId)).Append(";\n");
        break;

      case ConstEnum ce:
        _sb.Append("  ").Append(TempCName(ce.Dest)).Append(" = ").Append(ce.Ordinal).Append(";\n");
        break;

      case Copy cp:
        _sb.Append("  ").Append(TempCName(cp.Dest)).Append(" = ").Append(OperandExpr(fn, cp.Source)).Append(";\n");
        break;

      case Move mv:
        _sb.Append("  ").Append(TempCName(mv.Dest)).Append(" = ").Append(OperandExpr(fn, mv.Source)).Append(";\n");
        break;

      // -----------------------------------------------------------------------
      // Locals & places
      // -----------------------------------------------------------------------
      case LoadSlot ls:
        _sb.Append("  ").Append(TempCName(ls.Dest)).Append(" = ").Append(SlotCName(fn, ls.Slot)).Append(";\n");
        break;

      case StoreSlot ss:
        _sb.Append("  ").Append(SlotCName(fn, ss.Slot)).Append(" = ").Append(OperandExpr(fn, ss.Value)).Append(";\n");
        break;

      case AddrLocal al:
        _sb.Append("  ").Append(TempCName(al.Dest)).Append(" = &").Append(SlotCName(fn, al.Slot)).Append(";\n");
        break;

      case AddrGlobal ag:
        _sb.Append("  ").Append(TempCName(ag.Dest)).Append(" = &").Append(GlobalCName(ag.Global)).Append(";\n");
        break;

      case FieldGet fg: {
          var st = (IrStructType) _types.GetType(GetOperandType(fn, fg.Target));
          string fName = FieldCName(fg.FieldIndex, st.Fields[fg.FieldIndex].Name);
          _sb.Append("  ").Append(TempCName(fg.Dest)).Append(" = ").Append(OperandExpr(fn, fg.Target))
              .Append('.').Append(fName).Append(";\n");
          break;
        }

      case FieldAddr fa: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, fa.TargetPtr));
          var st = (IrStructType) _types.GetType(ptrT.TargetType);
          string fName = FieldCName(fa.FieldIndex, st.Fields[fa.FieldIndex].Name);
          _sb.Append("  ").Append(TempCName(fa.Dest)).Append(" = &(").Append(OperandExpr(fn, fa.TargetPtr))
              .Append("->").Append(fName).Append(");\n");
          break;
        }

      case LoadPtr lp:
        _sb.Append("  ").Append(TempCName(lp.Dest)).Append(" = *(").Append(OperandExpr(fn, lp.Ptr)).Append(");\n");
        break;

      case StorePtr sp:
        _sb.Append("  *(").Append(OperandExpr(fn, sp.Ptr)).Append(") = ").Append(OperandExpr(fn, sp.Value)).Append(";\n");
        break;

      case PtrEq pe:
        _sb.Append("  ").Append(TempCName(pe.Dest)).Append(" = (uint8_t)((const void*)(")
            .Append(OperandExpr(fn, pe.LeftPtr)).Append(") == (const void*)(")
            .Append(OperandExpr(fn, pe.RightPtr)).Append("));\n");
        break;

      // -----------------------------------------------------------------------
      // Arithmetic, casts & comparisons
      // -----------------------------------------------------------------------
      case Add add: {
          IrTypeId tId = fn.Temps[add.Dest.Index].Type;
          string l = OperandExpr(fn, add.Left);
          string r = OperandExpr(fn, add.Right);
          if (tId == _types.Int) {
            _sb.Append("  ").Append(TempCName(add.Dest)).Append(" = (int64_t)((uint64_t)").Append(l)
                .Append(" + (uint64_t)").Append(r).Append(");\n");
          } else if (tId == _types.Rune) {
            _sb.Append("  ").Append(TempCName(add.Dest)).Append(" = (int32_t)((uint32_t)").Append(l)
                .Append(" + (uint32_t)").Append(r).Append(");\n");
          } else {
            _sb.Append("  ").Append(TempCName(add.Dest)).Append(" = ").Append(l).Append(" + ").Append(r).Append(";\n");
          }
          break;
        }

      case Sub sub: {
          IrTypeId tId = fn.Temps[sub.Dest.Index].Type;
          string l = OperandExpr(fn, sub.Left);
          string r = OperandExpr(fn, sub.Right);
          if (tId == _types.Int) {
            _sb.Append("  ").Append(TempCName(sub.Dest)).Append(" = (int64_t)((uint64_t)").Append(l)
                .Append(" - (uint64_t)").Append(r).Append(");\n");
          } else if (tId == _types.Rune) {
            _sb.Append("  ").Append(TempCName(sub.Dest)).Append(" = (int32_t)((uint32_t)").Append(l)
                .Append(" - (uint32_t)").Append(r).Append(");\n");
          } else {
            _sb.Append("  ").Append(TempCName(sub.Dest)).Append(" = ").Append(l).Append(" - ").Append(r).Append(";\n");
          }
          break;
        }

      case Mul mul: {
          IrTypeId tId = fn.Temps[mul.Dest.Index].Type;
          string l = OperandExpr(fn, mul.Left);
          string r = OperandExpr(fn, mul.Right);
          if (tId == _types.Int) {
            _sb.Append("  ").Append(TempCName(mul.Dest)).Append(" = (int64_t)((uint64_t)").Append(l)
                .Append(" * (uint64_t)").Append(r).Append(");\n");
          } else {
            _sb.Append("  ").Append(TempCName(mul.Dest)).Append(" = ").Append(l).Append(" * ").Append(r).Append(";\n");
          }
          break;
        }

      case DivF64 div:
        _sb.Append("  ").Append(TempCName(div.Dest)).Append(" = pino_div_f64(")
            .Append(OperandExpr(fn, div.Left)).Append(", ").Append(OperandExpr(fn, div.Right))
            .Append(", ").Append(FormatPinoLoc(source, div.Span)).Append(");\n");
        break;

      case FloorDiv fdiv: {
          IrTypeId tId = fn.Temps[fdiv.Dest.Index].Type;
          string helper = tId == _types.Float ? "pino_floordiv_f64" : "pino_floordiv_i64";
          _sb.Append("  ").Append(TempCName(fdiv.Dest)).Append(" = ").Append(helper).Append('(')
              .Append(OperandExpr(fn, fdiv.Left)).Append(", ").Append(OperandExpr(fn, fdiv.Right))
              .Append(", ").Append(FormatPinoLoc(source, fdiv.Span)).Append(");\n");
          break;
        }

      case FloorMod fmod: {
          IrTypeId tId = fn.Temps[fmod.Dest.Index].Type;
          string helper = tId == _types.Float ? "pino_floormod_f64" : "pino_floormod_i64";
          _sb.Append("  ").Append(TempCName(fmod.Dest)).Append(" = ").Append(helper).Append('(')
              .Append(OperandExpr(fn, fmod.Left)).Append(", ").Append(OperandExpr(fn, fmod.Right))
              .Append(", ").Append(FormatPinoLoc(source, fmod.Span)).Append(");\n");
          break;
        }

      case Neg neg: {
          IrTypeId tId = fn.Temps[neg.Dest.Index].Type;
          string v = OperandExpr(fn, neg.Operand);
          if (tId == _types.Int) {
            _sb.Append("  ").Append(TempCName(neg.Dest)).Append(" = (int64_t)(0ull - (uint64_t)").Append(v).Append(");\n");
          } else {
            _sb.Append("  ").Append(TempCName(neg.Dest)).Append(" = -(").Append(v).Append(");\n");
          }
          break;
        }

      case Not not:
        _sb.Append("  ").Append(TempCName(not.Dest)).Append(" = (uint8_t)(!(").Append(OperandExpr(fn, not.Operand)).Append("));\n");
        break;

      case Cmp cmp: {
          IrType leftT = _types.GetType(GetOperandType(fn, cmp.Left));
          string l = OperandExpr(fn, cmp.Left);
          string r = OperandExpr(fn, cmp.Right);
          if (leftT.Kind == IrTypeKind.Handle) {
            if (cmp.Op == IrCmpOp.Eq) {
              _sb.Append("  ").Append(TempCName(cmp.Dest)).Append(" = (uint8_t)(")
                  .Append(l).Append(".index == ").Append(r).Append(".index && ")
                  .Append(l).Append(".gen == ").Append(r).Append(".gen);\n");
            } else {
              _sb.Append("  ").Append(TempCName(cmp.Dest)).Append(" = (uint8_t)(")
                  .Append(l).Append(".index != ").Append(r).Append(".index || ")
                  .Append(l).Append(".gen != ").Append(r).Append(".gen);\n");
            }
          } else {
            string op = cmp.Op switch {
              IrCmpOp.Eq => "==",
              IrCmpOp.Ne => "!=",
              IrCmpOp.Lt => "<",
              IrCmpOp.Le => "<=",
              IrCmpOp.Gt => ">",
              IrCmpOp.Ge => ">=",
              _ => "==",
            };
            _sb.Append("  ").Append(TempCName(cmp.Dest)).Append(" = (uint8_t)(")
                .Append(l).Append(' ').Append(op).Append(' ').Append(r).Append(");\n");
          }
          break;
        }

      case CastI64ToF64 c:
        _sb.Append("  ").Append(TempCName(c.Dest)).Append(" = (double)(").Append(OperandExpr(fn, c.Source)).Append(");\n");
        break;

      case CastF64ToI64 c:
        _sb.Append("  ").Append(TempCName(c.Dest)).Append(" = (int64_t)(").Append(OperandExpr(fn, c.Source)).Append(");\n");
        break;

      case CastI64ToRune c:
        _sb.Append("  ").Append(TempCName(c.Dest)).Append(" = (int32_t)(").Append(OperandExpr(fn, c.Source)).Append(");\n");
        break;

      case CastRuneToI64 c:
        _sb.Append("  ").Append(TempCName(c.Dest)).Append(" = (int64_t)(").Append(OperandExpr(fn, c.Source)).Append(");\n");
        break;

      case StructEq seq: {
          IrTypeId tId = GetOperandType(fn, seq.Left);
          _sb.Append("  ").Append(TempCName(seq.Dest)).Append(" = (uint8_t)").Append(EqFnName(tId)).Append('(')
              .Append(OperandExpr(fn, seq.Left)).Append(", ").Append(OperandExpr(fn, seq.Right)).Append(");\n");
          break;
        }

      // -----------------------------------------------------------------------
      // Aggregates
      // -----------------------------------------------------------------------
      case StructNew sn: {
          IrTypeId stId = fn.Temps[sn.Dest.Index].Type;
          var st = (IrStructType) _types.GetType(stId);
          string cType = CType(stId);
          if (st.Fields.IsEmpty) {
            _sb.Append("  ").Append(TempCName(sn.Dest)).Append(" = (").Append(cType).Append("){0};\n");
          } else {
            _sb.Append("  ").Append(TempCName(sn.Dest)).Append(" = (").Append(cType).Append("){");
            for (int f = 0; f < sn.Fields.Length; f++) {
              if (f > 0) _sb.Append(", ");
              _sb.Append('.').Append(FieldCName(f, st.Fields[f].Name)).Append(" = ").Append(OperandExpr(fn, sn.Fields[f]));
            }
            _sb.Append("};\n");
          }
          break;
        }

      case TupleNew tn: {
          IrTypeId ttId = fn.Temps[tn.Dest.Index].Type;
          var tt = (IrTupleType) _types.GetType(ttId);
          string cType = CType(ttId);
          if (tt.Fields.IsEmpty) {
            _sb.Append("  ").Append(TempCName(tn.Dest)).Append(" = (").Append(cType).Append("){0};\n");
          } else {
            _sb.Append("  ").Append(TempCName(tn.Dest)).Append(" = (").Append(cType).Append("){");
            for (int f = 0; f < tn.Fields.Length; f++) {
              if (f > 0) _sb.Append(", ");
              _sb.Append('.').Append(FieldCName(f, tt.Fields[f].Label)).Append(" = ").Append(OperandExpr(fn, tn.Fields[f]));
            }
            _sb.Append("};\n");
          }
          break;
        }

      case TupleField tf: {
          var tt = (IrTupleType) _types.GetType(GetOperandType(fn, tf.Tuple));
          string fName = FieldCName(tf.FieldIndex, tt.Fields[tf.FieldIndex].Label);
          _sb.Append("  ").Append(TempCName(tf.Dest)).Append(" = ").Append(OperandExpr(fn, tf.Tuple))
              .Append('.').Append(fName).Append(";\n");
          break;
        }

      case UnionNew un: {
          IrTypeId utId = fn.Temps[un.Dest.Index].Type;
          var ut = (IrUnionType) _types.GetType(utId);
          IrUnionVariant variant = ut.Variants[un.VariantIndex];
          if (ut.IsRecursiveUnion) {
            if (!HasRuntimeFields(variant)) {
              _sb.Append("  ").Append(TempCName(un.Dest)).Append(" = &")
                  .Append(RecursiveUnionSingletonCName(utId, variant.TagOrdinal)).Append(";\n");
            } else {
              string uStruct = UnionCName(utId);
              string flags = ut.IsCycleCapable ? "PINO_FLAG_CYCLE_CANDIDATE" : "0";
              _sb.Append("  {\n");
              _sb.Append("    ").Append(uStruct).Append("* _u = (").Append(uStruct)
                  .Append("*)PINO_RC_ALLOC(pino_cur_alloc, sizeof(").Append(uStruct).Append("), ")
                  .Append(utId.Index).Append(", ").Append(flags).Append(", \"union\");\n");
              _sb.Append("    _u->tag = ").Append(variant.TagOrdinal).Append(";\n");
              for (int f = 0; f < un.Fields.Length; f++) {
                if (IsVoidOrNever(variant.Fields[f].Type)) {
                  continue;
                }
                _sb.Append("    _u->u.v").Append(variant.TagOrdinal).Append(".f").Append(f)
                    .Append(" = ").Append(OperandExpr(fn, un.Fields[f])).Append(";\n");
              }
              _sb.Append("    ").Append(TempCName(un.Dest)).Append(" = _u;\n");
              _sb.Append("  }\n");
            }
          } else {
            string uStruct = UnionCName(utId);
            _sb.Append("  {\n");
            _sb.Append("    ").Append(uStruct).Append(" _u = {0};\n");
            _sb.Append("    _u.tag = ").Append(variant.TagOrdinal).Append(";\n");
            for (int f = 0; f < un.Fields.Length; f++) {
              if (IsVoidOrNever(variant.Fields[f].Type)) {
                continue;
              }
              _sb.Append("    _u.u.v").Append(variant.TagOrdinal).Append(".f").Append(f)
                  .Append(" = ").Append(OperandExpr(fn, un.Fields[f])).Append(";\n");
            }
            _sb.Append("    ").Append(TempCName(un.Dest)).Append(" = _u;\n");
            _sb.Append("  }\n");
          }
          break;
        }

      case UnionTag utag: {
          var ut = (IrUnionType) _types.GetType(GetOperandType(fn, utag.Union));
          string acc = ut.IsRecursiveUnion ? "->" : ".";
          _sb.Append("  ").Append(TempCName(utag.Dest)).Append(" = (int64_t)(")
              .Append(OperandExpr(fn, utag.Union)).Append(acc).Append("tag);\n");
          break;
        }

      case UnionField uf: {
          if (IsVoidOrNever(fn.Temps[uf.Dest.Index].Type)) {
            break;
          }
          var ut = (IrUnionType) _types.GetType(GetOperandType(fn, uf.Union));
          string acc = ut.IsRecursiveUnion ? "->" : ".";
          int tagOrdinal = ut.Variants[uf.VariantIndex].TagOrdinal;
          _sb.Append("  ").Append(TempCName(uf.Dest)).Append(" = ")
              .Append(OperandExpr(fn, uf.Union)).Append(acc)
              .Append("u.v").Append(tagOrdinal).Append(".f").Append(uf.FieldIndex).Append(";\n");
          break;
        }

      case IfaceBox ib: {
          IrTypeId ifaceId = fn.Temps[ib.Dest.Index].Type;
          IrVTable vt = _module.VTables[ib.VTable.Index];
          string boxName = IfaceBoxCName(vt.ConcreteStructType);
          string flags = _types.GetType(vt.ConcreteStructType).IsCycleCapable ? "PINO_FLAG_CYCLE_CANDIDATE" : "0";
          _sb.Append("  {\n");
          _sb.Append("    ").Append(boxName).Append("* _b = (").Append(boxName)
              .Append("*)PINO_RC_ALLOC(pino_cur_alloc, sizeof(").Append(boxName).Append("), ")
              .Append(vt.ConcreteStructType.Index).Append(", ").Append(flags).Append(", \"interface box\");\n");
          _sb.Append("    _b->value = ").Append(OperandExpr(fn, ib.Value)).Append(";\n");
          _sb.Append("    ").Append(TempCName(ib.Dest)).Append(" = (").Append(CType(ifaceId))
              .Append("){&_b->base, &").Append(VTableGlobalCName(ib.VTable)).Append("};\n");
          _sb.Append("  }\n");
          break;
        }

      case IfaceFieldGet ifg: {
          IrTypeId propTypeId = fn.Temps[ifg.Dest.Index].Type;
          string ifaceExpr = OperandExpr(fn, ifg.Iface);
          _sb.Append("  ").Append(TempCName(ifg.Dest)).Append(" = *(").Append(CType(propTypeId))
              .Append("*)((").Append(ifaceExpr).Append(").vtable->prop_addr_").Append(ifg.PropertyIndex)
              .Append("((").Append(ifaceExpr).Append(").data));\n");
          break;
        }

      case IfaceFieldAddr ifa: {
          string ptrCType = CType(fn.Temps[ifa.Dest.Index].Type);
          string ifacePtrExpr = OperandExpr(fn, ifa.IfacePtr);
          _sb.Append("  ").Append(TempCName(ifa.Dest)).Append(" = (").Append(ptrCType)
              .Append(")((").Append(ifacePtrExpr).Append(")->vtable->prop_addr_").Append(ifa.PropertyIndex)
              .Append("((").Append(ifacePtrExpr).Append(")->data));\n");
          break;
        }

      // -----------------------------------------------------------------------
      // Closures & cells
      // -----------------------------------------------------------------------
      case CellNew cn: {
          IrTypeId cellId = fn.Temps[cn.Dest.Index].Type;
          var ct = (IrCellType) _types.GetType(cellId);
          string cellStruct = CellStructCName(cellId);
          string flags = ct.IsCycleCapable ? "PINO_FLAG_CYCLE_CANDIDATE" : "0";
          _sb.Append("  {\n");
          _sb.Append("    ").Append(cellStruct).Append("* _c = (").Append(cellStruct)
              .Append("*)PINO_RC_ALLOC(pino_cur_alloc, sizeof(").Append(cellStruct).Append("), ")
              .Append(cellId.Index).Append(", ").Append(flags).Append(", \"closure cell\");\n");
          _sb.Append("    _c->value = ").Append(OperandExpr(fn, cn.InitialValue)).Append(";\n");
          _sb.Append("    ").Append(TempCName(cn.Dest)).Append(" = _c;\n");
          _sb.Append("  }\n");
          break;
        }

      case CellGet cg:
        _sb.Append("  ").Append(TempCName(cg.Dest)).Append(" = (").Append(OperandExpr(fn, cg.Cell)).Append(")->value;\n");
        break;

      case CellSet cs:
        _sb.Append("  (").Append(OperandExpr(fn, cs.Cell)).Append(")->value = ").Append(OperandExpr(fn, cs.Value)).Append(";\n");
        break;

      case CellAddr ca:
        _sb.Append("  ").Append(TempCName(ca.Dest)).Append(" = &((").Append(OperandExpr(fn, ca.Cell)).Append(")->value);\n");
        break;

      case EnvNew en: {
          IrTypeId envId = fn.Temps[en.Dest.Index].Type;
          var et = (IrEnvType) _types.GetType(envId);
          string envStruct = EnvStructCName(envId);
          string flags = et.IsCycleCapable ? "PINO_FLAG_CYCLE_CANDIDATE" : "0";
          _sb.Append("  {\n");
          _sb.Append("    ").Append(envStruct).Append("* _e = (").Append(envStruct)
              .Append("*)PINO_RC_ALLOC(pino_cur_alloc, sizeof(").Append(envStruct).Append("), ")
              .Append(envId.Index).Append(", ").Append(flags).Append(", \"closure env\");\n");
          _sb.Append("    _e->hdr.drop = ").Append(EnvDropCallbackCName(envId)).Append(";\n");
          for (int f = 0; f < en.Captures.Length; f++) {
            _sb.Append("    _e->").Append(FieldCName(f, et.Fields[f].Name))
                .Append(" = ").Append(OperandExpr(fn, en.Captures[f])).Append(";\n");
          }
          _sb.Append("    ").Append(TempCName(en.Dest)).Append(" = _e;\n");
          _sb.Append("  }\n");
          break;
        }

      case EnvFieldGet efg: {
          var et = (IrEnvType) _types.GetType(GetOperandType(fn, efg.Env));
          _sb.Append("  ").Append(TempCName(efg.Dest)).Append(" = (").Append(OperandExpr(fn, efg.Env))
              .Append(")->").Append(FieldCName(efg.FieldIndex, et.Fields[efg.FieldIndex].Name)).Append(";\n");
          break;
        }

      case ClosureMake cm: {
          IrTypeId closId = fn.Temps[cm.Dest.Index].Type;
          string thunkName = _closureThunks[(cm.Function, closId)];
          string envExpr = cm.Env is { } envOp ? $"&(({OperandExpr(fn, envOp)})->hdr)" : "NULL";
          _sb.Append("  ").Append(TempCName(cm.Dest)).Append(" = (PinoClosure){(void*)").Append(thunkName)
              .Append(", ").Append(envExpr).Append("};\n");
          break;
        }

      // -----------------------------------------------------------------------
      // Vectors
      // -----------------------------------------------------------------------
      case VecNew vn: {
          IrTypeId vId = fn.Temps[vn.Dest.Index].Type;
          if (vn.Elements.IsEmpty) {
            _sb.Append("  ").Append(TempCName(vn.Dest)).Append(" = &").Append(EmptySingletonCName(vId)).Append(";\n");
          } else {
            int cap = Math.Max(4, vn.Elements.Length);
            _sb.Append("  {\n");
            _sb.Append("    ").Append(CType(vId)).Append(" _v = ").Append(VecNewFnName(vId)).Append('(').Append(cap).Append(");\n");
            for (int e = 0; e < vn.Elements.Length; e++) {
              _sb.Append("    _v->data[").Append(e).Append("] = ").Append(OperandExpr(fn, vn.Elements[e])).Append(";\n");
            }
            _sb.Append("    _v->len = ").Append(vn.Elements.Length).Append(";\n");
            _sb.Append("    ").Append(TempCName(vn.Dest)).Append(" = _v;\n");
            _sb.Append("  }\n");
          }
          break;
        }

      case VecWithCap vwc: {
          IrTypeId vId = fn.Temps[vwc.Dest.Index].Type;
          _sb.Append("  ").Append(TempCName(vwc.Dest)).Append(" = ").Append(VecNewFnName(vId))
              .Append('(').Append(OperandExpr(fn, vwc.Capacity)).Append(");\n");
          break;
        }

      case VecLen vl:
        _sb.Append("  ").Append(TempCName(vl.Dest)).Append(" = (").Append(OperandExpr(fn, vl.Vec)).Append(")->len;\n");
        break;

      case VecCap vc:
        _sb.Append("  ").Append(TempCName(vc.Dest)).Append(" = (").Append(OperandExpr(fn, vc.Vec)).Append(")->cap;\n");
        break;

      case VecGet vg: {
          IrTypeId vId = GetOperandType(fn, vg.Vec);
          if (vg.Unchecked) {
            _sb.Append("  ").Append(TempCName(vg.Dest)).Append(" = (").Append(OperandExpr(fn, vg.Vec))
                .Append(")->data[").Append(OperandExpr(fn, vg.Index)).Append("];\n");
          } else {
            _sb.Append("  ").Append(TempCName(vg.Dest)).Append(" = ").Append(VecGetFnName(vId)).Append('(')
                .Append(OperandExpr(fn, vg.Vec)).Append(", ").Append(OperandExpr(fn, vg.Index))
                .Append(", ").Append(FormatPinoLoc(source, vg.Span)).Append(");\n");
          }
          break;
        }

      case VecElemAddr vea: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, vea.VecPtr));
          _sb.Append("  ").Append(TempCName(vea.Dest)).Append(" = ").Append(VecElemAddrFnName(ptrT.TargetType)).Append('(')
              .Append(OperandExpr(fn, vea.VecPtr)).Append(", ").Append(OperandExpr(fn, vea.Index))
              .Append(", ").Append(FormatPinoLoc(source, vea.Span)).Append(");\n");
          break;
        }

      case VecPush vp: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, vp.VecPtr));
          _sb.Append("  ").Append(VecPushFnName(ptrT.TargetType)).Append('(')
              .Append(OperandExpr(fn, vp.VecPtr)).Append(", ").Append(OperandExpr(fn, vp.Value)).Append(");\n");
          break;
        }

      case VecPop vpop: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, vpop.VecPtr));
          var vt = (IrVectorType) _types.GetType(ptrT.TargetType);
          EmitOptionOutCall(
              fn,
              vpop.Dest,
              vt.ElementType,
              $"{VecPopFnName(vt.Id)}({OperandExpr(fn, vpop.VecPtr)}, &_opt_val)");
          break;
        }

      case VecSwap vswap: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, vswap.VecPtr));
          _sb.Append("  ").Append(VecSwapFnName(ptrT.TargetType)).Append('(')
              .Append(OperandExpr(fn, vswap.VecPtr)).Append(", ")
              .Append(OperandExpr(fn, vswap.IndexA)).Append(", ")
              .Append(OperandExpr(fn, vswap.IndexB)).Append(", ")
              .Append(FormatPinoLoc(source, vswap.Span)).Append(");\n");
          break;
        }

      case VecContains vcont: {
          IrTypeId vId = GetOperandType(fn, vcont.Vec);
          _sb.Append("  ").Append(TempCName(vcont.Dest)).Append(" = (uint8_t)").Append(VecContainsFnName(vId)).Append('(')
              .Append(OperandExpr(fn, vcont.Vec)).Append(", ").Append(OperandExpr(fn, vcont.Value)).Append(");\n");
          break;
        }

      // -----------------------------------------------------------------------
      // Maps
      // -----------------------------------------------------------------------
      case MapNew mn: {
          IrTypeId mId = fn.Temps[mn.Dest.Index].Type;
          if (mn.Entries.IsEmpty) {
            _sb.Append("  ").Append(TempCName(mn.Dest)).Append(" = &").Append(EmptySingletonCName(mId)).Append(";\n");
          } else {
            _sb.Append("  {\n");
            _sb.Append("    ").Append(CType(mId)).Append(" _m = &").Append(EmptySingletonCName(mId)).Append(";\n");
            foreach ((IrOperand k, IrOperand v) in mn.Entries) {
              _sb.Append("    ").Append(MapSetFnName(mId)).Append("(&_m, ")
                  .Append(OperandExpr(fn, k)).Append(", ").Append(OperandExpr(fn, v)).Append(");\n");
            }
            _sb.Append("    ").Append(TempCName(mn.Dest)).Append(" = _m;\n");
            _sb.Append("  }\n");
          }
          break;
        }

      case MapLen ml:
        _sb.Append("  ").Append(TempCName(ml.Dest)).Append(" = (").Append(OperandExpr(fn, ml.Map)).Append(")->count;\n");
        break;

      case MapGet mg: {
          IrTypeId mId = GetOperandType(fn, mg.Map);
          _sb.Append("  ").Append(TempCName(mg.Dest)).Append(" = ").Append(MapGetFnName(mId)).Append('(')
              .Append(OperandExpr(fn, mg.Map)).Append(", ").Append(OperandExpr(fn, mg.Key))
              .Append(", ").Append(FormatPinoLoc(source, mg.Span)).Append(");\n");
          break;
        }

      case MapElemAddr mea: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, mea.MapPtr));
          _sb.Append("  ").Append(TempCName(mea.Dest)).Append(" = ").Append(MapElemAddrFnName(ptrT.TargetType)).Append('(')
              .Append(OperandExpr(fn, mea.MapPtr)).Append(", ").Append(OperandExpr(fn, mea.Key))
              .Append(", ").Append(FormatPinoLoc(source, mea.Span)).Append(");\n");
          break;
        }

      case MapSet ms: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, ms.MapPtr));
          _sb.Append("  ").Append(MapSetFnName(ptrT.TargetType)).Append('(')
              .Append(OperandExpr(fn, ms.MapPtr)).Append(", ")
              .Append(OperandExpr(fn, ms.Key)).Append(", ")
              .Append(OperandExpr(fn, ms.Value)).Append(");\n");
          break;
        }

      case MapRemove mr: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, mr.MapPtr));
          var mt = (IrMapType) _types.GetType(ptrT.TargetType);
          EmitOptionOutCall(
              fn,
              mr.Dest,
              mt.ValueType,
              $"{MapRemoveFnName(mt.Id)}({OperandExpr(fn, mr.MapPtr)}, {OperandExpr(fn, mr.Key)}, &_opt_val)");
          break;
        }

      case MapContains mc: {
          IrTypeId mId = GetOperandType(fn, mc.Map);
          _sb.Append("  ").Append(TempCName(mc.Dest)).Append(" = (uint8_t)").Append(MapContainsFnName(mId)).Append('(')
              .Append(OperandExpr(fn, mc.Map)).Append(", ").Append(OperandExpr(fn, mc.Key)).Append(");\n");
          break;
        }

      case MapKeys mk: {
          IrTypeId mId = GetOperandType(fn, mk.Map);
          _sb.Append("  ").Append(TempCName(mk.Dest)).Append(" = ").Append(MapKeysFnName(mId))
              .Append('(').Append(OperandExpr(fn, mk.Map)).Append(");\n");
          break;
        }

      case MapValues mv: {
          IrTypeId mId = GetOperandType(fn, mv.Map);
          _sb.Append("  ").Append(TempCName(mv.Dest)).Append(" = ").Append(MapValuesFnName(mId))
              .Append('(').Append(OperandExpr(fn, mv.Map)).Append(");\n");
          break;
        }

      case MapSlotLen msl:
        _sb.Append("  ").Append(TempCName(msl.Dest)).Append(" = (").Append(OperandExpr(fn, msl.Map)).Append(")->entry_len;\n");
        break;

      case MapSlotIsLive msil:
        _sb.Append("  ").Append(TempCName(msil.Dest)).Append(" = (uint8_t)((")
            .Append(OperandExpr(fn, msil.Map)).Append(")->entries[").Append(OperandExpr(fn, msil.SlotIndex)).Append("].state == 1);\n");
        break;

      case MapSlotKey msk:
        _sb.Append("  ").Append(TempCName(msk.Dest)).Append(" = (")
            .Append(OperandExpr(fn, msk.Map)).Append(")->entries[").Append(OperandExpr(fn, msk.SlotIndex)).Append("].key;\n");
        break;

      case MapSlotValue msv:
        _sb.Append("  ").Append(TempCName(msv.Dest)).Append(" = (")
            .Append(OperandExpr(fn, msv.Map)).Append(")->entries[").Append(OperandExpr(fn, msv.SlotIndex)).Append("].value;\n");
        break;

      // -----------------------------------------------------------------------
      // Sets
      // -----------------------------------------------------------------------
      case SetNew sn: {
          IrTypeId sId = fn.Temps[sn.Dest.Index].Type;
          if (sn.Elements.IsEmpty) {
            _sb.Append("  ").Append(TempCName(sn.Dest)).Append(" = &").Append(EmptySingletonCName(sId)).Append(";\n");
          } else {
            _sb.Append("  {\n");
            _sb.Append("    ").Append(CType(sId)).Append(" _s = &").Append(EmptySingletonCName(sId)).Append(";\n");
            foreach (IrOperand el in sn.Elements) {
              _sb.Append("    ").Append(SetAddFnName(sId)).Append("(&_s, ").Append(OperandExpr(fn, el)).Append(");\n");
            }
            _sb.Append("    ").Append(TempCName(sn.Dest)).Append(" = _s;\n");
            _sb.Append("  }\n");
          }
          break;
        }

      case SetLen sl:
        _sb.Append("  ").Append(TempCName(sl.Dest)).Append(" = (").Append(OperandExpr(fn, sl.Set)).Append(")->count;\n");
        break;

      case SetAdd sa: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, sa.SetPtr));
          _sb.Append("  ").Append(SetAddFnName(ptrT.TargetType)).Append('(')
              .Append(OperandExpr(fn, sa.SetPtr)).Append(", ").Append(OperandExpr(fn, sa.Value)).Append(");\n");
          break;
        }

      case SetRemove sr: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, sr.SetPtr));
          _sb.Append("  ").Append(SetRemoveFnName(ptrT.TargetType)).Append('(')
              .Append(OperandExpr(fn, sr.SetPtr)).Append(", ").Append(OperandExpr(fn, sr.Value)).Append(");\n");
          break;
        }

      case SetContains sc: {
          IrTypeId sId = GetOperandType(fn, sc.Set);
          _sb.Append("  ").Append(TempCName(sc.Dest)).Append(" = (uint8_t)").Append(SetContainsFnName(sId)).Append('(')
              .Append(OperandExpr(fn, sc.Set)).Append(", ").Append(OperandExpr(fn, sc.Value)).Append(");\n");
          break;
        }

      case SetSlotLen ssl:
        _sb.Append("  ").Append(TempCName(ssl.Dest)).Append(" = (").Append(OperandExpr(fn, ssl.Set)).Append(")->entry_len;\n");
        break;

      case SetSlotIsLive ssil:
        _sb.Append("  ").Append(TempCName(ssil.Dest)).Append(" = (uint8_t)((")
            .Append(OperandExpr(fn, ssil.Set)).Append(")->entries[").Append(OperandExpr(fn, ssil.SlotIndex)).Append("].state == 1);\n");
        break;

      case SetSlotElem sse:
        _sb.Append("  ").Append(TempCName(sse.Dest)).Append(" = (")
            .Append(OperandExpr(fn, sse.Set)).Append(")->entries[").Append(OperandExpr(fn, sse.SlotIndex)).Append("].elem;\n");
        break;

      // -----------------------------------------------------------------------
      // Pools (D-185)
      // -----------------------------------------------------------------------
      case PoolNew pn: {
          IrTypeId pId = fn.Temps[pn.Dest.Index].Type;
          _sb.Append("  ").Append(TempCName(pn.Dest)).Append(" = &").Append(EmptySingletonCName(pId)).Append(";\n");
          break;
        }

      case PoolLen pl:
        _sb.Append("  ").Append(TempCName(pl.Dest)).Append(" = (").Append(OperandExpr(fn, pl.Pool)).Append(")->live_count;\n");
        break;

      case PoolAdd pa: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, pa.PoolPtr));
          _sb.Append("  ").Append(TempCName(pa.Dest)).Append(" = ").Append(PoolAddFnName(ptrT.TargetType)).Append('(')
              .Append(OperandExpr(fn, pa.PoolPtr)).Append(", ").Append(OperandExpr(fn, pa.Value)).Append(");\n");
          break;
        }

      case PoolRemove pr: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, pr.PoolPtr));
          var pt = (IrPoolType) _types.GetType(ptrT.TargetType);
          EmitOptionOutCall(
              fn,
              pr.Dest,
              pt.ElementType,
              $"{PoolRemoveFnName(pt.Id)}({OperandExpr(fn, pr.PoolPtr)}, {OperandExpr(fn, pr.Handle)}, &_opt_val)");
          break;
        }

      case PoolGet pg: {
          var pt = (IrPoolType) _types.GetType(GetOperandType(fn, pg.Pool));
          EmitOptionOutCall(
              fn,
              pg.Dest,
              pt.ElementType,
              $"{PoolGetFnName(pt.Id)}({OperandExpr(fn, pg.Pool)}, {OperandExpr(fn, pg.Handle)}, &_opt_val)");
          break;
        }

      case PoolGetOrPanic pgop: {
          IrTypeId pId = GetOperandType(fn, pgop.Pool);
          _sb.Append("  ").Append(TempCName(pgop.Dest)).Append(" = ").Append(PoolGetOrPanicFnName(pId)).Append('(')
              .Append(OperandExpr(fn, pgop.Pool)).Append(", ").Append(OperandExpr(fn, pgop.Handle))
              .Append(", \"").Append(EscapeCString(pgop.PoolName)).Append("\", ")
              .Append(FormatPinoLoc(source, pgop.Span)).Append(");\n");
          break;
        }

      case PoolElemAddr pea: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, pea.PoolPtr));
          _sb.Append("  ").Append(TempCName(pea.Dest)).Append(" = ").Append(PoolElemAddrFnName(ptrT.TargetType)).Append('(')
              .Append(OperandExpr(fn, pea.PoolPtr)).Append(", ").Append(OperandExpr(fn, pea.Handle))
              .Append(", \"").Append(EscapeCString(pea.PoolName)).Append("\", ")
              .Append(FormatPinoLoc(source, pea.Span)).Append(");\n");
          break;
        }

      case PoolHandles ph: {
          IrTypeId pId = GetOperandType(fn, ph.Pool);
          _sb.Append("  ").Append(TempCName(ph.Dest)).Append(" = ").Append(PoolHandlesFnName(pId))
              .Append('(').Append(OperandExpr(fn, ph.Pool)).Append(");\n");
          break;
        }

      case PoolClear pc: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, pc.PoolPtr));
          _sb.Append("  ").Append(PoolClearFnName(ptrT.TargetType))
              .Append('(').Append(OperandExpr(fn, pc.PoolPtr)).Append(");\n");
          break;
        }

      case PoolContains pcont: {
          IrTypeId pId = GetOperandType(fn, pcont.Pool);
          _sb.Append("  ").Append(TempCName(pcont.Dest)).Append(" = (uint8_t)").Append(PoolContainsFnName(pId)).Append('(')
              .Append(OperandExpr(fn, pcont.Pool)).Append(", ").Append(OperandExpr(fn, pcont.Handle)).Append(");\n");
          break;
        }

      case PoolSlotLen psl:
        _sb.Append("  ").Append(TempCName(psl.Dest)).Append(" = (").Append(OperandExpr(fn, psl.Pool)).Append(")->slot_len;\n");
        break;

      case PoolSlotIsLive psil:
        _sb.Append("  ").Append(TempCName(psil.Dest)).Append(" = (uint8_t)((")
            .Append(OperandExpr(fn, psil.Pool)).Append(")->slots[").Append(OperandExpr(fn, psil.SlotIndex)).Append("].state == 1);\n");
        break;

      case PoolSlotHandle psh: {
          string pExpr = OperandExpr(fn, psh.Pool);
          string iExpr = OperandExpr(fn, psh.SlotIndex);
          _sb.Append("  ").Append(TempCName(psh.Dest)).Append(" = (PinoHandle){(uint32_t)(").Append(iExpr)
              .Append("), (").Append(pExpr).Append(")->slots[").Append(iExpr).Append("].gen};\n");
          break;
        }

      case PoolSlotValue psv:
        _sb.Append("  ").Append(TempCName(psv.Dest)).Append(" = (")
            .Append(OperandExpr(fn, psv.Pool)).Append(")->slots[").Append(OperandExpr(fn, psv.SlotIndex)).Append("].value;\n");
        break;

      // -----------------------------------------------------------------------
      // Strings
      // -----------------------------------------------------------------------
      case StrLen sl:
        _sb.Append("  ").Append(TempCName(sl.Dest)).Append(" = pino_str_rune_len(").Append(OperandExpr(fn, sl.Str)).Append(");\n");
        break;

      case StrGetRune sgr:
        _sb.Append("  ").Append(TempCName(sgr.Dest)).Append(" = pino_str_get_rune(")
            .Append(OperandExpr(fn, sgr.Str)).Append(", ").Append(OperandExpr(fn, sgr.Index))
            .Append(", ").Append(FormatPinoLoc(source, sgr.Span)).Append(");\n");
        break;

      case StrByteLen sbl:
        _sb.Append("  ").Append(TempCName(sbl.Dest)).Append(" = (").Append(OperandExpr(fn, sbl.Str)).Append(").byte_len;\n");
        break;

      case StrDecodeRune sdr:
        _sb.Append("  ").Append(TempCName(sdr.Dest)).Append(" = pino_str_decode_at(")
            .Append(OperandExpr(fn, sdr.Str)).Append(", ").Append(OperandExpr(fn, sdr.BytePos)).Append(");\n");
        break;

      case StrNextBytePos snbp:
        _sb.Append("  ").Append(TempCName(snbp.Dest)).Append(" = pino_str_next_pos(")
            .Append(OperandExpr(fn, snbp.Str)).Append(", ").Append(OperandExpr(fn, snbp.BytePos)).Append(");\n");
        break;

      case StrEq seq:
        _sb.Append("  ").Append(TempCName(seq.Dest)).Append(" = (uint8_t)pino_str_eq(")
            .Append(OperandExpr(fn, seq.Left)).Append(", ").Append(OperandExpr(fn, seq.Right)).Append(");\n");
        break;

      case StrCmp scmp:
        _sb.Append("  ").Append(TempCName(scmp.Dest)).Append(" = (int64_t)pino_str_cmp(")
            .Append(OperandExpr(fn, scmp.Left)).Append(", ").Append(OperandExpr(fn, scmp.Right)).Append(");\n");
        break;

      case StrConcat sc:
        _sb.Append("  ").Append(TempCName(sc.Dest)).Append(" = pino_str_concat(")
            .Append(OperandExpr(fn, sc.Left)).Append(", ").Append(OperandExpr(fn, sc.Right)).Append(");\n");
        break;

      case StrConcatN scn:
        if (scn.Parts.IsEmpty) {
          _sb.Append("  ").Append(TempCName(scn.Dest)).Append(" = PINO_STR_EMPTY;\n");
        } else {
          _sb.Append("  {\n");
          _sb.Append("    PinoString _parts[").Append(scn.Parts.Length).Append("] = {");
          for (int p = 0; p < scn.Parts.Length; p++) {
            if (p > 0) _sb.Append(", ");
            _sb.Append(OperandExpr(fn, scn.Parts[p]));
          }
          _sb.Append("};\n");
          _sb.Append("    ").Append(TempCName(scn.Dest)).Append(" = pino_str_concat_n(_parts, ")
              .Append(scn.Parts.Length).Append("LL);\n");
          _sb.Append("  }\n");
        }
        break;

      case StrConcatRune scr:
        _sb.Append("  ").Append(TempCName(scr.Dest)).Append(" = pino_str_concat_rune(")
            .Append(OperandExpr(fn, scr.Str)).Append(", ").Append(OperandExpr(fn, scr.RuneValue)).Append(");\n");
        break;

      case RuneConcatStr rcs:
        _sb.Append("  ").Append(TempCName(rcs.Dest)).Append(" = pino_rune_concat_str(")
            .Append(OperandExpr(fn, rcs.RuneValue)).Append(", ").Append(OperandExpr(fn, rcs.Str)).Append(");\n");
        break;

      // -----------------------------------------------------------------------
      // Calls
      // -----------------------------------------------------------------------
      case CallDirect cd: {
          IrFunction callee = _module.Functions[cd.Callee.Index];
          bool hasDest = cd.Dest is { } d && fn.Temps[d.Index].Type != _types.Void && fn.Temps[d.Index].Type != _types.Never;
          _sb.Append("  ");
          if (hasDest) {
            _sb.Append(TempCName(cd.Dest!.Value)).Append(" = ");
          }
          _sb.Append(FuncCName(cd.Callee)).Append('(');
          for (int a = 0; a < cd.Arguments.Length; a++) {
            if (a > 0) _sb.Append(", ");
            IrParam p = callee.Parameters[a];
            _sb.Append(OperandArgForParam(fn, cd.Arguments[a], p.Type, p.Ownership));
          }
          _sb.Append(");\n");
          break;
        }

      case CallClosure cc: {
          var closType = (IrClosureType) _types.GetType(GetOperandType(fn, cc.Closure));
          bool hasDest = cc.Dest is { } d && fn.Temps[d.Index].Type != _types.Void && fn.Temps[d.Index].Type != _types.Never;
          string closExpr = OperandExpr(fn, cc.Closure);
          _sb.Append("  {\n");
          _sb.Append("    PinoClosure _cl = ").Append(closExpr).Append(";\n");
          // RC ABI (062 changelog): CallClosure borrows Closure; callee takes @owned %env, so inc before calling!
          _sb.Append("    if (_cl.env != NULL) pino_rc_inc(&_cl.env->hdr);\n");
          _sb.Append("    typedef ").Append(CType(closType.ReturnType)).Append(" (*_cl_fn_t)(PinoEnvHdr*");
          for (int a = 0; a < closType.Parameters.Length; a++) {
            IrClosureParam cp = closType.Parameters[a];
            _sb.Append(", ").Append(CParamType(cp.Type, cp.Ownership));
          }
          _sb.Append(");\n");
          _sb.Append("    ");
          if (hasDest) {
            _sb.Append(TempCName(cc.Dest!.Value)).Append(" = ");
          }
          _sb.Append("((_cl_fn_t)_cl.fn_ptr)(_cl.env");
          for (int a = 0; a < cc.Arguments.Length; a++) {
            IrClosureParam cp = closType.Parameters[a];
            _sb.Append(", ").Append(OperandArgForParam(fn, cc.Arguments[a], cp.Type, cp.Ownership));
          }
          _sb.Append(");\n");
          _sb.Append("  }\n");
          break;
        }

      case CallIface ci: {
          IrType recvIr = _types.GetType(GetOperandType(fn, ci.Receiver));
          IrInterfaceType ifaceType = ci.IsMutating
              ? (IrInterfaceType) _types.GetType(((IrPtrType) recvIr).TargetType)
              : (IrInterfaceType) recvIr;
          IrInterfaceMethod method = ifaceType.Methods[ci.MethodIndex];
          bool hasDest = ci.Dest is { } d && fn.Temps[d.Index].Type != _types.Void && fn.Temps[d.Index].Type != _types.Never;
          string recvExpr = OperandExpr(fn, ci.Receiver);
          string acc = ci.IsMutating ? "->" : ".";
          _sb.Append("  ");
          if (hasDest) {
            _sb.Append(TempCName(ci.Dest!.Value)).Append(" = ");
          }
          _sb.Append('(').Append(recvExpr).Append(')').Append(acc).Append("vtable->m").Append(ci.MethodIndex)
              .Append("((").Append(recvExpr).Append(')').Append(acc).Append("data");
          for (int a = 0; a < ci.Arguments.Length; a++) {
            IrClosureParam cp = method.Parameters[a];
            _sb.Append(", ").Append(OperandArgForParam(fn, ci.Arguments[a], cp.Type, cp.Ownership));
          }
          _sb.Append(");\n");
          break;
        }

      case CallRuntime crt:
        EmitCallRuntime(fn, crt, source);
        break;

      // -----------------------------------------------------------------------
      // Ownership & arenas
      // -----------------------------------------------------------------------
      case RcInc rci: {
          IrTypeId tId = GetOperandType(fn, rci.Value);
          if (_types.GetType(tId).IsRcManaged) {
            _sb.Append("  ").Append(IncFnName(tId)).Append('(').Append(OperandExpr(fn, rci.Value)).Append(");\n");
          }
          break;
        }

      case RcDec rcd: {
          IrTypeId tId = GetOperandType(fn, rcd.Value);
          if (_types.GetType(tId).IsRcManaged) {
            _sb.Append("  ").Append(DropFnName(tId)).Append('(').Append(OperandExpr(fn, rcd.Value)).Append(");\n");
          }
          break;
        }

      case EnsureUnique eu: {
          var ptrT = (IrPtrType) _types.GetType(GetOperandType(fn, eu.PlacePtr));
          IrType container = _types.GetType(ptrT.TargetType);
          string ptrExpr = OperandExpr(fn, eu.PlacePtr);
          switch (container) {
            case IrVectorType vt:
              _sb.Append("  ").Append(VecEnsureUniqueFnName(vt.Id)).Append('(').Append(ptrExpr).Append(");\n");
              break;
            case IrMapType mt:
              _sb.Append("  ").Append(MapEnsureUniqueFnName(mt.Id)).Append('(').Append(ptrExpr).Append(");\n");
              break;
            case IrSetType setT:
              _sb.Append("  ").Append(SetEnsureUniqueFnName(setT.Id)).Append('(').Append(ptrExpr).Append(");\n");
              break;
            case IrPoolType pt:
              _sb.Append("  ").Append(PoolEnsureUniqueFnName(pt.Id)).Append('(').Append(ptrExpr).Append(");\n");
              break;
            case IrInterfaceType it:
              _sb.Append("  ").Append(IfaceEnsureUniqueFnName(it.Id)).Append('(').Append(ptrExpr).Append(");\n");
              break;
          }
          break;
        }

      case Reset:
      case Reuse:
      case ArenaEnter:
      case ArenaExit:
        // Step 2/3 instructions; safe no-ops in Step 1.
        break;

      default:
        throw new InvalidOperationException($"Unsupported IR instruction '{instr.GetType().Name}'.");
    }
  }

  private void EmitOptionOutCall(IrFunction fn, IrTempId dest, IrTypeId elemTypeId, string callExpr) {
    IrTypeId optTypeId = fn.Temps[dest.Index].Type;
    string optCType = CType(optTypeId);
    string elemCType = CType(elemTypeId);
    _sb.Append("  {\n");
    _sb.Append("    ").Append(elemCType).Append(" _opt_val = ").Append(ZeroInitFor(elemTypeId)).Append(";\n");
    _sb.Append("    ").Append(optCType).Append(" _opt_res = {0};\n");
    _sb.Append("    if (").Append(callExpr).Append(") {\n");
    _sb.Append("      _opt_res.tag = 0;\n");
    _sb.Append("      _opt_res.u.v0.f0 = _opt_val;\n");
    _sb.Append("    } else {\n");
    _sb.Append("      _opt_res.tag = 1;\n");
    _sb.Append("    }\n");
    _sb.Append("    ").Append(TempCName(dest)).Append(" = _opt_res;\n");
    _sb.Append("  }\n");
  }

  private void EmitCallRuntime(IrFunction fn, CallRuntime crt, SourceText? source) {
    string loc = FormatPinoLoc(source, crt.Span);
    switch (crt.Op) {
      case IrRuntimeOp.PrintlnN:
        if (crt.Arguments.IsEmpty) {
          _sb.Append("  pino_println_n(NULL, 0);\n");
        } else {
          _sb.Append("  {\n");
          _sb.Append("    PinoString _pargs[").Append(crt.Arguments.Length).Append("] = {");
          for (int i = 0; i < crt.Arguments.Length; i++) {
            if (i > 0) _sb.Append(", ");
            _sb.Append(OperandExpr(fn, crt.Arguments[i]));
          }
          _sb.Append("};\n");
          _sb.Append("    pino_println_n(_pargs, ").Append(crt.Arguments.Length).Append("LL);\n");
          _sb.Append("  }\n");
        }
        break;

      case IrRuntimeOp.Eprintln:
        _sb.Append("  pino_eprintln(").Append(OperandExpr(fn, crt.Arguments[0])).Append(");\n");
        break;

      case IrRuntimeOp.Exit:
        _sb.Append("  exit((int)(").Append(OperandExpr(fn, crt.Arguments[0])).Append("));\n");
        break;

      case IrRuntimeOp.Readline: {
          IrTempId dest = crt.Dest!.Value;
          string resCType = CType(fn.Temps[dest.Index].Type);
          _sb.Append("  {\n");
          _sb.Append("    PinoString _rl_out = PINO_STR_EMPTY;\n");
          _sb.Append("    ").Append(resCType).Append(" _res = {0};\n");
          _sb.Append("    if (pino_readline(").Append(OperandExpr(fn, crt.Arguments[0])).Append(", &_rl_out)) {\n");
          _sb.Append("      _res.tag = 0; _res.u.v0.f0 = _rl_out;\n");
          _sb.Append("    } else {\n");
          _sb.Append("      _res.tag = 1; _res.u.v1.f0 = _rl_out;\n");
          _sb.Append("    }\n");
          _sb.Append("    ").Append(TempCName(dest)).Append(" = _res;\n");
          _sb.Append("  }\n");
          break;
        }

      case IrRuntimeOp.ParseInt: {
          IrTempId dest = crt.Dest!.Value;
          string resCType = CType(fn.Temps[dest.Index].Type);
          _sb.Append("  {\n");
          _sb.Append("    int64_t _pi_out = 0;\n");
          _sb.Append("    ").Append(resCType).Append(" _res = {0};\n");
          _sb.Append("    if (pino_parse_int(").Append(OperandExpr(fn, crt.Arguments[0])).Append(", &_pi_out)) {\n");
          _sb.Append("      _res.tag = 0; _res.u.v0.f0 = _pi_out;\n");
          _sb.Append("    } else {\n");
          _sb.Append("      _res.tag = 1; _res.u.v1.f0 = PINO_STR_LIT(\"Invalid integer format\");\n");
          _sb.Append("    }\n");
          _sb.Append("    ").Append(TempCName(dest)).Append(" = _res;\n");
          _sb.Append("  }\n");
          break;
        }

      case IrRuntimeOp.ParseFloat: {
          IrTempId dest = crt.Dest!.Value;
          string resCType = CType(fn.Temps[dest.Index].Type);
          _sb.Append("  {\n");
          _sb.Append("    double _pf_out = 0.0;\n");
          _sb.Append("    ").Append(resCType).Append(" _res = {0};\n");
          _sb.Append("    if (pino_parse_float(").Append(OperandExpr(fn, crt.Arguments[0])).Append(", &_pf_out)) {\n");
          _sb.Append("      _res.tag = 0; _res.u.v0.f0 = _pf_out;\n");
          _sb.Append("    } else {\n");
          _sb.Append("      _res.tag = 1; _res.u.v1.f0 = PINO_STR_LIT(\"Invalid float format\");\n");
          _sb.Append("    }\n");
          _sb.Append("    ").Append(TempCName(dest)).Append(" = _res;\n");
          _sb.Append("  }\n");
          break;
        }

      case IrRuntimeOp.StrFirstRune:
        _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = pino_str_first_rune(")
            .Append(OperandExpr(fn, crt.Arguments[0])).Append(");\n");
        break;

      case IrRuntimeOp.ToStr: {
          IrTypeId argTypeId = GetOperandType(fn, crt.Arguments[0]);
          _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = ").Append(StrFnName(argTypeId))
              .Append('(').Append(OperandExpr(fn, crt.Arguments[0])).Append(");\n");
          break;
        }

      case IrRuntimeOp.IfaceTypeName: {
          string ifaceExpr = OperandExpr(fn, crt.Arguments[0]);
          _sb.Append("  {\n");
          _sb.Append("    const char* _tn = (").Append(ifaceExpr).Append(").vtable->type_name;\n");
          _sb.Append("    ").Append(TempCName(crt.Dest!.Value)).Append(" = (PinoString){NULL, _tn, (int64_t)strlen(_tn)};\n");
          _sb.Append("  }\n");
          break;
        }

      case IrRuntimeOp.RandFloat:
        _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = pino_rand_float();\n");
        break;

      case IrRuntimeOp.RandInt:
        _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = pino_rand_int(")
            .Append(OperandExpr(fn, crt.Arguments[0])).Append(");\n");
        break;

      case IrRuntimeOp.Time:
        _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = pino_time();\n");
        break;

      case IrRuntimeOp.Sleep:
        _sb.Append("  pino_sleep(").Append(OperandExpr(fn, crt.Arguments[0])).Append(");\n");
        break;

      case IrRuntimeOp.Clear:
        _sb.Append("  pino_clear();\n");
        break;

      case IrRuntimeOp.RegexCompile:
        _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = pino_regex_compile(")
            .Append(OperandExpr(fn, crt.Arguments[0])).Append(", ").Append(loc).Append(");\n");
        break;

      case IrRuntimeOp.ReadFile:
        EmitFileResultCall(fn, crt.Dest!.Value, $"pino_read_file({OperandExpr(fn, crt.Arguments[0])})");
        break;

      case IrRuntimeOp.WriteFile:
        EmitFileResultCall(
            fn,
            crt.Dest!.Value,
            $"pino_write_file({OperandExpr(fn, crt.Arguments[0])}, {OperandExpr(fn, crt.Arguments[1])})");
        break;

      case IrRuntimeOp.FileExists:
        _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = (uint8_t)pino_file_exists(")
            .Append(OperandExpr(fn, crt.Arguments[0])).Append(");\n");
        break;

      case IrRuntimeOp.StrLower:
        EmitUnaryRtCall(fn, crt, "pino_str_lower");
        break;

      case IrRuntimeOp.StrUpper:
        EmitUnaryRtCall(fn, crt, "pino_str_upper");
        break;

      case IrRuntimeOp.StrTrim:
        EmitUnaryRtCall(fn, crt, "pino_str_trim");
        break;

      case IrRuntimeOp.StrTrimStart:
        EmitUnaryRtCall(fn, crt, "pino_str_trim_start");
        break;

      case IrRuntimeOp.StrTrimEnd:
        EmitUnaryRtCall(fn, crt, "pino_str_trim_end");
        break;

      case IrRuntimeOp.StrClone:
        EmitUnaryRtCall(fn, crt, "pino_str_clone");
        break;

      case IrRuntimeOp.StrToCString:
        EmitUnaryRtCall(fn, crt, "pino_str_to_cstring");
        break;

      case IrRuntimeOp.StrContains:
        _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = (uint8_t)pino_str_contains(")
            .Append(OperandExpr(fn, crt.Arguments[0])).Append(", ").Append(OperandExpr(fn, crt.Arguments[1])).Append(");\n");
        break;

      case IrRuntimeOp.StrStartsWith:
        _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = (uint8_t)pino_str_starts_with(")
            .Append(OperandExpr(fn, crt.Arguments[0])).Append(", ").Append(OperandExpr(fn, crt.Arguments[1])).Append(");\n");
        break;

      case IrRuntimeOp.StrEndsWith:
        _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = (uint8_t)pino_str_ends_with(")
            .Append(OperandExpr(fn, crt.Arguments[0])).Append(", ").Append(OperandExpr(fn, crt.Arguments[1])).Append(");\n");
        break;

      case IrRuntimeOp.StrIndexOf:
        EmitBinaryRtCall(fn, crt, "pino_str_index_of");
        break;

      case IrRuntimeOp.StrLastIndexOf:
        EmitBinaryRtCall(fn, crt, "pino_str_last_index_of");
        break;

      case IrRuntimeOp.StrSplit:
        EmitBinaryRtCall(fn, crt, "pino_str_split");
        break;

      case IrRuntimeOp.StrReplace:
        _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = pino_str_replace(")
            .Append(OperandExpr(fn, crt.Arguments[0])).Append(", ")
            .Append(OperandExpr(fn, crt.Arguments[1])).Append(", ")
            .Append(OperandExpr(fn, crt.Arguments[2])).Append(");\n");
        break;

      case IrRuntimeOp.StrSubstring:
        _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = pino_str_substring(")
            .Append(OperandExpr(fn, crt.Arguments[0])).Append(", ")
            .Append(OperandExpr(fn, crt.Arguments[1])).Append(", ")
            .Append(OperandExpr(fn, crt.Arguments[2])).Append(", ")
            .Append(loc).Append(");\n");
        break;

      case IrRuntimeOp.RegexPattern:
        EmitUnaryRtCall(fn, crt, "pino_regex_pattern");
        break;

      case IrRuntimeOp.RegexHasMatch:
        _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = (uint8_t)pino_regex_has_match(")
            .Append(OperandExpr(fn, crt.Arguments[0])).Append(", ").Append(OperandExpr(fn, crt.Arguments[1])).Append(");\n");
        break;

      case IrRuntimeOp.RegexMatchPrefix:
        EmitBinaryRtCall(fn, crt, "pino_regex_match_prefix");
        break;

      case IrRuntimeOp.RegexFind:
        EmitBinaryRtCall(fn, crt, "pino_regex_find");
        break;

      case IrRuntimeOp.RegexFindAll:
        EmitBinaryRtCall(fn, crt, "pino_regex_find_all");
        break;

      case IrRuntimeOp.RegexReplace:
        _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = pino_regex_replace(")
            .Append(OperandExpr(fn, crt.Arguments[0])).Append(", ")
            .Append(OperandExpr(fn, crt.Arguments[1])).Append(", ")
            .Append(OperandExpr(fn, crt.Arguments[2])).Append(");\n");
        break;

      default:
        throw new InvalidOperationException($"Unsupported IrRuntimeOp '{crt.Op}'.");
    }
  }

  private void EmitUnaryRtCall(IrFunction fn, CallRuntime crt, string cFn) {
    _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = ").Append(cFn)
        .Append('(').Append(OperandExpr(fn, crt.Arguments[0])).Append(");\n");
  }

  private void EmitBinaryRtCall(IrFunction fn, CallRuntime crt, string cFn) {
    _sb.Append("  ").Append(TempCName(crt.Dest!.Value)).Append(" = ").Append(cFn)
        .Append('(').Append(OperandExpr(fn, crt.Arguments[0])).Append(", ")
        .Append(OperandExpr(fn, crt.Arguments[1])).Append(");\n");
  }

  private void EmitFileResultCall(IrFunction fn, IrTempId dest, string callExpr) {
    IrTypeId resTypeId = fn.Temps[dest.Index].Type;
    var resUnion = (IrUnionType) _types.GetType(resTypeId);
    IrTypeId errTypeId = resUnion.Variants[1].Fields[0].Type;
    string resCType = CType(resTypeId);
    string errCType = CType(errTypeId);

    _sb.Append("  {\n");
    _sb.Append("    PinoFileResult _fr = ").Append(callExpr).Append(";\n");
    _sb.Append("    ").Append(resCType).Append(" _res = {0};\n");
    _sb.Append("    if (_fr.ok) {\n");
    _sb.Append("      _res.tag = 0;\n");
    _sb.Append("      _res.u.v0.f0 = _fr.value;\n");
    _sb.Append("    } else {\n");
    _sb.Append("      ").Append(errCType).Append(" _ioe = {0};\n");
    _sb.Append("      _ioe.tag = _fr.err_tag;\n");
    _sb.Append("      switch (_fr.err_tag) {\n");
    _sb.Append("        case 0: _ioe.u.v0.f0 = _fr.value; break;\n");
    _sb.Append("        case 1: _ioe.u.v1.f0 = _fr.value; break;\n");
    _sb.Append("        case 2: _ioe.u.v2.f0 = _fr.value; break;\n");
    _sb.Append("        default: _ioe.u.v3.f0 = _fr.value; break;\n");
    _sb.Append("      }\n");
    _sb.Append("      _res.tag = 1;\n");
    _sb.Append("      _res.u.v1.f0 = _ioe;\n");
    _sb.Append("    }\n");
    _sb.Append("    ").Append(TempCName(dest)).Append(" = _res;\n");
    _sb.Append("  }\n");
  }

  private void EmitTerminator(IrFunction fn, IrTerminator term, SourceText? source, bool pushFrame) {
    switch (term) {
      case Br br:
        _sb.Append("  goto ").Append(BlockLabel(br.Target)).Append(";\n");
        break;

      case BrIf brIf:
        _sb.Append("  if (").Append(OperandExpr(fn, brIf.Condition)).Append(") goto ")
            .Append(BlockLabel(brIf.TrueTarget)).Append("; else goto ")
            .Append(BlockLabel(brIf.FalseTarget)).Append(";\n");
        break;

      case Switch sw:
        _sb.Append("  switch (").Append(OperandExpr(fn, sw.Scrutinee)).Append(") {\n");
        foreach ((int caseVal, IrBlockId target) in sw.Cases) {
          _sb.Append("    case ").Append(caseVal).Append(": goto ").Append(BlockLabel(target)).Append(";\n");
        }
        if (sw.DefaultTarget is { } defTarget) {
          _sb.Append("    default: goto ").Append(BlockLabel(defTarget)).Append(";\n");
        } else {
          _sb.Append("    default: pino_panic_cstr_at(\"unreachable switch arm\", ")
              .Append(FormatPinoLoc(source, sw.Span)).Append(");\n");
        }
        _sb.Append("  }\n");
        break;

      case Ret ret:
        if (pushFrame) {
          _sb.Append("  PINO_FRAME_POP();\n");
        }
        _sb.Append("  return ").Append(OperandExpr(fn, ret.Value)).Append(";\n");
        break;

      case RetVoid:
        if (pushFrame) {
          _sb.Append("  PINO_FRAME_POP();\n");
        }
        _sb.Append("  return;\n");
        break;

      case Panic panic:
        _sb.Append("  pino_panic_at(").Append(OperandExpr(fn, panic.Message)).Append(", ")
            .Append(FormatPinoLoc(source, panic.Span)).Append(");\n");
        break;

      case Unreachable unreach:
        _sb.Append("  pino_panic_cstr_at(\"unreachable\", ").Append(FormatPinoLoc(source, unreach.Span)).Append(");\n");
        break;

      default:
        throw new InvalidOperationException($"Unsupported IR terminator '{term.GetType().Name}'.");
    }
  }

  private void EmitMainEntrypoint() {
    _sb.Append("int main(void) {\n");
    if (_module.EntryFuncId is { } entryId) {
      _sb.Append("  ").Append(FuncCName(entryId)).Append("();\n");
    } else {
      foreach (IrFuncId initId in _module.InitFuncIds) {
        _sb.Append("  ").Append(FuncCName(initId)).Append("();\n");
      }
      if (_module.ShutdownFuncId is { } shutId) {
        _sb.Append("  ").Append(FuncCName(shutId)).Append("();\n");
      }
    }

    _sb.Append("  return 0;\n");
    _sb.Append("}\n");
  }
}
