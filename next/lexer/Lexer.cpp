#pragma once

#include "./Lexer.h"
#include "./token/Keyword.cpp"
#include "./token/Literal.cpp"
#include "./token/Marker.cpp"
#include "./token/Operator.cpp"
#include "./token/Mapper.cpp"
#include "./token/Matcher.cpp"
#include "./token/Stream.cpp"

std::shared_ptr<Token> Lexer::consume_operator(const std::string &final_line, int &index) {
  std::string single_char_operator = final_line.substr(index, 1);
  
  if (index == final_line.size() - 1) {
    return std::make_shared<Operator>(
      Mapper::get_operator_enum_from_str(single_char_operator), single_char_operator
    );
  }
  
  std::string dual_char_operator = final_line.substr(index, 2);
  
  if (Matcher::is_operator(dual_char_operator)) {
    index++;
    return std::make_shared<Operator>(
      Mapper::get_operator_enum_from_str(dual_char_operator), dual_char_operator
    );
  } 
    
  return std::make_shared<Operator>(
    Mapper::get_operator_enum_from_str(single_char_operator), single_char_operator
  );
}

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

    bool is_marker = Matcher::is_marker(character);
    bool is_operator = Matcher::is_operator(std::string(1, character));

    if (is_marker or is_operator) {
      if (not is_whitespace(buffer)) {
        collection.push_back(get_token_from_buffer(buffer));
        buffer = "";
      }

      if (is_operator) {
        collection.push_back(consume_operator(final_line, i));
      } else {
        collection.push_back(
          std::make_shared<Marker>(Mapper::get_marker_enum_from_char(character), character)
        );
      }

      continue;
    }

    buffer += character;
  }

  return Stream(collection);
}