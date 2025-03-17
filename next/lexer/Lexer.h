#pragma once

#include <memory>
#include <vector>
#include "./token/Stream.h"

class Lexer {
  static std::shared_ptr<Token> get_token_from_buffer(const std::string &buffer); 

  public:
    static Stream lex_line(const std::string &line);
};