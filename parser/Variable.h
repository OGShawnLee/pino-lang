#pragma once

#include "Statement.h"
#include "Expression.h"

class Variable : public Statement {
  public:
    std::unique_ptr<Value> value;
    std::string type;

    Variable();

    static bool is_reassignment(std::vector<Token> stream, size_t index);

    static PeekPtr<Variable> build(
      std::vector<Token> stream, 
      size_t index, 
      bool is_reassignment = false
    );

    void print(size_t indentation = 0);
};