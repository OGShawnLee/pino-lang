using System.Collections.Immutable;

using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

public enum PrimitiveKind : byte {
  Bool,
  Int,
  Float,
  Rune,
  String,
  Regex,
}

public abstract class TypeSymbol {
  private protected TypeSymbol() { }

  public abstract string DisplayName { get; }

  public virtual bool IsError => false;

  public virtual bool IsNever => false;

  public virtual bool IsVoid => false;

  public bool ContainsError { get; protected init; }

  public override string ToString() => DisplayName;
}

public sealed class PrimitiveType : TypeSymbol {
  public static PrimitiveType Bool { get; } = new(PrimitiveKind.Bool);

  public static PrimitiveType Int { get; } = new(PrimitiveKind.Int);

  public static PrimitiveType Float { get; } = new(PrimitiveKind.Float);

  public static PrimitiveType Rune { get; } = new(PrimitiveKind.Rune);

  public static PrimitiveType String { get; } = new(PrimitiveKind.String);

  public static PrimitiveType Regex { get; } = new(PrimitiveKind.Regex);

  private PrimitiveType(PrimitiveKind kind) => Kind = kind;

  public PrimitiveKind Kind { get; }

  public bool IsInteger => Kind == PrimitiveKind.Int;

  public bool IsFloat => Kind == PrimitiveKind.Float;

  public bool IsNumeric => IsInteger || IsFloat;

  public override string DisplayName => Kind switch {
    PrimitiveKind.Bool => "bool",
    PrimitiveKind.Int => "int",
    PrimitiveKind.Float => "float",
    PrimitiveKind.Rune => "rune",
    PrimitiveKind.String => "string",
    PrimitiveKind.Regex => "regex",
    _ => "error",
  };

  public static PrimitiveType FromKind(PrimitiveKind kind) => kind switch {
    PrimitiveKind.Bool => Bool,
    PrimitiveKind.Int => Int,
    PrimitiveKind.Float => Float,
    PrimitiveKind.Rune => Rune,
    PrimitiveKind.String => String,
    PrimitiveKind.Regex => Regex,
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown primitive kind."),
  };
}

public sealed class VoidType : TypeSymbol {
  public static VoidType Instance { get; } = new();

  private VoidType() { }

  public override bool IsVoid => true;

  public override string DisplayName => "void";
}

public sealed class NeverType : TypeSymbol {
  public static NeverType Instance { get; } = new();

  private NeverType() { }

  public override bool IsNever => true;

  public override string DisplayName => "never";
}

public sealed class ErrorType : TypeSymbol {
  public static ErrorType Instance { get; } = new();

  private ErrorType() => ContainsError = true;

  public override bool IsError => true;

  public override string DisplayName => "<error>";
}

public sealed class VectorType : TypeSymbol {
  internal VectorType(TypeSymbol elementType) {
    ElementType = elementType;
    ContainsError = elementType.ContainsError;
  }

  public TypeSymbol ElementType { get; }

  public override string DisplayName => $"[]{ElementType.DisplayName}";
}

public sealed class MapType : TypeSymbol {
  internal MapType(TypeSymbol keyType, TypeSymbol valueType) {
    KeyType = keyType;
    ValueType = valueType;
    ContainsError = keyType.ContainsError || valueType.ContainsError;
  }

  public TypeSymbol KeyType { get; }

  public TypeSymbol ValueType { get; }

  public override string DisplayName => $"map[{KeyType.DisplayName}, {ValueType.DisplayName}]";
}

public sealed class SetType : TypeSymbol {
  internal SetType(TypeSymbol elementType) {
    ElementType = elementType;
    ContainsError = elementType.ContainsError;
  }

  public TypeSymbol ElementType { get; }

  public override string DisplayName => $"set[{ElementType.DisplayName}]";
}

public sealed class PoolType : TypeSymbol {
  internal PoolType(TypeSymbol elementType) {
    ElementType = elementType;
    ContainsError = elementType.ContainsError;
  }

  public TypeSymbol ElementType { get; }

  public override string DisplayName => $"Pool[{ElementType.DisplayName}]";
}

public sealed class HandleType : TypeSymbol {
  internal HandleType(TypeSymbol elementType) {
    ElementType = elementType;
    ContainsError = elementType.ContainsError;
  }

  public TypeSymbol ElementType { get; }

  public override string DisplayName => $"Handle[{ElementType.DisplayName}]";
}

public readonly record struct TupleField(string Label, TypeSymbol Type);

public sealed class TupleType : TypeSymbol {
  internal TupleType(ImmutableArray<TupleField> canonicalFields) {
    Fields = canonicalFields;
    ContainsError = canonicalFields.Any(static f => f.Type.ContainsError);
  }

  /// <summary>Fields sorted canonically by <see cref="TupleField.Label"/> (Ordinal) for order-independent equality.</summary>
  public ImmutableArray<TupleField> Fields { get; }

  public override string DisplayName => FormatFields(Fields);

  public static string FormatFields(ImmutableArray<TupleField> fields) =>
      $"@({string.Join(", ", fields.Select(static f => $"{f.Label} {f.Type.DisplayName}"))})";
}

public readonly record struct FunctionParameter(TypeSymbol Type, bool IsMutable);

public sealed class FunctionType : TypeSymbol {
  internal FunctionType(ImmutableArray<FunctionParameter> parameters, TypeSymbol returnType) {
    Parameters = parameters;
    ReturnType = returnType;
    ContainsError = returnType.ContainsError || parameters.Any(static p => p.Type.ContainsError);
  }

  public ImmutableArray<FunctionParameter> Parameters { get; }

  public TypeSymbol ReturnType { get; }

  public override string DisplayName =>
      $"fn ({string.Join(", ", Parameters.Select(static p => p.IsMutable ? $"mut {p.Type.DisplayName}" : p.Type.DisplayName))}) -> {ReturnType.DisplayName}";
}

public sealed class StructType : TypeSymbol {
  internal StructType(StructSymbol definition, ImmutableArray<TypeSymbol> typeArguments) {
    Definition = definition;
    TypeArguments = typeArguments;
    ContainsError = typeArguments.Any(static a => a.ContainsError);
  }

  public StructSymbol Definition { get; }

  public ImmutableArray<TypeSymbol> TypeArguments { get; }

  public override string DisplayName => TypeArguments.IsEmpty
      ? Definition.Name
      : $"{Definition.Name}[{string.Join(", ", TypeArguments.Select(static a => a.DisplayName))}]";
}

public sealed class EnumType : TypeSymbol {
  internal EnumType(EnumSymbol definition) => Definition = definition;

  public EnumSymbol Definition { get; }

  public override string DisplayName => Definition.Name;
}

public sealed class UnionType : TypeSymbol {
  internal UnionType(UnionSymbol definition, ImmutableArray<TypeSymbol> typeArguments) {
    Definition = definition;
    TypeArguments = typeArguments;
    ContainsError = typeArguments.Any(static a => a.ContainsError);
  }

  public UnionSymbol Definition { get; }

  public ImmutableArray<TypeSymbol> TypeArguments { get; }

  public override string DisplayName => TypeArguments.IsEmpty
      ? Definition.Name
      : $"{Definition.Name}[{string.Join(", ", TypeArguments.Select(static a => a.DisplayName))}]";
}

public sealed class InterfaceType : TypeSymbol {
  internal InterfaceType(InterfaceSymbol definition, ImmutableArray<TypeSymbol> typeArguments) {
    Definition = definition;
    TypeArguments = typeArguments;
    ContainsError = typeArguments.Any(static a => a.ContainsError);
  }

  public InterfaceSymbol Definition { get; }

  public ImmutableArray<TypeSymbol> TypeArguments { get; }

  public override string DisplayName => TypeArguments.IsEmpty
      ? Definition.Name
      : $"{Definition.Name}[{string.Join(", ", TypeArguments.Select(static a => a.DisplayName))}]";
}

public sealed class TypeParameterType : TypeSymbol {
  internal TypeParameterType(TypeParameterSymbol symbol) => Symbol = symbol;

  public TypeParameterSymbol Symbol { get; }

  public override string DisplayName => Symbol.Name;
}

internal sealed class TypeVariableType : TypeSymbol {
  public TypeVariableType(int id, SourceSpan originSpan) {
    Id = id;
    OriginSpan = originSpan;
  }

  public int Id { get; }

  public SourceSpan OriginSpan { get; }

  public override string DisplayName => $"?T{Id}";
}

internal sealed class PendingReturnType : TypeSymbol {
  public PendingReturnType(Symbol callableSymbol) => CallableSymbol = callableSymbol;

  public Symbol CallableSymbol { get; }

  public override string DisplayName => $"<pending:{CallableSymbol.Name}>";
}
