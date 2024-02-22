#pragma once

#include "Statement.cpp"
#include "Variable.cpp"
#include "parser_utils.h"
#include "../lexer/lexer.h"
#include "../utils.h"

class Function : public Statement {
  static PeekStreamPtr<Statement> parse_arguments(std::vector<Token> stream, size_t index) {
    PeekStreamPtr<Statement> result;

    // + 1 to skip the left parenthesis
    for (size_t i = index + 1; i < stream.size(); i++) {
      Token token = stream[i];

      switch (token.kind) {
        case Kind::LITERAL: {
          Statement *literal = new Statement();
          literal->name = token.value;
          literal->kind = StatementType::EXPRESSION;
          result.nodes.push_back(std::unique_ptr<Statement>(literal));
          break;
        }
        case Kind::IDENTIFIER: {
          if (Function::is_function_call(stream, i)) {
            PeekPtr<Function> function = Function::build(stream, i);
            result.nodes.push_back(std::move(function.node));
            i = function.index;
            break;
          }

          if (Variable::is_reassignment(stream, i)) {
            PeekPtr<Variable> variable = Variable::build(stream, i, true);
            result.nodes.push_back(std::move(variable.node));
            i = variable.index;
            break;
          }

          Statement *identifier = new Statement();
          identifier->name = token.value;
          identifier->kind = StatementType::EXPRESSION;
          result.nodes.push_back(std::unique_ptr<Statement>(identifier));
          break;
        }
        case Kind::MARKER:
          if (token.is_given_marker(Marker::RIGHT_PARENTHESIS)) {
            result.index = i;
            return result;
          }

          if (token.is_given_marker(Marker::COMMA)) {
            continue;
          }

          if (token.is_given_marker(Marker::DOUBLE_QUOTE)) {
            Peek<Token> str_literal = parse_str_literal(stream, i);
            Statement *literal = new Statement();
            literal->name = '"' + str_literal.node.value + '"';
            literal->kind = StatementType::EXPRESSION;
            result.nodes.push_back(std::unique_ptr<Statement>(literal));
            i = str_literal.index;
            continue;
          }
        default:
          throw std::runtime_error("Unexpected token " + token.value);
      }

    }

    throw std::runtime_error("Unterminated function call");
  }

  public:
    std::vector<std::unique_ptr<Statement>> arguments;

    Function() {
      kind = StatementType::FUNCTION_CALL;
      name = get_statement_type_name(kind);
    }

    static PeekPtr<Function> build(std::vector<Token> stream, size_t index) {
      PeekPtr<Function> result;
      Token current = stream[index];

      result.index = index;
      result.node->name = current.value;
      result.node->kind = StatementType::FUNCTION_CALL;

      PeekStreamPtr<Statement> arguments = parse_arguments(stream, index + 1);
      result.index = arguments.index;
      result.node->arguments = std::move(arguments.nodes);

      return result;
    }

    static bool is_function_call(std::vector<Token> stream, size_t index) {
      return is_next<Token>(stream, index, [](Token &token) {
        return token.is_given_marker(Marker::LEFT_PARENTHESIS);
      });
    }

    void print(size_t indentation = 0) {
      std::string indentation_str = get_indentation(indentation);

      println(indentation_str + get_statement_type_name(kind) + " {");
      println(indentation_str + "  name: " + name);
      println(indentation_str + "  arguments: [");
      for (std::unique_ptr<Statement> &argument : arguments) {
        argument->print(indentation + 4);
      }
      println(indentation_str + "  ]");
      println(indentation_str + "}");
    }
};