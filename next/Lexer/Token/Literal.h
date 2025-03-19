#pragma once

#include "Token.h"
#include <vector>

class Literal : public Token {
  LITERAL_TYPE literal_type;
  std::vector<std::string> injections;

  public:
    Literal(LITERAL_TYPE literal_type, std::string data);
    Literal(LITERAL_TYPE literal_type, std::string data, std::vector<std::string> injections);

    LITERAL_TYPE get_literal_type() const;

    const std::vector<std::string>& get_injections() const;

    void print() const override;

    bool equals(const Token &other) const override;
};
