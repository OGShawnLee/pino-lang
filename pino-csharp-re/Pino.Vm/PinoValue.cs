using System.Runtime.CompilerServices;

namespace Pino.Vm;

/// <summary>
/// The unboxed 16-byte VM value (<c>IQ-7</c>, design §6.2). <c>int</c>, <c>float</c> (as IEEE bits),
/// <c>bool</c>, <c>rune</c>, <c>enum</c> and <c>Handle[T]</c> live entirely in <see cref="Raw"/> with
/// <see cref="Obj"/> equal to <see langword="null"/>, so numeric, enum and handle loops never allocate.
/// Aggregates keep their payload in <see cref="Obj"/>: a <see cref="PinoVmHeapObject"/> for collections, strings,
/// cells, environments, interface boxes and recursive unions, or a <c>PinoValue[]</c> for inline structs, tuples
/// and unions (whose tag then lives in <see cref="Raw"/>). A closure keeps its function id in <see cref="Raw"/>
/// and its environment in <see cref="Obj"/>; an interface keeps its vtable id in <see cref="Raw"/> and its box in
/// <see cref="Obj"/>.
/// </summary>
public readonly struct PinoValue {
  /// <summary>The empty value (<c>int 0</c>, also the uninitialized register).</summary>
  public static readonly PinoValue Default = default;

  public readonly long Raw;
  public readonly object? Obj;

  public PinoValue(long raw, object? obj = null) {
    Raw = raw;
    Obj = obj;
  }

  public static PinoValue FromInt(long value) => new(value);

  public static PinoValue FromFloat(double value) => new(BitConverter.DoubleToInt64Bits(value));

  public static PinoValue FromBool(bool value) => new(value ? 1 : 0);

  public static PinoValue FromRune(int codePoint) => new(codePoint);

  public static PinoValue FromEnum(int ordinal) => new(ordinal);

  /// <summary>Packs a <c>Handle[T]</c> as <c>(uint)index | ((ulong)generation &lt;&lt; 32)</c>.</summary>
  public static PinoValue FromHandle(uint index, uint generation) =>
      new((long) ((ulong) index | ((ulong) generation << 32)));

  public static PinoValue FromObject(object? obj) => new(0, obj);

  public long AsInt => Raw;

  public double AsFloat => BitConverter.Int64BitsToDouble(Raw);

  public bool AsBool => Raw != 0;

  public int AsRune => (int) Raw;

  public int AsEnum => (int) Raw;

  public uint HandleIndex => (uint) (ulong) Raw;

  public uint HandleGeneration => (uint) ((ulong) Raw >> 32);

  public bool IsNullObject => Obj is null;

  public PinoVmHeapObject? AsHeap => Obj as PinoVmHeapObject;

  /// <summary>Size in bytes of one <see cref="PinoValue"/> on a 64-bit runtime (16).</summary>
  public static int SizeInBytes => Unsafe.SizeOf<PinoValue>();

  public override string ToString() => Obj is null ? $"raw:{Raw}" : $"raw:{Raw} obj:{Obj.GetType().Name}";
}
