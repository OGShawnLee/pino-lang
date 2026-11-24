using Pino.Compiler.IR;

namespace Pino.Compiler.CodeGen.C;

public sealed partial class CEmitter {
  // ---------------------------------------------------------------------------
  // Value formatting helpers (`str(x)`, `println(...)`, MAP-9, SET-6, POOL-13, IQ-12)
  // ---------------------------------------------------------------------------

  private void EmitFormattingHelperBodies() {
    _sb.Append("/* Per-type formatting helpers (owned PinoString results) */\n");

    for (int i = 0; i < _types.Count; i++) {
      IrType t = _types.Types[i];
      if (t.Kind is IrTypeKind.Void or IrTypeKind.Never or IrTypeKind.Ptr or IrTypeKind.Cell or IrTypeKind.Env) {
        continue;
      }

      EmitStrBody(t);
      EmitReprBody(t);
    }
  }

  private void EmitStrBody(IrType t) {
    string cType = CType(t.Id);
    _sb.Append("static PinoString ").Append(StrFnName(t.Id)).Append('(').Append(cType).Append(" v) {\n");

    switch (t) {
      case IrPrimitiveType { Kind: IrTypeKind.Bool }:
        _sb.Append("  return pino_bool_to_str(v != 0);\n");
        break;

      case IrPrimitiveType { Kind: IrTypeKind.Int }:
        _sb.Append("  return pino_i64_to_str(v);\n");
        break;

      case IrPrimitiveType { Kind: IrTypeKind.Float }:
        _sb.Append("  return pino_f64_to_str(v);\n");
        break;

      case IrPrimitiveType { Kind: IrTypeKind.Rune }:
        _sb.Append("  return pino_rune_to_str(v);\n");
        break;

      case IrPrimitiveType { Kind: IrTypeKind.String }:
        _sb.Append("  return pino_str_retain(v);\n");
        break;

      case IrHandleType:
        _sb.Append("  return pino_handle_to_str(v);\n");
        break;

      case IrEnumType et: {
          string baseName = StripGenericArgs(et.Name);
          _sb.Append("  switch (v) {\n");
          for (int m = 0; m < et.MemberNames.Length; m++) {
            string label = $"{baseName}::{et.MemberNames[m]}";
            _sb.Append("    case ").Append(m).Append(": return PINO_STR_LIT(\"")
                .Append(EscapeCString(label)).Append("\");\n");
          }
          _sb.Append("    default: return PINO_STR_LIT(\"").Append(EscapeCString(baseName)).Append("::?\");\n");
          _sb.Append("  }\n");
          break;
        }

      case IrPrimitiveType { Kind: IrTypeKind.Regex }:
        _sb.Append("  PinoFmtBuf fb;\n");
        _sb.Append("  pino_fb_init(&fb, v != NULL ? v->pattern.byte_len + 12 : 12);\n");
        _sb.Append("  pino_fb_append_cstr(&fb, \"regex(\\\"\");\n");
        _sb.Append("  if (v != NULL) pino_fb_append_bytes(&fb, v->pattern.ptr, v->pattern.byte_len);\n");
        _sb.Append("  pino_fb_append_cstr(&fb, \"\\\")\");\n");
        _sb.Append("  return pino_fb_finish(&fb);\n");
        break;

      case IrVectorType vt:
        _sb.Append("  if (v->len == 0) return PINO_STR_LIT(\"[]\");\n");
        _sb.Append("  PinoFmtBuf fb;\n");
        _sb.Append("  pino_fb_init(&fb, 32);\n");
        _sb.Append("  pino_fb_append_cstr(&fb, \"[\");\n");
        _sb.Append("  for (int64_t i = 0; i < v->len; i++) {\n");
        _sb.Append("    if (i > 0) pino_fb_append_cstr(&fb, \", \");\n");
        _sb.Append("    pino_fb_append_str_owned(&fb, ").Append(ReprFnName(vt.ElementType)).Append("(v->data[i]));\n");
        _sb.Append("  }\n");
        _sb.Append("  pino_fb_append_cstr(&fb, \"]\");\n");
        _sb.Append("  return pino_fb_finish(&fb);\n");
        break;

      case IrMapType mt: {
          string header = $"map[{_types.GetType(mt.KeyType).Name}, {_types.GetType(mt.ValueType).Name}]";
          _sb.Append("  if (v->count == 0) return PINO_STR_LIT(\"").Append(EscapeCString(header)).Append(" { }\");\n");
          _sb.Append("  PinoFmtBuf fb;\n");
          _sb.Append("  pino_fb_init(&fb, 48);\n");
          _sb.Append("  pino_fb_append_cstr(&fb, \"").Append(EscapeCString(header)).Append(" { \");\n");
          _sb.Append("  bool first = true;\n");
          _sb.Append("  for (int64_t i = 0; i < v->entry_len; i++) {\n");
          _sb.Append("    if (v->entries[i].state != 1) continue;\n");
          _sb.Append("    if (!first) pino_fb_append_cstr(&fb, \", \");\n");
          _sb.Append("    first = false;\n");
          _sb.Append("    pino_fb_append_str_owned(&fb, ").Append(ReprFnName(mt.KeyType)).Append("(v->entries[i].key));\n");
          _sb.Append("    pino_fb_append_cstr(&fb, \" -> \");\n");
          _sb.Append("    pino_fb_append_str_owned(&fb, ").Append(ReprFnName(mt.ValueType)).Append("(v->entries[i].value));\n");
          _sb.Append("  }\n");
          _sb.Append("  pino_fb_append_cstr(&fb, \" }\");\n");
          _sb.Append("  return pino_fb_finish(&fb);\n");
          break;
        }

      case IrSetType setT: {
          string header = $"set[{_types.GetType(setT.ElementType).Name}]";
          _sb.Append("  if (v->count == 0) return PINO_STR_LIT(\"").Append(EscapeCString(header)).Append(" { }\");\n");
          _sb.Append("  PinoFmtBuf fb;\n");
          _sb.Append("  pino_fb_init(&fb, 48);\n");
          _sb.Append("  pino_fb_append_cstr(&fb, \"").Append(EscapeCString(header)).Append(" { \");\n");
          _sb.Append("  bool first = true;\n");
          _sb.Append("  for (int64_t i = 0; i < v->entry_len; i++) {\n");
          _sb.Append("    if (v->entries[i].state != 1) continue;\n");
          _sb.Append("    if (!first) pino_fb_append_cstr(&fb, \", \");\n");
          _sb.Append("    first = false;\n");
          _sb.Append("    pino_fb_append_str_owned(&fb, ").Append(ReprFnName(setT.ElementType)).Append("(v->entries[i].elem));\n");
          _sb.Append("  }\n");
          _sb.Append("  pino_fb_append_cstr(&fb, \" }\");\n");
          _sb.Append("  return pino_fb_finish(&fb);\n");
          break;
        }

      case IrPoolType pt: {
          string header = $"Pool[{_types.GetType(pt.ElementType).Name}]";
          _sb.Append("  if (v->live_count == 0) return PINO_STR_LIT(\"").Append(EscapeCString(header)).Append(" { }\");\n");
          _sb.Append("  PinoFmtBuf fb;\n");
          _sb.Append("  pino_fb_init(&fb, 48);\n");
          _sb.Append("  pino_fb_append_cstr(&fb, \"").Append(EscapeCString(header)).Append(" { \");\n");
          _sb.Append("  bool first = true;\n");
          _sb.Append("  for (int64_t i = 0; i < v->slot_len; i++) {\n");
          _sb.Append("    if (v->slots[i].state != 1) continue;\n");
          _sb.Append("    if (!first) pino_fb_append_cstr(&fb, \", \");\n");
          _sb.Append("    first = false;\n");
          _sb.Append("    PinoHandle ph = {(uint32_t)i, v->slots[i].gen};\n");
          _sb.Append("    pino_fb_append_str_owned(&fb, pino_handle_to_str(ph));\n");
          _sb.Append("    pino_fb_append_cstr(&fb, \" -> \");\n");
          _sb.Append("    pino_fb_append_str_owned(&fb, ").Append(ReprFnName(pt.ElementType)).Append("(v->slots[i].value));\n");
          _sb.Append("  }\n");
          _sb.Append("  pino_fb_append_cstr(&fb, \" }\");\n");
          _sb.Append("  return pino_fb_finish(&fb);\n");
          break;
        }

      case IrStructType st: {
          string baseName = st.Definition?.Name ?? StripGenericArgs(st.Name);
          _sb.Append("  (void)v;\n");
          if (st.Fields.IsEmpty) {
            _sb.Append("  return PINO_STR_LIT(\"").Append(EscapeCString(baseName)).Append(" { }\");\n");
          } else {
            _sb.Append("  PinoFmtBuf fb;\n");
            _sb.Append("  pino_fb_init(&fb, 48);\n");
            _sb.Append("  pino_fb_append_cstr(&fb, \"").Append(EscapeCString(baseName)).Append(" { \");\n");
            for (int f = 0; f < st.Fields.Length; f++) {
              if (f > 0) {
                _sb.Append("  pino_fb_append_cstr(&fb, \", \");\n");
              }
              _sb.Append("  pino_fb_append_cstr(&fb, \"").Append(EscapeCString(st.Fields[f].Name)).Append(": \");\n");
              _sb.Append("  pino_fb_append_str_owned(&fb, ").Append(ReprFnName(st.Fields[f].Type))
                  .Append("(v.").Append(FieldCName(f, st.Fields[f].Name)).Append("));\n");
            }
            _sb.Append("  pino_fb_append_cstr(&fb, \" }\");\n");
            _sb.Append("  return pino_fb_finish(&fb);\n");
          }
          break;
        }

      case IrTupleType tt:
        _sb.Append("  (void)v;\n");
        if (tt.Fields.IsEmpty) {
          _sb.Append("  return PINO_STR_LIT(\"@()\");\n");
        } else {
          _sb.Append("  PinoFmtBuf fb;\n");
          _sb.Append("  pino_fb_init(&fb, 32);\n");
          _sb.Append("  pino_fb_append_cstr(&fb, \"@(\");\n");
          for (int f = 0; f < tt.Fields.Length; f++) {
            if (f > 0) {
              _sb.Append("  pino_fb_append_cstr(&fb, \", \");\n");
            }
            _sb.Append("  pino_fb_append_cstr(&fb, \"").Append(EscapeCString(tt.Fields[f].Label)).Append(": \");\n");
            _sb.Append("  pino_fb_append_str_owned(&fb, ").Append(ReprFnName(tt.Fields[f].Type))
                .Append("(v.").Append(FieldCName(f, tt.Fields[f].Label)).Append("));\n");
          }
          _sb.Append("  pino_fb_append_cstr(&fb, \")\");\n");
          _sb.Append("  return pino_fb_finish(&fb);\n");
        }
        break;

      case IrUnionType ut: {
          string baseName = ut.Definition?.Name ?? StripGenericArgs(ut.Name);
          string acc = ut.IsRecursiveUnion ? "->" : ".";
          if (ut.IsRecursiveUnion) {
            _sb.Append("  if (v == NULL) return PINO_STR_LIT(\"<null>\");\n");
          }
          _sb.Append("  switch (v").Append(acc).Append("tag) {\n");
          foreach (IrUnionVariant variant in ut.Variants) {
            string prefix = $"{baseName}::{variant.Name}";
            _sb.Append("    case ").Append(variant.TagOrdinal).Append(": {\n");
            if (!HasRuntimeFields(variant)) {
              _sb.Append("      return PINO_STR_LIT(\"").Append(EscapeCString(prefix)).Append("\");\n");
            } else {
              _sb.Append("      PinoFmtBuf fb;\n");
              _sb.Append("      pino_fb_init(&fb, 48);\n");
              _sb.Append("      pino_fb_append_cstr(&fb, \"").Append(EscapeCString(prefix)).Append("(\");\n");
              bool firstField = true;
              for (int f = 0; f < variant.Fields.Length; f++) {
                if (IsVoidOrNever(variant.Fields[f].Type)) {
                  continue;
                }

                if (!firstField) {
                  _sb.Append("      pino_fb_append_cstr(&fb, \", \");\n");
                }
                firstField = false;
                _sb.Append("      pino_fb_append_str_owned(&fb, ").Append(ReprFnName(variant.Fields[f].Type))
                    .Append("(v").Append(acc).Append("u.v").Append(variant.TagOrdinal).Append(".f").Append(f).Append("));\n");
              }
              _sb.Append("      pino_fb_append_cstr(&fb, \")\");\n");
              _sb.Append("      return pino_fb_finish(&fb);\n");
            }
            _sb.Append("    }\n");
          }
          _sb.Append("    default: return PINO_STR_LIT(\"").Append(EscapeCString(baseName)).Append("::?\");\n");
          _sb.Append("  }\n");
          break;
        }

      case IrInterfaceType:
        _sb.Append("  return v.vtable->to_string(pino_cur_alloc, v.data);\n");
        break;

      case IrClosureType:
        _sb.Append("  (void)v;\n");
        _sb.Append("  return PINO_STR_LIT(\"<function>\");\n");
        break;
    }

    _sb.Append("}\n\n");
  }

  private void EmitReprBody(IrType t) {
    string cType = CType(t.Id);
    _sb.Append("static PinoString ").Append(ReprFnName(t.Id)).Append('(').Append(cType).Append(" v) {\n");
    if (t.Kind == IrTypeKind.String) {
      _sb.Append("  PinoFmtBuf fb;\n");
      _sb.Append("  pino_fb_init(&fb, v.byte_len + 2);\n");
      _sb.Append("  pino_fb_append_cstr(&fb, \"\\\"\");\n");
      _sb.Append("  pino_fb_append_bytes(&fb, v.ptr, v.byte_len);\n");
      _sb.Append("  pino_fb_append_cstr(&fb, \"\\\"\");\n");
      _sb.Append("  return pino_fb_finish(&fb);\n");
    } else {
      _sb.Append("  return ").Append(StrFnName(t.Id)).Append("(v);\n");
    }

    _sb.Append("}\n\n");
  }

  private static string StripGenericArgs(string typeName) {
    int bracket = typeName.IndexOf('[', StringComparison.Ordinal);
    return bracket > 0 ? typeName[..bracket] : typeName;
  }
}
