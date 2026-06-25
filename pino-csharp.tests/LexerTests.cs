using Xunit;
using System.Collections.Generic;
using Pino;

namespace pino_csharp.tests;

public class LexerTests {
  [Fact]
  public void TestIdentifiersAndKeywords() {
    var input = "var val fn if else match when for struct enum break continue return identifier_name";
    var tokens = Lexer.LexLine(input);

    Assert.Equal(14, tokens.Count);

    Assert.True(tokens[0].IsKeyword(KeywordType.Variable));
    Assert.Equal("var", tokens[0].Data);

    Assert.True(tokens[1].IsKeyword(KeywordType.Constant));
    Assert.Equal("val", tokens[1].Data);

    Assert.True(tokens[2].IsKeyword(KeywordType.Function));
    Assert.Equal("fn", tokens[2].Data);

    Assert.True(tokens[3].IsKeyword(KeywordType.If));
    Assert.Equal("if", tokens[3].Data);

    Assert.True(tokens[4].IsKeyword(KeywordType.Else));
    Assert.Equal("else", tokens[4].Data);

    Assert.True(tokens[5].IsKeyword(KeywordType.Match));
    Assert.Equal("match", tokens[5].Data);

    Assert.True(tokens[6].IsKeyword(KeywordType.When));
    Assert.Equal("when", tokens[6].Data);

    Assert.True(tokens[7].IsKeyword(KeywordType.Loop));
    Assert.Equal("for", tokens[7].Data);

    Assert.True(tokens[8].IsKeyword(KeywordType.Struct));
    Assert.Equal("struct", tokens[8].Data);

    Assert.True(tokens[9].IsKeyword(KeywordType.Enum));
    Assert.Equal("enum", tokens[9].Data);

    Assert.True(tokens[10].IsKeyword(KeywordType.Break));
    Assert.Equal("break", tokens[10].Data);

    Assert.True(tokens[11].IsKeyword(KeywordType.Continue));
    Assert.Equal("continue", tokens[11].Data);

    Assert.True(tokens[12].IsKeyword(KeywordType.Return));
    Assert.Equal("return", tokens[12].Data);

    Assert.Equal(TokenType.Identifier, tokens[13].Type);
    Assert.Equal("identifier_name", tokens[13].Data);
  }

  [Fact]
  public void TestOperators() {
    var input = "= + - * / % == != < <= > >= and or not : :: += -= *= /= %=";
    var tokens = Lexer.LexLine(input);

    Assert.Equal(22, tokens.Count);

    Assert.True(tokens[0].IsOperator(OperatorType.Assignment));
    Assert.True(tokens[1].IsOperator(OperatorType.Addition));
    Assert.True(tokens[2].IsOperator(OperatorType.Subtraction));
    Assert.True(tokens[3].IsOperator(OperatorType.Multiplication));
    Assert.True(tokens[4].IsOperator(OperatorType.Division));
    Assert.True(tokens[5].IsOperator(OperatorType.Modulus));
    Assert.True(tokens[6].IsOperator(OperatorType.Equal));
    Assert.True(tokens[7].IsOperator(OperatorType.NotEqual));
    Assert.True(tokens[8].IsOperator(OperatorType.LessThan));
    Assert.True(tokens[9].IsOperator(OperatorType.LessThanEqual));
    Assert.True(tokens[10].IsOperator(OperatorType.GreaterThan));
    Assert.True(tokens[11].IsOperator(OperatorType.GreaterThanEqual));
    Assert.True(tokens[12].IsOperator(OperatorType.And));
    Assert.True(tokens[13].IsOperator(OperatorType.Or));
    Assert.True(tokens[14].IsOperator(OperatorType.Not));
    Assert.True(tokens[15].IsOperator(OperatorType.MemberAccess));
    Assert.True(tokens[16].IsOperator(OperatorType.StaticMemberAccess));
    Assert.True(tokens[17].IsOperator(OperatorType.AdditionAssignment));
    Assert.True(tokens[18].IsOperator(OperatorType.SubtractionAssignment));
    Assert.True(tokens[19].IsOperator(OperatorType.MultiplicationAssignment));
    Assert.True(tokens[20].IsOperator(OperatorType.DivisionAssignment));
    Assert.True(tokens[21].IsOperator(OperatorType.ModulusAssignment));
  }

  [Fact]
  public void TestLiterals() {
    var input = "42 3.14 true false \"hello world\"";
    var tokens = Lexer.LexLine(input);

    Assert.Equal(5, tokens.Count);

    Assert.Equal(TokenType.Literal, tokens[0].Type);
    Assert.Equal(LiteralType.Integer, tokens[0].Literal);
    Assert.Equal("42", tokens[0].Data);

    Assert.Equal(TokenType.Literal, tokens[1].Type);
    Assert.Equal(LiteralType.Float, tokens[1].Literal);
    Assert.Equal("3.14", tokens[1].Data);

    Assert.Equal(TokenType.Literal, tokens[2].Type);
    Assert.Equal(LiteralType.Boolean, tokens[2].Literal);
    Assert.Equal("true", tokens[2].Data);

    Assert.Equal(TokenType.Literal, tokens[3].Type);
    Assert.Equal(LiteralType.Boolean, tokens[3].Literal);
    Assert.Equal("false", tokens[3].Data);

    Assert.Equal(TokenType.Literal, tokens[4].Type);
    Assert.Equal(LiteralType.String, tokens[4].Literal);
    Assert.Equal("hello world", tokens[4].Data);
  }

  [Fact]
  public void TestStringInterpolation() {
    var input = "\"hello $name inside $room\"";
    var tokens = Lexer.LexLine(input);

    Assert.Equal(7, tokens.Count);
    
    Assert.Equal(TokenType.Literal, tokens[0].Type);
    Assert.Equal(LiteralType.String, tokens[0].Literal);
    Assert.Equal("hello ", tokens[0].Data);

    Assert.True(tokens[1].IsOperator(OperatorType.Addition));

    Assert.Equal(TokenType.Identifier, tokens[2].Type);
    Assert.Equal("name", tokens[2].Data);

    Assert.True(tokens[3].IsOperator(OperatorType.Addition));

    Assert.Equal(TokenType.Literal, tokens[4].Type);
    Assert.Equal(LiteralType.String, tokens[4].Literal);
    Assert.Equal(" inside ", tokens[4].Data);

    Assert.True(tokens[5].IsOperator(OperatorType.Addition));

    Assert.Equal(TokenType.Identifier, tokens[6].Type);
    Assert.Equal("room", tokens[6].Data);
  }

  [Fact]
  public void TestComplexStringInterpolation() {
    var input = "\"Base Attack: $(player:attack)\"";
    var tokens = Lexer.LexLine(input);

    Assert.Equal(7, tokens.Count);

    Assert.Equal(TokenType.Literal, tokens[0].Type);
    Assert.Equal(LiteralType.String, tokens[0].Literal);
    Assert.Equal("Base Attack: ", tokens[0].Data);

    Assert.True(tokens[1].IsOperator(OperatorType.Addition));

    Assert.True(tokens[2].IsMarker(MarkerType.ParenthesisBegin));

    Assert.Equal(TokenType.Identifier, tokens[3].Type);
    Assert.Equal("player", tokens[3].Data);

    Assert.True(tokens[4].IsOperator(OperatorType.MemberAccess));

    Assert.Equal(TokenType.Identifier, tokens[5].Type);
    Assert.Equal("attack", tokens[5].Data);

    Assert.True(tokens[6].IsMarker(MarkerType.ParenthesisEnd));
  }

  [Fact]
  public void TestSkipCommentsAndWhitespace() {
    var input = "   var  # this is a comment";
    var tokens = Lexer.LexLine(input);

    Assert.Single(tokens);
    Assert.True(tokens[0].IsKeyword(KeywordType.Variable));
  }

  [Fact]
  public void TestComplexStatement() {
    var input = "var pos = Coordinates { x: 10, y: 20 }";
    var tokens = Lexer.LexLine(input);

    Assert.Equal(13, tokens.Count);

    Assert.True(tokens[0].IsKeyword(KeywordType.Variable));
    Assert.Equal(TokenType.Identifier, tokens[1].Type);
    Assert.Equal("pos", tokens[1].Data);
    Assert.True(tokens[2].IsOperator(OperatorType.Assignment));
    Assert.Equal(TokenType.Identifier, tokens[3].Type);
    Assert.Equal("Coordinates", tokens[3].Data);
    Assert.True(tokens[4].IsMarker(MarkerType.BlockBegin));
    Assert.Equal(TokenType.Identifier, tokens[5].Type);
    Assert.Equal("x", tokens[5].Data);
    Assert.True(tokens[6].IsOperator(OperatorType.MemberAccess));
    Assert.Equal(TokenType.Literal, tokens[7].Type);
    Assert.Equal("10", tokens[7].Data);
    Assert.True(tokens[8].IsMarker(MarkerType.Comma));
    Assert.Equal(TokenType.Identifier, tokens[9].Type);
    Assert.Equal("y", tokens[9].Data);
    Assert.True(tokens[10].IsOperator(OperatorType.MemberAccess));
    Assert.Equal(TokenType.Literal, tokens[11].Type);
    Assert.Equal("20", tokens[11].Data);
    Assert.True(tokens[12].IsMarker(MarkerType.BlockEnd));
  }

  [Fact]
  public void TestRuneLiterals() {
    var input = "'a' '🌲' '\\n' '\\''";
    var tokens = Lexer.LexLine(input);

    Assert.Equal(4, tokens.Count);

    Assert.Equal(TokenType.Literal, tokens[0].Type);
    Assert.Equal(LiteralType.Rune, tokens[0].Literal);
    Assert.Equal("97", tokens[0].Data); // 'a' code point is 97

    Assert.Equal(TokenType.Literal, tokens[1].Type);
    Assert.Equal(LiteralType.Rune, tokens[1].Literal);
    Assert.Equal("127794", tokens[1].Data); // '🌲' code point is 127794

    Assert.Equal(TokenType.Literal, tokens[2].Type);
    Assert.Equal(LiteralType.Rune, tokens[2].Literal);
    Assert.Equal("10", tokens[2].Data); // '\n' code point is 10

    Assert.Equal(TokenType.Literal, tokens[3].Type);
    Assert.Equal(LiteralType.Rune, tokens[3].Literal);
    Assert.Equal("39", tokens[3].Data); // '\'' code point is 39
  }

  [Fact]
  public void TestAtAndIsTokens() {
    var input = "@is @generic T is DocumentShape";
    var tokens = Lexer.LexLine(input);

    Assert.Equal(7, tokens.Count);

    Assert.True(tokens[0].IsMarker(MarkerType.At));
    Assert.True(tokens[1].IsKeyword(KeywordType.Is));

    Assert.True(tokens[2].IsMarker(MarkerType.At));
    Assert.Equal(TokenType.Identifier, tokens[3].Type);
    Assert.Equal("generic", tokens[3].Data);

    Assert.Equal(TokenType.Identifier, tokens[4].Type);
    Assert.Equal("T", tokens[4].Data);

    Assert.True(tokens[5].IsKeyword(KeywordType.Is));

    Assert.Equal(TokenType.Identifier, tokens[6].Type);
    Assert.Equal("DocumentShape", tokens[6].Data);
  }
}
