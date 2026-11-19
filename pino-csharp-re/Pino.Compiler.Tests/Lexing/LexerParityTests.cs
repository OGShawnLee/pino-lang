using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Lexing;

/// <summary>
/// Mirrors every scenario of the legacy <c>pino-csharp.tests/LexerTests.cs</c> against the new
/// token model: explicit string tokens instead of synthetic <c>+</c> operators, plus exact spans.
/// </summary>
public sealed class LexerParityTests {
  private static LexResult Lex(string text) => LexerCoreTests.Lex(text);

  /// <summary>Space-separated words, one token per word; spans are derived from the offsets.</summary>
  private static void AssertWords(string text, params TokenKind[] kinds) {
    string[] words = text.Split(' ');
    Assert.Equal(words.Length, kinds.Length);

    var expected = new List<(TokenKind, int, int)>();
    int offset = 0;
    for (int i = 0; i < words.Length; i++) {
      expected.Add((kinds[i], offset, words[i].Length));
      offset += words[i].Length + 1;
    }

    LexerStringTests.AssertTokens(text, [.. expected]);
  }

  [Fact]
  public void IdentifiersAndKeywords() {
    AssertWords(
      "var val fn if else match when for struct enum break continue return identifier_name",
      TokenKind.VarKeyword,
      TokenKind.ValKeyword,
      TokenKind.FnKeyword,
      TokenKind.IfKeyword,
      TokenKind.ElseKeyword,
      TokenKind.MatchKeyword,
      TokenKind.WhenKeyword,
      TokenKind.ForKeyword,
      TokenKind.StructKeyword,
      TokenKind.EnumKeyword,
      TokenKind.BreakKeyword,
      TokenKind.ContinueKeyword,
      TokenKind.ReturnKeyword,
      TokenKind.Identifier);
  }

  [Fact]
  public void Operators() {
    AssertWords(
      "= + - * / % == != < <= > >= and or not : :: += -= *= /= %=",
      TokenKind.Assign,
      TokenKind.Plus,
      TokenKind.Minus,
      TokenKind.Star,
      TokenKind.Slash,
      TokenKind.Percent,
      TokenKind.EqualEqual,
      TokenKind.BangEqual,
      TokenKind.Less,
      TokenKind.LessEqual,
      TokenKind.Greater,
      TokenKind.GreaterEqual,
      TokenKind.And,
      TokenKind.Or,
      TokenKind.Not,
      TokenKind.Colon,
      TokenKind.ColonColon,
      TokenKind.PlusAssign,
      TokenKind.MinusAssign,
      TokenKind.StarAssign,
      TokenKind.SlashAssign,
      TokenKind.PercentAssign);
  }

  [Fact]
  public void Literals() {
    LexerStringTests.AssertTokens(
      "42 3.14 true false \"hello world\"",
      (TokenKind.IntLiteral, 0, 2),
      (TokenKind.FloatLiteral, 3, 4),
      (TokenKind.TrueLiteral, 8, 4),
      (TokenKind.FalseLiteral, 13, 5),
      (TokenKind.StringStart, 19, 1),
      (TokenKind.StringPart, 20, 11),
      (TokenKind.StringEnd, 31, 1));
  }

  [Fact]
  public void LiteralStringTextIsRecoverableFromTheSpan() {
    LexResult result = Lex("42 3.14 true false \"hello world\"");

    Assert.Equal("42", result.GetText(result.Tokens[0]).ToString());
    Assert.Equal("3.14", result.GetText(result.Tokens[1]).ToString());
    Assert.Equal("hello world", result.GetText(result.Tokens[5]).ToString());
  }

  [Fact]
  public void StringInterpolation() {
    // Legacy: "hello " + name + " inside " + room (synthetic '+'). New model: explicit tokens.
    const string Text = "\"hello $name inside $room\"";

    LexerStringTests.AssertTokens(
      Text,
      (TokenKind.StringStart, 0, 1),
      (TokenKind.StringPart, 1, 6),
      (TokenKind.InterpStart, 7, 1),
      (TokenKind.Identifier, 8, 4),
      (TokenKind.InterpEnd, 12, 0),
      (TokenKind.StringPart, 12, 8),
      (TokenKind.InterpStart, 20, 1),
      (TokenKind.Identifier, 21, 4),
      (TokenKind.InterpEnd, 25, 0),
      (TokenKind.StringEnd, 25, 1));
    Assert.DoesNotContain(Lex(Text).Tokens, static t => t.Kind == TokenKind.Plus);
  }

  [Fact]
  public void ComplexStringInterpolation() {
    // Legacy: "Base Attack: " + ( player : attack ). New model: InterpStart/InterpEnd around the expression.
    const string Text = "\"Base Attack: $(player:attack)\"";

    LexerStringTests.AssertTokens(
      Text,
      (TokenKind.StringStart, 0, 1),
      (TokenKind.StringPart, 1, 13),
      (TokenKind.InterpStart, 14, 2),
      (TokenKind.Identifier, 16, 6),
      (TokenKind.Colon, 22, 1),
      (TokenKind.Identifier, 23, 6),
      (TokenKind.InterpEnd, 29, 1),
      (TokenKind.StringEnd, 30, 1));
    Assert.DoesNotContain(Lex(Text).Tokens, static t => t.Kind == TokenKind.Plus);
  }

  [Fact]
  public void SkipCommentsAndWhitespace() {
    const string Text = "   var  # this is a comment";
    LexResult result = Lex(Text);

    Assert.Equal(
      [
        new Token(TokenKind.VarKeyword, new SourceSpan(3, 3), TokenFlags.LeadingWhitespace),
        new Token(TokenKind.EOF, new SourceSpan(Text.Length, 0), TokenFlags.LeadingWhitespace | TokenFlags.LeadingComment),
      ],
      result.Tokens.AsEnumerable());
    Assert.Equal(
      [
        new Trivia(TriviaKind.Whitespace, new SourceSpan(0, 3)),
        new Trivia(TriviaKind.Whitespace, new SourceSpan(6, 2)),
        new Trivia(TriviaKind.Comment, new SourceSpan(8, 19)),
      ],
      result.Trivia.AsEnumerable());
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void ComplexStatement() {
    LexerStringTests.AssertTokens(
      "var pos = Coordinates { x: 10, y: 20 }",
      (TokenKind.VarKeyword, 0, 3),
      (TokenKind.Identifier, 4, 3),
      (TokenKind.Assign, 8, 1),
      (TokenKind.Identifier, 10, 11),
      (TokenKind.OpenBrace, 22, 1),
      (TokenKind.Identifier, 24, 1),
      (TokenKind.Colon, 25, 1),
      (TokenKind.IntLiteral, 27, 2),
      (TokenKind.Comma, 29, 1),
      (TokenKind.Identifier, 31, 1),
      (TokenKind.Colon, 32, 1),
      (TokenKind.IntLiteral, 34, 2),
      (TokenKind.CloseBrace, 37, 1));
  }

  [Fact]
  public void RuneLiterals() {
    // 'a' 'tree emoji (surrogate pair)' '\n' '\''
    const string Text = "'a' '\uD83C\uDF32' '\\n' '\\''";

    LexerStringTests.AssertTokens(
      Text,
      (TokenKind.RuneLiteral, 0, 3),
      (TokenKind.RuneLiteral, 4, 4),
      (TokenKind.RuneLiteral, 9, 4),
      (TokenKind.RuneLiteral, 14, 4));
  }

  [Fact]
  public void AtAndIsTokens() {
    LexerStringTests.AssertTokens(
      "@is @generic T is DocumentShape",
      (TokenKind.At, 0, 1),
      (TokenKind.IsKeyword, 1, 2),
      (TokenKind.At, 4, 1),
      (TokenKind.Identifier, 5, 7),
      (TokenKind.Identifier, 13, 1),
      (TokenKind.IsKeyword, 15, 2),
      (TokenKind.Identifier, 18, 13));
  }
}
