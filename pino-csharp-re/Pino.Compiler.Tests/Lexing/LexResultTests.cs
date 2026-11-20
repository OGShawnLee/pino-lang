using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Lexing;

public sealed class LexResultTests {
  [Fact]
  public void FindTokenIndex_FindsFirstMiddleAndEOF() {
    LexResult lex = Lexer.Lex(new SourceText("val x = 1 + 2"));

    Assert.Equal(0, lex.FindTokenIndex(lex.Tokens[0]));

    int mid = lex.Tokens.Length / 2;
    Assert.Equal(mid, lex.FindTokenIndex(lex.Tokens[mid]));

    int last = lex.Tokens.Length - 1;
    Assert.Equal(TokenKind.EOF, lex.Tokens[last].Kind);
    Assert.Equal(last, lex.FindTokenIndex(lex.Tokens[last]));
  }

  [Fact]
  public void FindTokenIndex_ReturnsMinusOneForMissingToken() {
    LexResult lex = Lexer.Lex(new SourceText("val x = 1"));
    var missingVal = new Token(TokenKind.ValKeyword, new SourceSpan(0, 0), TokenFlags.Missing);

    Assert.Equal(-1, lex.FindTokenIndex(missingVal));
    Assert.Equal(-1, lex.FindTokenIndex(lex.Tokens[0] with { Flags = lex.Tokens[0].Flags | TokenFlags.Missing }));
  }

  [Fact]
  public void FindTokenIndex_ReturnsMinusOneForNonExistentSpanOrKind() {
    LexResult lex = Lexer.Lex(new SourceText("val x = 1"));

    // Span inside whitespace between tokens
    var bogusSpanToken = new Token(TokenKind.Identifier, new SourceSpan(3, 1));
    Assert.Equal(-1, lex.FindTokenIndex(bogusSpanToken));

    // Existing span, wrong kind
    var wrongKind = new Token(TokenKind.Identifier, lex.Tokens[0].Span);
    Assert.Equal(-1, lex.FindTokenIndex(wrongKind));

    // Past end of file
    var pastEnd = new Token(TokenKind.Identifier, new SourceSpan(100, 1));
    Assert.Equal(-1, lex.FindTokenIndex(pastEnd));
  }
}
