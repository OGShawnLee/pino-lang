#pragma once

#include <regex>
#include "./Matcher.h"
#include "./Mapper.cpp"

const std::string Matcher::BOOLEAN_REGEX = "^(true|false)$";
/* 
  FLOAT_REGEX: 
    - UP TO 9 DIGITS BEFORE THE DECIMAL POINT
    - UP TO 9 DIGITS AFTER THE DECIMAL POINT
    - OPTIONAL SEPARATOR AFTER EACH 3 DIGITS
    - OPTIONAL NEGATIVE SIGN
*/
const std::string Matcher::FLOAT_REGEX = "^-?[0-9]{1,9}(_[0-9]{3})*\\.[0-9]{1,9}(_[0-9]{3})*$";
/*
  IDENTIFIER_REGEX:
    - STARTS WITH A LETTER, UNDERSCORE OR DOLLAR SIGN
    - FOLLOWED BY LETTERS, DIGITS, UNDERSCORES OR DOLLAR SIGNS
*/
const std::string Matcher::IDENTIFIER_REGEX = "^[a-zA-Z_$][a-zA-Z0-9_$]*$";
/*
  INT_REGEX:
    - UP TO 18 DIGITS
    - OPTIONAL SEPARATOR AFTER EACH 3 DIGITS
    - OPTIONAL NEGATIVE SIGN
*/
const std::string Matcher::INT_REGEX = "^-?[0-9]{1,18}(_[0-9]{3})*$";

bool Matcher::is_boolean(const std::string &str) {
  return std::regex_match(str, std::regex(BOOLEAN_REGEX));
}

bool Matcher::is_float(const std::string &str) {
  return std::regex_match(str, std::regex(FLOAT_REGEX));
}

bool Matcher::is_identifier(const std::string &str) {
  return std::regex_match(str, std::regex(IDENTIFIER_REGEX));
}

bool Matcher::is_integer(const std::string &str) {
  return std::regex_match(str, std::regex(INT_REGEX));
}

bool Matcher::is_keyword(const std::string &str) {
  return Mapper::is_keyword(str);
}