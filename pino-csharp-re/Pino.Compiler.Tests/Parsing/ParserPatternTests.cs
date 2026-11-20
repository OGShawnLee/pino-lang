using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Tests.Parsing;

public sealed class ParserPatternTests {
  [Theory]
  [InlineData("x", "(IdentifierPattern x)")]
  [InlineData("42", "(LiteralPattern (LiteralExpression 42))")]
  [InlineData("-1", "(LiteralPattern (UnaryExpression - (LiteralExpression 1)))")]
  [InlineData("-3.5", "(LiteralPattern (UnaryExpression - (LiteralExpression 3.5)))")]
  [InlineData("'a'", "(LiteralPattern (LiteralExpression 'a'))")]
  [InlineData("\"start\"", "(LiteralPattern (InterpolatedStringExpression (StringTextPart start)))")]
  [InlineData("true", "(LiteralPattern (LiteralExpression true))")]
  [InlineData("false", "(LiteralPattern (LiteralExpression false))")]
  [InlineData("Direction::North", "(VariantPattern Direction :: North)")]
  [InlineData("Option::None", "(VariantPattern Option :: None)")]
  [InlineData("Option::Some(v)", "(VariantPattern Option :: Some (IdentifierPattern v))")]
  [InlineData("Pair::Both(a, b)", "(VariantPattern Pair :: Both (IdentifierPattern a) (IdentifierPattern b))")]
  [InlineData("Pair::Both(a b)", "(VariantPattern Pair :: Both (IdentifierPattern a) (IdentifierPattern b))")]
  [InlineData("Option[int]::Some(1)", "(VariantPattern Option (TypeArgumentList (NamedType int)) :: Some (LiteralPattern (LiteralExpression 1)))")]
  [InlineData("Result::Success(Option::Some(v))", "(VariantPattern Result :: Success (VariantPattern Option :: Some (IdentifierPattern v)))")]
  public void AllPatternForms_ParseCorrectly(string source, string expected) {
    ParseResult<PatternSyntax> result = ParserTestHelpers.ParsePattern(source);
    Assert.Equal(expected, SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Fact]
  public void UnexpectedTokenInPattern_EmitsPN2005() {
    ParseResult<PatternSyntax> result = ParserTestHelpers.ParsePattern("+", expectEOF: false, expectNoDiagnostics: false);
    Assert.True(result.Node.IsMissing);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedPattern, diag.Code);
    Assert.Equal("+", result.Lex.Source.AsSpan(diag.Span).ToString());
  }
}
