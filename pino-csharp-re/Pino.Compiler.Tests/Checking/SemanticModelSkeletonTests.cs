using Pino.Compiler.Checking;
using Pino.Compiler.Lexing;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Checking;

public sealed class SemanticModelSkeletonTests {
  [Fact]
  public void CompilationCreate_BuildsParentMapAndSupportsSafeOutOfRangeLookups() {
    var source = new SourceText("""
      fn add(a int, b int) int {
        val sum = a + b
        return sum
      }
      """, "math.pino");
    ParseResult parsed = Parser.Parse(source);
    Assert.Empty(parsed.Diagnostics);

    Compilation compilation = Compilation.Create(parsed);
    SemanticModel model = compilation.RootModel;

    Assert.Same(parsed, compilation.RootTree);
    Assert.Same(model, compilation.GetSemanticModel(parsed));
    Assert.Null(compilation.GetSemanticModel(Parser.Parse(new SourceText("val x = 1"))));
    Assert.Same(PreludeSymbols.Instance, compilation.Prelude);

    // Parent map verification
    Assert.Null(model.GetParent(parsed.Root));
    var fnDecl = Assert.IsType<FunctionDeclarationSyntax>(parsed.Root.Items[0]);
    Assert.Same(parsed.Root, model.GetParent(fnDecl));
    Assert.Same(fnDecl, model.GetParent(fnDecl.Body));

    var valDecl = Assert.IsType<VariableDeclarationSyntax>(fnDecl.Body.Items[0]);
    Assert.Same(fnDecl.Body, model.GetParent(valDecl));
    Assert.Same(valDecl, model.GetParent(valDecl.Initializer));

    var retStmt = Assert.IsType<ReturnStatementSyntax>(fnDecl.Body.Items[1]);
    Assert.Same(fnDecl.Body, model.GetParent(retStmt));
    Assert.NotNull(retStmt.Expression);
    Assert.Same(retStmt, model.GetParent(retStmt.Expression!));

    // Root scope links to prelude
    Scope? rootScope = model.GetScope(parsed.Root);
    Assert.NotNull(rootScope);
    Assert.Same(compilation.Prelude.Scope, rootScope!.Parent);

    // Out-of-range node IDs must safely return null instead of throwing
    var outOfRangeLow = new IdentifierExpressionSyntax(-5, SourceSpan.Empty, new Token(TokenKind.Identifier, SourceSpan.Empty));
    var outOfRangeHigh = new IdentifierExpressionSyntax(parsed.NodeCount + 100, SourceSpan.Empty, new Token(TokenKind.Identifier, SourceSpan.Empty));
    var syntheticCall = new CallExpressionSyntax(
        parsed.NodeCount + 50,
        SourceSpan.Empty,
        outOfRangeHigh,
        new Token(TokenKind.OpenParen, SourceSpan.Empty),
        [],
        new Token(TokenKind.CloseParen, SourceSpan.Empty));

    foreach (SyntaxNode badNode in new SyntaxNode[] { outOfRangeLow, outOfRangeHigh }) {
      Assert.Null(model.GetType(badNode));
      Assert.Null(model.GetReferencedSymbol(badNode));
      Assert.Null(model.GetDeclaredSymbol(badNode));
      Assert.Null(model.GetConversion(badNode));
      Assert.Null(model.GetCaptures(badNode));
      Assert.Null(model.GetScope(badNode));
      Assert.Null(model.GetParent(badNode));
      Assert.Null(model.GetConstantValue(badNode));
      Assert.Null(model.GetLoopInfo(badNode));
      Assert.Null(model.GetPatternBindings(badNode));
      Assert.Null(model.GetMutationPlace(badNode));
    }
    Assert.Null(model.GetCallResolution(syntheticCall));

    // AssignedBindings and CaptureList baseline
    var local = new LocalSymbol("x", PrimitiveType.Int, true, LocalKind.Variable, 1, default, "math.pino");
    Assert.False(model.IsAssignedAfterInit(local));
    var assigned = new AssignedBindings([local]);
    Assert.True(assigned.IsAssignedAfterInit(local));
    Assert.True(CaptureList.Empty.IsEmpty);
  }

  [Fact]
  public void CompilationCreate_RespectsCancellationToken() {
    ParseResult parsed = Parser.Parse(new SourceText("val x = 1"));
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    Assert.Throws<OperationCanceledException>(() => Compilation.Create(parsed, cancellationToken: cts.Token));
  }

  [Fact]
  public async Task CompilationCreate_IsReentrantAcrossConcurrentCalls() {
    ParseResult[] trees = Enumerable.Range(0, 8)
        .Select(i => Parser.Parse(new SourceText($"fn f{i}(x int) int {{ return x + {i} }}", $"file{i}.pino")))
        .ToArray();

    Task<Compilation>[] tasks = trees
        .Select(t => Task.Run(() => Compilation.Create(t, NullModuleSource.Instance)))
        .ToArray();

    Compilation[] compilations = await Task.WhenAll(tasks);

    for (int i = 0; i < compilations.Length; i++) {
      Compilation c = compilations[i];
      Assert.Same(trees[i], c.RootTree);
      Assert.Same(PreludeSymbols.Instance, c.Prelude);
      Assert.Equal($"file{i}", c.RootModel.Module.Name);
      var fn = Assert.IsType<FunctionDeclarationSyntax>(trees[i].Root.Items[0]);
      Assert.Same(trees[i].Root, c.RootModel.GetParent(fn));
    }
  }
}
