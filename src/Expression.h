#pragma once

#include "Statement.h"

class Expression : public Statement {
  public:
    enum class Kind {
      ASSIGNMENT,
      IDENTIFIER,
      LITERAL,
    };

    Expression();
    Expression(Kind kind, std::string value);

  private:
    Kind kind;
    std::string value;

    static std::map<Kind, std::string> KIND_NAME_MAPPING;

  public:
    static std::unique_ptr<Expression> build(Lexer::Stream &collection);

    static bool is_expression(Lexer::Stream &collection);
    static bool is_binary_expression(Lexer::Stream &collection);

    void print(const size_t &indentation) const override;
};