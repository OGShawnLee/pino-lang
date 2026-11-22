using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Parsing;

public sealed class ParserPoolTests {
  [Theory]
  [InlineData("var p = Pool[Weapon]")]
  [InlineData("var p = Pool[[]int]")]
  [InlineData("var p = Pool[Option[int]]")]
  [InlineData("var p = Pool[map[string, int]]")]
  [InlineData("var p = Pool[fn (int) -> int]")]
  [InlineData("var p Pool[Weapon] = Pool[Weapon]")]
  [InlineData("fn f(p Pool[Weapon], h Handle[Weapon]) Option[Handle[Weapon]] { return Option::Some(h) }")]
  [InlineData("if p == Pool[Weapon] { println(1) }")]
  public void PoolAndHandleForms_ParseCleanWithFullTokenAccounting(string source) {
    ParseResult result = ParserTestHelpers.ParseFile(source, expectNoDiagnostics: true);
    Assert.Empty(result.Diagnostics);
  }

  [Theory]
  [InlineData("var p = Pool[")]
  [InlineData("var p = Pool[Weapon")]
  [InlineData("var h = Handle[")]
  [InlineData("var h = Handle[Weapon")]
  [InlineData("fn f(p Pool[) {}")]
  [InlineData("fn f(p Pool[Weapon) {}")]
  public void HalfTypedPoolAndHandle_RecoverCleanlyWithFullTokenAccounting(string source) {
    ParseResult result = ParserTestHelpers.ParseFile(source, expectNoDiagnostics: false);
    Assert.NotEmpty(result.Diagnostics);
  }

  [Fact]
  public void IndexingVariableNamedPoolOrHandle_IsParseError() {
    ParseResult poolIndex = ParserTestHelpers.ParseFile("val Pool = [1, 2]\nprintln(Pool[0])", expectNoDiagnostics: false);
    Assert.Contains(poolIndex.Diagnostics, d => d.Code == DiagnosticCodes.ExpectedType);

    ParseResult handleIndex = ParserTestHelpers.ParseFile("val Handle = [1, 2]\nprintln(Handle[0])", expectNoDiagnostics: false);
    Assert.Contains(handleIndex.Diagnostics, d => d.Code == DiagnosticCodes.ExpectedType);
  }

  [Fact]
  public void BarePoolOrHandleFollowedByNewlineBracket_DoesNotParseAsPoolForm() {
    ParseResult result = ParserTestHelpers.ParseFile("val x = Pool\n[1, 2]", expectNoDiagnostics: true);
    VariableDeclarationSyntax decl = Assert.IsType<VariableDeclarationSyntax>(result.Root.Items[0]);
    Assert.IsType<IdentifierExpressionSyntax>(decl.Initializer);
    ExpressionStatementSyntax stmt = Assert.IsType<ExpressionStatementSyntax>(result.Root.Items[1]);
    Assert.IsType<VectorLiteralExpressionSyntax>(stmt.Expression);
  }

  [Fact]
  public void EditLocality_HalfTypedPoolExpressionPreservesFollowingDeclaration() {
    const string clean = """
fn first() {
  var p = Pool[Weapon]
}

fn second(x int) int {
  return x + 1
}
""";
    const string halfTyped = """
fn first() {
  var p = Pool[Weapon
}

fn second(x int) int {
  return x + 1
}
""";

    ParseResult orig = ParserTestHelpers.ParseFile(clean, expectNoDiagnostics: true);
    ParseResult edited = ParserTestHelpers.ParseFile(halfTyped, expectNoDiagnostics: false);
    Assert.NotEmpty(edited.Diagnostics);
    Assert.Equal(2, orig.Root.Items.Length);
    Assert.True(edited.Root.Items.Length >= 2);
    Assert.True(
        SyntaxEquivalence.AreEquivalent(
            orig.Root.Items[^1],
            orig.Lex.Source,
            edited.Root.Items[^1],
            edited.Lex.Source,
            out string? diff),
        diff);
  }
}
