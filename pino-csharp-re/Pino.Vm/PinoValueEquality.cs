namespace Pino.Vm;

/// <summary>
/// Type-agnostic structural equality and hashing over <see cref="PinoValue"/>, used for map keys, set elements,
/// <c>VecContains</c>, and the <c>StructEq</c> of interfaces and closures. Scalars compare by their raw bits
/// (floats by IEEE bits, so a NaN key is findable), strings by content, aggregates element by element.
/// </summary>
public sealed class PinoValueEquality : IEqualityComparer<PinoValue> {
  public static readonly PinoValueEquality Instance = new();

  public bool Equals(PinoValue x, PinoValue y) => StructurallyEqual(x, y);

  public int GetHashCode(PinoValue value) => Hash(value);

  public static bool StructurallyEqual(in PinoValue a, in PinoValue b) {
    if (a.Obj is null && b.Obj is null) {
      return a.Raw == b.Raw;
    }

    if (a.Obj is null || b.Obj is null) {
      return false;
    }

    if (ReferenceEquals(a.Obj, b.Obj) && a.Raw == b.Raw) {
      return true;
    }

    switch (a.Obj) {
      case PinoVmString sa when b.Obj is PinoVmString sb:
        return sa.ContentEquals(sb);

      case PinoValue[] fa when b.Obj is PinoValue[] fb:
        return a.Raw == b.Raw && ArraysEqual(fa, fb);

      case PinoVmUnion ua when b.Obj is PinoVmUnion ub:
        return ua.Tag == ub.Tag && ArraysEqual(ua.Fields, ub.Fields);

      case PinoVmVec va when b.Obj is PinoVmVec vb:
        if (va.Count != vb.Count) {
          return false;
        }

        for (int i = 0; i < va.Count; i++) {
          if (!StructurallyEqual(va.Items[i], vb.Items[i])) {
            return false;
          }
        }

        return true;

      case PinoVmOrderedTable ta when b.Obj is PinoVmOrderedTable tb && ta.GetType() == tb.GetType():
        if (ta.Count != tb.Count) {
          return false;
        }

        for (int i = 0; i < ta.SlotLength; i++) {
          if (!ta.SlotIsLive(i)) {
            continue;
          }

          ref PinoVmOrderedTable.Entry ea = ref ta.EntryAt(i);
          if (!tb.TryFindSlot(ea.Key, out int other)) {
            return false;
          }

          if (ta is PinoVmMap && !StructurallyEqual(ea.Value, tb.EntryAt(other).Value)) {
            return false;
          }
        }

        return true;

      case PinoVmPool pa when b.Obj is PinoVmPool pb:
        if (pa.LiveCount != pb.LiveCount) {
          return false;
        }

        for (int i = 0; i < pa.SlotLength; i++) {
          ref PoolSlot sa2 = ref pa.SlotAt(i);
          if (sa2.State != PoolSlotState.Live) {
            continue;
          }

          if (i >= pb.SlotLength) {
            return false;
          }

          ref PoolSlot sb2 = ref pb.SlotAt(i);
          if (sb2.State != PoolSlotState.Live || sa2.Gen != sb2.Gen || !StructurallyEqual(sa2.Value, sb2.Value)) {
            return false;
          }
        }

        return true;

      case PinoVmIfaceBox ia when b.Obj is PinoVmIfaceBox ib:
        return ia.VTableId == ib.VTableId && StructurallyEqual(ia.Value, ib.Value);

      case PinoVmRegex ra when b.Obj is PinoVmRegex rb:
        return ra.Pattern.ContentEquals(rb.Pattern);

      default:
        return false;
    }
  }

  private static bool ArraysEqual(PinoValue[] a, PinoValue[] b) {
    if (a.Length != b.Length) {
      return false;
    }

    for (int i = 0; i < a.Length; i++) {
      if (!StructurallyEqual(a[i], b[i])) {
        return false;
      }
    }

    return true;
  }

  public static int Hash(in PinoValue value) {
    switch (value.Obj) {
      case null:
        return value.Raw.GetHashCode();
      case PinoVmString s: {
          var hash = new HashCode();
          hash.AddBytes(s.Span);
          return hash.ToHashCode();
        }

      case PinoValue[] fields: {
          int hash = value.Raw.GetHashCode();
          foreach (PinoValue field in fields) {
            hash = HashCode.Combine(hash, Hash(field));
          }

          return hash;
        }

      case PinoVmUnion u: {
          int hash = u.Tag;
          foreach (PinoValue field in u.Fields) {
            hash = HashCode.Combine(hash, Hash(field));
          }

          return hash;
        }

      case PinoVmVec v: {
          int hash = v.Count;
          for (int i = 0; i < v.Count; i++) {
            hash = HashCode.Combine(hash, Hash(v.Items[i]));
          }

          return hash;
        }

      case PinoVmIfaceBox box:
        return HashCode.Combine(box.VTableId, Hash(box.Value));
      case PinoVmRegex rx:
        return rx.Pattern.ToString().GetHashCode(StringComparison.Ordinal);
      default:
        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value.Obj);
    }
  }
}
