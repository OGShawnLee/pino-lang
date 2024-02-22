#ifndef UTILS_PARSER_H
#define UTILS_PARSER_H

#include <map>
#include <vector>
#include <memory>
#include "../lexer/lexer.h"
#include "../utils.h"

enum class StatementType {
  PROGRAM,
  VAR_DECLARATION,
  VAL_DECLARATION,
  VAR_REASSIGNMENT,
  EXPRESSION,
  FUNCTION_CALL,
  IF_STATEMENT,
  ELSE_STATEMENT,
};

std::map<StatementType, std::string> STATEMENT_TYPE_NAME = {
  {StatementType::PROGRAM, "Program"},
  {StatementType::VAR_DECLARATION, "Variable Declaration"},
  {StatementType::VAL_DECLARATION, "Constant Declaration"},
  {StatementType::VAR_REASSIGNMENT, "Variable Reassignment"},
  {StatementType::FUNCTION_CALL, "Function Call"},
  {StatementType::EXPRESSION, "Expression"},
  {StatementType::IF_STATEMENT, "If Statement"},
  {StatementType::ELSE_STATEMENT, "Else Statement"},
};

std::string get_statement_type_name(StatementType type) {
  return STATEMENT_TYPE_NAME.at(type);
}

Peek<Token> parse_str_literal(std::vector<Token> stream, size_t index) {
  Token current = stream[index];
  if (current.is_given_marker(Marker::DOUBLE_QUOTE) == false) {
    throw std::runtime_error("Expected double quote, but got " + current.value);
  }

  Peek<Token> result;

  for (size_t i = index + 1; i < stream.size(); i++) {
    Token token = stream[i];

    if (token.kind == Kind::LITERAL) {
      result.node = token;
    }

    if (token.is_given_marker(Marker::DOUBLE_QUOTE)) {
      result.index = i;
      return result;
    }
  }

  throw std::runtime_error("Unterminated String Literal");
}

namespace Entity {  
  Peek<std::string> get_name(std::vector<Token> stream, size_t index) {
    auto result = peek<Token>(
      stream,
      index,
      [](Token &token) {
        return token.kind == Kind::IDENTIFIER;
      },
      [](Token &token) {
        return std::runtime_error("Expected an identifier, but got " + token.value);
      },
      [](Token &token) {
        return std::runtime_error("Unexpected end of stream");
      }
    );

    Peek<std::string> name;
    name.node = result.node.value;
    name.index = result.index;
    return name;
  }

  Peek<Token> get_value(std::vector<Token> stream, size_t index) {
    auto marker = peek<Token>(
      stream,
      index,
      [](Token &token) {
        return token.is_given_marker(Marker::EQUAL_SIGN);
      },
      [](Token &token) {
        return std::runtime_error("Expected assignment marker, but got " + token.value);
      },
      [](Token &token) {
        return std::runtime_error("Unexpected end of stream");
      }
    );

    auto next = peek<Token>(
      stream,
      marker.index,
      [](Token &token) {
        return token.kind == Kind::LITERAL || token.is_given_marker(Marker::DOUBLE_QUOTE);
      },
      [](Token &token) {
        return std::runtime_error("Expected identifier value, but got " + token.value);
      },
      [](Token &token) {
        return std::runtime_error("Unexpected end of stream");
      }
    );

    if (next.node.is_given_marker(Marker::DOUBLE_QUOTE)) {
      return parse_str_literal(stream, next.index);
    }

    return next;
  }
};

#endif