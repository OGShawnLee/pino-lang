#pragma once

#include "Expression.h"
#include "Statement.h"

class StructDefinition : public Statement {
  public:
    std::string name;
    std::vector<std::unique_ptr<Field>> fields;

    StructDefinition() {
      kind = StatementKind::STRUCT_DEFINITION;
    };

    static PeekPtr<StructDefinition> build(std::vector<Token> collection, size_t index) {
      if (collection[index].is_given_keyword(Keyword::STRUCT_KEYWORD) == false) {
        throw std::runtime_error("DEV: Not a Struct Definition");
      }

      PeekPtr<StructDefinition> result;

      auto name = peek<Token>(collection, index, [](Token &token) {
        return token.kind == Kind::IDENTIFIER;
      });

      result.node->name = name.node.value;
      result.index = name.index;

      auto left_brace = peek<Token>(collection, result.index, [](Token &token) {
        return token.is_given_marker(Marker::LEFT_BRACE);
      });

      result.index = left_brace.index;

      while (true) {
        bool is_right_brace = is_next<Token>(collection, result.index, [](Token &token) {
          return token.is_given_marker(Marker::RIGHT_BRACE);
        });

        if (is_right_brace) {
          result.index += 1;
          return result;
        }

        PeekPtr<Field> field = Field::build(collection, result.index);
        result.node->fields.push_back(std::move(field.node));
        result.index = field.index;
      }

      throw std::runtime_error("DEV: Unterminated Struct Definition");
    }

    void print(size_t indentation) const {
      std::string indent = get_indentation(indentation);
      println(indent + "Struct Definition {");
      println(indent + "  name: " + name);

      if (fields.size() > 0) {
        println(indent + "  fields: {");
        for (const std::unique_ptr<Field> &field : fields) {
          field->print(indentation + 4);
        }
        println(indent + "  }");
      }

      println(indent + "}");
    }
}; 