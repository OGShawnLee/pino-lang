#include "Declaration.h"
#include "Common.h"

std::map<Variable::Kind, std::string> Variable::KIND_NAME_MAPPING = {
  {Variable::Kind::CONSTANT_DECLARATION, "Constant Declaration"},
  {Variable::Kind::VARIABLE_DECLARATION, "Variable Declaration"},
  {Variable::Kind::PARAMETER_DECLARATION, "Parameter Declaration"},
};

void Variable::consume_keyword(
  const std::vector<Lexer::Token> &collection, size_t &index
) {
  Lexer::Token::Keyword keyword = collection[index].get_keyword();

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

  index++;
}

void Declaration::consume_identifier(
  const std::vector<Lexer::Token> &collection, size_t &index
) {
  if (collection[index].get_type() != Lexer::Token::Type::IDENTIFIER) {
    throw std::runtime_error("PARSER: Invalid Identifier");
  }

  identifier = collection[index].get_value();
  index++;
}

void Variable::consume_value(
  const std::vector<Lexer::Token> &collection, size_t &index
) {
  if (not collection[index].is_given_operator(Token::Operator::ASSIGNMENT)) {
    throw std::runtime_error("PARSER: Invalid Assignment Operator");
  }

  index++;
  value = collection[index].get_value();
}

void Variable::consume_typing(
  const std::vector<Lexer::Token> &collection, size_t &index
) {
  if (collection[index].get_type() != Lexer::Token::Type::IDENTIFIER) {
    throw std::runtime_error("PARSER: Invalid Typing");
  }

  typing = collection[index].get_value();
  index++;
}

void Variable::print(const size_t &indentation = 0) const {
  std::string indent(indentation, ' ');

  println(indent + KIND_NAME_MAPPING.at(kind) + " {");
  println(indent + "  identifier: " + identifier);
  if (not typing.empty()) {
    println(indent + "  typing: " + typing);
  }
  if (not value.empty()) {
    println(indent + "  value: " + value);
  }
  println(indent + "}");
}

void Function::consume_keyword(
  const std::vector<Lexer::Token> &collection, size_t &index
) {
  if (collection[index].get_keyword() != Lexer::Token::Keyword::FUNCTION) {
    throw std::runtime_error("PARSER: Invalid Function Declaration");
  }

  set_type(Type::FUNCTION_DECLARATION);
  index++;
}

void Function::consume_parameter(
  const std::vector<Lexer::Token> &collection, size_t &index
) {
  std::unique_ptr<Variable> parameter = std::make_unique<Variable>();

  parameter->consume_identifier(collection, index);
  parameter->consume_typing(collection, index);

  parameters.push_back(std::move(parameter));
}

void Function::consume_parameters(
  const std::vector<Lexer::Token> &collection, size_t &index
) {

  if (collection[index].is_given_marker(Token::Marker::BLOCK_BEGIN)) {
    index++;
    return;
  }

  if (not collection[index].is_given_marker(Token::Marker::PARENTHESIS_BEGIN)) {
    throw std::runtime_error("PARSER: Expected Open Parenthesis");
  }

  index++;

  if (collection[index].is_given_marker(Token::Marker::PARENTHESIS_END)) {
    index++;
    return;
  }

  while (true) {
    if (collection[index].is_given_marker(Token::Marker::PARENTHESIS_END)) {
      index++;
      return;
    }

    consume_parameter(collection, index);

    if (collection[index].is_given_marker(Token::Marker::COMMA)) {
      index++;
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
