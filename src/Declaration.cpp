#include "Declaration.h"
#include "Common.h"

void Variable::consume_kind(
  const std::vector<Lexer::Token> &collection, size_t &index
) {
  Lexer::Token::Keyword keyword = collection[index].get_keyword();

  if (keyword != Lexer::Token::Keyword::CONSTANT and keyword != Lexer::Token::Keyword::VARIABLE) {
    throw std::runtime_error("PARSER: Invalid Variable Declaration");
  }

  if (keyword == Token::Keyword::CONSTANT) {
    is_readonly = true;
    set_type(Type::CONSTANT_DECLARATION);
  } else {
    is_readonly = false;
    set_type(Type::VARIABLE_DECLARATION);
  }

  index++;
}

void Variable::consume_identifier(
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

void Variable::print(const size_t &indentation = 0) const {
  std::string indent(indentation, ' ');

  println(indent + (is_readonly ? "Constant" : "Variable") + " Declaration {");
  println(indent + "  identifier: " + identifier);
  println(indent + "  value: " + value);
  println(indent + "}");
}