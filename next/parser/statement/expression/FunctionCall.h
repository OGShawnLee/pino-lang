#pragma once

#include "./Expression.h"
#include "../../../lexer/token/Literal.h"

class FunctionCall : public Expression {
  std::vector<std::shared_ptr<Expression>> arguments;
  std::string calle;

  public:
    FunctionCall(std::string calle, std::vector<std::shared_ptr<Expression>> arguments) : Expression(EXPRESSION_TYPE::FUNCTION_CALL) {
      this->calle = calle;
      this->arguments = std::move(arguments);
    }

    const std::vector<std::shared_ptr<Expression>>& get_arguments() const {
      return this->arguments;
    }

    std::string get_callee() const {
      return this->calle;
    }


    bool equals(const std::shared_ptr<Statement> &candidate) const override {
      if (candidate->get_type() != STATEMENT_TYPE::EXPRESSION) {
        return false;
      }

      const Expression& expression = static_cast<const Expression&>(*candidate);
      
      if (expression.get_expression_type() != EXPRESSION_TYPE::FUNCTION_CALL) {
        return false;
      }
      
      const FunctionCall& function_call = static_cast<const FunctionCall&>(expression);
      
      if (this->calle != function_call.get_callee()) {
        return false;
      }
      
      if (this->arguments.size() != function_call.get_arguments().size()) {
        return false;
      }

      for (size_t i = 0; i < this->arguments.size(); i++) {
        if (not this->arguments[i]->equals(function_call.get_arguments()[i])) {
          return false;
        }
      }

      return true;
    }
};