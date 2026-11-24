using System.Globalization;
using System.Text;

using Pino.Compiler.IR;

namespace Pino.Vm;

/// <summary>Exact print forms of <c>D-022</c>, <c>D-186</c>, <c>MAP-9</c>, <c>SET-6</c>, <c>POOL-13</c> and <c>str()</c>.</summary>
public sealed partial class PinoVm {
  /// <summary>
  /// Formats a float the way the legacy .NET oracle does: shortest round-trip digits in invariant culture (including its
  /// exponent form such as <c>1E+20</c> and <c>1E-05</c>, <c>NaN</c> and <c>Infinity</c>), with <c>.0</c> added to a whole
  /// value in fixed form (<c>D-022</c>).
  /// </summary>
  public static string FormatFloat(double value) {
    if (double.IsNaN(value)) {
      return "NaN";
    }

    if (double.IsPositiveInfinity(value)) {
      return "Infinity";
    }

    if (double.IsNegativeInfinity(value)) {
      return "-Infinity";
    }

    string text = value.ToString(CultureInfo.InvariantCulture);
    return text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal) ? text : text + ".0";
  }

  private string Format(PinoValue value, IrTypeId typeId, bool quoteStrings) {
    var sb = new StringBuilder();
    AppendValue(sb, value, typeId, quoteStrings);
    return sb.ToString();
  }

  private static string BaseName(string name) {
    int bracket = name.IndexOf('[', StringComparison.Ordinal);
    return bracket >= 0 ? name[..bracket] : name;
  }

  private void AppendValue(StringBuilder sb, PinoValue value, IrTypeId typeId, bool quoteStrings) {
    IrType type = _types.GetType(typeId);
    switch (type.Kind) {
      case IrTypeKind.Bool:
        sb.Append(value.AsBool ? "true" : "false");
        break;

      case IrTypeKind.Int:
        sb.Append(value.Raw.ToString(CultureInfo.InvariantCulture));
        break;

      case IrTypeKind.Float:
        sb.Append(FormatFloat(value.AsFloat));
        break;

      case IrTypeKind.Rune: {
          int cp = value.AsRune;
          sb.Append(Rune.IsValid(cp) ? char.ConvertFromUtf32(cp) : "�");
          break;
        }

      case IrTypeKind.Enum: {
          var et = (IrEnumType) type;
          int ordinal = value.AsEnum;
          sb.Append(BaseName(et.Name)).Append("::")
              .Append((uint) ordinal < (uint) et.MemberNames.Length ? et.MemberNames[ordinal] : ordinal.ToString(CultureInfo.InvariantCulture));
          break;
        }

      case IrTypeKind.Handle:
        sb.Append(CultureInfo.InvariantCulture, $"Handle({value.HandleIndex}, {value.HandleGeneration})");
        break;

      case IrTypeKind.String: {
          string text = UseObject((PinoVmString) value.Obj!, "string formatting").ToString();
          if (quoteStrings) {
            sb.Append('"').Append(text).Append('"');
          } else {
            sb.Append(text);
          }

          break;
        }

      case IrTypeKind.Regex:
        sb.Append(UseObject((PinoVmRegex) value.Obj!, "regex formatting").Pattern.ToString());
        break;

      case IrTypeKind.Struct: {
          var st = (IrStructType) type;
          PinoValue[] fields = Fields(value);
          sb.Append(BaseName(st.Name)).Append(" {");
          for (int i = 0; i < st.Fields.Length; i++) {
            sb.Append(i == 0 ? " " : ", ").Append(st.Fields[i].Name).Append(": ");
            AppendValue(sb, fields[i], st.Fields[i].Type, quoteStrings: true);
          }

          sb.Append(st.Fields.Length == 0 ? "}" : " }");
          break;
        }

      case IrTypeKind.Tuple: {
          var tt = (IrTupleType) type;
          PinoValue[] fields = Fields(value);
          sb.Append("@(");
          for (int i = 0; i < tt.Fields.Length; i++) {
            sb.Append(i == 0 ? string.Empty : ", ").Append(tt.Fields[i].Label).Append(": ");
            AppendValue(sb, fields[i], tt.Fields[i].Type, quoteStrings: true);
          }

          sb.Append(')');
          break;
        }

      case IrTypeKind.Union: {
          var ut = (IrUnionType) type;
          int tag;
          PinoValue[] payload;
          if (value.Obj is PinoVmUnion boxed) {
            UseObject(boxed, "union formatting");
            tag = boxed.Tag;
            payload = boxed.Fields;
          } else {
            tag = (int) value.Raw;
            payload = value.Obj as PinoValue[] ?? s_noFields;
          }

          IrUnionVariant variant = VariantByTag(ut, tag);
          sb.Append(BaseName(ut.Name)).Append("::").Append(variant.Name);
          if (variant.Fields.Length > 0) {
            sb.Append('(');
            for (int i = 0; i < variant.Fields.Length; i++) {
              if (i > 0) {
                sb.Append(", ");
              }

              AppendValue(sb, payload[i], variant.Fields[i].Type, quoteStrings: true);
            }

            sb.Append(')');
          }

          break;
        }

      case IrTypeKind.Vector: {
          var vec = UseObject((PinoVmVec) value.Obj!, "vector formatting");
          IrTypeId element = ((IrVectorType) type).ElementType;
          sb.Append('[');
          for (int i = 0; i < vec.Count; i++) {
            if (i > 0) {
              sb.Append(", ");
            }

            AppendValue(sb, vec.Items[i], element, quoteStrings: true);
          }

          sb.Append(']');
          break;
        }

      case IrTypeKind.Map: {
          var map = UseObject((PinoVmMap) value.Obj!, "map formatting");
          var mt = (IrMapType) type;
          sb.Append(mt.Name).Append(" {");
          bool first = true;
          for (int slot = 0; slot < map.SlotLength; slot++) {
            if (!map.SlotIsLive(slot)) {
              continue;
            }

            sb.Append(first ? " " : ", ");
            first = false;
            AppendValue(sb, map.EntryAt(slot).Key, mt.KeyType, quoteStrings: true);
            sb.Append(" -> ");
            AppendValue(sb, map.EntryAt(slot).Value, mt.ValueType, quoteStrings: true);
          }

          sb.Append(first ? " }" : " }");
          break;
        }

      case IrTypeKind.Set: {
          var set = UseObject((PinoVmSet) value.Obj!, "set formatting");
          var stt = (IrSetType) type;
          sb.Append(stt.Name).Append(" {");
          bool first = true;
          for (int slot = 0; slot < set.SlotLength; slot++) {
            if (!set.SlotIsLive(slot)) {
              continue;
            }

            sb.Append(first ? " " : ", ");
            first = false;
            AppendValue(sb, set.EntryAt(slot).Key, stt.ElementType, quoteStrings: true);
          }

          sb.Append(" }");
          break;
        }

      case IrTypeKind.Pool: {
          var pool = UseObject((PinoVmPool) value.Obj!, "pool formatting");
          var pt = (IrPoolType) type;
          sb.Append(pt.Name).Append(" {");
          bool first = true;
          for (int slot = 0; slot < pool.SlotLength; slot++) {
            if (pool.SlotAt(slot).State != PoolSlotState.Live) {
              continue;
            }

            sb.Append(first ? " " : ", ");
            first = false;
            sb.Append(CultureInfo.InvariantCulture, $"Handle({slot}, {pool.SlotAt(slot).Gen}) -> ");
            AppendValue(sb, pool.SlotAt(slot).Value, pt.ElementType, quoteStrings: true);
          }

          sb.Append(" }");
          break;
        }

      case IrTypeKind.Interface: {
          var box = UseObject((PinoVmIfaceBox) value.Obj!, "interface formatting");
          AppendValue(sb, box.Value, _module.VTables[box.VTableId].ConcreteStructType, quoteStrings);
          break;
        }

      case IrTypeKind.Closure:
        sb.Append("<function>");
        break;

      default:
        sb.Append('<').Append(type.Name).Append('>');
        break;
    }
  }
}
