#pragma once

#include <memory>
#include <vector>
#include "Expression.h"
#include "Statement.h"

Field::Field() {
  name = "";
  type = "";
}

Peek<Field> Field::build_as_property(std::vector<Token> collection, size_t index) {
  Peek<Field> result;
  result.index = index;

  auto name = peek<Token>(collection, result.index, [](Token &token) {
    return token.kind == Kind::IDENTIFIER;
  });

  result.node.name = name.node.value;
  result.index = name.index;

  auto colon = peek<Token>(collection, result.index, [](Token &token) {
    return token.is_given_marker(Marker::COLON);
  });

  result.index = colon.index;

  PeekPtr<Expression> value = Expression::build(collection, result.index + 1);
  result.node.value = std::move(value.node);
  result.index = value.index;

  return result;
}

Peek<Field> Field::build(std::vector<Token> collection, size_t index) {
  Peek<Field> result;
  result.index = index;

  auto name = peek<Token>(collection, result.index, [](Token &token) {
    return token.kind == Kind::IDENTIFIER;
  });

  result.node.name = name.node.value;
  result.index = name.index;

  auto type = peek<Token>(collection, result.index, [](Token &token) {
    return token.kind == Kind::IDENTIFIER || token.kind == Kind::BUILT_IN_TYPE;
  });

  result.node.type = type.node.value;
  result.index = type.index;

  return result;
}

void Field::print(size_t indentation = 0) const {
  std::string indent = get_indentation(indentation);
  println(indent + "Field {");
  println(indent + "  name: " + name);
  println(indent + "  type: " + type);
  if (value != nullptr) {
    value->print(indentation + 2);
  }
  println(indent + "}");
}