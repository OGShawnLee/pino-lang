using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Pino.Compiler.Checking;

/// <summary>
/// Per-<see cref="Compilation"/> type interner ensuring structurally identical composite and
/// constructed types intern to the exact same <see cref="TypeSymbol"/> instance.
/// </summary>
public sealed class TypeInterner {
  private readonly Lock _gate = new();
  private readonly Dictionary<TypeSymbol, VectorType> _vectors = new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<(TypeSymbol Key, TypeSymbol Value), MapType> _maps = new(PairKeyComparer.Instance);
  private readonly Dictionary<TypeSymbol, SetType> _sets = new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<TypeSymbol, PoolType> _pools = new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<TypeSymbol, HandleType> _handles = new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<TupleKey, TupleType> _tuples = [];
  private readonly Dictionary<FunctionKey, FunctionType> _functions = [];
  private readonly Dictionary<NominalKey<StructSymbol>, StructType> _structs = [];
  private readonly Dictionary<EnumSymbol, EnumType> _enums = new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<NominalKey<UnionSymbol>, UnionType> _unions = [];
  private readonly Dictionary<NominalKey<InterfaceSymbol>, InterfaceType> _interfaces = [];

  public PrimitiveType Bool => PrimitiveType.Bool;

  public PrimitiveType Int => PrimitiveType.Int;

  public PrimitiveType Float => PrimitiveType.Float;

  public PrimitiveType Rune => PrimitiveType.Rune;

  public PrimitiveType String => PrimitiveType.String;

  public PrimitiveType Regex => PrimitiveType.Regex;

  public VoidType Void => VoidType.Instance;

  public NeverType Never => NeverType.Instance;

  public ErrorType Error => ErrorType.Instance;

  public PrimitiveType FromPrimitiveKind(PrimitiveKind kind) => PrimitiveType.FromKind(kind);

  public VectorType GetVector(TypeSymbol elementType) {
    ArgumentNullException.ThrowIfNull(elementType);
    lock (_gate) {
      if (!_vectors.TryGetValue(elementType, out VectorType? existing)) {
        existing = new VectorType(elementType);
        _vectors[elementType] = existing;
      }
      return existing;
    }
  }

  public MapType GetMap(TypeSymbol keyType, TypeSymbol valueType) {
    ArgumentNullException.ThrowIfNull(keyType);
    ArgumentNullException.ThrowIfNull(valueType);
    var key = (keyType, valueType);
    lock (_gate) {
      if (!_maps.TryGetValue(key, out MapType? existing)) {
        existing = new MapType(keyType, valueType);
        _maps[key] = existing;
      }
      return existing;
    }
  }

  public SetType GetSet(TypeSymbol elementType) {
    ArgumentNullException.ThrowIfNull(elementType);
    lock (_gate) {
      if (!_sets.TryGetValue(elementType, out SetType? existing)) {
        existing = new SetType(elementType);
        _sets[elementType] = existing;
      }
      return existing;
    }
  }

  public PoolType GetPool(TypeSymbol elementType) {
    ArgumentNullException.ThrowIfNull(elementType);
    lock (_gate) {
      if (!_pools.TryGetValue(elementType, out PoolType? existing)) {
        existing = new PoolType(elementType);
        _pools[elementType] = existing;
      }
      return existing;
    }
  }

  public HandleType GetHandle(TypeSymbol elementType) {
    ArgumentNullException.ThrowIfNull(elementType);
    lock (_gate) {
      if (!_handles.TryGetValue(elementType, out HandleType? existing)) {
        existing = new HandleType(elementType);
        _handles[elementType] = existing;
      }
      return existing;
    }
  }

  public TupleType GetTuple(ImmutableArray<TupleField> fields) {
    ImmutableArray<TupleField> normalized = fields.IsDefault ? [] : fields;
    ImmutableArray<TupleField> sorted = normalized.Length <= 1
        ? normalized
        : [.. normalized.OrderBy(static f => f.Label, StringComparer.Ordinal)];

    var key = new TupleKey(sorted);
    lock (_gate) {
      if (!_tuples.TryGetValue(key, out TupleType? existing)) {
        existing = new TupleType(sorted);
        _tuples[key] = existing;
      }
      return existing;
    }
  }

  public FunctionType GetFunction(ImmutableArray<FunctionParameter> parameters, TypeSymbol returnType) {
    ArgumentNullException.ThrowIfNull(returnType);
    ImmutableArray<FunctionParameter> normalizedParams = parameters.IsDefault ? [] : parameters;
    var key = new FunctionKey(normalizedParams, returnType);
    lock (_gate) {
      if (!_functions.TryGetValue(key, out FunctionType? existing)) {
        existing = new FunctionType(normalizedParams, returnType);
        _functions[key] = existing;
      }
      return existing;
    }
  }

  public StructType GetStruct(StructSymbol definition, ImmutableArray<TypeSymbol> typeArguments = default) {
    ArgumentNullException.ThrowIfNull(definition);
    ImmutableArray<TypeSymbol> normalizedArgs = typeArguments.IsDefault ? [] : typeArguments;
    var key = new NominalKey<StructSymbol>(definition, normalizedArgs);
    lock (_gate) {
      if (!_structs.TryGetValue(key, out StructType? existing)) {
        existing = new StructType(definition, normalizedArgs);
        _structs[key] = existing;
      }
      return existing;
    }
  }

  public EnumType GetEnum(EnumSymbol definition) {
    ArgumentNullException.ThrowIfNull(definition);
    lock (_gate) {
      if (!_enums.TryGetValue(definition, out EnumType? existing)) {
        existing = new EnumType(definition);
        _enums[definition] = existing;
      }
      return existing;
    }
  }

  public UnionType GetUnion(UnionSymbol definition, ImmutableArray<TypeSymbol> typeArguments = default) {
    ArgumentNullException.ThrowIfNull(definition);
    ImmutableArray<TypeSymbol> normalizedArgs = typeArguments.IsDefault ? [] : typeArguments;
    var key = new NominalKey<UnionSymbol>(definition, normalizedArgs);
    lock (_gate) {
      if (!_unions.TryGetValue(key, out UnionType? existing)) {
        existing = new UnionType(definition, normalizedArgs);
        _unions[key] = existing;
      }
      return existing;
    }
  }

  public InterfaceType GetInterface(InterfaceSymbol definition, ImmutableArray<TypeSymbol> typeArguments = default) {
    ArgumentNullException.ThrowIfNull(definition);
    ImmutableArray<TypeSymbol> normalizedArgs = typeArguments.IsDefault ? [] : typeArguments;
    var key = new NominalKey<InterfaceSymbol>(definition, normalizedArgs);
    lock (_gate) {
      if (!_interfaces.TryGetValue(key, out InterfaceType? existing)) {
        existing = new InterfaceType(definition, normalizedArgs);
        _interfaces[key] = existing;
      }
      return existing;
    }
  }

  public TypeParameterType GetTypeParameter(TypeParameterSymbol symbol) {
    ArgumentNullException.ThrowIfNull(symbol);
    return symbol.Type;
  }

  private sealed class PairKeyComparer : IEqualityComparer<(TypeSymbol Key, TypeSymbol Value)> {
    public static PairKeyComparer Instance { get; } = new();

    public bool Equals((TypeSymbol Key, TypeSymbol Value) x, (TypeSymbol Key, TypeSymbol Value) y) =>
        ReferenceEquals(x.Key, y.Key) && ReferenceEquals(x.Value, y.Value);

    public int GetHashCode((TypeSymbol Key, TypeSymbol Value) obj) =>
        HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Key), RuntimeHelpers.GetHashCode(obj.Value));
  }

  private readonly struct TupleKey(ImmutableArray<TupleField> fields) : IEquatable<TupleKey> {
    public ImmutableArray<TupleField> Fields { get; } = fields;

    public bool Equals(TupleKey other) {
      if (Fields.Length != other.Fields.Length) {
        return false;
      }

      for (int i = 0; i < Fields.Length; i++) {
        if (!string.Equals(Fields[i].Label, other.Fields[i].Label, StringComparison.Ordinal)
            || !ReferenceEquals(Fields[i].Type, other.Fields[i].Type)) {
          return false;
        }
      }

      return true;
    }

    public override bool Equals(object? obj) => obj is TupleKey other && Equals(other);

    public override int GetHashCode() {
      var hc = new HashCode();
      hc.Add(Fields.Length);
      foreach (TupleField f in Fields) {
        hc.Add(f.Label, StringComparer.Ordinal);
        hc.Add(RuntimeHelpers.GetHashCode(f.Type));
      }
      return hc.ToHashCode();
    }
  }

  private readonly struct FunctionKey(
      ImmutableArray<FunctionParameter> parameters,
      TypeSymbol returnType) : IEquatable<FunctionKey> {
    public ImmutableArray<FunctionParameter> Parameters { get; } = parameters;
    public TypeSymbol ReturnType { get; } = returnType;

    public bool Equals(FunctionKey other) {
      if (!ReferenceEquals(ReturnType, other.ReturnType)
          || Parameters.Length != other.Parameters.Length) {
        return false;
      }

      for (int i = 0; i < Parameters.Length; i++) {
        if (Parameters[i].IsMutable != other.Parameters[i].IsMutable
            || !ReferenceEquals(Parameters[i].Type, other.Parameters[i].Type)) {
          return false;
        }
      }

      return true;
    }

    public override bool Equals(object? obj) => obj is FunctionKey other && Equals(other);

    public override int GetHashCode() {
      var hc = new HashCode();
      hc.Add(RuntimeHelpers.GetHashCode(ReturnType));
      hc.Add(Parameters.Length);
      foreach (FunctionParameter p in Parameters) {
        hc.Add(p.IsMutable);
        hc.Add(RuntimeHelpers.GetHashCode(p.Type));
      }
      return hc.ToHashCode();
    }
  }

  private readonly struct NominalKey<TDefinition>(
      TDefinition definition,
      ImmutableArray<TypeSymbol> typeArguments) : IEquatable<NominalKey<TDefinition>>
      where TDefinition : class {
    public TDefinition Definition { get; } = definition;
    public ImmutableArray<TypeSymbol> TypeArguments { get; } = typeArguments;

    public bool Equals(NominalKey<TDefinition> other) {
      if (!ReferenceEquals(Definition, other.Definition)
          || TypeArguments.Length != other.TypeArguments.Length) {
        return false;
      }

      for (int i = 0; i < TypeArguments.Length; i++) {
        if (!ReferenceEquals(TypeArguments[i], other.TypeArguments[i])) {
          return false;
        }
      }

      return true;
    }

    public override bool Equals(object? obj) => obj is NominalKey<TDefinition> other && Equals(other);

    public override int GetHashCode() {
      var hc = new HashCode();
      hc.Add(RuntimeHelpers.GetHashCode(Definition));
      hc.Add(TypeArguments.Length);
      foreach (TypeSymbol arg in TypeArguments) {
        hc.Add(RuntimeHelpers.GetHashCode(arg));
      }
      return hc.ToHashCode();
    }
  }
}
