#pragma once

#include "Statement.h"
#include "Expression.h"
#include "global.h"

Expression::Expression() {
  kind = StatementType::EXPRESSION;
}

Identifier::Identifier() {
  type = ExpressionType::IDENTIFIER;
}

  Value::Value() {
    type = ExpressionType::LITERAL;
  }

void Value::print(size_t indentation) {
  std::string indentation_str = get_indentation(indentation);

  println(indentation_str + "Value {");
  println(indentation_str + "  value: " + value);
  println(indentation_str + "  literal: " + get_literal_name(literal));
  println(indentation_str + "}");
}

String::String() {
  literal = Literal::STRING;
}

PeekPtr<String> String::build(std::vector<Token> stream, size_t index) {
  Token current = stream[index];
  if (current.is_given_marker(Marker::DOUBLE_QUOTE) == false) {
    std::runtime_error("not a string literal " + current.value);
  }

  PeekPtr<String> result;

  for (size_t i = index + 1; i < stream.size(); i++) {
    Token token = stream[i];

    if (token.kind == Kind::IDENTIFIER) {
      std::unique_ptr<Identifier> id = std::make_unique<Identifier>();
      id->name = token.value;
      result.node->body.push_back(std::move(id));
    }

    if (token.kind == Kind::LITERAL) {
      result.node->value = token.value;
    }

    if (token.is_given_marker(Marker::DOUBLE_QUOTE)) {
      result.index = i;
      return result;
    }
  }

  throw std::runtime_error("Unterminated String Literal");
}

void String::print(size_t indentation) {
  std::string indentation_str = get_indentation(indentation);

  println(indentation_str + "String {");
  println(indentation_str + "  value: " + value);
  if (body.size() > 0) {
    println(indentation_str + "  body: [");
    for (size_t i = 0; i < body.size(); i++) {
      println(indentation_str + "    " + body[i]->name);
    }
    println(indentation_str + "  ]");
  }
  println(indentation_str + "}");
}

PeekPtr<Value> Value::build(std::vector<Token> stream, size_t index) {
  PeekPtr<Value> result;

  auto equal = peek<Token>(
    stream,
    index,
    [](Token &token) {
      return token.is_given_marker(Marker::EQUAL_SIGN);
    },
    [](Token &token) {
      return std::runtime_error("Expected equal sign, but got " + token.value);
    },
    [](Token &token) {
      return std::runtime_error("Unexpected end of stream");
    }
  );

  auto next = peek<Token>(
    stream,
    equal.index,
    [](Token &token) {
      if (token.is_given_marker(Marker::DOUBLE_QUOTE)) {
        return true;
      }

      return token.kind == Kind::LITERAL || token.kind == Kind::IDENTIFIER;
    },
    [](Token &token) {
      return std::runtime_error("Expected a literal or identifier, but got " + token.value);
    },
    [](Token &token) {
      return std::runtime_error("Unexpected end of stream");
    }
  );

  if (next.node.is_given_marker(Marker::DOUBLE_QUOTE)) {
    PeekPtr<String> str = String::build(stream, next.index);
    result.node.reset(str.node.release());
    result.index = str.index;
  } else {
    result.index = next.index;
    result.node->literal = get_literal(next.node.name);
    result.node->value = next.node.value;
  }

  return result;
}