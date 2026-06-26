using System;
using System.Collections.Generic;

namespace Pino;

public partial class Parser {
  private static bool IsExpression(TokenStream stream) {
    var current = stream.Current;
    return IsFunctionLambda(stream) ||
           IsVector(stream) ||
           current.IsKeyword(KeywordType.If) ||
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
      if (stream.Peek(-1).IsOperator(OperatorType.StaticMemberAccess)) {
        return false;
      }
      return true;
    }
    return IsStructBlock(stream, offset);
  }

  private static bool IsVector(TokenStream stream) {
    return stream.Current.IsMarker(MarkerType.BracketBegin);
  }

  private static Expression ParseExpression(TokenStream stream, bool allowStruct = true, bool allowMemberAccess = true, bool allowIn = true) {
    var startToken = stream.Current;
    var expr = ParseExpressionInternal(stream, allowStruct, allowMemberAccess, allowIn);
    if (expr != null && expr.Token == null) {
      expr.Token = startToken;
    }
    return expr;
  }

  private static Expression ParseExpressionInternal(TokenStream stream, bool allowStruct = true, bool allowMemberAccess = true, bool allowIn = true) {
    return ParseExpressionWithPrecedence(stream, 0, allowStruct, allowMemberAccess, allowIn);
  }

  private static Expression ParseExpressionWithPrecedence(TokenStream stream, int minPrecedence, bool allowStruct = true, bool allowMemberAccess = true, bool allowIn = true) {
    var startToken = stream.Current;
    var expression = ParsePrimaryExpression(stream, allowStruct, allowMemberAccess);
    if (expression != null && expression.Token == null) {
      expression.Token = startToken;
    }

    while (stream.HasNext && (stream.Current.Type == TokenType.Operator || stream.Current.IsKeyword(KeywordType.In))) {
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

      stream.Consume(); // consume operator

      var nextMinPrecedence = IsRightAssociative(opType) ? precedence : precedence + 1;
      var right = ParseExpressionWithPrecedence(stream, nextMinPrecedence, allowStruct, allowMemberAccess, allowIn);
      var binaryExpr = new BinaryExpression(expression, opType, right);
      binaryExpr.Token = opToken;
      expression = binaryExpr;
    }

    return expression;
  }

  private static Expression ParsePrimaryExpression(TokenStream stream, bool allowStruct = true, bool allowMemberAccess = true) {
    Expression expr;
    var startToken = stream.Current;

    if (stream.Current.IsMarker(MarkerType.ParenthesisBegin)) {
      stream.Consume();
      expr = ParseExpression(stream, true, true);
      if (!stream.Consume().IsMarker(MarkerType.ParenthesisEnd)) {
        throw Error(stream, "Expected ')' to close grouped expression");
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
    } else if (stream.Current.Type == TokenType.Identifier) {
      expr = new IdentifierExpression(stream.Consume().Data);
    } else if (stream.Current.Type == TokenType.Literal) {
      var t = stream.Consume();
      expr = new LiteralExpression(t.Data, t.Literal!.Value, t.Injections);
    } else {
      throw Error(stream, $"Expected expression, got '{stream.Current.Data}'");
    }

    if (expr != null && expr.Token == null) {
      expr.Token = startToken;
    }

    while (stream.HasNext) {
      var opTok = stream.Current;
      if (stream.Current.IsMarker(MarkerType.BracketBegin)) {
        stream.Consume(); // consume '['
        var indexExpr = ParseExpression(stream);
        if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
          throw Error(stream, "Expected ']' to close index access");
        }
        var nextExpr = new IndexAccessExpression(expr, indexExpr);
        nextExpr.Token = opTok;
        expr = nextExpr;
      } else if (stream.Current.IsOperator(OperatorType.MemberAccess)) {
        if (!allowMemberAccess) {
          break;
        }
        stream.Consume(); // consume ':'
        var memberName = ConsumeIdentifier(stream);
        Expression rightSide;
        var rTok = stream.Current;
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
              throw Error(stream, "Expected ']' to close generic arguments in member call");
            }
          }
          var args = ConsumeArguments(stream);
          var callExpr = new FunctionCallExpression(memberName, args, genericArgs);
          callExpr.Token = rTok;
          rightSide = callExpr;
        } else {
          var idExpr = new IdentifierExpression(memberName);
          idExpr.Token = rTok;
          rightSide = idExpr;
        }
        var nextExpr = new BinaryExpression(expr, OperatorType.MemberAccess, rightSide);
        nextExpr.Token = opTok;
        expr = nextExpr;
      } else if (stream.Current.IsOperator(OperatorType.StaticMemberAccess)) {
        stream.Consume(); // consume '::'
        Expression rightSide;
        var rTok = stream.Current;
        if (allowStruct && IsStructInstance(stream)) {
          rightSide = ParseStructInstance(stream);
        } else if (IsFunctionCall(stream)) {
          rightSide = ParseFunctionCall(stream);
        } else {
          var memberName = ConsumeIdentifier(stream);
          var idExpr = new IdentifierExpression(memberName);
          idExpr.Token = rTok;
          rightSide = idExpr;
        }
        var nextExpr = new BinaryExpression(expr, OperatorType.StaticMemberAccess, rightSide);
        nextExpr.Token = opTok;
        expr = nextExpr;
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
    var startToken = stream.Current;
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
        throw Error(stream, "Expected ']' to close generic arguments in function call");
      }
    }
    var args = ConsumeArguments(stream);
    var call = new FunctionCallExpression(callee, args, genericArgs);
    call.Token = startToken;
    return call;
  }

  private static FunctionLambdaExpression ParseFunctionLambda(TokenStream stream) {
    var startToken = stream.Current;
    if (!stream.Consume().IsKeyword(KeywordType.Function)) {
      throw Error(stream, "Expected 'fn' keyword for lambda");
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

    var lambda = new FunctionLambdaExpression(parameters, body);
    lambda.Token = startToken;
    return lambda;
  }

  private static VectorExpression ParseVector(TokenStream stream) {
    var startToken = stream.Current;
    if (!stream.Consume().IsMarker(MarkerType.BracketBegin)) {
      throw Error(stream, "Expected '[' for vector");
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
          throw Error(stream, "Empty or invalid vector init block properties");
        }

        var vec = new VectorExpression(null, len.Value, init.Value, typing);
        vec.Token = startToken;
        return vec;
      }

      var emptyVec = new VectorExpression(null, Typing: typing);
      emptyVec.Token = startToken;
      return emptyVec;
    }

    // Literal elements: `[1, 2, 3]`
    var elements = new List<Expression>();
    while (stream.HasNext) {
      if (stream.Current.IsMarker(MarkerType.BracketEnd)) {
        stream.Consume(); // consume ']'
        var vec = new VectorExpression(elements);
        vec.Token = startToken;
        return vec;
      }

      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
        continue;
      }

      elements.Add(ParseExpression(stream));
    }

    throw Error(stream, "Expected ']'");
  }

  private static StructInstanceExpression ParseStructInstance(TokenStream stream) {
    var startToken = stream.Current;
    var structName = ConsumeIdentifier(stream);
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
        throw Error(stream, "Expected ']' to close generic arguments");
      }
    }
    var props = ConsumeProperties(stream);
    var inst = new StructInstanceExpression(structName, props, genericArgs);
    inst.Token = startToken;
    return inst;
  }

  private static TernaryExpression ParseTernaryExpression(TokenStream stream) {
    var startToken = stream.Current;
    if (!stream.Consume().IsKeyword(KeywordType.If)) {
      throw Error(stream, "Expected 'if' in ternary expression");
    }

    var condition = ParseExpression(stream);

    if (!stream.Consume().IsKeyword(KeywordType.Then)) {
      throw Error(stream, "Expected 'then' in ternary expression");
    }

    var consequent = ParseExpression(stream);

    if (!stream.Consume().IsKeyword(KeywordType.Else)) {
      throw Error(stream, "Expected 'else' in ternary expression");
    }

    var alternate = ParseExpression(stream);

    var tern = new TernaryExpression(condition, consequent, alternate);
    tern.Token = startToken;
    return tern;
  }

  private static MapExpression ParseMapExpression(TokenStream stream) {
    var startToken = stream.Current;
    var mapTok = stream.Consume();
    if (mapTok.Type != TokenType.Identifier || mapTok.Data != "map") {
      throw Error(stream, mapTok, "Expected 'map' identifier");
    }

    if (!stream.Consume().IsMarker(MarkerType.BracketBegin)) {
      throw Error(stream, "Expected '[' after 'map'");
    }

    var keyType = ConsumeTyping(stream);

    if (!stream.Consume().IsMarker(MarkerType.Comma)) {
      throw Error(stream, "Expected ',' separator in map types");
    }

    var valType = ConsumeTyping(stream);

    if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
      throw Error(stream, "Expected ']' after map types");
    }

    if (!stream.Consume().IsMarker(MarkerType.BlockBegin)) {
      throw Error(stream, "Expected '{' to start map initializer");
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
        throw Error(stream, "Expected ':' after map key expression");
      }
      var valExpr = ParseExpression(stream);

      entries.Add(new KeyValuePair<Expression, Expression>(keyExpr, valExpr));
    }

    var map = new MapExpression(keyType, valType, entries);
    map.Token = startToken;
    return map;
  }
}
