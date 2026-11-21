using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  // ---------------------------------------------------------------------------------------------
  // Struct literals (D-078, D-104, D-163, OQ-29, OQ-31, STRUCT-1, STRUCT-8, STRUCT-15, STRUCT-E7, E11-E13)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol SynthesizeStructLiteral(StructLiteralExpressionSyntax literal, TypeSymbol? expectedType = null) {
    if (literal.Fields.IsEmpty
        && literal.TypeName is NamedTypeSyntax partialNamed
        && (IsMissing(literal.CloseBraceToken)
            || ((uint) literal.Id < (uint) _state.ParentsByNodeId.Length
                && _state.ParentsByNodeId[literal.Id] is IfExpressionSyntax parentIf
                && IsMissing(parentIf.ThenKeyword)))
        && (IsMissing(partialNamed.NameToken)
            || !_scope.TryLookup(NameOf(partialNamed.NameToken), out Symbol? maybeStruct)
            || maybeStruct is not StructSymbol)) {
      return _types.Error;
    }

    // GEN-3: Generic struct literal without explicit type arguments (`Pair { key: "a", value: 1 }`).
    if (literal.TypeName is NamedTypeSyntax or QualifiedTypeSyntax
        && TryPeekGenericStructSymbol(literal.TypeName) is { } inferredGenericStruct) {
      return InferGenericStructLiteral(literal, inferredGenericStruct, expectedType);
    }

    TypeSymbol resolvedType = _session.ResolveType(
        _state,
        literal.TypeName,
        _scope,
        TypePosition.StructLiteralType);

    if (resolvedType is not StructType structType) {
      if (!resolvedType.IsError) {
        Report(
            DiagnosticCodes.TypeMismatch,
            literal.TypeName.Span,
            $"'{resolvedType.DisplayName}' is not a struct type.");
      }

      foreach (StructFieldInitSyntax field in literal.Fields) {
        if (field.Value is { } valExpr) {
          Synthesize(valExpr);
        }
      }

      return _types.Error;
    }

    StructSymbol structSym = structType.Definition;
    ImmutableArray<TypeParameterSymbol> structParams = structSym.TypeParameters;
    ImmutableArray<TypeSymbol> structArgs = structType.TypeArguments;

    var fieldsByName = new Dictionary<string, FieldSymbol>(structSym.AllFields.Length, StringComparer.Ordinal);
    foreach (FieldSymbol field in structSym.AllFields) {
      fieldsByName.TryAdd(field.Name, field);
    }

    var seenFields = new HashSet<string>(StringComparer.Ordinal);
    bool reportedPrivateField = false;

    foreach (StructFieldInitSyntax fieldInit in literal.Fields) {
      if (IsMissing(fieldInit.NameToken)) {
        if (fieldInit.Value is { } partialVal) {
          Synthesize(partialVal);
        }

        continue;
      }

      string fieldName = NameOf(fieldInit.NameToken);
      if (!fieldsByName.TryGetValue(fieldName, out FieldSymbol? fieldSymbol)) {
        if (fieldInit.Value is { } unknownVal) {
          Synthesize(unknownVal);
        }

        Report(
            DiagnosticCodes.UnknownMember,
            fieldInit.NameToken.Span,
            $"Struct '{structSym.Name}' does not have field '{fieldName}'.");
        continue;
      }

      if (!seenFields.Add(fieldName)) {
        if (fieldInit.Value is { } dupVal) {
          Synthesize(dupVal);
        }

        Report(
            DiagnosticCodes.DuplicateDeclaration,
            fieldInit.NameToken.Span,
            $"Field '{fieldName}' is already specified in struct literal '{structSym.Name}'.");
        continue;
      }

      _state.SetReferencedSymbol(fieldInit, fieldSymbol);

      if (!IsFieldVisibleInCurrentModule(fieldSymbol)) {
        reportedPrivateField = true;
        ReportPrivateField(fieldInit.Span, structSym.Name, fieldSymbol);
      }

      TypeSymbol expectedFieldType = TypeSubstitution.Substitute(
          fieldSymbol.Type,
          structParams,
          structArgs,
          _types);
      _state.SetNodeType(fieldInit, expectedFieldType);

      if (fieldInit.Value is { } valueExpr) {
        if (valueExpr is not MissingExpressionSyntax) {
          Check(valueExpr, expectedFieldType, $"field '{fieldName}'", $" in struct '{structSym.Name}'");
        }
      } else if (fieldInit.ColonToken is null) {
        // D-104 / STRUCT-15: shorthand `Point { x }` resolves `x` in the enclosing scope.
        if (!TryLookupAndRecordCapture(fieldName, out Symbol? localSym)) {
          if (_tolerantNames == 0) {
            Report(
                DiagnosticCodes.UndeclaredIdentifier,
                fieldInit.NameToken.Span,
                $"Identifier '{fieldName}' is not declared.");
          }
        } else {
          _state.SetShorthandValueSymbol(fieldInit, localSym);
          TypeSymbol actualValType = SymbolValueType(localSym);
          if (!TryConvert(fieldInit, actualValType, expectedFieldType)) {
            if (expectedFieldType is InterfaceType) {
              Report(
                  DiagnosticCodes.InterfaceNotSatisfied,
                  fieldInit.Span,
                  $"Cannot assign type '{actualValType.DisplayName}' to field '{fieldName}' in struct '{structSym.Name}'; expected type '{expectedFieldType.DisplayName}', but got '{actualValType.DisplayName}'.");
            } else {
              Report(
                  DiagnosticCodes.TypeMismatch,
                  fieldInit.Span,
                  $"Cannot assign type '{actualValType.DisplayName}' to field '{fieldName}' of type '{expectedFieldType.DisplayName}' in struct '{structSym.Name}'.");
            }
          }
        }
      }
    }

    // A2/A4: half-typed `Point { x: 1,` omits the closing brace; skip missing-field diagnostics.
    if (!IsMissing(literal.CloseBraceToken)) {
      if (!IsStructInCurrentModule(structSym)) {
        FieldSymbol? privateField = null;
        foreach (FieldSymbol f in structSym.AllFields) {
          if (!IsFieldVisibleInCurrentModule(f)) {
            privateField = f;
            break;
          }
        }

        if (privateField is not null && !reportedPrivateField) {
          ImmutableArray<RelatedLocation> related = privateField.NameSpan.Length > 0
              ? [new RelatedLocation(privateField.NameSpan, $"'{privateField.Name}' is declared here", privateField.FilePath)]
              : [];
          Report(
              DiagnosticCodes.PrivateFieldAccess,
              literal.Span,
              $"Cannot construct struct '{structSym.Name}' from another module because field '{privateField.Name}' is private.",
              related,
              $"construct '{structSym.Name}' through a 'pub' function or static method instead");
          return structType;
        }
      }

      foreach (FieldSymbol field in structSym.AllFields) {
        if (!seenFields.Contains(field.Name)) {
          ImmutableArray<RelatedLocation> related = structSym.NameSpan.Length > 0
              ? [new RelatedLocation(structSym.NameSpan, $"struct '{structSym.Name}' is declared here", structSym.FilePath)]
              : [];
          Report(
              DiagnosticCodes.MissingStructField,
              literal.Span,
              $"Missing property '{field.Name}' in struct literal '{structSym.Name}'.",
              related);
        }
      }
    }

    if (!structArgs.IsEmpty) {
      RecordGenericInstantiation(structSym, structArgs);
    }

    return structType;
  }

  // ---------------------------------------------------------------------------------------------
  // Member access & field assignment (STRUCT-1..20, STRUCT-E1..E5, E13, IFACE-4, IFACE-E4..E5)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol SynthesizeMemberAccess(MemberAccessExpressionSyntax member) {
    TypeSymbol targetType = Synthesize(member.Target);
    if (targetType is PendingReturnType) {
      return targetType;
    }

    if (IsMissing(member.MemberToken) || IsSilent(targetType)) {
      return _types.Error;
    }

    string memberName = NameOf(member.MemberToken);

    switch (targetType) {
      case StructType structType:
        return SynthesizeStructMember(member, structType, memberName);

      case InterfaceType ifaceType:
        return SynthesizeInterfaceMember(member, ifaceType, memberName);

      case TypeParameterType tpType:
        if (tpType.Symbol.Constraint is { } constraint) {
          return SynthesizeInterfaceMember(member, constraint, memberName);
        }

        Report(
            DiagnosticCodes.UnknownMember,
            member.MemberToken.Span,
            $"Struct '{tpType.DisplayName}' does not have field or instance method '{memberName}'.");
        return _types.Error;

      default:
        if (StdlibCatalog.TryGetMember(targetType, memberName, out IntrinsicDescriptor? descriptor)) {
          if (descriptor.IsProperty) {
            TypeSymbol[] receiverArgs = GetReceiverTypeArgs(targetType);
            return descriptor.Signatures[0].ReturnType.Resolve(_types, _prelude, receiverArgs, []);
          }

          if (descriptor.Signatures.Length == 1
              && descriptor.Signatures[0].MethodTypeParameterNames.IsEmpty
              && descriptor.Signatures[0].Parameters.All(static p => p.Kind == IntrinsicParamKind.Fixed)) {
            TypeSymbol[] receiverArgs = GetReceiverTypeArgs(targetType);
            IntrinsicSignature sig = descriptor.Signatures[0];
            ImmutableArray<TypeSymbol>.Builder paramTypes = ImmutableArray.CreateBuilder<TypeSymbol>(sig.Parameters.Length);
            foreach (IntrinsicParameter p in sig.Parameters) {
              paramTypes.Add(p.TypeSpec.Resolve(_types, _prelude, receiverArgs, []));
            }

            return _types.GetFunction(
                paramTypes.MoveToImmutable(),
                sig.ReturnType.Resolve(_types, _prelude, receiverArgs, []));
          }

          return _types.Error;
        }

        Report(
            DiagnosticCodes.UnknownMember,
            member.MemberToken.Span,
            $"Type '{targetType.DisplayName}' does not have member '{memberName}'.");
        return _types.Error;
    }
  }

  private TypeSymbol SynthesizeStructMember(
      MemberAccessExpressionSyntax member,
      StructType structType,
      string memberName) {
    StructSymbol structSym = structType.Definition;

    if (FindStructField(structSym, memberName) is { } field) {
      if (!IsFieldVisibleInCurrentModule(field)) {
        ReportPrivateField(member.Span, structSym.Name, field);
      }

      _state.SetReferencedSymbol(member, field);
      return TypeSubstitution.Substitute(field.Type, structSym.TypeParameters, structType.TypeArguments, _types);
    }

    if (FindStructMethod(structSym, memberName) is { } method) {
      if (method.IsStatic) {
        Report(
            DiagnosticCodes.InvalidStaticOrInstanceAccess,
            member.Span,
            $"Cannot call static method '{memberName}' of struct '{structSym.Name}' on an instance.");
        return _types.Error;
      }

      _state.SetReferencedSymbol(member, method);
      return MethodValueType(method, structSym.TypeParameters, structType.TypeArguments);
    }

    Report(
        DiagnosticCodes.UnknownMember,
        member.MemberToken.Span,
        $"Struct '{structSym.Name}' does not have field or instance method '{memberName}'.");
    return _types.Error;
  }

  private TypeSymbol SynthesizeInterfaceMember(
      MemberAccessExpressionSyntax member,
      InterfaceType ifaceType,
      string memberName) {
    InterfaceSymbol ifaceSym = ifaceType.Definition;

    if (FindInterfaceProperty(ifaceSym, memberName) is { } prop) {
      return TypeSubstitution.Substitute(prop.Type, ifaceSym.TypeParameters, ifaceType.TypeArguments, _types);
    }

    if (FindInterfaceMethod(ifaceSym, memberName) is { } method) {
      _state.SetReferencedSymbol(member, method);
      return MethodValueType(method, ifaceSym.TypeParameters, ifaceType.TypeArguments);
    }

    Report(
        DiagnosticCodes.UnknownMember,
        member.MemberToken.Span,
        $"Interface '{ifaceSym.Name}' does not have property or method '{memberName}'.");
    return _types.Error;
  }

  private TypeSymbol AssignToMember(
      AssignmentExpressionSyntax assignment,
      MemberAccessExpressionSyntax member,
      TokenKind op) {
    TypeSymbol targetType = Synthesize(member.Target);
    if (targetType is PendingReturnType) {
      Synthesize(assignment.Value);
      return targetType;
    }

    if (IsMissing(member.MemberToken) || IsSilent(targetType)) {
      Synthesize(assignment.Value);
      return _types.Error;
    }

    string memberName = NameOf(member.MemberToken);

    if (targetType is StructType structType) {
      StructSymbol structSym = structType.Definition;
      if (FindStructField(structSym, memberName) is { } field) {
        if (!IsFieldVisibleInCurrentModule(field)) {
          ReportPrivateField(member.Span, structSym.Name, field);
        }

        _state.SetReferencedSymbol(member, field);
        TypeSymbol fieldType = TypeSubstitution.Substitute(
            field.Type,
            structSym.TypeParameters,
            structType.TypeArguments,
            _types);
        _state.SetNodeType(member, fieldType);
        return CheckAssignedValue(assignment, member, fieldType, op);
      }

      if (FindStructMethod(structSym, memberName) is not null) {
        Synthesize(assignment.Value);
        Report(
            DiagnosticCodes.InvalidAssignmentTarget,
            member.Span,
            $"Cannot assign to method '{memberName}' of struct '{structSym.Name}'.");
        return _types.Error;
      }

      Synthesize(assignment.Value);
      Report(
          DiagnosticCodes.UnknownMember,
          member.MemberToken.Span,
          $"Struct '{structSym.Name}' does not have field or instance method '{memberName}'.");
      return _types.Error;
    }

    if (targetType is InterfaceType ifaceType) {
      InterfaceSymbol ifaceSym = ifaceType.Definition;
      if (FindInterfaceProperty(ifaceSym, memberName) is { } prop) {
        TypeSymbol propType = TypeSubstitution.Substitute(
            prop.Type,
            ifaceSym.TypeParameters,
            ifaceType.TypeArguments,
            _types);
        _state.SetNodeType(member, propType);
        return CheckAssignedValue(assignment, member, propType, op);
      }

      if (FindInterfaceMethod(ifaceSym, memberName) is not null) {
        Synthesize(assignment.Value);
        Report(
            DiagnosticCodes.InvalidAssignmentTarget,
            member.Span,
            $"Cannot assign to method '{memberName}' of interface '{ifaceSym.Name}'.");
        return _types.Error;
      }

      Synthesize(assignment.Value);
      Report(
          DiagnosticCodes.UnknownMember,
          member.MemberToken.Span,
          $"Interface '{ifaceSym.Name}' does not have property or method '{memberName}'.");
      return _types.Error;
    }

    if (targetType is TypeParameterType tpType) {
      if (tpType.Symbol.Constraint is { } constraint) {
        InterfaceSymbol ifaceSym = constraint.Definition;
        if (FindInterfaceProperty(ifaceSym, memberName) is { } prop) {
          TypeSymbol propType = TypeSubstitution.Substitute(
              prop.Type,
              ifaceSym.TypeParameters,
              constraint.TypeArguments,
              _types);
          _state.SetNodeType(member, propType);
          return CheckAssignedValue(assignment, member, propType, op);
        }

        if (FindInterfaceMethod(ifaceSym, memberName) is not null) {
          Synthesize(assignment.Value);
          Report(
              DiagnosticCodes.InvalidAssignmentTarget,
              member.Span,
              $"Cannot assign to method '{memberName}' of interface '{ifaceSym.Name}'.");
          return _types.Error;
        }

        Synthesize(assignment.Value);
        Report(
            DiagnosticCodes.UnknownMember,
            member.MemberToken.Span,
            $"Interface '{ifaceSym.Name}' does not have property or method '{memberName}'.");
        return _types.Error;
      }

      Synthesize(assignment.Value);
      Report(
          DiagnosticCodes.UnknownMember,
          member.MemberToken.Span,
          $"Struct '{tpType.DisplayName}' does not have field or instance method '{memberName}'.");
      return _types.Error;
    }

    Synthesize(assignment.Value);
    if (StdlibCatalog.TryGetMember(targetType, memberName, out IntrinsicDescriptor? desc) && desc.IsProperty) {
      Report(
          DiagnosticCodes.InvalidAssignmentTarget,
          member.Span,
          $"Cannot assign to read-only property '{memberName}' on type '{targetType.DisplayName}'.");
    } else {
      Report(
          DiagnosticCodes.InvalidAssignmentTarget,
          member.Span,
          $"Cannot assign to member '{memberName}' on type '{targetType.DisplayName}'.");
    }

    return _types.Error;
  }

  // ---------------------------------------------------------------------------------------------
  // Static access (Enum::Member, Struct::static_fn, Union::Variant, Mod::member)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol SynthesizeStaticAccess(StaticAccessExpressionSyntax access, TypeSymbol? expectedType) {
    Symbol? targetSym = ResolveStaticTargetSymbol(access.Target, out bool alreadyReported);
    if (IsMissing(access.MemberToken) || targetSym is null) {
      if (!alreadyReported && targetSym is null && !IsMissing(access.MemberToken)) {
        // Error already reported by ResolveStaticTargetSymbol when needed.
      }
      return _types.Error;
    }

    string memberName = NameOf(access.MemberToken);

    switch (targetSym) {
      case ModuleSymbol module:
        return SynthesizeModuleStaticMember(access, module, memberName);

      case EnumSymbol enumSym:
        foreach (EnumMemberSymbol member in enumSym.Members) {
          if (string.Equals(member.Name, memberName, StringComparison.Ordinal)) {
            _state.SetReferencedSymbol(access, member);
            return _types.GetEnum(enumSym);
          }
        }

        Report(
            DiagnosticCodes.UnknownEnumMember,
            access.MemberToken.Span,
            $"Enum '{enumSym.Name}' has no member '{memberName}'.");
        return _types.Error;

      case StructSymbol structSym: {
          if (FindStructMethod(structSym, memberName) is not { } method) {
            Report(
                DiagnosticCodes.UnknownMember,
                access.MemberToken.Span,
                $"Struct '{structSym.Name}' does not have static method '{memberName}'.");
            return _types.Error;
          }

          if (!method.IsStatic) {
            Report(
                DiagnosticCodes.InvalidStaticOrInstanceAccess,
                access.Span,
                $"Method '{memberName}' of struct '{structSym.Name}' is not static.");
            return _types.Error;
          }

          _state.SetReferencedSymbol(access, method);
          ImmutableArray<TypeSymbol> typeArgs = ResolveOptionalTypeArguments(access.TargetTypeArguments, structSym.TypeParameters, structSym.Name, access.Span);
          return MethodValueType(method, structSym.TypeParameters, typeArgs);
        }

      case UnionSymbol unionSym:
        return SynthesizeUnionVariantAccess(access, unionSym, memberName, expectedType);

      case ErrorSymbol:
        return _types.Error;

      default:
        Report(
            DiagnosticCodes.UnknownMember,
            access.MemberToken.Span,
            $"'{targetSym.Name}' does not have static member '{memberName}'.");
        return _types.Error;
    }
  }

  private TypeSymbol SynthesizeModuleStaticMember(
      StaticAccessExpressionSyntax access,
      ModuleSymbol module,
      string memberName) {
    Symbol? member = CheckerSession.FindModuleDeclaration(module, memberName);
    if (member is null) {
      Report(
          DiagnosticCodes.ModuleMemberNotFound,
          access.MemberToken.Span,
          $"Module '{module.Name}' does not define '{memberName}'.");
      return _types.Error;
    }

    if (!IsModuleSymbolAccessible(module, member)) {
      string kind = member switch {
        FunctionSymbol => "Function",
        StructSymbol => "Struct",
        InterfaceSymbol => "Interface",
        EnumSymbol => "Enum",
        UnionSymbol => "Union",
        LocalSymbol => "Variable",
        _ => "Symbol",
      };
      Report(
          DiagnosticCodes.SymbolNotPublic,
          access.Span,
          $"{kind} '{module.Name}::{memberName}' is private.");
      return _types.Error;
    }

    _state.SetReferencedSymbol(access, member);
    return member switch {
      FunctionSymbol fnSym => FunctionValueType(fnSym),
      LocalSymbol localSym => localSym.Type,
      _ => _types.Error,
    };
  }

  private TypeSymbol SynthesizeUnionVariantAccess(
      StaticAccessExpressionSyntax access,
      UnionSymbol unionSym,
      string memberName,
      TypeSymbol? expectedType) {
    UnionVariantSymbol? variant = FindUnionVariant(unionSym, memberName);
    if (variant is null) {
      Report(
          DiagnosticCodes.InvalidIsPattern,
          access.MemberToken.Span,
          $"Union '{unionSym.Name}' has no variant '{memberName}'.");
      return _types.Error;
    }

    _state.SetReferencedSymbol(access, variant);

    if (unionSym.TypeParameters.IsEmpty) {
      if (!variant.Fields.IsEmpty) {
        Report(
            DiagnosticCodes.VariantArgumentMismatch,
            access.Span,
            $"Variant constructor '{unionSym.Name}::{variant.Name}' expected {variant.Fields.Length} arguments, but got 0.");
        return _types.Error;
      }

      return _types.GetUnion(unionSym);
    }

    if (access.TargetTypeArguments is not null) {
      bool isResult = ReferenceEquals(unionSym, _prelude.ResultUnion);
      ImmutableArray<TypeSymbol> explicitArgs = ResolveOptionalTypeArguments(
          access.TargetTypeArguments,
          unionSym.TypeParameters,
          unionSym.Name,
          access.Span,
          isResultUnion: isResult);
      if (explicitArgs.Length == unionSym.TypeParameters.Length && !explicitArgs.Any(static a => a.IsError)) {
        UnionType unionType = _types.GetUnion(unionSym, explicitArgs);
        bool isBareVoidResultSuccess = isResult
            && ReferenceEquals(variant, _prelude.ResultSuccessVariant)
            && explicitArgs.Length == 2
            && explicitArgs[0].IsVoid;
        if (!isBareVoidResultSuccess && !variant.Fields.IsEmpty) {
          Report(
              DiagnosticCodes.VariantArgumentMismatch,
              access.Span,
              $"Variant constructor '{unionSym.Name}::{variant.Name}' expected {variant.Fields.Length} arguments, but got 0.");
          return _types.Error;
        }

        RecordGenericInstantiation(unionSym, explicitArgs);
        return unionType;
      }

      return _types.Error;
    }

    if (expectedType is UnionType expectedUnion && ReferenceEquals(expectedUnion.Definition, unionSym)) {
      return CheckGenericUnionStaticAccess(access, expectedUnion);
    }

    if (!variant.Fields.IsEmpty) {
      Report(
          DiagnosticCodes.VariantArgumentMismatch,
          access.Span,
          $"Variant constructor '{unionSym.Name}::{variant.Name}' expected {variant.Fields.Length} arguments, but got 0.");
      return _types.Error;
    }

    Report(
        DiagnosticCodes.CannotInferGenericArguments,
        access.Span,
        $"Cannot infer generic type argument(s) for '{unionSym.Name}::{variant.Name}'; add a type annotation.");
    return _types.Error;
  }

  private TypeSymbol CheckGenericUnionStaticAccess(StaticAccessExpressionSyntax access, UnionType expectedUnion) {
    if (IsMissing(access.MemberToken)) {
      return _types.Error;
    }

    UnionSymbol unionSym = expectedUnion.Definition;
    Symbol? targetSym = ResolveStaticTargetSymbol(access.Target, out _);
    if (!ReferenceEquals(targetSym, unionSym)) {
      return SynthesizeStaticAccess(access, expectedUnion);
    }

    string memberName = NameOf(access.MemberToken);
    UnionVariantSymbol? variant = FindUnionVariant(unionSym, memberName);
    if (variant is null) {
      Report(
          DiagnosticCodes.InvalidIsPattern,
          access.MemberToken.Span,
          $"Union '{unionSym.Name}' has no variant '{memberName}'.");
      return _types.Error;
    }

    _state.SetReferencedSymbol(access, variant);

    // D-162 / ERR-23: `Result[void, E]` allows bare `Result::Success`.
    if (ReferenceEquals(unionSym, _prelude.ResultUnion)
        && ReferenceEquals(variant, _prelude.ResultSuccessVariant)
        && expectedUnion.TypeArguments.Length == 2
        && expectedUnion.TypeArguments[0].IsVoid) {
      RecordGenericInstantiation(unionSym, expectedUnion.TypeArguments);
      return expectedUnion;
    }

    if (variant.Fields.IsEmpty) {
      RecordGenericInstantiation(unionSym, expectedUnion.TypeArguments);
      return expectedUnion;
    }

    Report(
        DiagnosticCodes.VariantArgumentMismatch,
        access.Span,
        $"Variant constructor '{unionSym.Name}::{variant.Name}' expected {variant.Fields.Length} arguments, but got 0.");
    return _types.Error;
  }

  private Symbol? ResolveStaticTargetSymbol(ExpressionSyntax target, out bool alreadyReported) {
    switch (target) {
      case IdentifierExpressionSyntax identifier: {
          if (IsMissing(identifier.IdentifierToken)) {
            alreadyReported = true;
            return null;
          }

          string name = NameOf(identifier.IdentifierToken);
          if (!_scope.TryLookup(name, out Symbol? symbol)) {
            if (_tolerantNames == 0) {
              Report(
                  DiagnosticCodes.UndeclaredIdentifier,
                  identifier.Span,
                  $"Identifier '{name}' is not declared.");
            }
            alreadyReported = true;
            return null;
          }

          _state.SetReferencedSymbol(identifier, symbol);
          alreadyReported = false;
          return symbol;
        }

      case StaticAccessExpressionSyntax innerAccess: {
          Symbol? outerTarget = ResolveStaticTargetSymbol(innerAccess.Target, out alreadyReported);
          if (outerTarget is null || IsMissing(innerAccess.MemberToken)) {
            return null;
          }

          string innerName = NameOf(innerAccess.MemberToken);
          if (outerTarget is ModuleSymbol module) {
            Symbol? member = CheckerSession.FindModuleDeclaration(module, innerName);
            if (member is null) {
              Report(
                  DiagnosticCodes.ModuleMemberNotFound,
                  innerAccess.MemberToken.Span,
                  $"Module '{module.Name}' does not define '{innerName}'.");
              alreadyReported = true;
              return null;
            }

            if (!IsModuleSymbolAccessible(module, member)) {
              string kind = member switch {
                StructSymbol => "Struct",
                InterfaceSymbol => "Interface",
                EnumSymbol => "Enum",
                UnionSymbol => "Union",
                _ => "Symbol",
              };
              Report(
                  DiagnosticCodes.SymbolNotPublic,
                  innerAccess.Span,
                  $"{kind} '{module.Name}::{innerName}' is private.");
              alreadyReported = true;
              return null;
            }

            _state.SetReferencedSymbol(innerAccess, member);
            alreadyReported = false;
            return member;
          }

          alreadyReported = false;
          return null;
        }

      default:
        Synthesize(target);
        alreadyReported = true;
        return null;
    }
  }

  private ImmutableArray<TypeSymbol> ResolveOptionalTypeArguments(
      TypeArgumentListSyntax? typeArgsSyntax,
      ImmutableArray<TypeParameterSymbol> typeParams,
      string ownerName,
      SourceSpan span,
      bool isResultUnion = false) {
    if (typeArgsSyntax is null) {
      return [];
    }

    var builder = ImmutableArray.CreateBuilder<TypeSymbol>(typeArgsSyntax.Arguments.Length);
    var argSpans = ImmutableArray.CreateBuilder<SourceSpan>(typeArgsSyntax.Arguments.Length);
    for (int i = 0; i < typeArgsSyntax.Arguments.Length; i++) {
      TypeSyntax argSyntax = typeArgsSyntax.Arguments[i];
      TypePosition pos = isResultUnion && i == 0
          ? TypePosition.ResultSuccessArg
          : TypePosition.GenericTypeArgument;
      TypeSymbol argType = _session.ResolveType(_state, argSyntax, _scope, pos);
      builder.Add(argType);
      argSpans.Add(argSyntax.Span);
    }

    ImmutableArray<TypeSymbol> resolvedArgs = builder.MoveToImmutable();
    ImmutableArray<SourceSpan> spans = argSpans.MoveToImmutable();

    if (typeArgsSyntax.Arguments.Length != typeParams.Length) {
      Report(
          DiagnosticCodes.TypeMismatch,
          span,
          $"Generic '{ownerName}' expects {typeParams.Length} type argument(s), but got {typeArgsSyntax.Arguments.Length}.");
    } else {
      CheckGenericBounds(typeParams, resolvedArgs, spans, span);
    }

    return resolvedArgs;
  }

  // ---------------------------------------------------------------------------------------------
  // Labeled tuples (TUP-1..10, TUP-E1..E7)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol SynthesizeTupleLiteralOutsideReturn(TupleLiteralExpressionSyntax tupleLiteral) {
    Report(
        DiagnosticCodes.TupleLiteralOutsideReturn,
        tupleLiteral.Span,
        "Tuple literals can only be used as a return value.");
    CheckTupleFieldsForNames(tupleLiteral, expectedTuple: null, checkDuplicateLabels: true);
    return _types.Error;
  }

  private TypeSymbol SynthesizeTupleReturnForInference(TupleLiteralExpressionSyntax tupleLiteral) {
    (ImmutableArray<TupleField> fields, bool valid, _) = CheckTupleFieldsForNames(
        tupleLiteral,
        expectedTuple: null,
        checkDuplicateLabels: true);
    if (!valid || fields.IsEmpty) {
      return _types.Error;
    }

    TypeSymbol tupleType = _types.GetTuple(fields);
    _state.SetNodeType(tupleLiteral, tupleType);
    return tupleType;
  }

  private void CheckTupleReturn(
      TupleLiteralExpressionSyntax tupleLiteral,
      TypeSymbol? expectedReturn,
      string callableName) {
    if (expectedReturn is null) {
      CheckTupleFieldsForNames(tupleLiteral, expectedTuple: null, checkDuplicateLabels: true);
      return;
    }

    if (expectedReturn.IsError) {
      CheckTupleFieldsForNames(tupleLiteral, expectedTuple: null, checkDuplicateLabels: true);
      return;
    }

    if (expectedReturn is not TupleType expectedTuple) {
      CheckTupleFieldsForNames(tupleLiteral, expectedTuple: null, checkDuplicateLabels: true);
      Report(
          DiagnosticCodes.ReturnTypeMismatch,
          tupleLiteral.Span,
          $"Cannot return a tuple from function '{callableName}' with declared return type '{expectedReturn.DisplayName}'.");
      return;
    }

    (ImmutableArray<TupleField> actualFields, bool hasAllLabels, bool reportedFieldMismatch) = CheckTupleFieldsForNames(
        tupleLiteral,
        expectedTuple,
        checkDuplicateLabels: true);
    if (!hasAllLabels) {
      return;
    }

    TupleType actualTuple = _types.GetTuple(actualFields);
    if (!reportedFieldMismatch && !ReferenceEquals(actualTuple, expectedTuple)) {
      bool hasShapeOrTypeDiff = actualTuple.Fields.Length != expectedTuple.Fields.Length;
      if (!hasShapeOrTypeDiff) {
        foreach (TupleField actualField in actualFields) {
          TupleField? expectedField = FindTupleField(expectedTuple, actualField.Label);
          if (expectedField is null || (!actualField.Type.IsError && !ReferenceEquals(actualField.Type, expectedField.Value.Type))) {
            hasShapeOrTypeDiff = true;
            break;
          }
        }
      }

      if (hasShapeOrTypeDiff) {
        Report(
            DiagnosticCodes.ReturnTypeMismatch,
            tupleLiteral.Span,
            $"Returned tuple '{actualTuple.DisplayName}' does not match declared return type '{expectedTuple.DisplayName}'.");
      }
    }

    _state.SetNodeType(tupleLiteral, expectedTuple);
  }

  private (ImmutableArray<TupleField> Fields, bool Valid, bool ReportedFieldMismatch) CheckTupleFieldsForNames(
      TupleLiteralExpressionSyntax tupleLiteral,
      TupleType? expectedTuple,
      bool checkDuplicateLabels) {
    var fields = ImmutableArray.CreateBuilder<TupleField>(tupleLiteral.Fields.Length);
    var seenLabels = new HashSet<string>(StringComparer.Ordinal);
    bool valid = true;
    bool reportedFieldMismatch = false;

    foreach (TupleFieldSyntax fieldSyntax in tupleLiteral.Fields) {
      if (IsMissing(fieldSyntax.NameToken)) {
        if (fieldSyntax.Value is { } partialVal) {
          Synthesize(partialVal);
        }

        valid = false;
        continue;
      }

      string label = NameOf(fieldSyntax.NameToken);
      if (checkDuplicateLabels && !seenLabels.Add(label)) {
        Report(
            DiagnosticCodes.DuplicateTupleLabel,
            fieldSyntax.NameToken.Span,
            $"Duplicate label '{label}' in tuple literal.");
        if (fieldSyntax.Value is { } dupVal) {
          Synthesize(dupVal);
        }

        valid = false;
        continue;
      }

      TypeSymbol? expectedFieldType = expectedTuple is not null
          ? FindTupleField(expectedTuple, label)?.Type
          : null;

      TypeSymbol fieldType;
      if (fieldSyntax.Value is { } valueExpr) {
        if (valueExpr is MissingExpressionSyntax) {
          fieldType = _types.Error;
          valid = false;
        } else {
          fieldType = Synthesize(valueExpr);
          if (expectedFieldType is not null && !expectedFieldType.IsError) {
            if (TryConvert(valueExpr, fieldType, expectedFieldType)) {
              fieldType = expectedFieldType;
            } else if (!fieldType.IsError) {
              reportedFieldMismatch = true;
              Report(
                  DiagnosticCodes.ReturnTypeMismatch,
                  valueExpr.Span,
                  $"Tuple field '{label}' has type '{fieldType.DisplayName}', which does not match declared return type '{expectedTuple!.DisplayName}'.");
            }
          }
        }
      } else if (fieldSyntax.ColonToken is null) {
        // TUP-3: shorthand `@(x, y)` resolves `x` in enclosing scope.
        if (!TryLookupAndRecordCapture(label, out Symbol? sym)) {
          if (_tolerantNames == 0) {
            Report(
                DiagnosticCodes.UndeclaredIdentifier,
                fieldSyntax.NameToken.Span,
                $"Identifier '{label}' is not declared.");
          }

          fieldType = _types.Error;
          valid = false;
        } else {
          _state.SetReferencedSymbol(fieldSyntax, sym);
          fieldType = SymbolValueType(sym);
          if (expectedFieldType is not null && !expectedFieldType.IsError) {
            if (TryConvert(fieldSyntax, fieldType, expectedFieldType)) {
              fieldType = expectedFieldType;
            } else if (!fieldType.IsError) {
              reportedFieldMismatch = true;
              Report(
                  DiagnosticCodes.ReturnTypeMismatch,
                  fieldSyntax.Span,
                  $"Tuple field '{label}' has type '{fieldType.DisplayName}', which does not match declared return type '{expectedTuple!.DisplayName}'.");
            }
          }
        }
      } else {
        fieldType = _types.Error;
        valid = false;
      }

      _state.SetNodeType(fieldSyntax, fieldType);
      fields.Add(new TupleField(label, fieldType));
    }

    return (fields.ToImmutable(), valid, reportedFieldMismatch);
  }

  private void CheckTupleDestructuring(TupleDestructuringStatementSyntax destructuring) {
    TypeSymbol initType = Synthesize(destructuring.Initializer);
    TupleType? tupleType = initType as TupleType;
    if (!IsSilent(initType) && tupleType is null) {
      Report(
          DiagnosticCodes.TypeMismatch,
          destructuring.Initializer.Span,
          $"Cannot destructure a value of type '{initType.DisplayName}' as a tuple.");
    }

    bool isVal = destructuring.KeywordToken.Kind == TokenKind.ValKeyword;
    var seenVarNames = new HashSet<string>(StringComparer.Ordinal);

    foreach (DestructureFieldSyntax field in destructuring.Fields) {
      if (IsMissing(field.LabelToken)) {
        continue;
      }

      string label = NameOf(field.LabelToken);
      Token nameToken = field.VariableToken ?? field.LabelToken;
      bool nameMissing = IsMissing(nameToken);
      string varName = nameMissing ? "<unknown>" : NameOf(nameToken);

      TypeSymbol boundType = _types.Error;
      if (tupleType is not null) {
        TupleField? tupleField = FindTupleField(tupleType, label);
        if (tupleField is { } tf) {
          boundType = tf.Type;
        } else {
          Report(
              DiagnosticCodes.UnknownTupleField,
              field.LabelToken.Span,
              $"Field '{label}' does not exist in tuple type '{tupleType.DisplayName}'.");
        }
      }

      _state.SetNodeType(field, boundType);
      if (nameMissing) {
        continue;
      }

      if (!seenVarNames.Add(varName)) {
        Report(
            DiagnosticCodes.DuplicateDestructureVariable,
            nameToken.Span,
            $"Duplicate variable name '{varName}' in destructuring.");
        continue;
      }

      var symbol = new LocalSymbol(
          varName,
          boundType,
          !isVal,
          LocalKind.Variable,
          field.Id,
          nameToken.Span,
          _state.FilePath,
          _scope.Owner);
      if (!_scope.TryDeclare(symbol, out Symbol? existing)) {
        CheckerSession.ReportDuplicate(_state, varName, nameToken.Span, existing!);
      }

      _state.SetDeclaredSymbol(field, symbol);
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------------------------

  private void ReportPrivateField(SourceSpan span, string structName, FieldSymbol field) {
    ImmutableArray<RelatedLocation> related = field.NameSpan.Length > 0
        ? [new RelatedLocation(field.NameSpan, $"'{field.Name}' is declared here", field.FilePath)]
        : [];
    ModuleSymbol? fieldModule = TypeCompatibility.GetDeclaringModule(TypeCompatibility.GetOriginalFieldStruct(field));
    string help = fieldModule is not null
        ? $"add 'pub' to field '{field.Name}' to expose it outside module '{fieldModule.Name}'"
        : $"add 'pub' to field '{field.Name}'";
    Report(
        DiagnosticCodes.PrivateFieldAccess,
        span,
        $"Field '{field.Name}' of struct '{structName}' is private.",
        related,
        help);
  }

  private bool IsStructInCurrentModule(StructSymbol structSym) {
    ModuleSymbol? declModule = TypeCompatibility.GetDeclaringModule(structSym);
    return declModule is null || ReferenceEquals(declModule, _state.Module);
  }

  private bool IsFieldVisibleInCurrentModule(FieldSymbol field) {
    if (field.Visibility == SymbolVisibility.Public) {
      return true;
    }

    if (field.Visibility == SymbolVisibility.Testable && _state.IsTestFile) {
      return true;
    }

    ModuleSymbol? fieldModule = TypeCompatibility.GetDeclaringModule(TypeCompatibility.GetOriginalFieldStruct(field));
    return fieldModule is null || ReferenceEquals(fieldModule, _state.Module);
  }

  private bool IsModuleSymbolAccessible(ModuleSymbol module, Symbol member) =>
      member.Visibility == SymbolVisibility.Public
      || (member.Visibility == SymbolVisibility.Testable && _state.IsTestFile)
      || ReferenceEquals(module, _state.Module);

  private static FieldSymbol? FindStructField(StructSymbol structSym, string name) {
    foreach (FieldSymbol field in structSym.AllFields) {
      if (string.Equals(field.Name, name, StringComparison.Ordinal)) {
        return field;
      }
    }

    return null;
  }

  private static MethodSymbol? FindStructMethod(StructSymbol structSym, string name) {
    foreach (MethodSymbol method in structSym.Methods) {
      if (string.Equals(method.Name, name, StringComparison.Ordinal)) {
        return method;
      }
    }

    return null;
  }

  private static InterfaceProperty? FindInterfaceProperty(InterfaceSymbol ifaceSym, string name) {
    foreach (InterfaceProperty prop in ifaceSym.Properties) {
      if (string.Equals(prop.Name, name, StringComparison.Ordinal)) {
        return prop;
      }
    }

    return null;
  }

  private static MethodSymbol? FindInterfaceMethod(InterfaceSymbol ifaceSym, string name) {
    foreach (MethodSymbol method in ifaceSym.Methods) {
      if (string.Equals(method.Name, name, StringComparison.Ordinal)) {
        return method;
      }
    }

    return null;
  }

  private static UnionVariantSymbol? FindUnionVariant(UnionSymbol unionSym, string name) {
    foreach (UnionVariantSymbol variant in unionSym.Variants) {
      if (string.Equals(variant.Name, name, StringComparison.Ordinal)) {
        return variant;
      }
    }

    return null;
  }

  private static TupleField? FindTupleField(TupleType tupleType, string label) {
    foreach (TupleField field in tupleType.Fields) {
      if (string.Equals(field.Label, label, StringComparison.Ordinal)) {
        return field;
      }
    }

    return null;
  }

  private static TypeSymbol[] GetReceiverTypeArgs(TypeSymbol receiverType) =>
      receiverType switch {
        VectorType vec => [vec.ElementType],
        MapType map => [map.KeyType, map.ValueType],
        SetType set => [set.ElementType],
        _ => [],
      };
}
