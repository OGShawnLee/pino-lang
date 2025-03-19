#pragma once

#include "Token.h"

class Keyword : public Token {
  KEYWORD_TYPE keyword;

  public:
    Keyword(KEYWORD_TYPE keyword);

    KEYWORD_TYPE get_keyword() const;

    static Keyword* from_base(const std::shared_ptr<Token> &base);
    
    void print() const override;
};
