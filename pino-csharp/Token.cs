using System.Collections.Generic;

namespace Pino;

public enum TokenType {
  Identifier,
  Illegal,
  Keyword,
  Literal,
  Marker,
  Operator
}

public enum KeywordType {
  As, Break, Constant, Continue, Else, Enum, From, Function, If, Import, In, Interface, Is, Loop, Map, Match, Module, Pub, Return, Static, Struct, Then, Variable, When, Union, Yield, Test, Assert
}

public enum LiteralType {
  Boolean, Float, Integer, String, Rune
}

public enum MarkerType {
  At, BlockBegin, BlockEnd, BracketBegin, BracketEnd, Comma, Comment, ParenthesisBegin, ParenthesisEnd, StrQuote
}

public enum OperatorType {
  Assignment, Addition, AdditionAssignment, Subtraction, SubtractionAssignment,
  Multiplication, MultiplicationAssignment, Division, DivisionAssignment, Modulus, ModulusAssignment,
  LessThan, LessThanEqual, GreaterThan, GreaterThanEqual, Equal, NotEqual, And, Or, Not, MemberAccess, StaticMemberAccess, Arrow, In, QuestionMark
}

public record Token(
    TokenType Type,
    string Data,
    KeywordType? Keyword = null,
    LiteralType? Literal = null,
    MarkerType? Marker = null,
    OperatorType? Operator = null,
    List<string>? Injections = null
) {
  public bool IsType(TokenType type) => Type == type;
  public bool IsType(TokenType typeA, TokenType typeB) => Type == typeA || Type == typeB;

  public bool IsKeyword(KeywordType keyword) => Type == TokenType.Keyword && Keyword == keyword;
  public bool IsMarker(MarkerType marker) => Type == TokenType.Marker && Marker == marker;
  public bool IsOperator(OperatorType op) => Type == TokenType.Operator && Operator == op;

  public override string ToString() {
    var extra = "";
    if (Keyword != null) extra = $" ({Keyword})";
    else if (Literal != null) extra = $" ({Literal})";
    else if (Marker != null) extra = $" ({Marker})";
    else if (Operator != null) extra = $" ({Operator})";
    return $"{Type}: {Data}{extra}";
  }
}
