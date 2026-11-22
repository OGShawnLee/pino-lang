using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

internal sealed partial class CheckerSession {
  private void RunPhase2(ModuleCheckState state) {
    _cancellationToken.ThrowIfCancellationRequested();

    // 1. Register generic type parameters and resolve constraints (`T is Shape`).
    foreach ((FunctionDeclarationSyntax decl, FunctionSymbol sym, Scope scope) in state.Functions) {
      RegisterTypeParameters(state, decl.GenericDecorator, sym.TypeParameters, scope, "function", sym.Name);
    }

    foreach ((StructDeclarationSyntax decl, StructSymbol sym, Scope typeParamScope, _) in state.Structs) {
      RegisterTypeParameters(state, decl.GenericDecorator, sym.TypeParameters, typeParamScope, "struct", sym.Name);
    }

    foreach ((InterfaceDeclarationSyntax decl, InterfaceSymbol sym, Scope scope) in state.Interfaces) {
      RegisterTypeParameters(state, decl.GenericDecorator, sym.TypeParameters, scope, "interface", sym.Name);
    }

    foreach ((UnionDeclarationSyntax decl, UnionSymbol sym, Scope scope) in state.Unions) {
      RegisterTypeParameters(state, decl.GenericDecorator, sym.TypeParameters, scope, "union", sym.Name);
    }

    // 2. Resolve interfaces, enums, unions, structs (direct members), and functions.
    foreach ((InterfaceDeclarationSyntax decl, InterfaceSymbol sym, Scope scope) in state.Interfaces) {
      ResolveInterfaceMembers(state, decl, sym, scope);
    }

    foreach ((UnionDeclarationSyntax decl, UnionSymbol sym, Scope scope) in state.Unions) {
      ResolveUnionVariants(state, decl, sym, scope);
    }

    foreach ((StructDeclarationSyntax decl, StructSymbol sym, Scope typeParamScope, Scope enclosingScope) in state.Structs) {
      ResolveStructDirectMembers(state, decl, sym, typeParamScope, enclosingScope);
    }

    foreach ((FunctionDeclarationSyntax decl, FunctionSymbol sym, Scope scope) in state.Functions) {
      ResolveFunctionSignature(state, decl, sym, scope);
    }

    // 3. Flatten embedded structs (cycle detection, field collisions, method override) and build MemberScopes.
    FlattenModuleStructs(state);

    // 4. Mark Phase 2 complete. Deferred Phase 2 generic constraint checks are flushed by BodyChecker
    //    after unannotated struct method return types are resolved.
    state.IsPhase2Complete = true;
  }

  private void RegisterTypeParameters(
      ModuleCheckState state,
      GenericDecoratorSyntax? decorator,
      ImmutableArray<TypeParameterSymbol> typeParameters,
      Scope declScope,
      string ownerKind,
      string ownerName) {
    if (decorator is null || typeParameters.IsEmpty) {
      return;
    }

    Scope? parentScope = declScope.Parent;
    foreach (TypeParameterSymbol tp in typeParameters) {
      // GEN-E4: type parameter name cannot conflict with an existing defined type name.
      if (IsExistingDefinedType(tp.Name, parentScope)) {
        state.Diagnostics.ReportError(
            DiagnosticCodes.GenericParameterNameConflict,
            tp.NameSpan,
            $"Generic parameter '{tp.Name}' in {ownerKind} '{ownerName}' conflicts with an existing defined type name.");
      }

      if (!declScope.TryDeclare(tp, out Symbol? existing)) {
        ReportDuplicate(state, tp.Name, tp.NameSpan, existing!);
      }
    }

    // Resolve constraints (`T is Interface`) after all type parameters of this declaration are in declScope.
    int tpIndex = 0;
    foreach (GenericParameterSyntax gp in decorator.Parameters) {
      if (IsMissingToken(gp.NameToken)) {
        continue;
      }

      if (tpIndex >= typeParameters.Length) {
        break;
      }

      TypeParameterSymbol tp = typeParameters[tpIndex++];
      if (gp.ConstraintType is not null) {
        TypeSymbol constraintType = ResolveType(state, gp.ConstraintType, declScope, TypePosition.GenericConstraint);
        if (constraintType is InterfaceType ifaceType) {
          tp.Constraint = ifaceType;
        } else if (!constraintType.IsError) {
          state.Diagnostics.ReportError(
              DiagnosticCodes.GenericConstraintNotSatisfied,
              gp.ConstraintType.Span,
              $"Generic constraint '{constraintType.DisplayName}' for parameter '{tp.Name}' must be an interface.");
        }
      }
    }
  }

  private static bool IsExistingDefinedType(string name, Scope? scope) {
    if (name is "bool" or "int" or "float" or "rune" or "string" or "regex" or "void" or "Pool" or "Handle" or "Option" or "Result") {
      return true;
    }

    if (scope is not null && scope.TryLookup(name, out Symbol? sym)) {
      return sym is StructSymbol or InterfaceSymbol or EnumSymbol or UnionSymbol;
    }

    return false;
  }

  private void ResolveFunctionSignature(
      ModuleCheckState state,
      FunctionDeclarationSyntax decl,
      FunctionSymbol sym,
      Scope fnScope) {
    sym.Parameters = ResolveParameterList(
        state,
        decl.Parameters,
        fnScope,
        sym,
        enclosingStruct: null,
        currentMethodName: null);

    if (decl.ReturnType is not null) {
      sym.ReturnType = ResolveType(state, decl.ReturnType, fnScope, TypePosition.FunctionReturn);
    }
  }

  private void ResolveInterfaceMembers(
      ModuleCheckState state,
      InterfaceDeclarationSyntax decl,
      InterfaceSymbol sym,
      Scope ifaceScope) {
    var properties = ImmutableArray.CreateBuilder<InterfaceProperty>();
    var methods = ImmutableArray.CreateBuilder<MethodSymbol>();
    var seenNames = new Dictionary<string, (SourceSpan Span, string Name)>(StringComparer.Ordinal);

    foreach (SyntaxNode member in decl.Members) {
      if (member is InterfacePropertySyntax propSyntax) {
        if (IsMissingToken(propSyntax.NameToken)) {
          continue;
        }

        string propName = state.Lex.GetText(propSyntax.NameToken).ToString();
        TypeSymbol propType = ResolveType(state, propSyntax.Type, ifaceScope, TypePosition.InterfacePropertyType);
        state.SetNodeType(propSyntax, propType);

        if (seenNames.TryGetValue(propName, out (SourceSpan Span, string Name) existing)) {
          state.Diagnostics.ReportError(
              DiagnosticCodes.DuplicateDeclaration,
              propSyntax.NameToken.Span,
              $"Identifier '{propName}' is already declared in interface '{sym.Name}'.",
              [new RelatedLocation(existing.Span, $"'{propName}' was previously declared here", state.FilePath)]);
        } else {
          seenNames.Add(propName, (propSyntax.NameToken.Span, propName));
          properties.Add(new InterfaceProperty(
              propName,
              propType,
              propSyntax.Id,
              propSyntax.NameToken.Span,
              IsMutable: propSyntax.MutKeyword is not null));
        }
      } else if (member is InterfaceMethodSyntax methodSyntax) {
        if (IsMissingToken(methodSyntax.NameToken)) {
          continue;
        }

        string methodName = state.Lex.GetText(methodSyntax.NameToken).ToString();
        var methodSymbol = new MethodSymbol(
            methodName,
            SymbolVisibility.Public,
            isStatic: false,
            sym,
            [],
            [],
            VoidType.Instance,
            ReturnInferenceState.Explicit,
            methodSyntax.Id,
            methodSyntax.NameToken.Span,
            state.FilePath,
            isMutating: methodSyntax.MutKeyword is not null);

        methodSymbol.Parameters = ResolveParameterList(
            state,
            methodSyntax.Parameters,
            ifaceScope,
            methodSymbol,
            enclosingStruct: null,
            currentMethodName: null);

        methodSymbol.ReturnType = methodSyntax.ReturnType is not null
            ? ResolveType(state, methodSyntax.ReturnType, ifaceScope, TypePosition.FunctionReturn)
            : VoidType.Instance;

        if (seenNames.TryGetValue(methodName, out (SourceSpan Span, string Name) existing)) {
          state.Diagnostics.ReportError(
              DiagnosticCodes.DuplicateDeclaration,
              methodSyntax.NameToken.Span,
              $"Identifier '{methodName}' is already declared in interface '{sym.Name}'.",
              [new RelatedLocation(existing.Span, $"'{methodName}' was previously declared here", state.FilePath)]);
        } else {
          seenNames.Add(methodName, (methodSyntax.NameToken.Span, methodName));
          methods.Add(methodSymbol);
        }

        state.SetDeclaredSymbol(methodSyntax, methodSymbol);
      }
    }

    sym.Properties = properties.ToImmutable();
    sym.Methods = methods.ToImmutable();
  }

  private void ResolveUnionVariants(
      ModuleCheckState state,
      UnionDeclarationSyntax decl,
      UnionSymbol sym,
      Scope unionScope) {
    int variantIdx = 0;
    foreach (SyntaxNode node in decl.Variants) {
      if (node is not UnionVariantSyntax variantSyntax || IsMissingToken(variantSyntax.NameToken)) {
        continue;
      }

      if (state.DeclaredSymbols[variantSyntax.Id] is not UnionVariantSymbol variantSymbol) {
        continue;
      }

      if (variantIdx < sym.Variants.Length && ReferenceEquals(sym.Variants[variantIdx], variantSymbol)) {
        variantIdx++;
      }

      var fields = ImmutableArray.CreateBuilder<UnionVariantField>(variantSyntax.Fields.Length);
      var seenFieldNames = new Dictionary<string, (SourceSpan Span, string Name)>(StringComparer.Ordinal);

      for (int i = 0; i < variantSyntax.Fields.Length; i++) {
        UnionVariantFieldSyntax fieldSyntax = variantSyntax.Fields[i];
        string? fieldName = fieldSyntax.NameToken is { } nameTok && !IsMissingToken(nameTok)
            ? state.Lex.GetText(nameTok).ToString()
            : null;

        TypeSymbol fieldType = ResolveType(state, fieldSyntax.Type, unionScope, TypePosition.UnionPayload);
        state.SetNodeType(fieldSyntax, fieldType);

        if (fieldName is not null) {
          if (seenFieldNames.TryGetValue(fieldName, out (SourceSpan Span, string Name) existing)) {
            state.Diagnostics.ReportError(
                DiagnosticCodes.DuplicateDeclaration,
                fieldSyntax.NameToken!.Value.Span,
                $"Identifier '{fieldName}' is already declared in variant '{sym.Name}::{variantSymbol.Name}'.",
                [new RelatedLocation(existing.Span, $"'{fieldName}' was previously declared here", state.FilePath)]);
          } else {
            seenFieldNames.Add(fieldName, (fieldSyntax.NameToken!.Value.Span, fieldName));
          }
        }

        fields.Add(new UnionVariantField(
            fieldName,
            fieldType,
            i,
            fieldSyntax.Id,
            fieldSyntax.NameToken?.Span ?? fieldSyntax.Span));
      }

      variantSymbol.Fields = fields.ToImmutable();
    }
  }

  private void ResolveStructDirectMembers(
      ModuleCheckState state,
      StructDeclarationSyntax decl,
      StructSymbol sym,
      Scope typeParamScope,
      Scope enclosingScope) {
    var fields = ImmutableArray.CreateBuilder<FieldSymbol>();
    var embedded = ImmutableArray.CreateBuilder<StructSymbol>();
    var methods = ImmutableArray.CreateBuilder<MethodSymbol>();
    var directMembers = new Dictionary<string, Symbol>(StringComparer.Ordinal);
    var seenEmbedded = new HashSet<StructSymbol>(ReferenceEqualityComparer.Instance);

    int fieldOrdinal = 0;

    foreach (SyntaxNode member in decl.Members) {
      switch (member) {
        case FieldDeclarationSyntax fieldSyntax: {
            if (IsMissingToken(fieldSyntax.NameToken)) {
              break;
            }

            string fieldName = state.Lex.GetText(fieldSyntax.NameToken).ToString();
            SymbolVisibility vis = GetVisibility(fieldSyntax.Visibility);
            TypeSymbol fieldType = ResolveType(state, fieldSyntax.Type, typeParamScope, TypePosition.FieldType);
            state.SetNodeType(fieldSyntax, fieldType);

            var fieldSym = new FieldSymbol(
                fieldName,
                fieldType,
                vis,
                fieldOrdinal++,
                sym,
                fieldSyntax.Id,
                fieldSyntax.NameToken.Span,
                state.FilePath,
                isMutable: fieldSyntax.MutKeyword is not null);

            if (directMembers.TryGetValue(fieldName, out Symbol? existing)) {
              ReportDuplicate(state, fieldName, fieldSyntax.NameToken.Span, existing);
            } else {
              directMembers.Add(fieldName, fieldSym);
              fields.Add(fieldSym);
            }

            state.SetDeclaredSymbol(fieldSyntax, fieldSym);
            break;
          }

        case EmbeddedStructSyntax embedSyntax: {
            if (IsMissingToken(embedSyntax.NameToken)) {
              break;
            }

            string embedName = state.Lex.GetText(embedSyntax.NameToken).ToString();
            if (!enclosingScope.TryLookup(embedName, out Symbol? found)) {
              state.Diagnostics.ReportError(
                  DiagnosticCodes.UnknownType,
                  embedSyntax.NameToken.Span,
                  $"Unknown struct '{embedName}' embedded in '{sym.Name}'.");
              break;
            }

            if (found is not StructSymbol embeddedStruct) {
              state.Diagnostics.ReportError(
                  DiagnosticCodes.TypeMismatch,
                  embedSyntax.NameToken.Span,
                  $"Only struct types can be embedded; '{embedName}' is not a struct.");
              break;
            }

            if (!embeddedStruct.TypeParameters.IsEmpty) {
              state.Diagnostics.ReportError(
                  DiagnosticCodes.CannotInferGenericArguments,
                  embedSyntax.NameToken.Span,
                  $"Generic struct '{embedName}' requires type arguments and cannot be embedded directly.");
              break;
            }

            state.SetReferencedSymbol(embedSyntax, embeddedStruct);
            if (!seenEmbedded.Add(embeddedStruct)) {
              state.Diagnostics.ReportError(
                  DiagnosticCodes.DuplicateDeclaration,
                  embedSyntax.NameToken.Span,
                  $"Struct '{embedName}' is already embedded in '{sym.Name}'.");
            } else {
              embedded.Add(embeddedStruct);
            }
            break;
          }

        case MethodDeclarationSyntax methodSyntax: {
            if (IsMissingToken(methodSyntax.NameToken)) {
              break;
            }

            string methodName = state.Lex.GetText(methodSyntax.NameToken).ToString();
            SymbolVisibility vis = GetVisibility(methodSyntax.Visibility);
            bool isStatic = methodSyntax.StaticKeyword is not null;
            ImmutableArray<TypeParameterSymbol> methodTypeParams =
                CollectGenericTypeParameters(state, methodSyntax.GenericDecorator);

            var methodSym = new MethodSymbol(
                methodName,
                vis,
                isStatic,
                sym,
                methodTypeParams,
                [],
                VoidType.Instance,
                methodSyntax.ReturnType is not null ? ReturnInferenceState.Explicit : ReturnInferenceState.Unchecked,
                methodSyntax.Id,
                methodSyntax.NameToken.Span,
                state.FilePath,
                isMutating: !isStatic && methodSyntax.MutKeyword is not null);

            foreach (TypeParameterSymbol tp in methodTypeParams) {
              tp.Owner = methodSym;
            }

            // Static methods cannot see instance type parameters (GEN-E9) or instance members (STRUCT-E3/E4).
            Scope baseScope = isStatic ? enclosingScope : typeParamScope;
            var methodScope = new Scope(ScopeKind.Method, baseScope, methodSym);
            state.SetScope(methodSyntax, methodScope);

            // Re-parent the method's body scope created in Phase 1 so it sees methodScope.
            if (state.ScopesByBlockId[methodSyntax.Body.Id] is { } bodyScope && !ReferenceEquals(bodyScope.Parent, methodScope)) {
              ReparentMethodBodyScope(state, methodSyntax.Body, methodScope);
            }

            RegisterTypeParameters(state, methodSyntax.GenericDecorator, methodTypeParams, methodScope, "method", methodName);

            StructSymbol? staticInstanceOwner = isStatic ? sym : null;
            methodSym.Parameters = ResolveParameterList(
                state,
                methodSyntax.Parameters,
                methodScope,
                methodSym,
                staticInstanceOwner,
                methodName);

            if (methodSyntax.ReturnType is not null) {
              methodSym.ReturnType = ResolveType(
                  state,
                  methodSyntax.ReturnType,
                  methodScope,
                  TypePosition.FunctionReturn,
                  staticInstanceOwner,
                  methodName);
            }

            if (directMembers.TryGetValue(methodName, out Symbol? existing)) {
              ReportDuplicate(state, methodName, methodSyntax.NameToken.Span, existing);
            } else {
              directMembers.Add(methodName, methodSym);
              methods.Add(methodSym);
            }

            state.SetDeclaredSymbol(methodSyntax, methodSym);
            break;
          }
      }
    }

    sym.Fields = fields.ToImmutable();
    sym.AllFields = sym.Fields;
    sym.EmbeddedStructs = embedded.ToImmutable();
    sym.Methods = methods.ToImmutable();
  }

  private void ReparentMethodBodyScope(ModuleCheckState state, BlockStatementSyntax body, Scope methodScope) {
    Scope? oldBodyScope = state.ScopesByBlockId[body.Id];
    if (oldBodyScope is null) {
      return;
    }

    var newBodyScope = new Scope(ScopeKind.Block, methodScope, methodScope.Owner);
    foreach (Symbol hoisted in oldBodyScope.HoistedDeclarations) {
      newBodyScope.TryDeclareHoisted(hoisted, out _);
    }

    state.SetScope(body, newBodyScope);
    // Any direct nested block scopes created in Phase 1 pointing to oldBodyScope? Update their parent by re-walking items.
    foreach (SyntaxNode item in body.Items) {
      UpdateDirectChildScopeParent(state, item, oldBodyScope, newBodyScope);
    }
  }

  private void UpdateDirectChildScopeParent(ModuleCheckState state, SyntaxNode node, Scope oldParent, Scope newParent) {
    // Re-parent scopes directly attached to immediate statements inside the method body.
    if ((uint) node.Id < (uint) state.ScopesByBlockId.Length
        && state.ScopesByBlockId[node.Id] is { } existingScope
        && ReferenceEquals(existingScope.Parent, oldParent)) {
      var replacement = new Scope(existingScope.Kind, newParent, existingScope.Owner);
      foreach (Symbol hoisted in existingScope.HoistedDeclarations) {
        replacement.TryDeclareHoisted(hoisted, out _);
      }
      state.SetScope(node, replacement);
      oldParent = existingScope;
      newParent = replacement;
    }

    foreach (SyntaxElement elem in node.GetChildren()) {
      if (elem.IsNode) {
        UpdateDirectChildScopeParent(state, elem.AsNode(), oldParent, newParent);
      }
    }
  }

  private void FlattenModuleStructs(ModuleCheckState state) {
    var visitState = new Dictionary<StructSymbol, byte>(ReferenceEqualityComparer.Instance);
    var structBySym = new Dictionary<StructSymbol, (StructDeclarationSyntax Syntax, Scope TypeParamScope)>(ReferenceEqualityComparer.Instance);

    foreach ((StructDeclarationSyntax syntax, StructSymbol sym, Scope typeParamScope, _) in state.Structs) {
      structBySym[sym] = (syntax, typeParamScope);
    }

    foreach ((StructDeclarationSyntax syntax, StructSymbol sym, Scope typeParamScope, _) in state.Structs) {
      FlattenStruct(state, sym, visitState, structBySym);
      BuildStructMemberScope(state, syntax, sym, typeParamScope);
    }

    ReportRecursiveStructValues(state);
  }

  /// <summary>
  /// STRUCT-E19 / D-186: a struct that contains itself by value, directly or through other structs, has infinite size.
  /// Only direct struct-typed fields, embedded structs, and tuples are followed; collections, <c>Option</c>, unions,
  /// interfaces, handles, and functions break the chain. Generic structs are checked on their own declaration only
  /// (for example <c>struct Box[T] { x Box[T] }</c>); containment through another generic's type argument is not followed.
  /// </summary>
  private static void ReportRecursiveStructValues(ModuleCheckState state) {
    var local = new HashSet<StructSymbol>(ReferenceEqualityComparer.Instance);
    foreach ((_, StructSymbol sym, _, _) in state.Structs) {
      local.Add(sym);
    }

    // Value-containment edges per struct, remembering which field (if any) holds the edge.
    var edges = new Dictionary<StructSymbol, List<(StructSymbol Target, FieldSymbol? Field)>>(ReferenceEqualityComparer.Instance);
    foreach ((_, StructSymbol sym, _, _) in state.Structs) {
      var list = new List<(StructSymbol, FieldSymbol?)>();
      foreach (FieldSymbol field in sym.Fields) {
        foreach (StructSymbol target in ValueContainedStructs(field.Type)) {
          if (local.Contains(target)) {
            list.Add((target, field));
          }
        }
      }
      foreach (StructSymbol embedded in sym.EmbeddedStructs) {
        if (local.Contains(embedded)) {
          list.Add((embedded, null));
        }
      }
      edges[sym] = list;
    }

    // Tarjan strongly connected components.
    int index = 0;
    var indices = new Dictionary<StructSymbol, int>(ReferenceEqualityComparer.Instance);
    var lowLinks = new Dictionary<StructSymbol, int>(ReferenceEqualityComparer.Instance);
    var onStack = new HashSet<StructSymbol>(ReferenceEqualityComparer.Instance);
    var stack = new Stack<StructSymbol>();
    var components = new List<List<StructSymbol>>();

    void Visit(StructSymbol v) {
      indices[v] = lowLinks[v] = index++;
      stack.Push(v);
      onStack.Add(v);
      foreach ((StructSymbol w, _) in edges[v]) {
        if (!indices.ContainsKey(w)) {
          Visit(w);
          lowLinks[v] = Math.Min(lowLinks[v], lowLinks[w]);
        } else if (onStack.Contains(w)) {
          lowLinks[v] = Math.Min(lowLinks[v], indices[w]);
        }
      }
      if (lowLinks[v] == indices[v]) {
        var component = new List<StructSymbol>();
        StructSymbol w;
        do {
          w = stack.Pop();
          onStack.Remove(w);
          component.Add(w);
        } while (!ReferenceEquals(w, v));
        components.Add(component);
      }
    }

    foreach ((_, StructSymbol sym, _, _) in state.Structs) {
      if (!indices.ContainsKey(sym)) {
        Visit(sym);
      }
    }

    foreach (List<StructSymbol> component in components) {
      var members = new HashSet<StructSymbol>(component, ReferenceEqualityComparer.Instance);
      foreach (StructSymbol sym in component) {
        bool selfLoop = edges[sym].Any(e => ReferenceEquals(e.Target, sym));
        if (component.Count == 1 && !selfLoop) {
          continue;
        }

        // Pure embedding cycles are already reported as cyclic embedding; only report cycles that use a field.
        bool usesField = edges.Where(kv => members.Contains(kv.Key))
            .SelectMany(static kv => kv.Value)
            .Any(e => e.Field is not null && members.Contains(e.Target));
        if (!usesField) {
          continue;
        }

        // The struct this one is first reached through, and a field somewhere in the component that points back to it.
        StructSymbol? through = null;
        if (!selfLoop) {
          through = edges[sym].First(e => members.Contains(e.Target) && !ReferenceEquals(e.Target, sym)).Target;
        }

        FieldSymbol? backField = null;
        foreach (StructSymbol holder in component.OrderBy(static s => s.NameSpan.Start)) {
          backField = edges[holder].FirstOrDefault(e => ReferenceEquals(e.Target, sym) && e.Field is not null).Field;
          if (backField is not null) {
            break;
          }
        }

        string message = through is null
            ? $"Struct '{sym.Name}' cannot contain itself directly because it would have infinite size."
            : $"Struct '{sym.Name}' cannot contain itself, even through '{through.Name}', because it would have infinite size.";
        ImmutableArray<RelatedLocation> related = backField is null
            ? default
            : [new RelatedLocation(backField.NameSpan, $"field '{backField.Name}' refers back to '{sym.Name}' here", backField.FilePath)];
        state.Diagnostics.ReportError(
            DiagnosticCodes.RecursiveStructValue,
            sym.NameSpan,
            message,
            related,
            help: $"wrap the field in 'Option[{sym.Name}]', store it in '[]{sym.Name}' or a recursive union, or use 'Handle[{sym.Name}]' with a 'Pool[{sym.Name}]'");
      }
    }
  }

  private static IEnumerable<StructSymbol> ValueContainedStructs(TypeSymbol type) {
    switch (type) {
      case StructType structType:
        yield return structType.Definition;
        break;
      case TupleType tuple:
        foreach (TupleField field in tuple.Fields) {
          foreach (StructSymbol nested in ValueContainedStructs(field.Type)) {
            yield return nested;
          }
        }
        break;
    }
  }

  private void FlattenStruct(
      ModuleCheckState state,
      StructSymbol sym,
      Dictionary<StructSymbol, byte> visitState,
      Dictionary<StructSymbol, (StructDeclarationSyntax Syntax, Scope TypeParamScope)> localStructs) {
    if (visitState.TryGetValue(sym, out byte mark)) {
      return;
    }

    // Only flatten structs declared in this module; imported structs were flattened in their own Phase 2.
    if (!localStructs.TryGetValue(sym, out (StructDeclarationSyntax Syntax, Scope TypeParamScope) info)) {
      visitState[sym] = 2;
      return;
    }

    visitState[sym] = 1; // Visiting

    var allFields = ImmutableArray.CreateBuilder<FieldSymbol>();
    var seenFields = new Dictionary<string, FieldSymbol>(StringComparer.Ordinal);
    foreach (FieldSymbol directField in sym.Fields) {
      allFields.Add(directField);
      seenFields[directField.Name] = directField;
    }

    var allMethods = ImmutableArray.CreateBuilder<MethodSymbol>();
    var seenMethods = new HashSet<string>(StringComparer.Ordinal);
    foreach (MethodSymbol directMethod in sym.Methods) {
      allMethods.Add(directMethod);
      seenMethods.Add(directMethod.Name);
    }

    // Map EmbeddedStructSyntax nodes by target StructSymbol for accurate diagnostic spans.
    var embedSyntaxByTarget = new Dictionary<StructSymbol, EmbeddedStructSyntax>(ReferenceEqualityComparer.Instance);
    foreach (SyntaxNode member in info.Syntax.Members) {
      if (member is EmbeddedStructSyntax embedSyntax
          && state.ReferencedSymbols[embedSyntax.Id] is StructSymbol target
          && !embedSyntaxByTarget.ContainsKey(target)) {
        embedSyntaxByTarget.Add(target, embedSyntax);
      }
    }

    foreach (StructSymbol embedded in sym.EmbeddedStructs) {
      if (visitState.TryGetValue(embedded, out byte embMark) && embMark == 1) {
        SourceSpan cycleSpan = embedSyntaxByTarget.TryGetValue(embedded, out EmbeddedStructSyntax? es)
            ? es.NameToken.Span
            : sym.NameSpan;
        state.Diagnostics.ReportError(
            DiagnosticCodes.TypeMismatch,
            cycleSpan,
            $"Cyclic struct embedding detected involving '{sym.Name}' and '{embedded.Name}'.");
        continue;
      }

      FlattenStruct(state, embedded, visitState, localStructs);

      SourceSpan holderSpan = embedSyntaxByTarget.TryGetValue(embedded, out EmbeddedStructSyntax? embedNode)
          ? embedNode.NameToken.Span
          : sym.NameSpan;
      int holderNodeId = embedNode?.Id ?? sym.DeclaringNodeId;
      var holderField = new FieldSymbol(
          embedded.Name,
          Types.GetStruct(embedded),
          SymbolVisibility.Public,
          -1,
          sym,
          holderNodeId,
          holderSpan,
          state.FilePath);

      foreach (FieldSymbol embField in embedded.AllFields) {
        if (seenFields.TryGetValue(embField.Name, out FieldSymbol? colliding)) {
          // STRUCT-E14 / OQ-29: colliding field name between outer & embedded (or two embedded) structs.
          bool collidingIsOwn = !colliding.IsPromotedFromEmbedded && ReferenceEquals(colliding.ContainingStruct, sym);
          SourceSpan errSpan = collidingIsOwn ? colliding.NameSpan : holderSpan;
          SourceSpan relatedSpan = collidingIsOwn ? embField.NameSpan : colliding.NameSpan;
          string? relatedFile = collidingIsOwn ? embField.FilePath : colliding.FilePath;
          state.Diagnostics.ReportError(
              DiagnosticCodes.DuplicateDeclaration,
              errSpan,
              $"Field '{embField.Name}' in struct '{sym.Name}' collides with field from embedded struct '{embedded.Name}'.",
              [new RelatedLocation(relatedSpan, $"'{embField.Name}' is also declared here", relatedFile)]);
          continue;
        }

        ImmutableArray<FieldSymbol> chain = [holderField, .. embField.EmbeddedChain];
        var promotedField = new FieldSymbol(
            embField.Name,
            embField.Type,
            embField.Visibility,
            allFields.Count,
            sym,
            embField.DeclaringNodeId,
            embField.NameSpan,
            embField.FilePath,
            chain,
            isMutable: embField.IsMutable);
        seenFields.Add(embField.Name, promotedField);
        allFields.Add(promotedField);
      }

      foreach (MethodSymbol embMethod in embedded.Methods) {
        if (embMethod.IsStatic || seenMethods.Contains(embMethod.Name)) {
          // STRUCT-10: outer method overrides embedded method of the same name.
          continue;
        }

        ImmutableArray<FieldSymbol> chain = [holderField, .. embMethod.EmbeddedChain];
        var promotedMethod = new MethodSymbol(
            embMethod.Name,
            embMethod.Visibility,
            isStatic: false,
            sym,
            embMethod.TypeParameters,
            embMethod.Parameters,
            embMethod.ReturnType,
            embMethod.ReturnState,
            embMethod.DeclaringNodeId,
            embMethod.NameSpan,
            embMethod.FilePath,
            embMethod.Intrinsic,
            chain,
            promotedFrom: embMethod.PromotedFrom ?? embMethod,
            isMutating: embMethod.IsMutating);
        seenMethods.Add(embMethod.Name);
        allMethods.Add(promotedMethod);
      }
    }

    sym.AllFields = allFields.ToImmutable();
    sym.Methods = allMethods.ToImmutable();
    visitState[sym] = 2; // Visited
  }

  private void BuildStructMemberScope(
      ModuleCheckState state,
      StructDeclarationSyntax structSyntax,
      StructSymbol sym,
      Scope typeParamScope) {
    ImmutableArray<TypeSymbol> selfTypeArgs = sym.TypeParameters.IsEmpty
        ? []
        : [.. sym.TypeParameters.Select(static tp => (TypeSymbol) tp.Type)];
    StructType selfStructType = Types.GetStruct(sym, selfTypeArgs);

    var memberScope = new Scope(ScopeKind.StructMember, typeParamScope, sym);
    var selfLocal = new LocalSymbol(
        "self",
        selfStructType,
        isMutable: false,
        LocalKind.Variable,
        sym.DeclaringNodeId,
        sym.NameSpan,
        sym.FilePath,
        sym);
    memberScope.TryDeclare(selfLocal, out _);

    foreach (FieldSymbol field in sym.AllFields) {
      memberScope.TryDeclare(field, out _);
    }

    foreach (MethodSymbol method in sym.Methods) {
      if (!method.IsStatic) {
        memberScope.TryDeclare(method, out _);
      }
    }

    sym.MemberScope = memberScope;
    state.SetScope(structSyntax, memberScope);

    // Connect instance methods' method scopes to MemberScope so ticket 037 sees self, fields, and sibling methods.
    // Bind `self` per instance method scope with `isMutable: methodSym.IsMutating` (D-179 / STRUCT-5).
    foreach (SyntaxNode member in structSyntax.Members) {
      if (member is MethodDeclarationSyntax methodSyntax
          && methodSyntax.StaticKeyword is null
          && !IsMissingToken(methodSyntax.NameToken)
          && state.DeclaredSymbols[methodSyntax.Id] is MethodSymbol methodSym
          && state.ScopesByBlockId[methodSyntax.Id] is { } oldMethodScope) {
        var linkedMethodScope = new Scope(ScopeKind.Method, memberScope, methodSym);
        foreach (TypeParameterSymbol tp in methodSym.TypeParameters) {
          linkedMethodScope.TryDeclare(tp, out _);
        }
        foreach (ParameterSymbol param in methodSym.Parameters) {
          linkedMethodScope.TryDeclare(param, out _);
        }
        if (!linkedMethodScope.TryLookupLocal("self", out _)) {
          var methodSelf = new LocalSymbol(
              "self",
              selfStructType,
              isMutable: methodSym.IsMutating,
              LocalKind.Variable,
              methodSym.DeclaringNodeId,
              methodSym.NameSpan,
              methodSym.FilePath,
              methodSym);
          linkedMethodScope.TryDeclare(methodSelf, out _);
        }

        state.SetScope(methodSyntax, linkedMethodScope);
        ReparentMethodBodyScope(state, methodSyntax.Body, linkedMethodScope);
      }
    }
  }

  private ImmutableArray<ParameterSymbol> ResolveParameterList(
      ModuleCheckState state,
      ImmutableArray<ParameterSyntax> parameters,
      Scope paramScope,
      Symbol ownerCallable,
      StructSymbol? enclosingStruct,
      string? currentMethodName) {
    if (parameters.IsEmpty) {
      return [];
    }

    var builder = ImmutableArray.CreateBuilder<ParameterSymbol>(parameters.Length);
    for (int i = 0; i < parameters.Length; i++) {
      ParameterSyntax paramSyntax = parameters[i];
      if (IsMissingToken(paramSyntax.NameToken)) {
        continue;
      }

      string paramName = state.Lex.GetText(paramSyntax.NameToken).ToString();
      bool hasDefault = paramSyntax.DefaultValue is not null || paramSyntax.EqualsToken is not null;
      if (hasDefault) {
        // OQ-20 / D-173 / FN-E12: parameter default values are rejected.
        state.Diagnostics.ReportError(
            DiagnosticCodes.DefaultParameterNotSupported,
            paramSyntax.Span,
            $"Parameter '{paramName}' cannot have a default value; default parameter values are not supported.");
      }

      TypeSymbol paramType = paramSyntax.Type is not null
          ? ResolveType(
              state,
              paramSyntax.Type,
              paramScope,
              TypePosition.ParameterType,
              enclosingStruct,
              currentMethodName)
          : ErrorType.Instance;

      var paramSymbol = new ParameterSymbol(
          paramName,
          paramType,
          i,
          hasDefault,
          paramSyntax.Id,
          paramSyntax.NameToken.Span,
          state.FilePath,
          ownerCallable,
          isMutable: paramSyntax.MutKeyword is not null);

      if (!paramScope.TryDeclare(paramSymbol, out Symbol? existing)) {
        ReportDuplicate(state, paramName, paramSyntax.NameToken.Span, existing!);
      }

      state.SetDeclaredSymbol(paramSyntax, paramSymbol);
      state.SetNodeType(paramSyntax, paramType);
      builder.Add(paramSymbol);
    }

    return builder.ToImmutable();
  }
}
