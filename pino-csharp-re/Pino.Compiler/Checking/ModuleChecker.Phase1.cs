using System.Collections.Frozen;
using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Checking;

internal sealed partial class CheckerSession {
  private void RunPhase1(ModuleCheckState state) {
    _cancellationToken.ThrowIfCancellationRequested();
    CompilationUnitSyntax root = state.Tree.Root;

    // 1. Hoist module-level declarations (D-165, FN-26) — never val/var (OQ-16 Option 2).
    CollectHoistedDeclarations(state, root.Items, state.ModuleScope, isModuleTopLevel: true);

    // 2. Finalize module declaration/export tables before resolving imports so cycles see hoisted names.
    var exports = new Dictionary<string, Symbol>(StringComparer.Ordinal);
    foreach (Symbol decl in state.TopLevelDeclarations) {
      if (decl.Visibility == SymbolVisibility.Public && !exports.ContainsKey(decl.Name)) {
        exports.Add(decl.Name, decl);
      }
    }
    state.Module.SetMembers(
        exports.ToFrozenDictionary(StringComparer.Ordinal),
        [.. state.TopLevelDeclarations]);

    // 3. Entry-point / Program Mode checks (MAIN-1..4, MAIN-E1..E4, MOD-E4, D-036).
    ValidateModuleEntryPoint(state, root.Items);

    // 4. Process imports & walk nested blocks/expressions for scopes and nested hoisting.
    foreach (SyntaxNode item in root.Items) {
      _cancellationToken.ThrowIfCancellationRequested();
      WalkItemPhase1(state, item, state.ModuleScope);
    }
  }

  private void ValidateModuleEntryPoint(ModuleCheckState state, ImmutableArray<SyntaxNode> items) {
    FunctionDeclarationSyntax? mainDecl = null;
    foreach (SyntaxNode item in items) {
      if (item is FunctionDeclarationSyntax fn && !IsMissingToken(fn.NameToken)) {
        string name = state.Lex.GetText(fn.NameToken).ToString();
        if (string.Equals(name, "main", StringComparison.Ordinal)) {
          if (!state.IsRoot) {
            state.Diagnostics.ReportError(
                DiagnosticCodes.ModuleDefinesMain,
                fn.NameToken.Span,
                $"Module '{state.Module.Name}' cannot define a 'main' function.");
          } else {
            mainDecl ??= fn;
            if (!fn.Parameters.IsEmpty) {
              state.Diagnostics.ReportError(
                  DiagnosticCodes.MainFunctionHasParameters,
                  fn.OpenParenToken?.Span ?? fn.NameToken.Span,
                  "Function 'main' cannot take parameters.");
            }
          }
        }
      }
    }

    if (!state.IsRoot || mainDecl is null) {
      return;
    }

    state.IsProgramMode = true;

    foreach (SyntaxNode item in items) {
      switch (item) {
        case FunctionDeclarationSyntax:
        case StructDeclarationSyntax:
        case InterfaceDeclarationSyntax:
        case EnumDeclarationSyntax:
        case UnionDeclarationSyntax:
        case ModuleDeclarationSyntax:
        case TestDeclarationSyntax:
        case ImportStatementSyntax:
        case FromImportStatementSyntax:
        case MissingStatementSyntax:
        case ErrorStatementSyntax:
          break;

        case VariableDeclarationSyntax varDecl:
          if (varDecl.KeywordToken.Kind == TokenKind.VarKeyword) {
            string varName = IsMissingToken(varDecl.NameToken)
                ? "<unknown>"
                : state.Lex.GetText(varDecl.NameToken).ToString();
            state.Diagnostics.ReportError(
                DiagnosticCodes.InvalidTopLevelInProgramMode,
                varDecl.Span,
                $"Top-level variable '{varName}' must be declared with 'val' in program mode.");
          }
          break;

        case TupleDestructuringStatementSyntax destructDecl:
          if (destructDecl.KeywordToken.Kind == TokenKind.VarKeyword) {
            state.Diagnostics.ReportError(
                DiagnosticCodes.InvalidTopLevelInProgramMode,
                destructDecl.Span,
                "Top-level variable must be declared with 'val' in program mode.");
          }
          break;

        case ExpressionStatementSyntax exprStmt:
          if (exprStmt.Expression is CallExpressionSyntax { Callee: IdentifierExpressionSyntax calleeId }
              && !IsMissingToken(calleeId.IdentifierToken)
              && string.Equals(state.Lex.GetText(calleeId.IdentifierToken).ToString(), "main", StringComparison.Ordinal)) {
            state.Diagnostics.ReportError(
                DiagnosticCodes.InvalidTopLevelInProgramMode,
                exprStmt.Span,
                "Explicit call to 'main()' is not allowed at the top level when a 'main' function is defined.");
          } else {
            state.Diagnostics.ReportError(
                DiagnosticCodes.InvalidTopLevelInProgramMode,
                exprStmt.Span,
                "Top-level statements are not allowed at the top level when a 'main' function is defined.");
          }
          break;

        default:
          state.Diagnostics.ReportError(
              DiagnosticCodes.InvalidTopLevelInProgramMode,
              item.Span,
              "Top-level statements are not allowed at the top level when a 'main' function is defined.");
          break;
      }
    }
  }

  private void CollectHoistedDeclarations(
      ModuleCheckState state,
      ImmutableArray<SyntaxNode> items,
      Scope scope,
      bool isModuleTopLevel) {
    foreach (SyntaxNode item in items) {
      _cancellationToken.ThrowIfCancellationRequested();
      switch (item) {
        case FunctionDeclarationSyntax fnDecl:
          HoistFunction(state, fnDecl, scope, isModuleTopLevel);
          break;

        case StructDeclarationSyntax structDecl:
          HoistStruct(state, structDecl, scope, isModuleTopLevel);
          break;

        case InterfaceDeclarationSyntax ifaceDecl:
          HoistInterface(state, ifaceDecl, scope, isModuleTopLevel);
          break;

        case EnumDeclarationSyntax enumDecl:
          HoistEnum(state, enumDecl, scope, isModuleTopLevel);
          break;

        case UnionDeclarationSyntax unionDecl:
          HoistUnion(state, unionDecl, scope, isModuleTopLevel);
          break;
      }
    }
  }

  private void HoistFunction(
      ModuleCheckState state,
      FunctionDeclarationSyntax decl,
      Scope enclosingScope,
      bool isModuleTopLevel) {
    if (IsMissingToken(decl.NameToken)) {
      var errScope = new Scope(ScopeKind.Function, enclosingScope, ErrorSymbol.Instance);
      state.SetScope(decl, errScope);
      state.SetDeclaredSymbol(decl, ErrorSymbol.Instance);
      return;
    }

    string name = state.Lex.GetText(decl.NameToken).ToString();
    SymbolVisibility visibility = GetVisibility(decl.Visibility);
    ImmutableArray<TypeParameterSymbol> typeParams = CollectGenericTypeParameters(state, decl.GenericDecorator);

    var fnSymbol = new FunctionSymbol(
        name,
        visibility,
        typeParams,
        parameters: [],
        returnType: VoidType.Instance,
        returnState: decl.ReturnType is not null ? ReturnInferenceState.Explicit : ReturnInferenceState.Unchecked,
        decl.Id,
        decl.NameToken.Span,
        state.FilePath,
        enclosingScope.Owner);

    foreach (TypeParameterSymbol tp in typeParams) {
      tp.Owner = fnSymbol;
    }

    if (!enclosingScope.TryDeclareHoisted(fnSymbol, out Symbol? existing)) {
      ReportDuplicate(state, name, decl.NameToken.Span, existing!);
    } else if (isModuleTopLevel) {
      state.TopLevelDeclarations.Add(fnSymbol);
    }

    state.SetDeclaredSymbol(decl, fnSymbol);
    var fnScope = new Scope(ScopeKind.Function, enclosingScope, fnSymbol);
    state.SetScope(decl, fnScope);
    state.Functions.Add((decl, fnSymbol, fnScope));
  }

  private void HoistStruct(
      ModuleCheckState state,
      StructDeclarationSyntax decl,
      Scope enclosingScope,
      bool isModuleTopLevel) {
    if (IsMissingToken(decl.NameToken)) {
      var errScope = new Scope(ScopeKind.StructMember, enclosingScope, ErrorSymbol.Instance);
      state.SetScope(decl, errScope);
      state.SetDeclaredSymbol(decl, ErrorSymbol.Instance);
      return;
    }

    string name = state.Lex.GetText(decl.NameToken).ToString();
    if (!IsAsciiCapitalized(name)) {
      ReportCapitalizationError(state, name, decl.NameToken.Span);
    }

    SymbolVisibility visibility = GetVisibility(decl.Visibility);
    ImmutableArray<TypeParameterSymbol> typeParams = CollectGenericTypeParameters(state, decl.GenericDecorator);

    var structSymbol = new StructSymbol(
        name,
        visibility,
        typeParams,
        decl.Id,
        decl.NameToken.Span,
        state.FilePath,
        enclosingScope.Owner);

    foreach (TypeParameterSymbol tp in typeParams) {
      tp.Owner = structSymbol;
    }

    if (!enclosingScope.TryDeclareHoisted(structSymbol, out Symbol? existing)) {
      ReportDuplicate(state, name, decl.NameToken.Span, existing!);
    } else if (isModuleTopLevel) {
      state.TopLevelDeclarations.Add(structSymbol);
    }

    state.SetDeclaredSymbol(decl, structSymbol);
    var typeParamScope = new Scope(ScopeKind.StructMember, enclosingScope, structSymbol);
    state.SetScope(decl, typeParamScope);
    state.Structs.Add((decl, structSymbol, typeParamScope, enclosingScope));
  }

  private void HoistInterface(
      ModuleCheckState state,
      InterfaceDeclarationSyntax decl,
      Scope enclosingScope,
      bool isModuleTopLevel) {
    if (IsMissingToken(decl.NameToken)) {
      var errScope = new Scope(ScopeKind.Block, enclosingScope, ErrorSymbol.Instance);
      state.SetScope(decl, errScope);
      state.SetDeclaredSymbol(decl, ErrorSymbol.Instance);
      return;
    }

    string name = state.Lex.GetText(decl.NameToken).ToString();
    if (!IsAsciiCapitalized(name)) {
      ReportCapitalizationError(state, name, decl.NameToken.Span);
    }

    SymbolVisibility visibility = GetVisibility(decl.Visibility);
    ImmutableArray<TypeParameterSymbol> typeParams = CollectGenericTypeParameters(state, decl.GenericDecorator);

    var ifaceSymbol = new InterfaceSymbol(
        name,
        visibility,
        typeParams,
        decl.Id,
        decl.NameToken.Span,
        state.FilePath,
        enclosingScope.Owner);

    foreach (TypeParameterSymbol tp in typeParams) {
      tp.Owner = ifaceSymbol;
    }

    if (!enclosingScope.TryDeclareHoisted(ifaceSymbol, out Symbol? existing)) {
      ReportDuplicate(state, name, decl.NameToken.Span, existing!);
    } else if (isModuleTopLevel) {
      state.TopLevelDeclarations.Add(ifaceSymbol);
    }

    state.SetDeclaredSymbol(decl, ifaceSymbol);
    var ifaceScope = new Scope(ScopeKind.Block, enclosingScope, ifaceSymbol);
    state.SetScope(decl, ifaceScope);
    state.Interfaces.Add((decl, ifaceSymbol, ifaceScope));
  }

  private void HoistEnum(
      ModuleCheckState state,
      EnumDeclarationSyntax decl,
      Scope enclosingScope,
      bool isModuleTopLevel) {
    if (IsMissingToken(decl.NameToken)) {
      state.SetDeclaredSymbol(decl, ErrorSymbol.Instance);
      return;
    }

    string name = state.Lex.GetText(decl.NameToken).ToString();
    if (!IsAsciiCapitalized(name)) {
      ReportCapitalizationError(state, name, decl.NameToken.Span);
    }

    SymbolVisibility visibility = GetVisibility(decl.Visibility);
    var enumSymbol = new EnumSymbol(
        name,
        visibility,
        decl.Id,
        decl.NameToken.Span,
        state.FilePath,
        enclosingScope.Owner);

    var members = ImmutableArray.CreateBuilder<EnumMemberSymbol>();
    var seenMembers = new Dictionary<string, EnumMemberSymbol>(StringComparer.Ordinal);
    int ordinal = 0;

    foreach (SyntaxNode node in decl.Members) {
      if (node is not EnumMemberSyntax memberSyntax || IsMissingToken(memberSyntax.NameToken)) {
        continue;
      }

      string memberName = state.Lex.GetText(memberSyntax.NameToken).ToString();
      if (!IsAsciiCapitalized(memberName)) {
        ReportCapitalizationError(state, memberName, memberSyntax.NameToken.Span);
      }

      var memberSymbol = new EnumMemberSymbol(
          memberName,
          ordinal++,
          enumSymbol,
          memberSyntax.Id,
          memberSyntax.NameToken.Span,
          state.FilePath);

      if (seenMembers.TryGetValue(memberName, out EnumMemberSymbol? existingMember)) {
        ReportDuplicate(state, memberName, memberSyntax.NameToken.Span, existingMember);
      } else {
        seenMembers.Add(memberName, memberSymbol);
        members.Add(memberSymbol);
      }

      state.SetDeclaredSymbol(memberSyntax, memberSymbol);
    }

    enumSymbol.Members = members.ToImmutable();

    if (!enclosingScope.TryDeclareHoisted(enumSymbol, out Symbol? existing)) {
      ReportDuplicate(state, name, decl.NameToken.Span, existing!);
    } else if (isModuleTopLevel) {
      state.TopLevelDeclarations.Add(enumSymbol);
    }

    state.SetDeclaredSymbol(decl, enumSymbol);
    state.Enums.Add((decl, enumSymbol, enclosingScope));
  }

  private void HoistUnion(
      ModuleCheckState state,
      UnionDeclarationSyntax decl,
      Scope enclosingScope,
      bool isModuleTopLevel) {
    if (IsMissingToken(decl.NameToken)) {
      var errScope = new Scope(ScopeKind.Block, enclosingScope, ErrorSymbol.Instance);
      state.SetScope(decl, errScope);
      state.SetDeclaredSymbol(decl, ErrorSymbol.Instance);
      return;
    }

    string name = state.Lex.GetText(decl.NameToken).ToString();
    if (!IsAsciiCapitalized(name)) {
      ReportCapitalizationError(state, name, decl.NameToken.Span);
    }

    SymbolVisibility visibility = GetVisibility(decl.Visibility);
    ImmutableArray<TypeParameterSymbol> typeParams = CollectGenericTypeParameters(state, decl.GenericDecorator);

    var unionSymbol = new UnionSymbol(
        name,
        visibility,
        typeParams,
        usesNamedFields: false,
        decl.Id,
        decl.NameToken.Span,
        state.FilePath,
        enclosingScope.Owner);

    foreach (TypeParameterSymbol tp in typeParams) {
      tp.Owner = unionSymbol;
    }

    var variants = ImmutableArray.CreateBuilder<UnionVariantSymbol>();
    var seenVariants = new Dictionary<string, UnionVariantSymbol>(StringComparer.Ordinal);
    int tagOrdinal = 0;
    bool usesNamedFields = false;

    foreach (SyntaxNode node in decl.Variants) {
      if (node is not UnionVariantSyntax variantSyntax || IsMissingToken(variantSyntax.NameToken)) {
        continue;
      }

      string variantName = state.Lex.GetText(variantSyntax.NameToken).ToString();
      if (!IsAsciiCapitalized(variantName)) {
        ReportCapitalizationError(state, variantName, variantSyntax.NameToken.Span);
      }

      foreach (UnionVariantFieldSyntax fieldSyntax in variantSyntax.Fields) {
        if (fieldSyntax.NameToken is { } fieldNameTok && !IsMissingToken(fieldNameTok)) {
          usesNamedFields = true;
          break;
        }
      }

      var variantSymbol = new UnionVariantSymbol(
          variantName,
          tagOrdinal++,
          unionSymbol,
          variantSyntax.Id,
          variantSyntax.NameToken.Span,
          state.FilePath);

      if (seenVariants.TryGetValue(variantName, out UnionVariantSymbol? existingVariant)) {
        ReportDuplicate(state, variantName, variantSyntax.NameToken.Span, existingVariant);
      } else {
        seenVariants.Add(variantName, variantSymbol);
        variants.Add(variantSymbol);
      }

      state.SetDeclaredSymbol(variantSyntax, variantSymbol);
    }

    unionSymbol.UsesNamedFields = usesNamedFields;
    unionSymbol.Variants = variants.ToImmutable();

    if (!enclosingScope.TryDeclareHoisted(unionSymbol, out Symbol? existing)) {
      ReportDuplicate(state, name, decl.NameToken.Span, existing!);
    } else if (isModuleTopLevel) {
      state.TopLevelDeclarations.Add(unionSymbol);
    }

    state.SetDeclaredSymbol(decl, unionSymbol);
    var unionScope = new Scope(ScopeKind.Block, enclosingScope, unionSymbol);
    state.SetScope(decl, unionScope);
    state.Unions.Add((decl, unionSymbol, unionScope));
  }

  private ImmutableArray<TypeParameterSymbol> CollectGenericTypeParameters(
      ModuleCheckState state,
      GenericDecoratorSyntax? decorator) {
    if (decorator is null || decorator.Parameters.IsEmpty) {
      return [];
    }

    var builder = ImmutableArray.CreateBuilder<TypeParameterSymbol>(decorator.Parameters.Length);
    for (int i = 0; i < decorator.Parameters.Length; i++) {
      GenericParameterSyntax gp = decorator.Parameters[i];
      if (IsMissingToken(gp.NameToken)) {
        continue;
      }

      string gpName = state.Lex.GetText(gp.NameToken).ToString();
      var tp = new TypeParameterSymbol(gpName, i, gp.Id, gp.NameToken.Span, state.FilePath);
      builder.Add(tp);
      state.SetDeclaredSymbol(gp, tp);
    }

    return builder.ToImmutable();
  }

  private void WalkItemPhase1(ModuleCheckState state, SyntaxNode item, Scope enclosingScope) {
    switch (item) {
      case ImportStatementSyntax importStmt:
        ProcessImport(state, importStmt);
        break;

      case FromImportStatementSyntax fromImportStmt:
        ProcessFromImport(state, fromImportStmt, enclosingScope);
        break;

      case FunctionDeclarationSyntax fnDecl: {
          Scope fnScope = state.ScopesByBlockId[fnDecl.Id] ?? enclosingScope;
          WalkBlockPhase1(state, fnDecl.Body, fnScope);
          break;
        }

      case StructDeclarationSyntax structDecl: {
          Scope typeParamScope = state.ScopesByBlockId[structDecl.Id] ?? enclosingScope;
          foreach (SyntaxNode member in structDecl.Members) {
            if (member is MethodDeclarationSyntax methodDecl) {
              var methodScope = new Scope(ScopeKind.Method, typeParamScope, state.DeclaredSymbols[structDecl.Id]);
              state.SetScope(methodDecl, methodScope);
              WalkBlockPhase1(state, methodDecl.Body, methodScope);
            }
          }
          break;
        }

      case TestDeclarationSyntax testDecl: {
          var testScope = new Scope(ScopeKind.Block, enclosingScope);
          state.SetScope(testDecl, testScope);
          WalkExpressionPhase1(state, testDecl.Name, enclosingScope);
          WalkBlockPhase1(state, testDecl.Body, testScope);
          break;
        }

      case BlockStatementSyntax blockStmt:
        WalkBlockPhase1(state, blockStmt, enclosingScope);
        break;

      case IfStatementSyntax ifStmt: {
          WalkExpressionPhase1(state, ifStmt.Condition, enclosingScope);
          var ifScope = new Scope(ScopeKind.Block, enclosingScope);
          state.SetScope(ifStmt, ifScope);
          WalkBlockPhase1(state, ifStmt.ThenBranch, ifScope);
          if (ifStmt.ElseBranch is not null) {
            WalkItemPhase1(state, ifStmt.ElseBranch, enclosingScope);
          }
          break;
        }

      case ForStatementSyntax forStmt: {
          if (forStmt.Header is ForConditionHeaderSyntax condHeader) {
            WalkExpressionPhase1(state, condHeader.Condition, enclosingScope);
          } else if (forStmt.Header is ForInHeaderSyntax inHeader) {
            WalkExpressionPhase1(state, inHeader.Collection, enclosingScope);
          }

          var loopScope = new Scope(ScopeKind.Loop, enclosingScope);
          state.SetScope(forStmt, loopScope);
          WalkBlockPhase1(state, forStmt.Body, loopScope);
          break;
        }

      case MatchStatementSyntax matchStmt:
        WalkExpressionPhase1(state, matchStmt.Subject, enclosingScope);
        WalkMatchArmsPhase1(state, matchStmt.Arms, enclosingScope);
        break;

      case VariableDeclarationSyntax varDecl:
        WalkExpressionPhase1(state, varDecl.Initializer, enclosingScope);
        break;

      case TupleDestructuringStatementSyntax destructStmt:
        WalkExpressionPhase1(state, destructStmt.Initializer, enclosingScope);
        break;

      case ExpressionStatementSyntax exprStmt:
        WalkExpressionPhase1(state, exprStmt.Expression, enclosingScope);
        break;

      case ReturnStatementSyntax retStmt:
        if (retStmt.Expression is not null) {
          WalkExpressionPhase1(state, retStmt.Expression, enclosingScope);
        }
        break;

      case YieldStatementSyntax yieldStmt:
        WalkExpressionPhase1(state, yieldStmt.Expression, enclosingScope);
        break;

      case AssertStatementSyntax assertStmt:
        WalkExpressionPhase1(state, assertStmt.Condition, enclosingScope);
        if (assertStmt.Message is not null) {
          WalkExpressionPhase1(state, assertStmt.Message, enclosingScope);
        }
        break;
    }
  }

  private void WalkBlockPhase1(ModuleCheckState state, BlockStatementSyntax block, Scope parentScope) {
    var blockScope = new Scope(ScopeKind.Block, parentScope, parentScope.Owner);
    state.SetScope(block, blockScope);
    CollectHoistedDeclarations(state, block.Items, blockScope, isModuleTopLevel: false);

    foreach (SyntaxNode item in block.Items) {
      _cancellationToken.ThrowIfCancellationRequested();
      WalkItemPhase1(state, item, blockScope);
    }
  }

  private void WalkMatchArmsPhase1(
      ModuleCheckState state,
      ImmutableArray<SyntaxNode> arms,
      Scope enclosingScope) {
    foreach (SyntaxNode arm in arms) {
      if (arm is WhenArmSyntax whenArm) {
        var armScope = new Scope(ScopeKind.MatchArm, enclosingScope, enclosingScope.Owner);
        state.SetScope(whenArm, armScope);
        if (whenArm.ExpressionBody is not null) {
          WalkExpressionPhase1(state, whenArm.ExpressionBody, armScope);
        }
        if (whenArm.BlockBody is not null) {
          WalkBlockPhase1(state, whenArm.BlockBody, armScope);
        }
      } else if (arm is ElseArmSyntax elseArm) {
        var elseScope = new Scope(ScopeKind.MatchArm, enclosingScope, enclosingScope.Owner);
        state.SetScope(elseArm, elseScope);
        if (elseArm.ExpressionBody is not null) {
          WalkExpressionPhase1(state, elseArm.ExpressionBody, elseScope);
        }
        if (elseArm.BlockBody is not null) {
          WalkBlockPhase1(state, elseArm.BlockBody, elseScope);
        }
      }
    }
  }

  private void WalkExpressionPhase1(ModuleCheckState state, ExpressionSyntax expr, Scope enclosingScope) {
    switch (expr) {
      case LambdaExpressionSyntax lambda: {
          var lambdaScope = new Scope(ScopeKind.Lambda, enclosingScope, enclosingScope.Owner);
          state.SetScope(lambda, lambdaScope);
          if (lambda.BodyExpression is not null) {
            WalkExpressionPhase1(state, lambda.BodyExpression, lambdaScope);
          }
          if (lambda.BodyBlock is not null) {
            WalkBlockPhase1(state, lambda.BodyBlock, lambdaScope);
          }
          break;
        }

      case RecoveryExpressionSyntax rec: {
          WalkExpressionPhase1(state, rec.Expression, enclosingScope);
          var recScope = new Scope(ScopeKind.Recovery, enclosingScope, enclosingScope.Owner);
          state.SetScope(rec.Block, recScope);
          CollectHoistedDeclarations(state, rec.Block.Items, recScope, isModuleTopLevel: false);
          foreach (SyntaxNode item in rec.Block.Items) {
            WalkItemPhase1(state, item, recScope);
          }
          break;
        }

      case MatchExpressionSyntax matchExpr:
        WalkExpressionPhase1(state, matchExpr.Subject, enclosingScope);
        WalkMatchArmsPhase1(state, matchExpr.Arms, enclosingScope);
        break;

      case ParenthesizedExpressionSyntax paren:
        WalkExpressionPhase1(state, paren.Expression, enclosingScope);
        break;

      case UnaryExpressionSyntax unary:
        WalkExpressionPhase1(state, unary.Operand, enclosingScope);
        break;

      case BinaryExpressionSyntax binary:
        WalkExpressionPhase1(state, binary.Left, enclosingScope);
        WalkExpressionPhase1(state, binary.Right, enclosingScope);
        break;

      case IsPatternExpressionSyntax isPat:
        WalkExpressionPhase1(state, isPat.Expression, enclosingScope);
        break;

      case AssignmentExpressionSyntax assign:
        WalkExpressionPhase1(state, assign.Target, enclosingScope);
        WalkExpressionPhase1(state, assign.Value, enclosingScope);
        break;

      case IndexExpressionSyntax index:
        WalkExpressionPhase1(state, index.Target, enclosingScope);
        WalkExpressionPhase1(state, index.Index, enclosingScope);
        break;

      case MemberAccessExpressionSyntax member:
        WalkExpressionPhase1(state, member.Target, enclosingScope);
        break;

      case StaticAccessExpressionSyntax staticAccess:
        WalkExpressionPhase1(state, staticAccess.Target, enclosingScope);
        break;

      case CallExpressionSyntax call:
        WalkExpressionPhase1(state, call.Callee, enclosingScope);
        foreach (ArgumentSyntax arg in call.Arguments) {
          WalkExpressionPhase1(state, arg.Expression, enclosingScope);
        }
        break;

      case BubbleExpressionSyntax bubble:
        WalkExpressionPhase1(state, bubble.Expression, enclosingScope);
        break;

      case VectorLiteralExpressionSyntax vecLit:
        foreach (ExpressionSyntax elem in vecLit.Elements) {
          WalkExpressionPhase1(state, elem, enclosingScope);
        }
        break;

      case VectorInitializerExpressionSyntax vecInit:
        foreach (VectorPropertySyntax prop in vecInit.Properties) {
          WalkExpressionPhase1(state, prop.Value, enclosingScope);
        }
        break;

      case MapLiteralExpressionSyntax mapLit:
        foreach (MapEntrySyntax entry in mapLit.Entries) {
          WalkExpressionPhase1(state, entry.Key, enclosingScope);
          WalkExpressionPhase1(state, entry.Value, enclosingScope);
        }
        break;

      case SetLiteralExpressionSyntax setLit:
        foreach (ExpressionSyntax elem in setLit.Elements) {
          WalkExpressionPhase1(state, elem, enclosingScope);
        }
        break;

      case StructLiteralExpressionSyntax structLit:
        foreach (StructFieldInitSyntax field in structLit.Fields) {
          if (field.Value is not null) {
            WalkExpressionPhase1(state, field.Value, enclosingScope);
          }
        }
        break;

      case TupleLiteralExpressionSyntax tupleLit:
        foreach (TupleFieldSyntax field in tupleLit.Fields) {
          if (field.Value is not null) {
            WalkExpressionPhase1(state, field.Value, enclosingScope);
          }
        }
        break;

      case IfExpressionSyntax ifExpr:
        WalkExpressionPhase1(state, ifExpr.Condition, enclosingScope);
        WalkExpressionPhase1(state, ifExpr.ThenBranch, enclosingScope);
        WalkExpressionPhase1(state, ifExpr.ElseBranch, enclosingScope);
        break;

      case InterpolatedStringExpressionSyntax interp:
        foreach (StringPartSyntax part in interp.Parts) {
          if (part is StringInterpolationPartSyntax exprPart) {
            WalkExpressionPhase1(state, exprPart.Expression, enclosingScope);
          }
        }
        break;
    }
  }

  private void ProcessImport(ModuleCheckState state, ImportStatementSyntax stmt) {
    if (IsMissingToken(stmt.ModuleToken)) {
      return;
    }

    string moduleName = state.Lex.GetText(stmt.ModuleToken).ToString();
    ModuleSymbol? targetModule = ResolveAndCheckImport(state, moduleName, stmt.ModuleToken.Span);
    if (targetModule is null) {
      return;
    }

    state.ImportedModules[moduleName] = targetModule;
    state.SetReferencedSymbol(stmt, targetModule);
    if (string.Equals(targetModule.Name, moduleName, StringComparison.Ordinal)) {
      state.ModuleScope.TryDeclare(targetModule, out _);
    }
  }

  private void ProcessFromImport(
      ModuleCheckState state,
      FromImportStatementSyntax stmt,
      Scope targetScope) {
    if (IsMissingToken(stmt.ModuleToken)) {
      return;
    }

    string moduleName = state.Lex.GetText(stmt.ModuleToken).ToString();
    ModuleSymbol? targetModule = ResolveAndCheckImport(state, moduleName, stmt.ModuleToken.Span);
    if (targetModule is null) {
      return;
    }

    state.SetReferencedSymbol(stmt, targetModule);

    foreach (Token nameToken in stmt.Names) {
      if (IsMissingToken(nameToken)) {
        continue;
      }

      string memberName = state.Lex.GetText(nameToken).ToString();
      Symbol? found = FindModuleDeclaration(targetModule, memberName);
      if (found is null) {
        state.Diagnostics.ReportError(
            DiagnosticCodes.ModuleMemberNotFound,
            nameToken.Span,
            $"Module '{moduleName}' does not export '{memberName}'.");
        DeclareFailedImport(targetScope, nameToken, memberName, state);
        continue;
      }

      bool accessible = found.Visibility == SymbolVisibility.Public
          || (found.Visibility == SymbolVisibility.Testable && state.IsTestFile)
          || ReferenceEquals(targetModule, state.Module);

      if (!accessible) {
        string reason = found.Visibility == SymbolVisibility.Testable
            ? $"Module '{moduleName}' does not export '{memberName}' (marked @@Testable; only accessible from test files)."
            : $"Module '{moduleName}' does not export '{memberName}' because it is private.";
        state.Diagnostics.ReportError(
            DiagnosticCodes.SymbolNotPublic,
            nameToken.Span,
            reason);
        DeclareFailedImport(targetScope, nameToken, memberName, state);
        continue;
      }

      if (!targetScope.TryDeclare(found, out Symbol? existing)) {
        if (!ReferenceEquals(existing, found)) {
          ReportDuplicate(state, memberName, nameToken.Span, existing!);
        }
      }
    }
  }

  /// <summary>
  /// A name that failed to import is bound to an <see cref="ErrorSymbol"/> so that later uses
  /// resolve silently instead of cascading into undeclared-identifier errors (Amendment A4).
  /// </summary>
  private static void DeclareFailedImport(Scope targetScope, Token nameToken, string memberName, ModuleCheckState state) =>
      targetScope.TryDeclare(
          new ErrorSymbol(memberName, -1, nameToken.Span, state.FilePath),
          out _);

  internal static Symbol? FindModuleDeclaration(ModuleSymbol module, string name) {
    if (module.TryGetExport(name, out Symbol? exported)) {
      return exported;
    }

    foreach (Symbol decl in module.Declarations) {
      if (string.Equals(decl.Name, name, StringComparison.Ordinal)) {
        return decl;
      }
    }

    return null;
  }
}
