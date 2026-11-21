using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  private readonly record struct MutablePlaceResult(
      bool IsValid,
      Symbol? RootSymbol,
      MutationRootKind RootKind,
      bool IsSelfRoot,
      bool WasSilent = false);

  /// <summary>
  /// Verifies that <paramref name="place"/> denotes a mutable storage place under AD-003,
  /// records AD-002/AD-003 mutation tracking (<see cref="ModuleCheckState.AssignedSymbols"/> and
  /// closure interior-mutation captures), and returns the resolved root symbol and
  /// <see cref="MutationRootKind"/>.
  /// Amendment A4: silently returns <c>IsValid = true, WasSilent = true</c> with zero diagnostics
  /// when any node along the path is missing, an error/never type, or unresolved.
  /// </summary>
  private MutablePlaceResult CheckMutablePlace(
      ExpressionSyntax place,
      MutationKind kind,
      SourceSpan reportSpan) =>
      CheckMutablePlaceCore(place, kind, reportSpan);

  private MutablePlaceResult CheckMutablePlaceAndRecord(
      SyntaxNode reportNode,
      ExpressionSyntax place,
      MutationKind kind,
      SourceSpan reportSpan) {
    MutablePlaceResult result = CheckMutablePlace(place, kind, reportSpan);
    if (result.IsValid && !result.WasSilent && result.RootSymbol is { } rootSym) {
      RecordMutationPlaceInfo(reportNode, place, kind, rootSym, result.RootKind);
    }

    return result;
  }

  private void RecordMutationPlaceInfo(
      SyntaxNode reportNode,
      ExpressionSyntax place,
      MutationKind kind,
      Symbol rootSym,
      MutationRootKind rootKind) {
    ExpressionSyntax unwrapped = UnwrapParens(place);
    TypeSymbol? placeType = (uint) unwrapped.Id < (uint) _state.NodeTypes.Length
        ? _state.NodeTypes[unwrapped.Id]
        : null;
    bool isInteriorPath = unwrapped is MemberAccessExpressionSyntax or IndexExpressionSyntax;
    bool requiresCoWCheck = kind switch {
      MutationKind.FieldWrite or MutationKind.IndexWrite or MutationKind.MutatingCall => true,
      MutationKind.MutArgument => isInteriorPath || IsCoWType(placeType),
      _ => false,
    };
    bool passByInOut = kind is MutationKind.MutArgument or MutationKind.MutatingCall;
    _state.SetMutationPlace(
        reportNode,
        new MutationPlaceInfo(kind, rootSym, rootKind, requiresCoWCheck, passByInOut));
  }

  private MutablePlaceResult CheckMutablePlaceCore(
      ExpressionSyntax place,
      MutationKind kind,
      SourceSpan reportSpan) {
    place = UnwrapParens(place);

    if (place is MissingExpressionSyntax or ErrorExpressionSyntax) {
      return SilentMutablePlace();
    }

    TypeSymbol? placeType = (uint) place.Id < (uint) _state.NodeTypes.Length
        ? _state.NodeTypes[place.Id]
        : null;
    if (placeType is not null && (IsSilent(placeType) || placeType is PendingReturnType)) {
      return SilentMutablePlace();
    }

    switch (place) {
      case MemberAccessExpressionSyntax member: {
          if (IsMissing(member.MemberToken)) {
            return SilentMutablePlace();
          }

          TypeSymbol? targetType = (uint) member.Target.Id < (uint) _state.NodeTypes.Length
              ? _state.NodeTypes[member.Target.Id]
              : null;
          if (targetType is null || IsSilent(targetType) || targetType is PendingReturnType) {
            return SilentMutablePlace();
          }

          string memberName = NameOf(member.MemberToken);
          switch (targetType) {
            case StructType structType: {
                Symbol? memberSym = (uint) member.Id < (uint) _state.ReferencedSymbols.Length
                    ? _state.ReferencedSymbols[member.Id]
                    : null;
                if (memberSym is not FieldSymbol field) {
                  return SilentMutablePlace();
                }

                if (!field.IsMutable) {
                  ReportReadOnlyField(reportSpan, structType.Definition.Name, field);
                  return InvalidMutablePlace();
                }

                return CheckMutablePlaceCore(member.Target, kind, reportSpan);
              }

            case InterfaceType ifaceType: {
                if (FindInterfaceProperty(ifaceType.Definition, memberName) is not { } prop) {
                  return SilentMutablePlace();
                }

                if (!prop.IsMutable) {
                  ReportReadOnlyInterfaceProperty(reportSpan, ifaceType.Definition, prop);
                  return InvalidMutablePlace();
                }

                return CheckMutablePlaceCore(member.Target, kind, reportSpan);
              }

            case TypeParameterType { Symbol.Constraint: { } constraint }: {
                if (FindInterfaceProperty(constraint.Definition, memberName) is not { } prop) {
                  return SilentMutablePlace();
                }

                if (!prop.IsMutable) {
                  ReportReadOnlyInterfaceProperty(reportSpan, constraint.Definition, prop);
                  return InvalidMutablePlace();
                }

                return CheckMutablePlaceCore(member.Target, kind, reportSpan);
              }

            default:
              // Read-only stdlib property (e.g., `v:len`) or other non-place member access.
              ReportInvalidMutExpression(reportSpan, kind);
              return InvalidMutablePlace();
          }
        }

      case IndexExpressionSyntax index: {
          TypeSymbol? containerType = (uint) index.Target.Id < (uint) _state.NodeTypes.Length
              ? _state.NodeTypes[index.Target.Id]
              : null;
          if (containerType is null || IsSilent(containerType) || containerType is PendingReturnType) {
            return SilentMutablePlace();
          }

          if (containerType is PrimitiveType { Kind: PrimitiveKind.String }) {
            if (kind == MutationKind.IndexWrite) {
              Report(
                  DiagnosticCodes.InvalidAssignmentTarget,
                  index.Span,
                  "Cannot assign to index of a 'string'; strings are immutable.");
            } else {
              ReportInvalidMutExpression(reportSpan, kind);
            }

            return InvalidMutablePlace();
          }

          if (containerType is not (VectorType or MapType)) {
            return SilentMutablePlace();
          }

          return CheckMutablePlaceCore(index.Target, kind, reportSpan);
        }

      case IdentifierExpressionSyntax identifier: {
          if (IsMissing(identifier.IdentifierToken)) {
            return SilentMutablePlace();
          }

          Symbol? symbol = (uint) identifier.Id < (uint) _state.ReferencedSymbols.Length
              ? _state.ReferencedSymbols[identifier.Id]
              : null;
          if (symbol is null or ErrorSymbol) {
            return SilentMutablePlace();
          }

          switch (symbol) {
            case LocalSymbol local:
              if (IsSelfLocal(local)) {
                MethodSymbol? enclosingMethod = FindEnclosingMethod(out Scope? methodScope);
                if (enclosingMethod is null || !enclosingMethod.IsMutating) {
                  ReportCannotMutateSelf(reportSpan, enclosingMethod);
                  return InvalidMutablePlace();
                }

                _state.AssignedSymbols.Add(local);
                if (methodScope is not null) {
                  RecordCaptureIfOuter(local, methodScope, isMutation: true, reportSpan);
                }

                return new MutablePlaceResult(
                    IsValid: true,
                    RootSymbol: local,
                    RootKind: MutationRootKind.MutSelf,
                    IsSelfRoot: true);
              }

              if (!local.IsMutable) {
                ReportCannotMutateImmutableLocal(reportSpan, local);
                return InvalidMutablePlace();
              }

              _state.AssignedSymbols.Add(local);
              Scope? localDeclScope = FindDeclaringScope(local);
              if (localDeclScope is not null) {
                RecordCaptureIfOuter(local, localDeclScope, isMutation: true, reportSpan);
              }

              return new MutablePlaceResult(
                  IsValid: true,
                  RootSymbol: local,
                  RootKind: ComputeLocalRootKind(localDeclScope),
                  IsSelfRoot: false);

            case ParameterSymbol param:
              if (!param.IsMutable) {
                ReportCannotMutateImmutableParameter(reportSpan, param);
                return InvalidMutablePlace();
              }

              _state.AssignedSymbols.Add(param);
              Scope? paramDeclScope = FindDeclaringScope(param);
              if (paramDeclScope is not null) {
                RecordCaptureIfOuter(param, paramDeclScope, isMutation: true, reportSpan);
              }

              return new MutablePlaceResult(
                  IsValid: true,
                  RootSymbol: param,
                  RootKind: MutationRootKind.MutParameter,
                  IsSelfRoot: false);

            case FieldSymbol field: {
                MethodSymbol? enclosingMethod = FindEnclosingMethod(out Scope? methodScope);
                if (enclosingMethod is null || !enclosingMethod.IsMutating) {
                  ReportCannotMutateSelf(reportSpan, enclosingMethod);
                  return InvalidMutablePlace();
                }

                if (!field.IsMutable) {
                  string structName = _currentStruct?.Name ?? field.ContainingStruct.Name;
                  ReportReadOnlyField(reportSpan, structName, field);
                  return InvalidMutablePlace();
                }

                Symbol? selfSym = null;
                if (methodScope is not null) {
                  methodScope.TryLookupLocal("self", out selfSym);
                }

                Symbol rootSym = selfSym ?? field;
                if (selfSym is LocalSymbol selfLocal) {
                  _state.AssignedSymbols.Add(selfLocal);
                }

                if (methodScope is not null && selfSym is not null) {
                  RecordCaptureIfOuter(selfSym, methodScope, isMutation: true, reportSpan);
                }

                return new MutablePlaceResult(
                    IsValid: true,
                    RootSymbol: rootSym,
                    RootKind: MutationRootKind.MutSelf,
                    IsSelfRoot: true);
              }

            default:
              ReportInvalidMutExpression(reportSpan, kind);
              return InvalidMutablePlace();
          }
        }

      case StaticAccessExpressionSyntax staticAccess: {
          Symbol? staticSym = (uint) staticAccess.Id < (uint) _state.ReferencedSymbols.Length
              ? _state.ReferencedSymbols[staticAccess.Id]
              : null;
          if (staticSym is LocalSymbol { IsMutable: false } pubVal) {
            ReportCannotMutateImmutableLocal(reportSpan, pubVal);
            return InvalidMutablePlace();
          }

          ReportInvalidMutExpression(reportSpan, kind);
          return InvalidMutablePlace();
        }

      default:
        ReportInvalidMutExpression(reportSpan, kind);
        return InvalidMutablePlace();
    }
  }

  /// <summary>
  /// Checks a bare sibling <c>mut fn</c> call inside an instance method (implicit <c>self</c> receiver),
  /// records <see cref="MutationPlaceInfo"/> with <see cref="MutationRootKind.MutSelf"/>, and returns
  /// the receiver root for call exclusivity checking.
  /// </summary>
  private (Symbol Root, SourceSpan Span)? CheckImplicitSelfMutation(
      CallExpressionSyntax call,
      SourceSpan calleeSpan) {
    MethodSymbol? enclosingMethod = FindEnclosingMethod(out Scope? methodScope);
    if (enclosingMethod is null || !enclosingMethod.IsMutating) {
      ReportCannotMutateSelf(call.Span, enclosingMethod);
      return null;
    }

    Symbol? selfSym = null;
    if (methodScope is not null) {
      methodScope.TryLookupLocal("self", out selfSym);
    }

    if (selfSym is LocalSymbol selfLocal) {
      _state.AssignedSymbols.Add(selfLocal);
      if (methodScope is not null) {
        RecordCaptureIfOuter(selfLocal, methodScope, isMutation: true, call.Span);
      }

      _state.SetMutationPlace(
          call,
          new MutationPlaceInfo(
              MutationKind.MutatingCall,
              selfLocal,
              MutationRootKind.MutSelf,
              RequiresCoWCheck: true,
              PassByInOut: true));
      return (selfLocal, calleeSpan);
    }

    return null;
  }

  /// <summary>
  /// Enforces call-site <c>mut</c> matching (<c>PN3223</c>/<c>PN3224</c>), mutable-place validation
  /// (<c>PN3220</c>/<c>PN3221</c>/<c>PN3222</c>/<c>PN3225</c>), static same-root <c>mut</c>+<c>mut</c>
  /// exclusivity (<c>PN3226</c>, counting a mutating receiver as one <c>mut</c> access, while allowing
  /// <c>mut</c>+read per <c>FN-31</c>), and populates <see cref="ModuleCheckState.MutationPlaces"/> for
  /// every valid <c>mut</c> argument.
  /// </summary>
  private void CheckCallArgumentMutability(
      CallExpressionSyntax call,
      int argCount,
      IReadOnlyList<FunctionParameter> expectedParameters,
      IReadOnlyList<ParameterSymbol>? parameterSymbols,
      string calleeKind,
      string calleeName,
      (Symbol Root, SourceSpan Span)? mutatingReceiverRoot) {
    Dictionary<Symbol, SourceSpan>? seenMutRoots = null;
    if (mutatingReceiverRoot is { } recv) {
      seenMutRoots = new Dictionary<Symbol, SourceSpan>(ReferenceEqualityComparer.Instance) {
        [recv.Root] = recv.Span,
      };
    }

    for (int i = 0; i < argCount; i++) {
      ArgumentSyntax arg = call.Arguments[i];
      if (arg.Expression is MissingExpressionSyntax) {
        continue;
      }

      bool paramIsMut = expectedParameters[i].IsMutable;
      bool hasMutAtCallSite = arg.MutKeyword is not null;
      ParameterSymbol? paramSym = parameterSymbols is not null && i < parameterSymbols.Count
          ? parameterSymbols[i]
          : null;

      if (paramIsMut && !hasMutAtCallSite) {
        ReportMissingMutAtCallSite(arg, i, calleeKind, calleeName, paramSym);
        continue;
      }

      if (!paramIsMut && hasMutAtCallSite) {
        ReportUnexpectedMutAtCallSite(arg, i, calleeKind, calleeName, paramSym);
        continue;
      }

      if (!paramIsMut) {
        continue;
      }

      SourceSpan argSpan = ArgumentSpanWithoutComma(arg);
      MutablePlaceResult place = CheckMutablePlace(arg.Expression, MutationKind.MutArgument, argSpan);
      if (!place.IsValid || place.WasSilent || place.RootSymbol is not { } rootSym) {
        continue;
      }

      seenMutRoots ??= new Dictionary<Symbol, SourceSpan>(ReferenceEqualityComparer.Instance);
      if (seenMutRoots.TryGetValue(rootSym, out SourceSpan earlierSpan)) {
        ReportOverlappingMutArguments(argSpan, rootSym.Name, earlierSpan);
        continue;
      }

      seenMutRoots.Add(rootSym, argSpan);
      RecordMutationPlaceInfo(arg, arg.Expression, MutationKind.MutArgument, rootSym, place.RootKind);
    }
  }

  /// <summary>
  /// Rejects call-site <c>mut</c> arguments on callables that have no <c>mut</c> parameters
  /// (stdlib intrinsics and union variant constructors).
  /// </summary>
  private void CheckNoUnexpectedMutArguments(
      CallExpressionSyntax call,
      string calleeKind,
      string calleeName) {
    for (int i = 0; i < call.Arguments.Length; i++) {
      ArgumentSyntax arg = call.Arguments[i];
      if (arg.MutKeyword is not null && arg.Expression is not MissingExpressionSyntax) {
        ReportUnexpectedMutAtCallSite(arg, i, calleeKind, calleeName, paramSym: null);
      }
    }
  }

  private static MutablePlaceResult SilentMutablePlace() =>
      new(IsValid: true, RootSymbol: null, MutationRootKind.LocalVar, IsSelfRoot: false, WasSilent: true);

  private static MutablePlaceResult InvalidMutablePlace() =>
      new(IsValid: false, RootSymbol: null, MutationRootKind.LocalVar, IsSelfRoot: false, WasSilent: false);

  private static bool IsSelfLocal(LocalSymbol local) =>
      string.Equals(local.Name, "self", StringComparison.Ordinal)
      && ((local.ContainingSymbol is MethodSymbol m && local.DeclaringNodeId == m.DeclaringNodeId)
          || (local.ContainingSymbol is StructSymbol s && local.DeclaringNodeId == s.DeclaringNodeId));

  private MethodSymbol? FindEnclosingMethod(out Scope? methodScope) {
    for (Scope? current = _scope; current is not null; current = current.Parent) {
      if (current.Kind == ScopeKind.Method && current.Owner is MethodSymbol method) {
        methodScope = current;
        return method;
      }
    }

    methodScope = null;
    return _currentCallable as MethodSymbol;
  }

  private Scope? FindDeclaringScope(Symbol target) {
    for (Scope? current = _scope; current is not null; current = current.Parent) {
      if (current.TryLookupLocal(target.Name, out Symbol? found) && ReferenceEquals(found, target)) {
        return current;
      }
    }

    return null;
  }

  private MutationRootKind ComputeLocalRootKind(Scope? declScope) {
    if (declScope is null) {
      return MutationRootKind.LocalVar;
    }

    if (declScope.Kind == ScopeKind.Module) {
      return MutationRootKind.TopLevelVar;
    }

    if (IsCapturedAcrossClosure(declScope)) {
      return MutationRootKind.CapturedVar;
    }

    return MutationRootKind.LocalVar;
  }

  private bool IsCapturedAcrossClosure(Scope declScope) {
    if (_captureStack.Count == 0) {
      return false;
    }

    for (Scope? current = _scope; current is not null && !ReferenceEquals(current, declScope); current = current.Parent) {
      for (int i = _captureStack.Count - 1; i >= 0; i--) {
        if (ReferenceEquals(_captureStack[i].BoundaryScope, current)) {
          return true;
        }
      }
    }

    return false;
  }

  private static bool IsCoWType(TypeSymbol? type) =>
      type is StructType or VectorType or MapType or SetType or InterfaceType or TypeParameterType;

  /// <summary>
  /// Returns <see langword="true"/> when <paramref name="type"/> has mutable interior contents
  /// (or is unknown / error-recovered) so <c>val mut</c> is permitted (D-183 / VAR-E13).
  /// </summary>
  private static bool HasMutableInterior(TypeSymbol type) {
    if (type.IsError || type.IsNever || type.IsVoid || type is PendingReturnType or TypeVariableType) {
      return true;
    }

    switch (type) {
      case VectorType:
      case MapType:
      case SetType:
      case TypeParameterType:
        return true;

      case StructType structType: {
          ImmutableArray<FieldSymbol> fields = structType.Definition.AllFields.IsEmpty
              ? structType.Definition.Fields
              : structType.Definition.AllFields;
          foreach (FieldSymbol field in fields) {
            if (field.IsMutable) {
              return true;
            }
          }
          return false;
        }

      case InterfaceType ifaceType: {
          foreach (InterfaceProperty prop in ifaceType.Definition.Properties) {
            if (prop.IsMutable) {
              return true;
            }
          }
          foreach (MethodSymbol method in ifaceType.Definition.Methods) {
            if (method.IsMutating) {
              return true;
            }
          }
          return false;
        }

      default:
        // Primitives (`int`, `float`, `bool`, `rune`, `string`), enums, unions (`Option`, `Result`),
        // tuples, and function types have no mutable interior.
        return false;
    }
  }

  private bool TryMatchMutArgumentType(TypeSymbol actual, TypeSymbol expected) {
    if (actual.IsError || expected.IsError || actual is PendingReturnType || expected is PendingReturnType || actual.IsNever) {
      return true;
    }

    // `mut` (`inout`) parameters are invariant: widening `int -> float` or `Struct -> Interface`
    // would be unsound because writes inside the callee flow back to the caller's place.
    return ReferenceEquals(actual, expected);
  }

  private string FormatShortExpressionSnippet(SourceSpan span) {
    if (span.Length <= 0 || span.End > _state.Tree.Source.Length) {
      return "arg";
    }

    ReadOnlySpan<char> text = _state.Tree.Source.AsSpan(span);
    if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0 || text.Length > 60) {
      return "arg";
    }

    return text.ToString();
  }

  // ---------------------------------------------------------------------------------------------
  // Diagnostics (PN3201, PN3220–PN3228)
  // ---------------------------------------------------------------------------------------------

  private void ReportCannotReassignSelf(SourceSpan span) {
    MethodSymbol? enclosing = FindEnclosingMethod(out _);
    ImmutableArray<RelatedLocation> related = enclosing is { NameSpan.Length: > 0 }
        ? [new RelatedLocation(enclosing.NameSpan, $"method '{enclosing.Name}' is declared here", enclosing.FilePath)]
        : [];
    Report(
        DiagnosticCodes.CannotAssignToImmutable,
        span,
        "Cannot reassign 'self'.",
        related,
        help: "mutate 'mut' fields of 'self' instead of reassigning 'self'");
  }

  private void ReportImmutableParameterAssignment(SourceSpan span, ParameterSymbol parameter) {
    ImmutableArray<RelatedLocation> related = parameter.NameSpan.Length > 0
        ? [new RelatedLocation(parameter.NameSpan, $"parameter '{parameter.Name}' is declared immutable here", parameter.FilePath)]
        : [];
    Report(
        DiagnosticCodes.CannotAssignToImmutable,
        span,
        $"Cannot reassign immutable parameter '{parameter.Name}'.",
        related,
        help: $"mark parameter '{parameter.Name}' as 'mut'");
  }

  private void ReportValMutReassignment(IdentifierExpressionSyntax identifier, LocalSymbol local) {
    ImmutableArray<RelatedLocation> related = local.NameSpan.Length > 0
        ? [new RelatedLocation(local.NameSpan, $"'{local.Name}' is declared with 'val mut' here", local.FilePath)]
        : [];
    Report(
        DiagnosticCodes.CannotReassignValMut,
        identifier.Span,
        $"Cannot reassign 'val mut' variable '{local.Name}'.",
        related,
        help: "declare it with 'var' to allow reassignment");
  }

  private void ReportCannotMutateImmutableLocal(SourceSpan span, LocalSymbol local) {
    string kindLabel = local.Kind switch {
      LocalKind.Variable => "variable",
      LocalKind.LoopVariable => "loop variable",
      LocalKind.PatternBinding => "pattern binding",
      LocalKind.RecoveryErr => "error binding",
      LocalKind.ImplicitIt => "implicit variable",
      _ => "variable",
    };
    ImmutableArray<RelatedLocation> related = local.NameSpan.Length > 0
        ? [new RelatedLocation(local.NameSpan, $"'{local.Name}' is declared immutable here", local.FilePath)]
        : [];
    string help = local.Kind == LocalKind.Variable
        ? $"declare '{local.Name}' with 'var'"
        : $"copy '{local.Name}' into a local 'var' if you need to mutate it";
    Report(
        DiagnosticCodes.CannotMutateImmutableValue,
        span,
        $"Cannot mutate immutable {kindLabel} '{local.Name}' of type '{local.Type.DisplayName}'.",
        related,
        help: help);
  }

  private void ReportCannotMutateImmutableParameter(SourceSpan span, ParameterSymbol param) {
    ImmutableArray<RelatedLocation> related = param.NameSpan.Length > 0
        ? [new RelatedLocation(param.NameSpan, $"'{param.Name}' is declared immutable here", param.FilePath)]
        : [];
    Report(
        DiagnosticCodes.CannotMutateImmutableValue,
        span,
        $"Cannot mutate immutable parameter '{param.Name}' of type '{param.Type.DisplayName}'.",
        related,
        help: $"mark parameter '{param.Name}' as 'mut'");
  }

  private void ReportReadOnlyField(SourceSpan span, string structName, FieldSymbol field) {
    StructSymbol declaringStruct = TypeCompatibility.GetOriginalFieldStruct(field);
    ImmutableArray<RelatedLocation> related = field.NameSpan.Length > 0
        ? [new RelatedLocation(field.NameSpan, $"field '{field.Name}' is declared read-only here", field.FilePath)]
        : [];
    Report(
        DiagnosticCodes.CannotMutateReadOnlyField,
        span,
        $"Cannot mutate read-only field '{field.Name}' of struct '{structName}'.",
        related,
        help: $"add 'mut' to field '{field.Name}' in struct '{declaringStruct.Name}' to allow mutation");
  }

  private void ReportReadOnlyInterfaceProperty(SourceSpan span, InterfaceSymbol ifaceSym, InterfaceProperty prop) {
    ImmutableArray<RelatedLocation> related = prop.NameSpan.Length > 0
        ? [new RelatedLocation(prop.NameSpan, $"property '{prop.Name}' is declared read-only here", ifaceSym.FilePath)]
        : [];
    Report(
        DiagnosticCodes.CannotMutateReadOnlyField,
        span,
        $"Cannot mutate read-only property '{prop.Name}' of interface '{ifaceSym.Name}'.",
        related,
        help: $"add 'mut' to property '{prop.Name}' in interface '{ifaceSym.Name}' to allow mutation");
  }

  private void ReportCannotMutateSelf(SourceSpan span, MethodSymbol? enclosingMethod) {
    string structName = _currentStruct?.Name ?? enclosingMethod?.ContainingType.Name ?? "<struct>";
    string methodName = enclosingMethod?.Name ?? "<method>";
    ImmutableArray<RelatedLocation> related = enclosingMethod is { NameSpan.Length: > 0 }
        ? [new RelatedLocation(enclosingMethod.NameSpan, $"method '{methodName}' is declared non-mutating here", enclosingMethod.FilePath)]
        : [];
    Report(
        DiagnosticCodes.CannotMutateSelfInNonMutMethod,
        span,
        $"Cannot mutate 'self' inside non-mutating method '{structName}:{methodName}'.",
        related,
        help: $"mark the method as 'mut fn {methodName}(...)'");
  }

  private static SourceSpan ArgumentSpanWithoutComma(ArgumentSyntax arg) =>
      arg.MutKeyword is { } mutTok
          ? SourceSpan.FromBounds(mutTok.Span.Start, arg.Expression.Span.End)
          : arg.Expression.Span;

  private void ReportMissingMutAtCallSite(
      ArgumentSyntax arg,
      int argIndex,
      string calleeKind,
      string calleeName,
      ParameterSymbol? paramSym) {
    string argText = FormatShortExpressionSnippet(arg.Expression.Span);
    ImmutableArray<RelatedLocation> related = paramSym is { NameSpan.Length: > 0 }
        ? [new RelatedLocation(paramSym.NameSpan, $"parameter '{paramSym.Name}' is declared 'mut' here", paramSym.FilePath)]
        : [];
    Report(
        DiagnosticCodes.MissingMutAtCallSite,
        ArgumentSpanWithoutComma(arg),
        $"Argument {argIndex + 1} for {calleeKind} '{calleeName}' is a 'mut' parameter and must be marked 'mut' at the call site.",
        related,
        help: $"write 'mut {argText}' at this call site");
  }

  private void ReportUnexpectedMutAtCallSite(
      ArgumentSyntax arg,
      int argIndex,
      string calleeKind,
      string calleeName,
      ParameterSymbol? paramSym) {
    string argText = FormatShortExpressionSnippet(arg.Expression.Span);
    ImmutableArray<RelatedLocation> related = paramSym is { NameSpan.Length: > 0 }
        ? [new RelatedLocation(paramSym.NameSpan, $"parameter '{paramSym.Name}' is not declared 'mut' here", paramSym.FilePath)]
        : [];
    Report(
        DiagnosticCodes.UnexpectedMutAtCallSite,
        ArgumentSpanWithoutComma(arg),
        $"Argument {argIndex + 1} for {calleeKind} '{calleeName}' is not a 'mut' parameter; remove 'mut' at the call site.",
        related,
        help: $"remove 'mut' before '{argText}'");
  }

  private void ReportInvalidMutExpression(SourceSpan span, MutationKind kind) {
    if (kind == MutationKind.MutArgument) {
      Report(
          DiagnosticCodes.InvalidMutArgument,
          span,
          "Cannot pass immutable or temporary expression as a 'mut' argument.",
          help: "pass a mutable 'var' binding, 'mut' parameter, or 'mut' field path");
    } else {
      Report(
          DiagnosticCodes.InvalidMutArgument,
          span,
          "Cannot mutate immutable or temporary expression.",
          help: "bind the value to a 'var' first if you need to mutate it");
    }
  }

  private void ReportOverlappingMutArguments(SourceSpan span, string rootName, SourceSpan earlierSpan) {
    ImmutableArray<RelatedLocation> related = earlierSpan.Length > 0
        ? [new RelatedLocation(earlierSpan, $"'{rootName}' is already mutably borrowed here", _state.FilePath)]
        : [];
    Report(
        DiagnosticCodes.OverlappingMutArguments,
        span,
        $"Cannot pass '{rootName}' as overlapping 'mut' arguments in the same call.",
        related,
        help: "pass distinct variables or mutate through a single 'mut' container");
  }

  private void ReportCannotBindMutatingMethod(
      SourceSpan span,
      string ownerName,
      string methodName,
      MethodSymbol? method,
      ExpressionSyntax? targetExpr) {
    ImmutableArray<RelatedLocation> related = method is { NameSpan.Length: > 0 }
        ? [new RelatedLocation(method.NameSpan, $"mutating method '{methodName}' is declared here", method.FilePath)]
        : [];
    string help;
    if (targetExpr is not null) {
      string targetText = FormatShortExpressionSnippet(targetExpr.Span);
      help = $"wrap the call in a lambda: fn () {{ {targetText}:{methodName}() }}";
    } else {
      help = $"wrap the call in a lambda: fn () {{ {methodName}() }}";
    }

    Report(
        DiagnosticCodes.CannotBindMutatingMethod,
        span,
        $"Cannot bind mutating method '{ownerName}:{methodName}' as a standalone function value.",
        related,
        help: help);
  }

  private void ReportCannotCaptureMutParameter(SourceSpan span, ParameterSymbol param) {
    ImmutableArray<RelatedLocation> related = param.NameSpan.Length > 0
        ? [new RelatedLocation(param.NameSpan, $"parameter '{param.Name}' is declared 'mut' here", param.FilePath)]
        : [];
    Report(
        DiagnosticCodes.CannotCaptureMutParameter,
        span,
        $"Cannot capture 'mut' parameter '{param.Name}' inside a closure.",
        related,
        help: $"copy the needed value into a local 'val' before the closure, or avoid capturing '{param.Name}'");
  }

  private void ReportCannotMutablyCaptureSelf(SourceSpan span, LocalSymbol selfLocal) {
    MethodSymbol? method = selfLocal.ContainingSymbol as MethodSymbol;
    ImmutableArray<RelatedLocation> related = method is { NameSpan.Length: > 0 }
        ? [new RelatedLocation(method.NameSpan, $"mutating method '{method.Name}' is declared here", method.FilePath)]
        : [];
    Report(
        DiagnosticCodes.CannotCaptureMutParameter,
        span,
        "Cannot mutably capture 'self' of a 'mut fn' inside a closure.",
        related,
        help: "copy the needed value into a local 'val' before the closure, or avoid capturing 'self'");
  }
}
