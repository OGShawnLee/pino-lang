#include "Expression.h"
#include "Common.h"

std::map<Expression::Kind, std::string> Expression::KIND_NAME_MAPPING = {
  {Expression::Kind::ASSIGNMENT, "Assignment"},
  {Expression::Kind::IDENTIFIER, "Identifier"},
  {Expression::Kind::LITERAL, "Literal"},
};

Expression::Expression() {
  set_type(Type::EXPRESSION);
}

Expression::Expression(Kind kind, std::string value) {
  this->kind = kind;
  this->value = value;
}

std::unique_ptr<Expression> Expression::build(Lexer::Stream &collection) {
  if (not Expression::is_expression(collection)) {
    throw std::runtime_error("PARSER: Expected Expression");
  }

  const Token &current = collection.consume();
  if (current.get_type() == Token::Type::IDENTIFIER) {
    return std::make_unique<Expression>(Expression::Kind::IDENTIFIER, current.get_value());
  } else {
    return std::make_unique<Expression>(Expression::Kind::LITERAL, current.get_value());
  }
}

bool Expression::is_expression(Lexer::Stream &collection) {
  return collection.current().is_given_type(Token::Type::IDENTIFIER, Token::Type::LITERAL);
}

bool Expression::is_binary_expression(Lexer::Stream &collection) {
  return is_expression(collection) && collection.is_next([](const Token &token) {
    return token.get_type() == Token::Type::OPERATOR;
  });
}

void Expression::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + KIND_NAME_MAPPING.at(kind) + " {");
  println(indent + "  value: " + value);
  println(indent + "}");
}