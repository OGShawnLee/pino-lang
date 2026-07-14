using System;
using System.Collections.Generic;

namespace Pino;

public partial class Parser {
  private static List<GenericParam> ParseGenericParamsList(TokenStream stream) {
    if (!stream.Consume().IsMarker(MarkerType.BracketBegin)) {
      throw new Exception("PARSER: Expected '[' to begin generic parameters");
    }

    var list = new List<GenericParam>();
    while (!stream.Current.IsMarker(MarkerType.BracketEnd)) {
      var name = ConsumeIdentifier(stream);
      string? constraint = null;
      if (stream.Current.IsKeyword(KeywordType.Is)) {
        stream.Consume(); // consume 'is'
        constraint = ConsumeTyping(stream);
      }
      list.Add(new GenericParam(name, constraint));

      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
      }
    }

    if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
      throw new Exception("PARSER: Expected ']' to end generic parameters");
    }

    return list;
  }

  private static Statement ParseStatement(TokenStream stream) {
    List<GenericParam>? genericParams = null;
    bool isPublic = false;

    while (true) {
      if (stream.Current.IsMarker(MarkerType.At)) {
        stream.Consume(); // consume '@'
        var decorator = ConsumeIdentifier(stream);
        if (decorator == "generic") {
          genericParams = ParseGenericParamsList(stream);
        } else {
          throw new Exception($"PARSER: Unknown decorator '@{decorator}'");
        }
        continue;
      }
      if (stream.Current.IsKeyword(KeywordType.Pub)) {
        stream.Consume(); // consume 'pub'
        isPublic = true;
        continue;
      }
      break;
    }

    var current = stream.Current;

    if (current.Type == TokenType.Keyword) {
      switch (current.Keyword) {
        case KeywordType.Variable:
        case KeywordType.Constant:
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to variable declarations");
          return ParseVariableDeclaration(stream, isPublic);
        case KeywordType.Function:
          return ParseFunctionDeclaration(stream, genericParams, isPublic);
        case KeywordType.Struct:
          return ParseStructDeclaration(stream, genericParams, isPublic);
        case KeywordType.Interface:
          return ParseInterfaceDeclaration(stream, genericParams, isPublic);
        case KeywordType.Enum:
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to enum declarations");
          return ParseEnumDeclaration(stream, isPublic);
        case KeywordType.Union:
          return ParseUnionDeclaration(stream, genericParams, isPublic);
        case KeywordType.Module:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'module' declaration");
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to module declarations");
          return ParseModuleDeclaration(stream);
        case KeywordType.Import:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'import' statement");
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to import statements");
          return ParseImportStatement(stream);
        case KeywordType.From:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'from ... import' statement");
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to from-import statements");
          return ParseFromImportStatement(stream);
        case KeywordType.Return:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'return' statement");
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to return statements");
          return ParseReturnStatement(stream);
        case KeywordType.Yield:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'yield' statement");
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to yield statements");
          return ParseYieldStatement(stream);
        case KeywordType.Test:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'test' block");
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to test blocks");
          return ParseTestDeclaration(stream);
        case KeywordType.Assert:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'assert' statement");
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to assert statements");
          return ParseAssertStatement(stream);
        case KeywordType.Loop:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'for' loop");
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to loops");
          return ParseLoopStatement(stream);
        case KeywordType.If:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'if' statement");
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to if statements");
          return ParseIfStatement(stream);
        case KeywordType.Break:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'break'");
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to break");
          stream.Consume();
          return new IdentifierExpression("break");
        case KeywordType.Continue:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'continue'");
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to continue");
          stream.Consume();
          return new IdentifierExpression("continue");
        case KeywordType.Match:
          if (isPublic) throw new Exception("PARSER: 'pub' cannot prefix 'match' statement");
          if (genericParams != null) throw new Exception("PARSER: '@generic' cannot be applied to match statements");
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
    if (genericParams != null) {
      throw new Exception("PARSER: '@generic' can only prefix declarations (fn, struct, interface)");
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

  private static Statement ParseVariableDeclaration(TokenStream stream, bool isPublic = false) {
    var keywordToken = stream.Consume();
    var kind = keywordToken.Keyword == KeywordType.Constant ? VariableKind.Constant : VariableKind.Variable;

    if (stream.Current.IsMarker(MarkerType.At) && stream.Peek(1).IsMarker(MarkerType.ParenthesisBegin)) {
      stream.Consume(); // consume '@'
      stream.Consume(); // consume '('
      var fields = new List<TupleDestructureField>();
      while (!stream.Current.IsMarker(MarkerType.ParenthesisEnd)) {
        var firstId = ConsumeIdentifier(stream);
        string label = firstId;
        string identifier = firstId;
        if (stream.Current.IsOperator(OperatorType.MemberAccess)) {
          stream.Consume(); // consume ':'
          identifier = ConsumeIdentifier(stream);
        }
        fields.Add(new TupleDestructureField(label, identifier));
        if (stream.Current.IsMarker(MarkerType.Comma)) {
          stream.Consume();
        }
      }
      if (!stream.Consume().IsMarker(MarkerType.ParenthesisEnd)) {
        throw new Exception("PARSER: Expected ')' to end tuple destructuring");
      }
      ConsumeAssignment(stream);
      var value = ParseExpression(stream);

      foreach (var field in fields) {
        DeclareVariable(stream, field.Identifier);
      }
      return new TupleDestructuringDeclaration(kind, fields, value);
    } else {
      var identifier = ConsumeIdentifier(stream);
      ConsumeAssignment(stream);
      var value = ParseExpression(stream);

      DeclareVariable(stream, identifier);
      return new VariableDeclaration(kind, identifier, value, IsPublic: isPublic);
    }
  }

  private static FunctionDeclaration ParseFunctionDeclaration(TokenStream stream, List<GenericParam>? decoratorGenerics = null, bool isPublic = false) {
    if (!stream.Consume().IsKeyword(KeywordType.Function)) {
      throw new Exception("PARSER: Expected 'fn' keyword");
    }

    var identifier = ConsumeIdentifier(stream);
    var parameters = ConsumeParameters(stream);

    string returnType = "";
    List<VariableDeclaration>? tupleReturnType = null;
    if (stream.Current.IsMarker(MarkerType.At) && stream.Peek(1).IsMarker(MarkerType.ParenthesisBegin)) {
      stream.Consume(); // consume '@'
      stream.Consume(); // consume '('
      tupleReturnType = new List<VariableDeclaration>();
      while (!stream.Current.IsMarker(MarkerType.ParenthesisEnd)) {
        var fieldId = ConsumeIdentifier(stream);
        var fieldType = ConsumeTyping(stream);
        tupleReturnType.Add(new VariableDeclaration(VariableKind.Property, fieldId, null, fieldType));
        if (stream.Current.IsMarker(MarkerType.Comma)) {
          stream.Consume();
        }
      }
      if (!stream.Consume().IsMarker(MarkerType.ParenthesisEnd)) {
        throw new Exception("PARSER: Expected ')' to close tuple return type");
      }
      var fieldStrings = new List<string>();
      foreach (var field in tupleReturnType) {
        fieldStrings.Add($"{field.Identifier}:{field.Typing}");
      }
      returnType = $"@({string.Join(",", fieldStrings)})";
    } else if (stream.Current.Type == TokenType.Identifier ||
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

    var fnDecl = new FunctionDeclaration(identifier, parameters, body, returnType, IsPublic: isPublic, GenericParams: decoratorGenerics);
    fnDecl.TupleReturnType = tupleReturnType;
    return fnDecl;
  }

  private static StructDeclaration ParseStructDeclaration(TokenStream stream, List<GenericParam>? decoratorGenerics = null, bool isPublic = false) {
    if (!stream.Consume().IsKeyword(KeywordType.Struct)) {
      throw new Exception("PARSER: Expected 'struct' keyword");
    }

    var identifier = ConsumeIdentifier(stream);
    List<GenericParam>? genericParams = decoratorGenerics;

    if (genericParams == null && stream.Current.IsMarker(MarkerType.BracketBegin)) {
      stream.Consume(); // consume '['
      genericParams = new List<GenericParam>();
      while (!stream.Current.IsMarker(MarkerType.BracketEnd)) {
        var name = ConsumeIdentifier(stream);
        string? constraint = null;
        if (stream.Current.IsKeyword(KeywordType.Is)) {
          stream.Consume(); // consume 'is'
          constraint = ConsumeTyping(stream);
        }
        genericParams.Add(new GenericParam(name, constraint));
        if (stream.Current.IsMarker(MarkerType.Comma)) {
          stream.Consume();
        }
      }
      if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
        throw new Exception("PARSER: Expected ']' to close generic parameters");
      }
    }

    var fields = new List<VariableDeclaration>();
    var methods = new List<FunctionDeclaration>();
    var inheritedStructs = new List<string>();

    ConsumeAttributesAndMethods(stream, fields, methods, inheritedStructs);

    return new StructDeclaration(identifier, fields, methods, inheritedStructs, genericParams, IsPublic: isPublic);
  }

  private static InterfaceDeclaration ParseInterfaceDeclaration(TokenStream stream, List<GenericParam>? decoratorGenerics = null, bool isPublic = false) {
    if (!stream.Consume().IsKeyword(KeywordType.Interface)) {
      throw new Exception("PARSER: Expected 'interface' keyword");
    }

    var identifier = ConsumeIdentifier(stream);
    var fields = new List<VariableDeclaration>();
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
      } else if (stream.Current.Type == TokenType.Identifier) {
        var propIdentifier = ConsumeIdentifier(stream);
        var typing = ConsumeTyping(stream);

        fields.Add(new VariableDeclaration(VariableKind.Property, propIdentifier, null, typing));
      } else {
        throw new Exception($"PARSER: Expected method signature or property declaration in interface body, got {stream.Current}");
      }

      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
      }
    }

    return new InterfaceDeclaration(identifier, fields, methods, decoratorGenerics, IsPublic: isPublic);
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

  private static YieldStatement ParseYieldStatement(TokenStream stream) {
    if (!stream.Consume().IsKeyword(KeywordType.Yield)) {
      throw new Exception("PARSER: Expected 'yield' keyword");
    }
    var val = ParseExpression(stream);
    return new YieldStatement(val);
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

    Statement body;
    if (stream.Current.IsOperator(OperatorType.Arrow)) {
      stream.Consume(); // consume '=>'
      body = ParseExpression(stream);
    } else {
      body = ParseBlock(stream);
    }
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

    var conditions = new List<Pattern>();
    while (stream.HasNext) {
      conditions.Add(ParsePattern(stream));

      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
        continue;
      }

      if (stream.Current.IsMarker(MarkerType.BlockBegin) || stream.Current.IsOperator(OperatorType.Arrow)) {
        break;
      }
    }

    Statement body;
    if (stream.Current.IsOperator(OperatorType.Arrow)) {
      stream.Consume(); // consume '=>'
      body = ParseExpression(stream);
    } else {
      body = ParseBlock(stream);
    }
    return new WhenStatement(conditions, body);
  }

  private static UnionDeclaration ParseUnionDeclaration(TokenStream stream, List<GenericParam>? decoratorGenerics, bool isPublic = false) {
    if (!stream.Consume().IsKeyword(KeywordType.Union)) {
      throw new Exception("PARSER: Expected 'union' keyword");
    }

    var identifier = ConsumeIdentifier(stream);
    List<GenericParam>? genericParams = decoratorGenerics;

    if (genericParams == null && stream.Current.IsMarker(MarkerType.BracketBegin)) {
      stream.Consume(); // consume '['
      genericParams = new List<GenericParam>();
      while (!stream.Current.IsMarker(MarkerType.BracketEnd)) {
        var name = ConsumeIdentifier(stream);
        string? constraint = null;
        if (stream.Current.IsKeyword(KeywordType.Is)) {
          stream.Consume(); // consume 'is'
          constraint = ConsumeTyping(stream);
        }
        genericParams.Add(new GenericParam(name, constraint));
        if (stream.Current.IsMarker(MarkerType.Comma)) {
          stream.Consume();
        }
      }
      if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
        throw new Exception("PARSER: Expected ']' to close generic parameters");
      }
    }

    if (!stream.Consume().IsMarker(MarkerType.BlockBegin)) {
      throw new Exception("PARSER: Expected '{' after union identifier");
    }

    var variants = new List<UnionVariant>();
    while (true) {
      if (stream.Current.IsMarker(MarkerType.BlockEnd)) {
        stream.Consume();
        break;
      }

      var variantName = ConsumeIdentifier(stream);
      var associatedTypes = new List<string>();

      if (stream.Current.IsMarker(MarkerType.ParenthesisBegin)) {
        stream.Consume(); // consume '('
        while (stream.HasNext && !stream.Current.IsMarker(MarkerType.ParenthesisEnd)) {
          associatedTypes.Add(ConsumeTyping(stream));
          if (stream.Current.IsMarker(MarkerType.Comma)) {
            stream.Consume();
          }
        }
        if (!stream.Consume().IsMarker(MarkerType.ParenthesisEnd)) {
          throw new Exception($"PARSER: Expected ')' in union variant '{variantName}'");
        }
      }

      variants.Add(new UnionVariant(variantName, associatedTypes));

      // Optional comma or newline separation between variants
      if (stream.Current.IsMarker(MarkerType.Comma)) {
        stream.Consume();
      }
    }

    return new UnionDeclaration(identifier, variants, genericParams, IsPublic: isPublic);
  }

  private static Pattern ParsePattern(TokenStream stream) {
    if (stream.Current.Type == TokenType.Identifier) {
      var id = stream.Current.Data;
      int offset = 1;
      if (stream.Peek(offset).IsMarker(MarkerType.BracketBegin)) {
        offset++;
        int depth = 1;
        while (stream.Peek(offset).Type != TokenType.Illegal && depth > 0) {
          if (stream.Peek(offset).IsMarker(MarkerType.BracketBegin)) depth++;
          else if (stream.Peek(offset).IsMarker(MarkerType.BracketEnd)) depth--;
          offset++;
        }
      }
      bool isVariantPattern = stream.Peek(offset).IsOperator(OperatorType.StaticMemberAccess);

      if (isVariantPattern) {
        id = stream.Consume().Data;
        if (stream.Current.IsMarker(MarkerType.BracketBegin)) {
          stream.Consume(); // consume '['
          var subTypes = new List<string>();
          while (!stream.Current.IsMarker(MarkerType.BracketEnd)) {
            subTypes.Add(ConsumeTyping(stream));
            if (stream.Current.IsMarker(MarkerType.Comma)) {
              stream.Consume();
            }
          }
          if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
            throw new Exception("PARSER: Expected ']' to close generic type arguments in variant pattern");
          }
          id += "[" + string.Join(", ", subTypes) + "]";
        }
        
        if (!stream.Consume().IsOperator(OperatorType.StaticMemberAccess)) {
          throw new Exception("PARSER: Expected '::' in variant pattern");
        }
        
        var variantName = ConsumeIdentifier(stream);
        var subPatterns = new List<Pattern>();
        
        if (stream.Current.IsMarker(MarkerType.ParenthesisBegin)) {
          stream.Consume(); // consume '('
          while (stream.HasNext && !stream.Current.IsMarker(MarkerType.ParenthesisEnd)) {
            subPatterns.Add(ParsePattern(stream));
            if (stream.Current.IsMarker(MarkerType.Comma)) {
              stream.Consume();
            }
          }
          if (!stream.Consume().IsMarker(MarkerType.ParenthesisEnd)) {
            throw new Exception($"PARSER: Expected ')' in variant pattern '{id}::{variantName}'");
          }
        }
        
        return new VariantPattern(id, variantName, subPatterns);
      }
      
      // Check if it's boolean literal
      if (id == "true" || id == "false") {
        var token = stream.Consume();
        var lit = new LiteralExpression(token.Data, LiteralType.Boolean);
        return new LiteralPattern(lit);
      }
      
      // Otherwise it is an identifier binding
      stream.Consume(); // consume the identifier
      return new IdentifierPattern(id);
    }
    
    // Otherwise parse it as a literal expression pattern
    var expr = ParseExpression(stream, false);
    return new LiteralPattern(expr);
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

      List<GenericParam>? methodGenerics = null;
      if (stream.Current.IsMarker(MarkerType.At)) {
        stream.Consume(); // consume '@'
        var dec = ConsumeIdentifier(stream);
        if (dec == "generic") {
          methodGenerics = ParseGenericParamsList(stream);
        } else {
          throw new Exception($"PARSER: Unknown decorator '@{dec}' in struct method");
        }
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
        var fn = ParseFunctionDeclaration(stream, methodGenerics);
        if (isStatic) {
          fn = fn with { IsStatic = true };
        }
        methods.Add(fn);
      } else {
        if (isStatic) {
          throw new Exception("PARSER: 'static' modifier cannot be applied to struct fields.");
        }
        if (methodGenerics != null) {
          throw new Exception("PARSER: '@generic' cannot be applied to struct fields.");
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

  private static TestDeclaration ParseTestDeclaration(TokenStream stream) {
    stream.Consume(); // consume 'test'
    if (stream.Current.Type != TokenType.Literal || stream.Current.Literal != LiteralType.String) {
      throw new Exception("PARSER: Expected string literal for test description");
    }
    var description = stream.Consume().Data;
    // Strip quotes from description literal if they are part of Data
    if (description.StartsWith("\"") && description.EndsWith("\"")) {
      description = description.Substring(1, description.Length - 2);
    }
    if (!stream.Current.IsMarker(MarkerType.BlockBegin)) {
      throw new Exception("PARSER: Expected '{' to begin test body");
    }
    var body = ParseBlock(stream);
    return new TestDeclaration(description, body);
  }

  private static AssertStatement ParseAssertStatement(TokenStream stream) {
    stream.Consume(); // consume 'assert'
    var expr = ParseExpression(stream);
    return new AssertStatement(expr);
  }
}
