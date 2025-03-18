#pragma once

#include <memory>
#include <vector>
#include "./Token/Stream.h"

class Lexer {
  
  static std::shared_ptr<Token> build_str_literal(const std::string &final_line, size_t &index); 
  
  static std::shared_ptr<Token> consume_operator(const std::string &buffer, size_t &index);
  
  static std::string consume_str_injection(const std::string &final_line, size_t &index);
  
  static std::shared_ptr<Token> get_token_from_buffer(const std::string &buffer); 
  
  static void handle_buffer(std::vector<std::shared_ptr<Token>> &collection,std::string &buffer);  
  public:
    static Stream lex_line(const std::string &line);
};