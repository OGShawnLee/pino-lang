#pragma once

#include <string>
#include "./Mapper.h"

class Token {
  TOKEN_TYPE token_type;
  std::string data;
  std::string name;

  public:
    Token(TOKEN_TYPE token_type, const std::string data);

    Token(TOKEN_TYPE token_type, const std::string data, const std::string name);

    TOKEN_TYPE get_type() const;
    
    std::string get_data() const;
    
    std::string get_name() const;
    
    bool equals(const Token &candidate) const;

    virtual inline void print() const;
};