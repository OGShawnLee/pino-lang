#pragma once

#include "./Mapper.h"
#include "./Token.h"

class Keyword : public Token {
  KEYWORD_TYPE keyword;

  public:
    Keyword(KEYWORD_TYPE keyword, std::string data);

    KEYWORD_TYPE get_keyword() const;
    
    inline void print() const override;
};
