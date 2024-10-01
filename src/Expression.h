#pragma once

#include "Statement.h"

class Expression : public Statement {
  public:
    enum class Kind {
      IDENTIFIER,
      LITERAL,
        VECTOR,
      FUNCTION_CALL,
      FUNCTION_LAMBDA,
    };

  private:
    Kind kind;
    std::string value;

    static std::map<Kind, std::string> KIND_NAME_MAPPING;

  public:
    Expression();
    Expression(Kind kind, std::string value);
    
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