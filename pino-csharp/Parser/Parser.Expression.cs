using System;
using System.Collections.Generic;

namespace Pino;

public partial class Parser {
  private static bool IsExpression(TokenStream stream) {
    var current = stream.Current;
    return IsFunctionLambda(stream) ||
           IsVector(stream) ||
           current.IsKeyword(KeywordType.If) ||
           current.IsKeyword(KeywordType.Match) ||
           current.IsMarker(MarkerType.ParenthesisBegin) ||
           (current.IsMarker(MarkerType.At) && stream.Peek(1).IsMarker(MarkerType.ParenthesisBegin)) ||
           current.IsType(TokenType.Identifier, TokenType.Literal);
  }

  private static bool IsFunctionCall(TokenStream stream) {
    if (!stream.Current.IsType(TokenType.Identifier)) return false;
    if (stream.IsNext(t => t.IsMarker(MarkerType.ParenthesisBegin))) return true;
    if (stream.IsNext(t => t.IsMarker(MarkerType.BracketBegin))) {
      int offset = 2; // skip identifier and '['
      int bracketDepth = 1;
      while (true) {
        var t = stream.Peek(offset);
        if (t.Type == TokenType.Illegal) return false;
        if (t.IsMarker(MarkerType.BracketBegin)) bracketDepth++;
        else if (t.IsMarker(MarkerType.BracketEnd)) {
          bracketDepth--;
          if (bracketDepth == 0) {
            return stream.Peek(offset + 1).IsMarker(MarkerType.ParenthesisBegin);
          }
        }
        offset++;
      }
    }
    return false;
  }

  private static bool IsFunctionLambda(TokenStream stream) {
    return stream.Current.IsKeyword(KeywordType.Function) && stream.IsNext(t => t.IsMarker(MarkerType.ParenthesisBegin) || t.IsMarker(MarkerType.BlockBegin));
  }

  private static bool IsStructBlock(TokenStream stream, int braceOffset) {
    if (!stream.Peek(braceOffset).IsMarker(MarkerType.BlockBegin)) {
      return false;
    }

    int nested = 0;
    int offset = braceOffset;
    bool hasPropInit = false;
    bool afterColon = false;
    int parenDepth = 0;
    int bracketDepth = 0;

    while (true) {
      var tok = stream.Peek(offset);
      if (tok.Type == TokenType.Illegal) {
        break;
      }
      if (tok.IsMarker(MarkerType.BlockBegin)) {
        nested++;
      } else if (tok.IsMarker(MarkerType.BlockEnd)) {
        nested--;
        if (nested == 0) {
          break;
        }
      } else if (nested == 1) {
        if (tok.IsMarker(MarkerType.ParenthesisBegin)) {
          parenDepth++;
        } else if (tok.IsMarker(MarkerType.ParenthesisEnd)) {
          parenDepth--;
        } else if (tok.IsMarker(MarkerType.BracketBegin)) {
          bracketDepth++;
        } else if (tok.IsMarker(MarkerType.BracketEnd)) {
          bracketDepth--;
        } else if (tok.IsMarker(MarkerType.Comma)) {
          if (parenDepth == 0 && bracketDepth == 0) {
            afterColon = false;
          }
        }
        else if (tok.IsOperator(OperatorType.MemberAccess)) {
          afterColon = true;
        }
        else if (tok.Type == TokenType.Keyword) {
          if (!afterColon) {
            return false;
          }
        }
        else if (tok.Type == TokenType.Operator) {
          var op = tok.Operator;
          if (op == OperatorType.Assignment ||
              op == OperatorType.AdditionAssignment ||
              op == OperatorType.SubtractionAssignment ||
              op == OperatorType.MultiplicationAssignment ||
              op == OperatorType.DivisionAssignment ||
              op == OperatorType.ModulusAssignment) {
            if (!afterColon) {
              return false;
            }
          }
        }
        else if (tok.Type == TokenType.Identifier) {
          var nextTok = stream.Peek(offset + 1);
          if (nextTok.IsOperator(OperatorType.MemberAccess)) {
            hasPropInit = true;
          }
        }
      }
      offset++;
    }
    return hasPropInit;
  }

  private static bool IsStructInstance(TokenStream stream) {
    var current = stream.Current;
    if (!current.IsType(TokenType.Identifier) || !char.IsUpper(current.Data[0])) {
      return false;
    }
    int offset = 1;
    if (stream.Peek(offset).IsMarker(MarkerType.BracketBegin)) {
      int bracketDepth = 0;
      while (stream.Peek(offset).Type != TokenType.Illegal) {
        var t = stream.Peek(offset);
        if (t.IsMarker(MarkerType.BracketBegin)) {
          bracketDepth++;
        } else if (t.IsMarker(MarkerType.BracketEnd)) {
          bracketDepth--;
          if (bracketDepth == 0) {
            offset++;
            break;
          }
        }
        offset++;
      }
    }
    if (!stream.Peek(offset).IsMarker(MarkerType.BlockBegin)) {
      return false;
    }
    if (stream.Peek(offset + 1).IsMarker(MarkerType.BlockEnd)) {
      return true;
    }
    return IsStructBlock(stream, offset);
  }

  private static bool IsVector(TokenStream stream) {
    return stream.Current.IsMarker(MarkerType.BracketBegin);
  }

  private static Expression ParseExpression(TokenStream stream, bool allowStruct = true, bool allowMemberAccess = true, bool allowIn = true) {
    return ParseExpressionWithPrecedence(stream, 0, allowStruct, allowMemberAccess, allowIn);
  }

  private static Expression ParseExpressionWithPrecedence(TokenStream stream, int minPrecedence, bool allowStruct = true, bool allowMemberAccess = true, bool allowIn = true) {
    var expression = ParsePrimaryExpression(stream, allowStruct, allowMemberAccess);

    while (stream.HasNext && (stream.Current.Type == TokenType.Operator || stream.Current.IsKeyword(KeywordType.In)) && !stream.Current.IsOperator(OperatorType.QuestionMark)) {
      var opToken = stream.Current;
      var opType = opToken.IsKeyword(KeywordType.In) ? OperatorType.In : opToken.Operator!.Value;
      if (opType == OperatorType.MemberAccess && !allowMemberAccess) {
        break;
      }
      if (opType == OperatorType.In && !allowIn) {
        break;
      }
      var precedence = GetOperatorPrecedence(opType);

      if (precedence < minPrecedence) {
        break;
      }

      if (opType == OperatorType.Or && stream.Peek(1).IsMarker(MarkerType.BlockBegin)) {
        stream.Consume(); // consume 'or'
        var body = ParseBlock(stream);
        expression = new RecoveryExpression(expression, body);
        continue;
      }

      stream.Consume(); // consume operator

      var nextMinPrecedence = IsRightAssociative(opType) ? precedence : precedence + 1;
      var right = ParseExpressionWithPrecedence(stream, nextMinPrecedence, allowStruct, allowMemberAccess, allowIn);
      expression = new BinaryExpression(expression, opType, right);
    }

    return expression;
  }

  private static Expression ParsePrimaryExpression(TokenStream stream, bool allowStruct = true, bool allowMemberAccess = true) {
    if (stream.Current.IsOperator(OperatorType.Not) || stream.Current.IsOperator(OperatorType.Subtraction)) {
      var op = stream.Consume().Operator!.Value;
      var right = ParseExpressionWithPrecedence(stream, 8, allowStruct, allowMemberAccess);
      return new UnaryExpression(op, right);
    }

    Expression expr;

    if (stream.Current.IsMarker(MarkerType.At) && stream.Peek(1).IsMarker(MarkerType.ParenthesisBegin)) {
      stream.Consume(); // consume '@'
      stream.Consume(); // consume '('
      var fields = new List<TupleField>();
      while (!stream.Current.IsMarker(MarkerType.ParenthesisEnd)) {
        var label = ConsumeIdentifier(stream);
        Expression val;
        if (stream.Current.IsOperator(OperatorType.MemberAccess)) {
          stream.Consume(); // consume ':'
          val = ParseExpression(stream);
        } else {
          val = new IdentifierExpression(label);
        }
        fields.Add(new TupleField(label, val));
        if (stream.Current.IsMarker(MarkerType.Comma)) {
          stream.Consume();
        }
      }
      if (!stream.Consume().IsMarker(MarkerType.ParenthesisEnd)) {
        throw new Exception("PARSER: Expected ')' to close tuple literal");
      }
      expr = new TupleLiteralExpression(fields);
    } else if (stream.Current.IsMarker(MarkerType.ParenthesisBegin)) {
      stream.Consume(); // consume '('
      expr = ParseExpression(stream, true, true);
      if (!stream.Consume().IsMarker(MarkerType.ParenthesisEnd)) {
        throw new Exception("PARSER: Expected ')' to close grouped expression");
      }
    } else if (IsFunctionCall(stream)) {
      expr = ParseFunctionCall(stream);
    } else if (stream.Current.Type == TokenType.Identifier && stream.Current.Data == "map" && stream.IsNext(t => t.IsMarker(MarkerType.BracketBegin))) {
      expr = ParseMapExpression(stream);
    } else if (IsFunctionLambda(stream)) {
      expr = ParseFunctionLambda(stream);
    } else if (IsVector(stream)) {
      expr = ParseVector(stream);
    } else if (allowStruct && IsStructInstance(stream)) {
      expr = ParseStructInstance(stream);
    } else if (stream.Current.IsKeyword(KeywordType.If)) {
      expr = ParseTernaryExpression(stream);
    } else if (stream.Current.IsKeyword(KeywordType.Match)) {
      expr = ParseMatchStatement(stream);
    } else if (stream.Current.Type == TokenType.Identifier) {
      expr = new IdentifierExpression(stream.Consume().Data);
    } else if (stream.Current.Type == TokenType.Literal) {
      var t = stream.Consume();
      expr = new LiteralExpression(t.Data, t.Literal!.Value, t.Injections);
    } else {
      throw new Exception($"PARSER: Expected expression, got {stream.Current}");
    }

    while (stream.HasNext) {
      if (stream.Current.IsMarker(MarkerType.BracketBegin)) {
        stream.Consume(); // consume '['
        var indexExpr = ParseExpression(stream);
        if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
          throw new Exception("PARSER: Expected ']' to close index access");
        }
        expr = new IndexAccessExpression(expr, indexExpr);
      } else if (stream.Current.IsOperator(OperatorType.MemberAccess)) {
        if (!allowMemberAccess) {
          break;
        }
        stream.Consume(); // consume ':'
        var memberName = ConsumeIdentifier(stream);
        Expression rightSide;
        if (stream.Current.IsMarker(MarkerType.ParenthesisBegin) || stream.Current.IsMarker(MarkerType.BracketBegin)) {
          List<string>? genericArgs = null;
          if (stream.Current.IsMarker(MarkerType.BracketBegin)) {
            stream.Consume(); // consume '['
            genericArgs = new List<string>();
            while (!stream.Current.IsMarker(MarkerType.BracketEnd)) {
              genericArgs.Add(ConsumeTyping(stream));
              if (stream.Current.IsMarker(MarkerType.Comma)) {
                stream.Consume();
              }
            }
            if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
              throw new Exception("PARSER: Expected ']' to close generic arguments in member call");
            }
          }
          var args = ConsumeArguments(stream);
          rightSide = new FunctionCallExpression(memberName, args, genericArgs);
        } else {
          rightSide = new IdentifierExpression(memberName);
        }
        expr = new BinaryExpression(expr, OperatorType.MemberAccess, rightSide);
      } else if (stream.Current.IsOperator(OperatorType.StaticMemberAccess)) {
        stream.Consume(); // consume '::'
        Expression rightSide;
        if (allowStruct && IsStructInstance(stream)) {
          rightSide = ParseStructInstance(stream);
        } else if (IsFunctionCall(stream)) {
          rightSide = ParseFunctionCall(stream);
        } else {
          var memberName = ConsumeIdentifier(stream);
          rightSide = new IdentifierExpression(memberName);
        }
        expr = new BinaryExpression(expr, OperatorType.StaticMemberAccess, rightSide);
      } else if (stream.Current.IsOperator(OperatorType.QuestionMark)) {
        stream.Consume(); // consume '?'
        expr = new BubbleExpression(expr);
      } else {
        break;
      }
    }

    return expr;
  }

  private static int GetOperatorPrecedence(OperatorType op) {
    return op switch {
      OperatorType.Assignment => 1,
      OperatorType.AdditionAssignment => 1,
      OperatorType.SubtractionAssignment => 1,
      OperatorType.MultiplicationAssignment => 1,
      OperatorType.DivisionAssignment => 1,
      OperatorType.ModulusAssignment => 1,

      OperatorType.Or => 2,
      OperatorType.And => 3,

      OperatorType.Equal => 4,
      OperatorType.NotEqual => 4,

      OperatorType.LessThan => 5,
      OperatorType.LessThanEqual => 5,
      OperatorType.GreaterThan => 5,
      OperatorType.GreaterThanEqual => 5,
      OperatorType.In => 5,

      OperatorType.Addition => 6,
      OperatorType.Subtraction => 6,

      OperatorType.Multiplication => 7,
      OperatorType.Division => 7,
      OperatorType.Modulus => 7,

      OperatorType.MemberAccess => 8,
      OperatorType.StaticMemberAccess => 8,

      _ => 0
    };
  }

  private static bool IsRightAssociative(OperatorType op) {
    return op == OperatorType.Assignment ||
           op == OperatorType.AdditionAssignment ||
           op == OperatorType.SubtractionAssignment ||
           op == OperatorType.MultiplicationAssignment ||
           op == OperatorType.DivisionAssignment ||
           op == OperatorType.ModulusAssignment;
  }

  private static FunctionCallExpression ParseFunctionCall(TokenStream stream) {
    var callee = ConsumeIdentifier(stream);
    List<string>? genericArgs = null;
    if (stream.Current.IsMarker(MarkerType.BracketBegin)) {
      stream.Consume(); // consume '['
      genericArgs = new List<string>();
      while (!stream.Current.IsMarker(MarkerType.BracketEnd)) {
        genericArgs.Add(ConsumeTyping(stream));
        if (stream.Current.IsMarker(MarkerType.Comma)) {
          stream.Consume();
        }
      }
      if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
        throw new Exception("PARSER: Expected ']' to close generic arguments in function call");
      }
    }
    var args = ConsumeArguments(stream);
    return new FunctionCallExpression(callee, args, genericArgs);
  }

  private static FunctionLambdaExpression ParseFunctionLambda(TokenStream stream) {
    if (!stream.Consume().IsKeyword(KeywordType.Function)) {
      throw new Exception("PARSER: Expected 'fn' keyword for lambda");
    }

    var parameters = ConsumeParameters(stream);

    PushScope(stream);
    foreach (var param in parameters) {
      DeclareVariable(stream, param.Identifier);
    }

    Statement body;
    if (stream.Current.IsOperator(OperatorType.Arrow)) {
      stream.Consume(); // consume '=>'
      var expr = ParseExpression(stream);
      body = new BlockStatement(new List<Statement> { new ReturnStatement(expr) });
    } else {
      body = ParseBlock(stream);
    }
    PopScope(stream);

    return new FunctionLambdaExpression(parameters, body);
  }

  private static VectorExpression ParseVector(TokenStream stream) {
    if (!stream.Consume().IsMarker(MarkerType.BracketBegin)) {
      throw new Exception("PARSER: Expected '[' for vector");
    }

    // Empty vector `[]type` or vector init block `[]type { len: limit, init: expr }`
    if (stream.Current.IsMarker(MarkerType.BracketEnd)) {
      stream.Consume(); // consume ']'
      var typing = ConsumeTyping(stream);

      if (stream.Current.IsMarker(MarkerType.BlockBegin)) {
        var props = ConsumeProperties(stream);
        VariableDeclaration? len = props.Find(p => p.Identifier == "len");
        VariableDeclaration? init = props.Find(p => p.Identifier == "init");

        if (len == null || init == null) {
          throw new Exception("PARSER: Empty or invalid vector init block properties");
        }

        return new VectorExpression(null, len.Value, init.Value, typing);
      }

      return new VectorExpression(null, Typing: typing);
    }

    // Literal elements: `[1, 2, 3]`
    var elements = new List<Expression>();
    while (stream.HasNext) {
      if (stream.Current.IsMarker(MarkerType.BracketEnd)) {
        stream.Consume(); // consume ']'
        return new VectorExpression(elements);
      }

      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
        continue;
      }

      elements.Add(ParseExpression(stream));
    }

    throw new Exception("PARSER: Expected ']'");
  }

  private static StructInstanceExpression ParseStructInstance(TokenStream stream) {
    var structName = ConsumeIdentifier(stream);
    List<string> genericArgs = null;
    if (stream.Current.IsMarker(MarkerType.BracketBegin)) {
      stream.Consume(); // consume '['
      genericArgs = new List<string>();
      while (!stream.Current.IsMarker(MarkerType.BracketEnd)) {
        genericArgs.Add(ConsumeTyping(stream));
        if (stream.Current.IsMarker(MarkerType.Comma)) {
          stream.Consume();
        }
      }
      if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
        throw new Exception("PARSER: Expected ']' to close generic arguments");
      }
    }
    var props = ConsumeProperties(stream);
    return new StructInstanceExpression(structName, props, genericArgs);
  }

  private static TernaryExpression ParseTernaryExpression(TokenStream stream) {
    if (!stream.Consume().IsKeyword(KeywordType.If)) {
      throw new Exception("PARSER: Expected 'if' in ternary expression");
    }

    var condition = ParseExpression(stream);

    if (!stream.Consume().IsKeyword(KeywordType.Then)) {
      throw new Exception("PARSER: Expected 'then' in ternary expression");
    }

    var consequent = ParseExpression(stream);

    if (!stream.Consume().IsKeyword(KeywordType.Else)) {
      throw new Exception("PARSER: Expected 'else' in ternary expression");
    }

    var alternate = ParseExpression(stream);

    return new TernaryExpression(condition, consequent, alternate);
  }

  private static MapExpression ParseMapExpression(TokenStream stream) {
    var mapTok = stream.Consume();
    if (mapTok.Type != TokenType.Identifier || mapTok.Data != "map") {
      throw new Exception("PARSER: Expected 'map' identifier");
    }

    if (!stream.Consume().IsMarker(MarkerType.BracketBegin)) {
      throw new Exception("PARSER: Expected '[' after 'map'");
    }

    var keyType = ConsumeTyping(stream);

    if (!stream.Consume().IsMarker(MarkerType.Comma)) {
      throw new Exception("PARSER: Expected ',' separator in map types");
    }

    var valType = ConsumeTyping(stream);

    if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
      throw new Exception("PARSER: Expected ']' after map types");
    }

    if (!stream.Consume().IsMarker(MarkerType.BlockBegin)) {
      throw new Exception("PARSER: Expected '{' to start map initializer");
    }

    var entries = new List<KeyValuePair<Expression, Expression>>();
    while (stream.HasNext) {
      if (stream.Current.IsMarker(MarkerType.BlockEnd)) {
        stream.Consume(); // consume '}'
        break;
      }

      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
        continue;
      }

      var keyExpr = ParseExpression(stream, allowStruct: true, allowMemberAccess: false);
      if (!stream.Consume().IsOperator(OperatorType.MemberAccess)) {
        throw new Exception("PARSER: Expected ':' after map key expression");
      }
      var valExpr = ParseExpression(stream);

      entries.Add(new KeyValuePair<Expression, Expression>(keyExpr, valExpr));
    }

    return new MapExpression(keyType, valType, entries);
  }
}
