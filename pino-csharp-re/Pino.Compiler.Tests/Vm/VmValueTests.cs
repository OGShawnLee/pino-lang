using System.Reflection;
using System.Runtime.CompilerServices;

using Pino.Vm;

namespace Pino.Compiler.Tests.Vm;

public sealed class VmValueTests {
  [Fact]
  public void PinoValue_IsASixteenByteReadonlyStruct() {
    Assert.True(typeof(PinoValue).IsValueType);
    Assert.True(typeof(PinoValue).IsDefined(typeof(IsReadOnlyAttribute), inherit: false));
    Assert.Equal(16, Unsafe.SizeOf<PinoValue>());
    Assert.Equal(16, PinoValue.SizeInBytes);
    Assert.All(typeof(PinoValue).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), static f => Assert.True(f.IsInitOnly));
  }

  [Fact]
  public void Scalars_LiveInRaw_WithNoObject() {
    Assert.Null(PinoValue.FromInt(-42).Obj);
    Assert.Null(PinoValue.FromFloat(2.5).Obj);
    Assert.Null(PinoValue.FromBool(true).Obj);
    Assert.Null(PinoValue.FromRune(0x1F332).Obj);
    Assert.Null(PinoValue.FromEnum(3).Obj);
    Assert.Null(PinoValue.FromHandle(7, 9).Obj);

    Assert.Equal(-42, PinoValue.FromInt(-42).AsInt);
    Assert.Equal(2.5, PinoValue.FromFloat(2.5).AsFloat);
    Assert.Equal(BitConverter.DoubleToInt64Bits(2.5), PinoValue.FromFloat(2.5).Raw);
    Assert.True(PinoValue.FromBool(true).AsBool);
    Assert.False(PinoValue.FromBool(false).AsBool);
    Assert.Equal(0x1F332, PinoValue.FromRune(0x1F332).AsRune);
    Assert.Equal(3, PinoValue.FromEnum(3).AsEnum);
  }

  [Fact]
  public void Handle_PacksIndexAndGenerationLikeTheDesign() {
    PinoValue handle = PinoValue.FromHandle(7, 9);

    Assert.Equal(7u, handle.HandleIndex);
    Assert.Equal(9u, handle.HandleGeneration);
    Assert.Equal((long) (7u | ((ulong) 9u << 32)), handle.Raw);

    PinoValue max = PinoValue.FromHandle(uint.MaxValue, uint.MaxValue);
    Assert.Equal(uint.MaxValue, max.HandleIndex);
    Assert.Equal(uint.MaxValue, max.HandleGeneration);
  }

  [Fact]
  public void ScalarLoops_AllocateNothingOnTheDotNetHeap() {
    long total = 0;
    double sum = 0;
    // Warm up so that JIT allocations are not counted.
    for (int i = 0; i < 100; i++) {
      total += Touch(i);
    }

    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 100_000; i++) {
      PinoValue n = PinoValue.FromInt(i);
      PinoValue f = PinoValue.FromFloat(i * 0.5);
      PinoValue b = PinoValue.FromBool((i & 1) == 0);
      PinoValue r = PinoValue.FromRune(65 + (i % 26));
      PinoValue e = PinoValue.FromEnum(i % 3);
      PinoValue h = PinoValue.FromHandle((uint) i, (uint) (i >> 3));
      total += n.AsInt + r.AsRune + e.AsEnum + h.HandleIndex + (b.AsBool ? 1 : 0);
      sum += f.AsFloat;
    }

    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Assert.Equal(0, allocated);
    Assert.True(total > 0 && sum > 0);
  }

  private static long Touch(int i) => PinoValue.FromInt(i).AsInt;

  [Fact]
  public void PlaceRef_EqualityIsExactSlotAddressEquality() {
    var frame = new PinoValue[4];
    var other = new PinoValue[4];
    var vec = new PinoVmVec();

    var a = new PlaceRef(PlaceKind.LocalSlot, frame, 1);
    Assert.Equal(a, new PlaceRef(PlaceKind.LocalSlot, frame, 1));
    Assert.NotEqual(a, new PlaceRef(PlaceKind.LocalSlot, frame, 2));
    Assert.NotEqual(a, new PlaceRef(PlaceKind.LocalSlot, other, 1));
    Assert.NotEqual(a, new PlaceRef(PlaceKind.GlobalSlot, frame, 1));
    Assert.NotEqual(a, new PlaceRef(PlaceKind.LocalSlot, frame, 1, subOffset: 1));

    var elem0 = new PlaceRef(PlaceKind.VecElem, vec, 0);
    var elem1 = new PlaceRef(PlaceKind.VecElem, vec, 1);
    Assert.NotEqual(elem0, elem1);
    Assert.Equal(elem0, new PlaceRef(PlaceKind.VecElem, vec, 0));
    Assert.True(elem0 == new PlaceRef(PlaceKind.VecElem, vec, 0));
    Assert.True(elem0 != elem1);

    var fieldA = new PlaceRef(PlaceKind.StructField, elem0, 2);
    var fieldB = new PlaceRef(PlaceKind.StructField, new PlaceRef(PlaceKind.VecElem, vec, 0), 2);
    Assert.Equal(fieldA, fieldB);
    Assert.NotEqual(fieldA, new PlaceRef(PlaceKind.StructField, elem1, 2));
  }

  [Fact]
  public void PlaceRef_RoundTripsThroughAValue() {
    var frame = new PinoValue[3];
    var place = new PlaceRef(PlaceKind.LocalSlot, frame, 2, subOffset: 5);

    PinoValue packed = place.ToValue();
    PlaceRef back = PlaceRef.FromValue(packed);

    Assert.Equal(place, back);
    Assert.Same(frame, back.Container);
    Assert.Equal(PlaceKind.LocalSlot, back.Kind);
    Assert.Equal(2, back.Index);
    Assert.Equal(5, back.SubOffset);
  }

  [Theory]
  [InlineData(1e20, "1E+20")]
  [InlineData(1e17, "1E+17")]
  [InlineData(1e16, "10000000000000000.0")]
  [InlineData(1e15, "1000000000000000.0")]
  [InlineData(1e14, "100000000000000.0")]
  [InlineData(0.0001, "0.0001")]
  [InlineData(0.00001, "1E-05")]
  [InlineData(-0.0, "-0.0")]
  [InlineData(1.0, "1.0")]
  [InlineData(42.0, "42.0")]
  [InlineData(2.5, "2.5")]
  [InlineData(-3.0, "-3.0")]
  public void FormatFloat_MatchesTheDotNetOracleWithPointZeroForWholeValues(double value, string expected) {
    Assert.Equal(expected, PinoVm.FormatFloat(value));
  }

  [Fact]
  public void FormatFloat_ZeroPointOnePlusZeroPointTwoShowsRoundTripDigits() {
    Assert.Equal("0.30000000000000004", PinoVm.FormatFloat(0.1 + 0.2));
    Assert.Equal("NaN", PinoVm.FormatFloat(double.NaN));
    Assert.Equal("Infinity", PinoVm.FormatFloat(double.PositiveInfinity));
    Assert.Equal("-Infinity", PinoVm.FormatFloat(double.NegativeInfinity));
  }

  [Fact]
  public void FloatsPrintedByAProgramFollowTheSameRules() {
    const string Source = """
        val a = 0.1 + 0.2
        println(a)
        println(-0.0)
        println(2.0 * 3)
        println(10.0 / 4)
        println(str(1.5) + "!")
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal("0.30000000000000004\n-0.0\n6.0\n2.5\n1.5!", VmTestHarness.Normalize(run.Stdout));
  }

  // ---------------------------------------------------------------------------
  // Pool (D-185, POOL-1..14)
  // ---------------------------------------------------------------------------

  [Fact]
  public void Pool_ReusesTheMostRecentlyFreedSlotFirst() {
    var pool = new PinoVmPool();
    PinoValue h0 = pool.Add(PinoValue.FromInt(10));
    PinoValue h1 = pool.Add(PinoValue.FromInt(20));
    PinoValue h2 = pool.Add(PinoValue.FromInt(30));

    Assert.True(pool.TryRemove(h0, out _));
    Assert.True(pool.TryRemove(h2, out _));
    PinoValue reused = pool.Add(PinoValue.FromInt(40));
    PinoValue reused2 = pool.Add(PinoValue.FromInt(50));

    Assert.Equal(2u, reused.HandleIndex);
    Assert.Equal(1u, reused.HandleGeneration);
    Assert.Equal(0u, reused2.HandleIndex);
    Assert.Equal(1u, reused2.HandleGeneration);
    Assert.False(pool.IsLive(h0));
    Assert.False(pool.IsLive(h2));
    Assert.True(pool.IsLive(h1));
    Assert.Equal(3, pool.LiveCount);
    Assert.Equal(3, pool.SlotLength);
  }

  [Fact]
  public void Pool_ClearInvalidatesEveryHandleAndReusesSlotsInIncreasingOrder() {
    var pool = new PinoVmPool();
    PinoValue[] handles = [pool.Add(PinoValue.FromInt(1)), pool.Add(PinoValue.FromInt(2)), pool.Add(PinoValue.FromInt(3))];
    pool.TryRemove(handles[1], out _);

    List<PinoValue> dropped = pool.Clear();

    Assert.Equal(2, dropped.Count);
    Assert.Equal(0, pool.LiveCount);
    Assert.All(handles, h => Assert.False(pool.IsLive(h)));
    PinoValue first = pool.Add(PinoValue.FromInt(7));
    PinoValue second = pool.Add(PinoValue.FromInt(8));
    PinoValue third = pool.Add(PinoValue.FromInt(9));
    Assert.Equal([0u, 1u, 2u], new[] { first.HandleIndex, second.HandleIndex, third.HandleIndex });
    Assert.Equal(3, pool.SlotLength);
  }

  [Fact]
  public void Pool_RetiresASlotWhoseGenerationReachesTheLimit() {
    var pool = new PinoVmPool();
    PinoValue h = pool.Add(PinoValue.FromInt(1));
    pool.SlotAt(0).Gen = uint.MaxValue;
    PinoValue atLimit = PinoValue.FromHandle(0, uint.MaxValue);

    Assert.True(pool.IsLive(atLimit));
    Assert.True(pool.TryRemove(atLimit, out _));
    Assert.Equal(PoolSlotState.Retired, pool.SlotAt(0).State);
    Assert.False(pool.IsLive(atLimit));
    Assert.False(pool.IsLive(h));

    PinoValue next = pool.Add(PinoValue.FromInt(2));
    Assert.Equal(1u, next.HandleIndex);
    Assert.Equal(0u, next.HandleGeneration);
    Assert.Equal(PoolSlotState.Retired, pool.SlotAt(0).State);

    pool.Clear();
    Assert.Equal(PoolSlotState.Retired, pool.SlotAt(0).State);
    Assert.Equal(1u, pool.Add(PinoValue.FromInt(3)).HandleIndex);
  }

  [Fact]
  public void Pool_RejectsOutOfRangeAndStaleHandles() {
    var pool = new PinoVmPool();
    pool.Add(PinoValue.FromInt(1));

    Assert.False(pool.IsLive(PinoValue.FromHandle(5, 0)));
    Assert.False(pool.IsLive(PinoValue.FromHandle(0, 3)));
    Assert.False(pool.TryRemove(PinoValue.FromHandle(5, 0), out _));
  }

  // ---------------------------------------------------------------------------
  // Map and set (D-004, D-175, MAP-9, SET-6)
  // ---------------------------------------------------------------------------

  [Fact]
  public void Map_KeepsInsertionOrder_AndReinsertedKeysMoveToTheEnd() {
    var map = new PinoVmMap();
    map.Append(PinoValue.FromInt(1), PinoValue.FromInt(10));
    map.Append(PinoValue.FromInt(2), PinoValue.FromInt(20));
    map.Append(PinoValue.FromInt(3), PinoValue.FromInt(30));

    Assert.True(map.RemoveKey(PinoValue.FromInt(1), out _, out _));
    map.Append(PinoValue.FromInt(1), PinoValue.FromInt(11));

    var keys = new List<long>();
    for (int slot = 0; slot < map.SlotLength; slot++) {
      if (map.SlotIsLive(slot)) {
        keys.Add(map.EntryAt(slot).Key.Raw);
      }
    }

    Assert.Equal([2L, 3L, 1L], keys);
    Assert.Equal(3, map.Count);
    Assert.Equal(4, map.SlotLength);
  }

  [Fact]
  public void StructuralEquality_ComparesStringsByContent() {
    PinoValue a = PinoValue.FromObject(PinoVmString.FromString("héllo"));
    PinoValue b = PinoValue.FromObject(PinoVmString.FromString("héllo"));
    PinoValue c = PinoValue.FromObject(PinoVmString.FromString("hello"));

    Assert.True(PinoValueEquality.StructurallyEqual(a, b));
    Assert.False(PinoValueEquality.StructurallyEqual(a, c));
    Assert.Equal(PinoValueEquality.Hash(a), PinoValueEquality.Hash(b));
  }

  [Fact]
  public void String_SlicesShareBytesAndCountRunes() {
    PinoVmString s = PinoVmString.FromString("a🌲bc");

    Assert.Equal(4, s.RuneCount);
    Assert.Equal(7, s.ByteLength);
    PinoVmString tail = s.Slice(s.ByteOffsetOfRune(1), s.ByteOffsetOfRune(3) - s.ByteOffsetOfRune(1));
    Assert.Same(s.Bytes, tail.Bytes);
    Assert.Equal("🌲b", tail.ToString());
    Assert.Equal(2, tail.RuneCount);
    Assert.Equal(0x1F332, tail.DecodeRuneAt(0, out int width));
    Assert.Equal(4, width);
  }
}
