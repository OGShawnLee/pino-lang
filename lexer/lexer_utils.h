#ifndef LEXER_UTILS_H
#define LEXER_UTILS_H

#include <map>

enum class Kind {
  IDENTIFIER,
  KEYWORD,
  LITERAL,
  MARKER,
  BUILT_IN_TYPE,
};

std::map<Kind, std::string> KIND_NAME = {
  {Kind::IDENTIFIER, "Identifier"},
  {Kind::KEYWORD, "Keyword"},
  {Kind::LITERAL, "Literal"},
  {Kind::MARKER, "Marker"},
  {Kind::BUILT_IN_TYPE, "Built-in Type"},
};

std::string get_kind_name(Kind kind) {
  return KIND_NAME[kind];
}

enum class BuiltInType {
  BOOL,
  INT,
  STR,
};

std::map<std::string, BuiltInType> BUILT_IN_TYPE_KEY = {
  {"bool", BuiltInType::BOOL},
  {"int", BuiltInType::INT},
  {"str", BuiltInType::STR},
};

std::map<BuiltInType, std::string> BUILT_IN_TYPE_NAME = {
  {BuiltInType::BOOL, "Boolean"},
  {BuiltInType::INT, "Integer"},
  {BuiltInType::STR, "String"},
};

bool is_built_in_type(std::string str) {
  return BUILT_IN_TYPE_KEY.find(str) != BUILT_IN_TYPE_KEY.end();
}

BuiltInType get_built_in_type(std::string str) {
  if (is_built_in_type(str)) {
    return BUILT_IN_TYPE_KEY.at(str);
  }

  throw "Not a built-in type";
}

std::string get_built_in_type_name(BuiltInType type) {
  return BUILT_IN_TYPE_NAME.at(type);
}

enum class Keyword {
  VARIABLE,
  CONSTANT,
  IF,
  ELSE,
  FUNCTION
};

std::map<std::string, Keyword> KEYWORD_KEY = {
  {"var", Keyword::VARIABLE},
  {"val", Keyword::CONSTANT},
  {"if", Keyword::IF},
  {"else", Keyword::ELSE},
  {"fn", Keyword::FUNCTION},
};

std::map<Keyword, std::string> KEYWORD_NAME = {
  {Keyword::VARIABLE, "Variable Keyword"},
  {Keyword::CONSTANT, "Constant Keyword"},
  {Keyword::IF, "If Keyword"},
  {Keyword::ELSE, "Else Keyword"},
  {Keyword::FUNCTION, "Function Keyword"},
};

bool is_keyword(std::string str) {
  return KEYWORD_KEY.find(str) != KEYWORD_KEY.end();
}

Keyword get_keyword(std::string str) {
  if (is_keyword(str)) {
    return KEYWORD_KEY.at(str);
  }

  throw "Not a keyword";
}

std::string get_keyword_name(Keyword keyword) {
  return KEYWORD_NAME.at(keyword);
}

std::string get_keyword_name(std::string str) {
  if (is_keyword(str)) {
    Keyword keyword = get_keyword(str);
    return get_keyword_name(keyword);
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
  DOLLAR_SIGN,
  DOUBLE_QUOTE,
  EQUAL_SIGN,
  LEFT_PARENTHESIS,
  RIGHT_PARENTHESIS,
  COMMA,
  LEFT_BRACE,
  RIGHT_BRACE,
};

std::map<char, Marker> MARKER_KEY = {
  {'$', Marker::DOLLAR_SIGN},
  {'"', Marker::DOUBLE_QUOTE},
  {'=', Marker::EQUAL_SIGN},
  {'(', Marker::LEFT_PARENTHESIS},
  {')', Marker::RIGHT_PARENTHESIS},
  {',', Marker::COMMA},
  {'{', Marker::LEFT_BRACE},
  {'}', Marker::RIGHT_BRACE},
};

std::map<Marker, std::string> MARKER_NAME = {
  {Marker::DOLLAR_SIGN, "String Injection Marker"},
  {Marker::EQUAL_SIGN, "Equal Sign"},
  {Marker::DOUBLE_QUOTE, "Double Quote"},
  {Marker::LEFT_PARENTHESIS, "Left Parenthesis"},
  {Marker::RIGHT_PARENTHESIS, "Right Parenthesis"},
  {Marker::COMMA, "Comma"},
  {Marker::LEFT_BRACE, "Left Brace"},
  {Marker::RIGHT_BRACE, "Right Brace"},
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
