using Pino.Compiler.IR;

namespace Pino.Compiler.CodeGen.C;

public sealed partial class CEmitter {
  // ---------------------------------------------------------------------------
  // Forward declarations & topological concrete definitions (design §2, §7.4)
  // ---------------------------------------------------------------------------

  private void EmitForwardTypeDecls() {
    _sb.Append("/* Forward type declarations */\n");
    for (int i = 0; i < _types.Count; i++) {
      IrType t = _types.Types[i];
      switch (t) {
        case IrStructType: {
            string name = StructCName(t.Id);
            _sb.Append("typedef struct ").Append(name).Append(' ').Append(name).Append(";\n");
            break;
          }

        case IrTupleType: {
            string name = TupleCName(t.Id);
            _sb.Append("typedef struct ").Append(name).Append(' ').Append(name).Append(";\n");
            break;
          }

        case IrUnionType: {
            string name = UnionCName(t.Id);
            _sb.Append("typedef struct ").Append(name).Append(' ').Append(name).Append(";\n");
            break;
          }

        case IrVectorType when !IsStringVector(t.Id): {
            string name = VectorStructCName(t.Id);
            _sb.Append("typedef struct ").Append(name).Append(' ').Append(name).Append(";\n");
            break;
          }

        case IrMapType: {
            string entry = MapEntryCName(t.Id);
            string map = MapStructCName(t.Id);
            _sb.Append("typedef struct ").Append(entry).Append(' ').Append(entry).Append(";\n");
            _sb.Append("typedef struct ").Append(map).Append(' ').Append(map).Append(";\n");
            break;
          }

        case IrSetType: {
            string entry = SetEntryCName(t.Id);
            string set = SetStructCName(t.Id);
            _sb.Append("typedef struct ").Append(entry).Append(' ').Append(entry).Append(";\n");
            _sb.Append("typedef struct ").Append(set).Append(' ').Append(set).Append(";\n");
            break;
          }

        case IrPoolType: {
            string slot = PoolSlotCName(t.Id);
            string pool = PoolStructCName(t.Id);
            _sb.Append("typedef struct ").Append(slot).Append(' ').Append(slot).Append(";\n");
            _sb.Append("typedef struct ").Append(pool).Append(' ').Append(pool).Append(";\n");
            break;
          }

        case IrCellType: {
            string name = CellStructCName(t.Id);
            _sb.Append("typedef struct ").Append(name).Append(' ').Append(name).Append(";\n");
            break;
          }

        case IrEnvType: {
            string name = EnvStructCName(t.Id);
            _sb.Append("typedef struct ").Append(name).Append(' ').Append(name).Append(";\n");
            break;
          }

        case IrInterfaceType: {
            string vt = VTableStructCName(t.Id);
            string iface = IfaceCName(t.Id);
            _sb.Append("typedef struct ").Append(vt).Append(' ').Append(vt).Append(";\n");
            _sb.Append("typedef struct ").Append(iface).Append(" {\n");
            _sb.Append("  PinoIfaceBox* data;\n");
            _sb.Append("  const ").Append(vt).Append("* vtable;\n");
            _sb.Append("} ").Append(iface).Append(";\n");
            break;
          }
      }
    }

    _sb.Append('\n');
  }

  private void EmitConcreteTypeDefs() {
    _sb.Append("/* Concrete aggregate definitions (topological order) */\n");

    // 1. Inline value aggregates (structs, tuples, non-recursive unions) in dependency order.
    List<IrTypeId> inlineOrder = TopologicalSortInlineAggregates();
    foreach (IrTypeId id in inlineOrder) {
      IrType t = _types.GetType(id);
      switch (t) {
        case IrStructType st:
          EmitStructDef(st);
          break;
        case IrTupleType tt:
          EmitTupleDef(tt);
          break;
        case IrUnionType ut when !ut.IsRecursiveUnion:
          EmitUnionDef(ut);
          break;
      }
    }

    // 2. Heap-allocated pointer-indirect aggregates (recursive unions, cells, envs, collections).
    for (int i = 0; i < _types.Count; i++) {
      IrType t = _types.Types[i];
      switch (t) {
        case IrUnionType ut when ut.IsRecursiveUnion:
          EmitUnionDef(ut);
          break;

        case IrCellType ct: {
            string name = CellStructCName(ct.Id);
            _sb.Append("struct ").Append(name).Append(" {\n");
            _sb.Append("  PinoRcHeader hdr;\n");
            _sb.Append("  ").Append(CType(ct.ElementType)).Append(" value;\n");
            _sb.Append("};\n\n");
            break;
          }

        case IrEnvType et: {
            string name = EnvStructCName(et.Id);
            _sb.Append("struct ").Append(name).Append(" {\n");
            _sb.Append("  PinoEnvHdr hdr;\n");
            for (int f = 0; f < et.Fields.Length; f++) {
              _sb.Append("  ").Append(CType(et.Fields[f].Type)).Append(' ')
                  .Append(FieldCName(f, et.Fields[f].Name)).Append(";\n");
            }
            _sb.Append("};\n\n");
            break;
          }

        case IrVectorType vt when !IsStringVector(vt.Id): {
            string name = VectorStructCName(vt.Id);
            _sb.Append("struct ").Append(name).Append(" {\n");
            _sb.Append("  PinoRcHeader hdr;\n");
            _sb.Append("  int64_t len;\n");
            _sb.Append("  int64_t cap;\n");
            _sb.Append("  ").Append(CType(vt.ElementType)).Append("* data;\n");
            _sb.Append("};\n\n");
            break;
          }

        case IrMapType mt: {
            string entry = MapEntryCName(mt.Id);
            string map = MapStructCName(mt.Id);
            _sb.Append("struct ").Append(entry).Append(" {\n");
            _sb.Append("  uint8_t state;\n");
            _sb.Append("  uint64_t hash;\n");
            _sb.Append("  ").Append(CType(mt.KeyType)).Append(" key;\n");
            _sb.Append("  ").Append(CType(mt.ValueType)).Append(" value;\n");
            _sb.Append("};\n");
            _sb.Append("struct ").Append(map).Append(" {\n");
            _sb.Append("  PinoRcHeader hdr;\n");
            _sb.Append("  int64_t count;\n");
            _sb.Append("  int64_t entry_len;\n");
            _sb.Append("  int64_t entry_cap;\n");
            _sb.Append("  int64_t index_cap;\n");
            _sb.Append("  int32_t* indices;\n");
            _sb.Append("  ").Append(entry).Append("* entries;\n");
            _sb.Append("};\n\n");
            break;
          }

        case IrSetType setT: {
            string entry = SetEntryCName(setT.Id);
            string set = SetStructCName(setT.Id);
            _sb.Append("struct ").Append(entry).Append(" {\n");
            _sb.Append("  uint8_t state;\n");
            _sb.Append("  uint64_t hash;\n");
            _sb.Append("  ").Append(CType(setT.ElementType)).Append(" elem;\n");
            _sb.Append("};\n");
            _sb.Append("struct ").Append(set).Append(" {\n");
            _sb.Append("  PinoRcHeader hdr;\n");
            _sb.Append("  int64_t count;\n");
            _sb.Append("  int64_t entry_len;\n");
            _sb.Append("  int64_t entry_cap;\n");
            _sb.Append("  int64_t index_cap;\n");
            _sb.Append("  int32_t* indices;\n");
            _sb.Append("  ").Append(entry).Append("* entries;\n");
            _sb.Append("};\n\n");
            break;
          }

        case IrPoolType pt: {
            string slot = PoolSlotCName(pt.Id);
            string pool = PoolStructCName(pt.Id);
            _sb.Append("struct ").Append(slot).Append(" {\n");
            _sb.Append("  uint32_t gen;\n");
            _sb.Append("  uint8_t state;\n");
            _sb.Append("  int32_t next_free;\n");
            _sb.Append("  ").Append(CType(pt.ElementType)).Append(" value;\n");
            _sb.Append("};\n");
            _sb.Append("struct ").Append(pool).Append(" {\n");
            _sb.Append("  PinoRcHeader hdr;\n");
            _sb.Append("  int64_t live_count;\n");
            _sb.Append("  int64_t slot_len;\n");
            _sb.Append("  int64_t slot_cap;\n");
            _sb.Append("  int32_t free_head;\n");
            _sb.Append("  ").Append(slot).Append("* slots;\n");
            _sb.Append("};\n\n");
            break;
          }
      }
    }

    // 3. Interface VTable structs & boxed concrete struct wrappers.
    for (int i = 0; i < _types.Count; i++) {
      if (_types.Types[i] is IrInterfaceType it) {
        EmitInterfaceVTableStructDef(it);
      }
    }

    var emittedBoxTypes = new HashSet<IrTypeId>();
    foreach (IrVTable vt in _module.VTables) {
      if (emittedBoxTypes.Add(vt.ConcreteStructType)) {
        string boxName = IfaceBoxCName(vt.ConcreteStructType);
        _sb.Append("typedef struct ").Append(boxName).Append(" {\n");
        _sb.Append("  PinoIfaceBox base;\n");
        _sb.Append("  ").Append(CType(vt.ConcreteStructType)).Append(" value;\n");
        _sb.Append("} ").Append(boxName).Append(";\n\n");
      }
    }
  }

  private void EmitStructDef(IrStructType st) {
    string name = StructCName(st.Id);
    _sb.Append("struct ").Append(name).Append(" {\n");
    if (st.Fields.IsEmpty) {
      _sb.Append("  uint8_t _empty;\n");
    } else {
      for (int f = 0; f < st.Fields.Length; f++) {
        _sb.Append("  ").Append(CType(st.Fields[f].Type)).Append(' ')
            .Append(FieldCName(f, st.Fields[f].Name)).Append(";\n");
      }
    }

    _sb.Append("};\n\n");
  }

  private void EmitTupleDef(IrTupleType tt) {
    string name = TupleCName(tt.Id);
    _sb.Append("struct ").Append(name).Append(" {\n");
    if (tt.Fields.IsEmpty) {
      _sb.Append("  uint8_t _empty;\n");
    } else {
      for (int f = 0; f < tt.Fields.Length; f++) {
        _sb.Append("  ").Append(CType(tt.Fields[f].Type)).Append(' ')
            .Append(FieldCName(f, tt.Fields[f].Label)).Append(";\n");
      }
    }

    _sb.Append("};\n\n");
  }

  private void EmitUnionDef(IrUnionType ut) {
    string name = UnionCName(ut.Id);
    _sb.Append("struct ").Append(name).Append(" {\n");
    if (ut.IsRecursiveUnion) {
      _sb.Append("  PinoRcHeader hdr;\n");
    }

    _sb.Append("  int32_t tag;\n");

    bool hasAnyPayload = false;
    foreach (IrUnionVariant v in ut.Variants) {
      if (HasRuntimeFields(v)) {
        hasAnyPayload = true;
        break;
      }
    }

    if (hasAnyPayload) {
      _sb.Append("  union {\n");
      foreach (IrUnionVariant v in ut.Variants) {
        if (!HasRuntimeFields(v)) {
          continue;
        }

        _sb.Append("    struct {\n");
        for (int f = 0; f < v.Fields.Length; f++) {
          if (IsVoidOrNever(v.Fields[f].Type)) {
            continue;
          }

          _sb.Append("      ").Append(CType(v.Fields[f].Type)).Append(" f").Append(f).Append(";\n");
        }
        _sb.Append("    } v").Append(v.TagOrdinal).Append(";\n");
      }
      _sb.Append("  } u;\n");
    }

    _sb.Append("};\n\n");
  }

  private bool HasRuntimeFields(IrUnionVariant variant) {
    foreach (IrUnionVariantField f in variant.Fields) {
      if (!IsVoidOrNever(f.Type)) {
        return true;
      }
    }

    return false;
  }

  private void EmitInterfaceVTableStructDef(IrInterfaceType it) {
    string vtName = VTableStructCName(it.Id);
    _sb.Append("struct ").Append(vtName).Append(" {\n");
    _sb.Append("  const char* type_name;\n");
    _sb.Append("  PinoDropFn drop;\n");
    _sb.Append("  PinoIfaceBox* (*clone)(PinoAlloc* a, const PinoIfaceBox* src);\n");
    _sb.Append("  bool (*equals)(const PinoIfaceBox* a, const PinoIfaceBox* b);\n");
    _sb.Append("  PinoString (*to_string)(PinoAlloc* a, const PinoIfaceBox* src);\n");

    for (int m = 0; m < it.Methods.Length; m++) {
      IrInterfaceMethod method = it.Methods[m];
      _sb.Append("  ").Append(CType(method.ReturnType)).Append(" (*m").Append(m).Append(")(PinoIfaceBox* _box");
      for (int p = 0; p < method.Parameters.Length; p++) {
        IrClosureParam cp = method.Parameters[p];
        _sb.Append(", ").Append(CParamType(cp.Type, cp.Ownership)).Append(" a").Append(p);
      }
      _sb.Append(");\n");
    }

    for (int p = 0; p < it.Properties.Length; p++) {
      _sb.Append("  void* (*prop_addr_").Append(p).Append(")(PinoIfaceBox* _box);\n");
    }

    _sb.Append("};\n\n");
  }

  private List<IrTypeId> TopologicalSortInlineAggregates() {
    var result = new List<IrTypeId>();
    var state = new int[_types.Count]; // 0 = unvisited, 1 = visiting, 2 = done

    void Visit(IrTypeId id) {
      if (state[id.Index] != 0) {
        return;
      }

      IrType t = _types.GetType(id);
      bool isInlineAggregate = t is IrStructType or IrTupleType || (t is IrUnionType ut && !ut.IsRecursiveUnion);
      if (!isInlineAggregate) {
        state[id.Index] = 2;
        return;
      }

      state[id.Index] = 1;
      switch (t) {
        case IrStructType st:
          foreach (IrStructField f in st.Fields) {
            Visit(f.Type);
          }
          break;

        case IrTupleType tt:
          foreach (IrTupleField f in tt.Fields) {
            Visit(f.Type);
          }
          break;

        case IrUnionType u:
          foreach (IrUnionVariant v in u.Variants) {
            foreach (IrUnionVariantField f in v.Fields) {
              Visit(f.Type);
            }
          }
          break;
      }

      state[id.Index] = 2;
      result.Add(id);
    }

    for (int i = 0; i < _types.Count; i++) {
      Visit(new IrTypeId(i));
    }

    return result;
  }

  private void EmitImmortalSingletons() {
    _sb.Append("/* Immortal empty singletons (PINO_RC_IMMORTAL, 0 heap allocations) */\n");
    for (int i = 0; i < _types.Count; i++) {
      IrType t = _types.Types[i];
      switch (t) {
        case IrVectorType when !IsStringVector(t.Id):
          _sb.Append("static ").Append(VectorStructCName(t.Id)).Append(' ')
              .Append(EmptySingletonCName(t.Id)).Append(" = { PINO_RC_IMMORTAL_HDR, 0, 0, NULL };\n");
          break;

        case IrMapType:
          _sb.Append("static ").Append(MapStructCName(t.Id)).Append(' ')
              .Append(EmptySingletonCName(t.Id)).Append(" = { PINO_RC_IMMORTAL_HDR, 0, 0, 0, 0, NULL, NULL };\n");
          break;

        case IrSetType:
          _sb.Append("static ").Append(SetStructCName(t.Id)).Append(' ')
              .Append(EmptySingletonCName(t.Id)).Append(" = { PINO_RC_IMMORTAL_HDR, 0, 0, 0, 0, NULL, NULL };\n");
          break;

        case IrPoolType:
          _sb.Append("static ").Append(PoolStructCName(t.Id)).Append(' ')
              .Append(EmptySingletonCName(t.Id)).Append(" = { PINO_RC_IMMORTAL_HDR, 0, 0, 0, -1, NULL };\n");
          break;

        case IrUnionType ut when ut.IsRecursiveUnion:
          foreach (IrUnionVariant v in ut.Variants) {
            if (!HasRuntimeFields(v)) {
              _sb.Append("static ").Append(UnionCName(ut.Id)).Append(' ')
                  .Append(RecursiveUnionSingletonCName(ut.Id, v.TagOrdinal))
                  .Append(" = { PINO_RC_IMMORTAL_HDR, ").Append(v.TagOrdinal).Append(" };\n");
            }
          }
          break;
      }
    }

    _sb.Append('\n');
  }

  private string EmptySingletonCName(IrTypeId id) =>
      IsStringVector(id) ? "pino_vec_string_empty" : $"pg_empty_t{id.Index}";

  private static string RecursiveUnionSingletonCName(IrTypeId id, int tagOrdinal) =>
      $"pg_usingleton_t{id.Index}_v{tagOrdinal}";

  // ---------------------------------------------------------------------------
  // Helper prototypes (RC inc/drop, eq, hash, formatting, collections)
  // ---------------------------------------------------------------------------

  private void EmitHelperPrototypes() {
    _sb.Append("/* Per-type RC, equality, hash, and formatting prototypes */\n");
    for (int i = 0; i < _types.Count; i++) {
      IrType t = _types.Types[i];
      if (t.Kind is IrTypeKind.Void or IrTypeKind.Never or IrTypeKind.Ptr) {
        continue;
      }

      string cType = CType(t.Id);

      if (t.IsRcManaged) {
        _sb.Append("static void ").Append(IncFnName(t.Id)).Append('(').Append(cType).Append(" v);\n");
        _sb.Append("static void ").Append(DropFnName(t.Id)).Append('(').Append(cType).Append(" v);\n");
      }

      if (t.Kind is not (IrTypeKind.Cell or IrTypeKind.Env)) {
        _sb.Append("static bool ").Append(EqFnName(t.Id)).Append('(').Append(cType).Append(" a, ")
            .Append(cType).Append(" b);\n");
        _sb.Append("static uint64_t ").Append(HashFnName(t.Id)).Append('(').Append(cType).Append(" v);\n");
        _sb.Append("static PinoString ").Append(StrFnName(t.Id)).Append('(').Append(cType).Append(" v);\n");
        _sb.Append("static PinoString ").Append(ReprFnName(t.Id)).Append('(').Append(cType).Append(" v);\n");
      }

      if (t is IrEnvType) {
        _sb.Append("static void ").Append(EnvDropCallbackCName(t.Id)).Append("(PinoAlloc* a, void* raw);\n");
      }
    }

    _sb.Append('\n');
    EmitCollectionPrototypes();
  }

  private static string EnvDropCallbackCName(IrTypeId envId) => $"pino_env_drop_t{envId.Index}";

  // ---------------------------------------------------------------------------
  // Per-type RC inc/drop, equality, and hash implementations
  // ---------------------------------------------------------------------------

  private void EmitRcAndEqHashHelperBodies() {
    _sb.Append("/* Per-type RC inc/drop, equality, and hash implementations */\n");

    for (int i = 0; i < _types.Count; i++) {
      IrType t = _types.Types[i];
      if (t.Kind is IrTypeKind.Void or IrTypeKind.Never or IrTypeKind.Ptr) {
        continue;
      }

      if (t.IsRcManaged) {
        EmitRcIncBody(t);
        EmitRcDropBody(t);
      }

      if (t is IrEnvType et) {
        EmitEnvDropCallbackBody(et);
      }

      if (t.Kind is not (IrTypeKind.Cell or IrTypeKind.Env)) {
        EmitEqBody(t);
        EmitHashBody(t);
      }
    }
  }

  private void EmitRcIncBody(IrType t) {
    string cType = CType(t.Id);
    _sb.Append("static void ").Append(IncFnName(t.Id)).Append('(').Append(cType).Append(" v) {\n");

    switch (t) {
      case IrPrimitiveType { Kind: IrTypeKind.String }:
        _sb.Append("  (void)pino_str_retain(v);\n");
        break;

      case IrPrimitiveType { Kind: IrTypeKind.Regex }:
        _sb.Append("  if (v != NULL) pino_rc_inc(&v->hdr);\n");
        break;

      case IrStructType st:
        _sb.Append("  (void)v;\n");
        for (int f = 0; f < st.Fields.Length; f++) {
          if (_types.GetType(st.Fields[f].Type).IsRcManaged) {
            _sb.Append("  ").Append(IncFnName(st.Fields[f].Type))
                .Append("(v.").Append(FieldCName(f, st.Fields[f].Name)).Append(");\n");
          }
        }
        break;

      case IrTupleType tt:
        _sb.Append("  (void)v;\n");
        for (int f = 0; f < tt.Fields.Length; f++) {
          if (_types.GetType(tt.Fields[f].Type).IsRcManaged) {
            _sb.Append("  ").Append(IncFnName(tt.Fields[f].Type))
                .Append("(v.").Append(FieldCName(f, tt.Fields[f].Label)).Append(");\n");
          }
        }
        break;

      case IrUnionType ut when !ut.IsRecursiveUnion:
        _sb.Append("  switch (v.tag) {\n");
        foreach (IrUnionVariant variant in ut.Variants) {
          bool hasRc = false;
          foreach (IrUnionVariantField vf in variant.Fields) {
            if (_types.GetType(vf.Type).IsRcManaged) {
              hasRc = true;
              break;
            }
          }
          if (!hasRc) {
            continue;
          }

          _sb.Append("    case ").Append(variant.TagOrdinal).Append(":\n");
          for (int f = 0; f < variant.Fields.Length; f++) {
            if (_types.GetType(variant.Fields[f].Type).IsRcManaged) {
              _sb.Append("      ").Append(IncFnName(variant.Fields[f].Type))
                  .Append("(v.u.v").Append(variant.TagOrdinal).Append(".f").Append(f).Append(");\n");
            }
          }
          _sb.Append("      break;\n");
        }
        _sb.Append("    default: break;\n");
        _sb.Append("  }\n");
        break;

      case IrUnionType ut when ut.IsRecursiveUnion:
      case IrVectorType:
      case IrMapType:
      case IrSetType:
      case IrPoolType:
      case IrCellType:
        _sb.Append("  if (v != NULL) pino_rc_inc(&v->hdr);\n");
        break;

      case IrEnvType:
        _sb.Append("  if (v != NULL) pino_rc_inc(&v->hdr.hdr);\n");
        break;

      case IrClosureType:
        _sb.Append("  if (v.env != NULL) pino_rc_inc(&v.env->hdr);\n");
        break;

      case IrInterfaceType:
        _sb.Append("  if (v.data != NULL) pino_rc_inc(&v.data->hdr);\n");
        break;
    }

    _sb.Append("}\n\n");
  }

  private void EmitRcDropBody(IrType t) {
    string cType = CType(t.Id);
    _sb.Append("static void ").Append(DropFnName(t.Id)).Append('(').Append(cType).Append(" v) {\n");

    switch (t) {
      case IrPrimitiveType { Kind: IrTypeKind.String }:
        _sb.Append("  pino_str_release(v);\n");
        break;

      case IrPrimitiveType { Kind: IrTypeKind.Regex }:
        _sb.Append("  pino_regex_release(v);\n");
        break;

      case IrStructType st:
        _sb.Append("  (void)v;\n");
        for (int f = 0; f < st.Fields.Length; f++) {
          if (_types.GetType(st.Fields[f].Type).IsRcManaged) {
            _sb.Append("  ").Append(DropFnName(st.Fields[f].Type))
                .Append("(v.").Append(FieldCName(f, st.Fields[f].Name)).Append(");\n");
          }
        }
        break;

      case IrTupleType tt:
        _sb.Append("  (void)v;\n");
        for (int f = 0; f < tt.Fields.Length; f++) {
          if (_types.GetType(tt.Fields[f].Type).IsRcManaged) {
            _sb.Append("  ").Append(DropFnName(tt.Fields[f].Type))
                .Append("(v.").Append(FieldCName(f, tt.Fields[f].Label)).Append(");\n");
          }
        }
        break;

      case IrUnionType ut when !ut.IsRecursiveUnion:
        _sb.Append("  switch (v.tag) {\n");
        foreach (IrUnionVariant variant in ut.Variants) {
          bool hasRc = false;
          foreach (IrUnionVariantField vf in variant.Fields) {
            if (_types.GetType(vf.Type).IsRcManaged) {
              hasRc = true;
              break;
            }
          }
          if (!hasRc) {
            continue;
          }

          _sb.Append("    case ").Append(variant.TagOrdinal).Append(":\n");
          for (int f = 0; f < variant.Fields.Length; f++) {
            if (_types.GetType(variant.Fields[f].Type).IsRcManaged) {
              _sb.Append("      ").Append(DropFnName(variant.Fields[f].Type))
                  .Append("(v.u.v").Append(variant.TagOrdinal).Append(".f").Append(f).Append(");\n");
            }
          }
          _sb.Append("      break;\n");
        }
        _sb.Append("    default: break;\n");
        _sb.Append("  }\n");
        break;

      case IrUnionType ut when ut.IsRecursiveUnion:
        _sb.Append("  if (v == NULL || !pino_rc_dec(&v->hdr)) return;\n");
        _sb.Append("  switch (v->tag) {\n");
        foreach (IrUnionVariant variant in ut.Variants) {
          bool hasRc = false;
          foreach (IrUnionVariantField vf in variant.Fields) {
            if (_types.GetType(vf.Type).IsRcManaged) {
              hasRc = true;
              break;
            }
          }
          if (!hasRc) {
            continue;
          }

          _sb.Append("    case ").Append(variant.TagOrdinal).Append(":\n");
          for (int f = 0; f < variant.Fields.Length; f++) {
            if (_types.GetType(variant.Fields[f].Type).IsRcManaged) {
              _sb.Append("      ").Append(DropFnName(variant.Fields[f].Type))
                  .Append("(v->u.v").Append(variant.TagOrdinal).Append(".f").Append(f).Append(");\n");
            }
          }
          _sb.Append("      break;\n");
        }
        _sb.Append("    default: break;\n");
        _sb.Append("  }\n");
        _sb.Append("  pino_dealloc(pino_cur_alloc, v, sizeof(").Append(UnionCName(ut.Id)).Append("));\n");
        break;

      case IrVectorType vt when IsStringVector(vt.Id):
        _sb.Append("  pino_vec_string_release(v);\n");
        break;

      case IrVectorType vt:
        _sb.Append("  if (v == NULL || !pino_rc_dec(&v->hdr)) return;\n");
        if (_types.GetType(vt.ElementType).IsRcManaged) {
          _sb.Append("  for (int64_t i = 0; i < v->len; i++) {\n");
          _sb.Append("    ").Append(DropFnName(vt.ElementType)).Append("(v->data[i]);\n");
          _sb.Append("  }\n");
        }
        _sb.Append("  if (v->data != NULL) pino_dealloc(pino_cur_alloc, v->data, (size_t)v->cap * sizeof(")
            .Append(CType(vt.ElementType)).Append("));\n");
        _sb.Append("  pino_dealloc(pino_cur_alloc, v, sizeof(").Append(VectorStructCName(vt.Id)).Append("));\n");
        break;

      case IrMapType mt:
        _sb.Append("  if (v == NULL || !pino_rc_dec(&v->hdr)) return;\n");
        if (_types.GetType(mt.KeyType).IsRcManaged || _types.GetType(mt.ValueType).IsRcManaged) {
          _sb.Append("  for (int64_t i = 0; i < v->entry_len; i++) {\n");
          _sb.Append("    if (v->entries[i].state == 1) {\n");
          if (_types.GetType(mt.KeyType).IsRcManaged) {
            _sb.Append("      ").Append(DropFnName(mt.KeyType)).Append("(v->entries[i].key);\n");
          }
          if (_types.GetType(mt.ValueType).IsRcManaged) {
            _sb.Append("      ").Append(DropFnName(mt.ValueType)).Append("(v->entries[i].value);\n");
          }
          _sb.Append("    }\n");
          _sb.Append("  }\n");
        }
        _sb.Append("  if (v->indices != NULL) pino_dealloc(pino_cur_alloc, v->indices, (size_t)v->index_cap * sizeof(int32_t));\n");
        _sb.Append("  if (v->entries != NULL) pino_dealloc(pino_cur_alloc, v->entries, (size_t)v->entry_cap * sizeof(")
            .Append(MapEntryCName(mt.Id)).Append("));\n");
        _sb.Append("  pino_dealloc(pino_cur_alloc, v, sizeof(").Append(MapStructCName(mt.Id)).Append("));\n");
        break;

      case IrSetType setT:
        _sb.Append("  if (v == NULL || !pino_rc_dec(&v->hdr)) return;\n");
        if (_types.GetType(setT.ElementType).IsRcManaged) {
          _sb.Append("  for (int64_t i = 0; i < v->entry_len; i++) {\n");
          _sb.Append("    if (v->entries[i].state == 1) {\n");
          _sb.Append("      ").Append(DropFnName(setT.ElementType)).Append("(v->entries[i].elem);\n");
          _sb.Append("    }\n");
          _sb.Append("  }\n");
        }
        _sb.Append("  if (v->indices != NULL) pino_dealloc(pino_cur_alloc, v->indices, (size_t)v->index_cap * sizeof(int32_t));\n");
        _sb.Append("  if (v->entries != NULL) pino_dealloc(pino_cur_alloc, v->entries, (size_t)v->entry_cap * sizeof(")
            .Append(SetEntryCName(setT.Id)).Append("));\n");
        _sb.Append("  pino_dealloc(pino_cur_alloc, v, sizeof(").Append(SetStructCName(setT.Id)).Append("));\n");
        break;

      case IrPoolType pt:
        _sb.Append("  if (v == NULL || !pino_rc_dec(&v->hdr)) return;\n");
        if (_types.GetType(pt.ElementType).IsRcManaged) {
          _sb.Append("  for (int64_t i = 0; i < v->slot_len; i++) {\n");
          _sb.Append("    if (v->slots[i].state == 1) {\n");
          _sb.Append("      ").Append(DropFnName(pt.ElementType)).Append("(v->slots[i].value);\n");
          _sb.Append("    }\n");
          _sb.Append("  }\n");
        }
        _sb.Append("  if (v->slots != NULL) pino_dealloc(pino_cur_alloc, v->slots, (size_t)v->slot_cap * sizeof(")
            .Append(PoolSlotCName(pt.Id)).Append("));\n");
        _sb.Append("  pino_dealloc(pino_cur_alloc, v, sizeof(").Append(PoolStructCName(pt.Id)).Append("));\n");
        break;

      case IrCellType ct:
        _sb.Append("  if (v == NULL || !pino_rc_dec(&v->hdr)) return;\n");
        if (_types.GetType(ct.ElementType).IsRcManaged) {
          _sb.Append("  ").Append(DropFnName(ct.ElementType)).Append("(v->value);\n");
        }
        _sb.Append("  pino_dealloc(pino_cur_alloc, v, sizeof(").Append(CellStructCName(ct.Id)).Append("));\n");
        break;

      case IrEnvType:
        _sb.Append("  pino_env_release(pino_cur_alloc, v != NULL ? &v->hdr : NULL);\n");
        break;

      case IrClosureType:
        _sb.Append("  pino_env_release(pino_cur_alloc, v.env);\n");
        break;

      case IrInterfaceType:
        _sb.Append("  if (v.data != NULL && pino_rc_dec(&v.data->hdr)) {\n");
        _sb.Append("    v.vtable->drop(pino_cur_alloc, v.data);\n");
        _sb.Append("  }\n");
        break;
    }

    _sb.Append("}\n\n");
  }

  private void EmitEnvDropCallbackBody(IrEnvType et) {
    string envStruct = EnvStructCName(et.Id);
    _sb.Append("static void ").Append(EnvDropCallbackCName(et.Id)).Append("(PinoAlloc* a, void* raw) {\n");
    _sb.Append("  ").Append(envStruct).Append("* e = (").Append(envStruct).Append("*)raw;\n");
    for (int f = 0; f < et.Fields.Length; f++) {
      if (_types.GetType(et.Fields[f].Type).IsRcManaged) {
        _sb.Append("  ").Append(DropFnName(et.Fields[f].Type))
            .Append("(e->").Append(FieldCName(f, et.Fields[f].Name)).Append(");\n");
      }
    }

    _sb.Append("  pino_dealloc(a, e, sizeof(").Append(envStruct).Append("));\n");
    _sb.Append("}\n\n");
  }

  private void EmitEqBody(IrType t) {
    string cType = CType(t.Id);
    _sb.Append("static bool ").Append(EqFnName(t.Id)).Append('(').Append(cType).Append(" a, ")
        .Append(cType).Append(" b) {\n");

    switch (t) {
      case IrPrimitiveType { Kind: IrTypeKind.Bool or IrTypeKind.Int or IrTypeKind.Float or IrTypeKind.Rune }:
      case IrEnumType:
        _sb.Append("  return a == b;\n");
        break;

      case IrHandleType:
        _sb.Append("  return a.index == b.index && a.gen == b.gen;\n");
        break;

      case IrPrimitiveType { Kind: IrTypeKind.String }:
        _sb.Append("  return pino_str_eq(a, b);\n");
        break;

      case IrPrimitiveType { Kind: IrTypeKind.Regex }:
        _sb.Append("  if (a == b) return true;\n");
        _sb.Append("  if (a == NULL || b == NULL) return false;\n");
        _sb.Append("  return pino_str_eq(a->pattern, b->pattern);\n");
        break;

      case IrStructType st:
        _sb.Append("  (void)a; (void)b;\n");
        for (int f = 0; f < st.Fields.Length; f++) {
          string fn = FieldCName(f, st.Fields[f].Name);
          _sb.Append("  if (!").Append(EqFnName(st.Fields[f].Type))
              .Append("(a.").Append(fn).Append(", b.").Append(fn).Append(")) return false;\n");
        }
        _sb.Append("  return true;\n");
        break;

      case IrTupleType tt:
        _sb.Append("  (void)a; (void)b;\n");
        for (int f = 0; f < tt.Fields.Length; f++) {
          string fn = FieldCName(f, tt.Fields[f].Label);
          _sb.Append("  if (!").Append(EqFnName(tt.Fields[f].Type))
              .Append("(a.").Append(fn).Append(", b.").Append(fn).Append(")) return false;\n");
        }
        _sb.Append("  return true;\n");
        break;

      case IrUnionType ut: {
          string acc = ut.IsRecursiveUnion ? "->" : ".";
          if (ut.IsRecursiveUnion) {
            _sb.Append("  if (a == b) return true;\n");
            _sb.Append("  if (a == NULL || b == NULL) return false;\n");
          }

          _sb.Append("  if (a").Append(acc).Append("tag != b").Append(acc).Append("tag) return false;\n");
          _sb.Append("  switch (a").Append(acc).Append("tag) {\n");
          foreach (IrUnionVariant v in ut.Variants) {
            if (!HasRuntimeFields(v)) {
              continue;
            }

            _sb.Append("    case ").Append(v.TagOrdinal).Append(":\n");
            for (int f = 0; f < v.Fields.Length; f++) {
              if (IsVoidOrNever(v.Fields[f].Type)) {
                continue;
              }

              _sb.Append("      if (!").Append(EqFnName(v.Fields[f].Type))
                  .Append("(a").Append(acc).Append("u.v").Append(v.TagOrdinal).Append(".f").Append(f)
                  .Append(", b").Append(acc).Append("u.v").Append(v.TagOrdinal).Append(".f").Append(f)
                  .Append(")) return false;\n");
            }
            _sb.Append("      return true;\n");
          }
          _sb.Append("    default: return true;\n");
          _sb.Append("  }\n");
          break;
        }

      case IrVectorType vt:
        _sb.Append("  if (a == b) return true;\n");
        _sb.Append("  if (a->len != b->len) return false;\n");
        _sb.Append("  for (int64_t i = 0; i < a->len; i++) {\n");
        _sb.Append("    if (!").Append(EqFnName(vt.ElementType)).Append("(a->data[i], b->data[i])) return false;\n");
        _sb.Append("  }\n");
        _sb.Append("  return true;\n");
        break;

      case IrMapType mt:
        _sb.Append("  if (a == b) return true;\n");
        _sb.Append("  if (a->count != b->count) return false;\n");
        _sb.Append("  for (int64_t i = 0; i < a->entry_len; i++) {\n");
        _sb.Append("    if (a->entries[i].state != 1) continue;\n");
        _sb.Append("    int64_t slot = ").Append(MapFindSlotFnName(mt.Id)).Append("(b, a->entries[i].key, a->entries[i].hash);\n");
        _sb.Append("    if (slot < 0) return false;\n");
        _sb.Append("    if (!").Append(EqFnName(mt.ValueType))
            .Append("(a->entries[i].value, b->entries[slot].value)) return false;\n");
        _sb.Append("  }\n");
        _sb.Append("  return true;\n");
        break;

      case IrSetType setT:
        _sb.Append("  if (a == b) return true;\n");
        _sb.Append("  if (a->count != b->count) return false;\n");
        _sb.Append("  for (int64_t i = 0; i < a->entry_len; i++) {\n");
        _sb.Append("    if (a->entries[i].state != 1) continue;\n");
        _sb.Append("    if (").Append(SetFindSlotFnName(setT.Id))
            .Append("(b, a->entries[i].elem, a->entries[i].hash) < 0) return false;\n");
        _sb.Append("  }\n");
        _sb.Append("  return true;\n");
        break;

      case IrPoolType pt:
        _sb.Append("  if (a == b) return true;\n");
        _sb.Append("  if (a->live_count != b->live_count) return false;\n");
        _sb.Append("  for (int64_t i = 0; i < a->slot_len; i++) {\n");
        _sb.Append("    if (a->slots[i].state != 1) continue;\n");
        _sb.Append("    if (i >= b->slot_len || b->slots[i].state != 1 || b->slots[i].gen != a->slots[i].gen) return false;\n");
        _sb.Append("    if (!").Append(EqFnName(pt.ElementType))
            .Append("(a->slots[i].value, b->slots[i].value)) return false;\n");
        _sb.Append("  }\n");
        _sb.Append("  return true;\n");
        break;

      case IrClosureType:
        _sb.Append("  return a.fn_ptr == b.fn_ptr && a.env == b.env;\n");
        break;

      case IrInterfaceType:
        _sb.Append("  if (a.vtable != b.vtable) return false;\n");
        _sb.Append("  if (a.data == b.data) return true;\n");
        _sb.Append("  return a.vtable->equals(a.data, b.data);\n");
        break;
    }

    _sb.Append("}\n\n");
  }

  private void EmitHashBody(IrType t) {
    string cType = CType(t.Id);
    _sb.Append("static uint64_t ").Append(HashFnName(t.Id)).Append('(').Append(cType).Append(" v) {\n");

    switch (t) {
      case IrPrimitiveType { Kind: IrTypeKind.Bool }:
        _sb.Append("  return pino_hash_mix64((uint64_t)(v != 0));\n");
        break;

      case IrPrimitiveType { Kind: IrTypeKind.Int }:
        _sb.Append("  return pino_hash_mix64((uint64_t)v);\n");
        break;

      case IrPrimitiveType { Kind: IrTypeKind.Float }:
        _sb.Append("  return pino_hash_f64(v);\n");
        break;

      case IrPrimitiveType { Kind: IrTypeKind.Rune }:
      case IrEnumType:
        _sb.Append("  return pino_hash_mix64((uint64_t)(uint32_t)v);\n");
        break;

      case IrHandleType:
        _sb.Append("  return pino_hash_mix64(((uint64_t)v.index) | (((uint64_t)v.gen) << 32));\n");
        break;

      case IrPrimitiveType { Kind: IrTypeKind.String }:
        _sb.Append("  return pino_str_hash(v);\n");
        break;

      case IrPrimitiveType { Kind: IrTypeKind.Regex }:
        _sb.Append("  return v != NULL ? pino_str_hash(v->pattern) : 0ull;\n");
        break;

      case IrStructType st:
        _sb.Append("  (void)v;\n");
        _sb.Append("  uint64_t h = 0xCBF29CE484222325ull;\n");
        for (int f = 0; f < st.Fields.Length; f++) {
          _sb.Append("  h = pino_hash_combine(h, ").Append(HashFnName(st.Fields[f].Type))
              .Append("(v.").Append(FieldCName(f, st.Fields[f].Name)).Append("));\n");
        }
        _sb.Append("  return h;\n");
        break;

      case IrTupleType tt:
        _sb.Append("  (void)v;\n");
        _sb.Append("  uint64_t h = 0xCBF29CE484222325ull;\n");
        for (int f = 0; f < tt.Fields.Length; f++) {
          _sb.Append("  h = pino_hash_combine(h, ").Append(HashFnName(tt.Fields[f].Type))
              .Append("(v.").Append(FieldCName(f, tt.Fields[f].Label)).Append("));\n");
        }
        _sb.Append("  return h;\n");
        break;

      case IrUnionType ut: {
          string acc = ut.IsRecursiveUnion ? "->" : ".";
          if (ut.IsRecursiveUnion) {
            _sb.Append("  if (v == NULL) return 0ull;\n");
          }
          _sb.Append("  uint64_t h = pino_hash_mix64((uint64_t)(uint32_t)v").Append(acc).Append("tag);\n");
          _sb.Append("  switch (v").Append(acc).Append("tag) {\n");
          foreach (IrUnionVariant variant in ut.Variants) {
            if (!HasRuntimeFields(variant)) {
              continue;
            }

            _sb.Append("    case ").Append(variant.TagOrdinal).Append(":\n");
            for (int f = 0; f < variant.Fields.Length; f++) {
              if (IsVoidOrNever(variant.Fields[f].Type)) {
                continue;
              }

              _sb.Append("      h = pino_hash_combine(h, ").Append(HashFnName(variant.Fields[f].Type))
                  .Append("(v").Append(acc).Append("u.v").Append(variant.TagOrdinal).Append(".f").Append(f).Append("));\n");
            }
            _sb.Append("      break;\n");
          }
          _sb.Append("    default: break;\n");
          _sb.Append("  }\n");
          _sb.Append("  return h;\n");
          break;
        }

      case IrVectorType vt:
        _sb.Append("  uint64_t h = pino_hash_mix64((uint64_t)v->len);\n");
        _sb.Append("  for (int64_t i = 0; i < v->len; i++) {\n");
        _sb.Append("    h = pino_hash_combine(h, ").Append(HashFnName(vt.ElementType)).Append("(v->data[i]));\n");
        _sb.Append("  }\n");
        _sb.Append("  return h;\n");
        break;

      case IrMapType mt:
        _sb.Append("  uint64_t h = pino_hash_mix64((uint64_t)v->count);\n");
        _sb.Append("  for (int64_t i = 0; i < v->entry_len; i++) {\n");
        _sb.Append("    if (v->entries[i].state == 1) {\n");
        _sb.Append("      h ^= pino_hash_combine(v->entries[i].hash, ")
            .Append(HashFnName(mt.ValueType)).Append("(v->entries[i].value));\n");
        _sb.Append("    }\n");
        _sb.Append("  }\n");
        _sb.Append("  return h;\n");
        break;

      case IrSetType:
        _sb.Append("  uint64_t h = pino_hash_mix64((uint64_t)v->count);\n");
        _sb.Append("  for (int64_t i = 0; i < v->entry_len; i++) {\n");
        _sb.Append("    if (v->entries[i].state == 1) h ^= v->entries[i].hash;\n");
        _sb.Append("  }\n");
        _sb.Append("  return h;\n");
        break;

      case IrPoolType pt:
        _sb.Append("  uint64_t h = pino_hash_mix64((uint64_t)v->live_count);\n");
        _sb.Append("  for (int64_t i = 0; i < v->slot_len; i++) {\n");
        _sb.Append("    if (v->slots[i].state == 1) {\n");
        _sb.Append("      PinoHandle ph = {(uint32_t)i, v->slots[i].gen};\n");
        _sb.Append("      h = pino_hash_combine(h, pino_hash_mix64(((uint64_t)ph.index) | (((uint64_t)ph.gen) << 32)));\n");
        _sb.Append("      h = pino_hash_combine(h, ").Append(HashFnName(pt.ElementType)).Append("(v->slots[i].value));\n");
        _sb.Append("    }\n");
        _sb.Append("  }\n");
        _sb.Append("  return h;\n");
        break;

      case IrClosureType:
        _sb.Append("  return pino_hash_combine(pino_hash_mix64((uint64_t)(uintptr_t)v.fn_ptr), pino_hash_mix64((uint64_t)(uintptr_t)v.env));\n");
        break;

      case IrInterfaceType:
        _sb.Append("  return pino_hash_mix64((uint64_t)(uintptr_t)v.vtable);\n");
        break;
    }

    _sb.Append("}\n\n");
  }
}
