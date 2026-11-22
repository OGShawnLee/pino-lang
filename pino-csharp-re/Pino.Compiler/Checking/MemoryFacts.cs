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

/// <summary>
/// Inferred module-state purity facts for a function or method (D-183).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReadsModuleState"/> is <c>true</c> when the callable reads a top-level <c>var</c> or
/// <c>val mut</c> binding of any module, directly or transitively through a call.
/// <see cref="WritesModuleState"/> is <c>true</c> when it reassigns or mutates one, directly or
/// transitively through a call. Both facts are computed as a least fixed point over the call
/// graph's strongly connected components.
/// </para>
/// <para>
/// Calls through function values (lambdas, function-typed parameters or locals, callable struct
/// fields, indexed function elements, or interface methods whose concrete implementation is not
/// statically fixed) are treated conservatively as unknown and mark both
/// <see cref="ReadsModuleState"/> and <see cref="WritesModuleState"/> <c>true</c> for the caller.
/// The checker does not perform points-to or closure-flow analysis, so an arbitrary function value
/// or dynamic interface receiver could read or mutate module-level state in any module.
/// </para>
/// <para>
/// A closure body's own effects count for the function that calls it, not the function that
/// declares it.
/// </para>
/// </remarks>
public readonly record struct ModuleStateEffects(
    bool ReadsModuleState,
    bool WritesModuleState) {
  public static ModuleStateEffects Pure => new(false, false);

  public static ModuleStateEffects ReadOnly => new(true, false);

  public static ModuleStateEffects WriteOnly => new(false, true);

  public static ModuleStateEffects ReadWrite => new(true, true);

  public bool IsPure => !ReadsModuleState && !WritesModuleState;

  public ModuleStateEffects Union(ModuleStateEffects other) =>
      new(ReadsModuleState || other.ReadsModuleState, WritesModuleState || other.WritesModuleState);
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
  /// Under AD-003 value semantics (D-177), structs, non-recursive unions, and tuples whose
  /// transitive fields are all pointer-free are themselves pointer-free (IQ-11).
  /// </summary>
  public bool IsPointerFree(TypeSymbol type) {
    ArgumentNullException.ThrowIfNull(type);
    var visiting = new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
    var memo = new Dictionary<TypeSymbol, bool>(ReferenceEqualityComparer.Instance);
    return IsPointerFreeCore(type, visiting, memo);
  }

  private bool IsPointerFreeCore(
      TypeSymbol type,
      HashSet<TypeSymbol> visiting,
      Dictionary<TypeSymbol, bool> memo) {
    if (memo.TryGetValue(type, out bool cached)) {
      return cached;
    }

    bool result = type switch {
      PrimitiveType p => IsScalarPrimitive(p.Kind),
      EnumType => true,
      HandleType => true,
      VoidType => true,
      NeverType => true,
      TupleType tuple => AllTupleFieldsPointerFree(tuple, visiting, memo),
      StructType structType => IsStructPointerFree(structType, visiting, memo),
      UnionType unionType => IsUnionPointerFree(unionType, visiting, memo),
      _ => false,
    };

    memo[type] = result;
    return result;
  }

  private bool IsStructPointerFree(
      StructType structType,
      HashSet<TypeSymbol> visiting,
      Dictionary<TypeSymbol, bool> memo) {
    if (!visiting.Add(structType)) {
      return false;
    }

    try {
      if (_adjacency.TryGetValue(structType, out ImmutableArray<TypeReachabilityEdge> edges)) {
        foreach (TypeReachabilityEdge edge in edges) {
          if (!IsPointerFreeCore(edge.Target, visiting, memo)) {
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
        if (!IsPointerFreeCore(resolved, visiting, memo)) {
          return false;
        }
      }
      return true;
    } finally {
      visiting.Remove(structType);
    }
  }

  private bool IsUnionPointerFree(
      UnionType unionType,
      HashSet<TypeSymbol> visiting,
      Dictionary<TypeSymbol, bool> memo) {
    if (!visiting.Add(unionType)) {
      return false;
    }

    try {
      if (_adjacency.TryGetValue(unionType, out ImmutableArray<TypeReachabilityEdge> edges)) {
        foreach (TypeReachabilityEdge edge in edges) {
          if (!IsPointerFreeCore(edge.Target, visiting, memo)) {
            return false;
          }
        }
        return true;
      }

      foreach (UnionVariantSymbol variant in unionType.Definition.Variants) {
        foreach (UnionVariantField field in variant.Fields) {
          TypeSymbol resolved = Substitute(field.Type, unionType.Definition.TypeParameters, unionType.TypeArguments);
          if (!IsPointerFreeCore(resolved, visiting, memo)) {
            return false;
          }
        }
      }
      return true;
    } finally {
      visiting.Remove(unionType);
    }
  }

  private bool AllTupleFieldsPointerFree(
      TupleType tuple,
      HashSet<TypeSymbol> visiting,
      Dictionary<TypeSymbol, bool> memo) {
    if (!visiting.Add(tuple)) {
      return false;
    }

    try {
      foreach (TupleField field in tuple.Fields) {
        if (!IsPointerFreeCore(field.Type, visiting, memo)) {
          return false;
        }
      }
      return true;
    } finally {
      visiting.Remove(tuple);
    }
  }

  private static bool IsScalarPrimitive(PrimitiveKind kind) =>
      kind is PrimitiveKind.Bool or PrimitiveKind.Int or PrimitiveKind.Float or PrimitiveKind.Rune;

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
