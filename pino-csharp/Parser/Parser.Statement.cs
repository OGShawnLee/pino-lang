using System;
using System.Collections.Generic;

namespace Pino;

public partial class Parser {
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

    string? KeyVar = null;
    if (stream.Current.IsMarker(MarkerType.Comma)) {
      if (begin is not IdentifierExpression idKey) {
        throw new Exception("PARSER: Loop key variable must be an identifier");
      }
      KeyVar = idKey.Name;
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

    if (!string.IsNullOrEmpty(KeyVar)) {
      DeclareVariable(stream, KeyVar);
    }

    var inBody = ParseBlock(stream);
    PopScope(stream);

    return new LoopStatement(LoopKind.ForIn, begin, end, inBody, KeyVar);
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
}
