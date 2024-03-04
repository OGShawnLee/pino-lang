#pragma once

#include "Statement.h"

enum class ExpressionKind {
  IDENTIFIER,
  LITERAL,
  FN_CALL,
  VAR_REASSIGNMENT,
  BINARY_EXPRESSION,
};

std::map<ExpressionKind, std::string> EXPRESSION_KIND = {
  {ExpressionKind::IDENTIFIER, "Identifier"},
  {ExpressionKind::LITERAL, "Literal"},
  {ExpressionKind::FN_CALL, "Function Call"},
  {ExpressionKind::VAR_REASSIGNMENT, "Variable Reassignment"},
  {ExpressionKind::BINARY_EXPRESSION, "Binary Expression"},
};

std::string get_expression_name(ExpressionKind kind) {
  return EXPRESSION_KIND.at(kind);
}

class Expression : public Statement {
  public:
    ExpressionKind expression;

    Expression();

    static bool is_expression(std::vector<Token> collection, size_t index);

    static PeekPtr<Expression> build(std::vector<Token> collection, size_t index);

    void print(size_t indentation = 0) const;
};

class BinaryExpression : public Expression {
  public:
    std::unique_ptr<Expression> left;
    std::unique_ptr<Expression> right;
    BinaryOperator operation;
    std::string operator_str;

    BinaryExpression();

    static bool is_binary_expression(std::vector<Token> collection, size_t index);
    
    static PeekPtr<BinaryExpression> build(std::vector<Token> collection, size_t index);

    void print(size_t indentation = 0) const;
};

class FunctionCall : public Expression {
  static PeekStreamPtr<Expression> handle_arguments(std::vector<Token> collection, size_t index);

  public:
    std::string name;
    std::vector<std::unique_ptr<Expression>> arguments;

    FunctionCall();

    static bool is_fn_call(std::vector<Token> collection, size_t index);
  
    static PeekPtr<FunctionCall> build(std::vector<Token> collection, size_t &index);
    
    void print(size_t indentation = 0) const;
};


class Identifier : public Expression {
  public:
    std::string name;

    Identifier();

    static std::unique_ptr<Identifier> from_identifier(Token token);

    void print(size_t indentation = 0) const;
};

class Value : public Expression {
  public:
    Literal literal;
    std::string value;

    Value();

    static std::unique_ptr<Value> from_literal(Token token);

    void print(size_t indentation = 0) const;
};

class Reassignment : public Expression {
  public:
    std::string identifier;
    std::unique_ptr<Expression> value;

    Reassignment();

    static bool is_reassigment(std::vector<Token> collection, size_t index);
    
    static PeekPtr<Reassignment> build(std::vector<Token> collection, size_t index);

    void print(size_t indentation = 0) const;
};

class String : public Value {
  static std::vector<std::unique_ptr<Identifier>> handle_injections(std::vector<std::string> injections);

  public:
    std::vector<std::unique_ptr<Identifier>> injections;

    String();

    static std::unique_ptr<String> from_string(Token token);    

    void print(size_t indentation = 0) const;
};