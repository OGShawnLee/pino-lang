using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Checking;

internal sealed partial class CheckerSession {
  internal TypeSymbol ResolveType(
      ModuleCheckState state,
      TypeSyntax typeSyntax,
      Scope scope,
      TypePosition position,
      StructSymbol? staticMethodEnclosingStruct = null,
      string? staticMethodName = null) {
    TypeSymbol resolved = ResolveTypeCore(
        state,
        typeSyntax,
        scope,
        position,
        staticMethodEnclosingStruct,
        staticMethodName);

    state.SetNodeType(typeSyntax, resolved);
    if (!resolved.IsError) {
      state.ResolvedTypes.Add(resolved);
    }

    return resolved;
  }

  private TypeSymbol ResolveTypeCore(
      ModuleCheckState state,
      TypeSyntax typeSyntax,
      Scope scope,
      TypePosition position,
      StructSymbol? staticMethodEnclosingStruct,
      string? staticMethodName) {
    switch (typeSyntax) {
      case MissingTypeSyntax:
        return Types.Error;

      case NamedTypeSyntax named:
        return ResolveNamedType(state, named, scope, position, staticMethodEnclosingStruct, staticMethodName);

      case QualifiedTypeSyntax qual:
        return ResolveQualifiedType(state, qual, position);

      case GenericTypeSyntax gen:
        return ResolveGenericType(state, gen, scope, staticMethodEnclosingStruct, staticMethodName);

      case VectorTypeSyntax vec: {
          TypeSymbol elem = ResolveType(
              state,
              vec.ElementType,
              scope,
              TypePosition.CollectionElement,
              staticMethodEnclosingStruct,
              staticMethodName);
          return elem.IsError ? Types.Error : Types.GetVector(elem);
        }

      case MapTypeSyntax map: {
          TypeSymbol key = ResolveType(
              state,
              map.KeyType,
              scope,
              TypePosition.MapKey,
              staticMethodEnclosingStruct,
              staticMethodName);
          TypeSymbol val = ResolveType(
              state,
              map.ValueType,
              scope,
              TypePosition.MapValue,
              staticMethodEnclosingStruct,
              staticMethodName);
          return key.IsError || val.IsError ? Types.Error : Types.GetMap(key, val);
        }

      case SetTypeSyntax set: {
          TypeSymbol elem = ResolveType(
              state,
              set.ElementType,
              scope,
              TypePosition.CollectionElement,
              staticMethodEnclosingStruct,
              staticMethodName);
          return elem.IsError ? Types.Error : Types.GetSet(elem);
        }

      case FunctionTypeSyntax fnType:
        return ResolveFunctionType(state, fnType, scope, staticMethodEnclosingStruct, staticMethodName);

      case TupleTypeSyntax tupleType:
        return ResolveTupleType(state, tupleType, scope, staticMethodEnclosingStruct, staticMethodName);

      default:
        return Types.Error;
    }
  }

  private TypeSymbol ResolveNamedType(
      ModuleCheckState state,
      NamedTypeSyntax named,
      Scope scope,
      TypePosition position,
      StructSymbol? staticMethodEnclosingStruct,
      string? staticMethodName) {
    if (IsMissingToken(named.NameToken)) {
      return Types.Error;
    }

    string name = state.Lex.GetText(named.NameToken).ToString();
    switch (name) {
      case "bool":
        return Types.Bool;
      case "int":
        return Types.Int;
      case "float":
        return Types.Float;
      case "rune":
        return Types.Rune;
      case "string":
        return Types.String;
      case "regex":
        return Types.Regex;
      case "void":
        if (IsVoidAllowed(position)) {
          return Types.Void;
        }

        state.Diagnostics.ReportError(
            DiagnosticCodes.InvalidVoidTypePosition,
            named.Span,
            $"Type 'void' is not allowed as a {DescribePosition(position)}.");
        return Types.Error;
    }

    if (!scope.TryLookup(name, out Symbol? sym)) {
      if (CheckStaticMethodInstanceGeneric(state, name, named.Span, staticMethodEnclosingStruct, staticMethodName)) {
        return Types.Error;
      }

      state.Diagnostics.ReportError(
          DiagnosticCodes.UnknownType,
          named.Span,
          $"Unknown type '{name}'.");
      return Types.Error;
    }

    if (sym is TypeParameterSymbol tpSym
        && staticMethodEnclosingStruct is not null
        && ReferenceEquals(tpSym.Owner, staticMethodEnclosingStruct)) {
      ReportStaticMethodInstanceGeneric(state, name, named.Span, staticMethodEnclosingStruct, staticMethodName);
      return Types.Error;
    }

    state.SetReferencedSymbol(named, sym);
    return SymbolToType(state, sym, named.Span, allowMissingTypeArgs: false);
  }

  private TypeSymbol ResolveQualifiedType(
      ModuleCheckState state,
      QualifiedTypeSyntax qual,
      TypePosition position) {
    if (IsMissingToken(qual.ModuleToken) || IsMissingToken(qual.NameToken)) {
      return Types.Error;
    }

    string modName = state.Lex.GetText(qual.ModuleToken).ToString();
    string memberName = state.Lex.GetText(qual.NameToken).ToString();

    if (!state.ImportedModules.TryGetValue(modName, out ModuleSymbol? targetModule)) {
      state.Diagnostics.ReportError(
          DiagnosticCodes.UnknownType,
          qual.Span,
          $"Module '{modName}' is not imported in this scope.");
      return Types.Error;
    }

    Symbol? member = FindModuleDeclaration(targetModule, memberName);
    if (member is null) {
      string prefix = position == TypePosition.StructLiteralType ? "Struct" : "Type";
      state.Diagnostics.ReportError(
          DiagnosticCodes.ModuleMemberNotFound,
          qual.NameToken.Span,
          $"{prefix} '{memberName}' is not defined in module '{modName}'.");
      return Types.Error;
    }

    bool accessible = member.Visibility == SymbolVisibility.Public
        || (member.Visibility == SymbolVisibility.Testable && state.IsTestFile)
        || ReferenceEquals(targetModule, state.Module);

    if (!accessible) {
      string message = member switch {
        StructSymbol => $"Struct '{modName}::{memberName}' is not public.",
        InterfaceSymbol => $"Interface '{modName}::{memberName}' is not public.",
        _ => $"Symbol '{modName}::{memberName}' is not public.",
      };
      state.Diagnostics.ReportError(
          DiagnosticCodes.SymbolNotPublic,
          qual.Span,
          message);
      return Types.Error;
    }

    state.SetReferencedSymbol(qual, member);
    return SymbolToType(state, member, qual.Span, allowMissingTypeArgs: false);
  }

  private TypeSymbol ResolveGenericType(
      ModuleCheckState state,
      GenericTypeSyntax gen,
      Scope scope,
      StructSymbol? staticMethodEnclosingStruct,
      string? staticMethodName) {
    Symbol? baseDef = ResolveNominalBaseSymbol(
        state,
        gen.BaseType,
        scope,
        staticMethodEnclosingStruct,
        staticMethodName);

    ImmutableArray<TypeParameterSymbol> typeParams = baseDef switch {
      StructSymbol s => s.TypeParameters,
      InterfaceSymbol i => i.TypeParameters,
      UnionSymbol u => u.TypeParameters,
      _ => [],
    };

    bool isResult = ReferenceEquals(baseDef, Prelude.ResultUnion);
    ImmutableArray<TypeSyntax> argSyntaxes = gen.TypeArguments.Arguments;
    var resolvedArgs = ImmutableArray.CreateBuilder<TypeSymbol>(argSyntaxes.Length);
    bool anyError = false;

    for (int i = 0; i < argSyntaxes.Length; i++) {
      TypePosition argPos = isResult && i == 0
          ? TypePosition.ResultSuccessArg
          : TypePosition.GenericTypeArgument;
      TypeSymbol argType = ResolveType(
          state,
          argSyntaxes[i],
          scope,
          argPos,
          staticMethodEnclosingStruct,
          staticMethodName);
      if (argType.IsError) {
        anyError = true;
      }

      resolvedArgs.Add(argType);
    }

    if (baseDef is null) {
      return Types.Error;
    }

    state.SetReferencedSymbol(gen, baseDef);

    if (typeParams.IsEmpty) {
      state.Diagnostics.ReportError(
          DiagnosticCodes.TypeMismatch,
          gen.Span,
          $"Type '{baseDef.Name}' does not accept generic type arguments.");
      return Types.Error;
    }

    if (argSyntaxes.Length != typeParams.Length) {
      state.Diagnostics.ReportError(
          DiagnosticCodes.TypeMismatch,
          gen.TypeArguments.Span,
          $"Generic type '{baseDef.Name}' expects {typeParams.Length} type argument(s), but got {argSyntaxes.Length}.");
      return Types.Error;
    }

    ImmutableArray<TypeSymbol> args = resolvedArgs.MoveToImmutable();

    // Verify or defer generic constraints (`GEN-E5`, `GEN-E7`).
    for (int i = 0; i < typeParams.Length; i++) {
      TypeParameterSymbol tp = typeParams[i];
      TypeSymbol argType = args[i];
      if (state.IsPhase2Complete) {
        VerifyGenericTypeConstraint(state, argSyntaxes[i], tp, argType, typeParams, args);
      } else {
        state.DeferredConstraintChecks.Add((argSyntaxes[i], tp, argType, typeParams, args));
      }
    }

    if (anyError) {
      return Types.Error;
    }

    if (state.IsPhase2Complete && !state.SuppressInstantiationRecording) {
      RecordGenericInstantiation(baseDef, args);
    }

    return baseDef switch {
      StructSymbol s => Types.GetStruct(s, args),
      InterfaceSymbol iface => Types.GetInterface(iface, args),
      UnionSymbol u => Types.GetUnion(u, args),
      _ => Types.Error,
    };
  }

  internal void VerifyGenericTypeConstraint(
      ModuleCheckState state,
      TypeSyntax argSyntax,
      TypeParameterSymbol tp,
      TypeSymbol argType,
      ImmutableArray<TypeParameterSymbol> ownerTypeParams,
      ImmutableArray<TypeSymbol> ownerTypeArgs) {
    if (tp.Constraint is not { } rawConstraint || argType.IsError || rawConstraint.IsError) {
      return;
    }

    TypeSymbol substituted = TypeSubstitution.Substitute(rawConstraint, ownerTypeParams, ownerTypeArgs, Types);
    if (substituted is not InterfaceType constraint || constraint.IsError) {
      return;
    }

    if (argType is StructType argStruct) {
      state.EnsureStructMethodsResolved?.Invoke(argStruct.Definition);
    }

    if (!TypeCompatibility.SatisfiesInterface(argType, constraint, Types, state.Module, InterfaceSatisfactionCache)) {
      state.Diagnostics.ReportError(
          DiagnosticCodes.GenericConstraintNotSatisfied,
          argSyntax.Span,
          $"Type '{argType.DisplayName}' does not satisfy generic constraint '{constraint.DisplayName}' for parameter '{tp.Name}'.");
    }
  }

  private Symbol? ResolveNominalBaseSymbol(
      ModuleCheckState state,
      TypeSyntax baseSyntax,
      Scope scope,
      StructSymbol? staticMethodEnclosingStruct,
      string? staticMethodName) {
    switch (baseSyntax) {
      case MissingTypeSyntax:
        return null;

      case NamedTypeSyntax named: {
          if (IsMissingToken(named.NameToken)) {
            return null;
          }

          string name = state.Lex.GetText(named.NameToken).ToString();
          if (!scope.TryLookup(name, out Symbol? sym)) {
            if (!CheckStaticMethodInstanceGeneric(state, name, named.Span, staticMethodEnclosingStruct, staticMethodName)) {
              state.Diagnostics.ReportError(
                  DiagnosticCodes.UnknownType,
                  named.Span,
                  $"Unknown type '{name}'.");
            }
            return null;
          }

          if (sym is TypeParameterSymbol tpSym
              && staticMethodEnclosingStruct is not null
              && ReferenceEquals(tpSym.Owner, staticMethodEnclosingStruct)) {
            ReportStaticMethodInstanceGeneric(state, name, named.Span, staticMethodEnclosingStruct, staticMethodName);
            return null;
          }

          state.SetReferencedSymbol(named, sym);
          if (sym is not (StructSymbol or InterfaceSymbol or UnionSymbol)) {
            state.Diagnostics.ReportError(
                DiagnosticCodes.TypeMismatch,
                named.Span,
                $"Type '{name}' cannot take generic type arguments.");
            return null;
          }

          return sym;
        }

      case QualifiedTypeSyntax qual: {
          if (IsMissingToken(qual.ModuleToken) || IsMissingToken(qual.NameToken)) {
            return null;
          }

          string modName = state.Lex.GetText(qual.ModuleToken).ToString();
          string memberName = state.Lex.GetText(qual.NameToken).ToString();

          if (!state.ImportedModules.TryGetValue(modName, out ModuleSymbol? targetModule)) {
            state.Diagnostics.ReportError(
                DiagnosticCodes.UnknownType,
                qual.Span,
                $"Module '{modName}' is not imported in this scope.");
            return null;
          }

          Symbol? member = FindModuleDeclaration(targetModule, memberName);
          if (member is null) {
            state.Diagnostics.ReportError(
                DiagnosticCodes.ModuleMemberNotFound,
                qual.NameToken.Span,
                $"Type '{memberName}' is not defined in module '{modName}'.");
            return null;
          }

          bool accessible = member.Visibility == SymbolVisibility.Public
              || (member.Visibility == SymbolVisibility.Testable && state.IsTestFile)
              || ReferenceEquals(targetModule, state.Module);
          if (!accessible) {
            string message = member switch {
              StructSymbol => $"Struct '{modName}::{memberName}' is not public.",
              InterfaceSymbol => $"Interface '{modName}::{memberName}' is not public.",
              _ => $"Symbol '{modName}::{memberName}' is not public.",
            };
            state.Diagnostics.ReportError(DiagnosticCodes.SymbolNotPublic, qual.Span, message);
            return null;
          }

          state.SetReferencedSymbol(qual, member);
          if (member is not (StructSymbol or InterfaceSymbol or UnionSymbol)) {
            state.Diagnostics.ReportError(
                DiagnosticCodes.TypeMismatch,
                qual.Span,
                $"Type '{modName}::{memberName}' cannot take generic type arguments.");
            return null;
          }

          return member;
        }

      default:
        return null;
    }
  }

  private TypeSymbol ResolveFunctionType(
      ModuleCheckState state,
      FunctionTypeSyntax fnType,
      Scope scope,
      StructSymbol? staticMethodEnclosingStruct,
      string? staticMethodName) {
    var paramTypes = ImmutableArray.CreateBuilder<TypeSymbol>(fnType.Parameters.Length);
    bool anyError = false;

    foreach (FunctionTypeParameterSyntax paramSyntax in fnType.Parameters) {
      TypeSymbol pt = ResolveType(
          state,
          paramSyntax.Type,
          scope,
          TypePosition.ParameterType,
          staticMethodEnclosingStruct,
          staticMethodName);
      if (pt.IsError) {
        anyError = true;
      }
      paramTypes.Add(pt);
    }

    TypeSymbol retType = fnType.ReturnType is not null
        ? ResolveType(
            state,
            fnType.ReturnType,
            scope,
            TypePosition.FunctionTypeReturn,
            staticMethodEnclosingStruct,
            staticMethodName)
        : Types.Void;

    if (retType.IsError || anyError) {
      return Types.Error;
    }

    return Types.GetFunction(paramTypes.MoveToImmutable(), retType);
  }

  private TypeSymbol ResolveTupleType(
      ModuleCheckState state,
      TupleTypeSyntax tupleType,
      Scope scope,
      StructSymbol? staticMethodEnclosingStruct,
      string? staticMethodName) {
    var fields = ImmutableArray.CreateBuilder<TupleField>(tupleType.Fields.Length);
    var seenLabels = new HashSet<string>(StringComparer.Ordinal);
    bool anyError = false;

    foreach (TupleFieldTypeSyntax fieldSyntax in tupleType.Fields) {
      if (IsMissingToken(fieldSyntax.NameToken)) {
        anyError = true;
        continue;
      }

      string label = state.Lex.GetText(fieldSyntax.NameToken).ToString();
      TypeSymbol fieldType = ResolveType(
          state,
          fieldSyntax.Type,
          scope,
          TypePosition.TupleField,
          staticMethodEnclosingStruct,
          staticMethodName);
      state.SetNodeType(fieldSyntax, fieldType);

      if (!seenLabels.Add(label)) {
        state.Diagnostics.ReportError(
            DiagnosticCodes.DuplicateTupleLabel,
            fieldSyntax.NameToken.Span,
            $"Duplicate label '{label}' in tuple type.");
      }

      if (fieldType.IsError) {
        anyError = true;
      }

      fields.Add(new TupleField(label, fieldType));
    }

    return anyError ? Types.Error : Types.GetTuple(fields.ToImmutable());
  }

  private TypeSymbol SymbolToType(
      ModuleCheckState state,
      Symbol sym,
      Text.SourceSpan span,
      bool allowMissingTypeArgs) {
    switch (sym) {
      case TypeParameterSymbol tp:
        return tp.Type;

      case EnumSymbol e:
        return Types.GetEnum(e);

      case StructSymbol s:
        if (!s.TypeParameters.IsEmpty && !allowMissingTypeArgs) {
          state.Diagnostics.ReportError(
              DiagnosticCodes.CannotInferGenericArguments,
              span,
              $"Generic struct '{s.Name}' requires {s.TypeParameters.Length} type argument(s).");
          return Types.Error;
        }
        return Types.GetStruct(s);

      case InterfaceSymbol iface:
        if (!iface.TypeParameters.IsEmpty && !allowMissingTypeArgs) {
          state.Diagnostics.ReportError(
              DiagnosticCodes.CannotInferGenericArguments,
              span,
              $"Generic interface '{iface.Name}' requires {iface.TypeParameters.Length} type argument(s).");
          return Types.Error;
        }
        return Types.GetInterface(iface);

      case UnionSymbol u:
        if (!u.TypeParameters.IsEmpty && !allowMissingTypeArgs) {
          state.Diagnostics.ReportError(
              DiagnosticCodes.CannotInferGenericArguments,
              span,
              $"Generic union '{u.Name}' requires {u.TypeParameters.Length} type argument(s).");
          return Types.Error;
        }
        return Types.GetUnion(u);

      case ErrorSymbol:
        return Types.Error;

      default:
        state.Diagnostics.ReportError(
            DiagnosticCodes.UnknownType,
            span,
            $"'{sym.Name}' is not a type.");
        return Types.Error;
    }
  }

  private static bool CheckStaticMethodInstanceGeneric(
      ModuleCheckState state,
      string name,
      Text.SourceSpan span,
      StructSymbol? enclosingStruct,
      string? methodName) {
    if (enclosingStruct is null) {
      return false;
    }

    foreach (TypeParameterSymbol tp in enclosingStruct.TypeParameters) {
      if (string.Equals(tp.Name, name, StringComparison.Ordinal)) {
        ReportStaticMethodInstanceGeneric(state, name, span, enclosingStruct, methodName);
        return true;
      }
    }

    return false;
  }

  private static void ReportStaticMethodInstanceGeneric(
      ModuleCheckState state,
      string paramName,
      Text.SourceSpan span,
      StructSymbol enclosingStruct,
      string? methodName) {
    string nameForMessage = methodName ?? "<static>";
    state.Diagnostics.ReportError(
        DiagnosticCodes.StaticMethodUsesInstanceGeneric,
        span,
        $"Static method '{nameForMessage}' cannot use instance-bound generic parameter '{paramName}' of struct '{enclosingStruct.Name}'.");
  }

  private static bool IsVoidAllowed(TypePosition position) =>
      position is TypePosition.FunctionReturn
          or TypePosition.FunctionTypeReturn
          or TypePosition.ResultSuccessArg;

  private static string DescribePosition(TypePosition position) =>
      position switch {
        TypePosition.VariableAnnotation => "variable type",
        TypePosition.ParameterType => "parameter type",
        TypePosition.FieldType => "struct field type",
        TypePosition.InterfacePropertyType => "interface property type",
        TypePosition.CollectionElement => "collection element type",
        TypePosition.MapKey => "map key type",
        TypePosition.MapValue => "map value type",
        TypePosition.UnionPayload => "union payload type",
        TypePosition.TupleField => "tuple field type",
        TypePosition.GenericTypeArgument => "generic type argument",
        TypePosition.GenericConstraint => "generic constraint",
        _ => "type annotation",
      };
}
