using System.Collections.Immutable;

using Pino.Compiler.Syntax;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  /// <summary>
  /// VEC-1 / VEC-18 / D-168: the element type is inferred from the elements, and an <c>int</c>
  /// element widens to <c>float</c> when a <c>float</c> element is present.
  /// </summary>
  private TypeSymbol SynthesizeVectorLiteral(VectorLiteralExpressionSyntax vector) {
    TypeSymbol elementType = SynthesizeElements(vector.Elements, "vector element");
    return elementType.IsError ? _types.Error : _types.GetVector(elementType);
  }

  /// <summary>
  /// Synthesizes <paramref name="elements"/>, joins them into one element type (widening
  /// <c>int</c> to <c>float</c>) and records <c>IntToFloat</c> on every <c>int</c> element of a
  /// <c>float</c> sequence. Returns <see cref="ErrorType"/> when there is nothing to infer from.
  /// </summary>
  private TypeSymbol SynthesizeElements(ImmutableArray<ExpressionSyntax> elements, string description) {
    var types = new TypeSymbol[elements.Length];
    TypeSymbol? joined = null;
    TypeSymbol? pending = null;
    for (int i = 0; i < elements.Length; i++) {
      types[i] = Synthesize(elements[i]);
      TypeSymbol current = types[i];
      if (current is PendingReturnType) {
        pending ??= current;
        continue;
      }

      if (IsSilent(current)) {
        continue;
      }

      if (joined is null) {
        joined = current;
      } else if (ReferenceEquals(joined, _types.Int) && ReferenceEquals(current, _types.Float)) {
        joined = _types.Float;
      } else if (ReferenceEquals(joined, _types.Float) && ReferenceEquals(current, _types.Int)) {
        // already float
      } else if (!ReferenceEquals(joined, current)) {
        ReportCannotAssign(elements[i], current, joined, description);
      }
    }

    if (joined is null) {
      return pending ?? _types.Error;
    }

    if (ReferenceEquals(joined, _types.Float)) {
      for (int i = 0; i < elements.Length; i++) {
        if (ReferenceEquals(types[i], _types.Int)) {
          RecordIntToFloat(elements[i]);
        }
      }
    }

    return joined;
  }

  /// <summary>VEC-5 / VEC-7 / VEC-8 / VEC-9: <c>[]T</c>, <c>[]T { cap: n }</c>, <c>[]T { len: n, init: e, cap: m }</c>.</summary>
  private TypeSymbol SynthesizeVectorInitializer(VectorInitializerExpressionSyntax initializer) {
    TypeSymbol elementType = _session.ResolveType(
        _state,
        initializer.ElementType,
        _scope,
        TypePosition.CollectionElement);

    ExpressionSyntax? lenExpr = null;
    ExpressionSyntax? capExpr = null;
    foreach (VectorPropertySyntax property in initializer.Properties) {
      string name = IsMissing(property.NameToken) ? string.Empty : NameOf(property.NameToken);
      switch (name) {
        case "len":
          lenExpr ??= property.Value;
          Check(property.Value, _types.Int, "vector length");
          break;

        case "cap":
          capExpr ??= property.Value;
          Check(property.Value, _types.Int, "vector capacity");
          break;

        case "init": {
            // VEC-8 / D-002: `it` is the zero-based index while `init` is evaluated.
            var initScope = new Scope(ScopeKind.Synthetic, _scope, _scope.Owner);
            var it = new LocalSymbol(
                "it",
                _types.Int,
                isMutable: false,
                LocalKind.ImplicitIt,
                property.Id,
                property.NameToken.Span,
                _state.FilePath,
                _scope.Owner);
            initScope.TryDeclare(it, out _);
            _state.SetScope(property, initScope);
            using ScopeRestore restore = EnterScope(initScope);
            CheckVectorInit(property.Value, elementType);
            break;
          }

        default:
          Synthesize(property.Value);
          break;
      }
    }

    CheckVectorInitializerConstants(initializer, lenExpr, capExpr);
    return elementType.IsError ? _types.Error : _types.GetVector(elementType);
  }

  /// <summary>
  /// VEC-8: <c>init</c> is an expression evaluated with <c>it</c> bound to the index. The projects
  /// corpus also passes a function reference (<c>init: make_item</c>) whose signature is
  /// <c>fn (int) -> T</c>; the spec is silent on that form, so it is accepted when the signature
  /// matches exactly and any other mismatch is reported as usual.
  /// </summary>
  private void CheckVectorInit(ExpressionSyntax value, TypeSymbol elementType) {
    if (value is not IdentifierExpressionSyntax) {
      Check(value, elementType, "vector element");
      return;
    }

    TypeSymbol actual = Synthesize(value);
    if (actual is FunctionType { Parameters.Length: 1 } callback
        && !callback.Parameters[0].IsMutable
        && ReferenceEquals(callback.Parameters[0].Type, _types.Int)
        && (elementType.IsError || ReferenceEquals(callback.ReturnType, elementType))) {
      return;
    }

    if (!TryConvert(value, actual, elementType)) {
      ReportCannotAssign(value, actual, elementType, "vector element");
    }
  }

  /// <summary>MAP-1 / MAP-3 / MAP-E2 / MAP-E3 / MAP-12 / D-168: keys and values are checked against the declared types.</summary>
  private TypeSymbol SynthesizeMapLiteral(MapLiteralExpressionSyntax map) {
    TypeSymbol mapType = _session.ResolveType(_state, map.Type, _scope, TypePosition.VariableAnnotation);
    MapType? declared = mapType as MapType;

    foreach (MapEntrySyntax entry in map.Entries) {
      if (declared is not null) {
        Check(entry.Key, declared.KeyType, "map key");
        Check(entry.Value, declared.ValueType, "map value");
      } else {
        Synthesize(entry.Key);
        Synthesize(entry.Value);
      }
    }

    return declared is not null ? mapType : _types.Error;
  }

  /// <summary>SET-1 / SET-2 / SET-5 / D-168: elements are checked against the declared element type.</summary>
  private TypeSymbol SynthesizeSetLiteral(SetLiteralExpressionSyntax set) {
    TypeSymbol setType = _session.ResolveType(_state, set.Type, _scope, TypePosition.VariableAnnotation);
    SetType? declared = setType as SetType;

    foreach (ExpressionSyntax element in set.Elements) {
      if (declared is not null) {
        Check(element, declared.ElementType, "set element");
      } else {
        Synthesize(element);
      }
    }

    return declared is not null ? setType : _types.Error;
  }
}
