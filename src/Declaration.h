#pragma once

#include "Statement.h"

class Variable : public Statement {
  private:
    bool is_readonly;
    std::string identifier;
    std::string value;

  public:
    Variable() = default;

    void consume_kind(
      const std::vector<Lexer::Token> &collection, size_t &index
    );
    void consume_identifier(
      const std::vector<Lexer::Token> &collection, size_t &index
    );
    void consume_value(
      const std::vector<Lexer::Token> &collection, size_t &index
    );

    void print(const size_t &indentation) const override;
};
