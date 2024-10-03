#pragma once

#include "Statement.h"

class Expression : public Statement {
  public:
    enum class Kind {
      BINARY_EXPRESSION,
      IDENTIFIER,
      LITERAL,
        VECTOR,
        STRUCT_INSTANCE,
      FUNCTION_CALL,
      FUNCTION_LAMBDA,
    };

  private:
    Kind kind;
    std::string value;

    static std::map<Kind, std::string> KIND_NAME_MAPPING;

  protected:
    void set_kind(Kind kind);

  public:
    Expression();
    Expression(Kind kind, std::string value);
    
    void print(const size_t &indentation) const override;
};

class BinaryExpression : public Expression {
  std::unique_ptr<Expression> left;
  std::unique_ptr<Expression> right;
  std::string operation;

  public:
    BinaryExpression(std::unique_ptr<Expression> left, std::string operation, std::unique_ptr<Expression> right);

    void print(const size_t &indentation) const override;
};

class FunctionCall : public Expression {
  std::vector<std::unique_ptr<Expression>> arguments;
  std::string callee;
  
  public:
    FunctionCall(std::string calle, std::vector<std::unique_ptr<Expression>> arguments);    
    
    void print(const size_t &indentation) const override;
};

class Variable;

class FunctionLambda : public Expression {
  std::vector<std::unique_ptr<Variable>> parameters;
  std::unique_ptr<Statement> body;
  
  public:
    FunctionLambda(std::vector<std::unique_ptr<Variable>> parameters, std::unique_ptr<Statement> body);
    
    void print(const size_t &indentation) const override;
};

class Vector : public Expression {
  public:
    Vector();

    void print(const size_t &indentation) const override;
};

class StructInstance : public Expression {
  std::string struct_name;
  std::vector<std::unique_ptr<Variable>> properties;

  public:
    StructInstance(std::string struct_name, std::vector<std::unique_ptr<Variable>> properties);

    void print(const size_t &indentation) const override;
};