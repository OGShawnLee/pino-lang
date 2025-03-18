#pragma once

#include "./Parser.h"
#include "./statement/Variable.cpp"
#include "./statement/expression/Value.h"
#include "../lexer/Lexer.cpp"

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
        }
      case TOKEN_TYPE::LITERAL:
      case TOKEN_TYPE::IDENTIFIER:
        return parse_expression(stream);
    }
  }

  return nullptr;
}
