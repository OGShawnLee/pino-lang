using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Pino.Compiler.Checking;

namespace Pino.Compiler.IR;

/// <summary>
/// Interned concrete IR type table supporting <see cref="TypeSymbol"/> lowering, transitive
/// <see cref="IrType.IsPointerFree"/>, Tarjan SCC <see cref="IrType.IsRecursiveUnion"/>
/// classification, 64-bit inline aggregate layout / <see cref="IrType.PassByConstPtr"/>,
/// and <see cref="IrType.IsCycleCapable"/> propagation from <see cref="TypeReachabilityGraph"/>.
/// </summary>
public sealed class IrTypeTable {
  private readonly TypeReachabilityGraph _reachability;
  private readonly List<IrType> _types = [];
  private readonly Dictionary<TypeSymbol, IrTypeId> _symbolToId = new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<IrTypeId, IrTypeId> _handles = [];
  private readonly Dictionary<IrTypeId, IrTypeId> _vectors = [];
  private readonly Dictionary<(IrTypeId Key, IrTypeId Value), IrTypeId> _maps = [];
  private readonly Dictionary<IrTypeId, IrTypeId> _sets = [];
  private readonly Dictionary<IrTypeId, IrTypeId> _pools = [];
  private readonly Dictionary<IrTypeId, IrTypeId> _ptrs = [];
  private readonly Dictionary<IrTypeId, IrTypeId> _cells = [];
  private readonly Dictionary<TupleKey, IrTypeId> _tuples = [];
  private readonly Dictionary<ClosureKey, IrTypeId> _closures = [];
  private readonly Dictionary<EnumSymbol, IrTypeId> _enumsBySymbol = new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<NominalKey<StructSymbol>, IrTypeId> _structsBySymbol = [];
  private readonly Dictionary<NominalKey<UnionSymbol>, IrTypeId> _unionsBySymbol = [];
  private readonly Dictionary<NominalKey<InterfaceSymbol>, IrTypeId> _interfacesBySymbol = [];

  public IrTypeTable(TypeReachabilityGraph? reachability = null) {
    _reachability = reachability ?? TypeReachabilityGraph.Empty;

    Void = AddPrimitive(IrTypeKind.Void, "void", isPointerFree: true, sizeBytes: 0, alignBytes: 1);
    Never = AddPrimitive(IrTypeKind.Never, "never", isPointerFree: true, sizeBytes: 0, alignBytes: 1);
    Bool = AddPrimitive(IrTypeKind.Bool, "bool", isPointerFree: true, sizeBytes: 1, alignBytes: 1);
    Int = AddPrimitive(IrTypeKind.Int, "int", isPointerFree: true, sizeBytes: 8, alignBytes: 8);
    Float = AddPrimitive(IrTypeKind.Float, "float", isPointerFree: true, sizeBytes: 8, alignBytes: 8);
    Rune = AddPrimitive(IrTypeKind.Rune, "rune", isPointerFree: true, sizeBytes: 4, alignBytes: 4);
    String = AddPrimitive(IrTypeKind.String, "string", isPointerFree: false, sizeBytes: 24, alignBytes: 8);
    Regex = AddPrimitive(IrTypeKind.Regex, "regex", isPointerFree: false, sizeBytes: 8, alignBytes: 8);
  }

  public IrTypeId Void { get; }

  public IrTypeId Never { get; }

  public IrTypeId Bool { get; }

  public IrTypeId Int { get; }

  public IrTypeId Float { get; }

  public IrTypeId Rune { get; }

  public IrTypeId String { get; }

  public IrTypeId Regex { get; }

  public int Count => _types.Count;

  public IReadOnlyList<IrType> Types => _types;

  public bool IsValidId(IrTypeId id) => (uint) id.Index < (uint) _types.Count;

  public IrType GetType(IrTypeId id) {
    if (!IsValidId(id)) {
      throw new ArgumentOutOfRangeException(nameof(id), id, "Invalid IrTypeId.");
    }

    return _types[id.Index];
  }

  public bool TryGetType(IrTypeId id, [NotNullWhen(true)] out IrType? type) {
    if (IsValidId(id)) {
      type = _types[id.Index];
      return true;
    }

    type = null;
    return false;
  }

  public IrTypeId Intern(TypeSymbol type) {
    ArgumentNullException.ThrowIfNull(type);
    int initialCount = _types.Count;
    IrTypeId id = InternCore(type, []);
    if (_types.Count != initialCount) {
      RecomputeMetadata();
    }

    return id;
  }

  public IrTypeId GetHandle(IrTypeId elementType) {
    ValidateElementType(elementType);
    if (_handles.TryGetValue(elementType, out IrTypeId existing)) {
      return existing;
    }

    var id = new IrTypeId(_types.Count);
    _types.Add(new IrHandleType(id, $"Handle[{_types[elementType.Index].Name}]", elementType));
    _handles[elementType] = id;
    return id;
  }

  public IrTypeId GetVector(IrTypeId elementType) {
    ValidateElementType(elementType);
    if (_vectors.TryGetValue(elementType, out IrTypeId existing)) {
      return existing;
    }

    var id = new IrTypeId(_types.Count);
    _types.Add(new IrVectorType(id, $"[]{_types[elementType.Index].Name}", elementType));
    _vectors[elementType] = id;
    return id;
  }

  public IrTypeId GetMap(IrTypeId keyType, IrTypeId valueType) {
    ValidateElementType(keyType);
    ValidateElementType(valueType);
    var key = (keyType, valueType);
    if (_maps.TryGetValue(key, out IrTypeId existing)) {
      return existing;
    }

    var id = new IrTypeId(_types.Count);
    string name = $"map[{_types[keyType.Index].Name}, {_types[valueType.Index].Name}]";
    _types.Add(new IrMapType(id, name, keyType, valueType));
    _maps[key] = id;
    return id;
  }

  public IrTypeId GetSet(IrTypeId elementType) {
    ValidateElementType(elementType);
    if (_sets.TryGetValue(elementType, out IrTypeId existing)) {
      return existing;
    }

    var id = new IrTypeId(_types.Count);
    _types.Add(new IrSetType(id, $"set[{_types[elementType.Index].Name}]", elementType));
    _sets[elementType] = id;
    return id;
  }

  public IrTypeId GetPool(IrTypeId elementType) {
    ValidateElementType(elementType);
    if (_pools.TryGetValue(elementType, out IrTypeId existing)) {
      return existing;
    }

    var id = new IrTypeId(_types.Count);
    _types.Add(new IrPoolType(id, $"Pool[{_types[elementType.Index].Name}]", elementType));
    _pools[elementType] = id;
    return id;
  }

  public IrTypeId GetPtr(IrTypeId targetType) {
    ValidateElementType(targetType);
    if (_ptrs.TryGetValue(targetType, out IrTypeId existing)) {
      return existing;
    }

    var id = new IrTypeId(_types.Count);
    _types.Add(new IrPtrType(id, $"ptr<{_types[targetType.Index].Name}>", targetType));
    _ptrs[targetType] = id;
    return id;
  }

  public IrTypeId GetCell(IrTypeId elementType, bool isCycleCapable = false) {
    ValidateElementType(elementType);
    if (_cells.TryGetValue(elementType, out IrTypeId existing)) {
      if (isCycleCapable) {
        _types[existing.Index].IsCycleCapable = true;
      }

      return existing;
    }

    var id = new IrTypeId(_types.Count);
    var cellType = new IrCellType(id, $"cell<{_types[elementType.Index].Name}>", elementType) {
      IsCycleCapable = isCycleCapable,
    };
    _types.Add(cellType);
    _cells[elementType] = id;
    return id;
  }

  public IrTypeId GetTuple(ImmutableArray<IrTupleField> fields) {
    ImmutableArray<IrTupleField> normalized = fields.IsDefault ? [] : fields;
    ImmutableArray<IrTupleField> sorted = normalized.Length <= 1
        ? normalized
        : [.. normalized.OrderBy(static f => f.Label, StringComparer.Ordinal)];

    foreach (IrTupleField field in sorted) {
      ValidateElementType(field.Type);
    }

    var key = new TupleKey(sorted);
    if (_tuples.TryGetValue(key, out IrTypeId existing)) {
      return existing;
    }

    var id = new IrTypeId(_types.Count);
    string name = $"@({string.Join(", ", sorted.Select(f => $"{f.Label} {_types[f.Type.Index].Name}"))})";
    _types.Add(new IrTupleType(id, name, sorted));
    _tuples[key] = id;
    RecomputeMetadata();
    return id;
  }

  public IrTypeId GetClosure(ImmutableArray<IrClosureParam> parameters, IrTypeId returnType) {
    ValidateElementType(returnType);
    ImmutableArray<IrClosureParam> normalized = parameters.IsDefault ? [] : parameters;
    foreach (IrClosureParam param in normalized) {
      ValidateElementType(param.Type);
    }

    var key = new ClosureKey(normalized, returnType);
    if (_closures.TryGetValue(key, out IrTypeId existing)) {
      return existing;
    }

    var id = new IrTypeId(_types.Count);
    string paramList = string.Join(", ", normalized.Select(FormatClosureParam));
    string name = $"fn ({paramList}) -> {_types[returnType.Index].Name}";
    _types.Add(new IrClosureType(id, name, normalized, returnType));
    _closures[key] = id;
    return id;
  }

  public IrTypeId GetEnv(string name, ImmutableArray<IrEnvField> fields, bool isCycleCapable = false) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ImmutableArray<IrEnvField> normalized = fields.IsDefault ? [] : fields;
    foreach (IrEnvField field in normalized) {
      ValidateElementType(field.Type);
    }

    var id = new IrTypeId(_types.Count);
    var envType = new IrEnvType(id, $"env<{name}>", normalized) {
      IsCycleCapable = isCycleCapable,
    };
    _types.Add(envType);
    return id;
  }

  public IrTypeId GetEnum(string name, ImmutableArray<string> memberNames, EnumSymbol? definition = null) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    if (definition is not null && _enumsBySymbol.TryGetValue(definition, out IrTypeId existing)) {
      return existing;
    }

    ImmutableArray<string> normalized = memberNames.IsDefault ? [] : memberNames;
    var id = new IrTypeId(_types.Count);
    _types.Add(new IrEnumType(id, name, normalized, definition));
    if (definition is not null) {
      _enumsBySymbol[definition] = id;
    }

    return id;
  }

  public IrTypeId DeclareStruct(
      string name,
      StructSymbol? definition = null,
      ImmutableArray<IrTypeId> typeArguments = default) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ImmutableArray<IrTypeId> normalizedArgs = typeArguments.IsDefault ? [] : typeArguments;
    var id = new IrTypeId(_types.Count);
    _types.Add(new IrStructType(id, name, definition, normalizedArgs));
    return id;
  }

  public void DefineStructFields(IrTypeId structId, ImmutableArray<IrStructField> fields) {
    if (GetType(structId) is not IrStructType structType) {
      throw new ArgumentException($"Type {structId} is not a struct.", nameof(structId));
    }

    ImmutableArray<IrStructField> normalized = fields.IsDefault ? [] : fields;
    foreach (IrStructField field in normalized) {
      ValidateElementType(field.Type);
    }

    structType.Fields = normalized;
    structType.HasDefinedFields = true;
    RecomputeMetadata();
  }

  public IrTypeId DeclareUnion(
      string name,
      UnionSymbol? definition = null,
      ImmutableArray<IrTypeId> typeArguments = default) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ImmutableArray<IrTypeId> normalizedArgs = typeArguments.IsDefault ? [] : typeArguments;
    var id = new IrTypeId(_types.Count);
    _types.Add(new IrUnionType(id, name, definition, normalizedArgs));
    return id;
  }

  public void DefineUnionVariants(IrTypeId unionId, ImmutableArray<IrUnionVariant> variants) {
    if (GetType(unionId) is not IrUnionType unionType) {
      throw new ArgumentException($"Type {unionId} is not a union.", nameof(unionId));
    }

    ImmutableArray<IrUnionVariant> normalized = variants.IsDefault ? [] : variants;
    foreach (IrUnionVariant variant in normalized) {
      foreach (IrUnionVariantField field in variant.Fields) {
        ValidateElementType(field.Type);
      }
    }

    unionType.Variants = normalized;
    unionType.HasDefinedVariants = true;
    RecomputeMetadata();
  }

  public IrTypeId DeclareInterface(
      string name,
      InterfaceSymbol? definition = null,
      ImmutableArray<IrTypeId> typeArguments = default) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ImmutableArray<IrTypeId> normalizedArgs = typeArguments.IsDefault ? [] : typeArguments;
    var id = new IrTypeId(_types.Count);
    _types.Add(new IrInterfaceType(id, name, definition, normalizedArgs));
    return id;
  }

  public void DefineInterfaceMembers(
      IrTypeId ifaceId,
      ImmutableArray<IrInterfaceProperty> properties,
      ImmutableArray<IrInterfaceMethod> methods) {
    if (GetType(ifaceId) is not IrInterfaceType ifaceType) {
      throw new ArgumentException($"Type {ifaceId} is not an interface.", nameof(ifaceId));
    }

    ImmutableArray<IrInterfaceProperty> normProps = properties.IsDefault ? [] : properties;
    ImmutableArray<IrInterfaceMethod> normMethods = methods.IsDefault ? [] : methods;
    foreach (IrInterfaceProperty prop in normProps) {
      ValidateElementType(prop.Type);
    }

    foreach (IrInterfaceMethod method in normMethods) {
      ValidateElementType(method.ReturnType);
      foreach (IrClosureParam param in method.Parameters) {
        ValidateElementType(param.Type);
      }
    }

    ifaceType.Properties = normProps;
    ifaceType.Methods = normMethods;
    ifaceType.HasDefinedMembers = true;
  }

  private IrTypeId InternCore(TypeSymbol type, ImmutableArray<(TypeParameterSymbol Param, IrTypeId Arg)> subst) {
    if (subst.IsEmpty && _symbolToId.TryGetValue(type, out IrTypeId cached)) {
      return cached;
    }

    if (type.IsError || type.ContainsError) {
      throw new ArgumentException($"Cannot lower error type '{type.DisplayName}' to IR.", nameof(type));
    }

    switch (type) {
      case TypeParameterType tp:
        for (int i = 0; i < subst.Length; i++) {
          if (ReferenceEquals(subst[i].Param, tp.Symbol)) {
            return subst[i].Arg;
          }
        }
        throw new ArgumentException($"Cannot lower unresolved type '{type.DisplayName}' to IR.", nameof(type));

      case TypeVariableType:
      case PendingReturnType:
        throw new ArgumentException($"Cannot lower unresolved type '{type.DisplayName}' to IR.", nameof(type));

      case PrimitiveType p:
        return p.Kind switch {
          PrimitiveKind.Bool => Bool,
          PrimitiveKind.Int => Int,
          PrimitiveKind.Float => Float,
          PrimitiveKind.Rune => Rune,
          PrimitiveKind.String => String,
          PrimitiveKind.Regex => Regex,
          _ => throw new ArgumentOutOfRangeException(nameof(type), p.Kind, "Unknown primitive kind."),
        };

      case VoidType:
        return Void;

      case NeverType:
        return Never;

      case EnumType e: {
          IrTypeId id = GetEnum(e.Definition.Name, [.. e.Definition.Members.Select(static m => m.Name)], e.Definition);
          if (subst.IsEmpty) {
            _symbolToId[type] = id;
          }
          return id;
        }

      case HandleType h: {
          IrTypeId id = GetHandle(InternCore(h.ElementType, subst));
          if (subst.IsEmpty) {
            _symbolToId[type] = id;
          }
          return id;
        }

      case VectorType v: {
          IrTypeId id = GetVector(InternCore(v.ElementType, subst));
          if (subst.IsEmpty) {
            _symbolToId[type] = id;
          }
          return id;
        }

      case MapType m: {
          IrTypeId id = GetMap(InternCore(m.KeyType, subst), InternCore(m.ValueType, subst));
          if (subst.IsEmpty) {
            _symbolToId[type] = id;
          }
          return id;
        }

      case SetType s: {
          IrTypeId id = GetSet(InternCore(s.ElementType, subst));
          if (subst.IsEmpty) {
            _symbolToId[type] = id;
          }
          return id;
        }

      case PoolType p: {
          IrTypeId id = GetPool(InternCore(p.ElementType, subst));
          if (subst.IsEmpty) {
            _symbolToId[type] = id;
          }
          return id;
        }

      case TupleType t: {
          var fields = ImmutableArray.CreateBuilder<IrTupleField>(t.Fields.Length);
          foreach (Checking.TupleField f in t.Fields) {
            fields.Add(new IrTupleField(f.Label, InternCore(f.Type, subst)));
          }

          IrTypeId id = GetTuple(fields.MoveToImmutable());
          if (subst.IsEmpty) {
            _symbolToId[type] = id;
          }
          return id;
        }

      case FunctionType fn: {
          var parameters = ImmutableArray.CreateBuilder<IrClosureParam>(fn.Parameters.Length);
          foreach (FunctionParameter p in fn.Parameters) {
            IrTypeId pType = InternCore(p.Type, subst);
            if (p.IsMutable) {
              parameters.Add(new IrClosureParam(GetPtr(pType), IrParamOwnership.Mut));
            } else {
              parameters.Add(new IrClosureParam(pType, IrParamOwnership.Owned));
            }
          }

          IrTypeId ret = InternCore(fn.ReturnType, subst);
          IrTypeId id = GetClosure(parameters.MoveToImmutable(), ret);
          if (subst.IsEmpty) {
            _types[id.Index].IsCycleCapable = _reachability.IsCycleCapable(type);
            _symbolToId[type] = id;
          }
          return id;
        }

      case StructType st:
        return InternStruct(st, subst);

      case UnionType ut:
        return InternUnion(ut, subst);

      case InterfaceType it:
        return InternInterface(it, subst);

      default:
        throw new ArgumentOutOfRangeException(nameof(type), type.GetType().Name, "Unsupported TypeSymbol kind.");
    }
  }

  private IrTypeId InternStruct(StructType structType, ImmutableArray<(TypeParameterSymbol Param, IrTypeId Arg)> outerSubst) {
    var tArgs = ImmutableArray.CreateBuilder<IrTypeId>(structType.TypeArguments.Length);
    foreach (TypeSymbol arg in structType.TypeArguments) {
      tArgs.Add(InternCore(arg, outerSubst));
    }

    ImmutableArray<IrTypeId> normalizedArgs = tArgs.MoveToImmutable();
    var key = new NominalKey<StructSymbol>(structType.Definition, normalizedArgs);
    if (_structsBySymbol.TryGetValue(key, out IrTypeId existing)) {
      if (outerSubst.IsEmpty) {
        _symbolToId[structType] = existing;
      }
      return existing;
    }

    string name = FormatNominalName(structType.Definition.Name, normalizedArgs);
    IrTypeId id = DeclareStruct(name, structType.Definition, normalizedArgs);
    if (outerSubst.IsEmpty) {
      _types[id.Index].IsCycleCapable = _reachability.IsCycleCapable(structType);
      _symbolToId[structType] = id;
    }
    _structsBySymbol[key] = id;

    ImmutableArray<(TypeParameterSymbol Param, IrTypeId Arg)> innerSubst =
        BuildSubst(structType.Definition.TypeParameters, normalizedArgs);
    ImmutableArray<FieldSymbol> sourceFields = structType.Definition.AllFields.IsEmpty
        ? structType.Definition.Fields
        : structType.Definition.AllFields;
    var irFields = ImmutableArray.CreateBuilder<IrStructField>(sourceFields.Length);
    foreach (FieldSymbol field in sourceFields) {
      irFields.Add(new IrStructField(field.Name, InternCore(field.Type, innerSubst), field.IsMutable));
    }

    var irStruct = (IrStructType) _types[id.Index];
    irStruct.Fields = irFields.MoveToImmutable();
    irStruct.HasDefinedFields = true;
    return id;
  }

  private IrTypeId InternUnion(UnionType unionType, ImmutableArray<(TypeParameterSymbol Param, IrTypeId Arg)> outerSubst) {
    var tArgs = ImmutableArray.CreateBuilder<IrTypeId>(unionType.TypeArguments.Length);
    foreach (TypeSymbol arg in unionType.TypeArguments) {
      tArgs.Add(InternCore(arg, outerSubst));
    }

    ImmutableArray<IrTypeId> normalizedArgs = tArgs.MoveToImmutable();
    var key = new NominalKey<UnionSymbol>(unionType.Definition, normalizedArgs);
    if (_unionsBySymbol.TryGetValue(key, out IrTypeId existing)) {
      if (outerSubst.IsEmpty) {
        _symbolToId[unionType] = existing;
      }
      return existing;
    }

    string name = FormatNominalName(unionType.Definition.Name, normalizedArgs);
    IrTypeId id = DeclareUnion(name, unionType.Definition, normalizedArgs);
    if (outerSubst.IsEmpty) {
      _types[id.Index].IsCycleCapable = _reachability.IsCycleCapable(unionType);
      _symbolToId[unionType] = id;
    }
    _unionsBySymbol[key] = id;

    ImmutableArray<(TypeParameterSymbol Param, IrTypeId Arg)> innerSubst =
        BuildSubst(unionType.Definition.TypeParameters, normalizedArgs);
    var variants = ImmutableArray.CreateBuilder<IrUnionVariant>(unionType.Definition.Variants.Length);
    foreach (UnionVariantSymbol variant in unionType.Definition.Variants) {
      var fields = ImmutableArray.CreateBuilder<IrUnionVariantField>(variant.Fields.Length);
      foreach (UnionVariantField field in variant.Fields) {
        fields.Add(new IrUnionVariantField(field.Name, InternCore(field.Type, innerSubst)));
      }

      variants.Add(new IrUnionVariant(variant.Name, variant.TagOrdinal, fields.MoveToImmutable()));
    }

    var irUnion = (IrUnionType) _types[id.Index];
    irUnion.Variants = variants.MoveToImmutable();
    irUnion.HasDefinedVariants = true;
    return id;
  }

  private IrTypeId InternInterface(InterfaceType ifaceType, ImmutableArray<(TypeParameterSymbol Param, IrTypeId Arg)> outerSubst) {
    var tArgs = ImmutableArray.CreateBuilder<IrTypeId>(ifaceType.TypeArguments.Length);
    foreach (TypeSymbol arg in ifaceType.TypeArguments) {
      tArgs.Add(InternCore(arg, outerSubst));
    }

    ImmutableArray<IrTypeId> normalizedArgs = tArgs.MoveToImmutable();
    var key = new NominalKey<InterfaceSymbol>(ifaceType.Definition, normalizedArgs);
    if (_interfacesBySymbol.TryGetValue(key, out IrTypeId existing)) {
      if (outerSubst.IsEmpty) {
        _symbolToId[ifaceType] = existing;
      }
      return existing;
    }

    string name = FormatNominalName(ifaceType.Definition.Name, normalizedArgs);
    IrTypeId id = DeclareInterface(name, ifaceType.Definition, normalizedArgs);
    if (outerSubst.IsEmpty) {
      _types[id.Index].IsCycleCapable = _reachability.IsCycleCapable(ifaceType);
      _symbolToId[ifaceType] = id;
    }
    _interfacesBySymbol[key] = id;

    ImmutableArray<(TypeParameterSymbol Param, IrTypeId Arg)> innerSubst =
        BuildSubst(ifaceType.Definition.TypeParameters, normalizedArgs);
    var props = ImmutableArray.CreateBuilder<IrInterfaceProperty>(ifaceType.Definition.Properties.Length);
    foreach (InterfaceProperty prop in ifaceType.Definition.Properties) {
      props.Add(new IrInterfaceProperty(prop.Name, InternCore(prop.Type, innerSubst), prop.IsMutable));
    }

    var methods = ImmutableArray.CreateBuilder<IrInterfaceMethod>(ifaceType.Definition.Methods.Length);
    foreach (MethodSymbol method in ifaceType.Definition.Methods) {
      var mParams = ImmutableArray.CreateBuilder<IrClosureParam>(method.Parameters.Length);
      foreach (ParameterSymbol param in method.Parameters) {
        IrTypeId paramId = InternCore(param.Type, innerSubst);
        mParams.Add(param.IsMutable
            ? new IrClosureParam(GetPtr(paramId), IrParamOwnership.Mut)
            : new IrClosureParam(paramId, IrParamOwnership.Owned));
      }

      methods.Add(new IrInterfaceMethod(
          method.Name,
          method.IsMutating,
          mParams.MoveToImmutable(),
          InternCore(method.ReturnType, innerSubst)));
    }

    var irIface = (IrInterfaceType) _types[id.Index];
    irIface.Properties = props.MoveToImmutable();
    irIface.Methods = methods.MoveToImmutable();
    irIface.HasDefinedMembers = true;
    return id;
  }

  private string FormatNominalName(string baseName, ImmutableArray<IrTypeId> args) =>
      args.IsEmpty
          ? baseName
          : $"{baseName}[{string.Join(", ", args.Select(a => _types[a.Index].Name))}]";

  private static ImmutableArray<(TypeParameterSymbol Param, IrTypeId Arg)> BuildSubst(
      ImmutableArray<TypeParameterSymbol> parameters,
      ImmutableArray<IrTypeId> arguments) {
    if (parameters.IsEmpty || arguments.IsEmpty) {
      return [];
    }

    int count = Math.Min(parameters.Length, arguments.Length);
    var builder = ImmutableArray.CreateBuilder<(TypeParameterSymbol, IrTypeId)>(count);
    for (int i = 0; i < count; i++) {
      builder.Add((parameters[i], arguments[i]));
    }

    return builder.MoveToImmutable();
  }

  /// <summary>
  /// Recomputes Tarjan SCC recursive-union flags, transitive <see cref="IrType.IsPointerFree"/>,
  /// and 64-bit inline aggregate byte sizes/offsets across the table.
  /// </summary>
  public void RecomputeMetadata() {
    int n = _types.Count;
    if (n == 0) {
      return;
    }

    // 1. Build inline value-containment adjacency (structs, unions, tuples).
    // Pointer-indirect types (collections, handles, closures, interfaces, ptr, cell, env) break value layout cycles.
    var edges = new List<int>[n];
    for (int i = 0; i < n; i++) {
      edges[i] = [];
      switch (_types[i]) {
        case IrStructType st:
          foreach (IrStructField f in st.Fields) {
            edges[i].Add(f.Type.Index);
          }
          break;

        case IrUnionType ut:
          foreach (IrUnionVariant v in ut.Variants) {
            foreach (IrUnionVariantField f in v.Fields) {
              edges[i].Add(f.Type.Index);
            }
          }
          break;

        case IrTupleType tt:
          foreach (IrTupleField f in tt.Fields) {
            edges[i].Add(f.Type.Index);
          }
          break;
      }
    }

    // 2. Iterative Tarjan SCC to detect recursive unions (UNION-11).
    int[] index = new int[n];
    int[] lowlink = new int[n];
    bool[] onStack = new bool[n];
    int[] sccId = new int[n];
    Array.Fill(index, -1);
    Array.Fill(sccId, -1);

    var tarjanStack = new Stack<int>();
    var callStack = new Stack<(int Node, int EdgeIdx)>();
    var sccs = new List<List<int>>();
    int nextIndex = 0;

    for (int start = 0; start < n; start++) {
      if (index[start] != -1) {
        continue;
      }

      index[start] = lowlink[start] = nextIndex++;
      onStack[start] = true;
      tarjanStack.Push(start);
      callStack.Push((start, 0));

      while (callStack.Count > 0) {
        (int v, int edgeIdx) = callStack.Pop();
        List<int> adj = edges[v];
        if (edgeIdx < adj.Count) {
          callStack.Push((v, edgeIdx + 1));
          int w = adj[edgeIdx];
          if (index[w] == -1) {
            index[w] = lowlink[w] = nextIndex++;
            onStack[w] = true;
            tarjanStack.Push(w);
            callStack.Push((w, 0));
          } else if (onStack[w] && index[w] < lowlink[v]) {
            lowlink[v] = index[w];
          }
        } else {
          if (lowlink[v] == index[v]) {
            int id = sccs.Count;
            var members = new List<int>();
            while (true) {
              int w = tarjanStack.Pop();
              onStack[w] = false;
              sccId[w] = id;
              members.Add(w);
              if (w == v) {
                break;
              }
            }
            sccs.Add(members);
          }

          if (callStack.Count > 0) {
            int parent = callStack.Peek().Node;
            if (lowlink[v] < lowlink[parent]) {
              lowlink[parent] = lowlink[v];
            }
          }
        }
      }
    }

    bool[] sccIsCyclic = new bool[sccs.Count];
    for (int s = 0; s < sccs.Count; s++) {
      List<int> members = sccs[s];
      if (members.Count > 1) {
        sccIsCyclic[s] = true;
      } else if (members.Count == 1) {
        int single = members[0];
        if (edges[single].Contains(single)) {
          sccIsCyclic[s] = true;
        }
      }
    }

    for (int i = 0; i < n; i++) {
      if (_types[i] is IrUnionType ut) {
        ut.IsRecursiveUnion = sccIsCyclic[sccId[i]];
      }
    }

    // 3. Transitive IsPointerFree across the value-containment DAG/graph.
    // Tarjan produces SCCs in reverse topological order (sinks first), so we evaluate SCCs 0..sccs.Count-1.
    bool[] sccPointerFree = new bool[sccs.Count];
    for (int s = 0; s < sccs.Count; s++) {
      if (sccIsCyclic[s]) {
        sccPointerFree[s] = false;
        continue;
      }

      int node = sccs[s][0];
      IrType irType = _types[node];
      bool pf = irType.Kind switch {
        IrTypeKind.Void or IrTypeKind.Never or IrTypeKind.Bool or IrTypeKind.Int or IrTypeKind.Float or IrTypeKind.Rune or IrTypeKind.Enum or IrTypeKind.Handle => true,
        IrTypeKind.Struct or IrTypeKind.Union or IrTypeKind.Tuple => AreAllTargetsPointerFree(edges[node], sccId, sccPointerFree),
        _ => false,
      };
      sccPointerFree[s] = pf;
      irType.IsPointerFree = pf;
    }

    // Also update cyclic nodes' IsPointerFree to false explicitly.
    for (int i = 0; i < n; i++) {
      if (sccIsCyclic[sccId[i]]) {
        _types[i].IsPointerFree = false;
      }
    }

    // 4. Compute 64-bit C ABI layout sizes & alignments in reverse topological order.
    // Recursive unions break value layout cycles with an 8-byte heap-boxed pointer.
    var layoutState = new byte[n]; // 0 = unvisited, 1 = visiting, 2 = done
    for (int i = 0; i < n; i++) {
      ComputeLayout(i, layoutState);
    }
  }

  private void ComputeLayout(int typeIndex, byte[] state) {
    if (state[typeIndex] == 2) {
      return;
    }

    if (state[typeIndex] == 1) {
      // Direct struct-only cycle without a union/collection; guard against stack overflow.
      _types[typeIndex].SizeBytes = 0;
      _types[typeIndex].AlignBytes = 1;
      return;
    }

    IrType irType = _types[typeIndex];
    switch (irType) {
      case IrUnionType ut when ut.IsRecursiveUnion:
        ut.SizeBytes = 8;
        ut.AlignBytes = 8;
        state[typeIndex] = 2;
        return;

      case IrStructType st: {
          state[typeIndex] = 1;
          int offset = 0;
          int maxAlign = 1;
          var updatedFields = ImmutableArray.CreateBuilder<IrStructField>(st.Fields.Length);
          foreach (IrStructField field in st.Fields) {
            ComputeLayout(field.Type.Index, state);
            IrType fType = _types[field.Type.Index];
            int fAlign = Math.Max(1, fType.AlignBytes);
            offset = AlignUp(offset, fAlign);
            updatedFields.Add(field with { OffsetBytes = offset });
            offset += fType.SizeBytes;
            if (fAlign > maxAlign) {
              maxAlign = fAlign;
            }
          }

          st.Fields = updatedFields.MoveToImmutable();
          st.AlignBytes = maxAlign;
          st.SizeBytes = st.Fields.Length == 0 ? 0 : AlignUp(offset, maxAlign);
          state[typeIndex] = 2;
          return;
        }

      case IrTupleType tt: {
          state[typeIndex] = 1;
          int offset = 0;
          int maxAlign = 1;
          var updatedFields = ImmutableArray.CreateBuilder<IrTupleField>(tt.Fields.Length);
          foreach (IrTupleField field in tt.Fields) {
            ComputeLayout(field.Type.Index, state);
            IrType fType = _types[field.Type.Index];
            int fAlign = Math.Max(1, fType.AlignBytes);
            offset = AlignUp(offset, fAlign);
            updatedFields.Add(field with { OffsetBytes = offset });
            offset += fType.SizeBytes;
            if (fAlign > maxAlign) {
              maxAlign = fAlign;
            }
          }

          tt.Fields = updatedFields.MoveToImmutable();
          tt.AlignBytes = maxAlign;
          tt.SizeBytes = tt.Fields.Length == 0 ? 0 : AlignUp(offset, maxAlign);
          state[typeIndex] = 2;
          return;
        }

      case IrUnionType ut: {
          state[typeIndex] = 1;
          int maxVariantSize = 0;
          int maxVariantAlign = 1;
          foreach (IrUnionVariant variant in ut.Variants) {
            int vOffset = 0;
            int vMaxAlign = 1;
            foreach (IrUnionVariantField field in variant.Fields) {
              ComputeLayout(field.Type.Index, state);
              IrType fType = _types[field.Type.Index];
              int fAlign = Math.Max(1, fType.AlignBytes);
              vOffset = AlignUp(vOffset, fAlign);
              vOffset += fType.SizeBytes;
              if (fAlign > vMaxAlign) {
                vMaxAlign = fAlign;
              }
            }

            int vSize = variant.Fields.Length == 0 ? 0 : AlignUp(vOffset, vMaxAlign);
            if (vSize > maxVariantSize) {
              maxVariantSize = vSize;
            }

            if (vMaxAlign > maxVariantAlign) {
              maxVariantAlign = vMaxAlign;
            }
          }

          if (maxVariantSize == 0) {
            ut.AlignBytes = 4;
            ut.SizeBytes = 4;
          } else {
            int totalAlign = Math.Max(4, maxVariantAlign);
            int payloadOffset = AlignUp(4, maxVariantAlign);
            ut.AlignBytes = totalAlign;
            ut.SizeBytes = AlignUp(payloadOffset + maxVariantSize, totalAlign);
          }

          state[typeIndex] = 2;
          return;
        }

      default:
        state[typeIndex] = 2;
        return;
    }
  }

  private static bool AreAllTargetsPointerFree(List<int> targets, int[] sccId, bool[] sccPointerFree) {
    foreach (int target in targets) {
      if (!sccPointerFree[sccId[target]]) {
        return false;
      }
    }

    return true;
  }

  private static int AlignUp(int value, int align) {
    if (align <= 1) {
      return value;
    }

    int rem = value % align;
    return rem == 0 ? value : value + (align - rem);
  }

  private IrTypeId AddPrimitive(
      IrTypeKind kind,
      string name,
      bool isPointerFree,
      int sizeBytes,
      int alignBytes) {
    var id = new IrTypeId(_types.Count);
    _types.Add(new IrPrimitiveType(id, kind, name, isPointerFree, sizeBytes, alignBytes));
    return id;
  }

  private void ValidateElementType(IrTypeId id) {
    if (!IsValidId(id)) {
      throw new ArgumentOutOfRangeException(nameof(id), id, "Referenced IrTypeId is not in this IrTypeTable.");
    }
  }

  private string FormatClosureParam(IrClosureParam param) {
    string typeName = _types[param.Type.Index].Name;
    return param.Ownership == IrParamOwnership.Mut ? $"@mut {typeName}" : typeName;
  }

  private readonly struct TupleKey(ImmutableArray<IrTupleField> fields) : IEquatable<TupleKey> {
    public ImmutableArray<IrTupleField> Fields { get; } = fields;

    public bool Equals(TupleKey other) {
      if (Fields.Length != other.Fields.Length) {
        return false;
      }

      for (int i = 0; i < Fields.Length; i++) {
        if (!string.Equals(Fields[i].Label, other.Fields[i].Label, StringComparison.Ordinal)
            || Fields[i].Type != other.Fields[i].Type) {
          return false;
        }
      }

      return true;
    }

    public override bool Equals(object? obj) => obj is TupleKey other && Equals(other);

    public override int GetHashCode() {
      var hc = new HashCode();
      hc.Add(Fields.Length);
      foreach (IrTupleField f in Fields) {
        hc.Add(f.Label, StringComparer.Ordinal);
        hc.Add(f.Type.Index);
      }

      return hc.ToHashCode();
    }
  }

  private readonly struct ClosureKey(
      ImmutableArray<IrClosureParam> parameters,
      IrTypeId returnType) : IEquatable<ClosureKey> {
    public ImmutableArray<IrClosureParam> Parameters { get; } = parameters;

    public IrTypeId ReturnType { get; } = returnType;

    public bool Equals(ClosureKey other) {
      if (ReturnType != other.ReturnType || Parameters.Length != other.Parameters.Length) {
        return false;
      }

      for (int i = 0; i < Parameters.Length; i++) {
        if (Parameters[i] != other.Parameters[i]) {
          return false;
        }
      }

      return true;
    }

    public override bool Equals(object? obj) => obj is ClosureKey other && Equals(other);

    public override int GetHashCode() {
      var hc = new HashCode();
      hc.Add(ReturnType.Index);
      hc.Add(Parameters.Length);
      foreach (IrClosureParam p in Parameters) {
        hc.Add(p);
      }

      return hc.ToHashCode();
    }
  }

  private readonly struct NominalKey<TDef>(
      TDef definition,
      ImmutableArray<IrTypeId> typeArguments) : IEquatable<NominalKey<TDef>>
      where TDef : class {
    public TDef Definition { get; } = definition;

    public ImmutableArray<IrTypeId> TypeArguments { get; } = typeArguments;

    public bool Equals(NominalKey<TDef> other) {
      if (!ReferenceEquals(Definition, other.Definition)
          || TypeArguments.Length != other.TypeArguments.Length) {
        return false;
      }

      for (int i = 0; i < TypeArguments.Length; i++) {
        if (TypeArguments[i] != other.TypeArguments[i]) {
          return false;
        }
      }

      return true;
    }

    public override bool Equals(object? obj) => obj is NominalKey<TDef> other && Equals(other);

    public override int GetHashCode() {
      var hc = new HashCode();
      hc.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Definition));
      hc.Add(TypeArguments.Length);
      foreach (IrTypeId arg in TypeArguments) {
        hc.Add(arg.Index);
      }

      return hc.ToHashCode();
    }
  }
}
