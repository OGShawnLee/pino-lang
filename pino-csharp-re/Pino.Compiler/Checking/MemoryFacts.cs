using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Pino.Compiler.Checking;

public sealed record CaptureEntry(
    Symbol Symbol,
    bool IsMutable,
    bool IsMutatedByClosure);

public sealed record CaptureList(ImmutableArray<CaptureEntry> Entries) {
  public static CaptureList Empty { get; } = new([]);

  public bool IsEmpty => Entries.IsDefaultOrEmpty;
}

/// <summary>
/// Tracks which local and parameter <see cref="Symbol"/>s are ever assigned after
/// initialization (AD-002).
/// </summary>
public sealed class AssignedBindings {
  public static AssignedBindings Empty { get; } = new(FrozenSet<Symbol>.Empty);

  private readonly FrozenSet<Symbol> _assigned;

  public AssignedBindings(IEnumerable<Symbol> assignedSymbols) {
    ArgumentNullException.ThrowIfNull(assignedSymbols);
    _assigned = assignedSymbols.ToFrozenSet<Symbol>(ReferenceEqualityComparer.Instance);
  }

  private AssignedBindings(FrozenSet<Symbol> assigned) => _assigned = assigned;

  public int Count => _assigned.Count;

  public FrozenSet<Symbol> Symbols => _assigned;

  public bool IsAssignedAfterInit(Symbol symbol) {
    ArgumentNullException.ThrowIfNull(symbol);
    return _assigned.Contains(symbol);
  }
}

public readonly record struct TypeReachabilityEdge(
    TypeSymbol Target,
    bool IsMutableEdge);

/// <summary>
/// Directed type-reachability graph recording which types can reference which other types
/// and whether each edge is mutable, supporting AD-002 cycle-capability and pointer-free queries.
/// </summary>
public sealed class TypeReachabilityGraph {
  public static TypeReachabilityGraph Empty { get; } = new(
      FrozenDictionary<TypeSymbol, ImmutableArray<TypeReachabilityEdge>>.Empty,
      FrozenSet<TypeSymbol>.Empty);

  private readonly FrozenDictionary<TypeSymbol, ImmutableArray<TypeReachabilityEdge>> _adjacency;
  private readonly FrozenSet<TypeSymbol> _cycleCapableTypes;

  private TypeReachabilityGraph(
      FrozenDictionary<TypeSymbol, ImmutableArray<TypeReachabilityEdge>> adjacency,
      FrozenSet<TypeSymbol> cycleCapableTypes) {
    _adjacency = adjacency;
    _cycleCapableTypes = cycleCapableTypes;
  }

  public IReadOnlyDictionary<TypeSymbol, ImmutableArray<TypeReachabilityEdge>> Edges => _adjacency;

  public ImmutableArray<TypeReachabilityEdge> GetEdges(TypeSymbol type) {
    ArgumentNullException.ThrowIfNull(type);
    return _adjacency.TryGetValue(type, out ImmutableArray<TypeReachabilityEdge> edges)
        ? edges
        : [];
  }

  public bool IsCycleCapable(TypeSymbol type) {
    ArgumentNullException.ThrowIfNull(type);
    return _cycleCapableTypes.Contains(type);
  }

  /// <summary>
  /// The type's values hold no references the memory manager must trace or count.
  /// Under AD-003 value semantics (D-177), structs and unions of inline scalars are pointer-free.
  /// </summary>
  public bool IsPointerFree(TypeSymbol type) {
    ArgumentNullException.ThrowIfNull(type);

    return type switch {
      PrimitiveType p => IsScalarPrimitive(p.Kind),
      EnumType => true,
      VoidType => true,
      NeverType => true,
      TupleType tuple => AllTupleFieldsInlineScalar(tuple),
      StructType structType => IsStructPointerFree(structType),
      UnionType unionType => IsUnionPointerFree(unionType),
      _ => false,
    };
  }

  private bool IsStructPointerFree(StructType structType) {
    if (_adjacency.TryGetValue(structType, out ImmutableArray<TypeReachabilityEdge> edges)) {
      foreach (TypeReachabilityEdge edge in edges) {
        if (!IsInlineScalar(edge.Target)) {
          return false;
        }
      }
      return true;
    }

    ImmutableArray<FieldSymbol> fields = structType.Definition.AllFields.IsEmpty
        ? structType.Definition.Fields
        : structType.Definition.AllFields;
    foreach (FieldSymbol field in fields) {
      TypeSymbol resolved = Substitute(field.Type, structType.Definition.TypeParameters, structType.TypeArguments);
      if (!IsInlineScalar(resolved)) {
        return false;
      }
    }
    return true;
  }

  private bool IsUnionPointerFree(UnionType unionType) {
    if (_adjacency.TryGetValue(unionType, out ImmutableArray<TypeReachabilityEdge> edges)) {
      foreach (TypeReachabilityEdge edge in edges) {
        if (!IsInlineScalar(edge.Target)) {
          return false;
        }
      }
      return true;
    }

    foreach (UnionVariantSymbol variant in unionType.Definition.Variants) {
      foreach (UnionVariantField field in variant.Fields) {
        TypeSymbol resolved = Substitute(field.Type, unionType.Definition.TypeParameters, unionType.TypeArguments);
        if (!IsInlineScalar(resolved)) {
          return false;
        }
      }
    }
    return true;
  }

  private static bool AllTupleFieldsInlineScalar(TupleType tuple) {
    foreach (TupleField field in tuple.Fields) {
      if (!IsInlineScalar(field.Type)) {
        return false;
      }
    }
    return true;
  }

  private static bool IsScalarPrimitive(PrimitiveKind kind) =>
      kind is PrimitiveKind.Bool or PrimitiveKind.Int or PrimitiveKind.Float or PrimitiveKind.Rune;

  private static bool IsInlineScalar(TypeSymbol type) =>
      type switch {
        PrimitiveType p => IsScalarPrimitive(p.Kind),
        EnumType => true,
        VoidType => true,
        TupleType tuple => AllTupleFieldsInlineScalar(tuple),
        _ => false,
      };

  private static TypeSymbol Substitute(
      TypeSymbol type,
      ImmutableArray<TypeParameterSymbol> parameters,
      ImmutableArray<TypeSymbol> arguments) {
    if (parameters.IsEmpty || arguments.IsEmpty || type is not TypeParameterType tp) {
      return type;
    }

    int count = Math.Min(parameters.Length, arguments.Length);
    for (int i = 0; i < count; i++) {
      if (ReferenceEquals(parameters[i], tp.Symbol)) {
        return arguments[i];
      }
    }

    return type;
  }

  public sealed class Builder {
    private readonly Dictionary<TypeSymbol, Dictionary<TypeSymbol, bool>> _edges =
        new(ReferenceEqualityComparer.Instance);

    public void AddNode(TypeSymbol type) {
      ArgumentNullException.ThrowIfNull(type);
      if (!_edges.ContainsKey(type)) {
        _edges.Add(type, new Dictionary<TypeSymbol, bool>(ReferenceEqualityComparer.Instance));
      }
    }

    public void AddEdge(TypeSymbol source, TypeSymbol target, bool isMutableEdge) {
      ArgumentNullException.ThrowIfNull(source);
      ArgumentNullException.ThrowIfNull(target);
      AddNode(source);
      AddNode(target);

      Dictionary<TypeSymbol, bool> targets = _edges[source];
      if (targets.TryGetValue(target, out bool existingMutable)) {
        if (isMutableEdge && !existingMutable) {
          targets[target] = true;
        }
      } else {
        targets.Add(target, isMutableEdge);
      }
    }

    public TypeReachabilityGraph Build() {
      if (_edges.Count == 0) {
        return Empty;
      }

      var adjacency = new Dictionary<TypeSymbol, ImmutableArray<TypeReachabilityEdge>>(
          _edges.Count,
          ReferenceEqualityComparer.Instance);
      foreach ((TypeSymbol source, Dictionary<TypeSymbol, bool> targets) in _edges) {
        if (targets.Count == 0) {
          adjacency.Add(source, []);
        } else {
          var builder = ImmutableArray.CreateBuilder<TypeReachabilityEdge>(targets.Count);
          foreach ((TypeSymbol target, bool isMutable) in targets) {
            builder.Add(new TypeReachabilityEdge(target, isMutable));
          }
          adjacency.Add(source, builder.MoveToImmutable());
        }
      }

      FrozenSet<TypeSymbol> cycleCapable = ComputeCycleCapableTypes(adjacency);
      return new TypeReachabilityGraph(
          adjacency.ToFrozenDictionary(ReferenceEqualityComparer.Instance),
          cycleCapable);
    }

    private static FrozenSet<TypeSymbol> ComputeCycleCapableTypes(
        Dictionary<TypeSymbol, ImmutableArray<TypeReachabilityEdge>> adjacency) {
      var nodes = adjacency.Keys.ToArray();
      var nodeIndex = new Dictionary<TypeSymbol, int>(nodes.Length, ReferenceEqualityComparer.Instance);
      for (int i = 0; i < nodes.Length; i++) {
        nodeIndex.Add(nodes[i], i);
      }

      int n = nodes.Length;
      int[] index = new int[n];
      int[] lowlink = new int[n];
      bool[] onStack = new bool[n];
      int[] sccId = new int[n];
      Array.Fill(index, -1);
      Array.Fill(sccId, -1);

      var tarjanStack = new Stack<int>();
      var callStack = new Stack<(int Node, int EdgeIndex)>();
      int nextIndex = 0;
      int nextScc = 0;

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
          ImmutableArray<TypeReachabilityEdge> edges = adjacency[nodes[v]];

          if (edgeIdx < edges.Length) {
            callStack.Push((v, edgeIdx + 1));
            int w = nodeIndex[edges[edgeIdx].Target];
            if (index[w] == -1) {
              index[w] = lowlink[w] = nextIndex++;
              onStack[w] = true;
              tarjanStack.Push(w);
              callStack.Push((w, 0));
            } else if (onStack[w]) {
              if (index[w] < lowlink[v]) {
                lowlink[v] = index[w];
              }
            }
          } else {
            if (lowlink[v] == index[v]) {
              int id = nextScc++;
              while (true) {
                int w = tarjanStack.Pop();
                onStack[w] = false;
                sccId[w] = id;
                if (w == v) {
                  break;
                }
              }
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

      bool[] sccHasMutableCycle = new bool[nextScc];
      for (int u = 0; u < n; u++) {
        int uScc = sccId[u];
        foreach (TypeReachabilityEdge edge in adjacency[nodes[u]]) {
          if (!edge.IsMutableEdge) {
            continue;
          }

          int v = nodeIndex[edge.Target];
          if (sccId[v] == uScc) {
            sccHasMutableCycle[uScc] = true;
            break;
          }
        }
      }

      var result = new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
      for (int i = 0; i < n; i++) {
        if (sccHasMutableCycle[sccId[i]]) {
          result.Add(nodes[i]);
        }
      }

      return result.ToFrozenSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
    }
  }
}
