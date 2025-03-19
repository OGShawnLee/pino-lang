#pragma once

#include <memory>
#include <string>
#include "Mapper.h"

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

    bool is_given_type(TOKEN_TYPE type) const;
    bool is_given_type(TOKEN_TYPE type_a, TOKEN_TYPE type_b) const;
    
    virtual bool equals(const Token &candidate) const;

    virtual void print() const;
};