#pragma once

#include "./Lexer.h"
#include "./token/Keyword.cpp"
#include "./token/Literal.cpp"
#include "./token/Marker.cpp"
#include "./token/Operator.cpp"
#include "./token/Mapper.cpp"
#include "./token/Matcher.cpp"
#include "./token/Stream.cpp"

std::shared_ptr<Token> Lexer::build_str_literal(const std::string &final_line, size_t &index) {
  std::string buffer = "";
  std::vector<std::string> injections;
  
  for (size_t i = index + 1; i < final_line.size(); i++) {
    const char &character = final_line[i];

    if (character == '"' and not has_escape_character(final_line, i)) {
      index = i;
      return std::make_shared<Literal>(LITERAL_TYPE::STRING, buffer, injections);
    }

    if (Matcher::is_str_injection(final_line, i)) {
      std::string injection = consume_str_injection(final_line, i);
      injections.push_back(injection);
      buffer += "#" + injection;
      continue;
    }

    buffer += character;
  }

  throw std::runtime_error("Unterminated String Literal");
}

std::shared_ptr<Token> Lexer::consume_operator(const std::string &final_line, size_t &index) {
  std::string single_char_operator = final_line.substr(index, 1);
  
  if (index == final_line.size() - 1) {
    return std::make_shared<Operator>(
      Mapper::get_operator_enum_from_str(single_char_operator)
    );
  }
  
  std::string dual_char_operator = final_line.substr(index, 2);
  
  if (Matcher::is_operator(dual_char_operator)) {
    index++;
    return std::make_shared<Operator>(
      Mapper::get_operator_enum_from_str(dual_char_operator)
    );
  } 
    
  return std::make_shared<Operator>(
    Mapper::get_operator_enum_from_str(single_char_operator)
  );
}

std::string Lexer::consume_str_injection(const std::string &line, size_t &index) {
  std::string injection = "";

  for (size_t i = index + 1; i < line.size(); i++) {
    const char &character = line[i];

    if (Matcher::is_identifier(character)) {
      injection += character;
      continue;
    }

    index = i - 1;
    return injection;
  }

  throw std::runtime_error("Unterminated String Injection");
}

std::shared_ptr<Token> Lexer::get_token_from_buffer(const std::string &buffer) {
  if (Matcher::is_keyword(buffer)) {
    return std::make_shared<Keyword>(Mapper::get_keyword_enum_from_str(buffer));
  }

  if (Matcher::is_boolean(buffer)) {
    return std::make_shared<Literal>(LITERAL_TYPE::BOOLEAN, buffer);
  }

  if (Matcher::is_integer(buffer)) {
    return std::make_shared<Literal>(LITERAL_TYPE::INTEGER, buffer);
  }

   if (Matcher::is_operator(buffer)) {
     return std::make_shared<Operator>(Mapper::get_operator_enum_from_str(buffer));
   }
 
  if (Matcher::is_float(buffer)) {
    return std::make_shared<Literal>(LITERAL_TYPE::FLOAT, buffer);
  }

  if (Matcher::is_identifier(buffer)) {
    return std::make_shared<Token>(TOKEN_TYPE::IDENTIFIER, buffer);
  }

  return std::make_shared<Token>(TOKEN_TYPE::ILLEGAL, buffer);
}

void Lexer::handle_buffer(std::vector<std::shared_ptr<Token>> &collection, std::string &buffer) {
  if (is_whitespace(buffer)) {
    return;
  }

  collection.push_back(get_token_from_buffer(buffer));
  buffer = "";
}

Stream Lexer::lex_line(const std::string &line) {
  std::vector<std::shared_ptr<Token>> collection;
  std::string final_line = line + " ";
  std::string buffer = "";

  for (size_t i = 0; i < final_line.size(); i++) {
    char character = final_line[i];

    if (is_whitespace(character)) {
      handle_buffer(collection, buffer);
      continue;
    }

    if (Matcher::is_marker(character)) {
      handle_buffer(collection, buffer);

      MARKER_TYPE marker_type = Mapper::get_marker_enum_from_char(character);
      switch (marker_type) {
        case MARKER_TYPE::COMMENT:
          return Stream(collection);
        case MARKER_TYPE::STR_QUOTE:
          collection.push_back(build_str_literal(final_line, i));
          break;
        default:
          collection.push_back(std::make_shared<Marker>(marker_type));
          break;
      }

      continue;
    }

    bool is_operator = Matcher::is_operator(to_str(character)) or character == '!' or Matcher::is_operator(buffer);
    if (is_operator) {
      handle_buffer(collection, buffer);
      collection.push_back(consume_operator(final_line, i));
      continue;
    }

    buffer += character;
  }

  return Stream(collection);
}