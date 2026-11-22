using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  // ---------------------------------------------------------------------------------------------
  // Calls (FN-1..19, FN-E1, FN-E9, D-055, D-057, STRUCT-12..14, UNION-2, UNION-E1, StdlibCatalog)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol SynthesizeCall(CallExpressionSyntax call, TypeSymbol? expectedType) {
    if (TryCheckIndexedElementCall(call, out TypeSymbol indexedReturn, out TypeSymbol? cachedMemberReceiver)) {
      return indexedReturn;
    }

    switch (call.Callee) {
      case IdentifierExpressionSyntax identifier:
        return CheckIdentifierCall(call, identifier, expectedType);

      case MemberAccessExpressionSyntax member:
        return CheckMemberCall(call, member, expectedType, cachedMemberReceiver);

      case StaticAccessExpressionSyntax staticAccess:
        return CheckStaticCall(call, staticAccess, expectedType);

      default: {
          TypeSymbol calleeType = Synthesize(call.Callee);
          if (calleeType is PendingReturnType) {
            SynthesizeCallArguments(call);
            return calleeType;
          }

          if (IsSilent(calleeType)) {
            SynthesizeCallArguments(call);
            return _types.Error;
          }

          if (calleeType is FunctionType fnType) {
            CheckFunctionTypeCall(call, fnType, CallKind.IndirectFunctionValue, targetSymbol: null, "value", "<callable>", hasImplicitSelf: false);
            return fnType.ReturnType;
          }

          SynthesizeCallArguments(call);
          Report(
              DiagnosticCodes.NotCallable,
              call.Callee.Span,
              $"Cannot call value of non-function type '{calleeType.DisplayName}'.");
          return _types.Error;
        }
    }
  }

  /// <summary>
  /// D-055: <c>fns[i](9)</c> parses speculatively with <see cref="CallExpressionSyntax.TypeArguments"/>
  /// of length 1 containing <see cref="NamedTypeSyntax"/>. When the callee is a non-generic
  /// indexable value (<c>[]T</c> or <c>map[K, V]</c>), reinterpret it as an indexed element call.
  /// </summary>
  private bool TryCheckIndexedElementCall(
      CallExpressionSyntax call,
      out TypeSymbol resultType,
      out TypeSymbol? cachedMemberReceiver) {
    cachedMemberReceiver = null;
    if (call.TypeArguments is not { Arguments.Length: 1 } typeArgs
        || typeArgs.Arguments[0] is not NamedTypeSyntax indexNameSyntax
        || IsMissing(indexNameSyntax.NameToken)) {
      resultType = _types.Error;
      return false;
    }

    TypeSymbol? containerType = null;
    Symbol? containerSymbol = null;

    if (call.Callee is IdentifierExpressionSyntax id && !IsMissing(id.IdentifierToken)) {
      string calleeName = NameOf(id.IdentifierToken);
      if (_scope.TryLookupWithScope(calleeName, out Symbol? sym, out Scope? containerDeclScope)
          && sym is LocalSymbol or ParameterSymbol or FieldSymbol) {
        containerSymbol = sym;
        containerType = SymbolValueType(sym);
        if (containerType is VectorType or MapType) {
          RecordCaptureIfOuter(sym, containerDeclScope, isMutation: false, id.Span);
          _state.SetReferencedSymbol(id, sym);
          _state.SetNodeType(id, containerType);
        } else {
          containerType = null;
        }
      }
    } else if (call.Callee is MemberAccessExpressionSyntax member && !IsMissing(member.MemberToken)) {
      TypeSymbol receiver = Synthesize(member.Target);
      cachedMemberReceiver = receiver;
      string memberName = NameOf(member.MemberToken);
      if (receiver is StructType st && FindStructField(st.Definition, memberName) is { } field) {
        TypeSymbol fType = TypeSubstitution.Substitute(field.Type, st.Definition.TypeParameters, st.TypeArguments, _types);
        if (fType is VectorType or MapType) {
          if (!IsFieldVisibleInCurrentModule(field)) {
            ReportPrivateField(member.Span, st.Definition.Name, field);
          }
          _state.SetReferencedSymbol(member, field);
          _state.SetNodeType(member, fType);
          containerSymbol = field;
          containerType = fType;
        }
      } else if (receiver is InterfaceType iface && FindInterfaceProperty(iface.Definition, memberName) is { } prop) {
        TypeSymbol pType = TypeSubstitution.Substitute(prop.Type, iface.Definition.TypeParameters, iface.TypeArguments, _types);
        if (pType is VectorType or MapType) {
          _state.SetNodeType(member, pType);
          containerType = pType;
        }
      } else if (receiver is TypeParameterType { Symbol.Constraint: { } boundIface }
          && FindInterfaceProperty(boundIface.Definition, memberName) is { } boundProp) {
        TypeSymbol pType = TypeSubstitution.Substitute(boundProp.Type, boundIface.Definition.TypeParameters, boundIface.TypeArguments, _types);
        if (pType is VectorType or MapType) {
          _state.SetNodeType(member, pType);
          containerType = pType;
        }
      }
    } else if (call.Callee is not StaticAccessExpressionSyntax) {
      TypeSymbol calleeVal = Synthesize(call.Callee);
      if (calleeVal is VectorType or MapType) {
        containerType = calleeVal;
      }
    }

    if (containerType is null) {
      resultType = _types.Error;
      return false;
    }

    string idxName = NameOf(indexNameSyntax.NameToken);
    Symbol? idxSymbol = null;
    TypeSymbol idxType = _types.Error;
    if (!TryLookupAndRecordCapture(idxName, out idxSymbol, reportSpan: indexNameSyntax.Span)) {
      if (_tolerantNames == 0) {
        Report(
            DiagnosticCodes.UndeclaredIdentifier,
            indexNameSyntax.Span,
            $"Identifier '{idxName}' is not declared.");
      }
    } else {
      _state.SetReferencedSymbol(indexNameSyntax, idxSymbol);
      idxType = SymbolValueType(idxSymbol);
      _state.SetNodeType(indexNameSyntax, idxType);
    }

    TypeSymbol elemType = _types.Error;
    if (containerType is VectorType vec) {
      if (!IsSilent(idxType) && !ReferenceEquals(idxType, _types.Int)) {
        Report(
            DiagnosticCodes.InvalidIndexOperation,
            indexNameSyntax.Span,
            $"Vector index must have type 'int', but got '{idxType.DisplayName}'.");
      }
      elemType = vec.ElementType;
    } else if (containerType is MapType map) {
      if (!IsSilent(idxType) && !TryConvert(indexNameSyntax, idxType, map.KeyType)) {
        Report(
            DiagnosticCodes.TypeMismatch,
            indexNameSyntax.Span,
            $"Cannot assign type '{idxType.DisplayName}' to map key of type '{map.KeyType.DisplayName}'.");
      }
      elemType = map.ValueType;
    }

    if (IsSilent(elemType)) {
      SynthesizeCallArguments(call);
      resultType = _types.Error;
      return true;
    }

    if (elemType is FunctionType fnType) {
      CheckFunctionTypeCall(
          call,
          fnType,
          CallKind.IndexedElementCall,
          containerSymbol,
          "value",
          containerSymbol?.Name ?? "<indexed>",
          hasImplicitSelf: false,
          indexSymbol: idxSymbol);
      resultType = fnType.ReturnType;
      return true;
    }

    SynthesizeCallArguments(call);
    Report(
        DiagnosticCodes.NotCallable,
        call.Callee.Span,
        $"Cannot call element of non-function type '{elemType.DisplayName}'.");
    resultType = _types.Error;
    return true;
  }

  private TypeSymbol CheckIdentifierCall(
      CallExpressionSyntax call,
      IdentifierExpressionSyntax identifier,
      TypeSymbol? expectedType) {
    if (IsMissing(identifier.IdentifierToken)) {
      SynthesizeCallArguments(call);
      return _types.Error;
    }

    string name = NameOf(identifier.IdentifierToken);
    if (!TryLookupAndRecordCapture(name, out Symbol? symbol, reportSpan: identifier.Span)) {
      if (_isStaticMethod && _currentStruct is not null) {
        if (_currentStruct.Methods.Any(m => !m.IsStatic && string.Equals(m.Name, name, StringComparison.Ordinal))) {
          Report(
              DiagnosticCodes.InvalidStaticOrInstanceAccess,
              identifier.Span,
              $"Cannot call instance method '{name}' from static method in struct '{_currentStruct.Name}'.");
          SynthesizeCallArguments(call);
          return _types.Error;
        }

        if (_currentStruct.AllFields.Any(f => string.Equals(f.Name, name, StringComparison.Ordinal))) {
          Report(
              DiagnosticCodes.InvalidStaticOrInstanceAccess,
              identifier.Span,
              $"Cannot access instance field '{name}' from static method in struct '{_currentStruct.Name}'.");
          SynthesizeCallArguments(call);
          return _types.Error;
        }
      }

      if (_tolerantNames == 0) {
        Report(
            DiagnosticCodes.UndeclaredIdentifier,
            identifier.Span,
            $"Identifier '{name}' is not declared.");
      }

      SynthesizeCallArguments(call);
      return _types.Error;
    }

    _state.SetReferencedSymbol(identifier, symbol);

    switch (symbol) {
      case FunctionSymbol fnSym when fnSym.Intrinsic is not null:
        if (StdlibCatalog.TryGetGlobal(fnSym.Name, out IntrinsicDescriptor? globalDesc)) {
          return CheckIntrinsicCall(call, globalDesc, receiverType: null, fnSym, expectedType);
        }

        SynthesizeCallArguments(call);
        return _types.Error;

      case FunctionSymbol fnSym:
        return CheckDeclaredFunctionCall(call, fnSym, identifier, expectedType);

      case MethodSymbol methodSym: {
          // D-075 / STRUCT-19: bare sibling method call inside an instance method.
          ImmutableArray<TypeParameterSymbol> ownerParams = _currentStruct?.TypeParameters ?? [];
          ImmutableArray<TypeSymbol> ownerArgs = [.. ownerParams.Select(static tp => (TypeSymbol) tp.Type)];
          return CheckMethodCallCore(
              call,
              methodSym,
              ownerParams,
              ownerArgs,
              identifier,
              hasImplicitSelf: true,
              expectedType);
        }

      case LocalSymbol or ParameterSymbol or FieldSymbol: {
          TypeSymbol valType = SymbolValueType(symbol);
          _state.SetNodeType(identifier, valType);
          if (IsSilent(valType)) {
            SynthesizeCallArguments(call);
            return _types.Error;
          }

          if (valType is FunctionType fnType) {
            CallKind kind = symbol is FieldSymbol ? CallKind.CallableField : CallKind.IndirectFunctionValue;
            string descKind = symbol is FieldSymbol ? "callable field" : "function";
            CheckFunctionTypeCall(
                call,
                fnType,
                kind,
                symbol,
                descKind,
                symbol.Name,
                hasImplicitSelf: symbol is FieldSymbol);
            return fnType.ReturnType;
          }

          SynthesizeCallArguments(call);
          Report(
              DiagnosticCodes.NotCallable,
              call.Callee.Span,
              $"Cannot call value '{name}' of non-function type '{valType.DisplayName}'.");
          return _types.Error;
        }

      default:
        SynthesizeCallArguments(call);
        Report(
            DiagnosticCodes.NotCallable,
            call.Callee.Span,
            $"'{symbol.Name}' is not callable.");
        return _types.Error;
    }
  }

  private TypeSymbol CheckMemberCall(
      CallExpressionSyntax call,
      MemberAccessExpressionSyntax member,
      TypeSymbol? expectedType,
      TypeSymbol? cachedReceiver = null) {
    TypeSymbol targetType = cachedReceiver ?? Synthesize(member.Target);
    if (targetType is PendingReturnType) {
      SynthesizeCallArguments(call);
      return targetType;
    }

    if (IsMissing(member.MemberToken) || IsSilent(targetType)) {
      SynthesizeCallArguments(call);
      return _types.Error;
    }

    string memberName = NameOf(member.MemberToken);

    switch (targetType) {
      case StructType structType: {
          StructSymbol structSym = structType.Definition;
          if (FindStructMethod(structSym, memberName) is { } method) {
            if (method.IsStatic) {
              SynthesizeCallArguments(call);
              Report(
                  DiagnosticCodes.InvalidStaticOrInstanceAccess,
                  member.Span,
                  $"Cannot call static method '{memberName}' of struct '{structSym.Name}' on an instance.");
              return _types.Error;
            }

            _state.SetReferencedSymbol(member, method);
            return CheckMethodCallCore(
                call,
                method,
                structSym.TypeParameters,
                structType.TypeArguments,
                member,
                hasImplicitSelf: false,
                expectedType);
          }

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

            if (IsSilent(fieldType)) {
              SynthesizeCallArguments(call);
              return _types.Error;
            }

            if (fieldType is FunctionType fnType) {
              CheckFunctionTypeCall(
                  call,
                  fnType,
                  CallKind.CallableField,
                  field,
                  "callable field",
                  field.Name,
                  hasImplicitSelf: false);
              return fnType.ReturnType;
            }

            // STRUCT-E10
            SynthesizeCallArguments(call);
            Report(
                DiagnosticCodes.UnknownMember,
                member.MemberToken.Span,
                $"Struct '{structSym.Name}' does not have method '{memberName}'.");
            return _types.Error;
          }

          // STRUCT-E2
          SynthesizeCallArguments(call);
          Report(
              DiagnosticCodes.UnknownMember,
              member.MemberToken.Span,
              $"Struct '{structSym.Name}' does not have method '{memberName}'.");
          return _types.Error;
        }

      case InterfaceType ifaceType:
        return CheckInterfaceMemberCall(call, member, ifaceType, memberName, expectedType);

      case TypeParameterType tpType:
        if (tpType.Symbol.Constraint is { } constraint) {
          return CheckInterfaceMemberCall(call, member, constraint, memberName, expectedType);
        }

        SynthesizeCallArguments(call);
        Report(
            DiagnosticCodes.UnknownMember,
            member.MemberToken.Span,
            $"Struct '{tpType.DisplayName}' does not have field or instance method '{memberName}'.");
        return _types.Error;

      default:
        if (StdlibCatalog.TryGetMember(targetType, memberName, out IntrinsicDescriptor? descriptor)) {
          if (descriptor.IsProperty) {
            SynthesizeCallArguments(call);
            Report(
                DiagnosticCodes.NotCallable,
                member.Span,
                $"Property '{memberName}' on type '{targetType.DisplayName}' is not callable.");
            return _types.Error;
          }

          return CheckIntrinsicCall(call, descriptor, targetType, targetSymbol: null, expectedType);
        }

        SynthesizeCallArguments(call);
        Report(
            DiagnosticCodes.UnknownMember,
            member.MemberToken.Span,
            $"Type '{targetType.DisplayName}' does not have method '{memberName}'.");
        return _types.Error;
    }
  }

  private TypeSymbol CheckInterfaceMemberCall(
      CallExpressionSyntax call,
      MemberAccessExpressionSyntax member,
      InterfaceType ifaceType,
      string memberName,
      TypeSymbol? expectedType) {
    InterfaceSymbol ifaceSym = ifaceType.Definition;

    if (FindInterfaceMethod(ifaceSym, memberName) is { } method) {
      _state.SetReferencedSymbol(member, method);
      return CheckMethodCallCore(
          call,
          method,
          ifaceSym.TypeParameters,
          ifaceType.TypeArguments,
          member,
          hasImplicitSelf: false,
          expectedType);
    }

    if (FindInterfaceProperty(ifaceSym, memberName) is { } prop) {
      TypeSymbol propType = TypeSubstitution.Substitute(
          prop.Type,
          ifaceSym.TypeParameters,
          ifaceType.TypeArguments,
          _types);
      _state.SetNodeType(member, propType);

      if (IsSilent(propType)) {
        SynthesizeCallArguments(call);
        return _types.Error;
      }

      if (propType is FunctionType fnType) {
        CheckFunctionTypeCall(
            call,
            fnType,
            CallKind.CallableField,
            targetSymbol: null,
            "callable field",
            prop.Name,
            hasImplicitSelf: false);
        return fnType.ReturnType;
      }
    }

    SynthesizeCallArguments(call);
    Report(
        DiagnosticCodes.UnknownMember,
        member.MemberToken.Span,
        $"Interface '{ifaceSym.Name}' does not have method or callable field '{memberName}'.");
    return _types.Error;
  }

  private TypeSymbol CheckStaticCall(
      CallExpressionSyntax call,
      StaticAccessExpressionSyntax staticAccess,
      TypeSymbol? expectedType) {
    Symbol? targetSym = ResolveStaticTargetSymbol(staticAccess.Target, out _);
    if (IsMissing(staticAccess.MemberToken) || targetSym is null) {
      SynthesizeCallArguments(call);
      return _types.Error;
    }

    string memberName = NameOf(staticAccess.MemberToken);

    switch (targetSym) {
      case ModuleSymbol module: {
          Symbol? member = CheckerSession.FindModuleDeclaration(module, memberName);
          if (member is null) {
            SynthesizeCallArguments(call);
            Report(
                DiagnosticCodes.ModuleMemberNotFound,
                staticAccess.MemberToken.Span,
                $"Module '{module.Name}' does not define '{memberName}'.");
            return _types.Error;
          }

          if (!IsModuleSymbolAccessible(module, member)) {
            SynthesizeCallArguments(call);
            Report(
                DiagnosticCodes.SymbolNotPublic,
                staticAccess.Span,
                $"Function '{module.Name}::{memberName}' is private.");
            return _types.Error;
          }

          _state.SetReferencedSymbol(staticAccess, member);
          if (member is FunctionSymbol fnSym) {
            return CheckDeclaredFunctionCall(call, fnSym, staticAccess, expectedType);
          }

          SynthesizeCallArguments(call);
          Report(
              DiagnosticCodes.NotCallable,
              staticAccess.Span,
              $"'{module.Name}::{memberName}' is not callable.");
          return _types.Error;
        }

      case StructSymbol structSym: {
          if (FindStructMethod(structSym, memberName) is not { } method) {
            SynthesizeCallArguments(call);
            Report(
                DiagnosticCodes.UnknownMember,
                staticAccess.MemberToken.Span,
                $"Struct '{structSym.Name}' does not have static method '{memberName}'.");
            return _types.Error;
          }

          if (!method.IsStatic) {
            SynthesizeCallArguments(call);
            Report(
                DiagnosticCodes.InvalidStaticOrInstanceAccess,
                staticAccess.Span,
                $"Method '{memberName}' of struct '{structSym.Name}' is not static.");
            return _types.Error;
          }

          _state.SetReferencedSymbol(staticAccess, method);
          ImmutableArray<TypeSymbol> ownerArgs = ResolveOptionalTypeArguments(
              staticAccess.TargetTypeArguments,
              structSym.TypeParameters,
              structSym.Name,
              staticAccess.Span);
          ImmutableArray<TypeParameterSymbol> ownerParams = ownerArgs.IsEmpty ? [] : structSym.TypeParameters;

          if (!method.TypeParameters.IsEmpty) {
            return CheckGenericCallableCall(
                call,
                method,
                "static method",
                method.Name,
                CallKind.StaticMethod,
                staticAccess,
                ownerParams,
                ownerArgs,
                method.TypeParameters,
                method.Parameters,
                () => EnsureCallableReturnType(method),
                hasImplicitSelf: false,
                expectedType);
          }

          if (call.TypeArguments is not null) {
            Report(
                DiagnosticCodes.TypeMismatch,
                call.TypeArguments.Span,
                $"Static method '{method.Name}' is not generic and does not accept type arguments.");
          }

          var expectedParamTypes = ImmutableArray.CreateBuilder<FunctionParameter>(method.Parameters.Length);
          foreach (ParameterSymbol param in method.Parameters) {
            expectedParamTypes.Add(new FunctionParameter(
                TypeSubstitution.Substitute(param.Type, ownerParams, ownerArgs, _types),
                param.IsMutable));
          }

          CheckCallArguments(call, expectedParamTypes, "static method", method.Name, parameterSymbols: method.Parameters);
          TypeSymbol rawRetType = EnsureCallableReturnType(method);
          TypeSymbol retType = TypeSubstitution.Substitute(rawRetType, ownerParams, ownerArgs, _types);
          _state.SetNodeType(staticAccess, _types.GetFunction(expectedParamTypes.ToImmutable(), retType));
          _state.CallResolutions[call.Id] = new CallResolution(CallKind.StaticMethod, TargetSymbol: method);
          RecordInstantiationsInType(retType);
          return retType;
        }

      case UnionSymbol unionSym:
        return CheckUnionConstructorCall(call, staticAccess, unionSym, memberName, expectedType, allowPartialInference: false);

      case EnumSymbol enumSym:
        SynthesizeCallArguments(call);
        Report(
            DiagnosticCodes.NotCallable,
            staticAccess.Span,
            $"Enum '{enumSym.Name}' members are not callable.");
        return _types.Error;

      default:
        SynthesizeCallArguments(call);
        Report(
            DiagnosticCodes.NotCallable,
            staticAccess.Span,
            $"'{targetSym.Name}' is not callable.");
        return _types.Error;
    }
  }

  private TypeSymbol CheckDeclaredFunctionCall(
      CallExpressionSyntax call,
      FunctionSymbol fnSym,
      ExpressionSyntax calleeNode,
      TypeSymbol? expectedType) {
    if (!fnSym.TypeParameters.IsEmpty) {
      return CheckGenericCallableCall(
          call,
          fnSym,
          "function",
          fnSym.Name,
          CallKind.DirectFunction,
          calleeNode,
          ownerTypeParams: [],
          ownerTypeArgs: [],
          fnSym.TypeParameters,
          fnSym.Parameters,
          () => EnsureCallableReturnType(fnSym),
          hasImplicitSelf: false,
          expectedType);
    }

    if (call.TypeArguments is not null) {
      Report(
          DiagnosticCodes.TypeMismatch,
          call.TypeArguments.Span,
          $"Function '{fnSym.Name}' is not generic and does not accept type arguments.");
    }

    var expectedParams = ImmutableArray.CreateBuilder<FunctionParameter>(fnSym.Parameters.Length);
    foreach (ParameterSymbol p in fnSym.Parameters) {
      expectedParams.Add(new FunctionParameter(p.Type, p.IsMutable));
    }

    CheckCallArguments(call, expectedParams, "function", fnSym.Name, parameterSymbols: fnSym.Parameters);
    TypeSymbol retType = EnsureCallableReturnType(fnSym);

    _state.SetNodeType(calleeNode, _types.GetFunction(expectedParams.ToImmutable(), retType));
    _state.CallResolutions[call.Id] = new CallResolution(CallKind.DirectFunction, TargetSymbol: fnSym);
    RecordInstantiationsInType(retType);
    return retType;
  }

  private TypeSymbol CheckMethodCallCore(
      CallExpressionSyntax call,
      MethodSymbol method,
      ImmutableArray<TypeParameterSymbol> ownerTypeParams,
      ImmutableArray<TypeSymbol> ownerTypeArgs,
      ExpressionSyntax calleeNode,
      bool hasImplicitSelf,
      TypeSymbol? expectedType) {
    MutAccessPlace? mutatingReceiver = null;
    if (method.IsMutating) {
      if (hasImplicitSelf) {
        mutatingReceiver = CheckImplicitSelfMutation(call, calleeNode.Span);
      } else if (calleeNode is MemberAccessExpressionSyntax memberCallee) {
        MutablePlaceResult receiverPlace = CheckMutablePlaceAndRecord(
            call,
            memberCallee.Target,
            MutationKind.MutatingCall,
            call.Span);
        if (receiverPlace.IsValid && !receiverPlace.WasSilent && receiverPlace.RootSymbol is { } recvRoot) {
          mutatingReceiver = new MutAccessPlace(
              MutAccessTarget.Receiver,
              recvRoot,
              receiverPlace.Steps,
              memberCallee.Target.Span);
        }
      }
    }

    if (!method.TypeParameters.IsEmpty) {
      return CheckGenericCallableCall(
          call,
          method,
          "method",
          method.Name,
          CallKind.InstanceMethod,
          calleeNode,
          ownerTypeParams,
          ownerTypeArgs,
          method.TypeParameters,
          method.Parameters,
          () => EnsureCallableReturnType(method),
          hasImplicitSelf,
          expectedType,
          mutatingReceiver);
    }

    if (call.TypeArguments is not null) {
      Report(
          DiagnosticCodes.TypeMismatch,
          call.TypeArguments.Span,
          $"Method '{method.Name}' is not generic and does not accept type arguments.");
    }

    var expectedParams = ImmutableArray.CreateBuilder<FunctionParameter>(method.Parameters.Length);
    foreach (ParameterSymbol p in method.Parameters) {
      expectedParams.Add(new FunctionParameter(
          TypeSubstitution.Substitute(p.Type, ownerTypeParams, ownerTypeArgs, _types),
          p.IsMutable));
    }

    CheckCallArguments(
        call,
        expectedParams,
        "method",
        method.Name,
        parameterSymbols: method.Parameters,
        mutatingReceiver: mutatingReceiver);
    TypeSymbol rawRetType = EnsureCallableReturnType(method);
    TypeSymbol retType = TypeSubstitution.Substitute(rawRetType, ownerTypeParams, ownerTypeArgs, _types);
    _state.SetNodeType(calleeNode, _types.GetFunction(expectedParams.ToImmutable(), retType));
    _state.CallResolutions[call.Id] = new CallResolution(
        CallKind.InstanceMethod,
        TargetSymbol: method,
        HasImplicitSelf: hasImplicitSelf);
    RecordInstantiationsInType(retType);
    return retType;
  }

  private TypeSymbol CheckUnionConstructorCall(
      CallExpressionSyntax call,
      StaticAccessExpressionSyntax staticAccess,
      UnionSymbol unionSym,
      string memberName,
      TypeSymbol? expectedType,
      bool allowPartialInference = false) {
    UnionVariantSymbol? variant = FindUnionVariant(unionSym, memberName);
    if (variant is null) {
      SynthesizeCallArguments(call);
      Report(
          DiagnosticCodes.InvalidIsPattern,
          staticAccess.MemberToken.Span,
          $"Union '{unionSym.Name}' has no variant '{memberName}'.");
      return _types.Error;
    }

    _state.SetReferencedSymbol(staticAccess, variant);

    bool isResult = ReferenceEquals(unionSym, _prelude.ResultUnion);
    bool isResultSuccess = isResult && ReferenceEquals(variant, _prelude.ResultSuccessVariant);

    // D-162 / ERR-E8: `Result[void, E]` must write bare `Result::Success`, not `Result::Success()`.
    if (isResultSuccess
        && call.Arguments.IsEmpty
        && staticAccess.TargetTypeArguments is null
        && expectedType is UnionType eu
        && ReferenceEquals(eu.Definition, _prelude.ResultUnion)
        && eu.TypeArguments.Length == 2
        && eu.TypeArguments[0].IsVoid) {
      Report(
          DiagnosticCodes.VariantArgumentMismatch,
          call.Span,
          "Cannot call 'Result::Success()' with empty parentheses for 'Result[void, E]'; write `Result::Success`.",
          help: "write `Result::Success`");
      _state.CallResolutions[call.Id] = new CallResolution(
          CallKind.UnionVariantConstructor,
          TargetSymbol: variant,
          ConstructedUnionType: eu);
      RecordGenericInstantiation(unionSym, eu.TypeArguments);
      return eu;
    }

    ImmutableArray<TypeSymbol> typeArgs = [];
    if (!unionSym.TypeParameters.IsEmpty) {
      if (staticAccess.TargetTypeArguments is not null) {
        typeArgs = ResolveOptionalTypeArguments(
            staticAccess.TargetTypeArguments,
            unionSym.TypeParameters,
            unionSym.Name,
            staticAccess.Span,
            isResultUnion: isResult);
        if (typeArgs.Length != unionSym.TypeParameters.Length || typeArgs.Any(static a => a.IsError)) {
          SynthesizeCallArguments(call);
          return _types.Error;
        }

        if (isResultSuccess && call.Arguments.IsEmpty && typeArgs[0].IsVoid) {
          UnionType explicitVoidRes = _types.GetUnion(unionSym, typeArgs);
          Report(
              DiagnosticCodes.VariantArgumentMismatch,
              call.Span,
              "Cannot call 'Result::Success()' with empty parentheses for 'Result[void, E]'; write `Result::Success`.",
              help: "write `Result::Success`");
          _state.CallResolutions[call.Id] = new CallResolution(
              CallKind.UnionVariantConstructor,
              TargetSymbol: variant,
              ConstructedUnionType: explicitVoidRes);
          RecordGenericInstantiation(unionSym, typeArgs);
          return explicitVoidRes;
        }
      } else {
        return InferGenericUnionConstructorCall(
            call,
            staticAccess,
            unionSym,
            variant,
            expectedType,
            allowPartialInference,
            out _);
      }
    }

    UnionType unionType = _types.GetUnion(unionSym, typeArgs);
    if (!IsMissing(call.CloseParenToken) && call.Arguments.Length != variant.Fields.Length) {
      Report(
          DiagnosticCodes.VariantArgumentMismatch,
          call.Span,
          $"Variant constructor '{unionSym.Name}::{variant.Name}' expected {variant.Fields.Length} arguments, but got {call.Arguments.Length}.");
    }

    int count = Math.Min(call.Arguments.Length, variant.Fields.Length);
    for (int i = 0; i < count; i++) {
      ArgumentSyntax arg = call.Arguments[i];
      if (arg.Expression is MissingExpressionSyntax) {
        continue;
      }

      TypeSymbol expectedFieldType = typeArgs.IsEmpty
          ? variant.Fields[i].Type
          : TypeSubstitution.Substitute(variant.Fields[i].Type, unionSym.TypeParameters, typeArgs, _types);
      TypeSymbol actualType = CheckWithContextualExpectation(arg.Expression, expectedFieldType);
      if (!TryConvert(arg.Expression, actualType, expectedFieldType)) {
        string code = expectedFieldType is InterfaceType
            ? DiagnosticCodes.InterfaceNotSatisfied
            : DiagnosticCodes.VariantArgumentMismatch;
        string detail = expectedFieldType is InterfaceType iface
            ? FormatInterfaceMismatchDetail(actualType, iface)
            : string.Empty;
        Report(
            code,
            arg.Expression.Span,
            $"Argument {i + 1} for variant constructor '{variant.Name}' expected type '{expectedFieldType.DisplayName}', but got '{actualType.DisplayName}'{detail}.");
      }
    }

    for (int i = count; i < call.Arguments.Length; i++) {
      if (call.Arguments[i].Expression is not MissingExpressionSyntax) {
        Synthesize(call.Arguments[i].Expression);
      }
    }

    CheckNoUnexpectedMutArguments(call, "variant constructor", variant.Name);

    _state.CallResolutions[call.Id] = new CallResolution(
        CallKind.UnionVariantConstructor,
        TargetSymbol: variant,
        ConstructedUnionType: unionType);
    _state.SetNodeType(staticAccess, unionType);
    if (!typeArgs.IsEmpty) {
      RecordGenericInstantiation(unionSym, typeArgs);
    }
    return unionType;
  }

  private void CheckFunctionTypeCall(
      CallExpressionSyntax call,
      FunctionType fnType,
      CallKind callKind,
      Symbol? targetSymbol,
      string calleeKind,
      string calleeName,
      bool hasImplicitSelf,
      Symbol? indexSymbol = null) {
    CheckCallArguments(call, fnType.Parameters, calleeKind, calleeName);
    _state.CallResolutions[call.Id] = new CallResolution(
        callKind,
        TargetSymbol: targetSymbol,
        HasImplicitSelf: hasImplicitSelf,
        IndexSymbol: indexSymbol);
  }

  private void CheckCallArguments(
      CallExpressionSyntax call,
      IReadOnlyList<FunctionParameter> expectedParameters,
      string calleeKind,
      string calleeName,
      IReadOnlyList<ParameterSymbol>? parameterSymbols = null,
      MutAccessPlace? mutatingReceiver = null) {
    string capitalizedKind = char.ToUpperInvariant(calleeKind[0]) + calleeKind[1..];
    if (!IsMissing(call.CloseParenToken) && call.Arguments.Length != expectedParameters.Count) {
      Report(
          DiagnosticCodes.WrongArgumentCount,
          call.Span,
          $"{capitalizedKind} '{calleeName}' expected {expectedParameters.Count} arguments, but got {call.Arguments.Length}.");
    }

    int count = Math.Min(call.Arguments.Length, expectedParameters.Count);
    for (int i = 0; i < count; i++) {
      ArgumentSyntax arg = call.Arguments[i];
      if (arg.Expression is MissingExpressionSyntax) {
        continue;
      }

      FunctionParameter expectedInfo = expectedParameters[i];
      TypeSymbol expectedParam = expectedInfo.Type;
      if (!expectedInfo.IsMutable && expectedParam is FunctionType expectedFn && ShouldWrapImplicitIt(arg.Expression)) {
        CheckImplicitItArgument(arg.Expression, expectedFn, i, calleeKind, calleeName);
        continue;
      }

      TypeSymbol actualType = CheckWithContextualExpectation(arg.Expression, expectedParam);
      bool typeOk = expectedInfo.IsMutable
          ? TryMatchMutArgumentType(actualType, expectedParam)
          : TryConvert(arg.Expression, actualType, expectedParam);
      if (!typeOk) {
        string code = !expectedInfo.IsMutable && expectedParam is InterfaceType
            ? DiagnosticCodes.InterfaceNotSatisfied
            : DiagnosticCodes.TypeMismatch;
        string detail = !expectedInfo.IsMutable && expectedParam is InterfaceType iface
            ? FormatInterfaceMismatchDetail(actualType, iface)
            : string.Empty;
        Report(
            code,
            arg.Expression.Span,
            $"Argument {i + 1} for {calleeKind} '{calleeName}' expected type '{expectedParam.DisplayName}', but got '{actualType.DisplayName}'{detail}.");
      }
    }

    for (int i = count; i < call.Arguments.Length; i++) {
      if (call.Arguments[i].Expression is not MissingExpressionSyntax) {
        Synthesize(call.Arguments[i].Expression);
      }
    }

    CheckCallArgumentMutability(
        call,
        count,
        expectedParameters,
        parameterSymbols,
        calleeKind,
        calleeName,
        mutatingReceiver);
  }

  private void SynthesizeCallArguments(CallExpressionSyntax call) {
    foreach (ArgumentSyntax arg in call.Arguments) {
      if (arg.Expression is not MissingExpressionSyntax) {
        Synthesize(arg.Expression);
      }
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Stdlib / Intrinsic dispatch (BUILTIN-1..16, STRLIB-1..19, VECLIB-1..25, MAPLIB-1..7, SETLIB-*, RGX-*)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol CheckIntrinsicCall(
      CallExpressionSyntax call,
      IntrinsicDescriptor descriptor,
      TypeSymbol? receiverType,
      Symbol? targetSymbol,
      TypeSymbol? expectedReturnType) {
    TypeSymbol[] receiverArgs = receiverType is not null ? GetReceiverTypeArgs(receiverType) : [];
    string calleeKind = receiverType is null ? "function" : "method";
    string capitalizedKind = receiverType is null ? "Function" : "Method";

    if (descriptor.MutatesReceiver && call.Callee is MemberAccessExpressionSyntax mutMember) {
      CheckMutablePlaceAndRecord(call, mutMember.Target, MutationKind.MutatingCall, call.Span);
    }

    CheckNoUnexpectedMutArguments(call, calleeKind, descriptor.Name);

    // 1. Variadic builtin: println(args...)
    if (descriptor.Signatures.Length == 1
        && descriptor.Signatures[0].Parameters.Length == 1
        && descriptor.Signatures[0].Parameters[0].IsVariadic) {
      IntrinsicSignature sig = descriptor.Signatures[0];
      foreach (ArgumentSyntax arg in call.Arguments) {
        if (arg.Expression is MissingExpressionSyntax) {
          continue;
        }

        TypeSymbol argType = Synthesize(arg.Expression);
        if (argType.IsVoid) {
          Report(
              DiagnosticCodes.TypeMismatch,
              arg.Expression.Span,
              $"Cannot pass a 'void' value to '{descriptor.Name}'.");
        }
      }

      _state.CallResolutions[call.Id] = new CallResolution(
          CallKind.Intrinsic,
          TargetSymbol: targetSymbol,
          Intrinsic: sig.Id);
      return sig.ReturnType.Resolve(_types, _prelude, receiverArgs, []);
    }

    // 2. Arity-disambiguated optional-argument globals: readline() vs readline(prompt), rand() vs rand(limit)
    if (descriptor.Receiver == StdlibReceiverKind.Global
        && descriptor.Name is "readline" or "rand") {
      if (call.Arguments.Length == 0) {
        IntrinsicSignature zeroSig = descriptor.Signatures[0];
        _state.CallResolutions[call.Id] = new CallResolution(
            CallKind.Intrinsic,
            TargetSymbol: targetSymbol,
            Intrinsic: zeroSig.Id);
        return zeroSig.ReturnType.Resolve(_types, _prelude, receiverArgs, []);
      }

      if (call.Arguments.Length == 1) {
        IntrinsicSignature oneSig = descriptor.Signatures[1];
        CheckIntrinsicSingleSignature(call, oneSig, receiverArgs, targetSymbol, calleeKind, descriptor.Name);
        return oneSig.ReturnType.Resolve(_types, _prelude, receiverArgs, []);
      }

      SynthesizeCallArguments(call);
      if (!IsMissing(call.CloseParenToken)) {
        Report(
            DiagnosticCodes.WrongArgumentCount,
            call.Span,
            $"{capitalizedKind} '{descriptor.Name}' expected 0 or 1 arguments, but got {call.Arguments.Length}.");
      }

      return descriptor.Signatures[0].ReturnType.Resolve(_types, _prelude, receiverArgs, []);
    }

    // 3. Type-disambiguated numeric/string conversion globals: int(v), float(v), rune(v)
    if (descriptor.Receiver == StdlibReceiverKind.Global
        && descriptor.Name is "int" or "float" or "rune") {
      if (call.Arguments.Length != 1) {
        SynthesizeCallArguments(call);
        if (!IsMissing(call.CloseParenToken)) {
          Report(
              DiagnosticCodes.WrongArgumentCount,
              call.Span,
              $"{capitalizedKind} '{descriptor.Name}' expected 1 arguments, but got {call.Arguments.Length}.");
        }

        return _types.Error;
      }

      ArgumentSyntax arg0 = call.Arguments[0];
      if (arg0.Expression is MissingExpressionSyntax) {
        return _types.Error;
      }

      TypeSymbol argType = Synthesize(arg0.Expression);
      if (IsSilent(argType)) {
        return _types.Error;
      }

      IntrinsicSignature? matched = null;
      if (descriptor.Name is "int" or "float") {
        if (argType is PrimitiveType { IsNumeric: true }) {
          matched = descriptor.Signatures[0];
        } else if (ReferenceEquals(argType, _types.String)) {
          matched = descriptor.Signatures[1];
        }
      } else {
        if (ReferenceEquals(argType, _types.Int)) {
          matched = descriptor.Signatures[0];
        } else if (ReferenceEquals(argType, _types.String)) {
          matched = descriptor.Signatures[1];
        }
      }

      if (matched is null) {
        string expectedDesc = descriptor.Name == "rune" ? "'int' or 'string'" : "numeric or 'string'";
        Report(
            DiagnosticCodes.TypeMismatch,
            arg0.Expression.Span,
            $"Argument 1 for {calleeKind} '{descriptor.Name}' expected {expectedDesc} value, but got '{argType.DisplayName}'.");
        return _types.Error;
      }

      _state.CallResolutions[call.Id] = new CallResolution(
          CallKind.Intrinsic,
          TargetSymbol: targetSymbol,
          Intrinsic: matched.Id);
      TypeSymbol convResult = matched.ReturnType.Resolve(_types, _prelude, receiverArgs, []);
      RecordInstantiationsInType(convResult);
      return convResult;
    }

    // 4. Vector 1- vs 2-arg callback methods (`each`, `map`, `filter`, `find`, `find_index`, `any`, `all`, `count`)
    if (descriptor.Receiver == StdlibReceiverKind.Vector && descriptor.Signatures.Length == 2) {
      TypeSymbol vecCbResult = CheckVectorCallbackIntrinsic(call, descriptor, receiverArgs, expectedReturnType);
      RecordInstantiationsInType(vecCbResult);
      return vecCbResult;
    }

    // 5. Set 1-arg callback methods (`any`, `all`, `each`)
    if (descriptor.Receiver == StdlibReceiverKind.Set
        && descriptor.Signatures.Length == 1
        && descriptor.Signatures[0].Parameters.Length == 1
        && descriptor.Signatures[0].Parameters[0].TypeSpec is StdlibTypeSpec.Function) {
      IntrinsicSignature sig = descriptor.Signatures[0];
      CheckCallbackIntrinsicSignature(call, sig, receiverArgs, descriptor.Name);
      TypeSymbol setCbResult = sig.ReturnType.Resolve(_types, _prelude, receiverArgs, []);
      RecordInstantiationsInType(setCbResult);
      return setCbResult;
    }

    // 6. Standard single-signature intrinsic (`str`, `type`, `time`, `sleep`, `clear`, `panic`, `regex`,
    //    `read_file`, `write_file`, `file_exists`, `string:*`, `vec:push/add/pop/join`, `map:*`, `set:add/remove`, `regex:*`)
    IntrinsicSignature primary = descriptor.Signatures[0];
    CheckIntrinsicSingleSignature(call, primary, receiverArgs, targetSymbol, calleeKind, descriptor.Name);
    TypeSymbol intrinsicResult = primary.ReturnType.Resolve(_types, _prelude, receiverArgs, []);
    RecordInstantiationsInType(intrinsicResult);
    return intrinsicResult;
  }

  private void CheckIntrinsicSingleSignature(
      CallExpressionSyntax call,
      IntrinsicSignature sig,
      TypeSymbol[] receiverArgs,
      Symbol? targetSymbol,
      string calleeKind,
      string calleeName) {
    string capitalizedKind = char.ToUpperInvariant(calleeKind[0]) + calleeKind[1..];
    if (!IsMissing(call.CloseParenToken) && call.Arguments.Length != sig.Parameters.Length) {
      Report(
          DiagnosticCodes.WrongArgumentCount,
          call.Span,
          $"{capitalizedKind} '{calleeName}' expected {sig.Parameters.Length} arguments, but got {call.Arguments.Length}.");
    }

    int count = Math.Min(call.Arguments.Length, sig.Parameters.Length);
    for (int i = 0; i < count; i++) {
      ArgumentSyntax arg = call.Arguments[i];
      if (arg.Expression is MissingExpressionSyntax) {
        continue;
      }

      IntrinsicParameter param = sig.Parameters[i];
      if (param.Kind == IntrinsicParamKind.AnyValue) {
        TypeSymbol anyType = Synthesize(arg.Expression);
        if (anyType.IsVoid) {
          Report(
              DiagnosticCodes.TypeMismatch,
              arg.Expression.Span,
              $"Argument {i + 1} for {calleeKind} '{calleeName}' cannot be 'void'.");
        }
        continue;
      }

      TypeSymbol expectedParam = param.TypeSpec.Resolve(_types, _prelude, receiverArgs, []);
      if (expectedParam is FunctionType expectedFn && ShouldWrapImplicitIt(arg.Expression)) {
        CheckImplicitItArgument(arg.Expression, expectedFn, i, calleeKind, calleeName);
        continue;
      }

      TypeSymbol actualType = CheckWithContextualExpectation(arg.Expression, expectedParam);
      if (!TryConvert(arg.Expression, actualType, expectedParam)) {
        string code = expectedParam is InterfaceType
            ? DiagnosticCodes.InterfaceNotSatisfied
            : DiagnosticCodes.TypeMismatch;
        string detail = expectedParam is InterfaceType iface
            ? FormatInterfaceMismatchDetail(actualType, iface)
            : string.Empty;
        Report(
            code,
            arg.Expression.Span,
            $"Argument {i + 1} for {calleeKind} '{calleeName}' expected type '{expectedParam.DisplayName}', but got '{actualType.DisplayName}'{detail}.");
      }
    }

    for (int i = count; i < call.Arguments.Length; i++) {
      if (call.Arguments[i].Expression is not MissingExpressionSyntax) {
        Synthesize(call.Arguments[i].Expression);
      }
    }

    _state.CallResolutions[call.Id] = new CallResolution(
        CallKind.Intrinsic,
        TargetSymbol: targetSymbol,
        Intrinsic: sig.Id);
  }

  private TypeSymbol CheckVectorCallbackIntrinsic(
      CallExpressionSyntax call,
      IntrinsicDescriptor descriptor,
      TypeSymbol[] receiverArgs,
      TypeSymbol? expectedReturnType) {
    IntrinsicSignature sig1 = descriptor.Signatures[0];
    IntrinsicSignature sig2 = descriptor.Signatures[1];

    if (call.Arguments.Length != 1) {
      SynthesizeCallArguments(call);
      if (!IsMissing(call.CloseParenToken)) {
        Report(
            DiagnosticCodes.WrongArgumentCount,
            call.Span,
            $"Method '{descriptor.Name}' expected 1 arguments, but got {call.Arguments.Length}.");
      }

      return _types.Error;
    }

    ArgumentSyntax arg0 = call.Arguments[0];
    if (arg0.Expression is MissingExpressionSyntax) {
      return _types.Error;
    }

    TypeSymbol? explicitMapReturn = null;
    if (!sig1.MethodTypeParameterNames.IsEmpty && call.TypeArguments is not null) {
      if (call.TypeArguments.Arguments.Length != 1) {
        Report(
            DiagnosticCodes.TypeMismatch,
            call.TypeArguments.Span,
            $"Generic '{descriptor.Name}' expects 1 type argument(s), but got {call.TypeArguments.Arguments.Length}.");
      } else {
        explicitMapReturn = _session.ResolveType(_state, call.TypeArguments.Arguments[0], _scope, TypePosition.GenericTypeArgument);
      }
    }

    TypeSymbol? expectedCallbackReturnForMap = explicitMapReturn
        ?? (expectedReturnType is VectorType ev ? ev.ElementType : null);
    TypeSymbol elemType = receiverArgs[0];

    // 1. Implicit `it` argument (FN-16, VECLIB-24, design §6.3): always selects 1-arg signature sig1.
    if (ShouldWrapImplicitIt(arg0.Expression)) {
      if (!sig1.MethodTypeParameterNames.IsEmpty) {
        // `map(it * 3)`: 1-arg callback with element type T, return type R inferred from body.
        TypeSymbol implicitFn = CheckImplicitItCore(
            arg0.Expression,
            elemType,
            expectedReturn: expectedCallbackReturnForMap,
            expectedFunctionForMismatch: explicitMapReturn is not null && !explicitMapReturn.IsError
                ? _types.GetFunction([new FunctionParameter(elemType, IsMutable: false)], explicitMapReturn)
                : null,
            argIndex: 0,
            calleeKind: "method",
            calleeName: descriptor.Name);
        TypeSymbol rType = explicitMapReturn is not null && !explicitMapReturn.IsError
            ? explicitMapReturn
            : (implicitFn is FunctionType fnT ? fnT.ReturnType : _types.Error);
        if (rType.IsVoid) {
          Report(
              DiagnosticCodes.InvalidVoidTypePosition,
              arg0.Expression.Span,
              "Callback for 'map' cannot return 'void'.");
          rType = _types.Error;
        }

        _state.CallResolutions[call.Id] = new CallResolution(CallKind.Intrinsic, Intrinsic: sig1.Id);
        return rType.IsError ? _types.Error : sig1.ReturnType.Resolve(_types, _prelude, receiverArgs, [rType]);
      }

      var expected1Fn = (FunctionType) sig1.Parameters[0].TypeSpec.Resolve(_types, _prelude, receiverArgs, []);
      CheckImplicitItArgument(arg0.Expression, expected1Fn, argIndex: 0, calleeKind: "method", calleeName: descriptor.Name);
      _state.CallResolutions[call.Id] = new CallResolution(CallKind.Intrinsic, Intrinsic: sig1.Id);
      return sig1.ReturnType.Resolve(_types, _prelude, receiverArgs, []);
    }

    // 2. Lambda argument (D-167, design §6.3): select 1- vs 2-arg signature by lambda.Parameters.Length.
    if (UnwrapParens(arg0.Expression) is LambdaExpressionSyntax lambda) {
      IntrinsicSignature? selectedByArity = lambda.Parameters.Length switch {
        1 => sig1,
        2 => sig2,
        _ => null,
      };

      if (selectedByArity is not null && !selectedByArity.MethodTypeParameterNames.IsEmpty) {
        // `map`: push parameter types [T] or [T, int] into untyped lambda and infer R from return type.
        ImmutableArray<FunctionParameter> mapParamTypes = lambda.Parameters.Length == 1
            ? [new FunctionParameter(elemType, IsMutable: false)]
            : [new FunctionParameter(elemType, IsMutable: false), new FunctionParameter(_types.Int, IsMutable: false)];
        TypeSymbol lambdaType = SynthesizeLambda(
            lambda,
            expectedFunction: null,
            expectedParamsOverride: mapParamTypes,
            expectedReturnOverride: expectedCallbackReturnForMap);
        SetWrappedExpressionType(arg0.Expression, lambdaType);

        if (lambdaType is FunctionType fnMapCallback) {
          TypeSymbol rType = explicitMapReturn is not null && !explicitMapReturn.IsError
              ? explicitMapReturn
              : fnMapCallback.ReturnType;
          if (rType.IsVoid) {
            Report(
                DiagnosticCodes.InvalidVoidTypePosition,
                arg0.Expression.Span,
                "Callback for 'map' cannot return 'void'.");
            rType = _types.Error;
          }

          TypeSymbol[] methodArgs = [rType];
          TypeSymbol expectedFn = selectedByArity.Parameters[0].TypeSpec.Resolve(_types, _prelude, receiverArgs, methodArgs);
          if (!TryConvert(arg0.Expression, fnMapCallback, expectedFn)) {
            Report(
                DiagnosticCodes.TypeMismatch,
                arg0.Expression.Span,
                $"Argument 1 for method '{descriptor.Name}' expected type '{expectedFn.DisplayName}', but got '{fnMapCallback.DisplayName}'.");
          }

          _state.CallResolutions[call.Id] = new CallResolution(CallKind.Intrinsic, Intrinsic: selectedByArity.Id);
          return rType.IsError ? _types.Error : selectedByArity.ReturnType.Resolve(_types, _prelude, receiverArgs, methodArgs);
        }

        return _types.Error;
      }

      if (selectedByArity is not null) {
        var expectedCallbackFn = (FunctionType) selectedByArity.Parameters[0].TypeSpec.Resolve(_types, _prelude, receiverArgs, []);
        TypeSymbol lambdaType = SynthesizeLambda(lambda, expectedCallbackFn);
        SetWrappedExpressionType(arg0.Expression, lambdaType);
        if (!TryConvert(arg0.Expression, lambdaType, expectedCallbackFn)) {
          Report(
              DiagnosticCodes.TypeMismatch,
              arg0.Expression.Span,
              $"Argument 1 for method '{descriptor.Name}' expected type '{expectedCallbackFn.DisplayName}', but got '{lambdaType.DisplayName}'.");
        }

        _state.CallResolutions[call.Id] = new CallResolution(CallKind.Intrinsic, Intrinsic: selectedByArity.Id);
        return selectedByArity.ReturnType.Resolve(_types, _prelude, receiverArgs, []);
      }

      // Wrong lambda arity (0 or >= 3): synthesize without expected signature and report mismatch.
      TypeSymbol wrongArityType = SynthesizeLambda(lambda, expectedFunction: null);
      SetWrappedExpressionType(arg0.Expression, wrongArityType);
      TypeSymbol expected1Desc = sig1.MethodTypeParameterNames.IsEmpty
          ? sig1.Parameters[0].TypeSpec.Resolve(_types, _prelude, receiverArgs, [])
          : _types.GetFunction([new FunctionParameter(elemType, IsMutable: false)], _types.Error);
      Report(
          DiagnosticCodes.TypeMismatch,
          arg0.Expression.Span,
          $"Argument 1 for method '{descriptor.Name}' expected type '{expected1Desc.DisplayName}', but got '{wrongArityType.DisplayName}'.");
      return _types.Error;
    }

    // 3. Non-lambda expression (e.g., function value variable): synthesize and match arity.
    TypeSymbol callbackType = Synthesize(arg0.Expression);
    if (IsSilent(callbackType) || callbackType is PendingReturnType) {
      return _types.Error;
    }

    if (callbackType is not FunctionType fnCallback) {
      IntrinsicSignature fallbackSig = sig1;
      TypeSymbol expectedFnType = sig1.MethodTypeParameterNames.IsEmpty
          ? fallbackSig.Parameters[0].TypeSpec.Resolve(_types, _prelude, receiverArgs, [])
          : _types.GetFunction([new FunctionParameter(receiverArgs[0], IsMutable: false)], _types.Error);
      Report(
          DiagnosticCodes.TypeMismatch,
          arg0.Expression.Span,
          $"Argument 1 for method '{descriptor.Name}' expected type '{expectedFnType.DisplayName}', but got '{callbackType.DisplayName}'.");
      return _types.Error;
    }

    IntrinsicSignature selected;
    if (fnCallback.Parameters.Length == 1) {
      selected = sig1;
    } else if (fnCallback.Parameters.Length == 2) {
      selected = sig2;
    } else {
      TypeSymbol expected1 = sig1.MethodTypeParameterNames.IsEmpty
          ? sig1.Parameters[0].TypeSpec.Resolve(_types, _prelude, receiverArgs, [])
          : _types.GetFunction([new FunctionParameter(receiverArgs[0], IsMutable: false)], _types.Error);
      Report(
          DiagnosticCodes.TypeMismatch,
          arg0.Expression.Span,
          $"Argument 1 for method '{descriptor.Name}' expected type '{expected1.DisplayName}', but got '{callbackType.DisplayName}'.");
      return _types.Error;
    }

    if (!selected.MethodTypeParameterNames.IsEmpty) {
      // `map`: infer method type parameter R from the callback's return type (or use explicit `[R]`).
      TypeSymbol rType = explicitMapReturn is not null && !explicitMapReturn.IsError
          ? explicitMapReturn
          : fnCallback.ReturnType;
      if (rType.IsVoid) {
        Report(
            DiagnosticCodes.InvalidVoidTypePosition,
            arg0.Expression.Span,
            "Callback for 'map' cannot return 'void'.");
        rType = _types.Error;
      }

      TypeSymbol[] methodArgs = [rType];
      TypeSymbol expectedFn = selected.Parameters[0].TypeSpec.Resolve(_types, _prelude, receiverArgs, methodArgs);
      if (!TryConvert(arg0.Expression, fnCallback, expectedFn)) {
        Report(
            DiagnosticCodes.TypeMismatch,
            arg0.Expression.Span,
            $"Argument 1 for method '{descriptor.Name}' expected type '{expectedFn.DisplayName}', but got '{fnCallback.DisplayName}'.");
      }

      _state.CallResolutions[call.Id] = new CallResolution(CallKind.Intrinsic, Intrinsic: selected.Id);
      return rType.IsError ? _types.Error : selected.ReturnType.Resolve(_types, _prelude, receiverArgs, methodArgs);
    }

    TypeSymbol expectedCallback = selected.Parameters[0].TypeSpec.Resolve(_types, _prelude, receiverArgs, []);
    if (!TryConvert(arg0.Expression, fnCallback, expectedCallback)) {
      Report(
          DiagnosticCodes.TypeMismatch,
          arg0.Expression.Span,
          $"Argument 1 for method '{descriptor.Name}' expected type '{expectedCallback.DisplayName}', but got '{fnCallback.DisplayName}'.");
    }

    _state.CallResolutions[call.Id] = new CallResolution(CallKind.Intrinsic, Intrinsic: selected.Id);
    return selected.ReturnType.Resolve(_types, _prelude, receiverArgs, []);
  }

  private void CheckCallbackIntrinsicSignature(
      CallExpressionSyntax call,
      IntrinsicSignature sig,
      TypeSymbol[] receiverArgs,
      string methodName) {
    if (!IsMissing(call.CloseParenToken) && call.Arguments.Length != 1) {
      SynthesizeCallArguments(call);
      Report(
          DiagnosticCodes.WrongArgumentCount,
          call.Span,
          $"Method '{methodName}' expected 1 arguments, but got {call.Arguments.Length}.");
      return;
    }

    if (call.Arguments.IsEmpty) {
      return;
    }

    ArgumentSyntax arg0 = call.Arguments[0];
    if (arg0.Expression is MissingExpressionSyntax) {
      return;
    }

    var expectedFn = (FunctionType) sig.Parameters[0].TypeSpec.Resolve(_types, _prelude, receiverArgs, []);
    if (ShouldWrapImplicitIt(arg0.Expression)) {
      CheckImplicitItArgument(arg0.Expression, expectedFn, argIndex: 0, calleeKind: "method", calleeName: methodName);
      _state.CallResolutions[call.Id] = new CallResolution(CallKind.Intrinsic, Intrinsic: sig.Id);
      return;
    }

    TypeSymbol actualType = CheckWithContextualExpectation(arg0.Expression, expectedFn);
    if (!TryConvert(arg0.Expression, actualType, expectedFn)) {
      Report(
          DiagnosticCodes.TypeMismatch,
          arg0.Expression.Span,
          $"Argument 1 for method '{methodName}' expected type '{expectedFn.DisplayName}', but got '{actualType.DisplayName}'.");
    }

    _state.CallResolutions[call.Id] = new CallResolution(CallKind.Intrinsic, Intrinsic: sig.Id);
  }

  private void SetWrappedExpressionType(ExpressionSyntax expression, TypeSymbol type) {
    ExpressionSyntax current = expression;
    while (true) {
      _state.SetNodeType(current, type);
      if (current is ParenthesizedExpressionSyntax paren) {
        current = paren.Expression;
      } else {
        break;
      }
    }
  }
}
