#pragma once

#include "Variable.h"
#include "Expression.h"

Variable::Variable() {
  kind = StatementType::VAR_DECLARATION;
  name = get_statement_type_name(kind);
}

bool Variable::is_reassignment(std::vector<Token> stream, size_t index) {
  return is_next<Token>(stream, index, [](Token &token) {
    return token.is_given_marker(Marker::EQUAL_SIGN);
  });
}

PeekPtr<Variable> Variable::build(
  std::vector<Token> stream, 
  size_t index, 
  bool is_reassignment
) {
  PeekPtr<Variable> result;
  Token current = stream[index];

  if (is_reassignment) {
    result.index = index;
    result.node->name = current.value;
    result.node->kind = StatementType::VAR_REASSIGNMENT;
  } else {
    if (get_keyword(current.value) == Keyword::CONSTANT) {
      result.node->kind = StatementType::VAL_DECLARATION;
    } else {
      result.node->kind = StatementType::VAR_DECLARATION;
    }

    Peek<std::string> name = Entity::get_name(stream, index);
    result.index = name.index;
    result.node->name = name.node;
  }

  PeekPtr<Value> val = Value::build(stream, result.index);
  result.index = val.index;
  result.node->value = std::move(val.node);
  return result;
}

void Variable::print(size_t indentation) {
  std::string indentation_str = get_indentation(indentation);

  println(indentation_str + get_statement_type_name(kind) + " {");
  println(indentation_str + "  name: " + name);
  println(indentation_str + "  value: " + value->value);
  println(indentation_str + "  type: " + type);
  println(indentation_str + "}");
}