using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Pino;

public class Lexer {
  private static readonly Dictionary<string, KeywordType> Keywords = new() {
        { "as", KeywordType.As },
        { "break", KeywordType.Break },
        { "val", KeywordType.Constant },
        { "continue", KeywordType.Continue },
        { "else", KeywordType.Else },
        { "enum", KeywordType.Enum },
        { "from", KeywordType.From },
        { "fn", KeywordType.Function },
        { "if", KeywordType.If },
        { "import", KeywordType.Import },
        { "in", KeywordType.In },
        { "interface", KeywordType.Interface },
        { "is", KeywordType.Is },
        { "for", KeywordType.Loop },
        { "match", KeywordType.Match },
        { "module", KeywordType.Module },
        { "pub", KeywordType.Pub },
        { "return", KeywordType.Return },
        { "static", KeywordType.Static },
        { "struct", KeywordType.Struct },
        { "then", KeywordType.Then },
        { "var", KeywordType.Variable },
        { "when", KeywordType.When }
    };

  private static readonly Dictionary<char, MarkerType> Markers = new() {
        { '@', MarkerType.At },
        { '{', MarkerType.BlockBegin },
        { '}', MarkerType.BlockEnd },
        { '[', MarkerType.BracketBegin },
        { ']', MarkerType.BracketEnd },
        { ',', MarkerType.Comma },
        { '#', MarkerType.Comment },
        { '(', MarkerType.ParenthesisBegin },
        { ')', MarkerType.ParenthesisEnd },
        { '"', MarkerType.StrQuote }
    };

  private static readonly Dictionary<string, OperatorType> Operators = new() {
        { "=", OperatorType.Assignment },
        { "+", OperatorType.Addition },
        { "+=", OperatorType.AdditionAssignment },
        { "-", OperatorType.Subtraction },
        { "-=", OperatorType.SubtractionAssignment },
        { "*", OperatorType.Multiplication },
        { "*=", OperatorType.MultiplicationAssignment },
        { "/", OperatorType.Division },
        { "/=", OperatorType.DivisionAssignment },
        { "%", OperatorType.Modulus },
        { "%=", OperatorType.ModulusAssignment },
        { "<", OperatorType.LessThan },
        { "<=", OperatorType.LessThanEqual },
        { ">", OperatorType.GreaterThan },
        { ">=", OperatorType.GreaterThanEqual },
        { "==", OperatorType.Equal },
        { "!=", OperatorType.NotEqual },
        { "and", OperatorType.And },
        { "or", OperatorType.Or },
        { "not", OperatorType.Not },
        { ":", OperatorType.MemberAccess },
        { "=>", OperatorType.Arrow },
        { "::", OperatorType.StaticMemberAccess }
    };

  private static readonly Regex FloatRegex = new(@"^-?[0-9]{1,9}(_[0-9]{3})*\.[0-9]{1,9}(_[0-9]{3})*$");
  private static readonly Regex IntRegex = new(@"^-?[0-9]{1,18}(_[0-9]{3})*$");
  private static readonly Regex IdentifierRegex = new(@"^[a-zA-Z_$][a-zA-Z0-9_$]*$");

  public static List<Token> LexLine(string line, int lineNum = 1) {
    var tokens = new List<Token>();
    var index = 0;

    while (index < line.Length) {
      var c = line[index];

      // 1. Whitespace
      if (char.IsWhiteSpace(c)) {
        index++;
        continue;
      }

      // 2. Comments
      if (c == '#') {
        // Comment spans to the end of the line
        break;
      }

      // 3. String Quote (String Literals with Potential Injections)
      if (c == '"') {
        LexStringLiteral(line, ref index, tokens, lineNum);
        continue;
      }

      // 3b. Rune Quote
      if (c == '\'') {
        LexRuneLiteral(line, ref index, tokens, lineNum);
        continue;
      }

      // 4. Markers (except comment, which is handled above)
      if (Markers.TryGetValue(c, out var markerType) && markerType != MarkerType.Comment && markerType != MarkerType.StrQuote) {
        var token = new Token(TokenType.Marker, c.ToString(), Marker: markerType) { Line = lineNum, Column = index + 1 };
        tokens.Add(token);
        index++;
        continue;
      }

      // 5. Multi-character Operators (like ==, !=, +=, -=, ::, <=, >=)
      if (index + 1 < line.Length) {
        var dualOp = line.Substring(index, 2);
        if (Operators.TryGetValue(dualOp, out var dualOpType) && !char.IsLetter(dualOp[0])) {
          var token = new Token(TokenType.Operator, dualOp, Operator: dualOpType) { Line = lineNum, Column = index + 1 };
          tokens.Add(token);
          index += 2;
          continue;
        }
      }

      // Single-character Operators
      if (Operators.TryGetValue(c.ToString(), out var opType) && c != 'a' && c != 'o' && c != 'n') { // avoid partial "and", "or", "not"
        var token = new Token(TokenType.Operator, c.ToString(), Operator: opType) { Line = lineNum, Column = index + 1 };
        tokens.Add(token);
        index++;
        continue;
      }

      // 6. Alphanumeric / Identifier / Number / Keyword buffer
      var bufferStart = index;
      while (index < line.Length) {
        var nextChar = line[index];
        if (char.IsWhiteSpace(nextChar) || Markers.ContainsKey(nextChar) || (Operators.ContainsKey(nextChar.ToString()) && nextChar != '_')) {
          break;
        }
        index++;
      }

      var buffer = line.Substring(bufferStart, index - bufferStart);
      if (!string.IsNullOrEmpty(buffer)) {
        var token = GetTokenFromBuffer(buffer);
        token.Line = lineNum;
        token.Column = bufferStart + 1;
        tokens.Add(token);
      }
    }

    return tokens;
  }

  public static List<Token> LexFile(string filePath) {
    var tokens = new List<Token>();
    int lineNum = 1;
    var lines = File.ReadAllLines(filePath);
    PinoException.RegisterFileLines(filePath, lines);
    foreach (var line in lines) {
      tokens.AddRange(LexLine(line, lineNum));
      lineNum++;
    }
    return tokens;
  }

  private static void LexStringLiteral(string line, ref int index, List<Token> tokens, int lineNum) {
    var buffer = new StringBuilder();
    bool hasAddedInitialString = false;
    int colStart = index + 1;
    index++; // Skip opening quote

    while (index < line.Length) {
      var c = line[index];

      // Check for closing quote, ignoring escaped quotes \"
      if (c == '"' && (index == 0 || line[index - 1] != '\\')) {
        index++; // Skip closing quote
        if (!hasAddedInitialString) {
          var token = new Token(TokenType.Literal, buffer.ToString(), Literal: LiteralType.String) { Line = lineNum, Column = colStart };
          tokens.Add(token);
        } else if (buffer.Length > 0) {
          var opToken = new Token(TokenType.Operator, "+", Operator: OperatorType.Addition) { Line = lineNum, Column = index };
          tokens.Add(opToken);
          var token = new Token(TokenType.Literal, buffer.ToString(), Literal: LiteralType.String) { Line = lineNum, Column = index };
          tokens.Add(token);
        }
        return;
      }

      // Check for string injection $(expr) or $varName, ignoring escaped \$
      if (c == '$' && (index == 0 || line[index - 1] != '\\') && index + 1 < line.Length) {
        if (line[index + 1] == '(') {
          // Complex interpolation $(expr)
          if (!hasAddedInitialString) {
            var token = new Token(TokenType.Literal, buffer.ToString(), Literal: LiteralType.String) { Line = lineNum, Column = colStart };
            tokens.Add(token);
            hasAddedInitialString = true;
          } else if (buffer.Length > 0) {
            var opToken = new Token(TokenType.Operator, "+", Operator: OperatorType.Addition) { Line = lineNum, Column = index + 1 };
            tokens.Add(opToken);
            var token = new Token(TokenType.Literal, buffer.ToString(), Literal: LiteralType.String) { Line = lineNum, Column = index + 1 };
            tokens.Add(token);
          }
          buffer.Clear();

          var outerOp = new Token(TokenType.Operator, "+", Operator: OperatorType.Addition) { Line = lineNum, Column = index + 1 };
          tokens.Add(outerOp);

          int parenCol = index + 2;
          index += 2; // skip "$("

          int depth = 1;
          int startExpr = index;
          while (index < line.Length && depth > 0) {
            char ec = line[index];
            if (ec == '"') {
              index++; // skip opening quote
              while (index < line.Length) {
                if (line[index] == '"' && line[index - 1] != '\\') {
                  break;
                }
                index++;
              }
            } else if (ec == '(') {
              depth++;
            } else if (ec == ')') {
              depth--;
            }
            index++;
          }

          string exprStr = line.Substring(startExpr, index - 1 - startExpr);
          var exprTokens = LexLine(exprStr, lineNum);
          foreach (var et in exprTokens) {
            et.Column += startExpr;
          }
          var pBegin = new Token(TokenType.Marker, "(", Marker: MarkerType.ParenthesisBegin) { Line = lineNum, Column = parenCol };
          tokens.Add(pBegin);
          tokens.AddRange(exprTokens);
          var pEnd = new Token(TokenType.Marker, ")", Marker: MarkerType.ParenthesisEnd) { Line = lineNum, Column = index };
          tokens.Add(pEnd);
          continue;
        } else if (IsIdentifierStart(line[index + 1]) && !char.IsDigit(line[index + 1])) {
          // Simple interpolation $varName
          if (!hasAddedInitialString) {
            var token = new Token(TokenType.Literal, buffer.ToString(), Literal: LiteralType.String) { Line = lineNum, Column = colStart };
            tokens.Add(token);
            hasAddedInitialString = true;
          } else if (buffer.Length > 0) {
            var opToken = new Token(TokenType.Operator, "+", Operator: OperatorType.Addition) { Line = lineNum, Column = index + 1 };
            tokens.Add(opToken);
            var token = new Token(TokenType.Literal, buffer.ToString(), Literal: LiteralType.String) { Line = lineNum, Column = index + 1 };
            tokens.Add(token);
          }
          buffer.Clear();

          var outerOp = new Token(TokenType.Operator, "+", Operator: OperatorType.Addition) { Line = lineNum, Column = index + 1 };
          tokens.Add(outerOp);
          index++; // skip '$'
          int startIdent = index;
          while (index < line.Length && IsIdentifierChar(line[index])) {
            index++;
          }
          string identStr = line.Substring(startIdent, index - startIdent);
          var identToken = new Token(TokenType.Identifier, identStr) { Line = lineNum, Column = startIdent + 1 };
          tokens.Add(identToken);
          continue;
        }
      }

      buffer.Append(c);
      index++;
    }

    throw new Exception($"Unterminated string literal at line index {index}");
  }

  private static Token GetTokenFromBuffer(string buffer) {
    if (Keywords.TryGetValue(buffer, out var keywordType)) {
      return new Token(TokenType.Keyword, buffer, Keyword: keywordType);
    }

    if (Operators.TryGetValue(buffer, out var opType)) {
      return new Token(TokenType.Operator, buffer, Operator: opType);
    }

    if (buffer == "true" || buffer == "false") {
      return new Token(TokenType.Literal, buffer, Literal: LiteralType.Boolean);
    }

    if (IntRegex.IsMatch(buffer)) {
      return new Token(TokenType.Literal, buffer, Literal: LiteralType.Integer);
    }

    if (FloatRegex.IsMatch(buffer)) {
      return new Token(TokenType.Literal, buffer, Literal: LiteralType.Float);
    }

    if (IdentifierRegex.IsMatch(buffer)) {
      return new Token(TokenType.Identifier, buffer);
    }

    return new Token(TokenType.Illegal, buffer);
  }

  private static bool IsIdentifierStart(char c) {
    return char.IsLetter(c) || c == '_' || c == '$';
  }

  private static bool IsIdentifierChar(char c) {
    return char.IsLetterOrDigit(c) || c == '_' || c == '$';
  }

  private static void LexRuneLiteral(string line, ref int index, List<Token> tokens, int lineNum) {
    int start = index;
    int colStart = index + 1;
    index++; // Skip opening single quote '
    if (index >= line.Length) {
      throw new Exception("Lexer Error: Unterminated rune literal");
    }

    int codePoint = 0;
    if (line[index] == '\\') {
      index++; // Skip backslash
      if (index >= line.Length) {
        throw new Exception("Lexer Error: Unterminated escape sequence in rune literal");
      }
      char escape = line[index];
      codePoint = escape switch {
        'n' => '\n',
        't' => '\t',
        'r' => '\r',
        '\'' => '\'',
        '\\' => '\\',
        _ => throw new Exception($"Lexer Error: Invalid escape character '{escape}' in rune literal")
      };
      index++;
    } else {
      if (char.IsHighSurrogate(line[index]) && index + 1 < line.Length && char.IsLowSurrogate(line[index + 1])) {
        codePoint = char.ConvertToUtf32(line[index], line[index + 1]);
        index += 2;
      } else {
        codePoint = line[index];
        index++;
      }
    }

    if (index >= line.Length || line[index] != '\'') {
      throw new Exception($"Lexer Error: Rune literal must end with a single quote: {line.Substring(start, Math.Min(line.Length - start, 5))}");
    }
    index++; // Skip closing single quote
    var token = new Token(TokenType.Literal, codePoint.ToString(), Literal: LiteralType.Rune) { Line = lineNum, Column = colStart };
    tokens.Add(token);
  }
}
