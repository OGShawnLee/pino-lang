#pragma once

#include <memory>
#include "Token.h"

class Keyword : public Token {
  KEYWORD_TYPE keyword;

  public:
    Keyword(KEYWORD_TYPE keyword);

    KEYWORD_TYPE get_keyword() const;

    static Keyword* from_base(const std::shared_ptr<Token> &base);

    static bool is_given_keyword(const std::shared_ptr<Token> &token, KEYWORD_TYPE keyword);
    
    void print() const override;
};
