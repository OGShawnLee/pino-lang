#pragma once

#include "./Mapper.h"
#include "./Token.h"

class Literal : public Token {
  LITERAL_TYPE literal_type;

  public:
    Literal(LITERAL_TYPE literal_type, std::string data);

    LITERAL_TYPE get_literal_type() const;

    inline void print() const override;
};
