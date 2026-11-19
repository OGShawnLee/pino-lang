using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Lexing;

public sealed class LexerCoreTests {
  internal static LexResult Lex(string text) => Lexer.Lex(new SourceText(text));

  internal static TokenKind[] Kinds(string text) =>
      [.. Lex(text).Tokens.Select(static t => t.Kind)];

  internal static TokenKind[] KindsWithoutEOF(string text) =>
      [.. Lex(text).Tokens.Where(static t => t.Kind != TokenKind.EOF).Select(static t => t.Kind)];

  public static TheoryData<string, TokenKind> KeywordCases => new()
  {
        { "as", TokenKind.AsKeyword },
        { "break", TokenKind.BreakKeyword },
        { "val", TokenKind.ValKeyword },
        { "continue", TokenKind.ContinueKeyword },
        { "else", TokenKind.ElseKeyword },
        { "enum", TokenKind.EnumKeyword },
        { "from", TokenKind.FromKeyword },
        { "fn", TokenKind.FnKeyword },
        { "if", TokenKind.IfKeyword },
        { "import", TokenKind.ImportKeyword },
        { "in", TokenKind.InKeyword },
        { "interface", TokenKind.InterfaceKeyword },
        { "is", TokenKind.IsKeyword },
        { "for", TokenKind.ForKeyword },
        { "match", TokenKind.MatchKeyword },
        { "module", TokenKind.ModuleKeyword },
        { "pub", TokenKind.PubKeyword },
        { "return", TokenKind.ReturnKeyword },
        { "static", TokenKind.StaticKeyword },
        { "struct", TokenKind.StructKeyword },
        { "then", TokenKind.ThenKeyword },
        { "var", TokenKind.VarKeyword },
        { "when", TokenKind.WhenKeyword },
        { "union", TokenKind.UnionKeyword },
        { "yield", TokenKind.YieldKeyword },
        { "test", TokenKind.TestKeyword },
        { "assert", TokenKind.AssertKeyword },
        { "and", TokenKind.And },
        { "or", TokenKind.Or },
        { "not", TokenKind.Not },
        { "true", TokenKind.TrueLiteral },
        { "false", TokenKind.FalseLiteral },
    };

  [Theory]
  [MemberData(nameof(KeywordCases))]
  public void KeywordsLexToTheirKinds(string text, TokenKind expected) {
    LexResult result = Lex(text);

    Assert.Equal([expected, TokenKind.EOF], result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(0, text.Length), result.Tokens[0].Span);
    Assert.Empty(result.Diagnostics);
  }

  [Theory]
  [InlineData("int")]
  [InlineData("string")]
  [InlineData("map")]
  [InlineData("set")]
  [InlineData("void")]
  [InlineData("any")]
  [InlineData("null")]
  [InlineData("Val")]
  [InlineData("iff")]
  [InlineData("_x")]
  [InlineData("$y")]
  [InlineData("a1_$")]
  public void NonKeywordWordsLexAsIdentifier(string text) {
    LexResult result = Lex(text);

    Assert.Equal([TokenKind.Identifier, TokenKind.EOF], result.Tokens.Select(static t => t.Kind));
    Assert.Empty(result.Diagnostics);
  }

  public static TheoryData<string, TokenKind> OperatorCases => new()
  {
        { "@", TokenKind.At },
        { "{", TokenKind.OpenBrace },
        { "}", TokenKind.CloseBrace },
        { "[", TokenKind.OpenBracket },
        { "]", TokenKind.CloseBracket },
        { "(", TokenKind.OpenParen },
        { ")", TokenKind.CloseParen },
        { ",", TokenKind.Comma },
        { "=", TokenKind.Assign },
        { "+", TokenKind.Plus },
        { "+=", TokenKind.PlusAssign },
        { "-", TokenKind.Minus },
        { "-=", TokenKind.MinusAssign },
        { "*", TokenKind.Star },
        { "*=", TokenKind.StarAssign },
        { "/", TokenKind.Slash },
        { "/=", TokenKind.SlashAssign },
        { "%", TokenKind.Percent },
        { "%=", TokenKind.PercentAssign },
        { "<", TokenKind.Less },
        { "<=", TokenKind.LessEqual },
        { ">", TokenKind.Greater },
        { ">=", TokenKind.GreaterEqual },
        { "==", TokenKind.EqualEqual },
        { "!=", TokenKind.BangEqual },
        { "===", TokenKind.EqualEqualEqual },
        { "!==", TokenKind.BangEqualEqual },
        { ":", TokenKind.Colon },
        { "::", TokenKind.ColonColon },
        { "=>", TokenKind.Arrow },
        { "?", TokenKind.Question },
    };

  [Theory]
  [MemberData(nameof(OperatorCases))]
  public void OperatorsAndPunctuationLexToTheirKinds(string text, TokenKind expected) {
    LexResult result = Lex(text);

    Assert.Equal([expected, TokenKind.EOF], result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(0, text.Length), result.Tokens[0].Span);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void OperatorsUseMaximalMunch() {
    Assert.Equal(
        [TokenKind.EqualEqualEqual, TokenKind.Assign, TokenKind.EqualEqual, TokenKind.Arrow, TokenKind.ColonColon, TokenKind.Colon],
        KindsWithoutEOF("=== = == => :: :"));
    Assert.Equal([TokenKind.EqualEqualEqual, TokenKind.Assign], KindsWithoutEOF("===="));
    Assert.Equal([TokenKind.EqualEqual, TokenKind.Assign], KindsWithoutEOF("==\t="));
    Assert.Equal([TokenKind.BangEqualEqual, TokenKind.BangEqual], KindsWithoutEOF("!== !="));
  }

  [Fact]
  public void SubtractionNeverAbsorbsMinusIntoNumber() {
    LexResult result = Lex("a-1");

    Assert.Equal(
        [TokenKind.Identifier, TokenKind.Minus, TokenKind.IntLiteral, TokenKind.EOF],
        result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(0, 1), result.Tokens[0].Span);
    Assert.Equal(new SourceSpan(1, 1), result.Tokens[1].Span);
    Assert.Equal(new SourceSpan(2, 1), result.Tokens[2].Span);
    Assert.Equal(new SourceSpan(3, 0), result.Tokens[3].Span);
    Assert.Equal([TokenKind.Minus, TokenKind.IntLiteral], KindsWithoutEOF("-5"));
    Assert.Equal([TokenKind.Minus, TokenKind.FloatLiteral], KindsWithoutEOF("-5.5"));
  }

  [Theory]
  [InlineData("0", TokenKind.IntLiteral)]
  [InlineData("42", TokenKind.IntLiteral)]
  [InlineData("1_000", TokenKind.IntLiteral)]
  [InlineData("1_000_000", TokenKind.IntLiteral)]
  [InlineData("3.14", TokenKind.FloatLiteral)]
  [InlineData("1_000.000_1", TokenKind.FloatLiteral)]
  [InlineData("0.5", TokenKind.FloatLiteral)]
  public void ValidNumbersLexWithoutDiagnostics(string text, TokenKind expected) {
    LexResult result = Lex(text);

    Assert.Equal([expected, TokenKind.EOF], result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(0, text.Length), result.Tokens[0].Span);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void IntFollowedByDotAndNonDigitIsNotAFloat() {
    LexResult result = Lex("1.x");

    Assert.Equal(
        [TokenKind.IntLiteral, TokenKind.Error, TokenKind.Identifier],
        result.Tokens.Take(3).Select(static t => t.Kind));
    Assert.Single(result.Diagnostics);
  }

  public static TheoryData<string, TokenKind> RuneCases => new()
  {
        { "'a'", TokenKind.RuneLiteral },
        { "'🌲'", TokenKind.RuneLiteral },
        { "'\\n'", TokenKind.RuneLiteral },
        { "'\\t'", TokenKind.RuneLiteral },
        { "'\\r'", TokenKind.RuneLiteral },
        { "'\\''", TokenKind.RuneLiteral },
        { "'\\\\'", TokenKind.RuneLiteral },
        { "' '", TokenKind.RuneLiteral },
        { "'#'", TokenKind.RuneLiteral },
    };

  [Theory]
  [MemberData(nameof(RuneCases))]
  public void ValidRunesLexAsRuneLiteral(string text, TokenKind expected) {
    LexResult result = Lex(text);

    Assert.Equal([expected, TokenKind.EOF], result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(0, text.Length), result.Tokens[0].Span);
    Assert.Empty(result.Diagnostics);
    Assert.Empty(result.Trivia);
  }

  [Fact]
  public void TriviaIsPreservedWithExactSpans() {
    const string Text = "a  b # note\r\nc\n\td";
    LexResult result = Lex(Text);

    Assert.Equal(
        [TokenKind.Identifier, TokenKind.Identifier, TokenKind.Identifier, TokenKind.Identifier, TokenKind.EOF],
        result.Tokens.Select(static t => t.Kind));

    ImmutableArray<Trivia> expected =
    [
        new Trivia(TriviaKind.Whitespace, new SourceSpan(1, 2)),
            new Trivia(TriviaKind.Whitespace, new SourceSpan(4, 1)),
            new Trivia(TriviaKind.Comment, new SourceSpan(5, 6)),
            new Trivia(TriviaKind.Newline, new SourceSpan(11, 2)),
            new Trivia(TriviaKind.Newline, new SourceSpan(14, 1)),
            new Trivia(TriviaKind.Whitespace, new SourceSpan(15, 1)),
        ];
    Assert.Equal(expected.AsEnumerable(), result.Trivia.AsEnumerable());
    Assert.Equal("# note", result.GetText(result.Trivia[2]).ToString());
  }

  [Fact]
  public void CommentStopsBeforeNewlineAndHashInsideCommentIsText() {
    LexResult result = Lex("# a # b\nx");

    Assert.Equal(new Trivia(TriviaKind.Comment, new SourceSpan(0, 7)), result.Trivia[0]);
    Assert.Equal(new Trivia(TriviaKind.Newline, new SourceSpan(7, 1)), result.Trivia[1]);
    Assert.Equal(new Token(TokenKind.Identifier, new SourceSpan(8, 1)), result.Tokens[0]);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   \n\t")]
  [InlineData("# only a comment")]
  [InlineData("fn main() { return 1_000 + a-1 <= 'x' }\n# end\r\n")]
  [InlineData("x ~ 1__0 '' 'abc' 'a\n\"s\" 12ab")]
  public void TokensAndTriviaLosslesslyCoverTheSource(string text) {
    LexResult result = Lex(text);

    var spans = result.Tokens.Select(static t => t.Span)
        .Concat(result.Trivia.Select(static t => t.Span))
        .OrderBy(static s => s.Start)
        .ThenBy(static s => s.Length)
        .ToList();

    int position = 0;
    foreach (SourceSpan span in spans) {
      Assert.Equal(position, span.Start);
      position = span.End;
    }

    Assert.Equal(text.Length, position);
    Assert.Equal(TokenKind.EOF, result.Tokens[^1].Kind);
    Assert.Equal(new SourceSpan(text.Length, 0), result.Tokens[^1].Span);
  }

  [Fact]
  public void EmptySourceProducesOnlyEOF() {
    LexResult result = Lex("");

    Assert.Equal([new Token(TokenKind.EOF, new SourceSpan(0, 0))], result.Tokens.AsEnumerable());
    Assert.Empty(result.Trivia);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void RealisticSnippetLexesToExpectedKinds() {
    LexResult result = Lex("pub fn add(a int, b int) int => a + b");

    Assert.Equal(
        [
            TokenKind.PubKeyword, TokenKind.FnKeyword, TokenKind.Identifier, TokenKind.OpenParen,
                TokenKind.Identifier, TokenKind.Identifier, TokenKind.Comma, TokenKind.Identifier,
                TokenKind.Identifier, TokenKind.CloseParen, TokenKind.Identifier, TokenKind.Arrow,
                TokenKind.Identifier, TokenKind.Plus, TokenKind.Identifier, TokenKind.EOF,
            ],
        result.Tokens.Select(static t => t.Kind));
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void LexingSameSourceTwiceIsDeterministic() {
    var source = new SourceText("var x = 1_0 + 'a' # c");

    LexResult first = Lexer.Lex(source);
    LexResult second = Lexer.Lex(source);

    Assert.Equal(first.Tokens.AsEnumerable(), second.Tokens.AsEnumerable());
    Assert.Equal(first.Trivia.AsEnumerable(), second.Trivia.AsEnumerable());
    Assert.Equal(first.Diagnostics.AsEnumerable(), second.Diagnostics.AsEnumerable());
    Assert.Same(source, first.Source);
  }
}
