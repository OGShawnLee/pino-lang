using System.Collections.Immutable;
using System.Runtime.CompilerServices;

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
  public void NonKeywordWordsLexAsIdentifier(string text) {
    LexResult result = Lex(text);

    Assert.Equal([TokenKind.Identifier, TokenKind.EOF], result.Tokens.Select(static t => t.Kind));
    Assert.Empty(result.Diagnostics);
  }

  // D-021
  [Fact]
  public void DollarOutsideStringIsUnexpectedCharacter() {
    LexResult lone = Lex("$");
    Assert.Equal([TokenKind.Error, TokenKind.EOF], lone.Tokens.Select(static t => t.Kind));
    Assert.Equal(DiagnosticCodes.UnexpectedCharacter, Assert.Single(lone.Diagnostics).Code);

    LexResult prefix = Lex("$y");
    Assert.Equal([TokenKind.Error, TokenKind.Identifier, TokenKind.EOF], prefix.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(0, 1), prefix.Tokens[0].Span);
    Assert.Equal(new SourceSpan(1, 1), prefix.Tokens[1].Span);
    Assert.Equal(DiagnosticCodes.UnexpectedCharacter, Assert.Single(prefix.Diagnostics).Code);

    LexResult middle = Lex("a$b");
    Assert.Equal([TokenKind.Identifier, TokenKind.Error, TokenKind.Identifier, TokenKind.EOF], middle.Tokens.Select(static t => t.Kind));
    Assert.Equal(DiagnosticCodes.UnexpectedCharacter, Assert.Single(middle.Diagnostics).Code);

    LexResult suffix = Lex("a1_$");
    Assert.Equal([TokenKind.Identifier, TokenKind.Error, TokenKind.EOF], suffix.Tokens.Select(static t => t.Kind));
    Assert.Equal(DiagnosticCodes.UnexpectedCharacter, Assert.Single(suffix.Diagnostics).Code);

    LexResult afterNumber = Lex("123$a");
    Assert.Equal([TokenKind.IntLiteral, TokenKind.Error, TokenKind.Identifier, TokenKind.EOF], afterNumber.Tokens.Select(static t => t.Kind));
    Assert.Equal(DiagnosticCodes.UnexpectedCharacter, Assert.Single(afterNumber.Diagnostics).Code);
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
        { "->", TokenKind.Arrow },
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
        KindsWithoutEOF("=== = == -> :: :"));
    Assert.Equal([TokenKind.EqualEqualEqual, TokenKind.Assign], KindsWithoutEOF("===="));
    Assert.Equal([TokenKind.EqualEqual, TokenKind.Assign], KindsWithoutEOF("==\t="));
    Assert.Equal([TokenKind.BangEqualEqual, TokenKind.BangEqual], KindsWithoutEOF("!== !="));
  }

  [Fact]
  public void FatArrowLexesAsAssignAndGreaterAndContinuesLexing() {
    LexResult result = Lex("when x => 1");

    Assert.Equal(
        [TokenKind.WhenKeyword, TokenKind.Identifier, TokenKind.Assign, TokenKind.Greater, TokenKind.IntLiteral, TokenKind.EOF],
        result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(7, 1), result.Tokens[2].Span);
    Assert.Equal(new SourceSpan(8, 1), result.Tokens[3].Span);
    Assert.Empty(result.Diagnostics);
    Assert.Equal([TokenKind.Minus, TokenKind.Greater], KindsWithoutEOF("- >"));
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

  // D-092
  [Theory]
  [InlineData("0", TokenKind.IntLiteral)]
  [InlineData("42", TokenKind.IntLiteral)]
  [InlineData("1_2", TokenKind.IntLiteral)]
  [InlineData("1_000", TokenKind.IntLiteral)]
  [InlineData("1_000_000", TokenKind.IntLiteral)]
  [InlineData("1_2_3_4", TokenKind.IntLiteral)]
  [InlineData("12345678901234567890", TokenKind.IntLiteral)]
  [InlineData("3.14", TokenKind.FloatLiteral)]
  [InlineData("1_2.3_4", TokenKind.FloatLiteral)]
  [InlineData("1_000.000_1", TokenKind.FloatLiteral)]
  [InlineData("1000000000.0", TokenKind.FloatLiteral)]
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
  public void LoneCarriageReturnActsAsNewlineTriviaAndSetsLeadingNewline() {
    LexResult result = Lex("a\rb");

    Assert.Equal([TokenKind.Identifier, TokenKind.Identifier, TokenKind.EOF], result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new Trivia(TriviaKind.Newline, new SourceSpan(1, 1)), Assert.Single(result.Trivia));
    Assert.Equal(TokenFlags.LeadingNewline, result.Tokens[1].Flags);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void CommentStopsBeforeNewlineAndHashInsideCommentIsText() {
    LexResult result = Lex("# a # b\nx");

    Assert.Equal(new Trivia(TriviaKind.Comment, new SourceSpan(0, 7)), result.Trivia[0]);
    Assert.Equal(new Trivia(TriviaKind.Newline, new SourceSpan(7, 1)), result.Trivia[1]);
    Assert.Equal(
        new Token(TokenKind.Identifier, new SourceSpan(8, 1), TokenFlags.LeadingComment | TokenFlags.LeadingNewline),
        result.Tokens[0]);
  }

  [Fact]
  public void LeadingByteOrderMarkBecomesTriviaWithoutDiagnostics() {
    LexResult result = Lex("\uFEFFval x = 1");

    Assert.Empty(result.Diagnostics);
    Assert.Equal([new Trivia(TriviaKind.ByteOrderMark, new SourceSpan(0, 1)), new Trivia(TriviaKind.Whitespace, new SourceSpan(4, 1)), new Trivia(TriviaKind.Whitespace, new SourceSpan(6, 1)), new Trivia(TriviaKind.Whitespace, new SourceSpan(8, 1))], result.Trivia.AsEnumerable());
    Assert.Equal(
        [TokenKind.ValKeyword, TokenKind.Identifier, TokenKind.Assign, TokenKind.IntLiteral, TokenKind.EOF],
        result.Tokens.Select(static t => t.Kind));
    Assert.Equal(new SourceSpan(1, 3), result.Tokens[0].Span);
    Assert.Equal(TokenFlags.None, result.Tokens[0].Flags);
    Assert.Equal(TriviaKind.ByteOrderMark, result.GetLeadingTrivia(0)[0].Kind);
  }

  [Fact]
  public void NonLeadingByteOrderMarkRemainsUnexpectedCharacter() {
    LexResult result = Lex(" \uFEFF");

    Assert.Equal([TokenKind.Error, TokenKind.EOF], result.Tokens.Select(static t => t.Kind));
    Assert.Equal(DiagnosticCodes.UnexpectedCharacter, Assert.Single(result.Diagnostics).Code);
  }

  [Fact]
  public void TokenSizeRemainsTwelveBytes() {
    Assert.Equal(12, Unsafe.SizeOf<Token>());
  }

  [Fact]
  public void TokenFlagsTrackLeadingWhitespaceNewlineAndComment() {
    LexResult tight = Lex("[a -b]");
    // [ (0), a (1), - (2), b (3), ] (4), EOF (5)
    Assert.Equal(TokenFlags.None, tight.Tokens[0].Flags);
    Assert.Equal(TokenFlags.None, tight.Tokens[1].Flags);
    Assert.Equal(TokenFlags.LeadingWhitespace, tight.Tokens[2].Flags);
    Assert.Equal(TokenFlags.None, tight.Tokens[3].Flags);
    Assert.Equal(TokenFlags.None, tight.Tokens[4].Flags);

    LexResult spaced = Lex("[a - b]");
    Assert.Equal(TokenFlags.LeadingWhitespace, spaced.Tokens[2].Flags);
    Assert.Equal(TokenFlags.LeadingWhitespace, spaced.Tokens[3].Flags);

    LexResult newlineBracket = Lex("a\n[b]");
    Assert.Equal(TokenKind.OpenBracket, newlineBracket.Tokens[1].Kind);
    Assert.True(newlineBracket.Tokens[1].Flags.HasFlag(TokenFlags.LeadingNewline));
    Assert.False(newlineBracket.Tokens[1].Flags.HasFlag(TokenFlags.LeadingWhitespace));

    LexResult indentedNewlineBracket = Lex("a\n  [b]");
    Assert.Equal(TokenFlags.LeadingNewline | TokenFlags.LeadingWhitespace, indentedNewlineBracket.Tokens[1].Flags);

    LexResult commentBeforeToken = Lex("# header\nval x = 1");
    Assert.Equal(TokenKind.ValKeyword, commentBeforeToken.Tokens[0].Kind);
    Assert.Equal(TokenFlags.LeadingComment | TokenFlags.LeadingNewline, commentBeforeToken.Tokens[0].Flags);
  }

  // D-010
  [Fact]
  public void TokenFlagsExposeAsymmetricMinusSpacingForD010() {
    LexResult leadingSpace = Lex("[a -b]");
    Assert.Equal(TokenKind.Minus, leadingSpace.Tokens[2].Kind);
    Assert.Equal(TokenFlags.LeadingWhitespace, leadingSpace.Tokens[2].Flags);
    Assert.Equal(TokenFlags.None, leadingSpace.Tokens[3].Flags);
    Assert.True(leadingSpace.GetLeadingTrivia(3).IsEmpty);

    LexResult trailingSpace = Lex("[a- b]");
    Assert.Equal(TokenKind.Minus, trailingSpace.Tokens[2].Kind);
    Assert.Equal(TokenFlags.None, trailingSpace.Tokens[2].Flags);
    Assert.True(trailingSpace.GetLeadingTrivia(2).IsEmpty);
    Assert.Equal(TokenFlags.LeadingWhitespace, trailingSpace.Tokens[3].Flags);
    Assert.False(trailingSpace.GetLeadingTrivia(3).IsEmpty);

    LexResult bothSpaced = Lex("[a - b]");
    Assert.Equal(TokenFlags.LeadingWhitespace, bothSpaced.Tokens[2].Flags);
    Assert.Equal(TokenFlags.LeadingWhitespace, bothSpaced.Tokens[3].Flags);

    LexResult tight = Lex("[a-b]");
    Assert.Equal(TokenFlags.None, tight.Tokens[2].Flags);
    Assert.Equal(TokenFlags.None, tight.Tokens[3].Flags);
  }

  [Fact]
  public void GetLeadingTriviaReturnsExactSliceViaBinarySearch() {
    const string Text = "  # lead\nval  x\n# mid\n  y # tail";
    LexResult result = Lex(Text);
    // Tokens: val(0), x(1), y(2), EOF(3)
    Assert.Equal(4, result.Tokens.Length);

    ReadOnlySpan<Trivia> beforeVal = result.GetLeadingTrivia(0);
    Assert.Equal(3, beforeVal.Length);
    Assert.Equal([TriviaKind.Whitespace, TriviaKind.Comment, TriviaKind.Newline], [beforeVal[0].Kind, beforeVal[1].Kind, beforeVal[2].Kind]);

    ReadOnlySpan<Trivia> beforeX = result.GetLeadingTrivia(1);
    Assert.Equal(1, beforeX.Length);
    Assert.Equal(TriviaKind.Whitespace, beforeX[0].Kind);

    ReadOnlySpan<Trivia> beforeY = result.GetLeadingTrivia(2);
    Assert.Equal(4, beforeY.Length);
    Assert.Equal(
        [TriviaKind.Newline, TriviaKind.Comment, TriviaKind.Newline, TriviaKind.Whitespace],
        [beforeY[0].Kind, beforeY[1].Kind, beforeY[2].Kind, beforeY[3].Kind]);

    ReadOnlySpan<Trivia> beforeEOF = result.GetLeadingTrivia(3);
    Assert.Equal(2, beforeEOF.Length);
    Assert.Equal([TriviaKind.Whitespace, TriviaKind.Comment], [beforeEOF[0].Kind, beforeEOF[1].Kind]);

    Assert.True(Lex("a+b").GetLeadingTrivia(1).IsEmpty);
    Assert.Throws<ArgumentOutOfRangeException>(() => result.GetLeadingTrivia(-1));
    Assert.Throws<ArgumentOutOfRangeException>(() => result.GetLeadingTrivia(result.Tokens.Length));
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
    LexResult result = Lex("pub fn add(a int, b int) int -> a + b");

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
