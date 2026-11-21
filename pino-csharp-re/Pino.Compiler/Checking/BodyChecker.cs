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
/// two-phase SCC return-type inference, contextual lambdas, implicit <c>it</c>, and AD-002 closure
/// capture lists). <c>@@Generic</c> unification and <c>?</c>/<c>or</c> land in ticket 039;
/// patterns and flow in 040.
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
    InitializeCallableRegistry();
  }

  /// <summary>Checks every top-level item of the module in textual order (OQ-16 Option 2).</summary>
  public void Run() {
    CompilationUnitSyntax root = _state.Tree.Root;

    // Flow-sensitive `is` bindings (ticket 040) are not modelled yet: when top-level statements
    // contain an `is` pattern, unresolved names are not reported rather than guessed at.
    foreach (SyntaxNode item in root.Items) {
      if (item is not (FunctionDeclarationSyntax or StructDeclarationSyntax or InterfaceDeclarationSyntax
          or EnumDeclarationSyntax or UnionDeclarationSyntax or TestDeclarationSyntax)
          && ContainsIsPattern(item)) {
        _tolerantNames = 1;
        break;
      }
    }

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
        Synthesize(yieldStatement.Expression);
        break;

      case MatchStatementSyntax matchStatement:
        // Arms bind pattern variables (ticket 040); only the subject is typed here.
        Synthesize(matchStatement.Subject);
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
    int tolerant = ContainsIsPattern(test.Body) ? 1 : 0;
    _tolerantNames += tolerant;
    Symbol? prevCallable = _currentCallable;
    StructSymbol? prevStruct = _currentStruct;
    bool prevStatic = _isStaticMethod;
    ReturnCollector? prevCollector = _activeReturnCollector;
    _currentCallable = null;
    _currentStruct = null;
    _isStaticMethod = false;
    _activeReturnCollector = null;
    try {
      using ScopeRestore _ = EnterScope(testScope);
      CheckBlock(test.Body);
    } finally {
      _activeReturnCollector = prevCollector;
      _isStaticMethod = prevStatic;
      _currentStruct = prevStruct;
      _currentCallable = prevCallable;
      _tolerantNames -= tolerant;
    }
  }

  private void CheckAssert(AssertStatementSyntax assertStatement) {
    CheckCondition(assertStatement.Condition, "'assert' condition");
    if (assertStatement.Message is { } message) {
      Check(message, _types.String, "assert message");
    }
  }

  private void CheckIfStatement(IfStatementSyntax ifStatement) {
    CheckCondition(ifStatement.Condition, "'if' condition");

    int tolerant = ContainsIsPattern(ifStatement.Condition) ? 1 : 0;
    _tolerantNames += tolerant;
    try {
      Scope ifScope = ScopeOf(ifStatement, ScopeKind.Block);
      using (EnterScope(ifScope)) {
        CheckBlock(ifStatement.ThenBranch);
      }

      if (ifStatement.ElseBranch is { } elseBranch) {
        CheckItem(elseBranch);
      }
    } finally {
      _tolerantNames -= tolerant;
    }
  }

  private void CheckVariableDeclaration(VariableDeclarationSyntax declaration) {
    bool isVal = declaration.KeywordToken.Kind == TokenKind.ValKeyword;
    bool nameMissing = IsMissing(declaration.NameToken);
    string name = nameMissing ? "<unknown>" : NameOf(declaration.NameToken);

    TypeSymbol? declared = declaration.Type is { } annotation
        ? _session.ResolveType(_state, annotation, _scope, TypePosition.VariableAnnotation)
        : null;

    TypeSymbol bindingType;
    if (isVal && IsOptionNone(declaration.Initializer)) {
      // D-160 / VAR-E7: a `val` can never hold the empty `Option`.
      Report(
          DiagnosticCodes.ValInitializedWithOptionNone,
          declaration.Initializer.Span,
          $"Cannot initialize immutable 'val' '{name}' with 'Option::None' because it can never hold a value.",
          help: $"use 'var {name} Option[T] = Option::None' for a variable that starts empty");
      Synthesize(declaration.Initializer);
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

    if (nameMissing) {
      return;
    }

    // The initializer is resolved before the new binding enters scope (`val x = x + 1` reads the
    // outer `x`, D-018 / VAR-5), and the binding is declared in textual order (OQ-16 Option 2).
    var symbol = new LocalSymbol(
        name,
        bindingType,
        !isVal,
        LocalKind.Variable,
        declaration.Id,
        declaration.NameToken.Span,
        _state.FilePath,
        _scope.Owner);

    if (!_scope.TryDeclare(symbol, out Symbol? existing)) {
      CheckerSession.ReportDuplicate(_state, name, declaration.NameToken.Span, existing!);
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

    using ScopeRestore _ = EnterScope(loopScope);
    CheckBlock(forStatement.Body);
  }

  private void CheckForCondition(ForStatementSyntax forStatement, ForConditionHeaderSyntax header, Scope loopScope) {
    TypeSymbol conditionType = Synthesize(header.Condition);
    if (conditionType.IsError || conditionType.IsNever || conditionType is PendingReturnType) {
      return;
    }

    if (ReferenceEquals(conditionType, _types.Bool)) {
      // LOOP-13 / D-003 (OQ-25): `for cond { ... }` is a condition loop.
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
        isMutable: true,
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

  private static bool ContainsIsPattern(SyntaxNode root) {
    var stack = new Stack<SyntaxNode>();
    stack.Push(root);
    while (stack.Count > 0) {
      SyntaxNode node = stack.Pop();
      if (node is IsPatternExpressionSyntax) {
        return true;
      }

      foreach (SyntaxElement element in node.GetChildren()) {
        if (element.IsNode) {
          stack.Push(element.AsNode());
        }
      }
    }

    return false;
  }
}
