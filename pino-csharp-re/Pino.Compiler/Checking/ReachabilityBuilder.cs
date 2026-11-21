using System.Collections.Immutable;

namespace Pino.Compiler.Checking;

/// <summary>
/// Builds the AD-002 <see cref="TypeReachabilityGraph"/> across all nominal and resolved
/// types in a compilation using verified mutability rules from <c>spec/</c>.
/// </summary>
internal static class ReachabilityBuilder {
  public static TypeReachabilityGraph Build(
      IReadOnlyList<ModuleCheckState> modules,
      PreludeSymbols prelude,
      TypeInterner types) {
    var builder = new TypeReachabilityGraph.Builder();
    var visited = new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
    var worklist = new Queue<TypeSymbol>();

    // Seed prelude nominal types.
    EnqueueUnion(prelude.IOErrorUnion, types, worklist);
    EnqueueUnion(prelude.OptionUnion, types, worklist);
    EnqueueUnion(prelude.ResultUnion, types, worklist);

    // Seed all module nominal types and resolved Phase 2 types.
    foreach (ModuleCheckState state in modules) {
      foreach ((_, StructSymbol structSym, _, _) in state.Structs) {
        EnqueueStruct(structSym, types, worklist);
      }

      foreach ((_, UnionSymbol unionSym, _) in state.Unions) {
        EnqueueUnion(unionSym, types, worklist);
      }

      foreach ((_, EnumSymbol enumSym, _) in state.Enums) {
        worklist.Enqueue(types.GetEnum(enumSym));
      }

      foreach ((_, InterfaceSymbol ifaceSym, _) in state.Interfaces) {
        EnqueueInterface(ifaceSym, types, worklist);
      }

      foreach (TypeSymbol resolved in state.ResolvedTypes) {
        worklist.Enqueue(resolved);
      }
    }

    while (worklist.Count > 0) {
      TypeSymbol type = worklist.Dequeue();
      if (type.IsError || !visited.Add(type)) {
        continue;
      }

      builder.AddNode(type);

      switch (type) {
        case StructType structType: {
            // STRUCT-4 / STRUCT-7: struct fields (including promoted embedded fields) are mutable.
            ImmutableArray<TypeParameterSymbol> tParams = structType.Definition.TypeParameters;
            ImmutableArray<TypeSymbol> tArgs = structType.TypeArguments;
            foreach (FieldSymbol field in structType.Definition.AllFields) {
              TypeSymbol fieldType = TypeSubstitution.Substitute(field.Type, tParams, tArgs, types);
              if (!fieldType.IsError) {
                builder.AddEdge(structType, fieldType, isMutableEdge: true);
                worklist.Enqueue(fieldType);
              }
            }
            break;
          }

        case VectorType vecType:
          // VEC-4 / VECLIB-5: vector elements are mutable via indexing and push/add.
          if (!vecType.ElementType.IsError) {
            builder.AddEdge(vecType, vecType.ElementType, isMutableEdge: true);
            worklist.Enqueue(vecType.ElementType);
          }
          break;

        case MapType mapType:
          // MAP-5: map entries (keys and values) are mutable.
          if (!mapType.KeyType.IsError) {
            builder.AddEdge(mapType, mapType.KeyType, isMutableEdge: true);
            worklist.Enqueue(mapType.KeyType);
          }
          if (!mapType.ValueType.IsError) {
            builder.AddEdge(mapType, mapType.ValueType, isMutableEdge: true);
            worklist.Enqueue(mapType.ValueType);
          }
          break;

        case SetType setType:
          // SETLIB-3 / SETLIB-4 / SETLIB-9: set elements are mutable via add/remove.
          if (!setType.ElementType.IsError) {
            builder.AddEdge(setType, setType.ElementType, isMutableEdge: true);
            worklist.Enqueue(setType.ElementType);
          }
          break;

        case UnionType unionType: {
            // Conservative: spec/features/unions.md states no explicit rule that payloads are immutable.
            ImmutableArray<TypeParameterSymbol> tParams = unionType.Definition.TypeParameters;
            ImmutableArray<TypeSymbol> tArgs = unionType.TypeArguments;
            foreach (UnionVariantSymbol variant in unionType.Definition.Variants) {
              foreach (UnionVariantField field in variant.Fields) {
                TypeSymbol payloadType = TypeSubstitution.Substitute(field.Type, tParams, tArgs, types);
                if (!payloadType.IsError) {
                  builder.AddEdge(unionType, payloadType, isMutableEdge: true);
                  worklist.Enqueue(payloadType);
                }
              }
            }
            break;
          }

        case TupleType tupleType:
          // TUP-1: tuples are return-only immutable packs.
          foreach (TupleField field in tupleType.Fields) {
            if (!field.Type.IsError) {
              builder.AddEdge(tupleType, field.Type, isMutableEdge: false);
              worklist.Enqueue(field.Type);
            }
          }
          break;

        case InterfaceType ifaceType: {
            // Conservative heap edge for opaque satisfying structs (IFACE-2..4, STRUCT-7).
            ImmutableArray<TypeParameterSymbol> tParams = ifaceType.Definition.TypeParameters;
            ImmutableArray<TypeSymbol> tArgs = ifaceType.TypeArguments;
            foreach (InterfaceProperty prop in ifaceType.Definition.Properties) {
              TypeSymbol propType = TypeSubstitution.Substitute(prop.Type, tParams, tArgs, types);
              if (!propType.IsError) {
                builder.AddEdge(ifaceType, propType, isMutableEdge: true);
                worklist.Enqueue(propType);
              }
            }
            foreach (MethodSymbol method in ifaceType.Definition.Methods) {
              foreach (ParameterSymbol param in method.Parameters) {
                TypeSymbol paramType = TypeSubstitution.Substitute(param.Type, tParams, tArgs, types);
                if (!paramType.IsError) {
                  builder.AddEdge(ifaceType, paramType, isMutableEdge: true);
                  worklist.Enqueue(paramType);
                }
              }
              TypeSymbol retType = TypeSubstitution.Substitute(method.ReturnType, tParams, tArgs, types);
              if (!retType.IsError) {
                builder.AddEdge(ifaceType, retType, isMutableEdge: true);
                worklist.Enqueue(retType);
              }
            }
            break;
          }

        case FunctionType fnType:
          // Conservative heap edge for closures capturing mutable vars (FN-21, D-056).
          foreach (TypeSymbol paramType in fnType.ParameterTypes) {
            if (!paramType.IsError) {
              builder.AddEdge(fnType, paramType, isMutableEdge: true);
              worklist.Enqueue(paramType);
            }
          }
          if (!fnType.ReturnType.IsError) {
            builder.AddEdge(fnType, fnType.ReturnType, isMutableEdge: true);
            worklist.Enqueue(fnType.ReturnType);
          }
          break;
      }
    }

    return builder.Build();
  }

  private static void EnqueueStruct(StructSymbol sym, TypeInterner types, Queue<TypeSymbol> worklist) {
    ImmutableArray<TypeSymbol> args = sym.TypeParameters.IsEmpty
        ? []
        : [.. sym.TypeParameters.Select(static tp => (TypeSymbol) tp.Type)];
    worklist.Enqueue(types.GetStruct(sym, args));
  }

  private static void EnqueueUnion(UnionSymbol sym, TypeInterner types, Queue<TypeSymbol> worklist) {
    ImmutableArray<TypeSymbol> args = sym.TypeParameters.IsEmpty
        ? []
        : [.. sym.TypeParameters.Select(static tp => (TypeSymbol) tp.Type)];
    worklist.Enqueue(types.GetUnion(sym, args));
  }

  private static void EnqueueInterface(InterfaceSymbol sym, TypeInterner types, Queue<TypeSymbol> worklist) {
    ImmutableArray<TypeSymbol> args = sym.TypeParameters.IsEmpty
        ? []
        : [.. sym.TypeParameters.Select(static tp => (TypeSymbol) tp.Type)];
    worklist.Enqueue(types.GetInterface(sym, args));
  }
}
