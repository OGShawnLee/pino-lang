#include <stdexcept>
#include "Parser.h"
#include "Common.h"
#include "Statement/Variable.h"
#include "Statement/Expression/Value.h"
#include "Token/Keyword.h"
#include "Token/Literal.h"
#include "Token/Marker.h"
#include "Token/Operator.h"

std::shared_ptr<Expression> Parser::consume_assignment(Stream &stream) {
  if (Operator::from_base(stream.consume())->get_marker_type() != OPERATOR_TYPE::ASSIGNMENT) {
    throw std::runtime_error("PARSER: Expected Assignment Operator");
  }

  return parse_expression(stream);
}

std::vector<std::shared_ptr<Expression>> Parser::consume_arguments(Stream &stream) {
  if (not Marker::is_target_marker_type(stream.consume(), MARKER_TYPE::PARENTHESIS_BEGIN)) {
    throw std::runtime_error("PARSER: Expected Open Parenthesis");
  }

  std::vector<std::shared_ptr<Expression>> arguments;

  while (true) {
    if (Marker::is_target_marker_type(stream.current(), MARKER_TYPE::PARENTHESIS_END)) {
      stream.increase_index();
      return arguments;
    }

    arguments.push_back(parse_expression(stream));

    if (Marker::is_target_marker_type(stream.current(), MARKER_TYPE::COMMA)) {
      stream.increase_index();
    }
  }
}

KEYWORD_TYPE Parser::consume_keyword(Stream &stream) {
  const std::shared_ptr<Token> &current = stream.consume();

  if (current->get_type() != TOKEN_TYPE::KEYWORD) {
    throw std::runtime_error("PARSER: Expected Keyword");
  }

  return dynamic_cast<Keyword*>(current.get())->get_keyword();
}

std::shared_ptr<Variable> Parser::consume_parameter(Stream &stream) {
  std::shared_ptr<Identifier> identifier = parse_identifier(stream);
  return std::make_shared<Variable>(identifier->get_name(), consume_typing(stream), VARIABLE_KIND::PARAMETER);
}

std::vector<std::shared_ptr<Variable>> Parser::consume_parameters(Stream &stream) {
  std::vector<std::shared_ptr<Variable>> parameters;

  if (Marker::is_target_marker_type(stream.current(), MARKER_TYPE::BLOCK_BEGIN)) {
    return parameters;
  }
  
  if (not Marker::is_target_marker_type(stream.consume(), MARKER_TYPE::PARENTHESIS_BEGIN)) {
    throw std::runtime_error("PARSER: Expected Open Parenthesis");
  }
  
  if (Marker::is_target_marker_type(stream.current(), MARKER_TYPE::PARENTHESIS_END)) {
    stream.increase_index();
    return parameters;
  }
  
  while (true) {
    if (Marker::is_target_marker_type(stream.current(), MARKER_TYPE::PARENTHESIS_END)) {
      stream.increase_index();
      return parameters;
    }
    
    parameters.push_back(consume_parameter(stream));
    
    if (Marker::is_target_marker_type(stream.current(), MARKER_TYPE::COMMA)) {
      stream.increase_index();
    }
  }
}

std::string Parser::consume_typing(Stream &stream) {
  const std::shared_ptr<Token> &current = stream.consume();

  if (not current->is_given_type(TOKEN_TYPE::IDENTIFIER)) {
    throw std::runtime_error("PARSER: Expected Typing");
  }

  return current->get_data();
}

bool Parser::is_expression(Stream &stream) {
  return 
    stream.current()->is_given_type(TOKEN_TYPE::IDENTIFIER, TOKEN_TYPE::LITERAL) or
    is_function_call(stream);
}

bool Parser::is_function_call(Stream &stream) {
  return stream.current()->is_given_type(TOKEN_TYPE::IDENTIFIER) and stream.is_next([](const std::shared_ptr<Token> &token) {
    return 
      token->is_given_type(TOKEN_TYPE::MARKER) and 
      Marker::from_base(token)->is_given_marker_type(MARKER_TYPE::PARENTHESIS_BEGIN);
  });
}


std::shared_ptr<Statement> Parser::parse_block(Stream &stream) {
  if (not Marker::is_target_marker_type(stream.consume(), MARKER_TYPE::BLOCK_BEGIN)) {
    throw std::runtime_error("PARSER: Expected Open Brace");
  }
  
  std::shared_ptr<Statement> block = std::make_shared<Statement>(STATEMENT_TYPE::BLOCK);
  
  while (stream.has_next()) {
    if (Marker::is_target_marker_type(stream.current(), MARKER_TYPE::BLOCK_END)) {
      stream.increase_index();
      return block;
    }

    const std::shared_ptr<Token> &token = stream.current();

    switch (token->get_type()) {
      case TOKEN_TYPE::KEYWORD:
        switch (Keyword::from_base(token)->get_keyword()) {
          case KEYWORD_TYPE::CONSTANT:
          case KEYWORD_TYPE::VARIABLE:
            block->push(parse_variable(stream));
            continue;
          case KEYWORD_TYPE::FUNCTION:
            block->push(parse_function(stream));
            continue;
        }
        break;
      case TOKEN_TYPE::IDENTIFIER:
      case TOKEN_TYPE::LITERAL:
        block->push(std::move(parse_expression(stream)));
        continue;
      case TOKEN_TYPE::ILLEGAL:
        println("Illegal Token");
        token->print();
        break;
    }

    stream.increase_index();
  }

  throw std::runtime_error("PARSER: Expected Close Brace");
}

std::shared_ptr<Function> Parser::parse_function(Stream &stream) {
  if (consume_keyword(stream) != KEYWORD_TYPE::FUNCTION) {
    throw std::runtime_error("PARSER: Expected Function Keyword");
  }

  std::shared_ptr<Identifier> identifier = parse_identifier(stream);
  std::vector<std::shared_ptr<Variable>> parameters = consume_parameters(stream);  
  std::shared_ptr<Statement> block = parse_block(stream);

  return std::make_shared<Function>(identifier->get_name(), parameters, block->get_children());
}

std::shared_ptr<FunctionCall> Parser::parse_function_call(Stream &stream) {
  std::shared_ptr<Identifier> identifier = parse_identifier(stream);
  std::vector<std::shared_ptr<Expression>> arguments = consume_arguments(stream);
  return std::make_shared<FunctionCall>(identifier->get_name(), arguments);
}

std::shared_ptr<Identifier> Parser::parse_identifier(Stream &stream) {
  const std::shared_ptr<Token> &current = stream.consume();

  if (current->get_type() != TOKEN_TYPE::IDENTIFIER) {
    throw std::runtime_error("PARSER: Expected Identifier");
  }

  return std::make_shared<Identifier>(current->get_data());
}

std::shared_ptr<Expression> Parser::parse_expression(Stream &stream) {
  if (not is_expression(stream)) {
    throw std::runtime_error("PARSER: Expected Expression");
  }

  std::shared_ptr<Expression> expression = nullptr;

  if (is_function_call(stream)) {
    expression = parse_function_call(stream);
  } else if (stream.current()->is_given_type(TOKEN_TYPE::IDENTIFIER)) {
    expression = parse_identifier(stream);
  } else {
    expression = std::make_shared<Value>(
      static_cast<const Literal&>(*stream.consume())
    );
  }

  return expression;
}

std::shared_ptr<Variable> Parser::parse_variable(Stream &stream) {
  KEYWORD_TYPE keyword = consume_keyword(stream);

  if (keyword != KEYWORD_TYPE::VARIABLE and keyword != KEYWORD_TYPE::CONSTANT) {
    throw std::runtime_error("PARSER: Invalid Variable Declaration");
  }

  std::shared_ptr<Identifier> identifier = parse_identifier(stream);
  std::shared_ptr<Expression> value = consume_assignment(stream);

  return std::make_shared<Variable>(
    identifier->get_name(), 
    value,
    "PENDING",
    keyword == KEYWORD_TYPE::CONSTANT ? VARIABLE_KIND::CONSTANT : VARIABLE_KIND::VARIABLE
  );
}

std::shared_ptr<Statement> Parser::parse_line(const std::string &line) {
  Stream stream = Lexer::lex_line(line);

  while (stream.has_next()) {
    const std::shared_ptr<Token> &current = stream.current();
    switch (current->get_type()) {
      case TOKEN_TYPE::KEYWORD:
        switch (Keyword::from_base(current)->get_keyword()) {
          case KEYWORD_TYPE::CONSTANT:
          case KEYWORD_TYPE::VARIABLE:
            return parse_variable(stream);
          case KEYWORD_TYPE::FUNCTION:
            return parse_function(stream);
        }
      case TOKEN_TYPE::LITERAL:
      case TOKEN_TYPE::IDENTIFIER:
        return parse_expression(stream);
    }
  }

  return nullptr;
}
