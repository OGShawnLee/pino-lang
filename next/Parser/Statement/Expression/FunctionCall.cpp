#include "FunctionCall.h"

FunctionCall::FunctionCall(std::string callee, std::vector<std::shared_ptr<Expression>> arguments) : Expression(EXPRESSION_TYPE::FUNCTION_CALL) {
  this->callee = callee;
  this->arguments = arguments;
}

const std::vector<std::shared_ptr<Expression>>& FunctionCall::get_arguments() const {
  return this->arguments;
}

std::string FunctionCall::get_callee() const {
  return this->callee;
}

bool FunctionCall::equals(const std::shared_ptr<Statement> &candidate) const {
  if (candidate->get_type() != STATEMENT_TYPE::EXPRESSION) {
    return false;
  }

  const Expression& expression = static_cast<const Expression&>(*candidate);
  if (expression.get_expression_type() != EXPRESSION_TYPE::FUNCTION_CALL) {
    return false;
  }

  const FunctionCall& function_call = static_cast<const FunctionCall&>(expression);
  if (this->callee != function_call.get_callee()) {
    return false;
  }

  if (this->arguments.size() != function_call.get_arguments().size()) {
    return false;
  }

  for (size_t i = 0; i < this->arguments.size(); i++) {
    if (!this->arguments[i]->equals(function_call.get_arguments()[i])) {
      return false;
    }
  }

  return true;
}