#ifndef PARSER_H
#define PARSER_H

#include <map>
#include <memory>
#include <vector>
#include "commons/parser.h"
#include "lexer.h"
#include "utils.h"

class Statement {
  public:
    StatementType kind;
    std::string name;
    std::string value;
    std::vector<std::unique_ptr<Statement>> body;

    virtual void print(size_t indentation = 0) {
      std::string indentation_str = get_indentation(indentation);

      println(indentation_str + get_statement_type_name(kind) + " {");
      if (name != "") {
        println(indentation_str + "  name: " + name);
      }
      if (value != "") {
        println(indentation_str + "  value: " + value);
      }
      for (std::unique_ptr<Statement> &statement : body) {
        statement->print(indentation + 2);
      }
      println(indentation_str + "}");
    }
};

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

    return peek<Token>(
      stream,
      marker.index,
      [](Token &token) {
        return token.kind == Kind::LITERAL;
      },
      [](Token &token) {
        return std::runtime_error("Expected identifier value, but got " + token.value);
      },
      [](Token &token) {
        return std::runtime_error("Unexpected end of stream");
      }
    );
  }
}

class Variable : public Statement {
  public:
    std::string type;

    Variable() {
      kind = StatementType::VAR_DECLARATION;
      name = get_statement_type_name(kind);
    }

    static PeekPtr<Variable> build(std::vector<Token> stream, size_t index) {
      PeekPtr<Variable> result;
      Token current = stream[index];

      if (get_keyword(current.value) == Keyword::CONSTANT) {
        result.node->kind = StatementType::VAL_DECLARATION;
      } else {
        result.node->kind = StatementType::VAR_DECLARATION;
      }

      Peek<std::string> name = Entity::get_name(stream, index);
      result.index = name.index;
      result.node->name = name.node;

      Peek<Token> value = Entity::get_value(stream, name.index);
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

class Parser {
  public:
    static Statement parse(std::vector<Token> stream) {
      Statement statement;
      statement.kind = StatementType::PROGRAM;
      statement.name = get_statement_type_name(StatementType::PROGRAM);

      for (size_t i = 0; i < stream.size(); i++) {
        Token token = stream[i];

        if (token.kind == Kind::KEYWORD) {
          switch (get_keyword(token.value)) {
            case Keyword::VARIABLE:
            case Keyword::CONSTANT:
              PeekPtr<Variable> variable = Variable::build(stream, i);
              statement.body.push_back(std::move(variable.node));
              i = variable.index;
              break;
          }
        }
      }

      return statement;
    }
};

#endif