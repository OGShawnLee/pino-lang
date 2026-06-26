using System;
using System.Collections.Generic;

namespace Pino;

public class TokenStream {
  private readonly List<Token> _tokens;
  private int _index = 0;
  public Stack<HashSet<string>> Scopes { get; } = new();

  public TokenStream(List<Token> tokens) {
    _tokens = tokens;
  }

  public Token Current => _index < _tokens.Count ? _tokens[_index] : new Token(TokenType.Illegal, "");

  public Token Consume() {
    var token = Current;
    _index++;
    return token;
  }

  public void Next() {
    _index++;
  }

  public bool HasNext => _index < _tokens.Count;

  public bool IsNext(Func<Token, bool> predicate) {
    if (_index + 1 >= _tokens.Count) return false;
    return predicate(_tokens[_index + 1]);
  }

  public Token Peek(int offset) {
    var idx = _index + offset;
    if (idx < 0 || idx >= _tokens.Count) return new Token(TokenType.Illegal, "");
    return _tokens[idx];
  }
}

public partial class Parser {
  private static void PushScope(TokenStream stream) {
    stream.Scopes.Push(new HashSet<string>());
  }

  private static void PopScope(TokenStream stream) {
    if (stream.Scopes.Count > 0) {
      stream.Scopes.Pop();
    }
  }

  private static void DeclareVariable(TokenStream stream, string name) {
    if (stream.Scopes.Count > 0) {
      stream.Scopes.Peek().Add(name);
    }
  }

  private static bool IsDeclared(TokenStream stream, string name) {
    foreach (var scope in stream.Scopes) {
      if (scope.Contains(name)) return true;
    }
    return false;
  }

  private static bool ContainsUndeclaredIt(Expression? expr, TokenStream stream) {
    if (expr == null) return false;
    switch (expr) {
      case IdentifierExpression id:
        return id.Name == "it" && !IsDeclared(stream, "it");

      case BinaryExpression bin:
        return ContainsUndeclaredIt(bin.Left, stream) || ContainsUndeclaredIt(bin.Right, stream);

      case TernaryExpression tern:
        return ContainsUndeclaredIt(tern.Condition, stream) ||
               ContainsUndeclaredIt(tern.Consequent, stream) ||
               ContainsUndeclaredIt(tern.Alternate, stream);

      case VectorExpression vec:
        if (vec.Elements != null) {
          foreach (var el in vec.Elements) {
            if (ContainsUndeclaredIt(el, stream)) return true;
          }
        }
        if (vec.Len != null && ContainsUndeclaredIt(vec.Len, stream)) return true;
        if (vec.Init != null && ContainsUndeclaredIt(vec.Init, stream)) return true;
        return false;

      case StructInstanceExpression inst:
        foreach (var prop in inst.Properties) {
          if (ContainsUndeclaredIt(prop.Value, stream)) return true;
        }
        return false;

      case FunctionCallExpression call:
        foreach (var arg in call.Arguments) {
          if (ContainsUndeclaredIt(arg, stream)) return true;
        }
        return false;

      case FunctionLambdaExpression lambda:
        return false;

      case IndexAccessExpression idx:
        return ContainsUndeclaredIt(idx.Target, stream) || ContainsUndeclaredIt(idx.Index, stream);

      case MapExpression map:
        foreach (var entry in map.Entries) {
          if (ContainsUndeclaredIt(entry.Key, stream) || ContainsUndeclaredIt(entry.Value, stream)) {
            return true;
          }
        }
        return false;

      default:
        return false;
    }
  }

  public static ProgramStatement ParseFile(string filePath) {
    var tokens = Lexer.LexFile(filePath);
    var stream = new TokenStream(tokens);
    var prog = ParseProgram(stream);
    prog.FilePath = filePath;
    return prog;
  }

  public static Statement ParseString(string input) {
    var tokens = Lexer.LexLine(input);
    var stream = new TokenStream(tokens);
    return ParseStatement(stream);
  }

  public static ProgramStatement ParseProgramString(string input) {
    var tokens = new List<Token>();
    var lines = input.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
    foreach (var line in lines) {
      tokens.AddRange(Lexer.LexLine(line));
    }
    var stream = new TokenStream(tokens);
    return ParseProgram(stream);
  }

  private static ProgramStatement ParseProgram(TokenStream stream) {
    PushScope(stream);
    var statements = new List<Statement>();
    bool first = true;
    while (stream.HasNext) {
      var stmt = ParseStatement(stream);
      if (stmt != null) {
        if (stmt is ModuleDeclaration && !first) {
          throw new Exception("PARSER: 'module' declaration must be the first statement in the file");
        }
        statements.Add(stmt);
        first = false;
      }
    }
    PopScope(stream);
    return new ProgramStatement(statements);
  }

  // Helper consumers
  private static string ConsumeIdentifier(TokenStream stream) {
    var t = stream.Consume();
    if (t.Type != TokenType.Identifier) {
      throw new Exception($"PARSER: Expected Identifier, got {t}");
    }
    return t.Data;
  }

  private static void ConsumeAssignment(TokenStream stream) {
    var t = stream.Consume();
    if (!t.IsOperator(OperatorType.Assignment)) {
      throw new Exception($"PARSER: Expected '=', got {t}");
    }
  }

  private static List<Expression> ConsumeArguments(TokenStream stream) {
    if (!stream.Consume().IsMarker(MarkerType.ParenthesisBegin)) {
      throw new Exception("PARSER: Expected '('");
    }

    var arguments = new List<Expression>();
    while (true) {
      if (stream.Current.IsMarker(MarkerType.ParenthesisEnd)) {
        stream.Consume();
        return arguments;
      }

      var expr = ParseExpression(stream);
      if (ContainsUndeclaredIt(expr, stream)) {
        var param = new VariableDeclaration(VariableKind.Constant, "it", null, "implicit");
        var lambdaBody = new BlockStatement(new List<Statement> { new ReturnStatement(expr) });
        expr = new FunctionLambdaExpression(new List<VariableDeclaration> { param }, lambdaBody);
      }
      arguments.Add(expr);

      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
      }
    }
  }

  private static List<VariableDeclaration> ConsumeParameters(TokenStream stream) {
    var parameters = new List<VariableDeclaration>();

    if (stream.Current.IsMarker(MarkerType.BlockBegin)) {
      return parameters;
    }

    if (!stream.Consume().IsMarker(MarkerType.ParenthesisBegin)) {
      throw new Exception("PARSER: Expected '('");
    }

    if (stream.Current.IsMarker(MarkerType.ParenthesisEnd)) {
      stream.Consume();
      return parameters;
    }

    while (true) {
      if (stream.Current.IsMarker(MarkerType.ParenthesisEnd)) {
        stream.Consume();
        return parameters;
      }

      var identifier = ConsumeIdentifier(stream);
      var typing = ConsumeTyping(stream);
      parameters.Add(new VariableDeclaration(VariableKind.Parameter, identifier, null, typing));

      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
      }
    }
  }
}