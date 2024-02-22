#pragma once

#include "Statement.cpp"
#include "parser_utils.h"
#include "../lexer/lexer.h"
#include "../utils.h"

class Variable : public Statement {
  public:
    std::string type;

    Variable() {
      kind = StatementType::VAR_DECLARATION;
      name = get_statement_type_name(kind);
    }

    static bool is_reassignment(std::vector<Token> stream, size_t index) {
      return is_next<Token>(stream, index, [](Token &token) {
        return token.is_given_marker(Marker::EQUAL_SIGN);
      });
    }

    static PeekPtr<Variable> build(
      std::vector<Token> stream, 
      size_t index, 
      bool is_reassignment = false
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

      Peek<Token> value = Entity::get_value(stream, result.index);
      result.index = value.index;
      result.node->value = value.node.value;
      result.node->type = get_literal_type_name(value.node.name);

      return result;
    }

    void print(size_t indentation = 0) {
      std::string indentation_str = get_indentation(indentation);

      println(indentation_str + get_statement_type_name(kind) + " {");
      println(indentation_str + "  name: " + name);
      println(indentation_str + "  value: " + value);
      println(indentation_str + "  type: " + type);
      println(indentation_str + "}");
    }
};