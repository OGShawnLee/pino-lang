#include "Parser.h"
#include "Lexer.cpp"
#include "Statement.cpp"
#include "Declaration.cpp"
#include "Expression.cpp"

bool Parser::is_expression(Lexer::Stream &collection) {
  return 
    is_function_lambda(collection)
    or is_vector(collection)
    or collection.current().is_given_keyword(Token::Keyword::IF)
    or collection.current().is_given_type(Token::Type::IDENTIFIER, Token::Type::LITERAL);
}

bool Parser::is_function_call(Lexer::Stream &collection) {
  return collection.current().is_given_type(Token::Type::IDENTIFIER) && collection.is_next([](const Token &token) {
    return token.is_given_marker(Token::Marker::PARENTHESIS_BEGIN);
  });
}

bool Parser::is_function_lambda(Lexer::Stream &collection) {
  return collection.current().is_given_keyword(Token::Keyword::FUNCTION) and collection.is_next([](const Token &token) {
    return token.is_given_marker(Token::Marker::PARENTHESIS_BEGIN, Token::Marker::BLOCK_BEGIN);
  });
}

bool Parser::is_struct_instance(Lexer::Stream &collection) {
  const Token &current = collection.current();
  return
    current.is_given_type(Token::Type::IDENTIFIER) and
    isupper(current.get_value()[0]) and
    collection.is_next([](const Token &token) {
      return token.is_given_marker(Token::Marker::BLOCK_BEGIN);
    });
}

bool Parser::is_vector(Lexer::Stream &collection) {
  return collection.current().is_given_marker(Token::Marker::BRACKET_BEGIN);
}

std::vector<std::unique_ptr<Expression>> Parser::consume_arguments(Lexer::Stream &collection) {
  if (not collection.consume().is_given_marker(Token::Marker::PARENTHESIS_BEGIN)) {
    throw std::runtime_error("PARSER: Expected Open Parenthesis");
  }

  std::vector<std::unique_ptr<Expression>> arguments;

  while (true) {
    if (collection.current().is_given_marker(Token::Marker::PARENTHESIS_END)) {
      collection.next();
      return arguments;
    }

    arguments.push_back(parse_expression(collection));

    if (collection.current().is_given_marker(Token::Marker::COMMA)) {
      collection.next();
    }
  }
}

std::unique_ptr<Variable> Parser::consume_attribute(Lexer::Stream &collection) {
  std::string identifier = consume_identifier(collection);
  return std::make_unique<Variable>(Variable::Kind::VARIABLE_DECLARATION, identifier, consume_typing(collection));
}

void Parser::consume_attributes_and_methods(
  Lexer::Stream &collection,
  std::vector<std::unique_ptr<Variable>> &attributes,
  std::vector<std::unique_ptr<Function>> &methods
) {
  if (not collection.consume().is_given_marker(Token::Marker::BLOCK_BEGIN)) {
    throw std::runtime_error("PARSER: Expected Open Brace");
  }

  while (true) {
    if (collection.current().is_given_marker(Token::Marker::BLOCK_END)) {
      collection.next();
      
      if (attributes.empty()) {
        println("WARNING: Empty Struct Declaration");
      }

      break;
    }

    if (collection.current().is_given_keyword(Token::Keyword::FUNCTION)) {
      methods.push_back(parse_function(collection));
    } else {
      attributes.push_back(consume_attribute(collection));
    }

    if (collection.current().is_given_marker(Token::Marker::COMMA)) {
      collection.next();
    }
  }
}

std::unique_ptr<Expression> Parser::consume_assignment(Lexer::Stream &collection) {
  if (not collection.consume().is_given_operator(Token::Operator::ASSIGNMENT)) {
    throw std::runtime_error("PARSER: Expected Assignment Operator");
  }

  return parse_expression(collection);
}

std::string Parser::consume_enum_member(Lexer::Stream &collection) {
  const Token &current = collection.consume();

  if (not current.is_given_type(Token::Type::IDENTIFIER)) {
    throw std::runtime_error("PARSER: Expected Enum Member");
  }

  return current.get_value();
}

std::vector<std::string> Parser::consume_enum_members(Lexer::Stream &collection) {
  if (not collection.consume().is_given_marker(Token::Marker::BLOCK_BEGIN)) {
    throw std::runtime_error("PARSER: Expected Open Brace");
  }

  std::vector<std::string> members;

  while (true) {
    if (collection.current().is_given_marker(Token::Marker::BLOCK_END)) {
      collection.next();

      if (members.empty()) {
        println("WARNING: Empty Enum Declaration");
      }

      return members;
    }

    members.push_back(consume_enum_member(collection));

    if (collection.current().is_given_marker(Token::Marker::COMMA)) {
      collection.next();
    }
  }
}

Token::Keyword Parser::consume_keyword(Lexer::Stream &collection) {
  const Token &current = collection.consume();

  if (not current.is_given_type(Token::Type::KEYWORD)) {
    throw std::runtime_error("PARSER: Expected Keyword");
  }

  return current.get_keyword();
}

std::string Parser::consume_identifier(Lexer::Stream &collection) {
  const Token &current = collection.consume();

  if (not current.is_given_type(Token::Type::IDENTIFIER)) {
    throw std::runtime_error("PARSER: Expected Identifier");
  }

  return current.get_value();
}

std::unique_ptr<Variable> Parser::consume_parameter(Lexer::Stream &collection) {
  std::string identifier = consume_identifier(collection);
  return std::make_unique<Variable>(Variable::Kind::PARAMETER_DECLARATION, identifier, consume_typing(collection));
}

std::vector<std::unique_ptr<Variable>> Parser::consume_parameters(Lexer::Stream &collection) {
  std::vector<std::unique_ptr<Variable>> parameters;
  
  if (collection.current().is_given_marker(Token::Marker::BLOCK_BEGIN)) {
    return parameters;
  }

  if (not collection.consume().is_given_marker(Token::Marker::PARENTHESIS_BEGIN)) {
    throw std::runtime_error("PARSER: Expected Open Parenthesis");
  }

  if (collection.current().is_given_marker(Token::Marker::PARENTHESIS_END)) {
    collection.next();
    return parameters;
  }

  while (true) {
    if (collection.current().is_given_marker(Token::Marker::PARENTHESIS_END)) {
      collection.next();
      return parameters;
    }

    parameters.push_back(consume_parameter(collection));

    if (collection.current().is_given_marker(Token::Marker::COMMA)) {
      collection.next();
    }
  }
}

std::unique_ptr<Variable> Parser::consume_property(Lexer::Stream &collection) {
  std::string identifier = consume_identifier(collection);

  if (not collection.consume().is_given_operator(Token::Operator::MEMBER_ACCESS)) {
    throw std::runtime_error("PARSER: Expected Colon");
  }

  return std::make_unique<Variable>(Variable::Kind::PROPERTY_DECLARATION, identifier, parse_expression(collection));
}

std::vector<std::unique_ptr<Variable>> Parser::consume_properties(Lexer::Stream &collection) {
  if (not collection.consume().is_given_marker(Token::Marker::BLOCK_BEGIN)) {
    throw std::runtime_error("PARSER: Expected Open Brace");
  }

  std::vector<std::unique_ptr<Variable>> properties;

  while (collection.has_next()) {
    if (collection.current().is_given_marker(Token::Marker::BLOCK_END)) {
      collection.next();

      if (properties.empty()) {
        println("WARNING: Empty Struct Literal");
      }

      return properties;
    }

    properties.push_back(consume_property(collection));

    if (collection.current().is_given_marker(Token::Marker::COMMA)) {
      collection.next();
    }
  }

  throw std::runtime_error("PARSER: Expected Close Brace");
}

std::string Parser::consume_typing(Lexer::Stream &collection) {
  const Token &current = collection.consume();

  if (not current.is_given_type(Token::Type::IDENTIFIER)) {
    throw std::runtime_error("PARSER: Expected Typing");
  }

  return current.get_value();
}

std::unique_ptr<Variable> Parser::extract_property(std::vector<std::unique_ptr<Variable>> &properties, const std::string &identifier) {
  auto it = std::find_if(properties.begin(), properties.end(), [&identifier](const std::unique_ptr<Variable> &property) {
    return property->get_identifier() == identifier;
  });

  if (it != properties.end()) {
    std::unique_ptr<Variable> property = std::move(*it);
    properties.erase(it);
    return property;
  }

  return nullptr;
}

std::unique_ptr<Enum> Parser::parse_enum(Lexer::Stream &collection) {
  if (consume_keyword(collection) != Token::Keyword::ENUM) {
    throw std::runtime_error("PARSER: Invalid Enum Declaration");
  }

  std::string identifier = consume_identifier(collection);
  return std::make_unique<Enum>(identifier, consume_enum_members(collection));
}

std::unique_ptr<Expression> Parser::parse_expression(Lexer::Stream &collection) {
  if (not is_expression(collection)) {
    throw std::runtime_error("PARSER: Expected Expression");
  }

  std::unique_ptr<Expression> expression;

  if (is_function_call(collection)) {
    expression = parse_function_call(collection);
  } else if (is_function_lambda(collection)) {
    expression = parse_function_lambda(collection);
  } else if (is_vector(collection)) {
    expression = parse_vector(collection);
  } else if (is_struct_instance(collection)) {
    expression = parse_struct_instance(collection);
  } else if (collection.current().is_given_keyword(Token::Keyword::IF)) {
    expression = parse_if_expression(collection);
  } else {
    bool is_identifier = collection.current().is_given_type(Token::Type::IDENTIFIER);
    expression = std::make_unique<Expression>(
      is_identifier ? Expression::Kind::IDENTIFIER : Expression::Kind::LITERAL,
      collection.consume().get_value()
    );
  }

  if (collection.has_next() and collection.current().is_given_type(Token::Type::OPERATOR)) {
    std::string operation = collection.consume().get_value();
    std::unique_ptr<Expression> right = parse_expression(collection);
    expression = std::make_unique<BinaryExpression>(std::move(expression), operation, std::move(right));
  }

  return expression;
}

std::unique_ptr<ElseStatement> Parser::parse_else_statement(Lexer::Stream &collection) {
  if (consume_keyword(collection) != Token::Keyword::ELSE) {
    throw std::runtime_error("PARSER: Invalid Else Statement");
  }

  return std::make_unique<ElseStatement>(parse_block(collection));
}

std::unique_ptr<Function> Parser::parse_function(Lexer::Stream &collection) {
  if (consume_keyword(collection) != Token::Keyword::FUNCTION) {
    throw std::runtime_error("PARSER: Invalid Function Declaration");
  }

  std::string identifier = consume_identifier(collection);
  std::vector<std::unique_ptr<Variable>> parameters = consume_parameters(collection);
  std::unique_ptr<Statement> body = parse_block(collection);

  return std::make_unique<Function>(identifier, std::move(parameters), std::move(body));
}

std::unique_ptr<FunctionCall> Parser::parse_function_call(Lexer::Stream &collection) {
  std::string callee = consume_identifier(collection);
  return std::make_unique<FunctionCall>(callee, consume_arguments(collection));
}

std::unique_ptr<FunctionLambda> Parser::parse_function_lambda(Lexer::Stream &collection) {
  if (consume_keyword(collection) != Token::Keyword::FUNCTION) {
    throw std::runtime_error("PARSER: Invalid Function Lambda Declaration");
  }

  std::vector<std::unique_ptr<Variable>> parameters = consume_parameters(collection);
  std::unique_ptr<Statement> body = parse_block(collection);

  return std::make_unique<FunctionLambda>(std::move(parameters), std::move(body));
}

std::unique_ptr<IfStatement> Parser::parse_if_statement(Lexer::Stream &collection) {
  if (consume_keyword(collection) != Token::Keyword::IF) {
    throw std::runtime_error("PARSER: Invalid If Statement");
  }

  std::unique_ptr<Expression> condition = parse_expression(collection);
  std::unique_ptr<Statement> body = parse_block(collection);
  std::unique_ptr<Statement> consequent;

  if (collection.current().is_given_keyword(Token::Keyword::ELSE)) {
    if (collection.is_next([](const Token &token) {
      return token.is_given_keyword(Token::Keyword::IF);
    })) {
      collection.next();
      consequent = parse_if_statement(collection);
    } else {
      consequent = parse_else_statement(collection);
    } 
  }

  return std::make_unique<IfStatement>(std::move(condition), std::move(body), std::move(consequent));
}

std::unique_ptr<TernaryExpression> Parser::parse_if_expression(Lexer::Stream &collection) {
  if (consume_keyword(collection) != Token::Keyword::IF) {
    throw std::runtime_error("PARSER: Invalid If Expression");
  }

  std::unique_ptr<Expression> condition = parse_expression(collection);

  if (consume_keyword(collection) != Token::Keyword::THEN) {
    throw std::runtime_error("PARSER: Invalid If Expression");
  }

  std::unique_ptr<Expression> consequent = parse_expression(collection);
  
  if (consume_keyword(collection) != Token::Keyword::ELSE) {
    throw std::runtime_error("PARSER: Invalid If Expression");
  }

  std::unique_ptr<Expression> alternate = parse_expression(collection);

  return std::make_unique<TernaryExpression>(std::move(condition), std::move(consequent), std::move(alternate));
}

std::unique_ptr<Loop> Parser::parse_loop(Lexer::Stream &collection) {
  if (consume_keyword(collection) != Token::Keyword::LOOP) {
    throw std::runtime_error("PARSER: Invalid Loop Declaration");
  }

  if (collection.current().is_given_marker(Token::Marker::BLOCK_BEGIN)) {
    return std::make_unique<Loop>(Loop::Kind::INFINITE_LOOP, nullptr, nullptr, parse_block(collection));
  }

  std::unique_ptr<Expression> begin = parse_expression(collection);

  if (collection.current().is_given_marker(Token::Marker::BLOCK_BEGIN)) {
    return std::make_unique<Loop>(Loop::Kind::FOR_TIMES_LOOP, std::move(begin), nullptr, parse_block(collection));
  }

  if (consume_keyword(collection) != Token::Keyword::IN) {
    throw std::runtime_error("PARSER: Invalid Loop Declaration");
  }

  std::unique_ptr<Expression> end = parse_expression(collection);
  std::unique_ptr<Statement> children = parse_block(collection);
  return std::make_unique<Loop>(Loop::Kind::FOR_IN_LOOP, std::move(begin), std::move(end), std::move(children));
}

std::unique_ptr<MatchStatement> Parser::parse_match_statement(Lexer::Stream &collection) {
  if (consume_keyword(collection) != Token::Keyword::MATCH) {
    throw std::runtime_error("PARSER: Invalid Match Statement");
  }

  std::unique_ptr<Expression> value = parse_expression(collection);

  if (not collection.consume().is_given_marker(Token::Marker::BLOCK_BEGIN)) {
    throw std::runtime_error("PARSER: Expected Match Open Brace");
  }

  std::vector<std::unique_ptr<WhenStatement>> branches;
  std::unique_ptr<ElseStatement> alternate;

  while (collection.has_next()) {
    if (collection.current().is_given_marker(Token::Marker::BLOCK_END)) {
      collection.next();
      break;
    }

    if (collection.current().is_given_keyword(Token::Keyword::ELSE)) {
      alternate = parse_else_statement(collection);
      break;
    } 

    if (collection.current().is_given_keyword(Token::Keyword::WHEN)) {
      branches.push_back(parse_when_statement(collection));
      continue;
    }

    throw std::runtime_error("PARSER: Invalid Match Statement");
  }

  if (branches.empty() and not alternate) {
    throw std::runtime_error("PARSER: Empty Match Statement");
  }

  return std::make_unique<MatchStatement>(std::move(value), std::move(branches), std::move(alternate));
}

std::unique_ptr<Variable> Parser::parse_variable(Lexer::Stream &collection) {
  Token::Keyword keyword = consume_keyword(collection);

  if (keyword != Token::Keyword::CONSTANT and keyword != Token::Keyword::VARIABLE) {
    throw std::runtime_error("PARSER: Invalid Variable Declaration");
  }

  std::string identifier = consume_identifier(collection);
  std::unique_ptr<Variable> variable = std::make_unique<Variable>(
    keyword == Token::Keyword::CONSTANT ? Variable::Kind::CONSTANT_DECLARATION : Variable::Kind::VARIABLE_DECLARATION,
    identifier,
    consume_assignment(collection)
  );

  return variable;
}

std::unique_ptr<Vector> Parser::parse_vector(Lexer::Stream &collection) {
  if (not collection.consume().is_given_marker(Token::Marker::BRACKET_BEGIN)) {
    throw std::runtime_error("PARSER: Invalid Vector Declaration");
  }

  if (collection.current().is_given_marker(Token::Marker::BRACKET_END)) {
    collection.next();

    std::string typing = consume_typing(collection);

    if (collection.current().is_given_marker(Token::Marker::BLOCK_BEGIN)) {
      std::vector<std::unique_ptr<Variable>> properties = consume_properties(collection);
      std::unique_ptr<Variable> len = extract_property(properties, "len");
      std::unique_ptr<Variable> init = extract_property(properties, "init");

      if (not len and not init) {
        throw std::runtime_error("PARSER: Empty Vector Init Block");
      }

      return std::make_unique<Vector>(len->extract_value(), init->extract_value(), typing);
    }

    return std::make_unique<Vector>(nullptr, nullptr, typing);
  }

  std::unique_ptr<Vector> list = std::make_unique<Vector>();
  
  while (collection.has_next()) {
    if (collection.current().is_given_marker(Token::Marker::BRACKET_END)) {
      collection.next();
      return list;
    }

    if (collection.current().is_given_marker(Token::Marker::COMMA)) {
      collection.next();
      continue;
    }

    list->push(parse_expression(collection));
  }

  return list;
}

std::unique_ptr<Struct> Parser::parse_struct(Lexer::Stream &collection) {
  if (consume_keyword(collection) != Token::Keyword::STRUCT) {
    throw std::runtime_error("PARSER: Invalid Struct Declaration");
  }

  std::string identifier = consume_identifier(collection);
  std::vector<std::unique_ptr<Variable>> fields;
  std::vector<std::unique_ptr<Function>> methods;
  
  consume_attributes_and_methods(collection, fields, methods);

  return std::make_unique<Struct>(identifier, std::move(fields), std::move(methods));
}

std::unique_ptr<StructInstance> Parser::parse_struct_instance(Lexer::Stream &collection) {
  std::string struct_name = consume_identifier(collection);
  return std::make_unique<StructInstance>(struct_name, consume_properties(collection));
}

std::unique_ptr<Return> Parser::parse_return(Lexer::Stream &collection) {
  if (consume_keyword(collection) != Token::Keyword::RETURN) {
    throw std::runtime_error("PARSER: Invalid Return Statement");
  }

  return std::make_unique<Return>(is_expression(collection) ? parse_expression(collection) : nullptr);
}

std::unique_ptr<WhenStatement> Parser::parse_when_statement(Lexer::Stream &collection) {
  if (consume_keyword(collection) != Token::Keyword::WHEN) {
    throw std::runtime_error("PARSER: Invalid When Statement");
  }

  std::vector<std::unique_ptr<Expression>> conditions;

  if (collection.current().is_given_marker(Token::Marker::BLOCK_BEGIN)) {
    throw std::runtime_error("PARSER: When Statement Without Conditions");
  }

  while (collection.has_next()) {
    conditions.push_back(parse_expression(collection));
  
    if (collection.current().is_given_marker(Token::Marker::COMMA)) {
      collection.next();
      continue;
    }

    if (collection.current().is_given_marker(Token::Marker::BLOCK_BEGIN)) {
      break;
    }
  }

  std::unique_ptr<Statement> body = parse_block(collection);
  return std::make_unique<WhenStatement>(std::move(conditions), std::move(body));
}

std::unique_ptr<Statement> Parser::parse_block(Lexer::Stream &collection) {
  if (not collection.consume().is_given_marker(Token::Marker::BLOCK_BEGIN)) {
    throw std::runtime_error("PARSER: Expected Open Brace");
  }

  std::unique_ptr<Statement> block = std::make_unique<Statement>(Statement::Type::BLOCK);

  while (collection.has_next()) {
    if (collection.current().is_given_marker(Token::Marker::BLOCK_END)) {
      collection.next();
      return block;
    }

    const Token &token = collection.current();

    switch (token.get_type()) {
      case Token::Type::KEYWORD:
        switch (token.get_keyword()) {
          case Token::Keyword::CONSTANT:
          case Token::Keyword::VARIABLE:
            block->push(std::move(parse_variable(collection)));
            continue;
          case Token::Keyword::FUNCTION:
            block->push(std::move(parse_function(collection)));
            continue;
          case Token::Keyword::STRUCT:
            block->push(std::move(parse_struct(collection)));
            continue;
          case Token::Keyword::ENUM:
            block->push(std::move(parse_enum(collection)));
            continue;
          case Token::Keyword::RETURN:
            block->push(std::move(parse_return(collection)));
            continue;
          case Token::Keyword::LOOP:
            block->push(std::move(parse_loop(collection)));
            continue;
          case Token::Keyword::IF:
            block->push(std::move(parse_if_statement(collection)));
            continue;
          case Token::Keyword::ELSE:
            throw std::runtime_error("PARSER: Else Statement Without If Statement");
          case Token::Keyword::MATCH:
            block->push(std::move(parse_match_statement(collection)));
            continue;
          case Token::Keyword::WHEN:
            throw std::runtime_error("PARSER: When Statement Outside Match Statement");
        }
        break;
      case Token::Type::IDENTIFIER:
      case Token::Type::LITERAL:
        block->push(std::move(parse_expression(collection)));
        continue;
      case Token::Type::ILLEGAL:
        println("Illegal Token");
        token.print();
        break;
    }

    collection.next();
  }

  throw std::runtime_error("PARSER: Expected Close Brace");
}

Statement Parser::parse_file(const std::string &filename) {
  Lexer::Stream collection = Lexer::lex_file(filename);
  Statement program;

  while (collection.has_next()) {
    const Token &token = collection.current();

    switch (token.get_type()) {
      case (Token::Type::KEYWORD): 
        switch (token.get_keyword()) {
          case Token::Keyword::CONSTANT:
          case Token::Keyword::VARIABLE:
            program.push(std::move(parse_variable(collection)));
            continue;
          case Token::Keyword::FUNCTION:
            program.push(std::move(parse_function(collection)));
            continue;
          case Token::Keyword::STRUCT:
            program.push(std::move(parse_struct(collection)));
            continue;
          case Token::Keyword::ENUM:
            program.push(std::move(parse_enum(collection)));
            continue;
          case Token::Keyword::RETURN:
            throw std::runtime_error("PARSER: Return Statement Outside Function");
          case Token::Keyword::LOOP:
            program.push(std::move(parse_loop(collection)));
            continue;
          case Token::Keyword::IF:
            program.push(std::move(parse_if_statement(collection)));
            continue;
          case Token::Keyword::ELSE:
            throw std::runtime_error("PARSER: Else Statement Without If Statement");
          case Token::Keyword::MATCH:
            program.push(std::move(parse_match_statement(collection)));
            continue;
          case Token::Keyword::WHEN:
            throw std::runtime_error("PARSER: When Statement Outside Match Statement");
        }
        break; 
      case Token::Type::IDENTIFIER:
      case Token::Type::LITERAL: {
        program.push(std::move(parse_expression(collection)));
        continue;
      }
      case Token::Type::ILLEGAL:
        println("Illegal Token");
        token.print();
        break;
    }

    collection.next();
  }

  return program;
}
