using System.Diagnostics;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Parsing;

public sealed class ParserRecoveryTests {
  [Theory]
  [InlineData("obj:", "(MemberAccessExpression (IdentifierExpression obj) : <missing Identifier>)", DiagnosticCodes.ExpectedIdentifier)]
  [InlineData("Mod::", "(StaticAccessExpression (IdentifierExpression Mod) :: <missing Identifier>)", DiagnosticCodes.ExpectedIdentifier)]
  [InlineData("foo(a,", "(CallExpression (IdentifierExpression foo) (Argument (IdentifierExpression a)) <missing CloseParen>)", DiagnosticCodes.ExpectedToken)]
  [InlineData("Point { x: 1,", "(StructLiteralExpression (NamedType Point) (StructFieldInit x : (LiteralExpression 1)) <missing CloseBrace>)", DiagnosticCodes.ExpectedToken)]
  [InlineData("[1, 2", "(VectorLiteralExpression (LiteralExpression 1) (LiteralExpression 2) <missing CloseBracket>)", DiagnosticCodes.ExpectedToken)]
  [InlineData("match x { when", "(MatchExpression (IdentifierExpression x) (WhenArm (MissingPattern)) <missing CloseBrace>)", DiagnosticCodes.ExpectedPattern)]
  public void DesignA2_ExpressionRows_ProduceStructuredMissingNodes(
      string source,
      string expectedSExpr,
      string expectedCode) {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(source, expectNoDiagnostics: false);
    Assert.Equal(expectedSExpr, SyntaxPrinter.Print(result.Node, result.Lex.Source));
    Diagnostic diag = Assert.Single(result.Diagnostics, d => d.Code == expectedCode);
    Assert.InRange(diag.Span.Start, 0, result.Lex.Source.Length);
  }

  [Fact]
  public void FooWithTrailingComma_StopsBeforeNextLineStatement() {
    const string Source = "foo(a,\nval y = 1";
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(Source, expectEOF: false, expectNoDiagnostics: false);
    Assert.Equal(4, result.EndTokenIndex); // foo, (, a, , — stops before `val`
    Assert.Equal(
        "(CallExpression (IdentifierExpression foo) (Argument (IdentifierExpression a)) <missing CloseParen>)",
        SyntaxPrinter.Print(result.Node, result.Lex.Source));
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedToken, diag.Code);
    Assert.Equal("val", result.Lex.Source.AsSpan(diag.Span).ToString());
    RelatedLocation rel = Assert.Single(diag.Related);
    Assert.Equal("opened here", rel.Message);
    Assert.Equal("(", result.Lex.Source.AsSpan(rel.Span).ToString());
  }

  [Theory]
  [InlineData("(1 + 2", "(")]
  [InlineData("[1, 2", "[")]
  [InlineData("Point { x: 1", "{")]
  public void UnclosedDelimiter_AttachesOpenedHereRelatedLocation(string source, string openerText) {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(source, expectNoDiagnostics: false);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedToken, diag.Code);
    Assert.Equal(source.Length, diag.Span.Start);
    Assert.Equal(0, diag.Span.Length);
    RelatedLocation rel = Assert.Single(diag.Related);
    Assert.Equal("opened here", rel.Message);
    Assert.Equal(openerText, result.Lex.Source.AsSpan(rel.Span).ToString());
  }

  [Fact]
  public void GarbageTokensInBlock_RecoverWithoutCrash() {
    const string Source = "{\n  ) ]\n  yield 1\n}";
    ParseResult<BlockStatementSyntax> result = ParserTestHelpers.ParseBlock(Source, expectNoDiagnostics: false);
    Assert.Contains(result.Diagnostics, static d => d.Code == DiagnosticCodes.ExpectedStatement);
    Assert.Contains(result.Node.Items, static item => item is YieldStatementSyntax);
  }

  [Fact]
  public void LexerErrorTokenInExpression_ProducesNoParserDiagnostic() {
    // `$` outside a string produces TokenKind.Error (PN1001) from the lexer.
    ParseResult<ExpressionSyntax> exprWithError = ParserTestHelpers.ParseExpression("a + $", expectNoDiagnostics: false);
    Assert.Single(exprWithError.Lex.Diagnostics);
    Assert.Empty(exprWithError.Diagnostics);
    Assert.Equal(
        "(BinaryExpression (IdentifierExpression a) + (ErrorExpression $))",
        SyntaxPrinter.Print(exprWithError.Node, exprWithError.Lex.Source));

    // `$if` inside a string produces TokenKind.Error (PN1017) from the lexer.
    ParseResult<ExpressionSyntax> stringWithError = ParserTestHelpers.ParseExpression("\"$if\"", expectNoDiagnostics: false);
    Assert.Single(stringWithError.Lex.Diagnostics);
    Assert.Empty(stringWithError.Diagnostics);
    Assert.Equal(
        "(InterpolatedStringExpression (StringInterpolationPart (ErrorExpression if)))",
        SyntaxPrinter.Print(stringWithError.Node, stringWithError.Lex.Source));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void TenThousandNestedParens_EmitsOnePN2020InUnderOneSecond(bool balanced) {
    const int Depth = 10_000;
    string source = balanced
        ? new string('(', Depth) + "1" + new string(')', Depth)
        : new string('(', Depth) + "1";

    LexResult lex = Lexer.Lex(new SourceText(source));
    var sw = Stopwatch.StartNew();
    ParseResult<ExpressionSyntax> result = Parser.ParseStandaloneExpression(lex);
    sw.Stop();

    ParserTestHelpers.AssertInvariants(result, expectEOF: true, expectNoDiagnostics: false);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.MaxNestingDepthExceeded, diag.Code);
    Assert.Equal(512, diag.Span.Start);
    Assert.Equal(1, diag.Span.Length);
    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"Took {sw.Elapsed.TotalMilliseconds:F1} ms.");
  }

  [Theory]
  [InlineData("val x =", "(CompilationUnit (VariableDeclaration val x = (MissingExpression)))", DiagnosticCodes.ExpectedExpression)]
  [InlineData("if x {", "(CompilationUnit (IfStatement (IdentifierExpression x) (BlockStatement <missing CloseBrace>)))", DiagnosticCodes.ExpectedToken)]
  [InlineData("match x { when", "(CompilationUnit (MatchStatement (IdentifierExpression x) (WhenArm (MissingPattern)) <missing CloseBrace>))", DiagnosticCodes.ExpectedPattern)]
  [InlineData("fn foo(", "(CompilationUnit (FunctionDeclaration foo <missing CloseParen> (BlockStatement <missing OpenBrace> <missing CloseBrace>)))", DiagnosticCodes.ExpectedToken)]
  public void DesignA2_StatementAndDeclarationRows_ProduceStructuredMissingNodes(
      string source,
      string expectedSExpr,
      string expectedCode) {
    ParseResult result = ParserTestHelpers.ParseFile(source, expectNoDiagnostics: false);
    Assert.Equal(expectedSExpr, SyntaxPrinter.Print(result.Root, result.Lex.Source));
    Assert.Contains(result.Diagnostics, d => d.Code == expectedCode);
  }

  [Fact]
  public void A1_MissingBraceBeforeColumn1Declaration_ClosesBlockAndParsesNextDeclaration() {
    const string Source = """
      fn foo() {
        val x = 1
      fn bar() {
        val y = 2
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source, expectNoDiagnostics: false);
    Assert.Equal(2, result.Root.Items.Length);
    FunctionDeclarationSyntax foo = Assert.IsType<FunctionDeclarationSyntax>(result.Root.Items[0]);
    FunctionDeclarationSyntax bar = Assert.IsType<FunctionDeclarationSyntax>(result.Root.Items[1]);
    Assert.Equal("foo", result.Lex.GetText(foo.NameToken).ToString());
    Assert.Equal("bar", result.Lex.GetText(bar.NameToken).ToString());
    Assert.True((foo.Body.CloseBraceToken.Flags & TokenFlags.Missing) != 0);
    Assert.True((bar.Body.CloseBraceToken.Flags & TokenFlags.Missing) == 0);

    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedToken, diag.Code);
    Assert.Equal("expected '}' before this declaration", diag.Message);
    Assert.Equal(0, diag.Span.Length);
    Assert.Equal(bar.Span.Start, diag.Span.Start);

    RelatedLocation rel = Assert.Single(diag.Related);
    Assert.Equal("this block is never closed", rel.Message);
    Assert.Equal(foo.Body.OpenBraceToken.Span, rel.Span);
  }

  [Fact]
  public void A1_TwoNestedUnclosedBlocks_EmitOnePN2001WithTwoRelatedLocations() {
    const string Source = """
      fn foo() {
        if cond {
          val x = 1
      fn bar() {
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source, expectNoDiagnostics: false);
    Assert.Equal(2, result.Root.Items.Length);
    FunctionDeclarationSyntax foo = Assert.IsType<FunctionDeclarationSyntax>(result.Root.Items[0]);
    FunctionDeclarationSyntax bar = Assert.IsType<FunctionDeclarationSyntax>(result.Root.Items[1]);
    IfStatementSyntax innerIf = Assert.IsType<IfStatementSyntax>(Assert.Single(foo.Body.Items));
    Assert.True((foo.Body.CloseBraceToken.Flags & TokenFlags.Missing) != 0);
    Assert.True((innerIf.ThenBranch.CloseBraceToken.Flags & TokenFlags.Missing) != 0);

    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedToken, diag.Code);
    Assert.Equal("expected '}' before this declaration", diag.Message);
    Assert.Equal(bar.Span.Start, diag.Span.Start);
    Assert.Equal(2, diag.Related.Length);
    Assert.All(diag.Related, static r => Assert.Equal("this block is never closed", r.Message));
    Assert.Equal(foo.Body.OpenBraceToken.Span, diag.Related[0].Span);
    Assert.Equal(innerIf.ThenBranch.OpenBraceToken.Span, diag.Related[1].Span);
  }

  [Fact]
  public void A1_BalancedFileWithColumn1DeclarationInsideBlock_FollowsGrammar() {
    const string Source = """
      fn outer() {
      fn inner() {
        val x = 1
      }
        inner()
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source, expectNoDiagnostics: true);
    FunctionDeclarationSyntax outer = Assert.IsType<FunctionDeclarationSyntax>(Assert.Single(result.Root.Items));
    Assert.Equal(2, outer.Body.Items.Length);
    FunctionDeclarationSyntax inner = Assert.IsType<FunctionDeclarationSyntax>(outer.Body.Items[0]);
    Assert.Equal("inner", result.Lex.GetText(inner.NameToken).ToString());
    Assert.IsType<ExpressionStatementSyntax>(outer.Body.Items[1]);
  }

  [Fact]
  public void A1_Column1LambdaDoesNotTriggerColumn1Recovery() {
    const string Source = """
      fn foo() {
      fn (x int) -> x + 1
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source, expectNoDiagnostics: true);
    FunctionDeclarationSyntax foo = Assert.IsType<FunctionDeclarationSyntax>(Assert.Single(result.Root.Items));
    ExpressionStatementSyntax stmt = Assert.IsType<ExpressionStatementSyntax>(Assert.Single(foo.Body.Items));
    Assert.IsType<LambdaExpressionSyntax>(stmt.Expression);
  }

  [Fact]
  public void A1_Column1LambdaInsideUnclosedFileDoesNotTriggerEarlyClose() {
    const string Source = """
      fn foo() {
      fn (x int) -> x + 1
      fn bar() {
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source, expectNoDiagnostics: false);
    Assert.Equal(2, result.Root.Items.Length);
    FunctionDeclarationSyntax foo = Assert.IsType<FunctionDeclarationSyntax>(result.Root.Items[0]);
    FunctionDeclarationSyntax bar = Assert.IsType<FunctionDeclarationSyntax>(result.Root.Items[1]);
    ExpressionStatementSyntax stmt = Assert.IsType<ExpressionStatementSyntax>(Assert.Single(foo.Body.Items));
    Assert.IsType<LambdaExpressionSyntax>(stmt.Expression);

    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedToken, diag.Code);
    Assert.Equal(bar.Span.Start, diag.Span.Start);
    Assert.Single(diag.Related);
  }

  [Fact]
  public void GarbageBetweenDeclarations_BecomesOneErrorNodeAndOneDiagnostic() {
    const string Source = """
      fn first() {}
      ) ] + *
      fn second() {}
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source, expectNoDiagnostics: false);
    Assert.Equal(3, result.Root.Items.Length);
    Assert.IsType<FunctionDeclarationSyntax>(result.Root.Items[0]);
    ErrorStatementSyntax err = Assert.IsType<ErrorStatementSyntax>(result.Root.Items[1]);
    Assert.Equal(4, err.SkippedTokens.Length);
    Assert.IsType<FunctionDeclarationSyntax>(result.Root.Items[2]);

    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedStatement, diag.Code);
  }
}
