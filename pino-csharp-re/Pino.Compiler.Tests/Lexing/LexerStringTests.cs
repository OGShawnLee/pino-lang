using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Lexing;

public sealed class LexerStringTests {
  private const string Quote = "\"";
  private const string TripleQuote = "\"\"\"";

  internal static LexResult Lex(string text) => LexerCoreTests.Lex(text);

  /// <summary>Renders every non-EOF token as <c>Kind|source text</c>.</summary>
  internal static string[] Pieces(LexResult result) =>
    [.. result.Tokens
      .Where(static t => t.Kind != TokenKind.EOF)
      .Select(t => $"{t.Kind}|{result.GetText(t)}")];

  internal static string[] Pieces(string text) => Pieces(Lex(text));

  /// <summary>Asserts the exact (non-EOF) token kinds/spans, the EOF span and that no diagnostics exist.</summary>
  internal static void AssertTokens(string text, params (TokenKind Kind, int Start, int Length)[] expected) {
    LexResult result = Lex(text);

    Assert.Equal(
      expected.Select(static e => new Token(e.Kind, new SourceSpan(e.Start, e.Length))),
      result.Tokens.Where(static t => t.Kind != TokenKind.EOF));
    Assert.Equal(new Token(TokenKind.EOF, new SourceSpan(text.Length, 0)), result.Tokens[^1]);
    Assert.Empty(result.Diagnostics);
  }

  /// <summary>Tokens and trivia cover every source character exactly once and Start/End tokens balance.</summary>
  internal static void AssertWellFormed(string text, LexResult result) {
    Assert.Equal(TokenKind.EOF, result.Tokens[^1].Kind);
    Assert.Equal(1, result.Tokens.Count(static t => t.Kind == TokenKind.EOF));
    Assert.Equal(new SourceSpan(text.Length, 0), result.Tokens[^1].Span);

    int position = 0;
    foreach (SourceSpan span in result.Tokens.Select(static t => t.Span)
      .Concat(result.Trivia.Select(static t => t.Span))
      .OrderBy(static s => s.Start).ThenBy(static s => s.Length)) {
      Assert.Equal(position, span.Start);
      position = span.End;
    }

    Assert.Equal(text.Length, position);
    Assert.Equal(
      result.Tokens.Count(static t => t.Kind == TokenKind.StringStart),
      result.Tokens.Count(static t => t.Kind == TokenKind.StringEnd));
    Assert.Equal(
      result.Tokens.Count(static t => t.Kind == TokenKind.InterpStart),
      result.Tokens.Count(static t => t.Kind == TokenKind.InterpEnd));
    Assert.All(result.Diagnostics, d => {
      Assert.InRange(d.Span.Start, 0, text.Length);
      Assert.InRange(d.Span.End, 0, text.Length);
    });
  }

  private static void AssertDiagnostics(LexResult result, params (string Code, int Start, int Length)[] expected) {
    Assert.All(result.Diagnostics, static d => {
      Assert.Equal(DiagnosticSeverity.Error, d.Severity);
      Assert.NotEmpty(d.Message);
    });
    Assert.Equal(
      expected.Select(static e => (e.Code, new SourceSpan(e.Start, e.Length))),
      result.Diagnostics.Select(static d => (d.Code, d.Span)));
  }

  // ------------------------------------------------------------------------------------------
  // Plain strings
  // ------------------------------------------------------------------------------------------

  [Fact]
  public void PlainStringEmitsStartPartEnd() {
    const string Text = "\"hello\"";

    Assert.Equal([$"StringStart|{Quote}", "StringPart|hello", $"StringEnd|{Quote}"], Pieces(Text));
    AssertTokens(
      Text,
      (TokenKind.StringStart, 0, 1),
      (TokenKind.StringPart, 1, 5),
      (TokenKind.StringEnd, 6, 1));
  }

  [Fact]
  public void EmptySingleLineStringHasNoPart() {
    AssertTokens("\"\"", (TokenKind.StringStart, 0, 1), (TokenKind.StringEnd, 1, 1));
  }

  [Fact]
  public void EmptyMultilineStringHasNoPart() {
    const string Text = "\"\"\"\"\"\"";

    Assert.Equal([$"StringStart|{TripleQuote}", $"StringEnd|{TripleQuote}"], Pieces(Text));
    AssertTokens(Text, (TokenKind.StringStart, 0, 3), (TokenKind.StringEnd, 3, 3));
  }

  [Fact]
  public void AdjacentEmptyStringsAreSeparateTokens() {
    AssertTokens(
      "\"\" \"\"",
      (TokenKind.StringStart, 0, 1),
      (TokenKind.StringEnd, 1, 1),
      (TokenKind.StringStart, 3, 1),
      (TokenKind.StringEnd, 4, 1));
  }

  [Fact]
  public void EscapedQuoteBackslashAndDollarStayInsideOnePart() {
    const string Text = "\"\\\"\\\\\\$\"";

    Assert.Equal(8, Text.Length);
    AssertTokens(
      Text,
      (TokenKind.StringStart, 0, 1),
      (TokenKind.StringPart, 1, 6),
      (TokenKind.StringEnd, 7, 1));
  }

  [Theory]
  [InlineData("\"a\\nb\"")]
  [InlineData("\"a\\tb\"")]
  [InlineData("\"a\\rb\"")]
  [InlineData("\"a\\\"b\"")]
  [InlineData("\"a\\\\b\"")]
  [InlineData("\"a\\$b\"")]
  public void ValidEscapesProduceNoDiagnostics(string text) {
    LexResult result = Lex(text);

    Assert.Equal(
      [TokenKind.StringStart, TokenKind.StringPart, TokenKind.StringEnd, TokenKind.EOF],
      result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(1, 4), result.Tokens[1].Span);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void EscapedDollarDoesNotStartInterpolation() {
    const string Text = "\"\\$name \\$(x)\"";

    Assert.Equal(
      [TokenKind.StringStart, TokenKind.StringPart, TokenKind.StringEnd],
      Lex(Text).Tokens.Where(static t => t.Kind != TokenKind.EOF).Select(static t => t.Kind));
    Assert.Empty(Lex(Text).Diagnostics);
  }

  [Fact]
  public void LoneDollarsAreLiteralText() {
    const string Text = "\"cost $5 and $ end$\"";
    LexResult result = Lex(Text);

    Assert.Equal(
      [$"StringStart|{Quote}", "StringPart|cost $5 and $ end$", $"StringEnd|{Quote}"],
      Pieces(result));
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void HashInsideStringIsTextNotAComment() {
    LexResult result = Lex("\"a # b\" x");

    Assert.Equal("StringPart|a # b", Pieces(result)[1]);
    Assert.DoesNotContain(result.Trivia, static t => t.Kind == TriviaKind.Comment);
    Assert.Empty(result.Diagnostics);
  }

  // ------------------------------------------------------------------------------------------
  // $ident interpolation
  // ------------------------------------------------------------------------------------------

  [Fact]
  public void SimpleInterpolationEmitsInterpStartIdentifierInterpEnd() {
    const string Text = "\"hello $name!\"";

    Assert.Equal(
      [
        $"StringStart|{Quote}",
        "StringPart|hello ",
        "InterpStart|$",
        "Identifier|name",
        "InterpEnd|",
        "StringPart|!",
        $"StringEnd|{Quote}",
      ],
      Pieces(Text));
    AssertTokens(
      Text,
      (TokenKind.StringStart, 0, 1),
      (TokenKind.StringPart, 1, 6),
      (TokenKind.InterpStart, 7, 1),
      (TokenKind.Identifier, 8, 4),
      (TokenKind.InterpEnd, 12, 0),
      (TokenKind.StringPart, 12, 1),
      (TokenKind.StringEnd, 13, 1));
  }

  [Fact]
  public void AdjacentSimpleInterpolationsHaveNoPartBetween() {
    const string Text = "\"$a$b\"";

    Assert.Equal(
      [
        $"StringStart|{Quote}",
        "InterpStart|$", "Identifier|a", "InterpEnd|",
        "InterpStart|$", "Identifier|b", "InterpEnd|",
        $"StringEnd|{Quote}",
      ],
      Pieces(Text));
    AssertTokens(
      Text,
      (TokenKind.StringStart, 0, 1),
      (TokenKind.InterpStart, 1, 1),
      (TokenKind.Identifier, 2, 1),
      (TokenKind.InterpEnd, 3, 0),
      (TokenKind.InterpStart, 3, 1),
      (TokenKind.Identifier, 4, 1),
      (TokenKind.InterpEnd, 5, 0),
      (TokenKind.StringEnd, 5, 1));
  }

  [Fact]
  public void SimpleInterpolationStopsAtNonIdentifierCharacters() {
    Assert.Equal(
      [
        $"StringStart|{Quote}",
        "InterpStart|$", "Identifier|a1_b", "InterpEnd|",
        "StringPart|-c.d",
        $"StringEnd|{Quote}",
      ],
      Pieces("\"$a1_b-c.d\""));
  }

  [Fact]
  public void SimpleInterpolationNeverProducesKeywordTokens() {
    Assert.Equal(
      [
        $"StringStart|{Quote}", "InterpStart|$", "Identifier|if", "InterpEnd|", $"StringEnd|{Quote}",
      ],
      Pieces("\"$if\""));
  }

  [Fact]
  public void InterpolationInsideCodeContextIsOrdinaryDollarIdentifier() {
    Assert.Equal([TokenKind.Identifier, TokenKind.EOF], LexerCoreTests.Kinds("$x"));
  }

  // ------------------------------------------------------------------------------------------
  // $(expr) interpolation
  // ------------------------------------------------------------------------------------------

  [Fact]
  public void MemberAccessInterpolation() {
    const string Text = "\"Base Attack: $(player:attack)\"";

    Assert.Equal(
      [
        $"StringStart|{Quote}",
        "StringPart|Base Attack: ",
        "InterpStart|$(",
        "Identifier|player",
        "Colon|:",
        "Identifier|attack",
        "InterpEnd|)",
        $"StringEnd|{Quote}",
      ],
      Pieces(Text));
  }

  [Fact]
  public void NestedStringInsideInterpolation() {
    const string Text = "\"sum: $(a + \"b\")\"";

    Assert.Equal(
      [
        $"StringStart|{Quote}",
        "StringPart|sum: ",
        "InterpStart|$(",
        "Identifier|a",
        "Plus|+",
        $"StringStart|{Quote}",
        "StringPart|b",
        $"StringEnd|{Quote}",
        "InterpEnd|)",
        $"StringEnd|{Quote}",
      ],
      Pieces(Text));
    Assert.Empty(Lex(Text).Diagnostics);
  }

  [Fact]
  public void NestedInterpolationInsideNestedString() {
    const string Text = "\"outer $(inner + \"$(deep)\")\"";

    Assert.Equal(
      [
        $"StringStart|{Quote}",
        "StringPart|outer ",
        "InterpStart|$(",
        "Identifier|inner",
        "Plus|+",
        $"StringStart|{Quote}",
        "InterpStart|$(",
        "Identifier|deep",
        "InterpEnd|)",
        $"StringEnd|{Quote}",
        "InterpEnd|)",
        $"StringEnd|{Quote}",
      ],
      Pieces(Text));
    Assert.Empty(Lex(Text).Diagnostics);
  }

  [Fact]
  public void NestedParenthesesDoNotCloseTheInterpolationEarly() {
    const string Text = "\"$(f(a, (b)))x\"";

    Assert.Equal(
      [
        $"StringStart|{Quote}",
        "InterpStart|$(",
        "Identifier|f", "OpenParen|(", "Identifier|a", "Comma|,",
        "OpenParen|(", "Identifier|b", "CloseParen|)", "CloseParen|)",
        "InterpEnd|)",
        "StringPart|x",
        $"StringEnd|{Quote}",
      ],
      Pieces(Text));
    Assert.Empty(Lex(Text).Diagnostics);
  }

  [Fact]
  public void ExpressionTokensInsideInterpolationUseNormalLexing() {
    LexResult result = Lex("\"$(a + 1_000 <= 'x' and not b)\"");

    Assert.Equal(
      [
        TokenKind.StringStart, TokenKind.InterpStart, TokenKind.Identifier, TokenKind.Plus,
        TokenKind.IntLiteral, TokenKind.LessEqual, TokenKind.RuneLiteral, TokenKind.And,
        TokenKind.Not, TokenKind.Identifier, TokenKind.InterpEnd, TokenKind.StringEnd, TokenKind.EOF,
      ],
      result.Tokens.Select(static t => t.Kind));
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void CloseParenOutsideInterpolationIsOrdinary() {
    Assert.Equal(
      [TokenKind.Identifier, TokenKind.CloseParen, TokenKind.EOF],
      LexerCoreTests.Kinds("a)"));
    Assert.Equal(
      [TokenKind.OpenParen, TokenKind.StringStart, TokenKind.StringEnd, TokenKind.CloseParen, TokenKind.EOF],
      LexerCoreTests.Kinds("(\"\")"));
  }

  [Fact]
  public void WhitespaceAndCommentsInsideInterpolationAreTrivia() {
    const string Text = "\"\"\"x $(a # note\n + b) y\"\"\"";
    LexResult result = Lex(Text);

    Assert.Equal(
      [
        $"StringStart|{TripleQuote}",
        "StringPart|x ",
        "InterpStart|$(",
        "Identifier|a",
        "Plus|+",
        "Identifier|b",
        "InterpEnd|)",
        "StringPart| y",
        $"StringEnd|{TripleQuote}",
      ],
      Pieces(result));
    Assert.Equal(
      [TriviaKind.Whitespace, TriviaKind.Comment, TriviaKind.Newline, TriviaKind.Whitespace, TriviaKind.Whitespace],
      result.Trivia.Select(static t => t.Kind));
    Assert.Equal("# note", result.GetText(result.Trivia[1]).ToString());
    Assert.Empty(result.Diagnostics);
    AssertWellFormed(Text, result);
  }

  // ------------------------------------------------------------------------------------------
  // Multi-line strings
  // ------------------------------------------------------------------------------------------

  [Fact]
  public void MultilineStringSpansLines() {
    const string Text = "\"\"\"line1\nline2\"\"\"";

    Assert.Equal(
      [$"StringStart|{TripleQuote}", "StringPart|line1\nline2", $"StringEnd|{TripleQuote}"],
      Pieces(Text));
    AssertTokens(
      Text,
      (TokenKind.StringStart, 0, 3),
      (TokenKind.StringPart, 3, 11),
      (TokenKind.StringEnd, 14, 3));
  }

  [Fact]
  public void MultilineStringKeepsCRLFInsideThePart() {
    const string Text = "\"\"\"a\r\nb\"\"\"";

    AssertTokens(
      Text,
      (TokenKind.StringStart, 0, 3),
      (TokenKind.StringPart, 3, 4),
      (TokenKind.StringEnd, 7, 3));
  }

  [Fact]
  public void MultilineStringAllowsInteriorQuotes() {
    const string Text = "\"\"\"say \"hi\" and \"\"ok\"\" now\"\"\"";

    Assert.Equal(
      [$"StringStart|{TripleQuote}", "StringPart|say \"hi\" and \"\"ok\"\" now", $"StringEnd|{TripleQuote}"],
      Pieces(Text));
    Assert.Empty(Lex(Text).Diagnostics);
  }

  [Fact]
  public void MultilineStringSupportsInterpolationAcrossLines() {
    const string Text = "\"\"\"a\n$(x + 1)\nb $name\"\"\"";
    LexResult result = Lex(Text);

    Assert.Equal(
      [
        $"StringStart|{TripleQuote}",
        "StringPart|a\n",
        "InterpStart|$(",
        "Identifier|x",
        "Plus|+",
        "IntLiteral|1",
        "InterpEnd|)",
        "StringPart|\nb ",
        "InterpStart|$",
        "Identifier|name",
        "InterpEnd|",
        $"StringEnd|{TripleQuote}",
      ],
      Pieces(result));
    Assert.Empty(result.Diagnostics);
    AssertWellFormed(Text, result);
  }

  [Fact]
  public void MultilineStringSupportsStandardEscapes() {
    const string Text = "\"\"\"a\\n\\t\\r\\\"\\\\\\$b\"\"\"";

    Assert.Equal(
      [$"StringStart|{TripleQuote}", "StringPart|a\\n\\t\\r\\\"\\\\\\$b", $"StringEnd|{TripleQuote}"],
      Pieces(Text));
    Assert.Empty(Lex(Text).Diagnostics);
  }

  [Fact]
  public void MultilineStringFollowedByCode() {
    LexResult result = Lex("val s = \"\"\"a\nb\"\"\"\nval t = 1");

    Assert.Equal(
      [
        TokenKind.ValKeyword, TokenKind.Identifier, TokenKind.Assign,
        TokenKind.StringStart, TokenKind.StringPart, TokenKind.StringEnd,
        TokenKind.ValKeyword, TokenKind.Identifier, TokenKind.Assign, TokenKind.IntLiteral, TokenKind.EOF,
      ],
      result.Tokens.Select(static t => t.Kind));
    Assert.Empty(result.Diagnostics);
  }

  // ------------------------------------------------------------------------------------------
  // Malformed escapes
  // ------------------------------------------------------------------------------------------

  [Theory]
  [InlineData("\"\\q\"", 'q')]
  [InlineData("\"\\s\"", 's')]
  [InlineData("\"\\+\"", '+')]
  [InlineData("\"\\'\"", '\'')]
  [InlineData("\"\\0\"", '0')]
  public void UnknownEscapeReportsErrorButKeepsLexing(string text, char escaped) {
    LexResult result = Lex(text);

    Assert.Equal(
      [TokenKind.StringStart, TokenKind.StringPart, TokenKind.StringEnd, TokenKind.EOF],
      result.Tokens.Select(static t => t.Kind));
    AssertDiagnostics(result, (Lexer.InvalidStringEscapeCode, 1, 2));
    Assert.Contains($"\\{escaped}", result.Diagnostics[0].Message);
  }

  [Fact]
  public void UnknownEscapeDoesNotStopLaterEscapesOrCode() {
    LexResult result = Lex("\"\\q ok \\n \\s\" x");

    Assert.Equal(
      [TokenKind.StringStart, TokenKind.StringPart, TokenKind.StringEnd, TokenKind.Identifier, TokenKind.EOF],
      result.Tokens.Select(static t => t.Kind));
    AssertDiagnostics(result, (Lexer.InvalidStringEscapeCode, 1, 2), (Lexer.InvalidStringEscapeCode, 10, 2));
  }

  [Fact]
  public void UnknownEscapeInMultilineStringIsReported() {
    LexResult result = Lex("\"\"\"a\\qb\"\"\"");

    Assert.Equal(
      [TokenKind.StringStart, TokenKind.StringPart, TokenKind.StringEnd, TokenKind.EOF],
      result.Tokens.Select(static t => t.Kind));
    AssertDiagnostics(result, (Lexer.InvalidStringEscapeCode, 4, 2));
  }

  [Fact]
  public void UnknownEscapeSwallowsOnlyOneCodePoint() {
    // '\\' + U+1F332 (surrogate pair) is one invalid escape of length 3; the closing quote survives.
    LexResult result = Lex("\"\\\uD83C\uDF32\"");

    Assert.Equal(
      [TokenKind.StringStart, TokenKind.StringPart, TokenKind.StringEnd, TokenKind.EOF],
      result.Tokens.Select(static t => t.Kind));
    AssertDiagnostics(result, (Lexer.InvalidStringEscapeCode, 1, 3));
  }

  [Fact]
  public void TrailingBackslashAtEOFReportsEscapeAndUnterminatedString() {
    LexResult result = Lex("\"abc\\");

    Assert.Equal(
      [$"StringStart|{Quote}", "StringPart|abc\\", "StringEnd|"],
      Pieces(result));
    AssertDiagnostics(
      result,
      (Lexer.UnterminatedStringCode, 0, 5),
      (Lexer.UnterminatedStringEscapeCode, 4, 1));
  }

  [Fact]
  public void TrailingBackslashAtEndOfLineStillRecoversAtTheLineBreak() {
    LexResult result = Lex("\"ab\\\nx");

    Assert.Equal(
      [TokenKind.StringStart, TokenKind.StringPart, TokenKind.StringEnd, TokenKind.Identifier, TokenKind.EOF],
      result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(4, 0), result.Tokens[2].Span);
    Assert.Equal(new Trivia(TriviaKind.Newline, new SourceSpan(4, 1)), Assert.Single(result.Trivia));
    AssertDiagnostics(
      result,
      (Lexer.UnterminatedStringCode, 0, 4),
      (Lexer.UnterminatedStringEscapeCode, 3, 1));
  }

  [Fact]
  public void TrailingBackslashAtEndOfLineInsideMultilineStringIsReportedOnly() {
    LexResult result = Lex("\"\"\"a\\\nb\"\"\"");

    Assert.Equal(
      [$"StringStart|{TripleQuote}", "StringPart|a\\\nb", $"StringEnd|{TripleQuote}"],
      Pieces(result));
    AssertDiagnostics(result, (Lexer.UnterminatedStringEscapeCode, 4, 1));
  }

  // ------------------------------------------------------------------------------------------
  // Recovery
  // ------------------------------------------------------------------------------------------

  [Fact]
  public void UnclosedQuoteRecoversAtNewlineAndLaterDeclarationsStillLex() {
    LexResult result = Lex("var s = \"oops\nval x = 1");

    Assert.Equal(
      [
        TokenKind.VarKeyword, TokenKind.Identifier, TokenKind.Assign,
        TokenKind.StringStart, TokenKind.StringPart, TokenKind.StringEnd,
        TokenKind.ValKeyword, TokenKind.Identifier, TokenKind.Assign, TokenKind.IntLiteral, TokenKind.EOF,
      ],
      result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(13, 0), result.Tokens[5].Span);
    AssertDiagnostics(result, (Lexer.UnterminatedStringCode, 8, 5));
    AssertWellFormed("var s = \"oops\nval x = 1", result);
  }

  [Fact]
  public void UnclosedQuoteRecoversAtCRLF() {
    LexResult result = Lex("\"abc\r\nx");

    Assert.Equal(
      [TokenKind.StringStart, TokenKind.StringPart, TokenKind.StringEnd, TokenKind.Identifier, TokenKind.EOF],
      result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(4, 0), result.Tokens[2].Span);
    Assert.Equal(new Trivia(TriviaKind.Newline, new SourceSpan(4, 2)), Assert.Single(result.Trivia));
    AssertDiagnostics(result, (Lexer.UnterminatedStringCode, 0, 4));
  }

  [Fact]
  public void UnclosedQuoteAtEOFRecovers() {
    LexResult result = Lex("\"abc");

    Assert.Equal([$"StringStart|{Quote}", "StringPart|abc", "StringEnd|"], Pieces(result));
    Assert.Equal(new SourceSpan(4, 0), result.Tokens[2].Span);
    AssertDiagnostics(result, (Lexer.UnterminatedStringCode, 0, 4));
  }

  [Fact]
  public void LoneQuoteAtEOFRecovers() {
    LexResult result = Lex("\"");

    Assert.Equal([TokenKind.StringStart, TokenKind.StringEnd, TokenKind.EOF], result.Tokens.Select(static t => t.Kind));
    AssertDiagnostics(result, (Lexer.UnterminatedStringCode, 0, 1));
  }

  [Fact]
  public void UnclosedMultilineStringRecoversAtEOF() {
    const string Text = "\"\"\"abc\nval x = 1";
    LexResult result = Lex(Text);

    Assert.Equal(
      [$"StringStart|{TripleQuote}", "StringPart|abc\nval x = 1", "StringEnd|"],
      Pieces(result));
    Assert.Equal(new SourceSpan(Text.Length, 0), result.Tokens[2].Span);
    AssertDiagnostics(result, (Lexer.UnterminatedMultilineStringCode, 0, 3));
    AssertWellFormed(Text, result);
  }

  [Fact]
  public void UnclosedInterpolationAtEndOfLineRecoversTheWholeString() {
    const string Text = "\"a $(b + \nx = 1";
    LexResult result = Lex(Text);

    Assert.Equal(
      [
        $"StringStart|{Quote}",
        "StringPart|a ",
        "InterpStart|$(",
        "Identifier|b",
        "Plus|+",
        "InterpEnd|",
        "StringEnd|",
        "Identifier|x",
        "Assign|=",
        "IntLiteral|1",
      ],
      Pieces(result));
    Assert.Equal(new SourceSpan(9, 0), result.Tokens[5].Span);
    Assert.Equal(new SourceSpan(9, 0), result.Tokens[6].Span);
    AssertDiagnostics(
      result,
      (Lexer.UnterminatedStringCode, 0, 9),
      (Lexer.UnterminatedInterpolationCode, 3, 2));
    AssertWellFormed(Text, result);
  }

  [Fact]
  public void UnclosedInterpolationAtEOFInsideMultilineStringRecovers() {
    const string Text = "\"\"\"abc\n$(x";
    LexResult result = Lex(Text);

    Assert.Equal(
      [
        $"StringStart|{TripleQuote}",
        "StringPart|abc\n",
        "InterpStart|$(",
        "Identifier|x",
        "InterpEnd|",
        "StringEnd|",
      ],
      Pieces(result));
    AssertDiagnostics(
      result,
      (Lexer.UnterminatedMultilineStringCode, 0, 3),
      (Lexer.UnterminatedInterpolationCode, 7, 2));
    AssertWellFormed(Text, result);
  }

  [Fact]
  public void NewlineInsideMultilineInterpolationIsOrdinaryTrivia() {
    const string Text = "\"\"\"a $(b +\n c) d\"\"\"";
    LexResult result = Lex(Text);

    Assert.Empty(result.Diagnostics);
    Assert.Contains(result.Trivia, static t => t.Kind == TriviaKind.Newline);
    AssertWellFormed(Text, result);
  }

  [Fact]
  public void UnclosedNestedStringInsideInterpolationReportsEveryOpenConstruct() {
    const string Text = "\"a $(b + \"c\nx";
    LexResult result = Lex(Text);

    Assert.Equal(
      [
        $"StringStart|{Quote}",
        "StringPart|a ",
        "InterpStart|$(",
        "Identifier|b",
        "Plus|+",
        $"StringStart|{Quote}",
        "StringPart|c",
        "StringEnd|",
        "InterpEnd|",
        "StringEnd|",
        "Identifier|x",
      ],
      Pieces(result));
    AssertDiagnostics(
      result,
      (Lexer.UnterminatedStringCode, 0, 11),
      (Lexer.UnterminatedInterpolationCode, 3, 2),
      (Lexer.UnterminatedStringCode, 9, 2));
    AssertWellFormed(Text, result);
  }

  [Fact]
  public void UnclosedNestedSingleLineStringInsideMultilineInterpolationKeepsTheInterpolationOpen() {
    const string Text = "\"\"\"a $(b + \"c\n d) e\"\"\"";
    LexResult result = Lex(Text);

    Assert.Equal(
      [
        $"StringStart|{TripleQuote}",
        "StringPart|a ",
        "InterpStart|$(",
        "Identifier|b",
        "Plus|+",
        $"StringStart|{Quote}",
        "StringPart|c",
        "StringEnd|",
        "Identifier|d",
        "InterpEnd|)",
        "StringPart| e",
        $"StringEnd|{TripleQuote}",
      ],
      Pieces(result));
    AssertDiagnostics(result, (Lexer.UnterminatedStringCode, 11, 2));
    AssertWellFormed(Text, result);
  }

  [Fact]
  public void CommentInsideSingleLineInterpolationEndsTheLineAndRecovers() {
    const string Text = "\"a $(b # c)\"\nx";
    LexResult result = Lex(Text);

    Assert.Equal(
      [
        $"StringStart|{Quote}", "StringPart|a ", "InterpStart|$(", "Identifier|b",
        "InterpEnd|", "StringEnd|", "Identifier|x",
      ],
      Pieces(result));
    Assert.Equal(2, result.Diagnostics.Length);
    AssertWellFormed(Text, result);
  }

  [Fact]
  public void CodeAfterRecoveredStringsLexesNormally() {
    const string Text = "fn a() { \"x $(y\n}\nfn b() { 1 }";
    LexResult result = Lex(Text);

    TokenKind[] kinds = [.. result.Tokens.Select(static t => t.Kind)];
    Assert.Equal(2, kinds.Count(static k => k == TokenKind.FnKeyword));
    Assert.Equal(2, kinds.Count(static k => k == TokenKind.CloseBrace));
    Assert.Contains(TokenKind.IntLiteral, kinds);
    AssertWellFormed(Text, result);
  }

  // ------------------------------------------------------------------------------------------
  // Robustness
  // ------------------------------------------------------------------------------------------

  [Fact]
  public void LexNeverThrowsAndStaysWellFormedOnRandomStringHeavyInput() {
    const string Alphabet = "ab$()\"\"\"\\\\\n\r #'+x\uD83D";
    var random = new Random(20260418);

    for (int i = 0; i < 5000; i++) {
      int length = random.Next(0, 32);
      char[] chars = new char[length];
      for (int j = 0; j < length; j++) {
        chars[j] = Alphabet[random.Next(Alphabet.Length)];
      }

      string text = new(chars);
      AssertWellFormed(text, Lex(text));
    }
  }
}
