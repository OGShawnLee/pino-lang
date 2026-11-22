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
      TypeInterner types,
      ImmutableArray<GenericInstantiation> genericInstantiations = default) {
    var builder = new TypeReachabilityGraph.Builder();
    var visited = new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
    var worklist = new Queue<TypeSymbol>();

    // Seed prelude nominal types.
    EnqueueUnion(prelude.IOErrorUnion, types, worklist);
    EnqueueUnion(prelude.OptionUnion, types, worklist);
    EnqueueUnion(prelude.ResultUnion, types, worklist);

    // Seed all concrete generic instantiations reached across the compilation (AD-002 / ticket 032 §6).
    if (!genericInstantiations.IsDefaultOrEmpty) {
      foreach (GenericInstantiation inst in genericInstantiations) {
        switch (inst.Definition) {
          case StructSymbol s:
            worklist.Enqueue(types.GetStruct(s, inst.TypeArguments));
            break;

          case UnionSymbol u:
            worklist.Enqueue(types.GetUnion(u, inst.TypeArguments));
            break;

          case InterfaceSymbol i:
            worklist.Enqueue(types.GetInterface(i, inst.TypeArguments));
            break;
        }

        foreach (TypeSymbol arg in inst.TypeArguments) {
          worklist.Enqueue(arg);
        }
      }
    }

    // Seed all module nominal types, resolved Phase 2 types, Phase 3 expression types, and closure environments.
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

      for (int nodeId = 0; nodeId < state.NodeTypes.Length; nodeId++) {
        if (state.NodeTypes[nodeId] is { } nodeType
            && !nodeType.IsError
            && nodeType is not (TypeVariableType or PendingReturnType)) {
          worklist.Enqueue(nodeType);
        }
      }

      // AD-002 / ticket 032 §6: register closure environment edges from every recorded CaptureList.
      // Captured mutable `var`s are boxed on the heap (D-056 / FN-21), so their edges are mutable;
      // immutable `val` / unmutated parameter captures produce immutable edges.
      for (int nodeId = 0; nodeId < state.CapturesByCallableId.Length; nodeId++) {
        CaptureList? captures = state.CapturesByCallableId[nodeId];
        if (captures is null || captures.IsEmpty) {
          continue;
        }

        FunctionType? callableFnType = ResolveCallableFunctionType(state, nodeId, types);
        if (callableFnType is null || callableFnType.ContainsError) {
          continue;
        }

        worklist.Enqueue(callableFnType);
        foreach (CaptureEntry entry in captures.Entries) {
          TypeSymbol? capturedType = entry.Symbol switch {
            LocalSymbol local => local.Type,
            ParameterSymbol param => param.Type,
            _ => null,
          };

          if (capturedType is null
              || capturedType.IsError
              || capturedType is TypeVariableType or PendingReturnType) {
            continue;
          }

          bool isMutableEdge = entry.IsMutable || entry.IsMutatedByClosure;
          builder.AddEdge(callableFnType, capturedType, isMutableEdge);
          worklist.Enqueue(capturedType);
        }
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
            // AD-003 / D-177: value semantics with CoW; pure struct containment edges cannot form reference cycles.
            ImmutableArray<TypeParameterSymbol> tParams = structType.Definition.TypeParameters;
            ImmutableArray<TypeSymbol> tArgs = structType.TypeArguments;
            foreach (FieldSymbol field in structType.Definition.AllFields) {
              TypeSymbol fieldType = TypeSubstitution.Substitute(field.Type, tParams, tArgs, types);
              if (!fieldType.IsError) {
                builder.AddEdge(structType, fieldType, isMutableEdge: false);
                worklist.Enqueue(fieldType);
              }
            }
            break;
          }

        case VectorType vecType:
          // AD-003 / D-177 / VEC-19: value semantics with CoW; vectors cannot form reference cycles.
          if (!vecType.ElementType.IsError) {
            builder.AddEdge(vecType, vecType.ElementType, isMutableEdge: false);
            worklist.Enqueue(vecType.ElementType);
          }
          break;

        case MapType mapType:
          // AD-003 / D-177 / MAP-13: value semantics with CoW; maps cannot form reference cycles.
          if (!mapType.KeyType.IsError) {
            builder.AddEdge(mapType, mapType.KeyType, isMutableEdge: false);
            worklist.Enqueue(mapType.KeyType);
          }
          if (!mapType.ValueType.IsError) {
            builder.AddEdge(mapType, mapType.ValueType, isMutableEdge: false);
            worklist.Enqueue(mapType.ValueType);
          }
          break;

        case SetType setType:
          // AD-003 / D-177 / SET-7: value semantics with CoW; sets cannot form reference cycles.
          if (!setType.ElementType.IsError) {
            builder.AddEdge(setType, setType.ElementType, isMutableEdge: false);
            worklist.Enqueue(setType.ElementType);
          }
          break;

        case PoolType poolType:
          // AD-003 / D-177 / D-185: value semantics with CoW; pools cannot form reference cycles.
          if (!poolType.ElementType.IsError) {
            builder.AddEdge(poolType, poolType.ElementType, isMutableEdge: false);
            worklist.Enqueue(poolType.ElementType);
          }
          break;

        case UnionType unionType: {
            // UNION-24 / D-182: union payloads are deeply immutable inductive values.
            ImmutableArray<TypeParameterSymbol> tParams = unionType.Definition.TypeParameters;
            ImmutableArray<TypeSymbol> tArgs = unionType.TypeArguments;
            foreach (UnionVariantSymbol variant in unionType.Definition.Variants) {
              foreach (UnionVariantField field in variant.Fields) {
                TypeSymbol payloadType = TypeSubstitution.Substitute(field.Type, tParams, tArgs, types);
                if (!payloadType.IsError) {
                  builder.AddEdge(unionType, payloadType, isMutableEdge: false);
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
            // AD-003 / D-177: interface values wrap value-semantic structs (cycles only possible via D-056 closure captures).
            ImmutableArray<TypeParameterSymbol> tParams = ifaceType.Definition.TypeParameters;
            ImmutableArray<TypeSymbol> tArgs = ifaceType.TypeArguments;
            foreach (InterfaceProperty prop in ifaceType.Definition.Properties) {
              TypeSymbol propType = TypeSubstitution.Substitute(prop.Type, tParams, tArgs, types);
              if (!propType.IsError) {
                builder.AddEdge(ifaceType, propType, isMutableEdge: false);
                worklist.Enqueue(propType);
              }
            }
            foreach (MethodSymbol method in ifaceType.Definition.Methods) {
              foreach (ParameterSymbol param in method.Parameters) {
                TypeSymbol paramType = TypeSubstitution.Substitute(param.Type, tParams, tArgs, types);
                if (!paramType.IsError) {
                  builder.AddEdge(ifaceType, paramType, isMutableEdge: false);
                  worklist.Enqueue(paramType);
                }
              }
              TypeSymbol retType = TypeSubstitution.Substitute(method.ReturnType, tParams, tArgs, types);
              if (!retType.IsError) {
                builder.AddEdge(ifaceType, retType, isMutableEdge: false);
                worklist.Enqueue(retType);
              }
            }
            break;
          }

        case FunctionType fnType:
          // Closure environment containment edges come from recorded CaptureLists (D-056 / FN-21)
          // above; parameter/return types are visited as nodes without adding spurious self-containment edges.
          foreach (FunctionParameter param in fnType.Parameters) {
            if (!param.Type.IsError) {
              worklist.Enqueue(param.Type);
            }
          }
          if (!fnType.ReturnType.IsError) {
            worklist.Enqueue(fnType.ReturnType);
          }
          break;
      }
    }

    return builder.Build();
  }

  private static FunctionType? ResolveCallableFunctionType(
      ModuleCheckState state,
      int nodeId,
      TypeInterner types) {
    if ((uint) nodeId < (uint) state.NodeTypes.Length && state.NodeTypes[nodeId] is FunctionType directFn) {
      return directFn;
    }

    if ((uint) nodeId < (uint) state.Conversions.Length
        && state.Conversions[nodeId] is { Kind: ConversionKind.ImplicitItLambda, ImplicitLambdaType: { } implicitFn }) {
      return implicitFn;
    }

    if ((uint) nodeId < (uint) state.DeclaredSymbols.Length
        && state.DeclaredSymbols[nodeId] is FunctionSymbol fnSym
        && fnSym.TypeParameters.IsEmpty) {
      var parameters = ImmutableArray.CreateBuilder<FunctionParameter>(fnSym.Parameters.Length);
      foreach (ParameterSymbol p in fnSym.Parameters) {
        parameters.Add(new FunctionParameter(p.Type, p.IsMutable));
      }

      return types.GetFunction(parameters.MoveToImmutable(), fnSym.ReturnType);
    }

    return null;
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
