using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  // ---------------------------------------------------------------------------------------------
  // Generic bounds & instantiation recording (D-072, GEN-1..11, GEN-E1..E9, UNION-6..11)
  // ---------------------------------------------------------------------------------------------

  private void CheckGenericBounds(
      ImmutableArray<TypeParameterSymbol> typeParameters,
      ImmutableArray<TypeSymbol> typeArguments,
      ImmutableArray<SourceSpan> argSpans,
      SourceSpan fallbackSpan) {
    int count = Math.Min(typeParameters.Length, typeArguments.Length);
    for (int i = 0; i < count; i++) {
      TypeParameterSymbol tp = typeParameters[i];
      TypeSymbol argType = typeArguments[i];
      if (tp.Constraint is not { } rawConstraint || argType.IsError || rawConstraint.IsError) {
        continue;
      }

      TypeSymbol substituted = TypeSubstitution.Substitute(rawConstraint, typeParameters, typeArguments, _types);
      if (substituted is not InterfaceType constraint || constraint.IsError) {
        continue;
      }

      if (argType is StructType argStruct) {
        EnsureStructMethodsResolved(argStruct.Definition);
      }

      if (!TypeCompatibility.TrySatisfiesInterface(
          argType,
          constraint,
          _types,
          out string? detail,
          _state.Module,
          _session.InterfaceSatisfactionCache)) {
        SourceSpan span = i < argSpans.Length ? argSpans[i] : fallbackSpan;
        string suffix = detail is not null ? $" ({detail})" : string.Empty;
        Report(
            DiagnosticCodes.GenericConstraintNotSatisfied,
            span,
            $"Type '{argType.DisplayName}' does not satisfy generic constraint '{constraint.DisplayName}' for parameter '{tp.Name}'{suffix}.");
      }
    }
  }

  private void RecordGenericInstantiation(Symbol definition, ImmutableArray<TypeSymbol> typeArguments) {
    if (_inPhase3a > 0 || typeArguments.IsEmpty) {
      return;
    }

    _session.RecordGenericInstantiation(definition, typeArguments);
  }

  private void RecordInstantiationsInType(TypeSymbol type) {
    if (_inPhase3a > 0) {
      return;
    }

    _session.RecordInstantiationsInType(type);
  }

  private void ReportUnifyConflict(
      Unifier.UnifyConflict conflict,
      SourceSpan siteSpan,
      string fallbackMessage) {
    if (conflict.Parameter is { } tp && !conflict.IsOccursCheck) {
      ImmutableArray<RelatedLocation> related = conflict.FirstBindingSpan.Length > 0
          ? [new RelatedLocation(conflict.FirstBindingSpan, $"'{tp.Name}' was first inferred as '{conflict.ExpectedOrFirst.DisplayName}' here", _state.FilePath)]
          : [];
      Report(
          DiagnosticCodes.InconsistentGenericInference,
          siteSpan,
          $"Inconsistent types for generic parameter '{tp.Name}': '{conflict.ExpectedOrFirst.DisplayName}' vs '{conflict.Actual.DisplayName}'.",
          related);
      return;
    }

    Report(DiagnosticCodes.TypeMismatch, siteSpan, fallbackMessage);
  }

  /// <summary>
  /// Expressions that rely on contextual type information and should be checked in Pass 2 after
  /// concrete arguments/fields have solved generic type variables (D-167, UNION-9, UNION-11).
  /// </summary>
  private bool IsDeferredGenericExpression(ExpressionSyntax expr, TypeSymbol expectedPattern) {
    ExpressionSyntax unwrapped = UnwrapParens(expr);

    if (unwrapped is LambdaExpressionSyntax) {
      return true;
    }

    if (expectedPattern is FunctionType && ShouldWrapImplicitIt(expr)) {
      return true;
    }

    if (unwrapped is VectorLiteralExpressionSyntax { Elements.IsEmpty: true }) {
      return true;
    }

    if (unwrapped is StaticAccessExpressionSyntax { TargetTypeArguments: null } staticAccess
        && !IsMissing(staticAccess.MemberToken)
        && IsGenericUnionTarget(staticAccess.Target)) {
      return true;
    }

    if (unwrapped is CallExpressionSyntax { Callee: StaticAccessExpressionSyntax { TargetTypeArguments: null } callStatic }
        && !IsMissing(callStatic.MemberToken)
        && IsGenericUnionTarget(callStatic.Target)) {
      return true;
    }

    if (unwrapped is StructLiteralExpressionSyntax { TypeName: NamedTypeSyntax or QualifiedTypeSyntax } structLit
        && TryPeekGenericStructSymbol(structLit.TypeName) is not null) {
      return true;
    }

    return false;
  }

  private bool IsGenericUnionTarget(ExpressionSyntax target) {
    if (target is IdentifierExpressionSyntax id && !IsMissing(id.IdentifierToken)) {
      return _scope.TryLookup(NameOf(id.IdentifierToken), out Symbol? sym)
          && sym is UnionSymbol { TypeParameters.IsEmpty: false };
    }

    if (target is StaticAccessExpressionSyntax { Target: IdentifierExpressionSyntax modId } inner
        && !IsMissing(modId.IdentifierToken)
        && !IsMissing(inner.MemberToken)
        && _state.ImportedModules.TryGetValue(NameOf(modId.IdentifierToken), out ModuleSymbol? mod)) {
      return CheckerSession.FindModuleDeclaration(mod, NameOf(inner.MemberToken)) is UnionSymbol { TypeParameters.IsEmpty: false };
    }

    return false;
  }

  private StructSymbol? TryPeekGenericStructSymbol(TypeSyntax typeSyntax) {
    switch (typeSyntax) {
      case NamedTypeSyntax named when !IsMissing(named.NameToken): {
          string name = NameOf(named.NameToken);
          return _scope.TryLookup(name, out Symbol? sym) && sym is StructSymbol { TypeParameters.IsEmpty: false } s
              ? s
              : null;
        }

      case QualifiedTypeSyntax qual when !IsMissing(qual.ModuleToken) && !IsMissing(qual.NameToken): {
          string modName = NameOf(qual.ModuleToken);
          string memberName = NameOf(qual.NameToken);
          if (_state.ImportedModules.TryGetValue(modName, out ModuleSymbol? mod)
              && CheckerSession.FindModuleDeclaration(mod, memberName) is StructSymbol { TypeParameters.IsEmpty: false } s) {
            return s;
          }
          return null;
        }

      default:
        return null;
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Generic Struct Literals (GEN-1..4, GEN-E1..E3, GEN-E5)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol InferGenericStructLiteral(
      StructLiteralExpressionSyntax literal,
      StructSymbol structSym,
      TypeSymbol? expectedType) {
    // Verify cross-module visibility for qualified struct names.
    if (literal.TypeName is QualifiedTypeSyntax qual
        && !IsMissing(qual.ModuleToken)
        && !IsMissing(qual.NameToken)) {
      string modName = NameOf(qual.ModuleToken);
      string memberName = NameOf(qual.NameToken);
      if (_state.ImportedModules.TryGetValue(modName, out ModuleSymbol? targetMod)
          && !IsModuleSymbolAccessible(targetMod, structSym)) {
        Report(
            DiagnosticCodes.SymbolNotPublic,
            qual.Span,
            $"Struct '{modName}::{memberName}' is not public.");
        SynthesizeStructLiteralFieldsFallback(literal);
        return _types.Error;
      }
    }

    _state.SetReferencedSymbol(literal.TypeName, structSym);

    ImmutableArray<TypeParameterSymbol> structParams = structSym.TypeParameters;
    var unifier = new Unifier(_types);
    var freshVars = ImmutableArray.CreateBuilder<TypeSymbol>(structParams.Length);
    var freshVarList = new TypeVariableType[structParams.Length];
    for (int i = 0; i < structParams.Length; i++) {
      TypeVariableType tv = unifier.Fresh(structParams[i], literal.TypeName.Span);
      freshVarList[i] = tv;
      freshVars.Add(tv);
    }

    ImmutableArray<TypeSymbol> freshVarTypes = freshVars.MoveToImmutable();

    var fieldsByName = new Dictionary<string, FieldSymbol>(structSym.AllFields.Length, StringComparer.Ordinal);
    foreach (FieldSymbol field in structSym.AllFields) {
      fieldsByName.TryAdd(field.Name, field);
    }

    var seenFields = new HashSet<string>(StringComparer.Ordinal);
    var resolvedInits = new List<(StructFieldInitSyntax Init, FieldSymbol Field, TypeSymbol PatternType, bool IsDeferred)>(literal.Fields.Length);
    bool reportedPrivateField = false;
    bool hadError = false;

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
        hadError = true;
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
        hadError = true;
        continue;
      }

      _state.SetReferencedSymbol(fieldInit, fieldSymbol);
      if (!IsFieldVisibleInCurrentModule(fieldSymbol)) {
        reportedPrivateField = true;
        ReportPrivateField(fieldInit.Span, structSym.Name, fieldSymbol);
      }

      TypeSymbol patternType = TypeSubstitution.Substitute(fieldSymbol.Type, structParams, freshVarTypes, _types);
      bool isDeferred = fieldInit.Value is { } valExpr
          && valExpr is not MissingExpressionSyntax
          && IsDeferredGenericExpression(valExpr, patternType);
      resolvedInits.Add((fieldInit, fieldSymbol, patternType, isDeferred));
    }

    var fieldActualTypes = new Dictionary<int, TypeSymbol>();
    var conflictReportedIds = new HashSet<int>();

    // Pass 1: Non-deferred field values & shorthands.
    foreach ((StructFieldInitSyntax fieldInit, FieldSymbol fieldSym, TypeSymbol patternType, bool isDeferred) in resolvedInits) {
      if (isDeferred) {
        continue;
      }

      if (fieldInit.Value is { } valueExpr) {
        if (valueExpr is MissingExpressionSyntax) {
          continue;
        }

        TypeSymbol actualType = Synthesize(valueExpr);
        fieldActualTypes[fieldInit.Id] = actualType;
        if (actualType.IsError) {
          hadError = true;
          continue;
        }

        if (!unifier.Unify(patternType, actualType, valueExpr.Span, out Unifier.UnifyConflict? conflict, allowIntToFloat: true)
            && conflict is { } c) {
          conflictReportedIds.Add(fieldInit.Id);
          hadError = true;
          ReportUnifyConflict(
              c,
              valueExpr.Span,
              $"Cannot assign type '{actualType.DisplayName}' to field '{fieldSym.Name}' of type '{unifier.ZonkPartial(patternType).DisplayName}' in struct '{structSym.Name}'.");
        }
      } else if (fieldInit.ColonToken is null) {
        if (!TryLookupAndRecordCapture(fieldSym.Name, out Symbol? localSym, reportSpan: fieldInit.NameToken.Span)) {
          if (_tolerantNames == 0) {
            Report(
                DiagnosticCodes.UndeclaredIdentifier,
                fieldInit.NameToken.Span,
                $"Identifier '{fieldSym.Name}' is not declared.");
          }
          hadError = true;
        } else {
          _state.SetShorthandValueSymbol(fieldInit, localSym);
          TypeSymbol actualType = SymbolValueType(localSym);
          fieldActualTypes[fieldInit.Id] = actualType;
          if (!unifier.Unify(patternType, actualType, fieldInit.Span, out Unifier.UnifyConflict? conflict, allowIntToFloat: true)
              && conflict is { } c) {
            conflictReportedIds.Add(fieldInit.Id);
            hadError = true;
            ReportUnifyConflict(
                c,
                fieldInit.Span,
                $"Cannot assign type '{actualType.DisplayName}' to field '{fieldSym.Name}' of type '{unifier.ZonkPartial(patternType).DisplayName}' in struct '{structSym.Name}'.");
          }
        }
      }
    }

    // Pass 1b: Contextual expected struct type fills any remaining unbound type variables.
    if (expectedType is StructType expectedStruct && ReferenceEquals(expectedStruct.Definition, structSym)) {
      StructType patternStruct = _types.GetStruct(structSym, freshVarTypes);
      unifier.BindUnboundFromExpected(patternStruct, expectedStruct, literal.Span);
    }

    // Pass 2: Deferred field values (lambdas, generic union variants, nested generic structs, etc.).
    var deferredZonkNodes = new List<(ExpressionSyntax Expr, TypeSymbol Type)>();
    foreach ((StructFieldInitSyntax fieldInit, FieldSymbol fieldSym, TypeSymbol patternType, bool isDeferred) in resolvedInits) {
      if (!isDeferred || fieldInit.Value is not { } valueExpr) {
        continue;
      }

      TypeSymbol partialExpected = unifier.ZonkPartial(patternType);
      TypeSymbol actualType = CheckDeferredGenericExpression(valueExpr, partialExpected, unifier, deferredZonkNodes, ref hadError);
      fieldActualTypes[fieldInit.Id] = actualType;
      if (actualType.IsError) {
        hadError = true;
        continue;
      }

      if (!unifier.Unify(patternType, actualType, valueExpr.Span, out Unifier.UnifyConflict? conflict, allowIntToFloat: true)
          && conflict is { } c) {
        conflictReportedIds.Add(fieldInit.Id);
        hadError = true;
        ReportUnifyConflict(
            c,
            valueExpr.Span,
            $"Cannot assign type '{actualType.DisplayName}' to field '{fieldSym.Name}' of type '{unifier.ZonkPartial(patternType).DisplayName}' in struct '{structSym.Name}'.");
      }
    }

    // Finalize type arguments (zonk everything so zero TypeVariableType escapes).
    var finalArgsBuilder = ImmutableArray.CreateBuilder<TypeSymbol>(structParams.Length);
    bool anyUnbound = false;
    for (int i = 0; i < structParams.Length; i++) {
      TypeVariableType tv = freshVarList[i];
      if (!unifier.IsBound(tv)) {
        anyUnbound = true;
        if (!hadError) {
          Report(
              DiagnosticCodes.CannotInferGenericArguments,
              literal.TypeName.Span,
              $"Cannot infer generic type argument for parameter '{structParams[i].Name}' of struct '{structSym.Name}'.");
        }
        unifier.ForceBind(tv, _types.Error, literal.TypeName.Span);
      }

      finalArgsBuilder.Add(unifier.Zonk(tv));
    }

    FinalizeDeferredZonkNodes(deferredZonkNodes, unifier);

    ImmutableArray<TypeSymbol> finalArgs = finalArgsBuilder.MoveToImmutable();

    // Record field types and verify final conversions (e.g., IntToFloat, StructToInterface).
    foreach ((StructFieldInitSyntax fieldInit, FieldSymbol fieldSym, _, _) in resolvedInits) {
      TypeSymbol concreteFieldType = TypeSubstitution.Substitute(fieldSym.Type, structParams, finalArgs, _types);
      _state.SetNodeType(fieldInit, concreteFieldType);

      if (conflictReportedIds.Contains(fieldInit.Id)) {
        continue;
      }

      if (fieldActualTypes.TryGetValue(fieldInit.Id, out TypeSymbol? rawActual)) {
        TypeSymbol actualType = unifier.Zonk(rawActual);
        SyntaxNode targetNode = fieldInit.Value is { } v ? v : fieldInit;
        if (!TryConvert(targetNode, actualType, concreteFieldType)) {
          if (concreteFieldType is InterfaceType expectedIface) {
            string detail = FormatInterfaceMismatchDetail(actualType, expectedIface);
            Report(
                DiagnosticCodes.InterfaceNotSatisfied,
                targetNode.Span,
                $"Cannot assign type '{actualType.DisplayName}' to field '{fieldSym.Name}' in struct '{structSym.Name}'; expected type '{concreteFieldType.DisplayName}', but got '{actualType.DisplayName}'{detail}.");
          } else {
            Report(
                DiagnosticCodes.TypeMismatch,
                targetNode.Span,
                $"Cannot assign type '{actualType.DisplayName}' to field '{fieldSym.Name}' of type '{concreteFieldType.DisplayName}' in struct '{structSym.Name}'.");
          }
        }
      }
    }

    // Completeness & cross-module private-field checks (D-078, D-163).
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
          return anyUnbound || finalArgs.Any(static a => a.IsError)
              ? _types.Error
              : _types.GetStruct(structSym, finalArgs);
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

    if (anyUnbound || finalArgs.Any(static a => a.IsError)) {
      _state.SetNodeType(literal.TypeName, _types.Error);
      return _types.Error;
    }

    CheckGenericBounds(structParams, finalArgs, [], literal.TypeName.Span);
    StructType resultType = _types.GetStruct(structSym, finalArgs);
    _state.SetNodeType(literal.TypeName, resultType);
    RecordGenericInstantiation(structSym, finalArgs);
    return resultType;
  }

  private void SynthesizeStructLiteralFieldsFallback(StructLiteralExpressionSyntax literal) {
    foreach (StructFieldInitSyntax field in literal.Fields) {
      if (field.Value is { } valExpr) {
        Synthesize(valExpr);
      }
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Generic Function & Method Calls (GEN-5..11, GEN-E1, GEN-E5, GEN-E7)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol CheckGenericCallableCall(
      CallExpressionSyntax call,
      Symbol callableSymbol,
      string calleeKind,
      string calleeName,
      CallKind callKind,
      ExpressionSyntax calleeNode,
      ImmutableArray<TypeParameterSymbol> ownerTypeParams,
      ImmutableArray<TypeSymbol> ownerTypeArgs,
      ImmutableArray<TypeParameterSymbol> methodTypeParams,
      ImmutableArray<ParameterSymbol> parameters,
      Func<TypeSymbol> getRawReturnType,
      bool hasImplicitSelf,
      TypeSymbol? expectedReturnType,
      MutAccessPlace? mutatingReceiver = null) {
    string capitalizedKind = char.ToUpperInvariant(calleeKind[0]) + calleeKind[1..];
    bool hasExplicitTypeArgs = call.TypeArguments is not null;

    if (hasExplicitTypeArgs) {
      ImmutableArray<TypeSymbol> explicitArgs = ResolveOptionalTypeArguments(
          call.TypeArguments,
          methodTypeParams,
          calleeName,
          call.Span);
      if (explicitArgs.Length != methodTypeParams.Length) {
        SynthesizeCallArguments(call);
        _state.CallResolutions[call.Id] = new CallResolution(callKind, TargetSymbol: callableSymbol, HasImplicitSelf: hasImplicitSelf);
        return _types.Error;
      }

      ImmutableArray<TypeParameterSymbol> combinedParams = [.. ownerTypeParams, .. methodTypeParams];
      ImmutableArray<TypeSymbol> combinedArgs = [.. ownerTypeArgs, .. explicitArgs];

      var expectedParamTypes = ImmutableArray.CreateBuilder<FunctionParameter>(parameters.Length);
      foreach (ParameterSymbol p in parameters) {
        expectedParamTypes.Add(new FunctionParameter(
            TypeSubstitution.Substitute(p.Type, combinedParams, combinedArgs, _types),
            p.IsMutable));
      }

      CheckCallArguments(
          call,
          expectedParamTypes,
          calleeKind,
          calleeName,
          parameterSymbols: parameters,
          mutatingReceiver: mutatingReceiver);
      TypeSymbol rawRet = getRawReturnType();
      TypeSymbol retType = TypeSubstitution.Substitute(rawRet, combinedParams, combinedArgs, _types);
      _state.SetNodeType(calleeNode, _types.GetFunction(expectedParamTypes.ToImmutable(), retType));
      _state.CallResolutions[call.Id] = new CallResolution(callKind, TargetSymbol: callableSymbol, HasImplicitSelf: hasImplicitSelf);

      if (!methodTypeParams.IsEmpty) {
        RecordGenericInstantiation(callableSymbol, combinedArgs);
      }
      RecordInstantiationsInType(retType);
      return retType;
    }

    // Inferred generic arguments via Unifier!
    var unifier = new Unifier(_types);
    var freshVarList = new TypeVariableType[methodTypeParams.Length];
    var freshVarTypes = ImmutableArray.CreateBuilder<TypeSymbol>(methodTypeParams.Length);
    for (int i = 0; i < methodTypeParams.Length; i++) {
      TypeVariableType tv = unifier.Fresh(methodTypeParams[i], call.Span);
      freshVarList[i] = tv;
      freshVarTypes.Add(tv);
    }

    ImmutableArray<TypeParameterSymbol> allParams = [.. ownerTypeParams, .. methodTypeParams];
    ImmutableArray<TypeSymbol> allVarArgs = [.. ownerTypeArgs, .. freshVarTypes.MoveToImmutable()];

    if (!IsMissing(call.CloseParenToken) && call.Arguments.Length != parameters.Length) {
      Report(
          DiagnosticCodes.WrongArgumentCount,
          call.Span,
          $"{capitalizedKind} '{calleeName}' expected {parameters.Length} arguments, but got {call.Arguments.Length}.");
    }

    int argCount = Math.Min(call.Arguments.Length, parameters.Length);
    var paramPatterns = new TypeSymbol[parameters.Length];
    for (int i = 0; i < parameters.Length; i++) {
      paramPatterns[i] = TypeSubstitution.Substitute(parameters[i].Type, allParams, allVarArgs, _types);
    }

    var isDeferredArg = new bool[argCount];
    var argActualTypes = new TypeSymbol?[argCount];
    var argReportedError = new bool[argCount];
    bool hadError = call.Arguments.Length != parameters.Length;

    for (int i = 0; i < argCount; i++) {
      ArgumentSyntax arg = call.Arguments[i];
      if (arg.Expression is MissingExpressionSyntax) {
        hadError = true;
        continue;
      }

      isDeferredArg[i] = !parameters[i].IsMutable && IsDeferredGenericExpression(arg.Expression, paramPatterns[i]);
    }

    // Pass 1: Non-deferred arguments.
    for (int i = 0; i < argCount; i++) {
      if (isDeferredArg[i]) {
        continue;
      }

      ArgumentSyntax arg = call.Arguments[i];
      if (arg.Expression is MissingExpressionSyntax) {
        continue;
      }

      TypeSymbol actualType = Synthesize(arg.Expression);
      argActualTypes[i] = actualType;
      if (actualType.IsError) {
        hadError = true;
        continue;
      }

      if (!unifier.Unify(paramPatterns[i], actualType, arg.Expression.Span, out Unifier.UnifyConflict? conflict, allowIntToFloat: !parameters[i].IsMutable)
          && conflict is { } c) {
        argReportedError[i] = true;
        hadError = true;
        ReportUnifyConflict(
            c,
            arg.Expression.Span,
            $"Argument {i + 1} for {calleeKind} '{calleeName}' expected type '{unifier.ZonkPartial(paramPatterns[i]).DisplayName}', but got '{actualType.DisplayName}'.");
      }
    }

    // Pass 1b: Contextual expected return type fills remaining unbound type variables.
    TypeSymbol rawReturn = getRawReturnType();
    TypeSymbol returnPattern = TypeSubstitution.Substitute(rawReturn, allParams, allVarArgs, _types);
    if (expectedReturnType is not null && !expectedReturnType.IsError) {
      unifier.BindUnboundFromExpected(returnPattern, expectedReturnType, call.Span);
    }

    // Pass 2: Deferred arguments (lambdas, implicit `it`, generic union variants, etc.).
    var deferredZonkNodes = new List<(ExpressionSyntax Expr, TypeSymbol Type)>();
    for (int i = 0; i < argCount; i++) {
      if (!isDeferredArg[i]) {
        continue;
      }

      ArgumentSyntax arg = call.Arguments[i];
      if (arg.Expression is MissingExpressionSyntax) {
        continue;
      }

      TypeSymbol partialExpected = unifier.ZonkPartial(paramPatterns[i]);
      TypeSymbol actualType;
      if (partialExpected is FunctionType expectedFn && ShouldWrapImplicitIt(arg.Expression)) {
        actualType = CheckDeferredImplicitIt(arg.Expression, expectedFn, i, calleeKind, calleeName, unifier, ref hadError);
      } else {
        actualType = CheckDeferredGenericExpression(arg.Expression, partialExpected, unifier, deferredZonkNodes, ref hadError);
      }

      argActualTypes[i] = actualType;
      if (actualType.IsError) {
        hadError = true;
        continue;
      }

      if (!unifier.Unify(paramPatterns[i], actualType, arg.Expression.Span, out Unifier.UnifyConflict? conflict, allowIntToFloat: !parameters[i].IsMutable)
          && conflict is { } c) {
        argReportedError[i] = true;
        hadError = true;
        ReportUnifyConflict(
            c,
            arg.Expression.Span,
            $"Argument {i + 1} for {calleeKind} '{calleeName}' expected type '{unifier.ZonkPartial(paramPatterns[i]).DisplayName}', but got '{actualType.DisplayName}'.");
      }
    }

    // Extra arguments past arity.
    for (int i = argCount; i < call.Arguments.Length; i++) {
      if (call.Arguments[i].Expression is not MissingExpressionSyntax) {
        Synthesize(call.Arguments[i].Expression);
      }
    }

    // Pass 2b: Re-bind from expected return type if still unbound.
    if (expectedReturnType is not null && !expectedReturnType.IsError) {
      unifier.BindUnboundFromExpected(returnPattern, expectedReturnType, call.Span);
    }

    // Finalize inferred type arguments (zonk everything).
    var finalMethodArgsBuilder = ImmutableArray.CreateBuilder<TypeSymbol>(methodTypeParams.Length);
    var argSpansBuilder = ImmutableArray.CreateBuilder<SourceSpan>(methodTypeParams.Length);
    for (int i = 0; i < methodTypeParams.Length; i++) {
      TypeVariableType tv = freshVarList[i];
      if (!unifier.IsBound(tv)) {
        if (!hadError) {
          Report(
              DiagnosticCodes.CannotInferGenericArguments,
              call.Span,
              $"Cannot infer generic type argument for parameter '{methodTypeParams[i].Name}' of {calleeKind} '{calleeName}'.");
        }
        unifier.ForceBind(tv, _types.Error, call.Span);
      }

      finalMethodArgsBuilder.Add(unifier.Zonk(tv));
      argSpansBuilder.Add(call.Span);
    }

    FinalizeDeferredZonkNodes(deferredZonkNodes, unifier);

    ImmutableArray<TypeSymbol> finalMethodArgs = finalMethodArgsBuilder.MoveToImmutable();
    ImmutableArray<TypeSymbol> finalAllArgs = [.. ownerTypeArgs, .. finalMethodArgs];

    // Check generic bounds on inferred type arguments (GEN-8, GEN-11, GEN-E5, GEN-E7).
    CheckGenericBounds(methodTypeParams, finalMethodArgs, argSpansBuilder.MoveToImmutable(), call.Span);

    // Finalize concrete parameter types and verify conversions / remaining mismatches.
    var finalParamTypes = ImmutableArray.CreateBuilder<FunctionParameter>(parameters.Length);
    for (int i = 0; i < parameters.Length; i++) {
      TypeSymbol concreteParam = TypeSubstitution.Substitute(parameters[i].Type, allParams, finalAllArgs, _types);
      finalParamTypes.Add(new FunctionParameter(concreteParam, parameters[i].IsMutable));
      if (i < argCount && !argReportedError[i] && argActualTypes[i] is { } rawActual) {
        TypeSymbol actual = unifier.Zonk(rawActual);
        ExpressionSyntax argExpr = call.Arguments[i].Expression;
        bool typeOk = parameters[i].IsMutable
            ? TryMatchMutArgumentType(actual, concreteParam)
            : TryConvert(argExpr, actual, concreteParam);
        if (!typeOk && parameters[i].IsMutable) {
          ReportMutArgumentNeedsExactType(argExpr, concreteParam, actual);
        } else if (!typeOk) {
          string code = !parameters[i].IsMutable && concreteParam is InterfaceType
              ? DiagnosticCodes.InterfaceNotSatisfied
              : DiagnosticCodes.TypeMismatch;
          string detail = !parameters[i].IsMutable && concreteParam is InterfaceType iface
              ? FormatInterfaceMismatchDetail(actual, iface)
              : string.Empty;
          Report(
              code,
              argExpr.Span,
              $"Argument {i + 1} for {calleeKind} '{calleeName}' expected type '{concreteParam.DisplayName}', but got '{actual.DisplayName}'{detail}.");
        }
      }
    }

    ImmutableArray<FunctionParameter> finalizedParams = finalParamTypes.ToImmutable();
    CheckCallArgumentMutability(
        call,
        argCount,
        finalizedParams,
        parameters,
        calleeKind,
        calleeName,
        mutatingReceiver);

    TypeSymbol finalRetType = TypeSubstitution.Substitute(rawReturn, allParams, finalAllArgs, _types);
    finalRetType = unifier.Zonk(finalRetType);

    _state.SetNodeType(calleeNode, _types.GetFunction(finalizedParams, finalRetType));
    _state.CallResolutions[call.Id] = new CallResolution(callKind, TargetSymbol: callableSymbol, HasImplicitSelf: hasImplicitSelf);

    if (!methodTypeParams.IsEmpty && !finalMethodArgs.Any(static a => a.IsError)) {
      RecordGenericInstantiation(callableSymbol, finalAllArgs);
    }
    RecordInstantiationsInType(finalRetType);
    return finalRetType;
  }

  private TypeSymbol CheckDeferredGenericExpression(
      ExpressionSyntax expr,
      TypeSymbol partialExpected,
      Unifier unifier,
      List<(ExpressionSyntax Expr, TypeSymbol Type)> deferredZonkNodes,
      ref bool hadError) {
    ExpressionSyntax unwrapped = UnwrapParens(expr);

    if (unwrapped is LambdaExpressionSyntax lambda) {
      TypeSymbol lambdaType;
      if (partialExpected is FunctionType expectedFn) {
        // Never leak TypeVariableType into SynthesizeLambda: pass only concrete parameter types
        // and concrete return type (null when return contains an unsolved variable so it synthesizes).
        var concreteParams = ImmutableArray.CreateBuilder<FunctionParameter>(expectedFn.Parameters.Length);
        bool allParamsConcrete = true;
        foreach (FunctionParameter fp in expectedFn.Parameters) {
          if (Unifier.ContainsTypeVariable(fp.Type)) {
            allParamsConcrete = false;
            concreteParams.Add(new FunctionParameter(_types.Error, fp.IsMutable));
          } else {
            concreteParams.Add(fp);
          }
        }

        TypeSymbol? concreteRet = Unifier.ContainsTypeVariable(expectedFn.ReturnType)
            ? null
            : expectedFn.ReturnType;

        lambdaType = SynthesizeLambda(
            lambda,
            expectedFunction: null,
            expectedParamsOverride: allParamsConcrete ? concreteParams.MoveToImmutable() : null,
            expectedReturnOverride: concreteRet);
      } else {
        lambdaType = SynthesizeLambda(lambda, expectedFunction: null);
      }

      SetWrappedExpressionType(expr, lambdaType);
      return lambdaType;
    }

    if (!Unifier.ContainsTypeVariable(partialExpected)) {
      return CheckWithContextualExpectation(expr, partialExpected);
    }

    // Partial expected still has type variables (e.g. Result[int, ?E] for Result::Failure("x")).
    if (unwrapped is CallExpressionSyntax innerCall
        && innerCall.Callee is StaticAccessExpressionSyntax { TargetTypeArguments: null } innerStatic
        && partialExpected is UnionType partialUnion) {
      TypeSymbol resolvedInner = SynthesizeCallWithPartialUnionExpected(innerCall, innerStatic, partialUnion, unifier);
      if (Unifier.ContainsTypeVariable(resolvedInner)) {
        deferredZonkNodes.Add((expr, resolvedInner));
        SetWrappedExpressionType(expr, _types.Error);
      } else {
        SetWrappedExpressionType(expr, resolvedInner);
      }
      if (resolvedInner.IsError) {
        hadError = true;
      }
      return resolvedInner;
    }

    if (unwrapped is StaticAccessExpressionSyntax { TargetTypeArguments: null } staticAccess
        && partialExpected is UnionType partialStaticUnion) {
      TypeSymbol resolvedStatic = SynthesizeStaticAccess(staticAccess, partialStaticUnion);
      if (Unifier.ContainsTypeVariable(resolvedStatic)) {
        deferredZonkNodes.Add((expr, resolvedStatic));
        SetWrappedExpressionType(expr, _types.Error);
      } else {
        SetWrappedExpressionType(expr, resolvedStatic);
      }
      return resolvedStatic;
    }

    return Synthesize(expr);
  }

  private void FinalizeDeferredZonkNodes(
      List<(ExpressionSyntax Expr, TypeSymbol Type)> deferredZonkNodes,
      Unifier unifier) {
    foreach ((ExpressionSyntax expr, TypeSymbol rawType) in deferredZonkNodes) {
      TypeSymbol zonked = unifier.Zonk(rawType);
      SetWrappedExpressionType(expr, zonked);
      if (UnwrapParens(expr) is CallExpressionSyntax call
          && zonked is UnionType zonkedUnion
          && _state.CallResolutions[call.Id] is { Kind: CallKind.UnionVariantConstructor } existing) {
        _state.CallResolutions[call.Id] = existing with { ConstructedUnionType = zonkedUnion };
      }
      RecordInstantiationsInType(zonked);
    }
  }

  private TypeSymbol CheckDeferredImplicitIt(
      ExpressionSyntax argExpression,
      FunctionType expectedFn,
      int argIndex,
      string calleeKind,
      string calleeName,
      Unifier unifier,
      ref bool hadError) {
    if (expectedFn.Parameters.Length != 1) {
      Report(
          DiagnosticCodes.InvalidImplicitIt,
          argExpression.Span,
          $"Implicit 'it' requires a 1-parameter function type, but {calleeKind} '{calleeName}' argument {argIndex + 1} expects '{expectedFn.DisplayName}'.");
      hadError = true;
      return _types.Error;
    }

    TypeSymbol itParamType = expectedFn.Parameters[0].Type;
    if (Unifier.ContainsTypeVariable(itParamType)) {
      hadError = true;
      return _types.Error;
    }

    TypeSymbol? concreteRet = Unifier.ContainsTypeVariable(expectedFn.ReturnType)
        ? null
        : expectedFn.ReturnType;

    return CheckImplicitItCore(
        argExpression,
        itParamType,
        concreteRet,
        expectedFunctionForMismatch: concreteRet is not null
            ? _types.GetFunction([new FunctionParameter(itParamType, IsMutable: false)], concreteRet)
            : null,
        argIndex,
        calleeKind,
        calleeName);
  }

  private TypeSymbol SynthesizeCallWithPartialUnionExpected(
      CallExpressionSyntax call,
      StaticAccessExpressionSyntax staticAccess,
      UnionType partialExpected,
      Unifier outerUnifier) {
    Symbol? targetSym = ResolveStaticTargetSymbol(staticAccess.Target, out _);
    if (IsMissing(staticAccess.MemberToken) || targetSym is not UnionSymbol unionSym) {
      return SynthesizeCall(call, expectedType: null);
    }

    // Zonking partialExpected converts unbound outer vars into ErrorType only if we don't keep them;
    // here we pass partialExpected to CheckUnionConstructorCall, which fills only concrete slots.
    string memberName = NameOf(staticAccess.MemberToken);
    TypeSymbol result = CheckUnionConstructorCall(call, staticAccess, unionSym, memberName, partialExpected, allowPartialInference: false);
    if (result is UnionType ut && ReferenceEquals(ut.Definition, partialExpected.Definition)) {
      outerUnifier.Unify(partialExpected, ut, call.Span, out _);
      TypeSymbol zonked = outerUnifier.Zonk(ut);
      _state.SetNodeType(call, zonked);
      if (zonked is UnionType zonkedUnion) {
        _state.CallResolutions[call.Id] = new CallResolution(
            CallKind.UnionVariantConstructor,
            TargetSymbol: _state.ReferencedSymbols[staticAccess.Id],
            ConstructedUnionType: zonkedUnion);
        RecordGenericInstantiation(unionSym, zonkedUnion.TypeArguments);
      }
      return zonked;
    }

    return result;
  }

  // ---------------------------------------------------------------------------------------------
  // Generic Union Constructor Calls (UNION-6..11, UNION-E1, UNION-E3, UNION-E4, ERR-E8)
  // ---------------------------------------------------------------------------------------------

  private TypeSymbol InferGenericUnionConstructorCall(
      CallExpressionSyntax call,
      StaticAccessExpressionSyntax staticAccess,
      UnionSymbol unionSym,
      UnionVariantSymbol variant,
      TypeSymbol? expectedType,
      bool allowPartialInference,
      out UnionType? partialUnresolvedResult) {
    partialUnresolvedResult = null;
    ImmutableArray<TypeParameterSymbol> unionParams = unionSym.TypeParameters;

    var unifier = new Unifier(_types);
    var freshVarList = new TypeVariableType[unionParams.Length];
    var freshVarTypes = ImmutableArray.CreateBuilder<TypeSymbol>(unionParams.Length);
    for (int i = 0; i < unionParams.Length; i++) {
      TypeVariableType tv = unifier.Fresh(unionParams[i], call.Span);
      freshVarList[i] = tv;
      freshVarTypes.Add(tv);
    }

    ImmutableArray<TypeSymbol> freshArgs = freshVarTypes.MoveToImmutable();

    if (!IsMissing(call.CloseParenToken) && call.Arguments.Length != variant.Fields.Length) {
      Report(
          DiagnosticCodes.VariantArgumentMismatch,
          call.Span,
          $"Variant constructor '{unionSym.Name}::{variant.Name}' expected {variant.Fields.Length} arguments, but got {call.Arguments.Length}.");
    }

    int count = Math.Min(call.Arguments.Length, variant.Fields.Length);
    var fieldPatterns = new TypeSymbol[variant.Fields.Length];
    for (int i = 0; i < variant.Fields.Length; i++) {
      fieldPatterns[i] = TypeSubstitution.Substitute(variant.Fields[i].Type, unionParams, freshArgs, _types);
    }

    var isDeferred = new bool[count];
    var argActuals = new TypeSymbol?[count];
    var argReported = new bool[count];
    bool hadError = call.Arguments.Length != variant.Fields.Length;

    for (int i = 0; i < count; i++) {
      ArgumentSyntax arg = call.Arguments[i];
      if (arg.Expression is MissingExpressionSyntax) {
        hadError = true;
        continue;
      }

      isDeferred[i] = IsDeferredGenericExpression(arg.Expression, fieldPatterns[i]);
    }

    // Pass 1: Non-deferred arguments.
    // Note: Pass 1 runs BEFORE expectedType so `use_container(Container::Value("not an int"))`
    // infers `Container[string]` and lets `use_container` report UNION-E4 (`expected type 'Container[int]'`).
    for (int i = 0; i < count; i++) {
      if (isDeferred[i]) {
        continue;
      }

      ArgumentSyntax arg = call.Arguments[i];
      if (arg.Expression is MissingExpressionSyntax) {
        continue;
      }

      TypeSymbol actualType = Synthesize(arg.Expression);
      argActuals[i] = actualType;
      if (actualType.IsError) {
        hadError = true;
        continue;
      }

      if (!unifier.Unify(fieldPatterns[i], actualType, arg.Expression.Span, out Unifier.UnifyConflict? conflict, allowIntToFloat: true)
          && conflict is { } c) {
        argReported[i] = true;
        hadError = true;
        ReportUnifyConflict(
            c,
            arg.Expression.Span,
            $"Argument {i + 1} for variant constructor '{variant.Name}' expected type '{unifier.ZonkPartial(fieldPatterns[i]).DisplayName}', but got '{actualType.DisplayName}'.");
      }
    }

    // Pass 1b: Contextual expected union fills ONLY still-unbound type parameters (UNION-9).
    if (expectedType is UnionType expectedUnion && ReferenceEquals(expectedUnion.Definition, unionSym)) {
      for (int i = 0; i < Math.Min(unionParams.Length, expectedUnion.TypeArguments.Length); i++) {
        TypeSymbol expArg = expectedUnion.TypeArguments[i];
        if (!unifier.IsBound(freshVarList[i]) && !expArg.IsError && !Unifier.ContainsTypeVariable(expArg)) {
          unifier.ForceBind(freshVarList[i], expArg, call.Span);
        }
      }
    }

    // Pass 2: Deferred arguments (e.g. List::Cons(42, List::Nil), lambdas, nested generic unions).
    var deferredZonkNodes = new List<(ExpressionSyntax Expr, TypeSymbol Type)>();
    for (int i = 0; i < count; i++) {
      if (!isDeferred[i]) {
        continue;
      }

      ArgumentSyntax arg = call.Arguments[i];
      if (arg.Expression is MissingExpressionSyntax) {
        continue;
      }

      TypeSymbol partialExpected = unifier.ZonkPartial(fieldPatterns[i]);
      TypeSymbol actualType = CheckDeferredGenericExpression(arg.Expression, partialExpected, unifier, deferredZonkNodes, ref hadError);
      argActuals[i] = actualType;
      if (actualType.IsError) {
        hadError = true;
        continue;
      }

      if (!unifier.Unify(fieldPatterns[i], actualType, arg.Expression.Span, out Unifier.UnifyConflict? conflict, allowIntToFloat: true)
          && conflict is { } c) {
        argReported[i] = true;
        hadError = true;
        ReportUnifyConflict(
            c,
            arg.Expression.Span,
            $"Argument {i + 1} for variant constructor '{variant.Name}' expected type '{unifier.ZonkPartial(fieldPatterns[i]).DisplayName}', but got '{actualType.DisplayName}'.");
      }
    }

    for (int i = count; i < call.Arguments.Length; i++) {
      if (call.Arguments[i].Expression is not MissingExpressionSyntax) {
        Synthesize(call.Arguments[i].Expression);
      }
    }

    CheckNoUnexpectedMutArguments(call, "variant constructor", variant.Name);

    // When `allowPartialInference` is true (inside `?` or `or { ... }`), return any partially solved
    // Result[T, E] so the enclosing `?` / `or` can resolve the missing slot (or fill `never`) without
    // emitting a spurious PN3110. Caller MUST finalize to concrete types (no TypeVariableType leaks).
    if (allowPartialInference) {
      var partialArgs = ImmutableArray.CreateBuilder<TypeSymbol>(unionParams.Length);
      bool anyUnboundSlot = false;
      for (int i = 0; i < unionParams.Length; i++) {
        if (unifier.IsBound(freshVarList[i])) {
          partialArgs.Add(unifier.Zonk(freshVarList[i]));
        } else {
          anyUnboundSlot = true;
          partialArgs.Add(_types.Never);
        }
      }

      if (anyUnboundSlot) {
        FinalizeDeferredZonkNodes(deferredZonkNodes, unifier);
        UnionType provisional = _types.GetUnion(unionSym, partialArgs.MoveToImmutable());
        partialUnresolvedResult = provisional;
        _state.CallResolutions[call.Id] = new CallResolution(
            CallKind.UnionVariantConstructor,
            TargetSymbol: variant,
            ConstructedUnionType: provisional);
        return provisional;
      }
    }

    // Finalize type arguments (zonk everything).
    var finalArgsBuilder = ImmutableArray.CreateBuilder<TypeSymbol>(unionParams.Length);
    var argSpans = ImmutableArray.CreateBuilder<SourceSpan>(unionParams.Length);
    for (int i = 0; i < unionParams.Length; i++) {
      TypeVariableType tv = freshVarList[i];
      if (!unifier.IsBound(tv)) {
        if (!hadError) {
          Report(
              DiagnosticCodes.CannotInferGenericArguments,
              call.Span,
              $"Cannot infer generic type argument for parameter '{unionParams[i].Name}' of union '{unionSym.Name}::{variant.Name}'.");
        }
        unifier.ForceBind(tv, _types.Error, call.Span);
      }

      finalArgsBuilder.Add(unifier.Zonk(tv));
      argSpans.Add(call.Span);
    }

    FinalizeDeferredZonkNodes(deferredZonkNodes, unifier);

    ImmutableArray<TypeSymbol> finalArgs = finalArgsBuilder.MoveToImmutable();
    CheckGenericBounds(unionParams, finalArgs, argSpans.MoveToImmutable(), call.Span);

    // Check conversions / mismatches against finalized concrete field types.
    for (int i = 0; i < count; i++) {
      if (argReported[i] || argActuals[i] is not { } rawActual) {
        continue;
      }

      TypeSymbol concreteFieldType = TypeSubstitution.Substitute(variant.Fields[i].Type, unionParams, finalArgs, _types);
      TypeSymbol actual = unifier.Zonk(rawActual);
      ExpressionSyntax argExpr = call.Arguments[i].Expression;
      if (!TryConvert(argExpr, actual, concreteFieldType)) {
        string code = concreteFieldType is InterfaceType
            ? DiagnosticCodes.InterfaceNotSatisfied
            : DiagnosticCodes.VariantArgumentMismatch;
        string detail = concreteFieldType is InterfaceType iface
            ? FormatInterfaceMismatchDetail(actual, iface)
            : string.Empty;
        Report(
            code,
            argExpr.Span,
            $"Argument {i + 1} for variant constructor '{variant.Name}' expected type '{concreteFieldType.DisplayName}', but got '{actual.DisplayName}'{detail}.");
      }
    }

    if (finalArgs.Any(static a => a.IsError)) {
      _state.CallResolutions[call.Id] = new CallResolution(
          CallKind.UnionVariantConstructor,
          TargetSymbol: variant);
      return _types.Error;
    }

    UnionType unionType = _types.GetUnion(unionSym, finalArgs);
    _state.CallResolutions[call.Id] = new CallResolution(
        CallKind.UnionVariantConstructor,
        TargetSymbol: variant,
        ConstructedUnionType: unionType);
    RecordGenericInstantiation(unionSym, finalArgs);
    return unionType;
  }
}
