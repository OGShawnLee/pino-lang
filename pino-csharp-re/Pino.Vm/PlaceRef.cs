namespace Pino.Vm;

public enum PlaceKind : byte {
  LocalSlot,
  GlobalSlot,
  Cell,
  StructField,
  VecElem,
  MapValue,
  PoolSlot,
  IfaceField,
}

/// <summary>
/// The VM's <c>ptr&lt;T&gt;</c> (<c>IQ-7</c>, design §3.3): a managed slot address with zero <c>unsafe</c> code.
/// <see cref="Container"/> is the frame or global <c>PinoValue[]</c>, the cell, vector, map, pool or interface box that
/// owns the slot; for <see cref="PlaceKind.StructField"/> it is the (boxed) <see cref="PlaceRef"/> of the enclosing struct.
/// </summary>
public readonly struct PlaceRef : IEquatable<PlaceRef> {
  public PlaceRef(PlaceKind kind, object container, int index, int subOffset = 0) {
    Kind = kind;
    Container = container;
    Index = index;
    SubOffset = subOffset;
  }

  public PlaceKind Kind { get; }

  public object Container { get; }

  public int Index { get; }

  public int SubOffset { get; }

  /// <summary>Stores this address in a <see cref="PinoValue"/> (<c>Raw</c> packs kind, offset and index).</summary>
  public PinoValue ToValue() {
    long raw = ((long) (byte) Kind << 56) | ((long) (SubOffset & 0xFFFFFF) << 32) | (uint) Index;
    return new PinoValue(raw, Container);
  }

  public static PlaceRef FromValue(PinoValue value) {
    long raw = value.Raw;
    var kind = (PlaceKind) (byte) ((ulong) raw >> 56);
    int sub = (int) ((raw >> 32) & 0xFFFFFF);
    int index = (int) (uint) raw;
    return new PlaceRef(kind, value.Obj ?? throw new InvalidOperationException("Null place reference."), index, sub);
  }

  /// <summary>Exact D-184 slot-address equality: same kind, same container object, same index, same sub-offset.</summary>
  public bool Equals(PlaceRef other) {
    if (Kind != other.Kind || Index != other.Index || SubOffset != other.SubOffset) {
      return false;
    }

    if (ReferenceEquals(Container, other.Container)) {
      return true;
    }

    return Container is PlaceRef a && other.Container is PlaceRef b && a.Equals(b);
  }

  public override bool Equals(object? obj) => obj is PlaceRef other && Equals(other);

  public override int GetHashCode() => HashCode.Combine(Kind, Index, SubOffset);

  public static bool operator ==(PlaceRef left, PlaceRef right) => left.Equals(right);

  public static bool operator !=(PlaceRef left, PlaceRef right) => !left.Equals(right);

  public override string ToString() => $"{Kind}[{Index}+{SubOffset}]";
}
