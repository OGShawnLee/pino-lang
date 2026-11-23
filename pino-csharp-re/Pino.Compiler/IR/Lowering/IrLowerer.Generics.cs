using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.IR;

public sealed partial class IrLowerer {
  private readonly Dictionary<FunctionSymbol, (SemanticModel Model, FunctionDeclarationSyntax Decl, string Prefix)> _genericFunctionDecls =
      new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<MethodSymbol, (SemanticModel Model, StructSymbol Owner, MethodDeclarationSyntax Decl, string Prefix)> _genericMethodDecls =
      new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<MonomorphKey, IrFuncId> _monoFuncIds = [];
  private readonly Queue<PendingMonomorphization> _monoWorklist = new();

  private sealed record PendingMonomorphization(
      Symbol Definition,
      ImmutableArray<TypeParameterSymbol> TypeParams,
      ImmutableArray<TypeSymbol> TypeArgs,
      IrFuncId FuncId);

  // ---------------------------------------------------------------------------
  // Seeding & Worklist Drain (GEN-1..11, IQ-6, design §2.5)
  // ---------------------------------------------------------------------------

  private void SeedGenericInstantiations() {
    foreach (GenericInstantiation inst in _compilation.GenericInstantiations) {
      if (inst.TypeArguments.IsDefaultOrEmpty || inst.TypeArguments.Any(ContainsUnresolvedTypeParam)) {
        continue;
      }

      switch (inst.Definition) {
        case StructSymbol st:
          _ = InternType(_compilation.Types.GetStruct(st, inst.TypeArguments));
          break;

        case UnionSymbol ut:
          _ = InternType(_compilation.Types.GetUnion(ut, inst.TypeArguments));
          break;

        case InterfaceSymbol it:
          _ = InternType(_compilation.Types.GetInterface(it, inst.TypeArguments));
          break;

        case FunctionSymbol fn when !fn.TypeParameters.IsEmpty && _genericFunctionDecls.ContainsKey(fn):
          _ = GetOrRequestMonoFunction(fn, inst.TypeArguments);
          break;

        case MethodSymbol m: {
            MethodSymbol canonical = m.PromotedFrom ?? m;
            if (!_genericMethodDecls.TryGetValue(canonical, out (SemanticModel Model, StructSymbol Owner, MethodDeclarationSyntax Decl, string Prefix) declInfo)) {
              break;
            }

            int ownerCount = declInfo.Owner.TypeParameters.Length;
            int methodCount = canonical.TypeParameters.Length;
            if (inst.TypeArguments.Length == ownerCount + methodCount) {
              ImmutableArray<TypeSymbol> ownerArgs = ownerCount == 0
                  ? []
                  : inst.TypeArguments.AsSpan(0, ownerCount).ToImmutableArray();
              ImmutableArray<TypeSymbol> methodArgs = methodCount == 0
                  ? []
                  : inst.TypeArguments.AsSpan(ownerCount, methodCount).ToImmutableArray();
              _ = GetOrRequestMonoMethod(canonical, ownerArgs, methodArgs);
            }
            break;
          }
      }
    }
  }

  private void DrainMonomorphizationWorklist() {
    while (_monoWorklist.Count > 0) {
      PendingMonomorphization item = _monoWorklist.Dequeue();
      switch (item.Definition) {
        case FunctionSymbol fnSym:
          LowerMonomorphizedFunction(fnSym, item.TypeParams, item.TypeArgs, item.FuncId);
          break;

        case MethodSymbol methodSym:
          LowerMonomorphizedMethod(methodSym, item.TypeParams, item.TypeArgs, item.FuncId);
          break;
      }
    }
  }

  private IrFuncId GetOrRequestMonoFunction(
      FunctionSymbol fnSym,
      ImmutableArray<TypeSymbol> concreteTypeArgs) {
    var irArgs = ImmutableArray.CreateBuilder<IrTypeId>(concreteTypeArgs.Length);
    foreach (TypeSymbol arg in concreteTypeArgs) {
      irArgs.Add(InternType(arg));
    }

    ImmutableArray<IrTypeId> normalizedIrArgs = irArgs.MoveToImmutable();
    var key = new MonomorphKey(fnSym, normalizedIrArgs);
    if (_monoFuncIds.TryGetValue(key, out IrFuncId existing)) {
      return existing;
    }

    if (!_genericFunctionDecls.TryGetValue(fnSym, out (SemanticModel Model, FunctionDeclarationSyntax Decl, string Prefix) declInfo)) {
      throw new InvalidOperationException($"Generic function '{fnSym.Name}' declaration was not recorded.");
    }

    ImmutableArray<TypeParameterSymbol> typeParams = fnSym.TypeParameters;
    var irParams = ImmutableArray.CreateBuilder<IrParam>(fnSym.Parameters.Length);
    foreach (ParameterSymbol p in fnSym.Parameters) {
      TypeSymbol concreteParam = TypeSubstitution.Substitute(p.Type, typeParams, concreteTypeArgs, _compilation.Types);
      IrTypeId pType = InternType(concreteParam);
      irParams.Add(p.IsMutable
          ? new IrParam(p.Name, _types.GetPtr(pType), IrParamOwnership.Mut)
          : new IrParam(p.Name, pType, IrParamOwnership.Owned));
    }

    TypeSymbol concreteRet = TypeSubstitution.Substitute(fnSym.ReturnType, typeParams, concreteTypeArgs, _compilation.Types);
    IrTypeId retType = InternType(concreteRet);
    ModuleStateEffects effects = _compilation.GetModuleStateEffects(fnSym);
    string mangledName = ReserveFunctionName(FormatMonoName($"{declInfo.Prefix}{fnSym.Name}", normalizedIrArgs));
    IrFunction irFn = _module.AddFunction(mangledName, irParams.MoveToImmutable(), retType, effects);

    _monoFuncIds[key] = irFn.Id;
    _monoWorklist.Enqueue(new PendingMonomorphization(fnSym, typeParams, concreteTypeArgs, irFn.Id));
    return irFn.Id;
  }

  private IrFuncId GetOrRequestMonoMethod(
      MethodSymbol canonicalMethod,
      ImmutableArray<TypeSymbol> ownerTypeArgs,
      ImmutableArray<TypeSymbol> methodTypeArgs) {
    if (!_genericMethodDecls.TryGetValue(canonicalMethod, out (SemanticModel Model, StructSymbol Owner, MethodDeclarationSyntax Decl, string Prefix) declInfo)) {
      if (ownerTypeArgs.IsEmpty && methodTypeArgs.IsEmpty && _methodIds.TryGetValue(canonicalMethod, out IrFuncId nonGenId)) {
        return nonGenId;
      }

      throw new InvalidOperationException($"Generic method '{canonicalMethod.Name}' declaration was not recorded.");
    }

    ImmutableArray<TypeParameterSymbol> combinedParams = [.. declInfo.Owner.TypeParameters, .. canonicalMethod.TypeParameters];
    ImmutableArray<TypeSymbol> combinedArgs = [.. ownerTypeArgs, .. methodTypeArgs];

    var irArgs = ImmutableArray.CreateBuilder<IrTypeId>(combinedArgs.Length);
    foreach (TypeSymbol arg in combinedArgs) {
      irArgs.Add(InternType(arg));
    }

    ImmutableArray<IrTypeId> normalizedIrArgs = irArgs.MoveToImmutable();
    var key = new MonomorphKey(canonicalMethod, normalizedIrArgs);
    if (_monoFuncIds.TryGetValue(key, out IrFuncId existing)) {
      return existing;
    }

    var irParams = ImmutableArray.CreateBuilder<IrParam>(
        canonicalMethod.Parameters.Length + (canonicalMethod.IsStatic ? 0 : 1));
    if (!canonicalMethod.IsStatic) {
      StructType concreteOwner = _compilation.Types.GetStruct(declInfo.Owner, ownerTypeArgs);
      IrTypeId ownerIrId = InternType(concreteOwner);
      irParams.Add(canonicalMethod.IsMutating
          ? new IrParam("self", _types.GetPtr(ownerIrId), IrParamOwnership.Mut)
          : new IrParam("self", ownerIrId, IrParamOwnership.Owned));
    }

    foreach (ParameterSymbol p in canonicalMethod.Parameters) {
      TypeSymbol concreteParam = TypeSubstitution.Substitute(p.Type, combinedParams, combinedArgs, _compilation.Types);
      IrTypeId pType = InternType(concreteParam);
      irParams.Add(p.IsMutable
          ? new IrParam(p.Name, _types.GetPtr(pType), IrParamOwnership.Mut)
          : new IrParam(p.Name, pType, IrParamOwnership.Owned));
    }

    TypeSymbol concreteRet = TypeSubstitution.Substitute(canonicalMethod.ReturnType, combinedParams, combinedArgs, _compilation.Types);
    IrTypeId retType = InternType(concreteRet);
    ModuleStateEffects effects = _compilation.GetModuleStateEffects(canonicalMethod);

    string ownerBase = ownerTypeArgs.IsEmpty
        ? $"{declInfo.Prefix}{declInfo.Owner.Name}"
        : FormatMonoName($"{declInfo.Prefix}{declInfo.Owner.Name}", normalizedIrArgs.AsSpan(0, ownerTypeArgs.Length).ToImmutableArray());
    string methodBase = methodTypeArgs.IsEmpty
        ? canonicalMethod.Name
        : FormatMonoName(canonicalMethod.Name, normalizedIrArgs.AsSpan(ownerTypeArgs.Length, methodTypeArgs.Length).ToImmutableArray());
    string mangledName = ReserveFunctionName($"{ownerBase}::{methodBase}");

    IrFunction irFn = _module.AddFunction(mangledName, irParams.MoveToImmutable(), retType, effects);
    _monoFuncIds[key] = irFn.Id;
    _monoWorklist.Enqueue(new PendingMonomorphization(canonicalMethod, combinedParams, combinedArgs, irFn.Id));
    return irFn.Id;
  }

  private void LowerMonomorphizedFunction(
      FunctionSymbol fnSym,
      ImmutableArray<TypeParameterSymbol> typeParams,
      ImmutableArray<TypeSymbol> typeArgs,
      IrFuncId funcId) {
    (SemanticModel model, FunctionDeclarationSyntax decl, _) = _genericFunctionDecls[fnSym];
    IrFunction irFn = _module.Functions[funcId.Index];
    TypeSymbol concreteRet = TypeSubstitution.Substitute(fnSym.ReturnType, typeParams, typeArgs, _compilation.Types);
    var fb = new FunctionBuilder(
        this,
        irFn,
        model,
        declaredReturnType: concreteRet,
        typeParams: typeParams,
        typeArgs: typeArgs);

    for (int i = 0; i < fnSym.Parameters.Length; i++) {
      fb.ParamIndices[fnSym.Parameters[i]] = i;
    }

    PrepareCallableScope(fb, decl.Body);
    LowerBlockItems(fb, decl.Body.Items);
    FinalizeFunctionBody(fb, decl.Body.Span);
  }

  private void LowerMonomorphizedMethod(
      MethodSymbol methodSym,
      ImmutableArray<TypeParameterSymbol> typeParams,
      ImmutableArray<TypeSymbol> typeArgs,
      IrFuncId funcId) {
    (SemanticModel model, StructSymbol owner, MethodDeclarationSyntax decl, _) = _genericMethodDecls[methodSym];
    IrFunction irFn = _module.Functions[funcId.Index];
    TypeSymbol concreteRet = TypeSubstitution.Substitute(methodSym.ReturnType, typeParams, typeArgs, _compilation.Types);
    var fb = new FunctionBuilder(
        this,
        irFn,
        model,
        enclosingStruct: owner,
        isMutatingMethod: methodSym.IsMutating,
        declaredReturnType: concreteRet,
        typeParams: typeParams,
        typeArgs: typeArgs);

    int paramOffset = methodSym.IsStatic ? 0 : 1;
    for (int i = 0; i < methodSym.Parameters.Length; i++) {
      fb.ParamIndices[methodSym.Parameters[i]] = i + paramOffset;
    }

    PrepareCallableScope(fb, decl.Body);
    LowerBlockItems(fb, decl.Body.Items);
    FinalizeFunctionBody(fb, decl.Body.Span);
  }

  // ---------------------------------------------------------------------------
  // Concrete type-argument recovery at generic call sites & bounded dispatch
  // ---------------------------------------------------------------------------

  private IrFuncId ResolveConcreteStructMethodId(
      StructType concreteStruct,
      MethodSymbol methodSym,
      ImmutableArray<TypeSymbol> methodTypeArgs = default) {
    MethodSymbol canonical = methodSym.PromotedFrom ?? methodSym;
    ImmutableArray<TypeSymbol> normMethodArgs = methodTypeArgs.IsDefault ? [] : methodTypeArgs;
    ImmutableArray<TypeSymbol> ownerArgs = methodSym.IsPromotedFromEmbedded
        ? []
        : concreteStruct.TypeArguments;

    if (ownerArgs.IsEmpty && normMethodArgs.IsEmpty) {
      if (_methodIds.TryGetValue(canonical, out IrFuncId id)) {
        return id;
      }
    }

    return GetOrRequestMonoMethod(canonical, ownerArgs, normMethodArgs);
  }

  private ImmutableArray<TypeSymbol> ResolveGenericFunctionTypeArgs(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      FunctionSymbol fnSym) =>
      ResolveCallableTypeArguments(
          fb,
          call,
          ownerParams: [],
          knownOwnerArgs: [],
          methodParams: fnSym.TypeParameters,
          rawParams: fnSym.Parameters,
          rawReturn: fnSym.ReturnType,
          explicitTypeArgs: call.TypeArguments);

  private (ImmutableArray<TypeSymbol> OwnerArgs, ImmutableArray<TypeSymbol> MethodArgs) ResolveStaticMethodTypeArgs(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      MethodSymbol methodSym) {
    var ownerStruct = (StructSymbol) methodSym.ContainingType;
    var staticAccess = call.Callee as StaticAccessExpressionSyntax;

    var bindings = new Dictionary<TypeParameterSymbol, TypeSymbol>(ReferenceEqualityComparer.Instance);

    if (staticAccess?.TargetTypeArguments is { } ownerExplicit) {
      int n = Math.Min(ownerStruct.TypeParameters.Length, ownerExplicit.Arguments.Length);
      for (int i = 0; i < n; i++) {
        if (fb.Model.GetType(ownerExplicit.Arguments[i]) is { } rawArg) {
          bindings[ownerStruct.TypeParameters[i]] = fb.ResolveType(rawArg);
        }
      }
    }

    if (call.TypeArguments is { } methodExplicit) {
      int n = Math.Min(methodSym.TypeParameters.Length, methodExplicit.Arguments.Length);
      for (int i = 0; i < n; i++) {
        if (fb.Model.GetType(methodExplicit.Arguments[i]) is { } rawArg) {
          bindings[methodSym.TypeParameters[i]] = fb.ResolveType(rawArg);
        }
      }
    }

    MatchCallableSignatureBindings(fb, call, methodSym.Parameters, methodSym.ReturnType, bindings);

    ImmutableArray<TypeSymbol> ownerArgs = CollectBoundArgs(ownerStruct.TypeParameters, bindings, methodSym.Name);
    ImmutableArray<TypeSymbol> methodArgs = CollectBoundArgs(methodSym.TypeParameters, bindings, methodSym.Name);
    return (ownerArgs, methodArgs);
  }

  private ImmutableArray<TypeSymbol> ResolveStaticAccessOwnerTypeArgs(
      FunctionBuilder fb,
      StaticAccessExpressionSyntax access,
      StructSymbol ownerStruct,
      MethodSymbol methodSym,
      FunctionType concreteFnType) {
    var bindings = new Dictionary<TypeParameterSymbol, TypeSymbol>(ReferenceEqualityComparer.Instance);

    if (access.TargetTypeArguments is { } ownerExplicit) {
      int n = Math.Min(ownerStruct.TypeParameters.Length, ownerExplicit.Arguments.Length);
      for (int i = 0; i < n; i++) {
        if (fb.Model.GetType(ownerExplicit.Arguments[i]) is { } rawArg) {
          bindings[ownerStruct.TypeParameters[i]] = fb.ResolveType(rawArg);
        }
      }
    }

    int pCount = Math.Min(methodSym.Parameters.Length, concreteFnType.Parameters.Length);
    for (int i = 0; i < pCount; i++) {
      MatchTypePattern(methodSym.Parameters[i].Type, concreteFnType.Parameters[i].Type, bindings);
    }

    MatchTypePattern(methodSym.ReturnType, concreteFnType.ReturnType, bindings);
    return CollectBoundArgs(ownerStruct.TypeParameters, bindings, methodSym.Name);
  }

  private ImmutableArray<TypeSymbol> ResolveCallableTypeArguments(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      ImmutableArray<TypeParameterSymbol> ownerParams,
      ImmutableArray<TypeSymbol> knownOwnerArgs,
      ImmutableArray<TypeParameterSymbol> methodParams,
      ImmutableArray<ParameterSymbol> rawParams,
      TypeSymbol rawReturn,
      TypeArgumentListSyntax? explicitTypeArgs) {
    if (methodParams.IsEmpty) {
      return [];
    }

    var bindings = new Dictionary<TypeParameterSymbol, TypeSymbol>(ReferenceEqualityComparer.Instance);
    for (int i = 0; i < Math.Min(ownerParams.Length, knownOwnerArgs.Length); i++) {
      bindings[ownerParams[i]] = knownOwnerArgs[i];
    }

    if (explicitTypeArgs is not null) {
      int n = Math.Min(methodParams.Length, explicitTypeArgs.Arguments.Length);
      for (int i = 0; i < n; i++) {
        if (fb.Model.GetType(explicitTypeArgs.Arguments[i]) is { } rawArg) {
          bindings[methodParams[i]] = fb.ResolveType(rawArg);
        }
      }
    }

    MatchCallableSignatureBindings(fb, call, rawParams, rawReturn, bindings);
    return CollectBoundArgs(methodParams, bindings, "generic callable");
  }

  private static void MatchCallableSignatureBindings(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      ImmutableArray<ParameterSymbol> rawParams,
      TypeSymbol rawReturn,
      Dictionary<TypeParameterSymbol, TypeSymbol> bindings) {
    if (fb.Model.GetType(call.Callee) is FunctionType calleeFnType) {
      FunctionType resolvedCalleeFn = (FunctionType) fb.ResolveType(calleeFnType);
      int pCount = Math.Min(rawParams.Length, resolvedCalleeFn.Parameters.Length);
      for (int i = 0; i < pCount; i++) {
        MatchTypePattern(rawParams[i].Type, resolvedCalleeFn.Parameters[i].Type, bindings);
      }

      MatchTypePattern(rawReturn, resolvedCalleeFn.ReturnType, bindings);
    }

    if (fb.Model.GetType(call) is { } callRet) {
      MatchTypePattern(rawReturn, fb.ResolveType(callRet), bindings);
    }
  }

  private static void MatchTypePattern(
      TypeSymbol pattern,
      TypeSymbol concrete,
      Dictionary<TypeParameterSymbol, TypeSymbol> bindings) {
    if (pattern is TypeParameterType tp) {
      bindings.TryAdd(tp.Symbol, concrete);
      return;
    }

    switch (pattern, concrete) {
      case (VectorType pv, VectorType cv):
        MatchTypePattern(pv.ElementType, cv.ElementType, bindings);
        break;

      case (MapType pm, MapType cm):
        MatchTypePattern(pm.KeyType, cm.KeyType, bindings);
        MatchTypePattern(pm.ValueType, cm.ValueType, bindings);
        break;

      case (SetType ps, SetType cs):
        MatchTypePattern(ps.ElementType, cs.ElementType, bindings);
        break;

      case (PoolType pp, PoolType cp):
        MatchTypePattern(pp.ElementType, cp.ElementType, bindings);
        break;

      case (HandleType ph, HandleType ch):
        MatchTypePattern(ph.ElementType, ch.ElementType, bindings);
        break;

      case (TupleType pt, TupleType ct) when pt.Fields.Length == ct.Fields.Length:
        for (int i = 0; i < pt.Fields.Length; i++) {
          MatchTypePattern(pt.Fields[i].Type, ct.Fields[i].Type, bindings);
        }
        break;

      case (FunctionType pf, FunctionType cf) when pf.Parameters.Length == cf.Parameters.Length:
        for (int i = 0; i < pf.Parameters.Length; i++) {
          MatchTypePattern(pf.Parameters[i].Type, cf.Parameters[i].Type, bindings);
        }
        MatchTypePattern(pf.ReturnType, cf.ReturnType, bindings);
        break;

      case (StructType pst, StructType cst)
          when ReferenceEquals(pst.Definition, cst.Definition) && pst.TypeArguments.Length == cst.TypeArguments.Length:
        for (int i = 0; i < pst.TypeArguments.Length; i++) {
          MatchTypePattern(pst.TypeArguments[i], cst.TypeArguments[i], bindings);
        }
        break;

      case (UnionType put, UnionType cut)
          when ReferenceEquals(put.Definition, cut.Definition) && put.TypeArguments.Length == cut.TypeArguments.Length:
        for (int i = 0; i < put.TypeArguments.Length; i++) {
          MatchTypePattern(put.TypeArguments[i], cut.TypeArguments[i], bindings);
        }
        break;

      case (InterfaceType pit, InterfaceType cit)
          when ReferenceEquals(pit.Definition, cit.Definition) && pit.TypeArguments.Length == cit.TypeArguments.Length:
        for (int i = 0; i < pit.TypeArguments.Length; i++) {
          MatchTypePattern(pit.TypeArguments[i], cit.TypeArguments[i], bindings);
        }
        break;
    }
  }

  private static ImmutableArray<TypeSymbol> CollectBoundArgs(
      ImmutableArray<TypeParameterSymbol> parameters,
      Dictionary<TypeParameterSymbol, TypeSymbol> bindings,
      string ownerName) {
    if (parameters.IsEmpty) {
      return [];
    }

    var result = ImmutableArray.CreateBuilder<TypeSymbol>(parameters.Length);
    foreach (TypeParameterSymbol tp in parameters) {
      if (!bindings.TryGetValue(tp, out TypeSymbol? bound)) {
        throw new InvalidOperationException(
            $"Could not resolve concrete type argument for '{tp.Name}' in '{ownerName}'.");
      }

      result.Add(bound);
    }

    return result.MoveToImmutable();
  }

  private static bool ContainsUnresolvedTypeParam(TypeSymbol type) => type switch {
    TypeParameterType or TypeVariableType or PendingReturnType or ErrorType => true,
    VectorType v => ContainsUnresolvedTypeParam(v.ElementType),
    MapType m => ContainsUnresolvedTypeParam(m.KeyType) || ContainsUnresolvedTypeParam(m.ValueType),
    SetType s => ContainsUnresolvedTypeParam(s.ElementType),
    PoolType p => ContainsUnresolvedTypeParam(p.ElementType),
    HandleType h => ContainsUnresolvedTypeParam(h.ElementType),
    TupleType t => t.Fields.Any(static f => ContainsUnresolvedTypeParam(f.Type)),
    FunctionType f => ContainsUnresolvedTypeParam(f.ReturnType) || f.Parameters.Any(static p => ContainsUnresolvedTypeParam(p.Type)),
    StructType st => st.TypeArguments.Any(ContainsUnresolvedTypeParam),
    UnionType ut => ut.TypeArguments.Any(ContainsUnresolvedTypeParam),
    InterfaceType it => it.TypeArguments.Any(ContainsUnresolvedTypeParam),
    _ => false,
  };

  private string FormatMonoName(string baseName, ImmutableArray<IrTypeId> irTypeArgs) {
    if (irTypeArgs.IsEmpty) {
      return baseName;
    }

    string suffix = string.Join("__", irTypeArgs.Select(id => SanitizeTypeName(_types.GetType(id).Name)));
    return $"{baseName}__{suffix}";
  }

  private readonly struct MonomorphKey(Symbol definition, ImmutableArray<IrTypeId> typeArgs)
      : IEquatable<MonomorphKey> {
    public Symbol Definition { get; } = definition;
    public ImmutableArray<IrTypeId> TypeArgs { get; } = typeArgs;

    public bool Equals(MonomorphKey other) {
      if (!ReferenceEquals(Definition, other.Definition) || TypeArgs.Length != other.TypeArgs.Length) {
        return false;
      }

      for (int i = 0; i < TypeArgs.Length; i++) {
        if (TypeArgs[i] != other.TypeArgs[i]) {
          return false;
        }
      }

      return true;
    }

    public override bool Equals(object? obj) => obj is MonomorphKey other && Equals(other);

    public override int GetHashCode() {
      var hc = new HashCode();
      hc.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Definition));
      hc.Add(TypeArgs.Length);
      foreach (IrTypeId id in TypeArgs) {
        hc.Add(id.Index);
      }

      return hc.ToHashCode();
    }
  }
}
