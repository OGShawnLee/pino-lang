using System.Collections.Immutable;

using Pino.Compiler.Text;

namespace Pino.Compiler.IR;

public enum AllocRegionKind : byte {
  Heap,
  Arena,
  Stack,
}

/// <summary>
/// Allocation region tag carried by every allocating IR instruction (amended AD-002 / §4.5).
/// </summary>
public readonly record struct AllocRegion(AllocRegionKind Kind, int ArenaId = -1) {
  public static AllocRegion Heap => new(AllocRegionKind.Heap);

  public static AllocRegion Stack => new(AllocRegionKind.Stack);

  public static AllocRegion Arena(int id) {
    ArgumentOutOfRangeException.ThrowIfNegative(id);
    return new(AllocRegionKind.Arena, id);
  }

  public override string ToString() => Kind switch {
    AllocRegionKind.Heap => "heap",
    AllocRegionKind.Arena => $"arena({ArenaId})",
    AllocRegionKind.Stack => "stack",
    _ => "invalid",
  };
}

public enum IrCmpOp : byte {
  Eq,
  Ne,
  Lt,
  Le,
  Gt,
  Ge,
}

public enum IrRuntimeOp : byte {
  PrintlnN,
  Eprintln,
  Exit,
  Readline,
  ParseInt,
  ParseFloat,
  StrFirstRune,
  ToStr,
  IfaceTypeName,
  RandFloat,
  RandInt,
  Time,
  Sleep,
  Clear,
  RegexCompile,
  ReadFile,
  WriteFile,
  FileExists,
  StrLower,
  StrUpper,
  StrTrim,
  StrTrimStart,
  StrTrimEnd,
  StrClone,
  StrToCString,
  StrContains,
  StrStartsWith,
  StrEndsWith,
  StrIndexOf,
  StrLastIndexOf,
  StrSplit,
  StrReplace,
  StrSubstring,
  RegexPattern,
  RegexHasMatch,
  RegexMatchPrefix,
  RegexFind,
  RegexFindAll,
  RegexReplace,
}

public readonly record struct IrBlockId(int Index) {
  public static IrBlockId Invalid => new(-1);
  public bool IsValid => Index >= 0;
  public override string ToString() => IsValid ? $"bb{Index}" : "<invalid-bb>";
}

public readonly record struct IrTempId(int Index) {
  public static IrTempId Invalid => new(-1);
  public bool IsValid => Index >= 0;
  public override string ToString() => IsValid ? $"%t{Index}" : "<invalid-temp>";
}

public readonly record struct IrSlotId(int Index) {
  public static IrSlotId Invalid => new(-1);
  public bool IsValid => Index >= 0;
  public override string ToString() => IsValid ? $"$s{Index}" : "<invalid-slot>";
}

public readonly record struct IrGlobalId(int Index) {
  public static IrGlobalId Invalid => new(-1);
  public bool IsValid => Index >= 0;
  public override string ToString() => IsValid ? $"$g{Index}" : "<invalid-global>";
}

public readonly record struct IrFuncId(int Index) {
  public static IrFuncId Invalid => new(-1);
  public bool IsValid => Index >= 0;
  public override string ToString() => IsValid ? $"fn#{Index}" : "<invalid-fn>";
}

public readonly record struct IrVTableId(int Index) {
  public static IrVTableId Invalid => new(-1);
  public bool IsValid => Index >= 0;
  public override string ToString() => IsValid ? $"vt{Index}" : "<invalid-vt>";
}

public enum IrOperandKind : byte {
  Temp,
  Param,
}

/// <summary>
/// Value operand referencing either a single-def temporary or a function parameter.
/// </summary>
public readonly record struct IrOperand(IrOperandKind Kind, int Index) {
  public static IrOperand FromTemp(IrTempId id) => new(IrOperandKind.Temp, id.Index);

  public static IrOperand FromParam(int paramIndex) => new(IrOperandKind.Param, paramIndex);

  public static implicit operator IrOperand(IrTempId id) => FromTemp(id);

  public bool IsTemp => Kind == IrOperandKind.Temp;

  public bool IsParam => Kind == IrOperandKind.Param;

  public IrTempId TempId => new(Index);
}

public abstract record IrInstruction(SourceSpan Span) {
  public virtual IrTempId? DefinedTemp => null;

  public virtual AllocRegion? AllocationRegion => null;

  public abstract void CollectReadOperands(List<IrOperand> operands);
}

// ---------------------------------------------------------------------------
// Constants & moves
// ---------------------------------------------------------------------------

public sealed record ConstInt(IrTempId Dest, long Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) { }
}

public sealed record ConstFloat(IrTempId Dest, double Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) { }
}

public sealed record ConstBool(IrTempId Dest, bool Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) { }
}

public sealed record ConstRune(IrTempId Dest, int Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) { }
}

public sealed record ConstString(IrTempId Dest, int StringPoolId, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) { }
}

public sealed record ConstEnum(IrTempId Dest, IrTypeId EnumType, int Ordinal, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) { }
}

public sealed record Copy(IrTempId Dest, IrOperand Source, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Source);
}

public sealed record Move(IrTempId Dest, IrOperand Source, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Source);
}

// ---------------------------------------------------------------------------
// Locals & places
// ---------------------------------------------------------------------------

public sealed record LoadSlot(IrTempId Dest, IrSlotId Slot, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) { }
}

public sealed record StoreSlot(IrSlotId Slot, IrOperand Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Value);
}

public sealed record AddrLocal(IrTempId Dest, IrSlotId Slot, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) { }
}

public sealed record AddrGlobal(IrTempId Dest, IrGlobalId Global, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) { }
}

public sealed record FieldGet(IrTempId Dest, IrOperand Target, int FieldIndex, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Target);
}

public sealed record FieldAddr(IrTempId Dest, IrOperand TargetPtr, int FieldIndex, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(TargetPtr);
}

public sealed record LoadPtr(IrTempId Dest, IrOperand Ptr, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Ptr);
}

public sealed record StorePtr(IrOperand Ptr, IrOperand Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Ptr);
    operands.Add(Value);
  }
}

public sealed record PtrEq(IrTempId Dest, IrOperand LeftPtr, IrOperand RightPtr, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(LeftPtr);
    operands.Add(RightPtr);
  }
}

// ---------------------------------------------------------------------------
// Arithmetic, casts & comparisons
// ---------------------------------------------------------------------------

public sealed record Add(IrTempId Dest, IrOperand Left, IrOperand Right, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Left);
    operands.Add(Right);
  }
}

public sealed record Sub(IrTempId Dest, IrOperand Left, IrOperand Right, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Left);
    operands.Add(Right);
  }
}

public sealed record Mul(IrTempId Dest, IrOperand Left, IrOperand Right, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Left);
    operands.Add(Right);
  }
}

public sealed record DivF64(IrTempId Dest, IrOperand Left, IrOperand Right, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Left);
    operands.Add(Right);
  }
}

public sealed record FloorDiv(IrTempId Dest, IrOperand Left, IrOperand Right, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Left);
    operands.Add(Right);
  }
}

public sealed record FloorMod(IrTempId Dest, IrOperand Left, IrOperand Right, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Left);
    operands.Add(Right);
  }
}

public sealed record Neg(IrTempId Dest, IrOperand Operand, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Operand);
}

public sealed record Not(IrTempId Dest, IrOperand Operand, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Operand);
}

public sealed record Cmp(IrTempId Dest, IrCmpOp Op, IrOperand Left, IrOperand Right, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Left);
    operands.Add(Right);
  }
}

public sealed record CastI64ToF64(IrTempId Dest, IrOperand Source, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Source);
}

public sealed record CastF64ToI64(IrTempId Dest, IrOperand Source, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Source);
}

public sealed record CastI64ToRune(IrTempId Dest, IrOperand Source, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Source);
}

public sealed record CastRuneToI64(IrTempId Dest, IrOperand Source, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Source);
}

public sealed record StructEq(IrTempId Dest, IrOperand Left, IrOperand Right, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Left);
    operands.Add(Right);
  }
}

// ---------------------------------------------------------------------------
// Aggregates
// ---------------------------------------------------------------------------

public sealed record StructNew(IrTempId Dest, ImmutableArray<IrOperand> Fields, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.AddRange(Fields);
}

public sealed record TupleNew(IrTempId Dest, ImmutableArray<IrOperand> Fields, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.AddRange(Fields);
}

public sealed record TupleField(IrTempId Dest, IrOperand Tuple, int FieldIndex, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Tuple);
}

public sealed record UnionNew(
    IrTempId Dest,
    int VariantIndex,
    ImmutableArray<IrOperand> Fields,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.AddRange(Fields);
}

public sealed record UnionTag(IrTempId Dest, IrOperand Union, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Union);
}

public sealed record UnionField(
    IrTempId Dest,
    IrOperand Union,
    int VariantIndex,
    int FieldIndex,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Union);
}

public sealed record IfaceBox(
    IrTempId Dest,
    IrOperand Value,
    IrVTableId VTable,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Value);
}

public sealed record IfaceFieldGet(
    IrTempId Dest,
    IrOperand Iface,
    int PropertyIndex,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Iface);
}

public sealed record IfaceFieldAddr(
    IrTempId Dest,
    IrOperand IfacePtr,
    int PropertyIndex,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(IfacePtr);
}

// ---------------------------------------------------------------------------
// Closures & cells
// ---------------------------------------------------------------------------

public sealed record CellNew(
    IrTempId Dest,
    IrOperand InitialValue,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(InitialValue);
}

public sealed record CellGet(IrTempId Dest, IrOperand Cell, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Cell);
}

public sealed record CellSet(IrOperand Cell, IrOperand Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Cell);
    operands.Add(Value);
  }
}

public sealed record CellAddr(IrTempId Dest, IrOperand Cell, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Cell);
}

public sealed record EnvNew(
    IrTempId Dest,
    ImmutableArray<IrOperand> Captures,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.AddRange(Captures);
}

public sealed record EnvFieldGet(IrTempId Dest, IrOperand Env, int FieldIndex, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Env);
}

public sealed record ClosureMake(
    IrTempId Dest,
    IrFuncId Function,
    IrOperand? Env,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    if (Env is { } envOp) {
      operands.Add(envOp);
    }
  }
}

// ---------------------------------------------------------------------------
// Vectors
// ---------------------------------------------------------------------------

public sealed record VecNew(
    IrTempId Dest,
    ImmutableArray<IrOperand> Elements,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.AddRange(Elements);
}

public sealed record VecWithCap(
    IrTempId Dest,
    IrOperand Capacity,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Capacity);
}

public sealed record VecLen(IrTempId Dest, IrOperand Vec, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Vec);
}

public sealed record VecCap(IrTempId Dest, IrOperand Vec, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Vec);
}

public sealed record VecGet(
    IrTempId Dest,
    IrOperand Vec,
    IrOperand Index,
    bool Unchecked = false,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Vec);
    operands.Add(Index);
  }
}

public sealed record VecElemAddr(
    IrTempId Dest,
    IrOperand VecPtr,
    IrOperand Index,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(VecPtr);
    operands.Add(Index);
  }
}

public sealed record VecPush(IrOperand VecPtr, IrOperand Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(VecPtr);
    operands.Add(Value);
  }
}

public sealed record VecPop(IrTempId Dest, IrOperand VecPtr, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(VecPtr);
}

public sealed record VecSwap(
    IrOperand VecPtr,
    IrOperand IndexA,
    IrOperand IndexB,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(VecPtr);
    operands.Add(IndexA);
    operands.Add(IndexB);
  }
}

public sealed record VecContains(
    IrTempId Dest,
    IrOperand Vec,
    IrOperand Value,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Vec);
    operands.Add(Value);
  }
}

// ---------------------------------------------------------------------------
// Maps
// ---------------------------------------------------------------------------

public sealed record MapNew(
    IrTempId Dest,
    ImmutableArray<(IrOperand Key, IrOperand Value)> Entries,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) {
    foreach ((IrOperand key, IrOperand value) in Entries) {
      operands.Add(key);
      operands.Add(value);
    }
  }
}

public sealed record MapLen(IrTempId Dest, IrOperand Map, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Map);
}

public sealed record MapGet(IrTempId Dest, IrOperand Map, IrOperand Key, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Map);
    operands.Add(Key);
  }
}

public sealed record MapElemAddr(IrTempId Dest, IrOperand MapPtr, IrOperand Key, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(MapPtr);
    operands.Add(Key);
  }
}

public sealed record MapSet(IrOperand MapPtr, IrOperand Key, IrOperand Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(MapPtr);
    operands.Add(Key);
    operands.Add(Value);
  }
}

public sealed record MapRemove(IrTempId Dest, IrOperand MapPtr, IrOperand Key, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(MapPtr);
    operands.Add(Key);
  }
}

public sealed record MapContains(IrTempId Dest, IrOperand Map, IrOperand Key, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Map);
    operands.Add(Key);
  }
}

public sealed record MapKeys(IrTempId Dest, IrOperand Map, AllocRegion Region, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Map);
}

public sealed record MapValues(IrTempId Dest, IrOperand Map, AllocRegion Region, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Map);
}

public sealed record MapSlotLen(IrTempId Dest, IrOperand Map, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Map);
}

public sealed record MapSlotIsLive(IrTempId Dest, IrOperand Map, IrOperand SlotIndex, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Map);
    operands.Add(SlotIndex);
  }
}

public sealed record MapSlotKey(IrTempId Dest, IrOperand Map, IrOperand SlotIndex, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Map);
    operands.Add(SlotIndex);
  }
}

public sealed record MapSlotValue(IrTempId Dest, IrOperand Map, IrOperand SlotIndex, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Map);
    operands.Add(SlotIndex);
  }
}

// ---------------------------------------------------------------------------
// Sets
// ---------------------------------------------------------------------------

public sealed record SetNew(
    IrTempId Dest,
    ImmutableArray<IrOperand> Elements,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.AddRange(Elements);
}

public sealed record SetLen(IrTempId Dest, IrOperand Set, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Set);
}

public sealed record SetAdd(IrOperand SetPtr, IrOperand Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(SetPtr);
    operands.Add(Value);
  }
}

public sealed record SetRemove(IrOperand SetPtr, IrOperand Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(SetPtr);
    operands.Add(Value);
  }
}

public sealed record SetContains(IrTempId Dest, IrOperand Set, IrOperand Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Set);
    operands.Add(Value);
  }
}

public sealed record SetSlotLen(IrTempId Dest, IrOperand Set, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Set);
}

public sealed record SetSlotIsLive(IrTempId Dest, IrOperand Set, IrOperand SlotIndex, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Set);
    operands.Add(SlotIndex);
  }
}

public sealed record SetSlotElem(IrTempId Dest, IrOperand Set, IrOperand SlotIndex, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Set);
    operands.Add(SlotIndex);
  }
}

// ---------------------------------------------------------------------------
// Pools (D-185)
// ---------------------------------------------------------------------------

public sealed record PoolNew(IrTempId Dest, AllocRegion Region, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) { }
}

public sealed record PoolLen(IrTempId Dest, IrOperand Pool, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Pool);
}

public sealed record PoolAdd(IrTempId Dest, IrOperand PoolPtr, IrOperand Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(PoolPtr);
    operands.Add(Value);
  }
}

public sealed record PoolRemove(IrTempId Dest, IrOperand PoolPtr, IrOperand Handle, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(PoolPtr);
    operands.Add(Handle);
  }
}

public sealed record PoolGet(IrTempId Dest, IrOperand Pool, IrOperand Handle, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Pool);
    operands.Add(Handle);
  }
}

public sealed record PoolGetOrPanic(
    IrTempId Dest,
    IrOperand Pool,
    IrOperand Handle,
    string PoolName,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Pool);
    operands.Add(Handle);
  }
}

public sealed record PoolElemAddr(
    IrTempId Dest,
    IrOperand PoolPtr,
    IrOperand Handle,
    string PoolName,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(PoolPtr);
    operands.Add(Handle);
  }
}

public sealed record PoolHandles(IrTempId Dest, IrOperand Pool, AllocRegion Region, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Pool);
}

public sealed record PoolClear(IrOperand PoolPtr, SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(PoolPtr);
}

public sealed record PoolContains(IrTempId Dest, IrOperand Pool, IrOperand Handle, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Pool);
    operands.Add(Handle);
  }
}

public sealed record PoolSlotLen(IrTempId Dest, IrOperand Pool, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Pool);
}

public sealed record PoolSlotIsLive(IrTempId Dest, IrOperand Pool, IrOperand SlotIndex, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Pool);
    operands.Add(SlotIndex);
  }
}

public sealed record PoolSlotHandle(IrTempId Dest, IrOperand Pool, IrOperand SlotIndex, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Pool);
    operands.Add(SlotIndex);
  }
}

public sealed record PoolSlotValue(IrTempId Dest, IrOperand Pool, IrOperand SlotIndex, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Pool);
    operands.Add(SlotIndex);
  }
}

// ---------------------------------------------------------------------------
// Strings
// ---------------------------------------------------------------------------

public sealed record StrLen(IrTempId Dest, IrOperand Str, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Str);
}

public sealed record StrGetRune(IrTempId Dest, IrOperand Str, IrOperand Index, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Str);
    operands.Add(Index);
  }
}

public sealed record StrByteLen(IrTempId Dest, IrOperand Str, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Str);
}

public sealed record StrDecodeRune(IrTempId Dest, IrOperand Str, IrOperand BytePos, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Str);
    operands.Add(BytePos);
  }
}

public sealed record StrNextBytePos(IrTempId Dest, IrOperand Str, IrOperand BytePos, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Str);
    operands.Add(BytePos);
  }
}

public sealed record StrEq(IrTempId Dest, IrOperand Left, IrOperand Right, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Left);
    operands.Add(Right);
  }
}

public sealed record StrCmp(IrTempId Dest, IrOperand Left, IrOperand Right, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Left);
    operands.Add(Right);
  }
}

public sealed record StrConcat(
    IrTempId Dest,
    IrOperand Left,
    IrOperand Right,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Left);
    operands.Add(Right);
  }
}

public sealed record StrAppendSlot(
    IrSlotId Slot,
    IrOperand Right,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Right);
}

public sealed record StrConcatN(
    IrTempId Dest,
    ImmutableArray<IrOperand> Parts,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.AddRange(Parts);
}

public sealed record StrConcatRune(
    IrTempId Dest,
    IrOperand Str,
    IrOperand RuneValue,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Str);
    operands.Add(RuneValue);
  }
}

public sealed record RuneConcatStr(
    IrTempId Dest,
    IrOperand RuneValue,
    IrOperand Str,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(RuneValue);
    operands.Add(Str);
  }
}

// ---------------------------------------------------------------------------
// Calls
// ---------------------------------------------------------------------------

public sealed record CallDirect(
    IrTempId? Dest,
    IrFuncId Callee,
    ImmutableArray<IrOperand> Arguments,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.AddRange(Arguments);
}

public sealed record CallClosure(
    IrTempId? Dest,
    IrOperand Closure,
    ImmutableArray<IrOperand> Arguments,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Closure);
    operands.AddRange(Arguments);
  }
}

public sealed record CallIface(
    IrTempId? Dest,
    IrOperand Receiver,
    int MethodIndex,
    bool IsMutating,
    ImmutableArray<IrOperand> Arguments,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Receiver);
    operands.AddRange(Arguments);
  }
}

public sealed record CallRuntime(
    IrTempId? Dest,
    IrRuntimeOp Op,
    ImmutableArray<IrOperand> Arguments,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.AddRange(Arguments);
}

// ---------------------------------------------------------------------------
// Ownership & arenas (amended AD-002)
// ---------------------------------------------------------------------------

public sealed record RcInc(IrOperand Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Value);
}

public sealed record RcDec(IrOperand Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Value);
}

public sealed record EnsureUnique(IrOperand PlacePtr, SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(PlacePtr);
}

public sealed record Reset(IrTempId Dest, IrOperand Value, SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Value);
}

public sealed record Reuse(
    IrTempId Dest,
    IrOperand Token,
    int VariantIndex,
    ImmutableArray<IrOperand> Fields,
    AllocRegion Region,
    SourceSpan Span = default) : IrInstruction(Span) {
  public override IrTempId? DefinedTemp => Dest;
  public override AllocRegion? AllocationRegion => Region;
  public override void CollectReadOperands(List<IrOperand> operands) {
    operands.Add(Token);
    operands.AddRange(Fields);
  }
}

public sealed record ArenaEnter(int ArenaId, SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) { }
}

public sealed record ArenaExit(int ArenaId, SourceSpan Span = default) : IrInstruction(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) { }
}

// ---------------------------------------------------------------------------
// Terminators
// ---------------------------------------------------------------------------

public abstract record IrTerminator(SourceSpan Span) {
  public abstract void CollectReadOperands(List<IrOperand> operands);

  public abstract void CollectTargets(List<IrBlockId> targets);
}

public sealed record Br(IrBlockId Target, SourceSpan Span = default) : IrTerminator(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) { }
  public override void CollectTargets(List<IrBlockId> targets) => targets.Add(Target);
}

public sealed record BrIf(
    IrOperand Condition,
    IrBlockId TrueTarget,
    IrBlockId FalseTarget,
    SourceSpan Span = default) : IrTerminator(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Condition);

  public override void CollectTargets(List<IrBlockId> targets) {
    targets.Add(TrueTarget);
    targets.Add(FalseTarget);
  }
}

public sealed record Switch(
    IrOperand Scrutinee,
    ImmutableArray<(int CaseValue, IrBlockId Target)> Cases,
    IrBlockId? DefaultTarget,
    SourceSpan Span = default) : IrTerminator(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Scrutinee);

  public override void CollectTargets(List<IrBlockId> targets) {
    foreach ((_, IrBlockId target) in Cases) {
      targets.Add(target);
    }

    if (DefaultTarget is { } defaultTarget) {
      targets.Add(defaultTarget);
    }
  }
}

public sealed record Ret(IrOperand Value, SourceSpan Span = default) : IrTerminator(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Value);
  public override void CollectTargets(List<IrBlockId> targets) { }
}

public sealed record RetVoid(SourceSpan Span = default) : IrTerminator(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) { }
  public override void CollectTargets(List<IrBlockId> targets) { }
}

public sealed record Panic(IrOperand Message, SourceSpan Span = default, bool IsAssert = false) : IrTerminator(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) => operands.Add(Message);
  public override void CollectTargets(List<IrBlockId> targets) { }
}

public sealed record Unreachable(SourceSpan Span = default) : IrTerminator(Span) {
  public override void CollectReadOperands(List<IrOperand> operands) { }
  public override void CollectTargets(List<IrBlockId> targets) { }
}
