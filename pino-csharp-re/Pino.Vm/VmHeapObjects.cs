using System.Text;

using Pino.Compiler.IR;
using Pino.Compiler.Text;

namespace Pino.Vm;

/// <summary>
/// Base class of every RC-managed VM object (design §4.3). It carries the shadow reference count that is the real
/// reference count used for copy-on-write uniqueness, plus the allocation metadata used by <see cref="VmRcTracker"/>.
/// </summary>
public abstract class PinoVmHeapObject {
  /// <summary>Shadow count of an immortal singleton (<c>INT32_MIN</c>).</summary>
  public const int ImmortalRc = int.MinValue;

  public long AllocId { get; internal set; }

  public int ShadowRc { get; internal set; } = 1;

  public SourceSpan AllocSpan { get; internal set; }

  public string? AllocFunction { get; internal set; }

  public IrTypeId TypeId { get; internal set; } = IrTypeId.Invalid;

  public bool IsCycleCandidate { get; internal set; }

  /// <summary>True for arena objects: their count stays <c>0</c> and <c>rc_inc</c>/<c>rc_dec</c> do nothing.</summary>
  public bool IsArena { get; internal set; }

  public bool IsImmortal => ShadowRc == ImmortalRc;

  /// <summary>An object whose count reached <c>0</c> and is not arena-owned has been freed.</summary>
  public bool IsDead => ShadowRc == 0 && !IsArena;

  public abstract string KindName { get; }

  /// <summary>Adds every heap object directly referenced by this object (used to classify cycle leaks).</summary>
  internal abstract void CollectChildren(List<PinoVmHeapObject> into);

  internal static void CollectFromValue(in PinoValue value, List<PinoVmHeapObject> into) {
    switch (value.Obj) {
      case PinoVmHeapObject heap:
        into.Add(heap);
        break;
      case PinoValue[] fields:
        foreach (PinoValue field in fields) {
          CollectFromValue(field, into);
        }

        break;
    }
  }
}

/// <summary>A UTF-8 string slice (<c>STRLIB-18</c>): <see cref="Bytes"/> may be shared with other slices.</summary>
public sealed class PinoVmString : PinoVmHeapObject {
  private byte[] _bytes;
  private int _byteLength;
  private bool _bufferShared;
  private int _runeCount = -1;

  public PinoVmString(byte[] bytes, int offset, int length)
      : this(bytes, offset, length, bufferShared: false) { }

  private PinoVmString(byte[] bytes, int offset, int length, bool bufferShared) {
    _bytes = bytes;
    Offset = offset;
    _byteLength = length;
    _bufferShared = bufferShared;
  }

  public byte[] Bytes => _bytes;

  public int Offset { get; }

  public int ByteLength => _byteLength;

  public bool IsBufferShared => _bufferShared;

  public override string KindName => "string";

  public ReadOnlySpan<byte> Span => new(_bytes, Offset, _byteLength);

  public int RuneCount {
    get {
      if (_runeCount < 0) {
        int count = 0;
        for (int i = 0; i < _byteLength; i++) {
          if ((_bytes[Offset + i] & 0xC0) != 0x80) {
            count++;
          }
        }

        _runeCount = count;
      }

      return _runeCount;
    }
  }

  public static PinoVmString FromString(string text) {
    byte[] bytes = Encoding.UTF8.GetBytes(text);
    return new PinoVmString(bytes, 0, bytes.Length);
  }

  /// <summary>Returns a new string object that shares this string's bytes (zero copy).</summary>
  public PinoVmString Slice(int byteStart, int byteLength) {
    _bufferShared = true;
    return new(_bytes, Offset + byteStart, byteLength, bufferShared: true);
  }

  /// <summary>Appends <paramref name="right"/> in place; caller must ensure <c>ShadowRc == 1</c> and <c>!IsBufferShared</c>.</summary>
  internal void AppendInPlace(ReadOnlySpan<byte> right) {
    int needed = _byteLength + right.Length;
    if (needed > _bytes.Length) {
      int newCap = _bytes.Length < 8
          ? 16
          : (_bytes.Length > int.MaxValue / 2 ? needed : _bytes.Length * 2);
      while (newCap < needed) {
        if (newCap > int.MaxValue / 2) {
          newCap = needed;
          break;
        }

        newCap *= 2;
      }

      Array.Resize(ref _bytes, newCap);
    }

    right.CopyTo(_bytes.AsSpan(_byteLength));
    _byteLength = needed;
    _runeCount = -1;
  }

  /// <summary>Byte offset (relative to this slice) of the rune at <paramref name="runeIndex"/>; <see cref="ByteLength"/> when equal to the rune count.</summary>
  public int ByteOffsetOfRune(int runeIndex) {
    int seen = 0;
    int i = 0;
    while (i < ByteLength) {
      if ((Bytes[Offset + i] & 0xC0) != 0x80) {
        if (seen == runeIndex) {
          return i;
        }

        seen++;
      }

      i++;
    }

    return ByteLength;
  }

  public static int RuneWidthAt(ReadOnlySpan<byte> bytes, int pos) {
    byte b = bytes[pos];
    int width = b < 0x80 ? 1 : b >= 0xF0 ? 4 : b >= 0xE0 ? 3 : b >= 0xC0 ? 2 : 1;
    return Math.Min(width, bytes.Length - pos);
  }

  public int DecodeRuneAt(int bytePos, out int width) {
    ReadOnlySpan<byte> span = Span;
    width = RuneWidthAt(span, bytePos);
    if (width == 1) {
      return span[bytePos];
    }

    if (Rune.DecodeFromUtf8(span[bytePos..], out Rune rune, out int consumed) == System.Buffers.OperationStatus.Done) {
      width = consumed;
      return rune.Value;
    }

    return 0xFFFD;
  }

  public bool ContentEquals(PinoVmString other) => Span.SequenceEqual(other.Span);

  public int CompareOrdinal(PinoVmString other) => Math.Sign(Span.SequenceCompareTo(other.Span));

  public override string ToString() => Encoding.UTF8.GetString(Bytes, Offset, ByteLength);

  internal override void CollectChildren(List<PinoVmHeapObject> into) { }
}

public sealed class PinoVmRegex : PinoVmHeapObject {
  public PinoVmRegex(PinoVmString pattern, Regex.PinoRegex regex) {
    Pattern = pattern;
    Regex = regex;
  }

  public PinoVmString Pattern { get; }

  public Regex.PinoRegex Regex { get; }

  public override string KindName => "regex";

  internal override void CollectChildren(List<PinoVmHeapObject> into) => into.Add(Pattern);
}

/// <summary>A CoW vector. <see cref="Capacity"/> is the logical capacity reported by <c>v:cap</c>.</summary>
public sealed class PinoVmVec : PinoVmHeapObject {
  private PinoValue[] _items;

  public PinoVmVec(int capacity = 0) {
    _items = new PinoValue[Math.Min(Math.Max(capacity, 0), 1024)];
    Capacity = Math.Max(capacity, 0);
  }

  public PinoValue[] Items => _items;

  public int Count { get; private set; }

  public int Capacity { get; private set; }

  public override string KindName => "vector";

  public void Add(PinoValue value) {
    if (Count == _items.Length) {
      Array.Resize(ref _items, Math.Max(4, _items.Length * 2));
    }

    if (Count >= Capacity) {
      Capacity = Math.Max(4, Math.Max(Capacity * 2, Count + 1));
    }

    _items[Count++] = value;
  }

  public PinoValue RemoveLast() {
    PinoValue value = _items[--Count];
    _items[Count] = default;
    return value;
  }

  public PinoVmVec ShallowClone() {
    var clone = new PinoVmVec(0) { Capacity = Capacity };
    clone._items = new PinoValue[Math.Max(Count, Math.Min(Capacity, 1024))];
    Array.Copy(_items, clone._items, Count);
    clone.Count = Count;
    return clone;
  }

  internal override void CollectChildren(List<PinoVmHeapObject> into) {
    for (int i = 0; i < Count; i++) {
      CollectFromValue(_items[i], into);
    }
  }
}

/// <summary>Shared structure of the insertion-ordered map and set (<c>D-004</c>, <c>D-175</c>).</summary>
public abstract class PinoVmOrderedTable : PinoVmHeapObject {
  public struct Entry {
    public PinoValue Key;
    public PinoValue Value;
    public bool Live;
  }

  private readonly Dictionary<PinoValue, int> _index;
  private Entry[] _entries = new Entry[4];

  protected PinoVmOrderedTable() {
    _index = new Dictionary<PinoValue, int>(PinoValueEquality.Instance);
  }

  public int Count => _index.Count;

  /// <summary>Number of entry slots including tombstones; iteration scans <c>0..SlotLength-1</c>.</summary>
  public int SlotLength { get; private set; }

  public bool SlotIsLive(int slot) => (uint) slot < (uint) SlotLength && _entries[slot].Live;

  public ref Entry EntryAt(int slot) => ref _entries[slot];

  public bool TryFindSlot(in PinoValue key, out int slot) => _index.TryGetValue(key, out slot);

  public bool ContainsKey(in PinoValue key) => _index.ContainsKey(key);

  /// <summary>Appends a new live entry (the key must be absent) and returns its slot.</summary>
  public int Append(in PinoValue key, in PinoValue value) {
    if (SlotLength == _entries.Length) {
      Array.Resize(ref _entries, _entries.Length * 2);
    }

    int slot = SlotLength++;
    _entries[slot] = new Entry { Key = key, Value = value, Live = true };
    _index[key] = slot;
    return slot;
  }

  /// <summary>Marks the entry for <paramref name="key"/> as a tombstone and returns its old key and value, if present.</summary>
  public bool RemoveKey(in PinoValue key, out PinoValue oldKey, out PinoValue oldValue) {
    if (!_index.TryGetValue(key, out int slot)) {
      oldKey = default;
      oldValue = default;
      return false;
    }

    oldKey = _entries[slot].Key;
    oldValue = _entries[slot].Value;
    _entries[slot] = default;
    _index.Remove(key);
    return true;
  }

  protected void CopyInto(PinoVmOrderedTable clone) {
    for (int i = 0; i < SlotLength; i++) {
      if (_entries[i].Live) {
        clone.Append(_entries[i].Key, _entries[i].Value);
      }
    }
  }

  internal override void CollectChildren(List<PinoVmHeapObject> into) {
    for (int i = 0; i < SlotLength; i++) {
      if (_entries[i].Live) {
        CollectFromValue(_entries[i].Key, into);
        CollectFromValue(_entries[i].Value, into);
      }
    }
  }
}

public sealed class PinoVmMap : PinoVmOrderedTable {
  public override string KindName => "map";

  public PinoVmMap ShallowClone() {
    var clone = new PinoVmMap();
    CopyInto(clone);
    return clone;
  }
}

public sealed class PinoVmSet : PinoVmOrderedTable {
  public override string KindName => "set";

  public PinoVmSet ShallowClone() {
    var clone = new PinoVmSet();
    CopyInto(clone);
    return clone;
  }
}

public enum PoolSlotState : byte {
  Free = 0,
  Live = 1,
  Retired = 2,
}

public struct PoolSlot {
  public uint Gen;
  public PoolSlotState State;
  public int NextFree;
  public PinoValue Value;
}

/// <summary>
/// Generational slot pool (<c>D-185</c>, <c>POOL-1..14</c>): LIFO reuse of removed slots, <c>clear()</c> reuse in
/// increasing index order, generation retirement at <see cref="uint.MaxValue"/>, and stale-handle checks.
/// </summary>
public sealed class PinoVmPool : PinoVmHeapObject {
  private PoolSlot[] _slots = new PoolSlot[4];

  public int SlotLength { get; private set; }

  public int LiveCount { get; private set; }

  public int FreeHead { get; private set; } = -1;

  public override string KindName => "pool";

  public ref PoolSlot SlotAt(int index) => ref _slots[index];

  public bool IsLive(PinoValue handle) {
    uint index = handle.HandleIndex;
    return index < (uint) SlotLength
        && _slots[index].State == PoolSlotState.Live
        && _slots[index].Gen == handle.HandleGeneration;
  }

  public PinoValue Add(PinoValue value) {
    int index;
    if (FreeHead >= 0) {
      index = FreeHead;
      FreeHead = _slots[index].NextFree;
    } else {
      if (SlotLength == _slots.Length) {
        Array.Resize(ref _slots, _slots.Length * 2);
      }

      index = SlotLength++;
      _slots[index] = default;
    }

    ref PoolSlot slot = ref _slots[index];
    slot.State = PoolSlotState.Live;
    slot.NextFree = -1;
    slot.Value = value;
    LiveCount++;
    return PinoValue.FromHandle((uint) index, slot.Gen);
  }

  /// <summary>Removes the live slot for <paramref name="handle"/>, returning its value, or <see langword="false"/> when stale.</summary>
  public bool TryRemove(PinoValue handle, out PinoValue value) {
    if (!IsLive(handle)) {
      value = default;
      return false;
    }

    int index = (int) handle.HandleIndex;
    ref PoolSlot slot = ref _slots[index];
    value = slot.Value;
    slot.Value = default;
    LiveCount--;
    if (slot.Gen == uint.MaxValue) {
      slot.State = PoolSlotState.Retired;
    } else {
      slot.Gen++;
      slot.State = PoolSlotState.Free;
      slot.NextFree = FreeHead;
      FreeHead = index;
    }

    return true;
  }

  /// <summary>Frees every live slot (returning the dropped values) and rebuilds the free list in increasing index order.</summary>
  public List<PinoValue> Clear() {
    var dropped = new List<PinoValue>();
    FreeHead = -1;
    for (int i = SlotLength - 1; i >= 0; i--) {
      ref PoolSlot slot = ref _slots[i];
      if (slot.State == PoolSlotState.Retired) {
        continue;
      }

      if (slot.State == PoolSlotState.Live) {
        dropped.Add(slot.Value);
        slot.Value = default;
        if (slot.Gen == uint.MaxValue) {
          slot.State = PoolSlotState.Retired;
          continue;
        }

        slot.Gen++;
      }

      slot.State = PoolSlotState.Free;
      slot.NextFree = FreeHead;
      FreeHead = i;
    }

    LiveCount = 0;
    dropped.Reverse();
    return dropped;
  }

  public PinoVmPool ShallowClone() {
    var clone = new PinoVmPool {
      _slots = (PoolSlot[]) _slots.Clone(),
      SlotLength = SlotLength,
      LiveCount = LiveCount,
      FreeHead = FreeHead,
    };
    return clone;
  }

  internal override void CollectChildren(List<PinoVmHeapObject> into) {
    for (int i = 0; i < SlotLength; i++) {
      if (_slots[i].State == PoolSlotState.Live) {
        CollectFromValue(_slots[i].Value, into);
      }
    }
  }
}

/// <summary>The box of a captured <c>var</c>/<c>val mut</c> (<c>D-056</c>).</summary>
public sealed class PinoVmCell : PinoVmHeapObject {
  public PinoVmCell(PinoValue value) {
    Value = value;
  }

  public PinoValue Value { get; set; }

  public override string KindName => "cell";

  internal override void CollectChildren(List<PinoVmHeapObject> into) => CollectFromValue(Value, into);
}

/// <summary>A closure environment: the captured values, in <c>IrEnvType</c> field order.</summary>
public sealed class PinoVmEnv : PinoVmHeapObject {
  public PinoVmEnv(PinoValue[] fields) {
    Fields = fields;
  }

  public PinoValue[] Fields { get; }

  public override string KindName => "env";

  internal override void CollectChildren(List<PinoVmHeapObject> into) {
    foreach (PinoValue field in Fields) {
      CollectFromValue(field, into);
    }
  }
}

/// <summary>An interface value's RC box around the concrete struct (<c>IQ-5</c>).</summary>
public sealed class PinoVmIfaceBox : PinoVmHeapObject {
  public PinoVmIfaceBox(int vtableId, PinoValue value) {
    VTableId = vtableId;
    Value = value;
  }

  public int VTableId { get; }

  public PinoValue Value { get; set; }

  public override string KindName => "interface";

  internal override void CollectChildren(List<PinoVmHeapObject> into) => CollectFromValue(Value, into);
}

/// <summary>A heap-boxed recursive union node (<c>UNION-11</c>).</summary>
public sealed class PinoVmUnion : PinoVmHeapObject {
  public PinoVmUnion(int tag, PinoValue[] fields) {
    Tag = tag;
    Fields = fields;
  }

  public int Tag { get; }

  public PinoValue[] Fields { get; }

  public override string KindName => "union";

  internal override void CollectChildren(List<PinoVmHeapObject> into) {
    foreach (PinoValue field in Fields) {
      CollectFromValue(field, into);
    }
  }
}
