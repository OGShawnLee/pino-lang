#pragma once

#include "Statement.h"

enum class ExpressionKind {
  IDENTIFIER,
  LITERAL,
  FN_CALL,
  VAR_REASSIGNMENT,
  BINARY_EXPRESSION,
  YIELD_CALL,
  LAMBDA,
};

std::map<ExpressionKind, std::string> EXPRESSION_KIND = {
  {ExpressionKind::IDENTIFIER, "Identifier"},
  {ExpressionKind::LITERAL, "Literal"},
  {ExpressionKind::FN_CALL, "Function Call"},
  {ExpressionKind::VAR_REASSIGNMENT, "Variable Reassignment"},
  {ExpressionKind::BINARY_EXPRESSION, "Binary Expression"},
  {ExpressionKind::YIELD_CALL, "Yield Call"},
  {ExpressionKind::LAMBDA, "Lambda"},
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
    // struct:field:field
    // field::field
    // field
    std::vector<std::unique_ptr<Identifier>> path;
    // struct:field:field
    std::string path_str;
    // struct
    std::string name;

    Identifier();

    static PeekPtr<Identifier> build(std::vector<Token> collection, size_t index);

    static std::unique_ptr<Identifier> from_identifier(Token token);

    static std::unique_ptr<Identifier> from_str(std::string name);

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

class Field {
  public:
    std::string name;
    std::string typing;
    // Unique to a Struct Literal
    // Might be used for default Struct Definition Values
    std::unique_ptr<Expression> value;

    // Unique to a Struct Definition
    static PeekPtr<Field> build(std::vector<Token> collection, size_t index);
    
    // Unique to a Struct Literal
    static PeekPtr<Field> build_as_property(std::vector<Token> collection, size_t index);

    void print(size_t indentation) const;
};

class Struct : public Value {
  public:
    std::string name;
    std::vector<std::unique_ptr<Field>> fields;

    Struct();

    static bool is_struct(std::vector<Token> collection, size_t index);

    static PeekPtr<Struct> build(std::vector<Token> collection, size_t index);

    void print(size_t indentation = 0) const;
};

class Vector : public Value {
  void handle_children(std::vector<Token> children);

  size_t handle_init_block(std::vector<Token> collection, size_t index);

  public:
    std::unique_ptr<Expression> len;
    std::unique_ptr<Expression> init;
    std::vector<std::unique_ptr<Expression>> children;
    std::string typing;

    Vector();
    
    static PeekPtr<Vector> build(std::vector<Token> collection, size_t index);

    void print(size_t indentation = 0) const;
};

class Yield : public Expression {
  public:
    ExpressionKind expression = ExpressionKind::YIELD_CALL;
    std::vector<std::unique_ptr<Expression>> arguments;

    static PeekPtr<Yield> build(std::vector<Token> collection, size_t index);

    void print(size_t indentation = 0) const;
};