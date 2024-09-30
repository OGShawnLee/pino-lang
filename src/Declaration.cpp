#include "Declaration.h"
#include "Common.h"

std::map<Variable::Kind, std::string> Variable::KIND_NAME_MAPPING = {
  {Variable::Kind::CONSTANT_DECLARATION, "Constant Declaration"},
  {Variable::Kind::VARIABLE_DECLARATION, "Variable Declaration"},
  {Variable::Kind::PARAMETER_DECLARATION, "Parameter Declaration"},
};

void Variable::consume_keyword(Lexer::Stream &collection) {
  Lexer::Token::Keyword keyword = collection.consume().get_keyword();

  if (keyword != Lexer::Token::Keyword::CONSTANT and keyword != Lexer::Token::Keyword::VARIABLE) {
    throw std::runtime_error("PARSER: Invalid Variable Declaration");
  }

  if (keyword == Token::Keyword::CONSTANT) {
    kind = Kind::CONSTANT_DECLARATION;
    set_type(Type::CONSTANT_DECLARATION);
  } else {
    kind = Kind::VARIABLE_DECLARATION;
    set_type(Type::VARIABLE_DECLARATION);
  }
}

void Declaration::consume_identifier(Lexer::Stream &collection) {
  const Token& current = collection.consume();

  if (current.get_type() != Lexer::Token::Type::IDENTIFIER) {
    throw std::runtime_error("PARSER: Invalid Identifier");
  }

  identifier = current.get_value();
}

void Variable::consume_value(Lexer::Stream &collection) {
  if (not collection.consume().is_given_operator(Token::Operator::ASSIGNMENT)) {
    throw std::runtime_error("PARSER: Invalid Assignment Operator");
  }

  value = Expression::build(collection);
}

void Variable::consume_typing(Lexer::Stream &collection) {
  const Token& current = collection.consume();

  if (current.get_type() != Lexer::Token::Type::IDENTIFIER) {
    throw std::runtime_error("PARSER: Invalid Typing");
  }

  typing = current.get_value();
}

void Variable::print(const size_t &indentation = 0) const {
  std::string indent(indentation, ' ');

  println(indent + KIND_NAME_MAPPING.at(kind) + " {");
  println(indent + "  identifier: " + identifier);
  if (not typing.empty()) {
    println(indent + "  typing: " + typing);
  }
  if (value) {
    println(indent + "  value: {");
    value->print(indentation + 4);
    println(indent + "  }");
  }
  println(indent + "}");
}

void Function::consume_keyword(Lexer::Stream &collection) {
  const Token& current = collection.consume();

  if (current.get_keyword() != Lexer::Token::Keyword::FUNCTION) {
    throw std::runtime_error("PARSER: Invalid Function Declaration");
  }

  set_type(Type::FUNCTION_DECLARATION);
}

void Function::consume_parameter(Lexer::Stream &collection) {
  std::unique_ptr<Variable> parameter = std::make_unique<Variable>();

  parameter->consume_identifier(collection);
  parameter->consume_typing(collection);

  parameters.push_back(std::move(parameter));
}

void Function::consume_parameters(Lexer::Stream &collection) {
  if (collection.current().is_given_marker(Token::Marker::BLOCK_BEGIN)) {
    collection.next();
    return;
  }

  if (not collection.current().is_given_marker(Token::Marker::PARENTHESIS_BEGIN)) {
    throw std::runtime_error("PARSER: Expected Open Parenthesis");
  }

  collection.next();

  if (collection.current().is_given_marker(Token::Marker::PARENTHESIS_END)) {
    collection.next();
    return;
  }

  while (true) {
    if (collection.current().is_given_marker(Token::Marker::PARENTHESIS_END)) {
      collection.next();
      return;
    }
    
    consume_parameter(collection);

    if (collection.current().is_given_marker(Token::Marker::COMMA)) {
      collection.next();
      continue;
    }
  }
}

void Function::print(const size_t &indentation = 0) const {
  std::string indent(indentation, ' ');

  println(indent + "Function Declaration {");
  println(indent + "  identifier: " + identifier);
  if (not parameters.empty()) {
    println(indent + "  parameters: [");

    for (const auto &parameter : parameters) {
      parameter->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  println(indent + "}");
}
