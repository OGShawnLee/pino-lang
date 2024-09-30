#pragma once

#include "Lexer.h"
#include <map>
#include <memory>
#include <vector>

typedef Lexer::Token Token;

class Statement {
  public:
    enum class Type {
      PROGRAM,
      CONSTANT_DECLARATION,
      VARIABLE_DECLARATION,
      FUNCTION_DECLARATION,
    };

    Statement();

  private:
    Type type;
    std::vector<std::unique_ptr<Statement>> children;

    static std::map<Type, std::string> TYPE_NAME_MAPPING;

  protected:
    void set_type(const Type &type);

  public:
    void push(std::unique_ptr<Statement> child);

    virtual void print(const size_t &indentation = 0) const;
};