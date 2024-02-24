#include "Parser.h"
#include "ControlFlow.cpp"
#include "Function.cpp"
#include "Loop.h"
#include "Statement.cpp"
#include "Variable.cpp"
#include "parser_utils.h"
#include "../lexer/lexer.h"
#include "../utils.h"

PeekStreamPtr<Statement> Parser::parse_block(std::vector<Token> stream, size_t index) {
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
        case Keyword::LOOP: {
          PeekPtr<Loop> loop = Loop::build(stream, i);
          result.nodes.push_back(std::move(loop.node));
          i = loop.index;
          break;
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
      if (is_function_call(stream, i)) {
        PeekPtr<FunctionCall> function = FunctionCall::build(stream, i);
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

Statement Parser::parse(std::vector<Token> stream) {
  Statement statement;
  statement.kind = StatementType::PROGRAM;
  statement.name = get_statement_type_name(StatementType::PROGRAM);

  for (size_t i = 0; i < stream.size(); i++) {
    Token token = stream[i];

    if (token.kind == Kind::KEYWORD) {
      switch (get_keyword(token.value)) {
        case Keyword::FUNCTION: {
          PeekPtr<FunctionDefinition> function = FunctionDefinition::build(stream, i);
          statement.body.push_back(std::move(function.node));
          i = function.index;
          break;
        }
        case Keyword::IF: {
          PeekPtr<IFStatement> if_statement = IFStatement::build(stream, i);
          statement.body.push_back(std::move(if_statement.node));
          i = if_statement.index;
          break;
        }
        case Keyword::LOOP: {
          PeekPtr<Loop> loop = Loop::build(stream, i);
          statement.body.push_back(std::move(loop.node));
          i = loop.index;
          break;
        }
        case Keyword::VARIABLE:
        case Keyword::CONSTANT:
          PeekPtr<Variable> variable = Variable::build(stream, i);
          statement.body.push_back(std::move(variable.node));
          i = variable.index;
          break;
      }
    } else if (token.kind == Kind::IDENTIFIER) {
      if (is_function_call(stream, i)) {
        PeekPtr<FunctionCall> function = FunctionCall::build(stream, i);
        statement.body.push_back(std::move(function.node));
        i = function.index;
      } 

      if (Variable::is_reassignment(stream, i)) {
        PeekPtr<Variable> variable = Variable::build(stream, i, true);
        statement.body.push_back(std::move(variable.node));
        i = variable.index;
      }
    }
  }

  return statement;
}
