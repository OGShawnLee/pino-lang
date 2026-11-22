using System.Collections.Immutable;

using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

/// <summary>
/// Local union-find unifier for <c>@@Generic</c> type inference. Every <see cref="TypeVariableType"/>
/// allocated by a unifier is fully zonked before leaving Phase 3 so zero type variables ever escape
/// into <see cref="SemanticModel"/> or <see cref="Compilation"/>.
/// </summary>
internal sealed class Unifier {
  private readonly TypeInterner _types;
  private readonly List<VarSlot> _slots = [];

  private sealed class VarSlot {
    public VarSlot(TypeVariableType variable, TypeParameterSymbol parameter) {
      Variable = variable;
      Parameter = parameter;
    }

    public TypeVariableType Variable { get; }
    public TypeParameterSymbol Parameter { get; }
    public TypeSymbol? Bound { get; set; }
    public SourceSpan FirstBindingSpan { get; set; }
    public TypeSymbol? FirstBindingType { get; set; }
  }

  public readonly record struct UnifyConflict(
      TypeParameterSymbol? Parameter,
      TypeSymbol ExpectedOrFirst,
      TypeSymbol Actual,
      SourceSpan FirstBindingSpan,
      bool IsOccursCheck);

  public Unifier(TypeInterner types) => _types = types;

  public TypeVariableType Fresh(TypeParameterSymbol parameter, SourceSpan originSpan) {
    int id = _slots.Count;
    var variable = new TypeVariableType(id, originSpan);
    _slots.Add(new VarSlot(variable, parameter));
    return variable;
  }

  public TypeParameterSymbol GetParameter(TypeVariableType variable) => _slots[variable.Id].Parameter;

  public bool IsBound(TypeVariableType variable) => Resolve(variable) is not TypeVariableType;

  public TypeSymbol Resolve(TypeSymbol type) {
    if (type is not TypeVariableType tv) {
      return type;
    }

    VarSlot slot = _slots[tv.Id];
    if (slot.Bound is null) {
      return tv;
    }

    TypeSymbol root = Resolve(slot.Bound);
    slot.Bound = root;
    return root;
  }

  public void ForceBind(TypeVariableType variable, TypeSymbol type, SourceSpan span) {
    TypeSymbol resolved = Resolve(variable);
    if (resolved is TypeVariableType rootVar) {
      VarSlot slot = _slots[rootVar.Id];
      slot.Bound = type;
      slot.FirstBindingSpan = span;
      slot.FirstBindingType = type;
    }
  }

  public bool Unify(
      TypeSymbol pattern,
      TypeSymbol actual,
      SourceSpan siteSpan,
      out UnifyConflict? conflict,
      bool allowIntToFloat = false) {
    conflict = null;

    if (pattern.IsError || actual.IsError
        || pattern.IsNever || actual.IsNever
        || pattern is PendingReturnType || actual is PendingReturnType) {
      return true;
    }

    if (pattern is TypeVariableType patternVar) {
      return UnifyWithVariable(patternVar, actual, siteSpan, out conflict, allowIntToFloat);
    }

    if (actual is TypeVariableType actualVar) {
      return UnifyWithVariable(actualVar, pattern, siteSpan, out conflict, allowIntToFloat: false);
    }

    if (ReferenceEquals(pattern, actual)) {
      return true;
    }

    if (allowIntToFloat && ReferenceEquals(pattern, _types.Float) && ReferenceEquals(actual, _types.Int)) {
      return true;
    }

    switch (pattern) {
      case VectorType pv when actual is VectorType av:
        return Unify(pv.ElementType, av.ElementType, siteSpan, out conflict, allowIntToFloat: false);

      case MapType pm when actual is MapType am:
        return Unify(pm.KeyType, am.KeyType, siteSpan, out conflict, allowIntToFloat: false)
            && Unify(pm.ValueType, am.ValueType, siteSpan, out conflict, allowIntToFloat: false);

      case SetType ps when actual is SetType @as:
        return Unify(ps.ElementType, @as.ElementType, siteSpan, out conflict, allowIntToFloat: false);

      case PoolType pp when actual is PoolType ap:
        return Unify(pp.ElementType, ap.ElementType, siteSpan, out conflict, allowIntToFloat: false);

      case HandleType ph when actual is HandleType ah:
        return Unify(ph.ElementType, ah.ElementType, siteSpan, out conflict, allowIntToFloat: false);

      case TupleType pt when actual is TupleType at && pt.Fields.Length == at.Fields.Length:
        for (int i = 0; i < pt.Fields.Length; i++) {
          if (!string.Equals(pt.Fields[i].Label, at.Fields[i].Label, StringComparison.Ordinal)) {
            conflict = new UnifyConflict(null, ZonkPartial(pattern), ZonkPartial(actual), siteSpan, IsOccursCheck: false);
            return false;
          }

          if (!Unify(pt.Fields[i].Type, at.Fields[i].Type, siteSpan, out conflict, allowIntToFloat: false)) {
            return false;
          }
        }
        return true;

      case FunctionType pf when actual is FunctionType af && pf.Parameters.Length == af.Parameters.Length:
        for (int i = 0; i < pf.Parameters.Length; i++) {
          if (pf.Parameters[i].IsMutable != af.Parameters[i].IsMutable) {
            conflict = new UnifyConflict(null, ZonkPartial(pattern), ZonkPartial(actual), siteSpan, IsOccursCheck: false);
            return false;
          }

          if (!Unify(pf.Parameters[i].Type, af.Parameters[i].Type, siteSpan, out conflict, allowIntToFloat: false)) {
            return false;
          }
        }
        return Unify(pf.ReturnType, af.ReturnType, siteSpan, out conflict, allowIntToFloat: false);

      case StructType pst when actual is StructType ast
          && ReferenceEquals(pst.Definition, ast.Definition)
          && pst.TypeArguments.Length == ast.TypeArguments.Length:
        for (int i = 0; i < pst.TypeArguments.Length; i++) {
          if (!Unify(pst.TypeArguments[i], ast.TypeArguments[i], siteSpan, out conflict, allowIntToFloat: false)) {
            return false;
          }
        }
        return true;

      case UnionType pu when actual is UnionType au
          && ReferenceEquals(pu.Definition, au.Definition)
          && pu.TypeArguments.Length == au.TypeArguments.Length:
        for (int i = 0; i < pu.TypeArguments.Length; i++) {
          if (!Unify(pu.TypeArguments[i], au.TypeArguments[i], siteSpan, out conflict, allowIntToFloat: false)) {
            return false;
          }
        }
        return true;

      case InterfaceType pi when actual is InterfaceType ai
          && ReferenceEquals(pi.Definition, ai.Definition)
          && pi.TypeArguments.Length == ai.TypeArguments.Length:
        for (int i = 0; i < pi.TypeArguments.Length; i++) {
          if (!Unify(pi.TypeArguments[i], ai.TypeArguments[i], siteSpan, out conflict, allowIntToFloat: false)) {
            return false;
          }
        }
        return true;
    }

    conflict = new UnifyConflict(null, ZonkPartial(pattern), ZonkPartial(actual), siteSpan, IsOccursCheck: false);
    return false;
  }

  /// <summary>
  /// Unifies a contextual expected type into ONLY still-unbound type variables of
  /// <paramref name="pattern"/> without overwriting or conflicting with already-bound variables.
  /// </summary>
  public void BindUnboundFromExpected(TypeSymbol pattern, TypeSymbol expected, SourceSpan siteSpan) {
    if (pattern.IsError || expected.IsError || expected.IsNever || expected is PendingReturnType) {
      return;
    }

    if (pattern is TypeVariableType tv) {
      TypeSymbol resolved = Resolve(tv);
      if (resolved is TypeVariableType rootVar) {
        if (!OccursIn(rootVar, expected)) {
          VarSlot slot = _slots[rootVar.Id];
          slot.Bound = expected;
          slot.FirstBindingSpan = siteSpan;
          slot.FirstBindingType = expected;
        }
      } else {
        BindUnboundFromExpected(resolved, expected, siteSpan);
      }
      return;
    }

    switch (pattern) {
      case VectorType pv when expected is VectorType ev:
        BindUnboundFromExpected(pv.ElementType, ev.ElementType, siteSpan);
        break;

      case MapType pm when expected is MapType em:
        BindUnboundFromExpected(pm.KeyType, em.KeyType, siteSpan);
        BindUnboundFromExpected(pm.ValueType, em.ValueType, siteSpan);
        break;

      case SetType ps when expected is SetType es:
        BindUnboundFromExpected(ps.ElementType, es.ElementType, siteSpan);
        break;

      case PoolType pp when expected is PoolType ep:
        BindUnboundFromExpected(pp.ElementType, ep.ElementType, siteSpan);
        break;

      case HandleType ph when expected is HandleType eh:
        BindUnboundFromExpected(ph.ElementType, eh.ElementType, siteSpan);
        break;

      case TupleType pt when expected is TupleType et && pt.Fields.Length == et.Fields.Length:
        for (int i = 0; i < pt.Fields.Length; i++) {
          if (string.Equals(pt.Fields[i].Label, et.Fields[i].Label, StringComparison.Ordinal)) {
            BindUnboundFromExpected(pt.Fields[i].Type, et.Fields[i].Type, siteSpan);
          }
        }
        break;

      case FunctionType pf when expected is FunctionType ef && pf.Parameters.Length == ef.Parameters.Length:
        bool mutsMatch = true;
        for (int i = 0; i < pf.Parameters.Length; i++) {
          if (pf.Parameters[i].IsMutable != ef.Parameters[i].IsMutable) {
            mutsMatch = false;
            break;
          }
        }
        if (mutsMatch) {
          for (int i = 0; i < pf.Parameters.Length; i++) {
            BindUnboundFromExpected(pf.Parameters[i].Type, ef.Parameters[i].Type, siteSpan);
          }
          BindUnboundFromExpected(pf.ReturnType, ef.ReturnType, siteSpan);
        }
        break;

      case StructType pst when expected is StructType est
          && ReferenceEquals(pst.Definition, est.Definition)
          && pst.TypeArguments.Length == est.TypeArguments.Length:
        for (int i = 0; i < pst.TypeArguments.Length; i++) {
          BindUnboundFromExpected(pst.TypeArguments[i], est.TypeArguments[i], siteSpan);
        }
        break;

      case UnionType pu when expected is UnionType eu
          && ReferenceEquals(pu.Definition, eu.Definition)
          && pu.TypeArguments.Length == eu.TypeArguments.Length:
        for (int i = 0; i < pu.TypeArguments.Length; i++) {
          BindUnboundFromExpected(pu.TypeArguments[i], eu.TypeArguments[i], siteSpan);
        }
        break;

      case InterfaceType pi when expected is InterfaceType ei
          && ReferenceEquals(pi.Definition, ei.Definition)
          && pi.TypeArguments.Length == ei.TypeArguments.Length:
        for (int i = 0; i < pi.TypeArguments.Length; i++) {
          BindUnboundFromExpected(pi.TypeArguments[i], ei.TypeArguments[i], siteSpan);
        }
        break;
    }
  }

  /// <summary>
  /// Replaces all solved type variables, turning any remaining unbound <see cref="TypeVariableType"/>
  /// into <see cref="ErrorType"/> so no type variable can escape into <see cref="SemanticModel"/>.
  /// </summary>
  public TypeSymbol Zonk(TypeSymbol type) => ZonkCore(type, unboundToError: true);

  /// <summary>Replaces solved type variables while keeping still-unbound variables intact for Pass 2.</summary>
  public TypeSymbol ZonkPartial(TypeSymbol type) => ZonkCore(type, unboundToError: false);

  public static bool ContainsTypeVariable(TypeSymbol type) => type switch {
    TypeVariableType => true,
    VectorType v => ContainsTypeVariable(v.ElementType),
    MapType m => ContainsTypeVariable(m.KeyType) || ContainsTypeVariable(m.ValueType),
    SetType s => ContainsTypeVariable(s.ElementType),
    PoolType p => ContainsTypeVariable(p.ElementType),
    HandleType h => ContainsTypeVariable(h.ElementType),
    TupleType t => t.Fields.Any(static f => ContainsTypeVariable(f.Type)),
    FunctionType f => ContainsTypeVariable(f.ReturnType) || f.Parameters.Any(static p => ContainsTypeVariable(p.Type)),
    StructType st => st.TypeArguments.Any(static a => ContainsTypeVariable(a)),
    UnionType ut => ut.TypeArguments.Any(static a => ContainsTypeVariable(a)),
    InterfaceType it => it.TypeArguments.Any(static a => ContainsTypeVariable(a)),
    _ => false,
  };

  public static bool IsConcreteType(TypeSymbol type) => type switch {
    ErrorType or TypeVariableType or TypeParameterType or PendingReturnType => false,
    VectorType v => IsConcreteType(v.ElementType),
    MapType m => IsConcreteType(m.KeyType) && IsConcreteType(m.ValueType),
    SetType s => IsConcreteType(s.ElementType),
    PoolType p => IsConcreteType(p.ElementType),
    HandleType h => IsConcreteType(h.ElementType),
    TupleType t => t.Fields.All(static f => IsConcreteType(f.Type)),
    FunctionType f => IsConcreteType(f.ReturnType) && f.Parameters.All(static p => IsConcreteType(p.Type)),
    StructType st => st.TypeArguments.All(static a => IsConcreteType(a)),
    UnionType ut => ut.TypeArguments.All(static a => IsConcreteType(a)),
    InterfaceType it => it.TypeArguments.All(static a => IsConcreteType(a)),
    _ => true,
  };

  private bool UnifyWithVariable(
      TypeVariableType variable,
      TypeSymbol other,
      SourceSpan siteSpan,
      out UnifyConflict? conflict,
      bool allowIntToFloat) {
    conflict = null;
    TypeSymbol resolvedVar = Resolve(variable);
    TypeSymbol resolvedOther = Resolve(other);

    if (ReferenceEquals(resolvedVar, resolvedOther)) {
      return true;
    }

    if (resolvedVar is TypeVariableType rootVar) {
      if (resolvedOther is TypeVariableType otherRoot) {
        _slots[rootVar.Id].Bound = otherRoot;
        return true;
      }

      if (OccursIn(rootVar, resolvedOther)) {
        conflict = new UnifyConflict(
            _slots[rootVar.Id].Parameter,
            rootVar,
            ZonkPartial(resolvedOther),
            siteSpan,
            IsOccursCheck: true);
        return false;
      }

      VarSlot slot = _slots[rootVar.Id];
      slot.Bound = resolvedOther;
      slot.FirstBindingSpan = siteSpan;
      slot.FirstBindingType = resolvedOther;
      return true;
    }

    // `variable` is already bound to a concrete/structured type.
    if (Unify(resolvedVar, resolvedOther, siteSpan, out UnifyConflict? innerConflict, allowIntToFloat)) {
      return true;
    }

    // Report GEN-E1 blaming this type parameter when two incompatible bindings collide.
    VarSlot originalSlot = _slots[variable.Id];
    conflict = innerConflict is { Parameter: not null }
        ? innerConflict
        : new UnifyConflict(
            originalSlot.Parameter,
            ZonkPartial(originalSlot.FirstBindingType ?? resolvedVar),
            ZonkPartial(resolvedOther),
            originalSlot.FirstBindingSpan,
            IsOccursCheck: false);
    return false;
  }

  private bool OccursIn(TypeVariableType needle, TypeSymbol haystack) {
    TypeSymbol resolved = Resolve(haystack);
    if (ReferenceEquals(resolved, needle)) {
      return true;
    }

    return resolved switch {
      VectorType v => OccursIn(needle, v.ElementType),
      MapType m => OccursIn(needle, m.KeyType) || OccursIn(needle, m.ValueType),
      SetType s => OccursIn(needle, s.ElementType),
      PoolType p => OccursIn(needle, p.ElementType),
      HandleType h => OccursIn(needle, h.ElementType),
      TupleType t => t.Fields.Any(f => OccursIn(needle, f.Type)),
      FunctionType f => OccursIn(needle, f.ReturnType) || f.Parameters.Any(p => OccursIn(needle, p.Type)),
      StructType st => st.TypeArguments.Any(a => OccursIn(needle, a)),
      UnionType ut => ut.TypeArguments.Any(a => OccursIn(needle, a)),
      InterfaceType it => it.TypeArguments.Any(a => OccursIn(needle, a)),
      _ => false,
    };
  }

  private TypeSymbol ZonkCore(TypeSymbol type, bool unboundToError) {
    if (type is TypeVariableType tv) {
      TypeSymbol resolved = Resolve(tv);
      if (resolved is TypeVariableType) {
        return unboundToError ? _types.Error : resolved;
      }

      return ZonkCore(resolved, unboundToError);
    }

    return type switch {
      VectorType v => _types.GetVector(ZonkCore(v.ElementType, unboundToError)),
      MapType m => _types.GetMap(ZonkCore(m.KeyType, unboundToError), ZonkCore(m.ValueType, unboundToError)),
      SetType s => _types.GetSet(ZonkCore(s.ElementType, unboundToError)),
      PoolType p => _types.GetPool(ZonkCore(p.ElementType, unboundToError)),
      HandleType h => _types.GetHandle(ZonkCore(h.ElementType, unboundToError)),
      TupleType t => _types.GetTuple([.. t.Fields.Select(f => new TupleField(f.Label, ZonkCore(f.Type, unboundToError)))]),
      FunctionType f => _types.GetFunction(
          [.. f.Parameters.Select(p => new FunctionParameter(ZonkCore(p.Type, unboundToError), p.IsMutable))],
          ZonkCore(f.ReturnType, unboundToError)),
      StructType st when !st.TypeArguments.IsEmpty => _types.GetStruct(
          st.Definition,
          [.. st.TypeArguments.Select(a => ZonkCore(a, unboundToError))]),
      UnionType ut when !ut.TypeArguments.IsEmpty => _types.GetUnion(
          ut.Definition,
          [.. ut.TypeArguments.Select(a => ZonkCore(a, unboundToError))]),
      InterfaceType it when !it.TypeArguments.IsEmpty => _types.GetInterface(
          it.Definition,
          [.. it.TypeArguments.Select(a => ZonkCore(a, unboundToError))]),
      _ => type,
    };
  }
}
