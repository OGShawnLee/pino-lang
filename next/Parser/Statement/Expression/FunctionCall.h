#pragma once

#include <memory>
#include <string>
#include <vector>
#include "Expression.h"

class FunctionCall : public Expression {
  std::vector<std::shared_ptr<Expression>> arguments;
  std::string callee;

public:
  FunctionCall(std::string callee, std::vector<std::shared_ptr<Expression>> arguments);

  const std::vector<std::shared_ptr<Expression>>& get_arguments() const;

  std::string get_callee() const;

  bool equals(const std::shared_ptr<Statement> &candidate) const override;
};