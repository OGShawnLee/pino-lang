using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Parsing;

public sealed class ParserFileTests {
  [Fact]
  public void ParseSourceTextAndLexResult_MultiDeclarationFile_AccountsForEveryToken() {
    const string Source = """
      module App

      from Helpers import sum

      pub struct Point {
        x int
        y int
        fn norm() int {
          return x + y
        }
      }

      pub fn main() {
        val p = Point { x: 1, y: 2 }
        println(sum(p:norm(), 3))
      }
      """;

    var sourceText = new SourceText(Source);
    ParseResult fromSource = Parser.Parse(sourceText);
    ParserTestHelpers.AssertFileInvariants(fromSource, expectNoDiagnostics: true);

    LexResult lex = Lexer.Lex(sourceText);
    ParseResult fromLex = Parser.Parse(lex);
    ParserTestHelpers.AssertFileInvariants(fromLex, expectNoDiagnostics: true);

    Assert.Equal(4, fromSource.Root.Items.Length);
    Assert.Equal(fromSource.NodeCount, fromLex.NodeCount);
    Assert.Equal(TokenKind.EOF, fromSource.Root.EOFToken.Kind);
  }

  [Fact]
  public void ParseFile_MergesLexerAndParserDiagnosticsSortedBySpan() {
    const string Source = """
      if a b()
      val s = "$if"
      if c d()
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source, expectNoDiagnostics: false);
    Assert.Equal(3, result.Diagnostics.Length);
    Assert.Equal(DiagnosticCodes.UnbracedIfBranch, result.Diagnostics[0].Code);
    Assert.Equal(DiagnosticCodes.KeywordInSimpleInterpolation, result.Diagnostics[1].Code);
    Assert.Equal(DiagnosticCodes.UnbracedIfBranch, result.Diagnostics[2].Code);
    Assert.True(result.Diagnostics[0].Span.Start < result.Diagnostics[1].Span.Start);
    Assert.True(result.Diagnostics[1].Span.Start < result.Diagnostics[2].Span.Start);
  }

  [Theory]
  [InlineData("")]
  [InlineData("# comment only\n")]
  public void ParseFile_EmptyOrCommentOnly_SucceedsWithEOF(string source) {
    ParseResult result = ParserTestHelpers.ParseFile(source);
    Assert.Empty(result.Root.Items);
    Assert.Equal(TokenKind.EOF, result.Root.EOFToken.Kind);
  }
}
