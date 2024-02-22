#pragma once

#include "Function.cpp"
#include "Statement.cpp"
#include "Variable.cpp"
#include "parser_utils.h"
#include "../lexer/lexer.h"
#include "../utils.h"

PeekStreamPtr<Statement> parse_block(std::vector<Token> stream, size_t index);

class ELSEStatement : public Statement {
  public:
    ELSEStatement() {
      kind = StatementType::ELSE_STATEMENT;
      name = get_statement_type_name(kind);
    }

    static PeekPtr<ELSEStatement> build(std::vector<Token> stream, size_t index) {
      PeekPtr<ELSEStatement> result;
      Token current = stream[index];

      if (current.is_given_keyword(Keyword::ELSE) == false) {
        throw std::runtime_error("Expected else keyword, but got " + current.value);
      }

      auto marker = peek<Token>(
        stream,
        index,
        [](Token &token) {
          return token.is_given_marker(Marker::LEFT_BRACE);
        },
        [](Token &token) {
          return std::runtime_error("Expected left brace, but got " + token.value);
        },
        [](Token &token) {
          return std::runtime_error("Unterminated else statement");
        }
      );

      PeekStreamPtr<Statement> body = parse_block(stream, marker.index + 1);
      result.node->body = std::move(body.nodes);
      result.index = body.index;
      return result;
    }
};

class IFStatement : public Statement {
  public:
    std::string condition;
    std::unique_ptr<ELSEStatement> else_statement;

    IFStatement() {
      kind = StatementType::IF_STATEMENT;
      name = get_statement_type_name(kind);
    }

    static PeekPtr<IFStatement> build(std::vector<Token> stream, size_t index) {
      PeekPtr<IFStatement> result;
      Token current = stream[index];

      if (current.is_given_keyword(Keyword::IF) == false) {
        throw std::runtime_error("Expected if keyword, but got " + current.value);
      }

      auto condition = peek<Token>(
        stream,
        index,
        [](Token &token) {
          return token.is_given_literal(Literal::BOOLEAN) || token.kind == Kind::IDENTIFIER;
        },
        [](Token &token) {
          return std::runtime_error("Expected condition, but got " + token.value);
        },
        [](Token &token) {
          return std::runtime_error("Unterminated if statement");
        }
      );

      result.node->condition = condition.node.value;

      auto marker = peek<Token>(
        stream,
        condition.index,
        [](Token &token) {
          return token.is_given_marker(Marker::LEFT_BRACE);
        },
        [](Token &token) {
          return std::runtime_error("Expected left brace, but got " + token.value);
        },
        [](Token &token) {
          return std::runtime_error("Unterminated if statement");
        }
      );

      PeekStreamPtr<Statement> body = parse_block(stream, marker.index + 1);
      result.node->body = std::move(body.nodes);
      result.index = body.index;

      bool else_keyword = is_next<Token>(
        stream,
        result.index,
        [](Token &token) {
          return token.is_given_keyword(Keyword::ELSE);
        }
      );

      if (else_keyword) {
        PeekPtr<ELSEStatement> else_statement = ELSEStatement::build(stream, result.index + 1);
        result.node->else_statement = std::move(else_statement.node);
        result.index = else_statement.index;
      }

      return result;
    }
};

PeekStreamPtr<Statement> parse_block(std::vector<Token> stream, size_t index) {
  PeekStreamPtr<Statement> result;
  result.index = index;

  for (size_t i = index; i < stream.size(); i++) {
    Token token = stream[i];

    if (token.kind == Kind::KEYWORD) {
      switch (get_keyword(token.value)) {
        case Keyword::IF: {
          PeekPtr<IFStatement> if_statement = IFStatement::build(stream, i);
          result.nodes.push_back(std::move(if_statement.node));
          i = if_statement.index;
          break;
        }
        case Keyword::ELSE: {
          throw std::runtime_error("Unexpected else keyword");
        }
        case Keyword::VARIABLE:
        case Keyword::CONSTANT: {
          PeekPtr<Variable> variable = Variable::build(stream, i);
          result.nodes.push_back(std::move(variable.node));
          i = variable.index;
          break;
        }
      }
    } else if (token.kind == Kind::IDENTIFIER) {
      if (Function::is_function_call(stream, i)) {
        PeekPtr<Function> function = Function::build(stream, i);
        result.nodes.push_back(std::move(function.node));
        i = function.index;
      } 

      if (Variable::is_reassignment(stream, i)) {
        PeekPtr<Variable> variable = Variable::build(stream, i, true);
        result.nodes.push_back(std::move(variable.node));
        i = variable.index;
      }
    }

    if (token.is_given_marker(Marker::RIGHT_BRACE)) {
      result.index = i;
      return result;
    }
  }

  throw std::runtime_error("Unterminated block");
}