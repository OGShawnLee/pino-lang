using Pino.Compiler.IR;

namespace Pino.Compiler.CodeGen.C;

public sealed partial class CEmitter {
  // ---------------------------------------------------------------------------
  // Monomorphized collection helper names
  // ---------------------------------------------------------------------------

  private static string VecNewFnName(IrTypeId id) => $"pino_vec_t{id.Index}_new";
  private static string VecEnsureUniqueFnName(IrTypeId id) => $"pino_vec_t{id.Index}_ensure_unique";
  private static string VecPushFnName(IrTypeId id) => $"pino_vec_t{id.Index}_push";
  private static string VecPopFnName(IrTypeId id) => $"pino_vec_t{id.Index}_pop";
  private static string VecGetFnName(IrTypeId id) => $"pino_vec_t{id.Index}_get";
  private static string VecElemAddrFnName(IrTypeId id) => $"pino_vec_t{id.Index}_elem_addr";
  private static string VecSwapFnName(IrTypeId id) => $"pino_vec_t{id.Index}_swap";
  private static string VecContainsFnName(IrTypeId id) => $"pino_vec_t{id.Index}_contains";

  private static string MapEnsureUniqueFnName(IrTypeId id) => $"pino_map_t{id.Index}_ensure_unique";
  private static string MapRehashFnName(IrTypeId id) => $"pino_map_t{id.Index}_rehash";
  private static string MapFindSlotFnName(IrTypeId id) => $"pino_map_t{id.Index}_find_slot";
  private static string MapGetFnName(IrTypeId id) => $"pino_map_t{id.Index}_get";
  private static string MapElemAddrFnName(IrTypeId id) => $"pino_map_t{id.Index}_elem_addr";
  private static string MapSetFnName(IrTypeId id) => $"pino_map_t{id.Index}_set";
  private static string MapRemoveFnName(IrTypeId id) => $"pino_map_t{id.Index}_remove";
  private static string MapContainsFnName(IrTypeId id) => $"pino_map_t{id.Index}_contains";
  private static string MapKeysFnName(IrTypeId id) => $"pino_map_t{id.Index}_keys";
  private static string MapValuesFnName(IrTypeId id) => $"pino_map_t{id.Index}_values";

  private static string SetEnsureUniqueFnName(IrTypeId id) => $"pino_set_t{id.Index}_ensure_unique";
  private static string SetRehashFnName(IrTypeId id) => $"pino_set_t{id.Index}_rehash";
  private static string SetFindSlotFnName(IrTypeId id) => $"pino_set_t{id.Index}_find_slot";
  private static string SetAddFnName(IrTypeId id) => $"pino_set_t{id.Index}_add";
  private static string SetRemoveFnName(IrTypeId id) => $"pino_set_t{id.Index}_remove";
  private static string SetContainsFnName(IrTypeId id) => $"pino_set_t{id.Index}_contains";

  private static string PoolEnsureUniqueFnName(IrTypeId id) => $"pino_pool_t{id.Index}_ensure_unique";
  private static string PoolAddFnName(IrTypeId id) => $"pino_pool_t{id.Index}_add";
  private static string PoolRemoveFnName(IrTypeId id) => $"pino_pool_t{id.Index}_remove";
  private static string PoolGetFnName(IrTypeId id) => $"pino_pool_t{id.Index}_get";
  private static string PoolGetOrPanicFnName(IrTypeId id) => $"pino_pool_t{id.Index}_get_or_panic";
  private static string PoolElemAddrFnName(IrTypeId id) => $"pino_pool_t{id.Index}_elem_addr";
  private static string PoolHandlesFnName(IrTypeId id) => $"pino_pool_t{id.Index}_handles";
  private static string PoolClearFnName(IrTypeId id) => $"pino_pool_t{id.Index}_clear";
  private static string PoolContainsFnName(IrTypeId id) => $"pino_pool_t{id.Index}_contains";

  private static string IfaceEnsureUniqueFnName(IrTypeId id) => $"pino_iface_t{id.Index}_ensure_unique";

  private void EmitCollectionPrototypes() {
    _sb.Append("/* Monomorphized collection helper prototypes */\n");
    for (int i = 0; i < _types.Count; i++) {
      IrType t = _types.Types[i];
      switch (t) {
        case IrVectorType vt: {
            string vecPtr = CType(vt.Id);
            string elemT = CType(vt.ElementType);
            _sb.Append("static ").Append(vecPtr).Append(' ').Append(VecNewFnName(vt.Id)).Append("(int64_t cap);\n");
            _sb.Append("static void ").Append(VecEnsureUniqueFnName(vt.Id)).Append('(').Append(vecPtr).Append("* vp);\n");
            _sb.Append("static void ").Append(VecPushFnName(vt.Id)).Append('(').Append(vecPtr).Append("* vp, ").Append(elemT).Append(" val);\n");
            _sb.Append("static bool ").Append(VecPopFnName(vt.Id)).Append('(').Append(vecPtr).Append("* vp, ").Append(elemT).Append("* out);\n");
            _sb.Append("static ").Append(elemT).Append(' ').Append(VecGetFnName(vt.Id)).Append('(').Append(vecPtr).Append(" v, int64_t idx, PinoLoc loc);\n");
            _sb.Append("static ").Append(elemT).Append("* ").Append(VecElemAddrFnName(vt.Id)).Append('(').Append(vecPtr).Append("* vp, int64_t idx, PinoLoc loc);\n");
            _sb.Append("static void ").Append(VecSwapFnName(vt.Id)).Append('(').Append(vecPtr).Append("* vp, int64_t a, int64_t b, PinoLoc loc);\n");
            _sb.Append("static bool ").Append(VecContainsFnName(vt.Id)).Append('(').Append(vecPtr).Append(" v, ").Append(elemT).Append(" val);\n");
            break;
          }

        case IrMapType mt: {
            string mapPtr = CType(mt.Id);
            string keyT = CType(mt.KeyType);
            string valT = CType(mt.ValueType);
            _sb.Append("static void ").Append(MapEnsureUniqueFnName(mt.Id)).Append('(').Append(mapPtr).Append("* mp);\n");
            _sb.Append("static void ").Append(MapRehashFnName(mt.Id)).Append('(').Append(mapPtr).Append(" m, int64_t new_index_cap);\n");
            _sb.Append("static int64_t ").Append(MapFindSlotFnName(mt.Id)).Append('(').Append(mapPtr).Append(" m, ").Append(keyT).Append(" key, uint64_t h);\n");
            _sb.Append("static ").Append(valT).Append(' ').Append(MapGetFnName(mt.Id)).Append('(').Append(mapPtr).Append(" m, ").Append(keyT).Append(" key, PinoLoc loc);\n");
            _sb.Append("static ").Append(valT).Append("* ").Append(MapElemAddrFnName(mt.Id)).Append('(').Append(mapPtr).Append("* mp, ").Append(keyT).Append(" key, PinoLoc loc);\n");
            _sb.Append("static void ").Append(MapSetFnName(mt.Id)).Append('(').Append(mapPtr).Append("* mp, ").Append(keyT).Append(" key, ").Append(valT).Append(" val);\n");
            _sb.Append("static bool ").Append(MapRemoveFnName(mt.Id)).Append('(').Append(mapPtr).Append("* mp, ").Append(keyT).Append(" key, ").Append(valT).Append("* out);\n");
            _sb.Append("static bool ").Append(MapContainsFnName(mt.Id)).Append('(').Append(mapPtr).Append(" m, ").Append(keyT).Append(" key);\n");
            if (_vectorByElement.TryGetValue(mt.KeyType, out IrTypeId keysVecId)) {
              _sb.Append("static ").Append(CType(keysVecId)).Append(' ').Append(MapKeysFnName(mt.Id)).Append('(').Append(mapPtr).Append(" m);\n");
            }
            if (_vectorByElement.TryGetValue(mt.ValueType, out IrTypeId valsVecId)) {
              _sb.Append("static ").Append(CType(valsVecId)).Append(' ').Append(MapValuesFnName(mt.Id)).Append('(').Append(mapPtr).Append(" m);\n");
            }
            break;
          }

        case IrSetType setT: {
            string setPtr = CType(setT.Id);
            string elemT = CType(setT.ElementType);
            _sb.Append("static void ").Append(SetEnsureUniqueFnName(setT.Id)).Append('(').Append(setPtr).Append("* sp);\n");
            _sb.Append("static void ").Append(SetRehashFnName(setT.Id)).Append('(').Append(setPtr).Append(" s, int64_t new_index_cap);\n");
            _sb.Append("static int64_t ").Append(SetFindSlotFnName(setT.Id)).Append('(').Append(setPtr).Append(" s, ").Append(elemT).Append(" elem, uint64_t h);\n");
            _sb.Append("static void ").Append(SetAddFnName(setT.Id)).Append('(').Append(setPtr).Append("* sp, ").Append(elemT).Append(" elem);\n");
            _sb.Append("static void ").Append(SetRemoveFnName(setT.Id)).Append('(').Append(setPtr).Append("* sp, ").Append(elemT).Append(" elem);\n");
            _sb.Append("static bool ").Append(SetContainsFnName(setT.Id)).Append('(').Append(setPtr).Append(" s, ").Append(elemT).Append(" elem);\n");
            break;
          }

        case IrPoolType pt: {
            string poolPtr = CType(pt.Id);
            string elemT = CType(pt.ElementType);
            _sb.Append("static void ").Append(PoolEnsureUniqueFnName(pt.Id)).Append('(').Append(poolPtr).Append("* pp);\n");
            _sb.Append("static PinoHandle ").Append(PoolAddFnName(pt.Id)).Append('(').Append(poolPtr).Append("* pp, ").Append(elemT).Append(" val);\n");
            _sb.Append("static bool ").Append(PoolRemoveFnName(pt.Id)).Append('(').Append(poolPtr).Append("* pp, PinoHandle h, ").Append(elemT).Append("* out);\n");
            _sb.Append("static bool ").Append(PoolGetFnName(pt.Id)).Append('(').Append(poolPtr).Append(" p, PinoHandle h, ").Append(elemT).Append("* out);\n");
            _sb.Append("static ").Append(elemT).Append(' ').Append(PoolGetOrPanicFnName(pt.Id)).Append('(').Append(poolPtr).Append(" p, PinoHandle h, const char* name, PinoLoc loc);\n");
            _sb.Append("static ").Append(elemT).Append("* ").Append(PoolElemAddrFnName(pt.Id)).Append('(').Append(poolPtr).Append("* pp, PinoHandle h, const char* name, PinoLoc loc);\n");
            if (_vectorByElement.TryGetValue(_types.GetHandle(pt.ElementType), out IrTypeId hVecId)) {
              _sb.Append("static ").Append(CType(hVecId)).Append(' ').Append(PoolHandlesFnName(pt.Id)).Append('(').Append(poolPtr).Append(" p);\n");
            }
            _sb.Append("static void ").Append(PoolClearFnName(pt.Id)).Append('(').Append(poolPtr).Append("* pp);\n");
            _sb.Append("static bool ").Append(PoolContainsFnName(pt.Id)).Append('(').Append(poolPtr).Append(" p, PinoHandle h);\n");
            break;
          }

        case IrInterfaceType it:
          _sb.Append("static void ").Append(IfaceEnsureUniqueFnName(it.Id)).Append('(').Append(CType(it.Id)).Append("* ip);\n");
          break;
      }
    }

    _sb.Append('\n');
  }

  private void EmitCollectionHelperBodies() {
    _sb.Append("/* Monomorphized collection helper implementations */\n");
    for (int i = 0; i < _types.Count; i++) {
      IrType t = _types.Types[i];
      switch (t) {
        case IrVectorType vt:
          EmitVectorHelpers(vt);
          break;
        case IrMapType mt:
          EmitMapHelpers(mt);
          break;
        case IrSetType setT:
          EmitSetHelpers(setT);
          break;
        case IrPoolType pt:
          EmitPoolHelpers(pt);
          break;
        case IrInterfaceType it:
          EmitInterfaceEnsureUnique(it);
          break;
      }
    }
  }

  // ---------------------------------------------------------------------------
  // Vectors ([]T)
  // ---------------------------------------------------------------------------

  private void EmitVectorHelpers(IrVectorType vt) {
    string vecStruct = VectorStructCName(vt.Id);
    string vecPtr = $"{vecStruct}*";
    string elemT = CType(vt.ElementType);
    bool elemRc = _types.GetType(vt.ElementType).IsRcManaged;

    // new(cap)
    _sb.Append("static ").Append(vecPtr).Append(' ').Append(VecNewFnName(vt.Id)).Append("(int64_t cap) {\n");
    if (IsStringVector(vt.Id)) {
      _sb.Append("  return pino_vec_string_new(cap);\n");
    } else {
      _sb.Append("  ").Append(vecPtr).Append(" v = (").Append(vecPtr).Append(")PINO_RC_ALLOC(pino_cur_alloc, sizeof(")
          .Append(vecStruct).Append("), ").Append(vt.Id.Index).Append(", 0, \"vector\");\n");
      _sb.Append("  v->len = 0;\n");
      _sb.Append("  v->cap = 0;\n");
      _sb.Append("  v->data = NULL;\n");
      _sb.Append("  if (cap > 0) {\n");
      _sb.Append("    v->data = (").Append(elemT).Append("*)PINO_ALLOC(pino_cur_alloc, (size_t)cap * sizeof(").Append(elemT).Append("), \"vector elements\");\n");
      _sb.Append("    v->cap = cap;\n");
      _sb.Append("  }\n");
      _sb.Append("  return v;\n");
    }
    _sb.Append("}\n\n");

    // ensure_unique(vp)
    _sb.Append("static void ").Append(VecEnsureUniqueFnName(vt.Id)).Append('(').Append(vecPtr).Append("* vp) {\n");
    _sb.Append("  ").Append(vecPtr).Append(" v = *vp;\n");
    _sb.Append("  if (v->hdr.rc == 1) return;\n");
    _sb.Append("  int64_t new_cap = v->len > 0 ? v->len : 4;\n");
    _sb.Append("  ").Append(vecPtr).Append(" copy = ").Append(VecNewFnName(vt.Id)).Append("(new_cap);\n");
    if (elemRc) {
      _sb.Append("  for (int64_t i = 0; i < v->len; i++) {\n");
      _sb.Append("    copy->data[i] = v->data[i];\n");
      _sb.Append("    ").Append(IncFnName(vt.ElementType)).Append("(copy->data[i]);\n");
      _sb.Append("  }\n");
    } else {
      _sb.Append("  if (v->len > 0) memcpy(copy->data, v->data, (size_t)v->len * sizeof(").Append(elemT).Append("));\n");
    }
    _sb.Append("  copy->len = v->len;\n");
    _sb.Append("  ").Append(DropFnName(vt.Id)).Append("(v);\n");
    _sb.Append("  *vp = copy;\n");
    _sb.Append("}\n\n");

    // push(vp, val) - consumes val!
    _sb.Append("static void ").Append(VecPushFnName(vt.Id)).Append('(').Append(vecPtr).Append("* vp, ").Append(elemT).Append(" val) {\n");
    if (IsStringVector(vt.Id)) {
      _sb.Append("  pino_vec_string_push(vp, val);\n");
    } else {
      _sb.Append("  ").Append(VecEnsureUniqueFnName(vt.Id)).Append("(vp);\n");
      _sb.Append("  ").Append(vecPtr).Append(" v = *vp;\n");
      _sb.Append("  if (v->len == v->cap) {\n");
      _sb.Append("    int64_t new_cap = v->cap < 4 ? 4 : v->cap * 2;\n");
      _sb.Append("    v->data = (").Append(elemT).Append("*)PINO_REALLOC(pino_cur_alloc, v->data, (size_t)v->cap * sizeof(")
          .Append(elemT).Append("), (size_t)new_cap * sizeof(").Append(elemT).Append("), \"vector elements\");\n");
      _sb.Append("    v->cap = new_cap;\n");
      _sb.Append("  }\n");
      _sb.Append("  v->data[v->len++] = val;\n");
    }
    _sb.Append("}\n\n");

    // pop(vp, out) - moves popped element into *out
    _sb.Append("static bool ").Append(VecPopFnName(vt.Id)).Append('(').Append(vecPtr).Append("* vp, ").Append(elemT).Append("* out) {\n");
    _sb.Append("  ").Append(VecEnsureUniqueFnName(vt.Id)).Append("(vp);\n");
    _sb.Append("  ").Append(vecPtr).Append(" v = *vp;\n");
    _sb.Append("  if (v->len == 0) return false;\n");
    _sb.Append("  *out = v->data[--v->len];\n");
    _sb.Append("  return true;\n");
    _sb.Append("}\n\n");

    // get(v, idx, loc) - returns borrowed shallow copy (RcInsertPass adds RcInc)
    _sb.Append("static ").Append(elemT).Append(' ').Append(VecGetFnName(vt.Id)).Append('(').Append(vecPtr).Append(" v, int64_t idx, PinoLoc loc) {\n");
    _sb.Append("  if (idx < 0 || idx >= v->len) pino_panic_vec_index(idx, v->len, loc);\n");
    _sb.Append("  return v->data[idx];\n");
    _sb.Append("}\n\n");

    // elem_addr(vp, idx, loc)
    _sb.Append("static ").Append(elemT).Append("* ").Append(VecElemAddrFnName(vt.Id)).Append('(').Append(vecPtr).Append("* vp, int64_t idx, PinoLoc loc) {\n");
    _sb.Append("  ").Append(vecPtr).Append(" v = *vp;\n");
    _sb.Append("  if (idx < 0 || idx >= v->len) pino_panic_vec_index(idx, v->len, loc);\n");
    _sb.Append("  return &v->data[idx];\n");
    _sb.Append("}\n\n");

    // swap(vp, a, b, loc) - checks BOTH bounds first (VECLIB-E2)
    _sb.Append("static void ").Append(VecSwapFnName(vt.Id)).Append('(').Append(vecPtr).Append("* vp, int64_t a, int64_t b, PinoLoc loc) {\n");
    _sb.Append("  ").Append(VecEnsureUniqueFnName(vt.Id)).Append("(vp);\n");
    _sb.Append("  ").Append(vecPtr).Append(" v = *vp;\n");
    _sb.Append("  if (a < 0 || a >= v->len) pino_panic_vec_index(a, v->len, loc);\n");
    _sb.Append("  if (b < 0 || b >= v->len) pino_panic_vec_index(b, v->len, loc);\n");
    _sb.Append("  if (a == b) return;\n");
    _sb.Append("  ").Append(elemT).Append(" tmp = v->data[a];\n");
    _sb.Append("  v->data[a] = v->data[b];\n");
    _sb.Append("  v->data[b] = tmp;\n");
    _sb.Append("}\n\n");

    // contains(v, val)
    _sb.Append("static bool ").Append(VecContainsFnName(vt.Id)).Append('(').Append(vecPtr).Append(" v, ").Append(elemT).Append(" val) {\n");
    _sb.Append("  for (int64_t i = 0; i < v->len; i++) {\n");
    _sb.Append("    if (").Append(EqFnName(vt.ElementType)).Append("(v->data[i], val)) return true;\n");
    _sb.Append("  }\n");
    _sb.Append("  return false;\n");
    _sb.Append("}\n\n");
  }

  // ---------------------------------------------------------------------------
  // Ordered open-addressing maps (map[K, V], D-004, MAP-9)
  // ---------------------------------------------------------------------------

  private void EmitMapHelpers(IrMapType mt) {
    string mapStruct = MapStructCName(mt.Id);
    string mapPtr = $"{mapStruct}*";
    string entryStruct = MapEntryCName(mt.Id);
    string keyT = CType(mt.KeyType);
    string valT = CType(mt.ValueType);
    bool keyRc = _types.GetType(mt.KeyType).IsRcManaged;
    bool valRc = _types.GetType(mt.ValueType).IsRcManaged;

    // rehash(m, new_index_cap) - compacts tombstones & rebuilds open-addressed index table
    _sb.Append("static void ").Append(MapRehashFnName(mt.Id)).Append('(').Append(mapPtr).Append(" m, int64_t new_index_cap) {\n");
    _sb.Append("  if (new_index_cap < 8) new_index_cap = 8;\n");
    _sb.Append("  int64_t new_entry_cap = new_index_cap;\n");
    _sb.Append("  if (new_entry_cap < m->count + 4) new_entry_cap = m->count + 4;\n");
    _sb.Append("  int32_t* new_indices = (int32_t*)PINO_ALLOC(pino_cur_alloc, (size_t)new_index_cap * sizeof(int32_t), \"map indices\");\n");
    _sb.Append("  for (int64_t i = 0; i < new_index_cap; i++) new_indices[i] = -1;\n");
    _sb.Append("  ").Append(entryStruct).Append("* new_entries = (").Append(entryStruct).Append("*)PINO_ALLOC(pino_cur_alloc, (size_t)new_entry_cap * sizeof(").Append(entryStruct).Append("), \"map entries\");\n");
    _sb.Append("  int64_t mask = new_index_cap - 1;\n");
    _sb.Append("  int64_t dst = 0;\n");
    _sb.Append("  for (int64_t i = 0; i < m->entry_len; i++) {\n");
    _sb.Append("    if (m->entries[i].state != 1) continue;\n");
    _sb.Append("    new_entries[dst] = m->entries[i];\n");
    _sb.Append("    int64_t b = (int64_t)(new_entries[dst].hash & (uint64_t)mask);\n");
    _sb.Append("    while (new_indices[b] != -1) b = (b + 1) & mask;\n");
    _sb.Append("    new_indices[b] = (int32_t)dst;\n");
    _sb.Append("    dst++;\n");
    _sb.Append("  }\n");
    _sb.Append("  if (m->indices != NULL) pino_dealloc(pino_cur_alloc, m->indices, (size_t)m->index_cap * sizeof(int32_t));\n");
    _sb.Append("  if (m->entries != NULL) pino_dealloc(pino_cur_alloc, m->entries, (size_t)m->entry_cap * sizeof(").Append(entryStruct).Append("));\n");
    _sb.Append("  m->indices = new_indices;\n");
    _sb.Append("  m->entries = new_entries;\n");
    _sb.Append("  m->index_cap = new_index_cap;\n");
    _sb.Append("  m->entry_cap = new_entry_cap;\n");
    _sb.Append("  m->entry_len = dst;\n");
    _sb.Append("}\n\n");

    // ensure_unique(mp)
    _sb.Append("static void ").Append(MapEnsureUniqueFnName(mt.Id)).Append('(').Append(mapPtr).Append("* mp) {\n");
    _sb.Append("  ").Append(mapPtr).Append(" m = *mp;\n");
    _sb.Append("  if (m->hdr.rc == 1) return;\n");
    _sb.Append("  ").Append(mapPtr).Append(" copy = (").Append(mapPtr).Append(")PINO_RC_ALLOC(pino_cur_alloc, sizeof(")
        .Append(mapStruct).Append("), ").Append(mt.Id.Index).Append(", 0, \"map\");\n");
    _sb.Append("  copy->count = 0;\n");
    _sb.Append("  copy->entry_len = 0;\n");
    _sb.Append("  copy->entry_cap = 0;\n");
    _sb.Append("  copy->index_cap = 0;\n");
    _sb.Append("  copy->indices = NULL;\n");
    _sb.Append("  copy->entries = NULL;\n");
    _sb.Append("  if (m->count > 0) {\n");
    _sb.Append("    int64_t icap = 8;\n");
    _sb.Append("    while (icap * 3 <= m->count * 4) icap *= 2;\n");
    _sb.Append("    ").Append(MapRehashFnName(mt.Id)).Append("(copy, icap);\n");
    _sb.Append("    int64_t mask = copy->index_cap - 1;\n");
    _sb.Append("    for (int64_t i = 0; i < m->entry_len; i++) {\n");
    _sb.Append("      if (m->entries[i].state != 1) continue;\n");
    _sb.Append("      int64_t slot = copy->entry_len++;\n");
    _sb.Append("      copy->entries[slot] = m->entries[i];\n");
    if (keyRc) {
      _sb.Append("      ").Append(IncFnName(mt.KeyType)).Append("(copy->entries[slot].key);\n");
    }
    if (valRc) {
      _sb.Append("      ").Append(IncFnName(mt.ValueType)).Append("(copy->entries[slot].value);\n");
    }
    _sb.Append("      int64_t b = (int64_t)(copy->entries[slot].hash & (uint64_t)mask);\n");
    _sb.Append("      while (copy->indices[b] != -1) b = (b + 1) & mask;\n");
    _sb.Append("      copy->indices[b] = (int32_t)slot;\n");
    _sb.Append("      copy->count++;\n");
    _sb.Append("    }\n");
    _sb.Append("  }\n");
    _sb.Append("  ").Append(DropFnName(mt.Id)).Append("(m);\n");
    _sb.Append("  *mp = copy;\n");
    _sb.Append("}\n\n");

    // find_slot(m, key, h)
    _sb.Append("static int64_t ").Append(MapFindSlotFnName(mt.Id)).Append('(').Append(mapPtr).Append(" m, ").Append(keyT).Append(" key, uint64_t h) {\n");
    _sb.Append("  if (m->index_cap == 0 || m->count == 0) return -1;\n");
    _sb.Append("  int64_t mask = m->index_cap - 1;\n");
    _sb.Append("  int64_t b = (int64_t)(h & (uint64_t)mask);\n");
    _sb.Append("  for (int64_t probe = 0; probe < m->index_cap; probe++) {\n");
    _sb.Append("    int32_t idx = m->indices[b];\n");
    _sb.Append("    if (idx == -1) return -1;\n");
    _sb.Append("    if (idx >= 0 && m->entries[idx].state == 1 && m->entries[idx].hash == h && ")
        .Append(EqFnName(mt.KeyType)).Append("(m->entries[idx].key, key)) return idx;\n");
    _sb.Append("    b = (b + 1) & mask;\n");
    _sb.Append("  }\n");
    _sb.Append("  return -1;\n");
    _sb.Append("}\n\n");

    // get(m, key, loc) - borrowed return; panics MAP-E1 on missing key
    _sb.Append("static ").Append(valT).Append(' ').Append(MapGetFnName(mt.Id)).Append('(').Append(mapPtr).Append(" m, ").Append(keyT).Append(" key, PinoLoc loc) {\n");
    _sb.Append("  int64_t slot = ").Append(MapFindSlotFnName(mt.Id)).Append("(m, key, ").Append(HashFnName(mt.KeyType)).Append("(key));\n");
    _sb.Append("  if (slot < 0) pino_panic_map_key(").Append(ReprFnName(mt.KeyType)).Append("(key), loc);\n");
    _sb.Append("  return m->entries[slot].value;\n");
    _sb.Append("}\n\n");

    // elem_addr(mp, key, loc)
    _sb.Append("static ").Append(valT).Append("* ").Append(MapElemAddrFnName(mt.Id)).Append('(').Append(mapPtr).Append("* mp, ").Append(keyT).Append(" key, PinoLoc loc) {\n");
    _sb.Append("  ").Append(mapPtr).Append(" m = *mp;\n");
    _sb.Append("  int64_t slot = ").Append(MapFindSlotFnName(mt.Id)).Append("(m, key, ").Append(HashFnName(mt.KeyType)).Append("(key));\n");
    _sb.Append("  if (slot < 0) pino_panic_map_key(").Append(ReprFnName(mt.KeyType)).Append("(key), loc);\n");
    _sb.Append("  return &m->entries[slot].value;\n");
    _sb.Append("}\n\n");

    // set(mp, key, val) - consumes key AND val; drops replaced value and duplicate key on update!
    _sb.Append("static void ").Append(MapSetFnName(mt.Id)).Append('(').Append(mapPtr).Append("* mp, ").Append(keyT).Append(" key, ").Append(valT).Append(" val) {\n");
    _sb.Append("  ").Append(MapEnsureUniqueFnName(mt.Id)).Append("(mp);\n");
    _sb.Append("  ").Append(mapPtr).Append(" m = *mp;\n");
    _sb.Append("  uint64_t h = ").Append(HashFnName(mt.KeyType)).Append("(key);\n");
    _sb.Append("  int64_t existing = ").Append(MapFindSlotFnName(mt.Id)).Append("(m, key, h);\n");
    _sb.Append("  if (existing >= 0) {\n");
    if (valRc) {
      _sb.Append("    ").Append(DropFnName(mt.ValueType)).Append("(m->entries[existing].value);\n");
    }
    if (keyRc) {
      _sb.Append("    ").Append(DropFnName(mt.KeyType)).Append("(key);\n");
    }
    _sb.Append("    m->entries[existing].value = val;\n");
    _sb.Append("    return;\n");
    _sb.Append("  }\n");
    _sb.Append("  if (m->index_cap == 0) {\n");
    _sb.Append("    ").Append(MapRehashFnName(mt.Id)).Append("(m, 8);\n");
    _sb.Append("  } else if ((m->entry_len + 1) * 4 >= m->index_cap * 3 || m->entry_len >= m->entry_cap) {\n");
    _sb.Append("    int64_t target = (m->count + 1) * 4 >= m->index_cap * 3 ? m->index_cap * 2 : m->index_cap;\n");
    _sb.Append("    ").Append(MapRehashFnName(mt.Id)).Append("(m, target);\n");
    _sb.Append("  }\n");
    _sb.Append("  int64_t slot = m->entry_len++;\n");
    _sb.Append("  m->entries[slot].state = 1;\n");
    _sb.Append("  m->entries[slot].hash = h;\n");
    _sb.Append("  m->entries[slot].key = key;\n");
    _sb.Append("  m->entries[slot].value = val;\n");
    _sb.Append("  int64_t mask = m->index_cap - 1;\n");
    _sb.Append("  int64_t b = (int64_t)(h & (uint64_t)mask);\n");
    _sb.Append("  while (m->indices[b] >= 0) b = (b + 1) & mask;\n");
    _sb.Append("  m->indices[b] = (int32_t)slot;\n");
    _sb.Append("  m->count++;\n");
    _sb.Append("}\n\n");

    // remove(mp, key, out) - moves value into *out, drops key
    _sb.Append("static bool ").Append(MapRemoveFnName(mt.Id)).Append('(').Append(mapPtr).Append("* mp, ").Append(keyT).Append(" key, ").Append(valT).Append("* out) {\n");
    _sb.Append("  ").Append(MapEnsureUniqueFnName(mt.Id)).Append("(mp);\n");
    _sb.Append("  ").Append(mapPtr).Append(" m = *mp;\n");
    _sb.Append("  if (m->index_cap == 0 || m->count == 0) return false;\n");
    _sb.Append("  uint64_t h = ").Append(HashFnName(mt.KeyType)).Append("(key);\n");
    _sb.Append("  int64_t mask = m->index_cap - 1;\n");
    _sb.Append("  int64_t b = (int64_t)(h & (uint64_t)mask);\n");
    _sb.Append("  for (int64_t probe = 0; probe < m->index_cap; probe++) {\n");
    _sb.Append("    int32_t idx = m->indices[b];\n");
    _sb.Append("    if (idx == -1) return false;\n");
    _sb.Append("    if (idx >= 0 && m->entries[idx].state == 1 && m->entries[idx].hash == h && ")
        .Append(EqFnName(mt.KeyType)).Append("(m->entries[idx].key, key)) {\n");
    _sb.Append("      m->entries[idx].state = 2;\n");
    _sb.Append("      m->indices[b] = -2;\n");
    _sb.Append("      m->count--;\n");
    if (keyRc) {
      _sb.Append("      ").Append(DropFnName(mt.KeyType)).Append("(m->entries[idx].key);\n");
    }
    _sb.Append("      *out = m->entries[idx].value;\n");
    _sb.Append("      return true;\n");
    _sb.Append("    }\n");
    _sb.Append("    b = (b + 1) & mask;\n");
    _sb.Append("  }\n");
    _sb.Append("  return false;\n");
    _sb.Append("}\n\n");

    // contains(m, key)
    _sb.Append("static bool ").Append(MapContainsFnName(mt.Id)).Append('(').Append(mapPtr).Append(" m, ").Append(keyT).Append(" key) {\n");
    _sb.Append("  return ").Append(MapFindSlotFnName(mt.Id)).Append("(m, key, ").Append(HashFnName(mt.KeyType)).Append("(key)) >= 0;\n");
    _sb.Append("}\n\n");

    // keys(m) -> []K (owned elements!)
    if (_vectorByElement.TryGetValue(mt.KeyType, out IrTypeId keysVecId)) {
      string kVecPtr = CType(keysVecId);
      _sb.Append("static ").Append(kVecPtr).Append(' ').Append(MapKeysFnName(mt.Id)).Append('(').Append(mapPtr).Append(" m) {\n");
      _sb.Append("  if (m->count == 0) return &").Append(EmptySingletonCName(keysVecId)).Append(";\n");
      _sb.Append("  ").Append(kVecPtr).Append(" out = ").Append(VecNewFnName(keysVecId)).Append("(m->count);\n");
      _sb.Append("  for (int64_t i = 0; i < m->entry_len; i++) {\n");
      _sb.Append("    if (m->entries[i].state != 1) continue;\n");
      _sb.Append("    ").Append(keyT).Append(" k = m->entries[i].key;\n");
      if (keyRc) {
        _sb.Append("    ").Append(IncFnName(mt.KeyType)).Append("(k);\n");
      }
      _sb.Append("    out->data[out->len++] = k;\n");
      _sb.Append("  }\n");
      _sb.Append("  return out;\n");
      _sb.Append("}\n\n");
    }

    // values(m) -> []V (owned elements!)
    if (_vectorByElement.TryGetValue(mt.ValueType, out IrTypeId valsVecId)) {
      string vVecPtr = CType(valsVecId);
      _sb.Append("static ").Append(vVecPtr).Append(' ').Append(MapValuesFnName(mt.Id)).Append('(').Append(mapPtr).Append(" m) {\n");
      _sb.Append("  if (m->count == 0) return &").Append(EmptySingletonCName(valsVecId)).Append(";\n");
      _sb.Append("  ").Append(vVecPtr).Append(" out = ").Append(VecNewFnName(valsVecId)).Append("(m->count);\n");
      _sb.Append("  for (int64_t i = 0; i < m->entry_len; i++) {\n");
      _sb.Append("    if (m->entries[i].state != 1) continue;\n");
      _sb.Append("    ").Append(valT).Append(" v = m->entries[i].value;\n");
      if (valRc) {
        _sb.Append("    ").Append(IncFnName(mt.ValueType)).Append("(v);\n");
      }
      _sb.Append("    out->data[out->len++] = v;\n");
      _sb.Append("  }\n");
      _sb.Append("  return out;\n");
      _sb.Append("}\n\n");
    }
  }

  // ---------------------------------------------------------------------------
  // Ordered open-addressing sets (set[T], D-175, SET-6)
  // ---------------------------------------------------------------------------

  private void EmitSetHelpers(IrSetType setT) {
    string setStruct = SetStructCName(setT.Id);
    string setPtr = $"{setStruct}*";
    string entryStruct = SetEntryCName(setT.Id);
    string elemT = CType(setT.ElementType);
    bool elemRc = _types.GetType(setT.ElementType).IsRcManaged;

    // rehash
    _sb.Append("static void ").Append(SetRehashFnName(setT.Id)).Append('(').Append(setPtr).Append(" s, int64_t new_index_cap) {\n");
    _sb.Append("  if (new_index_cap < 8) new_index_cap = 8;\n");
    _sb.Append("  int64_t new_entry_cap = new_index_cap;\n");
    _sb.Append("  if (new_entry_cap < s->count + 4) new_entry_cap = s->count + 4;\n");
    _sb.Append("  int32_t* new_indices = (int32_t*)PINO_ALLOC(pino_cur_alloc, (size_t)new_index_cap * sizeof(int32_t), \"set indices\");\n");
    _sb.Append("  for (int64_t i = 0; i < new_index_cap; i++) new_indices[i] = -1;\n");
    _sb.Append("  ").Append(entryStruct).Append("* new_entries = (").Append(entryStruct).Append("*)PINO_ALLOC(pino_cur_alloc, (size_t)new_entry_cap * sizeof(").Append(entryStruct).Append("), \"set entries\");\n");
    _sb.Append("  int64_t mask = new_index_cap - 1;\n");
    _sb.Append("  int64_t dst = 0;\n");
    _sb.Append("  for (int64_t i = 0; i < s->entry_len; i++) {\n");
    _sb.Append("    if (s->entries[i].state != 1) continue;\n");
    _sb.Append("    new_entries[dst] = s->entries[i];\n");
    _sb.Append("    int64_t b = (int64_t)(new_entries[dst].hash & (uint64_t)mask);\n");
    _sb.Append("    while (new_indices[b] != -1) b = (b + 1) & mask;\n");
    _sb.Append("    new_indices[b] = (int32_t)dst;\n");
    _sb.Append("    dst++;\n");
    _sb.Append("  }\n");
    _sb.Append("  if (s->indices != NULL) pino_dealloc(pino_cur_alloc, s->indices, (size_t)s->index_cap * sizeof(int32_t));\n");
    _sb.Append("  if (s->entries != NULL) pino_dealloc(pino_cur_alloc, s->entries, (size_t)s->entry_cap * sizeof(").Append(entryStruct).Append("));\n");
    _sb.Append("  s->indices = new_indices;\n");
    _sb.Append("  s->entries = new_entries;\n");
    _sb.Append("  s->index_cap = new_index_cap;\n");
    _sb.Append("  s->entry_cap = new_entry_cap;\n");
    _sb.Append("  s->entry_len = dst;\n");
    _sb.Append("}\n\n");

    // ensure_unique
    _sb.Append("static void ").Append(SetEnsureUniqueFnName(setT.Id)).Append('(').Append(setPtr).Append("* sp) {\n");
    _sb.Append("  ").Append(setPtr).Append(" s = *sp;\n");
    _sb.Append("  if (s->hdr.rc == 1) return;\n");
    _sb.Append("  ").Append(setPtr).Append(" copy = (").Append(setPtr).Append(")PINO_RC_ALLOC(pino_cur_alloc, sizeof(")
        .Append(setStruct).Append("), ").Append(setT.Id.Index).Append(", 0, \"set\");\n");
    _sb.Append("  copy->count = 0;\n");
    _sb.Append("  copy->entry_len = 0;\n");
    _sb.Append("  copy->entry_cap = 0;\n");
    _sb.Append("  copy->index_cap = 0;\n");
    _sb.Append("  copy->indices = NULL;\n");
    _sb.Append("  copy->entries = NULL;\n");
    _sb.Append("  if (s->count > 0) {\n");
    _sb.Append("    int64_t icap = 8;\n");
    _sb.Append("    while (icap * 3 <= s->count * 4) icap *= 2;\n");
    _sb.Append("    ").Append(SetRehashFnName(setT.Id)).Append("(copy, icap);\n");
    _sb.Append("    int64_t mask = copy->index_cap - 1;\n");
    _sb.Append("    for (int64_t i = 0; i < s->entry_len; i++) {\n");
    _sb.Append("      if (s->entries[i].state != 1) continue;\n");
    _sb.Append("      int64_t slot = copy->entry_len++;\n");
    _sb.Append("      copy->entries[slot] = s->entries[i];\n");
    if (elemRc) {
      _sb.Append("      ").Append(IncFnName(setT.ElementType)).Append("(copy->entries[slot].elem);\n");
    }
    _sb.Append("      int64_t b = (int64_t)(copy->entries[slot].hash & (uint64_t)mask);\n");
    _sb.Append("      while (copy->indices[b] != -1) b = (b + 1) & mask;\n");
    _sb.Append("      copy->indices[b] = (int32_t)slot;\n");
    _sb.Append("      copy->count++;\n");
    _sb.Append("    }\n");
    _sb.Append("  }\n");
    _sb.Append("  ").Append(DropFnName(setT.Id)).Append("(s);\n");
    _sb.Append("  *sp = copy;\n");
    _sb.Append("}\n\n");

    // find_slot
    _sb.Append("static int64_t ").Append(SetFindSlotFnName(setT.Id)).Append('(').Append(setPtr).Append(" s, ").Append(elemT).Append(" elem, uint64_t h) {\n");
    _sb.Append("  if (s->index_cap == 0 || s->count == 0) return -1;\n");
    _sb.Append("  int64_t mask = s->index_cap - 1;\n");
    _sb.Append("  int64_t b = (int64_t)(h & (uint64_t)mask);\n");
    _sb.Append("  for (int64_t probe = 0; probe < s->index_cap; probe++) {\n");
    _sb.Append("    int32_t idx = s->indices[b];\n");
    _sb.Append("    if (idx == -1) return -1;\n");
    _sb.Append("    if (idx >= 0 && s->entries[idx].state == 1 && s->entries[idx].hash == h && ")
        .Append(EqFnName(setT.ElementType)).Append("(s->entries[idx].elem, elem)) return idx;\n");
    _sb.Append("    b = (b + 1) & mask;\n");
    _sb.Append("  }\n");
    _sb.Append("  return -1;\n");
    _sb.Append("}\n\n");

    // add(sp, elem) - consumes elem; drops duplicate!
    _sb.Append("static void ").Append(SetAddFnName(setT.Id)).Append('(').Append(setPtr).Append("* sp, ").Append(elemT).Append(" elem) {\n");
    _sb.Append("  ").Append(SetEnsureUniqueFnName(setT.Id)).Append("(sp);\n");
    _sb.Append("  ").Append(setPtr).Append(" s = *sp;\n");
    _sb.Append("  uint64_t h = ").Append(HashFnName(setT.ElementType)).Append("(elem);\n");
    _sb.Append("  if (").Append(SetFindSlotFnName(setT.Id)).Append("(s, elem, h) >= 0) {\n");
    if (elemRc) {
      _sb.Append("    ").Append(DropFnName(setT.ElementType)).Append("(elem);\n");
    }
    _sb.Append("    return;\n");
    _sb.Append("  }\n");
    _sb.Append("  if (s->index_cap == 0) {\n");
    _sb.Append("    ").Append(SetRehashFnName(setT.Id)).Append("(s, 8);\n");
    _sb.Append("  } else if ((s->entry_len + 1) * 4 >= s->index_cap * 3 || s->entry_len >= s->entry_cap) {\n");
    _sb.Append("    int64_t target = (s->count + 1) * 4 >= s->index_cap * 3 ? s->index_cap * 2 : s->index_cap;\n");
    _sb.Append("    ").Append(SetRehashFnName(setT.Id)).Append("(s, target);\n");
    _sb.Append("  }\n");
    _sb.Append("  int64_t slot = s->entry_len++;\n");
    _sb.Append("  s->entries[slot].state = 1;\n");
    _sb.Append("  s->entries[slot].hash = h;\n");
    _sb.Append("  s->entries[slot].elem = elem;\n");
    _sb.Append("  int64_t mask = s->index_cap - 1;\n");
    _sb.Append("  int64_t b = (int64_t)(h & (uint64_t)mask);\n");
    _sb.Append("  while (s->indices[b] >= 0) b = (b + 1) & mask;\n");
    _sb.Append("  s->indices[b] = (int32_t)slot;\n");
    _sb.Append("  s->count++;\n");
    _sb.Append("}\n\n");

    // remove(sp, elem) - borrows elem; drops stored elem if found
    _sb.Append("static void ").Append(SetRemoveFnName(setT.Id)).Append('(').Append(setPtr).Append("* sp, ").Append(elemT).Append(" elem) {\n");
    _sb.Append("  ").Append(SetEnsureUniqueFnName(setT.Id)).Append("(sp);\n");
    _sb.Append("  ").Append(setPtr).Append(" s = *sp;\n");
    _sb.Append("  if (s->index_cap == 0 || s->count == 0) return;\n");
    _sb.Append("  uint64_t h = ").Append(HashFnName(setT.ElementType)).Append("(elem);\n");
    _sb.Append("  int64_t mask = s->index_cap - 1;\n");
    _sb.Append("  int64_t b = (int64_t)(h & (uint64_t)mask);\n");
    _sb.Append("  for (int64_t probe = 0; probe < s->index_cap; probe++) {\n");
    _sb.Append("    int32_t idx = s->indices[b];\n");
    _sb.Append("    if (idx == -1) return;\n");
    _sb.Append("    if (idx >= 0 && s->entries[idx].state == 1 && s->entries[idx].hash == h && ")
        .Append(EqFnName(setT.ElementType)).Append("(s->entries[idx].elem, elem)) {\n");
    _sb.Append("      s->entries[idx].state = 2;\n");
    _sb.Append("      s->indices[b] = -2;\n");
    _sb.Append("      s->count--;\n");
    if (elemRc) {
      _sb.Append("      ").Append(DropFnName(setT.ElementType)).Append("(s->entries[idx].elem);\n");
    }
    _sb.Append("      return;\n");
    _sb.Append("    }\n");
    _sb.Append("    b = (b + 1) & mask;\n");
    _sb.Append("  }\n");
    _sb.Append("}\n\n");

    // contains(s, elem)
    _sb.Append("static bool ").Append(SetContainsFnName(setT.Id)).Append('(').Append(setPtr).Append(" s, ").Append(elemT).Append(" elem) {\n");
    _sb.Append("  return ").Append(SetFindSlotFnName(setT.Id)).Append("(s, elem, ").Append(HashFnName(setT.ElementType)).Append("(elem)) >= 0;\n");
    _sb.Append("}\n\n");
  }

  // ---------------------------------------------------------------------------
  // Dense generational pools (Pool[T], D-185, POOL-8..POOL-11)
  // ---------------------------------------------------------------------------

  private void EmitPoolHelpers(IrPoolType pt) {
    string poolStruct = PoolStructCName(pt.Id);
    string poolPtr = $"{poolStruct}*";
    string slotStruct = PoolSlotCName(pt.Id);
    string elemT = CType(pt.ElementType);
    bool elemRc = _types.GetType(pt.ElementType).IsRcManaged;

    // ensure_unique(pp) - preserves exact index, gen, state, free_head (POOL-3, EQ-14)
    _sb.Append("static void ").Append(PoolEnsureUniqueFnName(pt.Id)).Append('(').Append(poolPtr).Append("* pp) {\n");
    _sb.Append("  ").Append(poolPtr).Append(" p = *pp;\n");
    _sb.Append("  if (p->hdr.rc == 1) return;\n");
    _sb.Append("  ").Append(poolPtr).Append(" copy = (").Append(poolPtr).Append(")PINO_RC_ALLOC(pino_cur_alloc, sizeof(")
        .Append(poolStruct).Append("), ").Append(pt.Id.Index).Append(", 0, \"pool\");\n");
    _sb.Append("  copy->live_count = p->live_count;\n");
    _sb.Append("  copy->slot_len = p->slot_len;\n");
    _sb.Append("  copy->slot_cap = p->slot_len > 0 ? p->slot_len : 0;\n");
    _sb.Append("  copy->free_head = p->free_head;\n");
    _sb.Append("  copy->slots = NULL;\n");
    _sb.Append("  if (copy->slot_cap > 0) {\n");
    _sb.Append("    copy->slots = (").Append(slotStruct).Append("*)PINO_ALLOC(pino_cur_alloc, (size_t)copy->slot_cap * sizeof(").Append(slotStruct).Append("), \"pool slots\");\n");
    _sb.Append("    memcpy(copy->slots, p->slots, (size_t)p->slot_len * sizeof(").Append(slotStruct).Append("));\n");
    if (elemRc) {
      _sb.Append("    for (int64_t i = 0; i < copy->slot_len; i++) {\n");
      _sb.Append("      if (copy->slots[i].state == 1) ").Append(IncFnName(pt.ElementType)).Append("(copy->slots[i].value);\n");
      _sb.Append("    }\n");
    }
    _sb.Append("  }\n");
    _sb.Append("  ").Append(DropFnName(pt.Id)).Append("(p);\n");
    _sb.Append("  *pp = copy;\n");
    _sb.Append("}\n\n");

    // add(pp, val) - consumes val; LIFO reuse from free_head or append (POOL-8)
    _sb.Append("static PinoHandle ").Append(PoolAddFnName(pt.Id)).Append('(').Append(poolPtr).Append("* pp, ").Append(elemT).Append(" val) {\n");
    _sb.Append("  ").Append(PoolEnsureUniqueFnName(pt.Id)).Append("(pp);\n");
    _sb.Append("  ").Append(poolPtr).Append(" p = *pp;\n");
    _sb.Append("  if (p->free_head >= 0) {\n");
    _sb.Append("    int32_t idx = p->free_head;\n");
    _sb.Append("    p->free_head = p->slots[idx].next_free;\n");
    _sb.Append("    p->slots[idx].state = 1;\n");
    _sb.Append("    p->slots[idx].next_free = -1;\n");
    _sb.Append("    p->slots[idx].value = val;\n");
    _sb.Append("    p->live_count++;\n");
    _sb.Append("    return (PinoHandle){(uint32_t)idx, p->slots[idx].gen};\n");
    _sb.Append("  }\n");
    _sb.Append("  if (p->slot_len == p->slot_cap) {\n");
    _sb.Append("    int64_t new_cap = p->slot_cap < 4 ? 4 : p->slot_cap * 2;\n");
    _sb.Append("    p->slots = (").Append(slotStruct).Append("*)PINO_REALLOC(pino_cur_alloc, p->slots, (size_t)p->slot_cap * sizeof(")
        .Append(slotStruct).Append("), (size_t)new_cap * sizeof(").Append(slotStruct).Append("), \"pool slots\");\n");
    _sb.Append("    p->slot_cap = new_cap;\n");
    _sb.Append("  }\n");
    _sb.Append("  int64_t idx = p->slot_len++;\n");
    _sb.Append("  p->slots[idx].gen = 0;\n");
    _sb.Append("  p->slots[idx].state = 1;\n");
    _sb.Append("  p->slots[idx].next_free = -1;\n");
    _sb.Append("  p->slots[idx].value = val;\n");
    _sb.Append("  p->live_count++;\n");
    _sb.Append("  return (PinoHandle){(uint32_t)idx, 0u};\n");
    _sb.Append("}\n\n");

    // remove(pp, h, out) - moves value into *out, increments gen or retires at UINT32_MAX (POOL-8, POOL-10)
    _sb.Append("static bool ").Append(PoolRemoveFnName(pt.Id)).Append('(').Append(poolPtr).Append("* pp, PinoHandle h, ").Append(elemT).Append("* out) {\n");
    _sb.Append("  ").Append(PoolEnsureUniqueFnName(pt.Id)).Append("(pp);\n");
    _sb.Append("  ").Append(poolPtr).Append(" p = *pp;\n");
    _sb.Append("  if ((uint64_t)h.index >= (uint64_t)p->slot_len) return false;\n");
    _sb.Append("  ").Append(slotStruct).Append("* sl = &p->slots[h.index];\n");
    _sb.Append("  if (sl->state != 1 || sl->gen != h.gen) return false;\n");
    _sb.Append("  *out = sl->value;\n");
    _sb.Append("  p->live_count--;\n");
    _sb.Append("  if (sl->gen == UINT32_MAX) {\n");
    _sb.Append("    sl->state = 2;\n");
    _sb.Append("    sl->next_free = -1;\n");
    _sb.Append("  } else {\n");
    _sb.Append("    sl->gen++;\n");
    _sb.Append("    sl->state = 0;\n");
    _sb.Append("    sl->next_free = p->free_head;\n");
    _sb.Append("    p->free_head = (int32_t)h.index;\n");
    _sb.Append("  }\n");
    _sb.Append("  return true;\n");
    _sb.Append("}\n\n");

    // get(p, h, out) - returns OWNED copy in *out (PoolGet is NOT in IsReadOrProjection!)
    _sb.Append("static bool ").Append(PoolGetFnName(pt.Id)).Append('(').Append(poolPtr).Append(" p, PinoHandle h, ").Append(elemT).Append("* out) {\n");
    _sb.Append("  if ((uint64_t)h.index >= (uint64_t)p->slot_len) return false;\n");
    _sb.Append("  const ").Append(slotStruct).Append("* sl = &p->slots[h.index];\n");
    _sb.Append("  if (sl->state != 1 || sl->gen != h.gen) return false;\n");
    _sb.Append("  *out = sl->value;\n");
    if (elemRc) {
      _sb.Append("  ").Append(IncFnName(pt.ElementType)).Append("(*out);\n");
    }
    _sb.Append("  return true;\n");
    _sb.Append("}\n\n");

    // get_or_panic(p, h, name, loc) - borrowed return (PoolGetOrPanic IS in IsReadOrProjection)
    _sb.Append("static ").Append(elemT).Append(' ').Append(PoolGetOrPanicFnName(pt.Id)).Append('(').Append(poolPtr).Append(" p, PinoHandle h, const char* name, PinoLoc loc) {\n");
    _sb.Append("  if ((uint64_t)h.index >= (uint64_t)p->slot_len || p->slots[h.index].state != 1 || p->slots[h.index].gen != h.gen) {\n");
    _sb.Append("    pino_panic_stale_handle(name, loc);\n");
    _sb.Append("  }\n");
    _sb.Append("  return p->slots[h.index].value;\n");
    _sb.Append("}\n\n");

    // elem_addr(pp, h, name, loc)
    _sb.Append("static ").Append(elemT).Append("* ").Append(PoolElemAddrFnName(pt.Id)).Append('(').Append(poolPtr).Append("* pp, PinoHandle h, const char* name, PinoLoc loc) {\n");
    _sb.Append("  ").Append(poolPtr).Append(" p = *pp;\n");
    _sb.Append("  if ((uint64_t)h.index >= (uint64_t)p->slot_len || p->slots[h.index].state != 1 || p->slots[h.index].gen != h.gen) {\n");
    _sb.Append("    pino_panic_stale_handle(name, loc);\n");
    _sb.Append("  }\n");
    _sb.Append("  return &p->slots[h.index].value;\n");
    _sb.Append("}\n\n");

    // handles(p) -> []Handle[T] in increasing slot order (POOL-11, POOLLIB-6)
    if (_vectorByElement.TryGetValue(_types.GetHandle(pt.ElementType), out IrTypeId hVecId)) {
      string hVecPtr = CType(hVecId);
      _sb.Append("static ").Append(hVecPtr).Append(' ').Append(PoolHandlesFnName(pt.Id)).Append('(').Append(poolPtr).Append(" p) {\n");
      _sb.Append("  if (p->live_count == 0) return &").Append(EmptySingletonCName(hVecId)).Append(";\n");
      _sb.Append("  ").Append(hVecPtr).Append(" out = ").Append(VecNewFnName(hVecId)).Append("(p->live_count);\n");
      _sb.Append("  for (int64_t i = 0; i < p->slot_len; i++) {\n");
      _sb.Append("    if (p->slots[i].state == 1) {\n");
      _sb.Append("      out->data[out->len++] = (PinoHandle){(uint32_t)i, p->slots[i].gen};\n");
      _sb.Append("    }\n");
      _sb.Append("  }\n");
      _sb.Append("  return out;\n");
      _sb.Append("}\n\n");
    }

    // clear(pp) - walks slot_len-1 down to 0 so reuse pops 0, 1, 2... skipping retired (POOL-9)
    _sb.Append("static void ").Append(PoolClearFnName(pt.Id)).Append('(').Append(poolPtr).Append("* pp) {\n");
    _sb.Append("  ").Append(PoolEnsureUniqueFnName(pt.Id)).Append("(pp);\n");
    _sb.Append("  ").Append(poolPtr).Append(" p = *pp;\n");
    _sb.Append("  p->free_head = -1;\n");
    _sb.Append("  p->live_count = 0;\n");
    _sb.Append("  for (int64_t i = p->slot_len - 1; i >= 0; i--) {\n");
    _sb.Append("    if (p->slots[i].state == 2) continue;\n");
    _sb.Append("    if (p->slots[i].state == 1) {\n");
    if (elemRc) {
      _sb.Append("      ").Append(DropFnName(pt.ElementType)).Append("(p->slots[i].value);\n");
    }
    _sb.Append("      if (p->slots[i].gen == UINT32_MAX) {\n");
    _sb.Append("        p->slots[i].state = 2;\n");
    _sb.Append("        p->slots[i].next_free = -1;\n");
    _sb.Append("        continue;\n");
    _sb.Append("      }\n");
    _sb.Append("      p->slots[i].gen++;\n");
    _sb.Append("    }\n");
    _sb.Append("    p->slots[i].state = 0;\n");
    _sb.Append("    p->slots[i].next_free = p->free_head;\n");
    _sb.Append("    p->free_head = (int32_t)i;\n");
    _sb.Append("  }\n");
    _sb.Append("}\n\n");

    // contains(p, h)
    _sb.Append("static bool ").Append(PoolContainsFnName(pt.Id)).Append('(').Append(poolPtr).Append(" p, PinoHandle h) {\n");
    _sb.Append("  return (uint64_t)h.index < (uint64_t)p->slot_len && p->slots[h.index].state == 1 && p->slots[h.index].gen == h.gen;\n");
    _sb.Append("}\n\n");
  }

  private void EmitInterfaceEnsureUnique(IrInterfaceType it) {
    string ifaceT = CType(it.Id);
    _sb.Append("static void ").Append(IfaceEnsureUniqueFnName(it.Id)).Append('(').Append(ifaceT).Append("* ip) {\n");
    _sb.Append("  if (ip->data->hdr.rc == 1) return;\n");
    _sb.Append("  PinoIfaceBox* old_box = ip->data;\n");
    _sb.Append("  ip->data = ip->vtable->clone(pino_cur_alloc, old_box);\n");
    _sb.Append("  if (pino_rc_dec(&old_box->hdr)) {\n");
    _sb.Append("    ip->vtable->drop(pino_cur_alloc, old_box);\n");
    _sb.Append("  }\n");
    _sb.Append("}\n\n");
  }
}
