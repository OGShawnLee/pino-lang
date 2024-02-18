#ifndef COMMONS_LEXER_H
#define COMMONS_LEXER_H

#include <map>

enum class Kind {
  IDENTIFIER,
  KEYWORD,
  LITERAL,
  MARKER,
};

std::map<Kind, std::string> KIND_NAME = {
  {Kind::IDENTIFIER, "Identifier"},
  {Kind::KEYWORD, "Keyword"},
  {Kind::LITERAL, "Literal"},
  {Kind::MARKER, "Marker"},
};

std::string get_kind_name(Kind kind) {
  return KIND_NAME[kind];
}

enum class Keyword {
  VARIABLE,
  CONSTANT,
};

std::map<std::string, Keyword> KEYWORD_KEY = {
  {"var", Keyword::VARIABLE},
  {"val", Keyword::CONSTANT},
};

std::map<Keyword, std::string> KEYWORD_NAME = {
  {Keyword::VARIABLE, "Variable Keyword"},
  {Keyword::CONSTANT, "Constant Keyword"},
};

bool is_keyword(std::string str) {
  return KEYWORD_KEY.find(str) != KEYWORD_KEY.end();
}

Keyword get_keyword(std::string str) {
  if (is_keyword(str)) {
    return KEYWORD_KEY[str];
  }

  throw "Not a keyword";
}

std::string get_keyword_name(std::string str) {
  if (is_keyword(str)) {
    return KEYWORD_NAME[static_cast<Keyword>(KEYWORD_KEY[str])];
  }

  throw "Not a keyword";
}

enum Literal {
  BOOLEAN,
  INTEGER,
  STRING,
};

std::map<Literal, std::string> LITERAL_NAME = {
  {Literal::BOOLEAN, "Boolean Literal"},
  {Literal::INTEGER, "Integer Literal"},
  {Literal::STRING, "String Literal"},
};

bool is_bool_literal(std::string str) {
  return str == "true" || str == "false";
}

bool is_int_literal(std::string str) {
  for (int i = 0; i < str.length(); i++) {
    if (isdigit(str[i]) == false) return false;
  }

  return true;
}

std::string get_literal_name(Literal literal) {
  return LITERAL_NAME[literal];
}

enum Marker {
  DOUBLE_QUOTE,
  EQUAL_SIGN,
};

std::map<char, Marker> MARKER_KEY = {
  {'"', Marker::DOUBLE_QUOTE},
  {'=', Marker::EQUAL_SIGN},
};

std::map<Marker, std::string> MARKER_NAME = {
  {Marker::EQUAL_SIGN, "Equal Sign"},
  {Marker::DOUBLE_QUOTE, "Double Quote"},
};

bool is_marker(char character) {
  return MARKER_KEY.find(character) != MARKER_KEY.end();
}

Marker get_marker(char character) {
  if (is_marker(character)) {
    return MARKER_KEY.at(character);
  }

  throw "Not a marker";
}

std::string get_marker_name(Marker marker) {
  return MARKER_NAME.at(marker);
}

enum class Type {
  BOOL,
  STR,
  INT,
};

std::map<std::string, std::string> LITERAL_NAME_TO_TYPE_NAME = {
  {LITERAL_NAME[Literal::BOOLEAN], "Boolean"},
  {LITERAL_NAME[Literal::INTEGER], "Integer"},
  {LITERAL_NAME[Literal::STRING], "String"},
};

std::string get_literal_type_name(std::string name) {
  return LITERAL_NAME_TO_TYPE_NAME.at(name);
}

#endif
