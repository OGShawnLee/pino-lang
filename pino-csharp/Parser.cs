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

public class Parser {
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
    return ParseProgram(stream);
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

  private static Statement ParseStatement(TokenStream stream) {
    var current = stream.Current;
    bool isPublic = false;

    if (current.IsKeyword(KeywordType.Pub)) {
      stream.Consume(); // consume 'pub'
      isPublic = true;
      current = stream.Current;
    }

    if (current.Type == TokenType.Keyword) {
      switch (current.Keyword) {
        case KeywordType.Variable:
        case KeywordType.Constant:
          return ParseVariableDeclaration(stream, isPublic);
        case KeywordType.Function:
          return ParseFunctionDeclaration(stream, isPublic);
        case KeywordType.Struct:
          return ParseStructDeclaration(stream, isPublic);
        case KeywordType.Interface:
          return ParseInterfaceDeclaration(stream, isPublic);
        case KeywordType.Enum:
          return ParseEnumDeclaration(stream, isPublic);
        case KeywordType.Module:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'module' declaration");
          return ParseModuleDeclaration(stream);
        case KeywordType.Import:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'import' statement");
          return ParseImportStatement(stream);
        case KeywordType.From:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'from ... import' statement");
          return ParseFromImportStatement(stream);
        case KeywordType.Return:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'return' statement");
          return ParseReturnStatement(stream);
        case KeywordType.Loop:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'for' loop");
          return ParseLoopStatement(stream);
        case KeywordType.If:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'if' statement");
          return ParseIfStatement(stream);
        case KeywordType.Break:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'break'");
          stream.Consume();
          return new IdentifierExpression("break");
        case KeywordType.Continue:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'continue'");
          stream.Consume();
          return new IdentifierExpression("continue");
        case KeywordType.Match:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'match' statement");
          return ParseMatchStatement(stream);
        case KeywordType.Else:
          throw new Exception("PARSER: Else statement without corresponding If");
        case KeywordType.When:
          throw new Exception("PARSER: When branch outside of Match statement");
        case KeywordType.Static:
          throw new Exception("PARSER: 'static' modifier is only valid inside struct definitions.");
      }
    }

    if (isPublic) {
      throw new Exception("PARSER: 'pub' can only prefix declarations (var, val, fn, struct, enum)");
    }

    if (current.Type == TokenType.Identifier || current.Type == TokenType.Literal || current.Type == TokenType.Marker) {
      if (IsExpression(stream)) {
        return ParseExpression(stream);
      }
    }

    stream.Consume(); // skip unknown/illegal
    return null!;
  }

  private static ModuleDeclaration ParseModuleDeclaration(TokenStream stream) {
    if (!stream.Consume().IsKeyword(KeywordType.Module)) {
      throw new Exception("PARSER: Expected 'module' keyword");
    }
    var identifier = ConsumeIdentifier(stream);
    return new ModuleDeclaration(identifier);
  }

  private static ImportStatement ParseImportStatement(TokenStream stream) {
    if (!stream.Consume().IsKeyword(KeywordType.Import)) {
      throw new Exception("PARSER: Expected 'import' keyword");
    }
    var moduleName = ConsumeIdentifier(stream);
    DeclareVariable(stream, moduleName);
    return new ImportStatement(moduleName);
  }

  private static FromImportStatement ParseFromImportStatement(TokenStream stream) {
    if (!stream.Consume().IsKeyword(KeywordType.From)) {
      throw new Exception("PARSER: Expected 'from' keyword");
    }
    var moduleName = ConsumeIdentifier(stream);
    if (!stream.Consume().IsKeyword(KeywordType.Import)) {
      throw new Exception("PARSER: Expected 'import' keyword after from <ModuleName>");
    }
    
    var imports = new List<string>();
    while (true) {
      var importedName = ConsumeIdentifier(stream);
      imports.Add(importedName);
      DeclareVariable(stream, importedName);
      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
        continue;
      }
      break;
    }
    return new FromImportStatement(moduleName, imports);
  }

  private static bool IsExpression(TokenStream stream) {
    var current = stream.Current;
    return IsFunctionLambda(stream) ||
           IsVector(stream) ||
           current.IsKeyword(KeywordType.If) ||
           current.IsType(TokenType.Identifier, TokenType.Literal);
  }

  private static bool IsFunctionCall(TokenStream stream) {
    return stream.Current.IsType(TokenType.Identifier) && stream.IsNext(t => t.IsMarker(MarkerType.ParenthesisBegin));
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
        if (tok.Type == TokenType.Keyword) {
          return false;
        }
        if (tok.Type == TokenType.Operator) {
          var op = tok.Operator;
          if (op == OperatorType.Assignment ||
              op == OperatorType.AdditionAssignment ||
              op == OperatorType.SubtractionAssignment ||
              op == OperatorType.MultiplicationAssignment ||
              op == OperatorType.DivisionAssignment ||
              op == OperatorType.ModulusAssignment) {
            return false;
          }
        }
        if (tok.Type == TokenType.Identifier) {
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
    if (!stream.Peek(1).IsMarker(MarkerType.BlockBegin)) {
      return false;
    }
    if (stream.Peek(2).IsMarker(MarkerType.BlockEnd)) {
      if (stream.Peek(-1).IsOperator(OperatorType.StaticMemberAccess)) {
        return false;
      }
      return true;
    }
    return IsStructBlock(stream, 1);
  }

  private static bool IsVector(TokenStream stream) {
    return stream.Current.IsMarker(MarkerType.BracketBegin);
  }

  private static VariableDeclaration ParseVariableDeclaration(TokenStream stream, bool isPublic = false) {
    var keywordToken = stream.Consume();
    var kind = keywordToken.Keyword == KeywordType.Constant ? VariableKind.Constant : VariableKind.Variable;

    var identifier = ConsumeIdentifier(stream);
    ConsumeAssignment(stream);
    var value = ParseExpression(stream);

    DeclareVariable(stream, identifier);
    return new VariableDeclaration(kind, identifier, value, IsPublic: isPublic);
  }

  private static FunctionDeclaration ParseFunctionDeclaration(TokenStream stream, bool isPublic = false) {
    if (!stream.Consume().IsKeyword(KeywordType.Function)) {
      throw new Exception("PARSER: Expected 'fn' keyword");
    }

    var identifier = ConsumeIdentifier(stream);
    var parameters = ConsumeParameters(stream);
    
    string returnType = "";
    if (stream.Current.Type == TokenType.Identifier || 
        stream.Current.IsMarker(MarkerType.BracketBegin) || 
        stream.Current.IsKeyword(KeywordType.Function)) {
      returnType = ConsumeTyping(stream);
    }

    PushScope(stream);
    foreach (var param in parameters) {
      DeclareVariable(stream, param.Identifier);
    }
    
    var body = ParseBlock(stream);
    PopScope(stream);

    DeclareVariable(stream, identifier);

    return new FunctionDeclaration(identifier, parameters, body, returnType, IsPublic: isPublic);
  }

  private static StructDeclaration ParseStructDeclaration(TokenStream stream, bool isPublic = false) {
    if (!stream.Consume().IsKeyword(KeywordType.Struct)) {
      throw new Exception("PARSER: Expected 'struct' keyword");
    }

    var identifier = ConsumeIdentifier(stream);
    var fields = new List<VariableDeclaration>();
    var methods = new List<FunctionDeclaration>();
    var inheritedStructs = new List<string>();

    ConsumeAttributesAndMethods(stream, fields, methods, inheritedStructs);

    return new StructDeclaration(identifier, fields, methods, inheritedStructs, IsPublic: isPublic);
  }

  private static InterfaceDeclaration ParseInterfaceDeclaration(TokenStream stream, bool isPublic = false) {
    if (!stream.Consume().IsKeyword(KeywordType.Interface)) {
      throw new Exception("PARSER: Expected 'interface' keyword");
    }

    var identifier = ConsumeIdentifier(stream);
    var methods = new List<FunctionDeclaration>();

    if (!stream.Consume().IsMarker(MarkerType.BlockBegin)) {
      throw new Exception("PARSER: Expected '{' to start interface body");
    }

    while (true) {
      if (stream.Current.IsMarker(MarkerType.BlockEnd)) {
        stream.Consume();
        break;
      }

      if (stream.Current.IsKeyword(KeywordType.Function)) {
        methods.Add(ParseInterfaceMethodSignature(stream));
      } else {
        throw new Exception($"PARSER: Expected method signature in interface body, got {stream.Current}");
      }

      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
      }
    }

    return new InterfaceDeclaration(identifier, methods, IsPublic: isPublic);
  }

  private static FunctionDeclaration ParseInterfaceMethodSignature(TokenStream stream) {
    if (!stream.Consume().IsKeyword(KeywordType.Function)) {
      throw new Exception("PARSER: Expected 'fn' keyword for method signature");
    }

    var identifier = ConsumeIdentifier(stream);
    var parameters = ConsumeParameters(stream);

    string returnType = "";
    if (stream.Current.Type == TokenType.Identifier || 
        stream.Current.IsMarker(MarkerType.BracketBegin) || 
        stream.Current.IsKeyword(KeywordType.Function)) {
      returnType = ConsumeTyping(stream);
    }

    return new FunctionDeclaration(identifier, parameters, null, returnType, IsPublic: false);
  }

  private static EnumDeclaration ParseEnumDeclaration(TokenStream stream, bool isPublic = false) {
    if (!stream.Consume().IsKeyword(KeywordType.Enum)) {
      throw new Exception("PARSER: Expected 'enum' keyword");
    }

    var identifier = ConsumeIdentifier(stream);
    var members = ConsumeEnumMembers(stream);

    return new EnumDeclaration(identifier, members, IsPublic: isPublic);
  }

  private static ReturnStatement ParseReturnStatement(TokenStream stream) {
    if (!stream.Consume().IsKeyword(KeywordType.Return)) {
      throw new Exception("PARSER: Expected 'return' keyword");
    }

    Expression? arg = null;
    if (IsExpression(stream)) {
      arg = ParseExpression(stream);
    }

    return new ReturnStatement(arg);
  }

  private static LoopStatement ParseLoopStatement(TokenStream stream) {
    if (!stream.Consume().IsKeyword(KeywordType.Loop)) {
      throw new Exception("PARSER: Expected 'for' keyword");
    }

    // Infinite loop: for { ... }
    if (stream.Current.IsMarker(MarkerType.BlockBegin)) {
      var body = ParseBlock(stream);
      return new LoopStatement(LoopKind.Infinite, null, null, body);
    }

    var begin = ParseExpression(stream, allowStruct: false, allowMemberAccess: true, allowIn: false);

    // For times loop: for 10 { ... }
    if (stream.Current.IsMarker(MarkerType.BlockBegin)) {
      var body = ParseBlock(stream);
      return new LoopStatement(LoopKind.ForTimes, begin, null, body);
    }

    string? keyVar = null;
    if (stream.Current.IsMarker(MarkerType.Comma)) {
      if (begin is not IdentifierExpression idKey) {
        throw new Exception("PARSER: Loop key variable must be an identifier");
      }
      keyVar = idKey.Name;
      stream.Consume(); // consume the comma ','
      begin = ParseExpression(stream, allowStruct: false, allowMemberAccess: true, allowIn: false);
      if (begin is not IdentifierExpression) {
        throw new Exception("PARSER: Loop value variable must be an identifier");
      }
    }

    // For In loop: for i in collection { ... }
    if (!stream.Consume().IsKeyword(KeywordType.In)) {
      throw new Exception("PARSER: Expected 'in' in loop declaration");
    }

    var end = ParseExpression(stream, false);
    
    PushScope(stream);
    if (begin is IdentifierExpression id) {
      DeclareVariable(stream, id.Name);
    }
    if (!string.IsNullOrEmpty(keyVar)) {
      DeclareVariable(stream, keyVar);
    }
    var inBody = ParseBlock(stream);
    PopScope(stream);

    return new LoopStatement(LoopKind.ForIn, begin, end, inBody, keyVar);
  }

  private static IfStatement ParseIfStatement(TokenStream stream) {
    if (!stream.Consume().IsKeyword(KeywordType.If)) {
      throw new Exception("PARSER: Expected 'if' keyword");
    }

    var condition = ParseExpression(stream, false);
    var body = ParseBlock(stream);
    Statement? alternate = null;

    if (stream.Current.IsKeyword(KeywordType.Else)) {
      if (stream.IsNext(t => t.IsKeyword(KeywordType.If))) {
        stream.Consume(); // consume 'else'
        alternate = ParseIfStatement(stream);
      } else {
        alternate = ParseElseStatement(stream);
      }
    }

    return new IfStatement(condition, body, alternate);
  }

  private static ElseStatement ParseElseStatement(TokenStream stream) {
    if (!stream.Consume().IsKeyword(KeywordType.Else)) {
      throw new Exception("PARSER: Expected 'else' keyword");
    }

    var body = ParseBlock(stream);
    return new ElseStatement(body);
  }

  private static MatchStatement ParseMatchStatement(TokenStream stream) {
    if (!stream.Consume().IsKeyword(KeywordType.Match)) {
      throw new Exception("PARSER: Expected 'match' keyword");
    }

    var condition = ParseExpression(stream, false);

    if (!stream.Consume().IsMarker(MarkerType.BlockBegin)) {
      throw new Exception("PARSER: Expected '{' after match expression");
    }

    var branches = new List<WhenStatement>();
    ElseStatement? alternate = null;

    while (stream.HasNext) {
      if (stream.Current.IsMarker(MarkerType.BlockEnd)) {
        stream.Consume(); // consume '}'
        break;
      }

      if (stream.Current.IsKeyword(KeywordType.Else)) {
        alternate = ParseElseStatement(stream);
        // Consume closing brace if alternate finishes before Match block finishes
        if (stream.Current.IsMarker(MarkerType.BlockEnd)) {
          stream.Consume();
        }
        break;
      }

      if (stream.Current.IsKeyword(KeywordType.When)) {
        branches.Add(ParseWhenStatement(stream));
        continue;
      }

      throw new Exception("PARSER: Expected 'when' or 'else' in match statement");
    }

    return new MatchStatement(condition, branches, alternate);
  }

  private static WhenStatement ParseWhenStatement(TokenStream stream) {
    if (!stream.Consume().IsKeyword(KeywordType.When)) {
      throw new Exception("PARSER: Expected 'when' keyword");
    }

    var conditions = new List<Expression>();
    while (stream.HasNext) {
      conditions.Add(ParseExpression(stream, false));

      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
        continue;
      }

      if (stream.Current.IsMarker(MarkerType.BlockBegin)) {
        break;
      }
    }

    var body = ParseBlock(stream);
    return new WhenStatement(conditions, body);
  }

  private static BlockStatement ParseBlock(TokenStream stream) {
    if (!stream.Consume().IsMarker(MarkerType.BlockBegin)) {
      throw new Exception("PARSER: Expected '{'");
    }

    PushScope(stream);
    var statements = new List<Statement>();

    while (stream.HasNext) {
      if (stream.Current.IsMarker(MarkerType.BlockEnd)) {
        stream.Consume(); // consume '}'
        PopScope(stream);
        return new BlockStatement(statements);
      }

      var stmt = ParseStatement(stream);
      if (stmt != null) {
        statements.Add(stmt);
      }
    }

    throw new Exception("PARSER: Expected '}'");
  }

  private static Expression ParseExpression(TokenStream stream, bool allowStruct = true, bool allowMemberAccess = true, bool allowIn = true) {
    return ParseExpressionWithPrecedence(stream, 0, allowStruct, allowMemberAccess, allowIn);
  }

  private static Expression ParseExpressionWithPrecedence(TokenStream stream, int minPrecedence, bool allowStruct = true, bool allowMemberAccess = true, bool allowIn = true) {
    var expression = ParsePrimaryExpression(stream, allowStruct, allowMemberAccess);

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
      expression = new BinaryExpression(expression, opType, right);
    }

    return expression;
  }

  private static Expression ParsePrimaryExpression(TokenStream stream, bool allowStruct = true, bool allowMemberAccess = true) {
    Expression expr;

    if (stream.Current.IsMarker(MarkerType.ParenthesisBegin)) {
      stream.Consume();
      expr = ParseExpression(stream, true, true);
      if (!stream.Consume().IsMarker(MarkerType.ParenthesisEnd)) {
        throw new Exception("PARSER: Expected ')' to close grouped expression");
      }
    }
    else if (stream.Current.Type == TokenType.Identifier && stream.Current.Data == "map" && stream.IsNext(t => t.IsMarker(MarkerType.BracketBegin))) {
      expr = ParseMapExpression(stream);
    }
    else if (IsFunctionCall(stream)) {
      expr = ParseFunctionCall(stream);
    }
    else if (IsFunctionLambda(stream)) {
      expr = ParseFunctionLambda(stream);
    }
    else if (IsVector(stream)) {
      expr = ParseVector(stream);
    }
    else if (allowStruct && IsStructInstance(stream)) {
      expr = ParseStructInstance(stream);
    }
    else if (stream.Current.IsKeyword(KeywordType.If)) {
      expr = ParseTernaryExpression(stream);
    }
    else if (stream.Current.Type == TokenType.Identifier) {
      expr = new IdentifierExpression(stream.Consume().Data);
    }
    else if (stream.Current.Type == TokenType.Literal) {
      var t = stream.Consume();
      expr = new LiteralExpression(t.Data, t.Literal!.Value, t.Injections);
    }
    else {
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
        if (stream.Current.IsMarker(MarkerType.ParenthesisBegin)) {
          var args = ConsumeArguments(stream);
          rightSide = new FunctionCallExpression(memberName, args);
        } else {
          rightSide = new IdentifierExpression(memberName);
        }
        expr = new BinaryExpression(expr, OperatorType.MemberAccess, rightSide);
      } else if (stream.Current.IsOperator(OperatorType.StaticMemberAccess)) {
        stream.Consume(); // consume '::'
        Expression rightSide;
        if (IsStructInstance(stream)) {
          rightSide = ParseStructInstance(stream);
        } else if (IsFunctionCall(stream)) {
          rightSide = ParseFunctionCall(stream);
        } else {
          var memberName = ConsumeIdentifier(stream);
          rightSide = new IdentifierExpression(memberName);
        }
        expr = new BinaryExpression(expr, OperatorType.StaticMemberAccess, rightSide);
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
    var args = ConsumeArguments(stream);
    return new FunctionCallExpression(callee, args);
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
    var props = ConsumeProperties(stream);
    return new StructInstanceExpression(structName, props);
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

  // Helper consumers
  private static string ConsumeIdentifier(TokenStream stream) {
    var t = stream.Consume();
    if (t.Type != TokenType.Identifier) {
      throw new Exception($"PARSER: Expected Identifier, got {t}");
    }
    return t.Data;
  }

  private static string ConsumeTyping(TokenStream stream) {
    if (stream.Current.Type == TokenType.Identifier && stream.Current.Data == "map") {
      stream.Consume(); // consume 'map'
      if (!stream.Consume().IsMarker(MarkerType.BracketBegin)) {
        throw new Exception($"PARSER: Expected '[' after 'map' in type signature, got {stream.Current}");
      }
      string keyType = ConsumeTyping(stream);
      if (!stream.Consume().IsMarker(MarkerType.Comma)) {
        throw new Exception($"PARSER: Expected ',' between map types in type signature, got {stream.Current}");
      }
      string valType = ConsumeTyping(stream);
      if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
        throw new Exception($"PARSER: Expected ']' after map types in type signature, got {stream.Current}");
      }
      return $"map[{keyType}, {valType}]";
    }

    if (stream.Current.IsMarker(MarkerType.BracketBegin)) {
      stream.Consume(); // consume '['
      if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
        throw new Exception($"PARSER: Expected ']' for array type, got {stream.Current}");
      }
      string elemType = ConsumeTyping(stream);
      return "[]" + elemType;
    }

    if (stream.Current.IsKeyword(KeywordType.Function)) {
      stream.Consume(); // consume 'fn'
      if (!stream.Consume().IsMarker(MarkerType.ParenthesisBegin)) {
        throw new Exception($"PARSER: Expected '(' for function type, got {stream.Current}");
      }
      
      var paramTypes = new List<string>();
      while (stream.HasNext && !stream.Current.IsMarker(MarkerType.ParenthesisEnd)) {
        paramTypes.Add(ConsumeTyping(stream));
        if (stream.Current.IsMarker(MarkerType.Comma)) {
          stream.Consume();
        }
      }
      
      if (!stream.Consume().IsMarker(MarkerType.ParenthesisEnd)) {
        throw new Exception($"PARSER: Expected ')' for function type, got {stream.Current}");
      }
      
      string returnType = " any";
      // Optional return type: can start with identifier, bracket '[', or keyword 'fn'
      if (stream.Current.Type == TokenType.Identifier || 
          stream.Current.IsMarker(MarkerType.BracketBegin) || 
          stream.Current.IsKeyword(KeywordType.Function)) {
        returnType = " " + ConsumeTyping(stream);
      }
      
      return $"fn({string.Join(", ", paramTypes)}){returnType}";
    }

    var t = stream.Consume();
    if (t.Type != TokenType.Identifier) {
      throw new Exception($"PARSER: Expected Typing, got {t}");
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
        var param = new VariableDeclaration(VariableKind.Constant, "it", null, "int");
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

  private static void ConsumeAttributesAndMethods(
      TokenStream stream,
      List<VariableDeclaration> attributes,
      List<FunctionDeclaration> methods,
      List<string> inheritedStructs) {
    if (!stream.Consume().IsMarker(MarkerType.BlockBegin)) {
      throw new Exception("PARSER: Expected '{'");
    }

    while (true) {
      if (stream.Current.IsMarker(MarkerType.BlockEnd)) {
        stream.Consume();
        break;
      }

      bool isStatic = false;
      if (stream.Current.IsKeyword(KeywordType.Static)) {
        stream.Consume(); // consume 'static'
        isStatic = true;
        if (!stream.Current.IsKeyword(KeywordType.Function)) {
          throw new Exception("PARSER: 'static' keyword can only modify function declarations inside structs.");
        }
      }

      if (stream.Current.IsKeyword(KeywordType.Function)) {
        var fn = ParseFunctionDeclaration(stream);
        if (isStatic) {
          fn = fn with { IsStatic = true };
        }
        methods.Add(fn);
      } else {
        if (isStatic) {
          throw new Exception("PARSER: 'static' modifier cannot be applied to struct fields.");
        }
        var identifier = ConsumeIdentifier(stream);
        if (char.IsUpper(identifier[0])) {
          inheritedStructs.Add(identifier);
        } else {
          var typing = ConsumeTyping(stream);
          attributes.Add(new VariableDeclaration(VariableKind.Variable, identifier, null, typing));
        }
      }

      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
      }
    }
  }

  private static List<string> ConsumeEnumMembers(TokenStream stream) {
    if (!stream.Consume().IsMarker(MarkerType.BlockBegin)) {
      throw new Exception("PARSER: Expected '{'");
    }

    var members = new List<string>();
    while (true) {
      if (stream.Current.IsMarker(MarkerType.BlockEnd)) {
        stream.Consume();
        return members;
      }

      members.Add(ConsumeIdentifier(stream));

      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
      }
    }
  }

  private static List<VariableDeclaration> ConsumeProperties(TokenStream stream) {
    if (!stream.Consume().IsMarker(MarkerType.BlockBegin)) {
      throw new Exception("PARSER: Expected '{'");
    }

    var properties = new List<VariableDeclaration>();
    while (stream.HasNext) {
      if (stream.Current.IsMarker(MarkerType.BlockEnd)) {
        stream.Consume();
        return properties;
      }

      var identifier = ConsumeIdentifier(stream);
      if (!stream.Consume().IsOperator(OperatorType.MemberAccess)) {
        throw new Exception("PARSER: Expected ':'");
      }
      var val = ParseExpression(stream);

      properties.Add(new VariableDeclaration(VariableKind.Property, identifier, val));

      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
      }
    }

    throw new Exception("PARSER: Expected '}'");
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
