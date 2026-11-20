using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Tests.Parsing;

public sealed class ParserBlockTests {
  [Fact]
  public void ReturnAcrossNewlines_D142() {
    // Shaped like projects/pino-compiler/modules/Parser.pino:264-267
    const string Source = """
      {
        return
          a or
          b
      }
      """;

    ParseResult<BlockStatementSyntax> result = ParserTestHelpers.ParseBlock(Source);
    ReturnStatementSyntax ret = Assert.IsType<ReturnStatementSyntax>(Assert.Single(result.Node.Items));
    Assert.NotNull(ret.Expression);
    Assert.Equal(
        "(BlockStatement (ReturnStatement (BinaryExpression (IdentifierExpression a) or (IdentifierExpression b))))",
        SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Fact]
  public void BareReturnBeforeClosingBrace_D142() {
    ParseResult<BlockStatementSyntax> result = ParserTestHelpers.ParseBlock("{\n  return\n}");
    ReturnStatementSyntax ret = Assert.IsType<ReturnStatementSyntax>(Assert.Single(result.Node.Items));
    Assert.Null(ret.Expression);
    Assert.Equal("(BlockStatement (ReturnStatement))", SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Fact]
  public void ReturnInsideMatchExpressionBlockArm_D143() {
    const string Source = """
      match x {
        when 1 {
          return 42
        }
        else {
          yield 0
        }
      }
      """;

    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(Source);
    Assert.Equal(
        "(MatchExpression (IdentifierExpression x) (WhenArm (LiteralPattern (LiteralExpression 1)) (BlockStatement (ReturnStatement (LiteralExpression 42)))) (ElseArm (BlockStatement (YieldStatement (LiteralExpression 0)))))",
        SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Fact]
  public void UnexpectedKeywordInBlock_EmitsPN2003AndRecovers() {
    const string Source = """
      {
        else
        yield 2
      }
      """;

    ParseResult<BlockStatementSyntax> result = ParserTestHelpers.ParseBlock(Source, expectNoDiagnostics: false);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedStatement, diag.Code);
    Assert.Equal("else", result.Lex.Source.AsSpan(diag.Span).ToString());
    Assert.Equal(2, result.Node.Items.Length);
    Assert.IsType<ErrorStatementSyntax>(result.Node.Items[0]);
    Assert.IsType<YieldStatementSyntax>(result.Node.Items[1]);
  }
}
