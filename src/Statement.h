#pragma once

#include <map>
#include <memory>
#include <vector>
#include "Lexer.h"

typedef Lexer::Token Token;

class Statement {
  public:
    enum class Type {
      PROGRAM,
      BLOCK,
      EXPRESSION,
      CONSTANT_DECLARATION,
      VARIABLE_DECLARATION,
      FUNCTION_DECLARATION,
      STRUCT_DECLARATION,
      ENUM_DECLARATION,
      RETURN,
    };

    Statement();
    Statement(Type type);

  private:
    Type type;

  protected:
    std::vector<std::unique_ptr<Statement>> children;
    
    Type get_type() const;
    void set_type(const Type &type);

    static std::map<Type, std::string> TYPE_NAME_MAPPING;

  public:
    void push(std::unique_ptr<Statement> child);

    virtual void print(const size_t &indentation = 0) const;
};

class Expression;

class Return : public Statement {
  private:
    std::unique_ptr<Expression> argument;

  public:
    Return(std::unique_ptr<Expression> argument);

    void print(const size_t &indentation) const;
};
