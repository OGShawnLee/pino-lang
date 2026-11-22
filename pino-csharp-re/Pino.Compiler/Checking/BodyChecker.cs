using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

/// <summary>
/// Phase 3 of the checker: bidirectional <c>Synthesize</c> / <c>Check</c> over the statements and
/// expressions of one module (variables, operators, collections, <c>if</c>, <c>for</c>,
/// <c>assert</c>, <c>test</c>, structs, methods, interfaces, enums, tuples, direct/stdlib calls,
/// two-phase SCC return-type inference, contextual lambdas, implicit <c>it</c>, AD-002 closure
/// capture lists, <c>@@Generic</c> unification/bounds/instantiations, <c>D-055</c>, and
/// <c>?</c>/<c>or</c> error handling). Patterns and flow land in ticket 040.
/// </summary>
internal sealed partial class BodyChecker {
  private readonly CheckerSession _session;
  private readonly ModuleCheckState _state;
  private readonly CancellationToken _cancellationToken;
  private readonly TypeInterner _types;
  private readonly PreludeSymbols _prelude;
  private Scope _scope;
  private int _tolerantNames;
  private Symbol? _currentCallable;
  private StructSymbol? _currentStruct;
  private bool _isStaticMethod;

  public BodyChecker(CheckerSession session, ModuleCheckState state, CancellationToken cancellationToken) {
    _session = session;
    _state = state;
    _cancellationToken = cancellationToken;
    _types = session.Types;
    _prelude = session.Prelude;
    _scope = state.ModuleScope;
    _rootDiagnostics = state.Diagnostics;
    _rootAssignedSymbols = state.AssignedSymbols;
    _state.EnsureStructMethodsResolved = EnsureStructMethodsResolved;
    InitializeCallableRegistry();
  }

  /// <summary>Checks every top-level item of the module in textual order (OQ-16 Option 2).</summary>
  public void Run() {
    CompilationUnitSyntax root = _state.Tree.Root;

    foreach (SyntaxNode item in root.Items) {
      _cancellationToken.ThrowIfCancellationRequested();
      CheckItem(item);
    }

    // Sync promoted embedded methods' inferred return types with their declaring embedded methods.
    foreach ((_, StructSymbol structSym, _, _) in _state.Structs) {
      foreach (MethodSymbol method in structSym.Methods) {
        if (method.PromotedFrom is { } original) {
          method.ReturnType = original.ReturnType;
          method.ReturnState = original.ReturnState;
        }
      }
    }

    // Flush deferred Phase 2 generic constraint checks now that all struct methods are resolved.
    foreach ((TypeSyntax argSyntax, TypeParameterSymbol tp, TypeSymbol argType, ImmutableArray<TypeParameterSymbol> ownerParams, ImmutableArray<TypeSymbol> ownerArgs) in _state.DeferredConstraintChecks) {
      _session.VerifyGenericTypeConstraint(_state, argSyntax, tp, argType, ownerParams, ownerArgs);
    }

    // Record concrete generic instantiations from Phase 2 type annotations.
    foreach (TypeSymbol resolved in _state.ResolvedTypes) {
      RecordInstantiationsInType(resolved);
    }

    RunPhase4DefiniteReturns();
    FillMissingExpressionTypes(root);
  }

  // ---------------------------------------------------------------------------------------------
  // Statements and declarations
  // ---------------------------------------------------------------------------------------------

  private void CheckItem(SyntaxNode item) {
    _cancellationToken.ThrowIfCancellationRequested();
    switch (item) {
      case VariableDeclarationSyntax variable:
        CheckVariableDeclaration(variable);
        break;

      case FunctionDeclarationSyntax function:
        CheckFunction(function);
        break;

      case StructDeclarationSyntax structDeclaration:
        CheckStruct(structDeclaration);
        break;

      case TestDeclarationSyntax test:
        CheckTest(test);
        break;

      case ExpressionStatementSyntax expressionStatement:
        Synthesize(expressionStatement.Expression);
        break;

      case IfStatementSyntax ifStatement:
        CheckIfStatement(ifStatement);
        break;

      case ForStatementSyntax forStatement:
        CheckFor(forStatement);
        break;

      case BlockStatementSyntax block:
        CheckBlock(block);
        break;

      case AssertStatementSyntax assertStatement:
        CheckAssert(assertStatement);
        break;

      case ReturnStatementSyntax returnStatement:
        CheckReturn(returnStatement);
        break;

      case YieldStatementSyntax yieldStatement:
        CheckYield(yieldStatement);
        break;

      case BreakStatementSyntax breakStatement:
        CheckBreak(breakStatement);
        break;

      case ContinueStatementSyntax continueStatement:
        CheckContinue(continueStatement);
        break;

      case MatchStatementSyntax matchStatement:
        CheckMatchStatement(matchStatement);
        break;

      case TupleDestructuringStatementSyntax destructuring:
        CheckTupleDestructuring(destructuring);
        break;
    }
  }

  private void CheckBlock(BlockStatementSyntax block) {
    Scope blockScope = ScopeOf(block, ScopeKind.Block);
    using ScopeRestore _ = EnterScope(blockScope);
    foreach (SyntaxNode item in block.Items) {
      CheckItem(item);
    }
  }

  private void CheckFunction(FunctionDeclarationSyntax function) {
    if (_state.DeclaredSymbols[function.Id] is not FunctionSymbol fnSymbol) {
      return;
    }

    EnsureCallableBodyChecked(fnSymbol);
  }

  private void CheckStruct(StructDeclarationSyntax structDeclaration) {
    if (_state.DeclaredSymbols[structDeclaration.Id] is not StructSymbol structSymbol) {
      return;
    }

    foreach (SyntaxNode member in structDeclaration.Members) {
      if (member is MethodDeclarationSyntax methodDeclaration) {
        CheckMethod(structSymbol, methodDeclaration);
      }
    }
  }

  private void CheckMethod(StructSymbol structSymbol, MethodDeclarationSyntax methodDeclaration) {
    _ = structSymbol;
    if (_state.DeclaredSymbols[methodDeclaration.Id] is not MethodSymbol methodSymbol) {
      return;
    }

    EnsureCallableBodyChecked(methodSymbol);
  }

  private void CheckTest(TestDeclarationSyntax test) {
    TypeSymbol nameType = Synthesize(test.Name);
    if (!IsSilent(nameType) && !ReferenceEquals(nameType, _types.String)) {
      Report(
          DiagnosticCodes.TypeMismatch,
          test.Name.Span,
          $"Test name must have type 'string', but got '{nameType.DisplayName}'.");
    }

    Scope testScope = ScopeOf(test, ScopeKind.Block);
    Symbol? prevCallable = _currentCallable;
    StructSymbol? prevStruct = _currentStruct;
    bool prevStatic = _isStaticMethod;
    ReturnCollector? prevCollector = _activeReturnCollector;
    YieldContext? prevYield = _activeYieldContext;
    int prevLoopDepth = _loopDepth;
    int prevFactBase = _inequalityFactBase;
    _currentCallable = null;
    _currentStruct = null;
    _isStaticMethod = false;
    _activeReturnCollector = null;
    _activeYieldContext = null;
    _loopDepth = 0;
    _inequalityFactBase = _inequalityFacts.Count;
    try {
      using ScopeRestore _ = EnterScope(testScope);
      CheckBlock(test.Body);
    } finally {
      _inequalityFactBase = prevFactBase;
      _loopDepth = prevLoopDepth;
      _activeYieldContext = prevYield;
      _activeReturnCollector = prevCollector;
      _isStaticMethod = prevStatic;
      _currentStruct = prevStruct;
      _currentCallable = prevCallable;
    }
  }

  private void CheckAssert(AssertStatementSyntax assertStatement) {
    CheckCondition(assertStatement.Condition, "'assert' condition");
    if (assertStatement.Message is { } message) {
      Check(message, _types.String, "assert message");
    }
  }

  private void CheckIfStatement(IfStatementSyntax ifStatement) {
    CheckCondition(ifStatement.Condition, "'if' condition", out List<LocalSymbol> trueBindings);

    Scope ifScope = ScopeOf(ifStatement, ScopeKind.Block);
    foreach (LocalSymbol binding in trueBindings) {
      if (!ifScope.TryDeclare(binding, out Symbol? existing)) {
        CheckerSession.ReportDuplicate(_state, binding.Name, binding.NameSpan, existing!);
      }
    }

    using (EnterScope(ifScope))
    using (EnterBranchInequalityScope(ifStatement.Condition)) {
      CheckBlock(ifStatement.ThenBranch);
    }

    if (ifStatement.ElseBranch is { } elseBranch) {
      CheckItem(elseBranch);
    }
  }

  private void CheckVariableDeclaration(VariableDeclarationSyntax declaration) {
    bool isValKeyword = declaration.KeywordToken.Kind == TokenKind.ValKeyword;
    bool isValMut = isValKeyword && declaration.MutKeyword is not null;
    bool isFrozenVal = isValKeyword && !isValMut;
    bool isMutable = !isFrozenVal;
    bool isReassignable = !isValKeyword;
    bool nameMissing = IsMissing(declaration.NameToken);
    string name = nameMissing ? "<unknown>" : NameOf(declaration.NameToken);

    SymbolVisibility visibility = SymbolVisibility.Private;
    if (declaration.Visibility is { } vis) {
      if (_scope.Kind != ScopeKind.Module) {
        Report(
            DiagnosticCodes.SymbolNotPublic,
            vis.Span,
            $"Visibility modifier '{(vis.IsPub ? "pub" : "@@Testable")}' is only allowed on top-level declarations.");
      } else if (!isFrozenVal) {
        string bindingKind = isValMut ? "val mut" : "var";
        if (vis.IsPub) {
          Report(
              DiagnosticCodes.SymbolNotPublic,
              vis.Span,
              $"Top-level '{bindingKind}' cannot be marked 'pub'; use 'pub val' or expose it through a 'pub fn'.",
              help: $"use 'pub val' for an exported constant, or keep '{bindingKind}' private and expose a 'pub fn'");
        } else {
          Report(
              DiagnosticCodes.SymbolNotPublic,
              vis.Span,
              $"Top-level '{bindingKind}' cannot be marked '@@Testable'; use '@@Testable val' or a '@@Testable fn'.");
        }
      } else {
        visibility = vis.IsPub ? SymbolVisibility.Public : SymbolVisibility.Testable;
      }
    }

    TypeSymbol? declared = declaration.Type is { } annotation
        ? _session.ResolveType(_state, annotation, _scope, TypePosition.VariableAnnotation)
        : null;

    TypeSymbol bindingType;
    if (isFrozenVal && IsOptionNone(declaration.Initializer)) {
      // D-160 / VAR-E7: a `val` can never hold the empty `Option`.
      Report(
          DiagnosticCodes.ValInitializedWithOptionNone,
          declaration.Initializer.Span,
          $"Cannot initialize immutable 'val' '{name}' with 'Option::None' because it can never hold a value.",
          help: $"use 'var {name} Option[T] = Option::None' for a variable that starts empty");
      if (declared is UnionType declaredOpt && ReferenceEquals(declaredOpt.Definition, _prelude.OptionUnion)) {
        CheckWithContextualExpectation(declaration.Initializer, declaredOpt);
      } else {
        // Avoid emitting a second cascading PN3110 after VAR-E7 when no annotation is present.
        if (UnwrapParens(declaration.Initializer) is StaticAccessExpressionSyntax noneAccess) {
          ResolveStaticTargetSymbol(noneAccess.Target, out _);
          _state.SetReferencedSymbol(noneAccess, _prelude.OptionNoneVariant);
        }
        SetWrappedExpressionType(declaration.Initializer, _types.Error);
      }
      bindingType = declared ?? _types.Error;
    } else if (declared is not null && !declared.IsError) {
      // D-012 / OQ-9: the declared type wins, `int` widens to `float` (D-172), nothing narrows.
      Check(declaration.Initializer, declared, $"variable '{name}'");
      bindingType = declared;
    } else if (declared is not null) {
      Synthesize(declaration.Initializer);
      bindingType = _types.Error;
    } else {
      TypeSymbol initializerType = Synthesize(declaration.Initializer);
      if (initializerType.IsVoid) {
        Report(
            DiagnosticCodes.InvalidVoidTypePosition,
            declaration.Initializer.Span,
            $"Cannot initialize '{name}' with a 'void' value; 'void' is not a type of any variable.");
        bindingType = _types.Error;
      } else {
        bindingType = initializerType.IsNever ? _types.Error : initializerType;
      }
    }

    if (isValMut && !HasMutableInterior(bindingType)) {
      // D-183 / VAR-E13: `val mut` requires a type with mutable contents.
      Token mutTok = declaration.MutKeyword!.Value;
      SourceSpan valMutSpan = SourceSpan.FromBounds(
          Math.Min(declaration.KeywordToken.Span.Start, mutTok.Span.Start),
          Math.Max(declaration.KeywordToken.Span.End, mutTok.Span.End));
      Report(
          DiagnosticCodes.ValMutRequiresMutableInterior,
          valMutSpan,
          $"Type '{bindingType.DisplayName}' has nothing to mutate; 'val mut' requires a type with mutable contents.",
          help: "use 'val' if it never changes, or 'var' to reassign it");
    }

    if (nameMissing) {
      return;
    }

    // The initializer is resolved before the new binding enters scope (`val x = x + 1` reads the
    // outer `x`, D-018 / VAR-5), and the binding is declared in textual order (OQ-16 Option 2).
    var symbol = new LocalSymbol(
        name,
        bindingType,
        isMutable,
        LocalKind.Variable,
        declaration.Id,
        declaration.NameToken.Span,
        _state.FilePath,
        _scope.Owner,
        visibility,
        isReassignable: isReassignable);

    if (isFrozenVal && TryEvalConstant(declaration.Initializer) is { } folded) {
      if (folded.Kind == ConstantValueKind.Int && ReferenceEquals(bindingType, _types.Float)) {
        folded = ConstantValue.FromFloat(folded.IntValue);
      }
      symbol.ConstantValue = folded;
      if ((uint) declaration.Id < (uint) _state.ConstantValues.Length) {
        _state.ConstantValues[declaration.Id] = folded;
      }
    }

    if (!_scope.TryDeclare(symbol, out Symbol? existing)) {
      CheckerSession.ReportDuplicate(_state, name, declaration.NameToken.Span, existing!);
    }

    if (_scope.Kind == ScopeKind.Module && _inPhase3a == 0) {
      _state.AddTopLevelValue(symbol);
    }

    _state.SetDeclaredSymbol(declaration, symbol);
    _state.SetNodeType(declaration, bindingType);
  }

  private void CheckFor(ForStatementSyntax forStatement) {
    Scope loopScope = ScopeOf(forStatement, ScopeKind.Loop);

    switch (forStatement.Header) {
      case null:
        _state.LoopInfos[forStatement.Id] = new LoopInfo(LoopKind.Infinite);
        break;

      case ForConditionHeaderSyntax condition:
        CheckForCondition(forStatement, condition, loopScope);
        break;

      case ForInHeaderSyntax forIn:
        CheckForIn(forStatement, forIn, loopScope);
        break;
    }

    _loopDepth++;
    try {
      using ScopeRestore _ = EnterScope(loopScope);
      CheckBlock(forStatement.Body);
    } finally {
      _loopDepth--;
    }
  }

  private void CheckForCondition(ForStatementSyntax forStatement, ForConditionHeaderSyntax header, Scope loopScope) {
    var condBindings = new List<LocalSymbol>();
    TypeSymbol conditionType = CheckConditionWithBindings(header.Condition, condBindings);
    if (conditionType.IsError || conditionType.IsNever || conditionType is PendingReturnType) {
      return;
    }

    if (ReferenceEquals(conditionType, _types.Bool)) {
      // LOOP-13 / D-003 (OQ-25): `for cond { ... }` is a condition loop.
      foreach (LocalSymbol binding in condBindings) {
        if (!loopScope.TryDeclare(binding, out Symbol? existing)) {
          CheckerSession.ReportDuplicate(_state, binding.Name, binding.NameSpan, existing!);
        }
      }
      CheckConstantFalseLoop(header);
      _state.LoopInfos[forStatement.Id] = new LoopInfo(LoopKind.ConditionLoop);
      return;
    }

    if (ReferenceEquals(conditionType, _types.Int)) {
      // LOOP-3 / LOOP-17 / D-002 (OQ-17): `it` is declared in the loop body scope.
      LocalSymbol it = new(
          "it",
          _types.Int,
          isMutable: false,
          LocalKind.ImplicitIt,
          header.Id,
          header.Condition.Span,
          _state.FilePath,
          loopScope.Owner);
      loopScope.TryDeclare(it, out _);
      _state.LoopInfos[forStatement.Id] = new LoopInfo(LoopKind.CountLoop, it);
      return;
    }

    Report(
        DiagnosticCodes.InvalidConditionType,
        header.Condition.Span,
        $"'for' condition must have type 'bool' or 'int', but got '{conditionType.DisplayName}'.");
  }

  private void CheckForIn(ForStatementSyntax forStatement, ForInHeaderSyntax header, Scope loopScope) {
    TypeSymbol collectionType = Synthesize(header.Collection);
    bool hasSecond = header.SecondBindingToken is { } second && !IsMissing(second);
    bool hasFirst = !IsMissing(header.FirstBindingToken);

    TypeSymbol firstType = _types.Error;
    TypeSymbol secondType = _types.Error;
    LoopKind? kind = null;

    switch (collectionType) {
      case var t when t.IsError || t.IsNever || t is PendingReturnType:
        break;

      case PrimitiveType { Kind: PrimitiveKind.Int }:
        // LOOP-4 / LOOP-8: both bindings, when written, are the counter.
        kind = LoopKind.ForInRange;
        firstType = _types.Int;
        secondType = _types.Int;
        break;

      case VectorType vector:
        // LOOP-5 / LOOP-7: `x` is the element, `i, x` is (index, element).
        kind = LoopKind.ForInVector;
        firstType = hasSecond ? _types.Int : vector.ElementType;
        secondType = hasSecond ? vector.ElementType : _types.Error;
        break;

      case PrimitiveType { Kind: PrimitiveKind.String }:
        // LOOP-6 / LOOP-7: `x` is a rune, `i, x` is (index, rune).
        kind = LoopKind.ForInString;
        firstType = hasSecond ? _types.Int : _types.Rune;
        secondType = hasSecond ? _types.Rune : _types.Error;
        break;

      case MapType map:
        // LOOP-9 / LOOP-10 / D-120 / D-004: `v` is the value, `k, v` is (key, value).
        kind = LoopKind.ForInMap;
        firstType = hasSecond ? map.KeyType : map.ValueType;
        secondType = hasSecond ? map.ValueType : _types.Error;
        break;

      case SetType set:
        // LOOP-18: `x` is the element; the two-binding form over a set is not specified.
        kind = LoopKind.ForInSet;
        firstType = set.ElementType;
        break;

      case TypeParameterType or InterfaceType:
        // Constrained iteration over generic values is ticket 039.
        break;

      default:
        Report(
            DiagnosticCodes.TypeMismatch,
            header.Collection.Span,
            $"Cannot iterate over a value of type '{collectionType.DisplayName}'.");
        break;
    }

    LocalSymbol? first = null;
    LocalSymbol? secondSymbol = null;
    if (hasFirst) {
      first = DeclareLoopVariable(loopScope, header, header.FirstBindingToken, firstType);
    }

    if (hasSecond) {
      secondSymbol = DeclareLoopVariable(loopScope, header, header.SecondBindingToken!.Value, secondType);
    }

    if (kind is { } loopKind) {
      _state.LoopInfos[forStatement.Id] = new LoopInfo(loopKind, FirstBinding: first, SecondBinding: secondSymbol);
    }
  }

  private LocalSymbol DeclareLoopVariable(Scope loopScope, ForInHeaderSyntax header, Token nameToken, TypeSymbol type) {
    string name = NameOf(nameToken);
    var symbol = new LocalSymbol(
        name,
        type,
        isMutable: false,
        LocalKind.LoopVariable,
        header.Id,
        nameToken.Span,
        _state.FilePath,
        loopScope.Owner);
    if (!loopScope.TryDeclare(symbol, out Symbol? existing)) {
      CheckerSession.ReportDuplicate(_state, name, nameToken.Span, existing!);
    }

    return symbol;
  }

  // ---------------------------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------------------------

  private Scope ScopeOf(SyntaxNode node, ScopeKind fallbackKind) {
    if ((uint) node.Id < (uint) _state.ScopesByBlockId.Length && _state.ScopesByBlockId[node.Id] is { } existing) {
      return existing;
    }

    var created = new Scope(fallbackKind, _scope, _scope.Owner);
    _state.SetScope(node, created);
    return created;
  }

  private ScopeRestore EnterScope(Scope scope) {
    var restore = new ScopeRestore(this, _scope);
    _scope = scope;
    return restore;
  }

  private readonly struct ScopeRestore(BodyChecker owner, Scope previous) : IDisposable {
    public void Dispose() => owner._scope = previous;
  }

  private void Report(
      string code,
      SourceSpan span,
      string message,
      ImmutableArray<RelatedLocation> related = default,
      string? help = null) =>
      _state.Diagnostics.ReportError(code, span, message, related, help: help);

  private string NameOf(Token token) => _state.Lex.GetText(token).ToString();

  private static bool IsMissing(Token token) => CheckerSession.IsMissingToken(token);

  /// <summary><see cref="ErrorType"/> and <see cref="NeverType"/> never produce cascading errors.</summary>
  private static bool IsSilent(TypeSymbol type) => type.IsError || type.IsNever;

  private bool IsOptionNone(ExpressionSyntax expression) {
    while (expression is ParenthesizedExpressionSyntax paren) {
      expression = paren.Expression;
    }

    return expression is StaticAccessExpressionSyntax { Target: IdentifierExpressionSyntax target } access
        && !IsMissing(access.MemberToken)
        && !IsMissing(target.IdentifierToken)
        && string.Equals(NameOf(access.MemberToken), "None", StringComparison.Ordinal)
        && _scope.TryLookup(NameOf(target.IdentifierToken), out Symbol? symbol)
        && ReferenceEquals(symbol, _prelude.OptionUnion);
  }

  private static bool HasMissingHeaderParts(
      Token? openParen,
      Token? closeParen,
      ImmutableArray<ParameterSyntax> parameters) {
    if (openParen is not { } open || IsMissing(open) || closeParen is not { } close || IsMissing(close)) {
      return true;
    }

    foreach (ParameterSyntax parameter in parameters) {
      if (IsMissing(parameter.NameToken)) {
        return true;
      }
    }

    return false;
  }

  private void FillMissingExpressionTypes(CompilationUnitSyntax root) {
    var stack = new Stack<SyntaxNode>();
    stack.Push(root);
    while (stack.Count > 0) {
      SyntaxNode current = stack.Pop();
      if (current is ExpressionSyntax expr
          && (uint) expr.Id < (uint) _state.NodeTypes.Length
          && _state.NodeTypes[expr.Id] is null) {
        _state.SetNodeType(expr, _types.Error);
      }

      foreach (SyntaxElement elem in current.GetChildren()) {
        if (elem.IsNode) {
          stack.Push(elem.AsNode());
        }
      }
    }
  }
}
