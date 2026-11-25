using System.Globalization;
using System.Text;

using Pino.Compiler.IR;

namespace Pino.Vm;

/// <summary>Vector, map, set, pool and string instructions.</summary>
public sealed partial class PinoVm {
  private static string IndexPanic(long index, int length, string what) =>
      string.Create(CultureInfo.InvariantCulture, $"Index {index} is out of range for a {what} of length {length}.");

  private PinoVmVec VecFrom(CompiledFunc func, PinoValue[] regs, IrOperand operand) =>
      UseObject((PinoVmVec) Val(func, regs, operand).Obj!, "vector access");

  private PinoVmVec VecFromPtr(CompiledFunc func, PinoValue[] regs, IrOperand ptr) =>
      UseObject((PinoVmVec) LoadPlace(PlaceRef.FromValue(Val(func, regs, ptr))).Obj!, "vector access");

  private PinoVmMap MapFrom(CompiledFunc func, PinoValue[] regs, IrOperand operand) =>
      UseObject((PinoVmMap) Val(func, regs, operand).Obj!, "map access");

  private PinoVmMap MapFromPtr(CompiledFunc func, PinoValue[] regs, IrOperand ptr) =>
      UseObject((PinoVmMap) LoadPlace(PlaceRef.FromValue(Val(func, regs, ptr))).Obj!, "map access");

  private PinoVmSet SetFrom(CompiledFunc func, PinoValue[] regs, IrOperand operand) =>
      UseObject((PinoVmSet) Val(func, regs, operand).Obj!, "set access");

  private PinoVmSet SetFromPtr(CompiledFunc func, PinoValue[] regs, IrOperand ptr) =>
      UseObject((PinoVmSet) LoadPlace(PlaceRef.FromValue(Val(func, regs, ptr))).Obj!, "set access");

  private PinoVmPool PoolFrom(CompiledFunc func, PinoValue[] regs, IrOperand operand) =>
      UseObject((PinoVmPool) Val(func, regs, operand).Obj!, "pool access");

  private PinoVmPool PoolFromPtr(CompiledFunc func, PinoValue[] regs, IrOperand ptr) =>
      UseObject((PinoVmPool) LoadPlace(PlaceRef.FromValue(Val(func, regs, ptr))).Obj!, "pool access");

  private PinoVmString StrFrom(CompiledFunc func, PinoValue[] regs, IrOperand operand) =>
      UseObject((PinoVmString) Val(func, regs, operand).Obj!, "string access");

  private IrTypeId DestType(CompiledFunc func, IrTempId dest) => func.Fn.Temps[dest.Index].Type;

  private string MapMissingKeyMessage(PinoValue key, IrTypeId keyType) =>
      $"Key {Format(key, keyType, quoteStrings: true)} was not found in map.";

  private bool ExecCollectionOp(CompiledFunc func, PinoValue[] regs, VmOp op) {
    switch (op.Kind) {
      // ----- vectors
      case OpKind.VecNew: {
          var i = (VecNew) op.Instr!;
          var vec = new PinoVmVec(i.Elements.Length);
          foreach (IrOperand element in i.Elements) {
            vec.Add(Val(func, regs, element));
          }

          Alloc(vec, DestType(func, i.Dest), i.Region);
          regs[Dest(func, i.Dest)] = PinoValue.FromObject(vec);
          return true;
        }

      case OpKind.VecWithCap: {
          var i = (VecWithCap) op.Instr!;
          long capacity = Val(func, regs, i.Capacity).Raw;
          var vec = new PinoVmVec((int) Math.Clamp(capacity, 0, int.MaxValue));
          Alloc(vec, DestType(func, i.Dest), i.Region);
          regs[Dest(func, i.Dest)] = PinoValue.FromObject(vec);
          return true;
        }

      case OpKind.VecLen: {
          var i = (VecLen) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromInt(VecFrom(func, regs, i.Vec).Count);
          return true;
        }

      case OpKind.VecCap: {
          var i = (VecCap) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromInt(VecFrom(func, regs, i.Vec).Capacity);
          return true;
        }

      case OpKind.VecGet: {
          var i = (VecGet) op.Instr!;
          PinoVmVec vec = VecFrom(func, regs, i.Vec);
          long index = Val(func, regs, i.Index).Raw;
          if ((ulong) index >= (ulong) vec.Count) {
            throw new PinoVmPanic(IndexPanic(index, vec.Count, "vector"));
          }

          regs[Dest(func, i.Dest)] = vec.Items[index];
          return true;
        }

      case OpKind.VecElemAddr: {
          var i = (VecElemAddr) op.Instr!;
          PinoVmVec vec = VecFromPtr(func, regs, i.VecPtr);
          long index = Val(func, regs, i.Index).Raw;
          if ((ulong) index >= (ulong) vec.Count) {
            throw new PinoVmPanic(IndexPanic(index, vec.Count, "vector"));
          }

          regs[Dest(func, i.Dest)] = new PlaceRef(PlaceKind.VecElem, vec, (int) index).ToValue();
          return true;
        }

      case OpKind.VecPush: {
          var i = (VecPush) op.Instr!;
          VecFromPtr(func, regs, i.VecPtr).Add(Val(func, regs, i.Value));
          return true;
        }

      case OpKind.VecPop: {
          var i = (VecPop) op.Instr!;
          PinoVmVec vec = VecFromPtr(func, regs, i.VecPtr);
          IrTypeId optionType = DestType(func, i.Dest);
          regs[Dest(func, i.Dest)] = vec.Count == 0 ? MakeNone(optionType) : MakeSome(optionType, vec.RemoveLast());
          return true;
        }

      case OpKind.VecSwap: {
          var i = (VecSwap) op.Instr!;
          PinoVmVec vec = VecFromPtr(func, regs, i.VecPtr);
          long a = Val(func, regs, i.IndexA).Raw;
          long b = Val(func, regs, i.IndexB).Raw;
          if ((ulong) a >= (ulong) vec.Count) {
            throw new PinoVmPanic(IndexPanic(a, vec.Count, "vector"));
          }

          if ((ulong) b >= (ulong) vec.Count) {
            throw new PinoVmPanic(IndexPanic(b, vec.Count, "vector"));
          }

          (vec.Items[a], vec.Items[b]) = (vec.Items[b], vec.Items[a]);
          return true;
        }

      case OpKind.VecContains: {
          var i = (VecContains) op.Instr!;
          PinoVmVec vec = VecFrom(func, regs, i.Vec);
          PinoValue needle = Val(func, regs, i.Value);
          bool found = false;
          for (int k = 0; k < vec.Count && !found; k++) {
            found = PinoValueEquality.StructurallyEqual(vec.Items[k], needle);
          }

          regs[Dest(func, i.Dest)] = PinoValue.FromBool(found);
          return true;
        }

      // ----- maps
      case OpKind.MapNew: {
          var i = (MapNew) op.Instr!;
          IrTypeId mapType = DestType(func, i.Dest);
          var mt = (IrMapType) _types.GetType(mapType);
          var map = new PinoVmMap();
          foreach ((IrOperand keyOp, IrOperand valueOp) in i.Entries) {
            PinoValue key = Val(func, regs, keyOp);
            PinoValue value = Val(func, regs, valueOp);
            MapPut(map, mt, key, value);
          }

          Alloc(map, mapType, i.Region);
          regs[Dest(func, i.Dest)] = PinoValue.FromObject(map);
          return true;
        }

      case OpKind.MapLen: {
          var i = (MapLen) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromInt(MapFrom(func, regs, i.Map).Count);
          return true;
        }

      case OpKind.MapGet: {
          var i = (MapGet) op.Instr!;
          PinoVmMap map = MapFrom(func, regs, i.Map);
          PinoValue key = Val(func, regs, i.Key);
          if (!map.TryFindSlot(key, out int slot)) {
            throw new PinoVmPanic(MapMissingKeyMessage(key, ((IrMapType) _types.GetType(map.TypeId)).KeyType));
          }

          regs[Dest(func, i.Dest)] = map.EntryAt(slot).Value;
          return true;
        }

      case OpKind.MapElemAddr: {
          var i = (MapElemAddr) op.Instr!;
          PinoVmMap map = MapFromPtr(func, regs, i.MapPtr);
          PinoValue key = Val(func, regs, i.Key);
          if (!map.TryFindSlot(key, out int slot)) {
            throw new PinoVmPanic(MapMissingKeyMessage(key, ((IrMapType) _types.GetType(map.TypeId)).KeyType));
          }

          regs[Dest(func, i.Dest)] = new PlaceRef(PlaceKind.MapValue, map, slot).ToValue();
          return true;
        }

      case OpKind.MapSet: {
          var i = (MapSet) op.Instr!;
          PinoVmMap map = MapFromPtr(func, regs, i.MapPtr);
          MapPut(map, (IrMapType) _types.GetType(map.TypeId), Val(func, regs, i.Key), Val(func, regs, i.Value));
          return true;
        }

      case OpKind.MapRemove: {
          var i = (MapRemove) op.Instr!;
          PinoVmMap map = MapFromPtr(func, regs, i.MapPtr);
          IrTypeId optionType = DestType(func, i.Dest);
          if (map.RemoveKey(Val(func, regs, i.Key), out PinoValue oldKey, out PinoValue oldValue)) {
            DecValue(oldKey, ((IrMapType) _types.GetType(map.TypeId)).KeyType);
            regs[Dest(func, i.Dest)] = MakeSome(optionType, oldValue);
          } else {
            regs[Dest(func, i.Dest)] = MakeNone(optionType);
          }

          return true;
        }

      case OpKind.MapContains: {
          var i = (MapContains) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromBool(MapFrom(func, regs, i.Map).ContainsKey(Val(func, regs, i.Key)));
          return true;
        }

      case OpKind.MapKeys:
      case OpKind.MapValues: {
          IrTempId dest = op.Kind == OpKind.MapKeys ? ((MapKeys) op.Instr!).Dest : ((MapValues) op.Instr!).Dest;
          IrOperand mapOp = op.Kind == OpKind.MapKeys ? ((MapKeys) op.Instr!).Map : ((MapValues) op.Instr!).Map;
          PinoVmMap map = MapFrom(func, regs, mapOp);
          var mt = (IrMapType) _types.GetType(map.TypeId);
          IrTypeId elementType = op.Kind == OpKind.MapKeys ? mt.KeyType : mt.ValueType;
          var vec = new PinoVmVec(map.Count);
          for (int slot = 0; slot < map.SlotLength; slot++) {
            if (!map.SlotIsLive(slot)) {
              continue;
            }

            PinoValue element = op.Kind == OpKind.MapKeys ? map.EntryAt(slot).Key : map.EntryAt(slot).Value;
            IncValue(element, elementType);
            vec.Add(element);
          }

          Alloc(vec, DestType(func, dest));
          regs[Dest(func, dest)] = PinoValue.FromObject(vec);
          return true;
        }

      case OpKind.MapSlotLen: {
          var i = (MapSlotLen) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromInt(MapFrom(func, regs, i.Map).SlotLength);
          return true;
        }

      case OpKind.MapSlotIsLive: {
          var i = (MapSlotIsLive) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromBool(MapFrom(func, regs, i.Map).SlotIsLive((int) Val(func, regs, i.SlotIndex).Raw));
          return true;
        }

      case OpKind.MapSlotKey: {
          var i = (MapSlotKey) op.Instr!;
          regs[Dest(func, i.Dest)] = MapFrom(func, regs, i.Map).EntryAt((int) Val(func, regs, i.SlotIndex).Raw).Key;
          return true;
        }

      case OpKind.MapSlotValue: {
          var i = (MapSlotValue) op.Instr!;
          regs[Dest(func, i.Dest)] = MapFrom(func, regs, i.Map).EntryAt((int) Val(func, regs, i.SlotIndex).Raw).Value;
          return true;
        }

      // ----- sets
      case OpKind.SetNew: {
          var i = (SetNew) op.Instr!;
          IrTypeId setType = DestType(func, i.Dest);
          var st = (IrSetType) _types.GetType(setType);
          var set = new PinoVmSet();
          foreach (IrOperand element in i.Elements) {
            SetPut(set, st, Val(func, regs, element));
          }

          Alloc(set, setType, i.Region);
          regs[Dest(func, i.Dest)] = PinoValue.FromObject(set);
          return true;
        }

      case OpKind.SetLen: {
          var i = (SetLen) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromInt(SetFrom(func, regs, i.Set).Count);
          return true;
        }

      case OpKind.SetAdd: {
          var i = (SetAdd) op.Instr!;
          PinoVmSet set = SetFromPtr(func, regs, i.SetPtr);
          SetPut(set, (IrSetType) _types.GetType(set.TypeId), Val(func, regs, i.Value));
          return true;
        }

      case OpKind.SetRemove: {
          var i = (SetRemove) op.Instr!;
          PinoVmSet set = SetFromPtr(func, regs, i.SetPtr);
          if (set.RemoveKey(Val(func, regs, i.Value), out PinoValue oldKey, out _)) {
            DecValue(oldKey, ((IrSetType) _types.GetType(set.TypeId)).ElementType);
          }

          return true;
        }

      case OpKind.SetContains: {
          var i = (SetContains) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromBool(SetFrom(func, regs, i.Set).ContainsKey(Val(func, regs, i.Value)));
          return true;
        }

      case OpKind.SetSlotLen: {
          var i = (SetSlotLen) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromInt(SetFrom(func, regs, i.Set).SlotLength);
          return true;
        }

      case OpKind.SetSlotIsLive: {
          var i = (SetSlotIsLive) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromBool(SetFrom(func, regs, i.Set).SlotIsLive((int) Val(func, regs, i.SlotIndex).Raw));
          return true;
        }

      case OpKind.SetSlotElem: {
          var i = (SetSlotElem) op.Instr!;
          regs[Dest(func, i.Dest)] = SetFrom(func, regs, i.Set).EntryAt((int) Val(func, regs, i.SlotIndex).Raw).Key;
          return true;
        }

      // ----- pools
      case OpKind.PoolNew: {
          var i = (PoolNew) op.Instr!;
          var pool = new PinoVmPool();
          Alloc(pool, DestType(func, i.Dest), i.Region);
          regs[Dest(func, i.Dest)] = PinoValue.FromObject(pool);
          return true;
        }

      case OpKind.PoolLen: {
          var i = (PoolLen) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromInt(PoolFrom(func, regs, i.Pool).LiveCount);
          return true;
        }

      case OpKind.PoolAdd: {
          var i = (PoolAdd) op.Instr!;
          regs[Dest(func, i.Dest)] = PoolFromPtr(func, regs, i.PoolPtr).Add(Val(func, regs, i.Value));
          return true;
        }

      case OpKind.PoolRemove: {
          var i = (PoolRemove) op.Instr!;
          PinoVmPool pool = PoolFromPtr(func, regs, i.PoolPtr);
          IrTypeId optionType = DestType(func, i.Dest);
          regs[Dest(func, i.Dest)] = pool.TryRemove(Val(func, regs, i.Handle), out PinoValue removed)
              ? MakeSome(optionType, removed)
              : MakeNone(optionType);
          return true;
        }

      case OpKind.PoolGet: {
          var i = (PoolGet) op.Instr!;
          PinoVmPool pool = PoolFrom(func, regs, i.Pool);
          PinoValue handle = Val(func, regs, i.Handle);
          IrTypeId optionType = DestType(func, i.Dest);
          if (pool.IsLive(handle)) {
            PinoValue value = pool.SlotAt((int) handle.HandleIndex).Value;
            IncValue(value, ((IrPoolType) _types.GetType(pool.TypeId)).ElementType);
            regs[Dest(func, i.Dest)] = MakeSome(optionType, value);
          } else {
            regs[Dest(func, i.Dest)] = MakeNone(optionType);
          }

          return true;
        }

      case OpKind.PoolGetOrPanic: {
          var i = (PoolGetOrPanic) op.Instr!;
          PinoVmPool pool = PoolFrom(func, regs, i.Pool);
          PinoValue handle = Val(func, regs, i.Handle);
          if (!pool.IsLive(handle)) {
            throw new PinoVmPanic(IrLowerer.FormatPoolStaleHandlePanic(i.PoolName));
          }

          regs[Dest(func, i.Dest)] = pool.SlotAt((int) handle.HandleIndex).Value;
          return true;
        }

      case OpKind.PoolElemAddr: {
          var i = (PoolElemAddr) op.Instr!;
          PinoVmPool pool = PoolFromPtr(func, regs, i.PoolPtr);
          PinoValue handle = Val(func, regs, i.Handle);
          if (!pool.IsLive(handle)) {
            throw new PinoVmPanic(IrLowerer.FormatPoolStaleHandlePanic(i.PoolName));
          }

          regs[Dest(func, i.Dest)] = new PlaceRef(PlaceKind.PoolSlot, pool, (int) handle.HandleIndex).ToValue();
          return true;
        }

      case OpKind.PoolHandles: {
          var i = (PoolHandles) op.Instr!;
          PinoVmPool pool = PoolFrom(func, regs, i.Pool);
          var vec = new PinoVmVec(pool.LiveCount);
          for (int slot = 0; slot < pool.SlotLength; slot++) {
            if (pool.SlotAt(slot).State == PoolSlotState.Live) {
              vec.Add(PinoValue.FromHandle((uint) slot, pool.SlotAt(slot).Gen));
            }
          }

          Alloc(vec, DestType(func, i.Dest), i.Region);
          regs[Dest(func, i.Dest)] = PinoValue.FromObject(vec);
          return true;
        }

      case OpKind.PoolClear: {
          var i = (PoolClear) op.Instr!;
          PinoVmPool pool = PoolFromPtr(func, regs, i.PoolPtr);
          IrTypeId element = ((IrPoolType) _types.GetType(pool.TypeId)).ElementType;
          foreach (PinoValue dropped in pool.Clear()) {
            DecValue(dropped, element);
          }

          return true;
        }

      case OpKind.PoolContains: {
          var i = (PoolContains) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromBool(PoolFrom(func, regs, i.Pool).IsLive(Val(func, regs, i.Handle)));
          return true;
        }

      case OpKind.PoolSlotLen: {
          var i = (PoolSlotLen) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromInt(PoolFrom(func, regs, i.Pool).SlotLength);
          return true;
        }

      case OpKind.PoolSlotIsLive: {
          var i = (PoolSlotIsLive) op.Instr!;
          PinoVmPool pool = PoolFrom(func, regs, i.Pool);
          long slot = Val(func, regs, i.SlotIndex).Raw;
          regs[Dest(func, i.Dest)] = PinoValue.FromBool((ulong) slot < (ulong) pool.SlotLength && pool.SlotAt((int) slot).State == PoolSlotState.Live);
          return true;
        }

      case OpKind.PoolSlotHandle: {
          var i = (PoolSlotHandle) op.Instr!;
          PinoVmPool pool = PoolFrom(func, regs, i.Pool);
          int slot = (int) Val(func, regs, i.SlotIndex).Raw;
          regs[Dest(func, i.Dest)] = PinoValue.FromHandle((uint) slot, pool.SlotAt(slot).Gen);
          return true;
        }

      case OpKind.PoolSlotValue: {
          var i = (PoolSlotValue) op.Instr!;
          regs[Dest(func, i.Dest)] = PoolFrom(func, regs, i.Pool).SlotAt((int) Val(func, regs, i.SlotIndex).Raw).Value;
          return true;
        }

      // ----- strings
      case OpKind.StrLen: {
          var i = (StrLen) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromInt(StrFrom(func, regs, i.Str).RuneCount);
          return true;
        }

      case OpKind.StrGetRune: {
          var i = (StrGetRune) op.Instr!;
          PinoVmString s = StrFrom(func, regs, i.Str);
          long index = Val(func, regs, i.Index).Raw;
          if ((ulong) index >= (ulong) s.RuneCount) {
            throw new PinoVmPanic(IndexPanic(index, s.RuneCount, "string"));
          }

          regs[Dest(func, i.Dest)] = PinoValue.FromRune(s.DecodeRuneAt(s.ByteOffsetOfRune((int) index), out _));
          return true;
        }

      case OpKind.StrByteLen: {
          var i = (StrByteLen) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromInt(StrFrom(func, regs, i.Str).ByteLength);
          return true;
        }

      case OpKind.StrDecodeRune: {
          var i = (StrDecodeRune) op.Instr!;
          PinoVmString s = StrFrom(func, regs, i.Str);
          regs[Dest(func, i.Dest)] = PinoValue.FromRune(s.DecodeRuneAt((int) Val(func, regs, i.BytePos).Raw, out _));
          return true;
        }

      case OpKind.StrNextBytePos: {
          var i = (StrNextBytePos) op.Instr!;
          PinoVmString s = StrFrom(func, regs, i.Str);
          int pos = (int) Val(func, regs, i.BytePos).Raw;
          s.DecodeRuneAt(pos, out int width);
          regs[Dest(func, i.Dest)] = PinoValue.FromInt(pos + width);
          return true;
        }

      case OpKind.StrEq: {
          var i = (StrEq) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromBool(StrFrom(func, regs, i.Left).ContentEquals(StrFrom(func, regs, i.Right)));
          return true;
        }

      case OpKind.StrCmp: {
          var i = (StrCmp) op.Instr!;
          regs[Dest(func, i.Dest)] = PinoValue.FromInt(StrFrom(func, regs, i.Left).CompareOrdinal(StrFrom(func, regs, i.Right)));
          return true;
        }

      case OpKind.StrConcat: {
          var i = (StrConcat) op.Instr!;
          PinoVmString a = StrFrom(func, regs, i.Left);
          PinoVmString b = StrFrom(func, regs, i.Right);
          regs[Dest(func, i.Dest)] = PinoValue.FromObject(ConcatBytes(i.Region, a.Span, b.Span));
          return true;
        }

      case OpKind.StrAppendSlot: {
          var i = (StrAppendSlot) op.Instr!;
          int slotReg = func.SlotBase + i.Slot.Index;
          PinoVmString a = UseObject((PinoVmString) regs[slotReg].Obj!, "string access");
          PinoVmString b = StrFrom(func, regs, i.Right);
          if (b.ByteLength == 0) {
            return true;
          }

          if (a.ByteLength == 0) {
            _rc.Inc(b);
            DecObject(a);
            regs[slotReg] = PinoValue.FromObject(b);
            return true;
          }

          if (a.ShadowRc == 1 && !a.IsBufferShared && !ReferenceEquals(a, b)) {
            a.AppendInPlace(b.Span);
            return true;
          }

          int needed = a.ByteLength + b.ByteLength;
          int cap = Math.Max(16, needed);
          var bytes = new byte[cap];
          a.Span.CopyTo(bytes);
          b.Span.CopyTo(bytes.AsSpan(a.ByteLength));
          PinoVmString next = Alloc(new PinoVmString(bytes, 0, needed), _types.String, i.Region);
          DecObject(a);
          regs[slotReg] = PinoValue.FromObject(next);
          return true;
        }

      case OpKind.StrConcatN: {
          var i = (StrConcatN) op.Instr!;
          int total = 0;
          var parts = new PinoVmString[i.Parts.Length];
          for (int k = 0; k < parts.Length; k++) {
            parts[k] = StrFrom(func, regs, i.Parts[k]);
            total += parts[k].ByteLength;
          }

          var bytes = new byte[total];
          int at = 0;
          foreach (PinoVmString part in parts) {
            part.Span.CopyTo(bytes.AsSpan(at));
            at += part.ByteLength;
          }

          regs[Dest(func, i.Dest)] = PinoValue.FromObject(Alloc(new PinoVmString(bytes, 0, bytes.Length), _types.String, i.Region));
          return true;
        }

      case OpKind.StrConcatRune: {
          var i = (StrConcatRune) op.Instr!;
          PinoVmString s = StrFrom(func, regs, i.Str);
          regs[Dest(func, i.Dest)] = PinoValue.FromObject(ConcatBytes(i.Region, s.Span, EncodeRune((int) Val(func, regs, i.RuneValue).Raw)));
          return true;
        }

      case OpKind.RuneConcatStr: {
          var i = (RuneConcatStr) op.Instr!;
          PinoVmString s = StrFrom(func, regs, i.Str);
          regs[Dest(func, i.Dest)] = PinoValue.FromObject(ConcatBytes(i.Region, EncodeRune((int) Val(func, regs, i.RuneValue).Raw), s.Span));
          return true;
        }

      default:
        return false;
    }
  }

  private PinoVmString ConcatBytes(AllocRegion region, ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) {
    var bytes = new byte[left.Length + right.Length];
    left.CopyTo(bytes);
    right.CopyTo(bytes.AsSpan(left.Length));
    return Alloc(new PinoVmString(bytes, 0, bytes.Length), _types.String, region);
  }

  private static byte[] EncodeRune(int codePoint) {
    if (!Rune.IsValid(codePoint)) {
      codePoint = 0xFFFD;
    }

    return Encoding.UTF8.GetBytes(char.ConvertFromUtf32(codePoint));
  }

  /// <summary>Inserts or updates a map entry; consumes <paramref name="key"/> and <paramref name="value"/>.</summary>
  private void MapPut(PinoVmMap map, IrMapType mapType, PinoValue key, PinoValue value) {
    if (map.TryFindSlot(key, out int slot)) {
      ref PinoVmOrderedTable.Entry entry = ref map.EntryAt(slot);
      PinoValue oldValue = entry.Value;
      entry.Value = value;
      DecValue(oldValue, mapType.ValueType);
      DecValue(key, mapType.KeyType);
    } else {
      map.Append(key, value);
    }
  }

  /// <summary>Adds a set element; consumes <paramref name="element"/> and drops it again when it was already present.</summary>
  private void SetPut(PinoVmSet set, IrSetType setType, PinoValue element) {
    if (set.ContainsKey(element)) {
      DecValue(element, setType.ElementType);
    } else {
      set.Append(element, default);
    }
  }
}
