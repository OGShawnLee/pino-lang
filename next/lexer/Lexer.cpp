#pragma once

#include "./Lexer.h"
#include "./token/Keyword.cpp"
#include "./token/Literal.cpp"
#include "./token/Mapper.cpp"
#include "./token/Matcher.cpp"
#include "./token/Stream.cpp"

std::shared_ptr<Token> Lexer::get_token_from_buffer(const std::string &buffer) {
  if (Matcher::is_keyword(buffer)) {
    return std::make_shared<Keyword>(Mapper::get_keyword_enum_from_str(buffer), buffer);
  }

  if (Matcher::is_boolean(buffer)) {
    return std::make_shared<Literal>(LITERAL_TYPE::BOOLEAN, buffer);
  }

  if (Matcher::is_integer(buffer)) {
    return std::make_shared<Literal>(LITERAL_TYPE::INTEGER, buffer);
  }

  if (Matcher::is_float(buffer)) {
    return std::make_shared<Literal>(LITERAL_TYPE::FLOAT, buffer);
  }

  if (Matcher::is_identifier(buffer)) {
    return std::make_shared<Token>(TOKEN_TYPE::IDENTIFIER, buffer);
  }

  return std::make_shared<Token>(TOKEN_TYPE::ILLEGAL, buffer);
}

Stream Lexer::lex_line(const std::string &line) {
  std::vector<std::shared_ptr<Token>> collection;
  std::string final_line = line + " ";
  std::string buffer = "";

  for (int i = 0; i < final_line.size(); i++) {
    char character = final_line[i];

    if (is_whitespace(character)) {
      if (is_whitespace(buffer)) continue;
    
      collection.push_back(get_token_from_buffer(buffer));
      buffer = "";

      continue;
    }

    buffer += character;
  }

  return Stream(collection);
}