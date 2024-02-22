#ifndef PARSER_H
#define PARSER_H

#include "ControlFlow.cpp"
#include "Function.cpp"
#include "Statement.cpp"
#include "Variable.cpp"
#include "parser_utils.h"
#include "../lexer/lexer.h"
#include "../utils.h"

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
            case Keyword::IF: {
              PeekPtr<IFStatement> if_statement = IFStatement::build(stream, i);
              statement.body.push_back(std::move(if_statement.node));
              i = if_statement.index;
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
          if (Function::is_function_call(stream, i)) {
            PeekPtr<Function> function = Function::build(stream, i);
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
};

#endif