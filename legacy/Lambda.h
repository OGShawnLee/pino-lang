#pragma once

#include "Expression.h"
#include "Variable.h"

class Lambda : public Expression {
  public:
    std::vector<std::unique_ptr<Variable>> parameters;

    Lambda();

    static bool is_lambda(std::vector<Token> collection, size_t index);

    static PeekPtr<Lambda> build(std::vector<Token> collection, size_t index);

    void print(size_t indentation = 0) const;
};