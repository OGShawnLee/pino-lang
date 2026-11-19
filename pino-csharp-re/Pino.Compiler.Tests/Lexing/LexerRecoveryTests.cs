using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Lexing;

public sealed class LexerRecoveryTests {
  private static LexResult Lex(string text) => LexerCoreTests.Lex(text);

  private static void AssertSingleError(LexResult result, string code, SourceSpan span) {
    Diagnostic diagnostic = Assert.Single(result.Diagnostics);
    Assert.Equal(code, diagnostic.Code);
    Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    Assert.Equal(span, diagnostic.Span);
    Assert.NotEmpty(diagnostic.Message);
  }

  [Theory]
  [InlineData("1__0", TokenKind.IntLiteral, 0, 4)]
  [InlineData("1_", TokenKind.IntLiteral, 0, 2)]
  [InlineData("1_.2", TokenKind.FloatLiteral, 0, 2)]
  [InlineData("1._2", TokenKind.FloatLiteral, 2, 2)]
  [InlineData("1.2__3", TokenKind.FloatLiteral, 2, 4)]
  public void InvalidUnderscoresReportDiagnosticAndKeepLiteralToken(string text, TokenKind kind, int diagStart, int diagLength) {
    LexResult result = Lex(text);

    Assert.Equal([kind, TokenKind.EOF], result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(0, text.Length), result.Tokens[0].Span);
    AssertSingleError(result, Lexer.InvalidNumericSeparatorCode, new SourceSpan(diagStart, diagLength));
  }

  [Fact]
  public void TrailingLettersAfterNumberBecomeSingleErrorToken() {
    LexResult result = Lex("123abc + 1");

    Assert.Equal(
        [TokenKind.Error, TokenKind.Plus, TokenKind.IntLiteral, TokenKind.EOF],
        result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(0, 6), result.Tokens[0].Span);
    AssertSingleError(result, Lexer.InvalidNumberSuffixCode, new SourceSpan(0, 6));
  }

  [Fact]
  public void EmptyRuneReportsDiagnostic() {
    LexResult result = Lex("''");

    Assert.Equal([new Token(TokenKind.Error, new SourceSpan(0, 2)), new Token(TokenKind.EOF, new SourceSpan(2, 0))], result.Tokens.AsEnumerable());
    AssertSingleError(result, Lexer.EmptyRuneCode, new SourceSpan(0, 2));
  }

  [Fact]
  public void MultiRuneLiteralReportsDiagnosticAndConsumesWholeLiteral() {
    LexResult result = Lex("'abc' x");

    Assert.Equal(
        [TokenKind.Error, TokenKind.Identifier, TokenKind.EOF],
        result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(0, 5), result.Tokens[0].Span);
    AssertSingleError(result, Lexer.MultiRuneLiteralCode, new SourceSpan(0, 5));
  }

  [Fact]
  public void InvalidEscapeReportsDiagnosticOnEscapeOnly() {
    LexResult result = Lex("'\\q'");

    Assert.Equal([TokenKind.Error, TokenKind.EOF], result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(0, 4), result.Tokens[0].Span);
    AssertSingleError(result, Lexer.InvalidRuneEscapeCode, new SourceSpan(1, 2));
  }

  [Fact]
  public void UnterminatedRuneAtEndOfFile() {
    LexResult result = Lex("'a");

    Assert.Equal([TokenKind.Error, TokenKind.EOF], result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(0, 2), result.Tokens[0].Span);
    AssertSingleError(result, Lexer.UnterminatedRuneCode, new SourceSpan(0, 2));
  }

  [Fact]
  public void UnterminatedRuneAtNewlineRecoversOnNextLine() {
    LexResult result = Lex("'a\nx");

    Assert.Equal(
        [TokenKind.Error, TokenKind.Identifier, TokenKind.EOF],
        result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(0, 2), result.Tokens[0].Span);
    Assert.Equal(new SourceSpan(3, 1), result.Tokens[1].Span);
    Assert.Equal(new Trivia(TriviaKind.Newline, new SourceSpan(2, 1)), Assert.Single(result.Trivia));
    AssertSingleError(result, Lexer.UnterminatedRuneCode, new SourceSpan(0, 2));
  }

  [Theory]
  [InlineData("'", 1)]
  [InlineData("'\\", 2)]
  [InlineData("'\\\nx", 2)]
  public void LoneQuoteAndDanglingEscapeAreUnterminated(string text, int errorLength) {
    LexResult result = Lex(text);

    Assert.Equal(TokenKind.Error, result.Tokens[0].Kind);
    Assert.Equal(new SourceSpan(0, errorLength), result.Tokens[0].Span);
    Assert.Equal(TokenKind.EOF, result.Tokens[^1].Kind);
    AssertSingleError(result, Lexer.UnterminatedRuneCode, new SourceSpan(0, errorLength));
  }

  [Theory]
  [InlineData("~", 1)]
  [InlineData("!", 1)]
  [InlineData(".", 1)]
  [InlineData("^", 1)]
  [InlineData("\\", 1)]
  [InlineData("é", 1)]
  [InlineData("😀", 2)]
  public void UnexpectedCharacterBecomesErrorToken(string text, int length) {
    LexResult result = Lex(text);

    Assert.Equal([TokenKind.Error, TokenKind.EOF], result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(0, length), result.Tokens[0].Span);
    AssertSingleError(result, Lexer.UnexpectedCharacterCode, new SourceSpan(0, length));
  }

  [Fact]
  public void MultipleErrorsAreAllReportedInOnePassInSourceOrder() {
    const string Text = "1__0 ~ '' 'abc' 12ab x";
    LexResult result = Lex(Text);

    Assert.Equal(
        [
            TokenKind.IntLiteral, TokenKind.Error, TokenKind.Error, TokenKind.Error,
                TokenKind.Error, TokenKind.Identifier, TokenKind.EOF,
            ],
        result.Tokens.Select(static t => t.Kind));

    Assert.Equal(
        [
            Lexer.InvalidNumericSeparatorCode,
                Lexer.UnexpectedCharacterCode,
                Lexer.EmptyRuneCode,
                Lexer.MultiRuneLiteralCode,
                Lexer.InvalidNumberSuffixCode,
            ],
        result.Diagnostics.Select(static d => d.Code));
    Assert.Equal(
        [
            new SourceSpan(0, 4), new SourceSpan(5, 1), new SourceSpan(7, 2),
                new SourceSpan(10, 5), new SourceSpan(16, 4),
            ],
        result.Diagnostics.Select(static d => d.Span));
    Assert.Equal(new SourceSpan(Text.Length, 0), result.Tokens[^1].Span);
  }

  [Fact]
  public void ErrorsDoNotPreventLaterValidTokensFromLexing() {
    LexResult result = Lex("~ a = 1\n'' b");

    Assert.Equal(
        [TokenKind.Error, TokenKind.Identifier, TokenKind.Assign, TokenKind.IntLiteral, TokenKind.Error, TokenKind.Identifier, TokenKind.EOF],
        result.Tokens.Select(static t => t.Kind));
    Assert.Equal(2, result.Diagnostics.Length);
  }

  [Fact]
  public void LexNeverThrowsAndAlwaysEndsWithEOFOnRandomInput() {
    const string Alphabet = "ab_$09. '\"\\#\n\r\t~!=<>:-+*/%@{}[]()?,é😀\uD83D\0";
    var random = new Random(12345);

    for (int i = 0; i < 2000; i++) {
      int length = random.Next(0, 24);
      char[] chars = new char[length];
      for (int j = 0; j < length; j++) {
        chars[j] = Alphabet[random.Next(Alphabet.Length)];
      }

      string text = new(chars);
      LexResult result = Lex(text);

      Assert.Equal(TokenKind.EOF, result.Tokens[^1].Kind);
      Assert.Equal(1, result.Tokens.Count(static t => t.Kind == TokenKind.EOF));

      int position = 0;
      foreach (SourceSpan span in result.Tokens.Select(static t => t.Span)
          .Concat(result.Trivia.Select(static t => t.Span))
          .OrderBy(static s => s.Start).ThenBy(static s => s.Length)) {
        Assert.Equal(position, span.Start);
        position = span.End;
      }

      Assert.Equal(text.Length, position);
    }
  }
}
