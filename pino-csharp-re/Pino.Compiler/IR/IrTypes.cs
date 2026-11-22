using System.Collections.Immutable;

using Pino.Compiler.Checking;

namespace Pino.Compiler.IR;

/// <summary>
/// Dense index into an <see cref="IrTypeTable"/>.
/// </summary>
public readonly record struct IrTypeId(int Index) {
  public static IrTypeId Invalid => new(-1);

  public bool IsValid => Index >= 0;

  public override string ToString() => IsValid ? $"t{Index}" : "<invalid-type>";
}

public enum IrTypeKind : byte {
  Void,
  Never,
  Bool,
  Int,
  Float,
  Rune,
  Enum,
  Handle,
  String,
  Regex,
  Struct,
  Vector,
  Map,
  Set,
  Pool,
  Union,
  Tuple,
  Closure,
  Interface,
  Ptr,
  Cell,
  Env,
}

public enum IrParamOwnership : byte {
  Owned,
  Borrowed,
  Mut,
}

public abstract class IrType {
  private protected IrType(IrTypeId id, IrTypeKind kind, string name) {
    Id = id;
    Kind = kind;
    Name = name;
  }

  public IrTypeId Id { get; }

  public IrTypeKind Kind { get; }

  public string Name { get; }

  public bool IsPointerFree { get; internal set; }

  public bool IsCycleCapable { get; internal set; }

  public bool IsRecursiveUnion { get; internal set; }

  public int SizeBytes { get; internal set; }

  public int AlignBytes { get; internal set; }

  /// <summary>
  /// Non-mutating struct parameters larger than 16 bytes pass by <c>const T*</c> in C ABI.
  /// </summary>
  public bool PassByConstPtr => Kind == IrTypeKind.Struct && SizeBytes > 16;

  /// <summary>
  /// True when values of this type require Perceus RC operations (<c>rc_inc</c>/<c>rc_dec</c>).
  /// </summary>
  public bool IsRcManaged => !IsPointerFree && Kind is not (IrTypeKind.Ptr or IrTypeKind.Void or IrTypeKind.Never);

  /// <summary>
  /// True when values of this type are CoW-managed containers or boxed interfaces.
  /// </summary>
  public bool IsCoWContainer =>
      Kind is IrTypeKind.Vector or IrTypeKind.Map or IrTypeKind.Set or IrTypeKind.Pool or IrTypeKind.Interface;

  public override string ToString() => Name;
}

public sealed class IrPrimitiveType : IrType {
  internal IrPrimitiveType(
      IrTypeId id,
      IrTypeKind kind,
      string name,
      bool isPointerFree,
      int sizeBytes,
      int alignBytes)
      : base(id, kind, name) {
    IsPointerFree = isPointerFree;
    SizeBytes = sizeBytes;
    AlignBytes = alignBytes;
  }
}

public sealed class IrEnumType : IrType {
  internal IrEnumType(
      IrTypeId id,
      string name,
      ImmutableArray<string> memberNames,
      EnumSymbol? definition)
      : base(id, IrTypeKind.Enum, name) {
    MemberNames = memberNames;
    Definition = definition;
    IsPointerFree = true;
    SizeBytes = 4;
    AlignBytes = 4;
  }

  public ImmutableArray<string> MemberNames { get; }

  public EnumSymbol? Definition { get; }
}

public sealed class IrHandleType : IrType {
  internal IrHandleType(IrTypeId id, string name, IrTypeId elementType)
      : base(id, IrTypeKind.Handle, name) {
    ElementType = elementType;
    IsPointerFree = true;
    SizeBytes = 8;
    AlignBytes = 4;
  }

  public IrTypeId ElementType { get; }
}

public sealed class IrVectorType : IrType {
  internal IrVectorType(IrTypeId id, string name, IrTypeId elementType)
      : base(id, IrTypeKind.Vector, name) {
    ElementType = elementType;
    IsPointerFree = false;
    SizeBytes = 8;
    AlignBytes = 8;
  }

  public IrTypeId ElementType { get; }
}

public sealed class IrMapType : IrType {
  internal IrMapType(IrTypeId id, string name, IrTypeId keyType, IrTypeId valueType)
      : base(id, IrTypeKind.Map, name) {
    KeyType = keyType;
    ValueType = valueType;
    IsPointerFree = false;
    SizeBytes = 8;
    AlignBytes = 8;
  }

  public IrTypeId KeyType { get; }

  public IrTypeId ValueType { get; }
}

public sealed class IrSetType : IrType {
  internal IrSetType(IrTypeId id, string name, IrTypeId elementType)
      : base(id, IrTypeKind.Set, name) {
    ElementType = elementType;
    IsPointerFree = false;
    SizeBytes = 8;
    AlignBytes = 8;
  }

  public IrTypeId ElementType { get; }
}

public sealed class IrPoolType : IrType {
  internal IrPoolType(IrTypeId id, string name, IrTypeId elementType)
      : base(id, IrTypeKind.Pool, name) {
    ElementType = elementType;
    IsPointerFree = false;
    SizeBytes = 8;
    AlignBytes = 8;
  }

  public IrTypeId ElementType { get; }
}

public readonly record struct IrStructField(
    string Name,
    IrTypeId Type,
    bool IsMutable,
    int OffsetBytes = 0);

public sealed class IrStructType : IrType {
  internal IrStructType(
      IrTypeId id,
      string name,
      StructSymbol? definition,
      ImmutableArray<IrTypeId> typeArguments)
      : base(id, IrTypeKind.Struct, name) {
    Definition = definition;
    TypeArguments = typeArguments;
    Fields = [];
  }

  public StructSymbol? Definition { get; }

  public ImmutableArray<IrTypeId> TypeArguments { get; }

  public ImmutableArray<IrStructField> Fields { get; internal set; }

  internal bool HasDefinedFields { get; set; }
}

public readonly record struct IrUnionVariantField(
    string? Name,
    IrTypeId Type);

public sealed record IrUnionVariant(
    string Name,
    int TagOrdinal,
    ImmutableArray<IrUnionVariantField> Fields);

public sealed class IrUnionType : IrType {
  internal IrUnionType(
      IrTypeId id,
      string name,
      UnionSymbol? definition,
      ImmutableArray<IrTypeId> typeArguments)
      : base(id, IrTypeKind.Union, name) {
    Definition = definition;
    TypeArguments = typeArguments;
    Variants = [];
  }

  public UnionSymbol? Definition { get; }

  public ImmutableArray<IrTypeId> TypeArguments { get; }

  public ImmutableArray<IrUnionVariant> Variants { get; internal set; }

  internal bool HasDefinedVariants { get; set; }
}

public readonly record struct IrTupleField(
    string Label,
    IrTypeId Type,
    int OffsetBytes = 0);

public sealed class IrTupleType : IrType {
  internal IrTupleType(IrTypeId id, string name, ImmutableArray<IrTupleField> fields)
      : base(id, IrTypeKind.Tuple, name) {
    Fields = fields;
  }

  public ImmutableArray<IrTupleField> Fields { get; internal set; }
}

public readonly record struct IrClosureParam(
    IrTypeId Type,
    IrParamOwnership Ownership);

public sealed class IrClosureType : IrType {
  internal IrClosureType(
      IrTypeId id,
      string name,
      ImmutableArray<IrClosureParam> parameters,
      IrTypeId returnType)
      : base(id, IrTypeKind.Closure, name) {
    Parameters = parameters;
    ReturnType = returnType;
    IsPointerFree = false;
    SizeBytes = 16;
    AlignBytes = 8;
  }

  public ImmutableArray<IrClosureParam> Parameters { get; }

  public IrTypeId ReturnType { get; }
}

public readonly record struct IrInterfaceProperty(
    string Name,
    IrTypeId Type,
    bool IsMutable);

public sealed record IrInterfaceMethod(
    string Name,
    bool IsMutating,
    ImmutableArray<IrClosureParam> Parameters,
    IrTypeId ReturnType);

public sealed class IrInterfaceType : IrType {
  internal IrInterfaceType(
      IrTypeId id,
      string name,
      InterfaceSymbol? definition,
      ImmutableArray<IrTypeId> typeArguments)
      : base(id, IrTypeKind.Interface, name) {
    Definition = definition;
    TypeArguments = typeArguments;
    Properties = [];
    Methods = [];
    IsPointerFree = false;
    SizeBytes = 16;
    AlignBytes = 8;
  }

  public InterfaceSymbol? Definition { get; }

  public ImmutableArray<IrTypeId> TypeArguments { get; }

  public ImmutableArray<IrInterfaceProperty> Properties { get; internal set; }

  public ImmutableArray<IrInterfaceMethod> Methods { get; internal set; }

  internal bool HasDefinedMembers { get; set; }
}

public sealed class IrPtrType : IrType {
  internal IrPtrType(IrTypeId id, string name, IrTypeId targetType)
      : base(id, IrTypeKind.Ptr, name) {
    TargetType = targetType;
    IsPointerFree = false;
    SizeBytes = 8;
    AlignBytes = 8;
  }

  public IrTypeId TargetType { get; }
}

public sealed class IrCellType : IrType {
  internal IrCellType(IrTypeId id, string name, IrTypeId elementType)
      : base(id, IrTypeKind.Cell, name) {
    ElementType = elementType;
    IsPointerFree = false;
    SizeBytes = 8;
    AlignBytes = 8;
  }

  public IrTypeId ElementType { get; }
}

public readonly record struct IrEnvField(
    string Name,
    IrTypeId Type);

public sealed class IrEnvType : IrType {
  internal IrEnvType(
      IrTypeId id,
      string name,
      ImmutableArray<IrEnvField> fields)
      : base(id, IrTypeKind.Env, name) {
    Fields = fields;
    IsPointerFree = false;
    SizeBytes = 8;
    AlignBytes = 8;
  }

  public ImmutableArray<IrEnvField> Fields { get; }
}
