#pragma once

#include "Statement.h"
#include "Expression.h"
#include "global.h"

struct Parameter {
  std::string name;
  std::string type;
};

bool is_function_call(std::vector<Token> stream, size_t index);

class FunctionDefinition : public Statement {
  static Peek<Parameter> parse_parameter(std::vector<Token> stream, size_t index);

  static PeekStream<Parameter> parse_parameters(std::vector<Token> stream, size_t index);

  public:
    std::string name;
    std::string return_type;
    std::vector<Parameter> parameters;
    std::vector<std::unique_ptr<Statement>> body;

    FunctionDefinition();

    static PeekPtr<FunctionDefinition> build(std::vector<Token> stream, size_t index);

    void print(size_t indentation = 0);
};

class FunctionCall : public Expression {
  static PeekStreamPtr<Expression> parse_arguments(std::vector<Token> stream, size_t index);

  public:
    std::vector<std::unique_ptr<Expression>> arguments;

    FunctionCall();

    static PeekPtr<FunctionCall> build(std::vector<Token> stream, size_t index);

    void print(size_t indentation = 0);
};