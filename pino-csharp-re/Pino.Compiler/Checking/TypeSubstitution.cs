using System.Collections.Immutable;

namespace Pino.Compiler.Checking;

internal enum TypePosition : byte {
  FunctionReturn,
  FunctionTypeReturn,
  ResultSuccessArg,
  VariableAnnotation,
  ParameterType,
  FieldType,
  InterfacePropertyType,
  CollectionElement,
  MapKey,
  MapValue,
  UnionPayload,
  TupleField,
  GenericTypeArgument,
  GenericConstraint,
  StructLiteralType,
  IsPatternType,
}

internal static class TypeSubstitution {
  public static TypeSymbol Substitute(
      TypeSymbol type,
      ImmutableArray<TypeParameterSymbol> parameters,
      ImmutableArray<TypeSymbol> arguments,
      TypeInterner types) {
    if (parameters.IsEmpty || arguments.IsEmpty || type.IsError || type.IsVoid || type.IsNever) {
      return type;
    }

    return type switch {
      TypeParameterType tp => SubstituteTypeParameter(tp, parameters, arguments),
      VectorType vec => types.GetVector(Substitute(vec.ElementType, parameters, arguments, types)),
      MapType map => types.GetMap(
          Substitute(map.KeyType, parameters, arguments, types),
          Substitute(map.ValueType, parameters, arguments, types)),
      SetType set => types.GetSet(Substitute(set.ElementType, parameters, arguments, types)),
      TupleType tuple => types.GetTuple(
          [.. tuple.Fields.Select(f => new TupleField(f.Label, Substitute(f.Type, parameters, arguments, types)))]),
      FunctionType fn => types.GetFunction(
          [.. fn.ParameterTypes.Select(p => Substitute(p, parameters, arguments, types))],
          Substitute(fn.ReturnType, parameters, arguments, types)),
      StructType st when !st.TypeArguments.IsEmpty => types.GetStruct(
          st.Definition,
          [.. st.TypeArguments.Select(a => Substitute(a, parameters, arguments, types))]),
      UnionType ut when !ut.TypeArguments.IsEmpty => types.GetUnion(
          ut.Definition,
          [.. ut.TypeArguments.Select(a => Substitute(a, parameters, arguments, types))]),
      InterfaceType it when !it.TypeArguments.IsEmpty => types.GetInterface(
          it.Definition,
          [.. it.TypeArguments.Select(a => Substitute(a, parameters, arguments, types))]),
      _ => type,
    };
  }

  private static TypeSymbol SubstituteTypeParameter(
      TypeParameterType tp,
      ImmutableArray<TypeParameterSymbol> parameters,
      ImmutableArray<TypeSymbol> arguments) {
    int count = Math.Min(parameters.Length, arguments.Length);
    for (int i = 0; i < count; i++) {
      if (ReferenceEquals(parameters[i], tp.Symbol)) {
        return arguments[i];
      }
    }

    return tp;
  }
}

internal static class TypeCompatibility {
  public static bool SatisfiesInterface(
      TypeSymbol candidate,
      InterfaceType target,
      TypeInterner types,
      HashSet<(TypeSymbol, TypeSymbol)>? visited = null) {
    if (candidate.IsError || target.IsError) {
      return true;
    }

    if (ReferenceEquals(candidate, target)) {
      return true;
    }

    visited ??= new HashSet<(TypeSymbol, TypeSymbol)>();
    if (!visited.Add((candidate, target))) {
      return true;
    }

    if (candidate is TypeParameterType tp) {
      return tp.Symbol.Constraint is { } constraint
          && SatisfiesInterface(constraint, target, types, visited);
    }

    if (candidate is StructType structType) {
      return StructSatisfiesInterface(structType, target, types);
    }

    if (candidate is InterfaceType candidateIface) {
      return InterfaceSatisfiesInterface(candidateIface, target, types);
    }

    return false;
  }

  private static bool StructSatisfiesInterface(
      StructType structType,
      InterfaceType target,
      TypeInterner types) {
    ImmutableArray<TypeParameterSymbol> targetParams = target.Definition.TypeParameters;
    ImmutableArray<TypeSymbol> targetArgs = target.TypeArguments;
    ImmutableArray<TypeParameterSymbol> structParams = structType.Definition.TypeParameters;
    ImmutableArray<TypeSymbol> structArgs = structType.TypeArguments;

    foreach (InterfaceProperty prop in target.Definition.Properties) {
      TypeSymbol expectedPropType = TypeSubstitution.Substitute(prop.Type, targetParams, targetArgs, types);
      FieldSymbol? matchingField = FindStructField(structType.Definition, prop.Name);
      if (matchingField is null) {
        return false;
      }

      TypeSymbol actualFieldType = TypeSubstitution.Substitute(matchingField.Type, structParams, structArgs, types);
      if (!expectedPropType.IsError && !actualFieldType.IsError
          && !ReferenceEquals(actualFieldType, expectedPropType)) {
        return false;
      }
    }

    foreach (MethodSymbol ifaceMethod in target.Definition.Methods) {
      if (!StructHasMatchingMethod(structType, ifaceMethod, targetParams, targetArgs, types)) {
        return false;
      }
    }

    return true;
  }

  private static bool InterfaceSatisfiesInterface(
      InterfaceType candidate,
      InterfaceType target,
      TypeInterner types) {
    ImmutableArray<TypeParameterSymbol> targetParams = target.Definition.TypeParameters;
    ImmutableArray<TypeSymbol> targetArgs = target.TypeArguments;
    ImmutableArray<TypeParameterSymbol> candParams = candidate.Definition.TypeParameters;
    ImmutableArray<TypeSymbol> candArgs = candidate.TypeArguments;

    foreach (InterfaceProperty prop in target.Definition.Properties) {
      TypeSymbol expectedType = TypeSubstitution.Substitute(prop.Type, targetParams, targetArgs, types);
      InterfaceProperty? match = null;
      foreach (InterfaceProperty candProp in candidate.Definition.Properties) {
        if (string.Equals(candProp.Name, prop.Name, StringComparison.Ordinal)) {
          match = candProp;
          break;
        }
      }

      if (match is null) {
        return false;
      }

      TypeSymbol actualType = TypeSubstitution.Substitute(match.Type, candParams, candArgs, types);
      if (!expectedType.IsError && !actualType.IsError && !ReferenceEquals(actualType, expectedType)) {
        return false;
      }
    }

    foreach (MethodSymbol targetMethod in target.Definition.Methods) {
      MethodSymbol? candMethod = null;
      foreach (MethodSymbol m in candidate.Definition.Methods) {
        if (string.Equals(m.Name, targetMethod.Name, StringComparison.Ordinal)) {
          candMethod = m;
          break;
        }
      }

      if (candMethod is null) {
        return false;
      }

      if (!SignaturesMatch(
          candMethod.Parameters,
          candMethod.ReturnType,
          candParams,
          candArgs,
          targetMethod.Parameters,
          targetMethod.ReturnType,
          targetParams,
          targetArgs,
          types)) {
        return false;
      }
    }

    return true;
  }

  private static FieldSymbol? FindStructField(StructSymbol structSym, string name) {
    foreach (FieldSymbol field in structSym.AllFields) {
      if (string.Equals(field.Name, name, StringComparison.Ordinal)) {
        return field;
      }
    }

    return null;
  }

  private static bool StructHasMatchingMethod(
      StructType structType,
      MethodSymbol ifaceMethod,
      ImmutableArray<TypeParameterSymbol> targetParams,
      ImmutableArray<TypeSymbol> targetArgs,
      TypeInterner types) {
    ImmutableArray<TypeParameterSymbol> structParams = structType.Definition.TypeParameters;
    ImmutableArray<TypeSymbol> structArgs = structType.TypeArguments;

    foreach (MethodSymbol m in structType.Definition.Methods) {
      if (m.IsStatic || !string.Equals(m.Name, ifaceMethod.Name, StringComparison.Ordinal)) {
        continue;
      }

      return SignaturesMatch(
          m.Parameters,
          m.ReturnType,
          structParams,
          structArgs,
          ifaceMethod.Parameters,
          ifaceMethod.ReturnType,
          targetParams,
          targetArgs,
          types);
    }

    // Also allow a callable field of matching FunctionType (STRUCT-12).
    FieldSymbol? callableField = FindStructField(structType.Definition, ifaceMethod.Name);
    if (callableField is not null) {
      TypeSymbol fieldType = TypeSubstitution.Substitute(callableField.Type, structParams, structArgs, types);
      if (fieldType is FunctionType fnType) {
        if (fnType.ParameterTypes.Length != ifaceMethod.Parameters.Length) {
          return false;
        }

        for (int i = 0; i < fnType.ParameterTypes.Length; i++) {
          TypeSymbol expectedParam = TypeSubstitution.Substitute(
              ifaceMethod.Parameters[i].Type, targetParams, targetArgs, types);
          if (!expectedParam.IsError && !fnType.ParameterTypes[i].IsError
              && !ReferenceEquals(fnType.ParameterTypes[i], expectedParam)) {
            return false;
          }
        }

        TypeSymbol expectedRet = TypeSubstitution.Substitute(
            ifaceMethod.ReturnType, targetParams, targetArgs, types);
        return expectedRet.IsError || fnType.ReturnType.IsError
            || ReferenceEquals(fnType.ReturnType, expectedRet);
      }
    }

    return false;
  }

  private static bool SignaturesMatch(
      ImmutableArray<ParameterSymbol> actualParams,
      TypeSymbol actualReturn,
      ImmutableArray<TypeParameterSymbol> actualTypeParams,
      ImmutableArray<TypeSymbol> actualTypeArgs,
      ImmutableArray<ParameterSymbol> expectedParams,
      TypeSymbol expectedReturn,
      ImmutableArray<TypeParameterSymbol> expectedTypeParams,
      ImmutableArray<TypeSymbol> expectedTypeArgs,
      TypeInterner types) {
    if (actualParams.Length != expectedParams.Length) {
      return false;
    }

    for (int i = 0; i < actualParams.Length; i++) {
      TypeSymbol actualType = TypeSubstitution.Substitute(
          actualParams[i].Type, actualTypeParams, actualTypeArgs, types);
      TypeSymbol expectedType = TypeSubstitution.Substitute(
          expectedParams[i].Type, expectedTypeParams, expectedTypeArgs, types);
      if (!actualType.IsError && !expectedType.IsError && !ReferenceEquals(actualType, expectedType)) {
        return false;
      }
    }

    TypeSymbol resolvedActualRet = TypeSubstitution.Substitute(
        actualReturn, actualTypeParams, actualTypeArgs, types);
    TypeSymbol resolvedExpectedRet = TypeSubstitution.Substitute(
        expectedReturn, expectedTypeParams, expectedTypeArgs, types);
    return resolvedActualRet.IsError || resolvedExpectedRet.IsError
        || ReferenceEquals(resolvedActualRet, resolvedExpectedRet);
  }
}
