using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Tests.Parsing;

public sealed class ParserSpacingTests {
  [Fact]
  public void AsymmetricMinusInVector_D010() {
    // [a -b] -> PN2009, split into two elements `a` and `-b`
    ParseResult<ExpressionSyntax> spaceBeforeOnly = ParserTestHelpers.ParseExpression("[a -b]", expectNoDiagnostics: false);
    Diagnostic d1 = Assert.Single(spaceBeforeOnly.Diagnostics);
    Assert.Equal(DiagnosticCodes.AmbiguousMinusInList, d1.Code);
    Assert.Equal("-", spaceBeforeOnly.Lex.Source.AsSpan(d1.Span).ToString());
    Assert.Contains("ambiguous '-'", d1.Message, StringComparison.Ordinal);
    Assert.Contains("write 'a - b' to subtract or 'a, -b' for two elements", d1.Help ?? string.Empty, StringComparison.Ordinal);
    Assert.Equal(
        "(VectorLiteralExpression (IdentifierExpression a) (UnaryExpression - (IdentifierExpression b)))",
        SyntaxPrinter.Print(spaceBeforeOnly.Node, spaceBeforeOnly.Lex.Source));

    // [a- b] -> PN2009, subtraction `a - b`
    ParseResult<ExpressionSyntax> spaceAfterOnly = ParserTestHelpers.ParseExpression("[a- b]", expectNoDiagnostics: false);
    Diagnostic d2 = Assert.Single(spaceAfterOnly.Diagnostics);
    Assert.Equal(DiagnosticCodes.AmbiguousMinusInList, d2.Code);
    Assert.Equal("-", spaceAfterOnly.Lex.Source.AsSpan(d2.Span).ToString());
    Assert.Equal(
        "(VectorLiteralExpression (BinaryExpression (IdentifierExpression a) - (IdentifierExpression b)))",
        SyntaxPrinter.Print(spaceAfterOnly.Node, spaceAfterOnly.Lex.Source));

    // [a\n-b] -> PN2009, split into two elements `a` and `-b`
    ParseResult<ExpressionSyntax> newlineBefore = ParserTestHelpers.ParseExpression("[a\n-b]", expectNoDiagnostics: false);
    Diagnostic d3 = Assert.Single(newlineBefore.Diagnostics);
    Assert.Equal(DiagnosticCodes.AmbiguousMinusInList, d3.Code);
    Assert.Equal(
        "(VectorLiteralExpression (IdentifierExpression a) (UnaryExpression - (IdentifierExpression b)))",
        SyntaxPrinter.Print(newlineBefore.Node, newlineBefore.Lex.Source));

    // Valid forms: [a - b], [a-b], [a, -b], [-b]
    ParseResult<ExpressionSyntax> spacedSub = ParserTestHelpers.ParseExpression("[a - b]");
    Assert.Equal(
        "(VectorLiteralExpression (BinaryExpression (IdentifierExpression a) - (IdentifierExpression b)))",
        SyntaxPrinter.Print(spacedSub.Node, spacedSub.Lex.Source));

    ParseResult<ExpressionSyntax> tightSub = ParserTestHelpers.ParseExpression("[a-b]");
    Assert.Equal(
        "(VectorLiteralExpression (BinaryExpression (IdentifierExpression a) - (IdentifierExpression b)))",
        SyntaxPrinter.Print(tightSub.Node, tightSub.Lex.Source));

    ParseResult<ExpressionSyntax> commaNeg = ParserTestHelpers.ParseExpression("[a, -b]");
    Assert.Equal(
        "(VectorLiteralExpression (IdentifierExpression a) (UnaryExpression - (IdentifierExpression b)))",
        SyntaxPrinter.Print(commaNeg.Node, commaNeg.Lex.Source));

    ParseResult<ExpressionSyntax> leadingNeg = ParserTestHelpers.ParseExpression("[-b]");
    Assert.Equal(
        "(VectorLiteralExpression (UnaryExpression - (IdentifierExpression b)))",
        SyntaxPrinter.Print(leadingNeg.Node, leadingNeg.Lex.Source));
  }

  [Fact]
  public void AsymmetricMinusInCallArguments_D010() {
    ParseResult<ExpressionSyntax> spaceBeforeOnly = ParserTestHelpers.ParseExpression("f(a -b)", expectNoDiagnostics: false);
    Diagnostic d1 = Assert.Single(spaceBeforeOnly.Diagnostics);
    Assert.Equal(DiagnosticCodes.AmbiguousMinusInList, d1.Code);
    Assert.Equal(
        "(CallExpression (IdentifierExpression f) (Argument (IdentifierExpression a)) (Argument (UnaryExpression - (IdentifierExpression b))))",
        SyntaxPrinter.Print(spaceBeforeOnly.Node, spaceBeforeOnly.Lex.Source));

    ParseResult<ExpressionSyntax> spaceAfterOnly = ParserTestHelpers.ParseExpression("f(a- b)", expectNoDiagnostics: false);
    Diagnostic d2 = Assert.Single(spaceAfterOnly.Diagnostics);
    Assert.Equal(DiagnosticCodes.AmbiguousMinusInList, d2.Code);
    Assert.Equal(
        "(CallExpression (IdentifierExpression f) (Argument (BinaryExpression (IdentifierExpression a) - (IdentifierExpression b))))",
        SyntaxPrinter.Print(spaceAfterOnly.Node, spaceAfterOnly.Lex.Source));

    ParseResult<ExpressionSyntax> newlineBefore = ParserTestHelpers.ParseExpression("f(a\n-b)", expectNoDiagnostics: false);
    Diagnostic d3 = Assert.Single(newlineBefore.Diagnostics);
    Assert.Equal(DiagnosticCodes.AmbiguousMinusInList, d3.Code);
    Assert.Equal(
        "(CallExpression (IdentifierExpression f) (Argument (IdentifierExpression a)) (Argument (UnaryExpression - (IdentifierExpression b))))",
        SyntaxPrinter.Print(newlineBefore.Node, newlineBefore.Lex.Source));

    Assert.Equal(
        "(CallExpression (IdentifierExpression f) (Argument (BinaryExpression (IdentifierExpression a) - (IdentifierExpression b))))",
        SyntaxPrinter.Print(ParserTestHelpers.ParseExpression("f(a - b)").Node, ParserTestHelpers.ParseExpression("f(a - b)").Lex.Source));

    Assert.Equal(
        "(CallExpression (IdentifierExpression f) (Argument (BinaryExpression (IdentifierExpression a) - (IdentifierExpression b))))",
        SyntaxPrinter.Print(ParserTestHelpers.ParseExpression("f(a-b)").Node, ParserTestHelpers.ParseExpression("f(a-b)").Lex.Source));

    Assert.Equal(
        "(CallExpression (IdentifierExpression f) (Argument (IdentifierExpression a)) (Argument (UnaryExpression - (IdentifierExpression b))))",
        SyntaxPrinter.Print(ParserTestHelpers.ParseExpression("f(a, -b)").Node, ParserTestHelpers.ParseExpression("f(a, -b)").Lex.Source));

    Assert.Equal(
        "(CallExpression (IdentifierExpression f) (Argument (UnaryExpression - (IdentifierExpression b))))",
        SyntaxPrinter.Print(ParserTestHelpers.ParseExpression("f(-b)").Node, ParserTestHelpers.ParseExpression("f(-b)").Lex.Source));

    // Outside comma-optional lists, `a -b` is ordinary subtraction with no error.
    ParseResult<ExpressionSyntax> outsideList = ParserTestHelpers.ParseExpression("a -b");
    Assert.Equal(
        "(BinaryExpression (IdentifierExpression a) - (IdentifierExpression b))",
        SyntaxPrinter.Print(outsideList.Node, outsideList.Lex.Source));
  }

  [Fact]
  public void NewlineBeforeOpenBracket_EndsExpression_D058() {
    ParseResult<ExpressionSyntax> split = ParserTestHelpers.ParseExpression("f()\n[1, 2]", expectEOF: false);
    Assert.Equal(3, split.EndTokenIndex); // f, (, ) — stops before [
    Assert.Equal("(CallExpression (IdentifierExpression f))", SyntaxPrinter.Print(split.Node, split.Lex.Source));

    ParseResult<ExpressionSyntax> sameLine = ParserTestHelpers.ParseExpression("f()[0]");
    Assert.Equal(
        "(IndexExpression (CallExpression (IdentifierExpression f)) (LiteralExpression 0))",
        SyntaxPrinter.Print(sameLine.Node, sameLine.Lex.Source));
  }

  [Fact]
  public void NewlineBeforeOpenParen_EndsExpression_D144() {
    ParseResult<ExpressionSyntax> split = ParserTestHelpers.ParseExpression("f\n(x)", expectEOF: false);
    Assert.Equal(1, split.EndTokenIndex); // f — stops before (
    Assert.Equal("(IdentifierExpression f)", SyntaxPrinter.Print(split.Node, split.Lex.Source));

    ParseResult<ExpressionSyntax> sameLine = ParserTestHelpers.ParseExpression("f(x)");
    Assert.Equal(
        "(CallExpression (IdentifierExpression f) (Argument (IdentifierExpression x)))",
        SyntaxPrinter.Print(sameLine.Node, sameLine.Lex.Source));
  }

  [Theory]
  [InlineData("[1, 2,]")]
  [InlineData("f(a,)")]
  [InlineData("map[int, int] { 1 -> 2, }")]
  [InlineData("set[int] { 1, }")]
  [InlineData("Point { x: 1, }")]
  [InlineData("fn (a,) -> a")]
  [InlineData("match x { when 1, -> 2 }")]
  public void TrailingComma_EmitsPN2007(string source) {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(source, expectNoDiagnostics: false);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.TrailingComma, diag.Code);
    Assert.Equal(",", result.Lex.Source.AsSpan(diag.Span).ToString());
  }

  [Theory]
  [InlineData("[, 1]")]
  [InlineData("[1,, 2]")]
  [InlineData("f(, a)")]
  [InlineData("f(a,, b)")]
  [InlineData("Point { , x: 1 }")]
  [InlineData("Point { x: 1,, y: 2 }")]
  public void LeadingAndConsecutiveComma_EmitsPN2008(string source) {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(source, expectNoDiagnostics: false);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.UnexpectedComma, diag.Code);
    Assert.Equal(",", result.Lex.Source.AsSpan(diag.Span).ToString());
  }

  [Theory]
  [InlineData("fn (x) => x + 1")]
  [InlineData("map[int, int] { 1 => 2 }")]
  [InlineData("match x { when 1 => 2 }")]
  [InlineData("match x { else => 2 }")]
  public void FatArrowInExpression_EmitsPN2010AndRecovers(string source) {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(source, expectNoDiagnostics: false);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.DeprecatedFatArrow, diag.Code);
    Assert.Equal("=>", result.Lex.Source.AsSpan(diag.Span).ToString());
    Assert.Equal("replace '=>' with '->'", diag.Help);
  }

  [Fact]
  public void FatArrowInFunctionType_EmitsPN2010AndRecovers() {
    ParseResult<TypeSyntax> result = ParserTestHelpers.ParseType("fn (int) => string", expectNoDiagnostics: false);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.DeprecatedFatArrow, diag.Code);
    Assert.Equal("=>", result.Lex.Source.AsSpan(diag.Span).ToString());
    Assert.Equal("replace '=>' with '->'", diag.Help);
    Assert.Equal("(FunctionType (NamedType int) = > (NamedType string))", SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }
}
