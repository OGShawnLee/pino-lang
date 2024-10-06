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
      LOOP_STATEMENT,
      IF_STATEMENT,
      ELSE_STATEMENT,
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

class Loop : public Statement {
  public:
    enum class Kind {
      FOR_IN_LOOP,
      FOR_TIMES_LOOP,
      INFINITE_LOOP,
    };

  private:
    Kind kind;
    std::unique_ptr<Expression> begin;
    std::unique_ptr<Expression> end;
    std::unique_ptr<Statement> children;

  protected:
    static std::map<Kind, std::string> KIND_NAME_MAPPING;

  public:
    Loop(Kind kind, std::unique_ptr<Expression> begin, std::unique_ptr<Expression> end, std::unique_ptr<Statement> children);

    void print(const size_t &indentation) const;
};

class ElseStatement : public Statement {
  private:
    std::unique_ptr<Statement> children;

  public:
    ElseStatement(std::unique_ptr<Statement> children);

    void print(const size_t &indentation) const;
};

class IfStatement : public Statement {
  private:
    std::unique_ptr<Expression> condition;
    std::unique_ptr<ElseStatement> consequent;
    std::unique_ptr<Statement> children;

  public:
    IfStatement(
      std::unique_ptr<Expression> condition, 
      std::unique_ptr<Statement> children, 
      std::unique_ptr<ElseStatement> consequent
    );

  void print(const size_t &indentation) const;
};