#pragma once

#include "./Mapper.h"

const std::map<KEYWORD_TYPE, std::string> Mapper::KEYWORD_TO_STR_NAME = {
  {KEYWORD_TYPE::AS, "as"},
  {KEYWORD_TYPE::BREAK, "break"},
  {KEYWORD_TYPE::CONSTANT, "constant"},
  {KEYWORD_TYPE::CONTINUE, "continue"},
  {KEYWORD_TYPE::ELSE, "else"},
  {KEYWORD_TYPE::ENUM, "enum"},
  {KEYWORD_TYPE::FROM, "from"},
  {KEYWORD_TYPE::FUNCTION, "function"},
  {KEYWORD_TYPE::IF, "if"},
  {KEYWORD_TYPE::IMPORT, "import"},
  {KEYWORD_TYPE::IN, "in"},
  {KEYWORD_TYPE::LOOP, "for"},
  {KEYWORD_TYPE::MATCH, "match"},
  {KEYWORD_TYPE::PUB, "public"},
  {KEYWORD_TYPE::RETURN, "return"},
  {KEYWORD_TYPE::STATIC, "static"},
  {KEYWORD_TYPE::STRUCT, "struct"},
  {KEYWORD_TYPE::THEN, "then"},
  {KEYWORD_TYPE::VARIABLE, "variable"},
  {KEYWORD_TYPE::WHEN, "when"},
};
const std::map<std::string, KEYWORD_TYPE> Mapper::STR_TO_KEYWORD = {
  {"as", KEYWORD_TYPE::AS},
  {"break", KEYWORD_TYPE::BREAK},
  {"val", KEYWORD_TYPE::CONSTANT},
  {"continue", KEYWORD_TYPE::CONTINUE},
  {"else", KEYWORD_TYPE::ELSE},
  {"enum", KEYWORD_TYPE::ENUM},
  {"from", KEYWORD_TYPE::FROM},
  {"fn", KEYWORD_TYPE::FUNCTION},
  {"if", KEYWORD_TYPE::IF},
  {"import", KEYWORD_TYPE::IMPORT},
  {"in", KEYWORD_TYPE::IN},
  {"for", KEYWORD_TYPE::LOOP},
  {"match", KEYWORD_TYPE::MATCH},
  {"pub", KEYWORD_TYPE::PUB},
  {"return", KEYWORD_TYPE::RETURN},
  {"static", KEYWORD_TYPE::STATIC},
  {"struct", KEYWORD_TYPE::STRUCT},
  {"then", KEYWORD_TYPE::THEN},
  {"var", KEYWORD_TYPE::VARIABLE},
  {"when", KEYWORD_TYPE::WHEN},
};
const std::map<LITERAL_TYPE, std::string> Mapper::LITERAL_TO_STR_NAME = {
  {LITERAL_TYPE::BOOLEAN, "Boolean"},
  {LITERAL_TYPE::FLOAT, "Float"},
  {LITERAL_TYPE::INTEGER, "Integer"},
  {LITERAL_TYPE::STRING, "String"},
};
const std::map<MARKER_TYPE, char> Mapper::MARKER_TO_CHAR = {
  {MARKER_TYPE::BLOCK_BEGIN, '{'},
  {MARKER_TYPE::BLOCK_END, '}'},
  {MARKER_TYPE::BRACKET_BEGIN, '['},
  {MARKER_TYPE::BRACKET_END, ']'},
  {MARKER_TYPE::COMMA, ','},
  {MARKER_TYPE::COMMENT, '#'},
  {MARKER_TYPE::PARENTHESIS_BEGIN, '('},
  {MARKER_TYPE::PARENTHESIS_END, ')'},
  {MARKER_TYPE::STR_QUOTE, '"'},
};
const std::map<char, MARKER_TYPE> Mapper::CHAR_TO_MARKER = {
  {'{', MARKER_TYPE::BLOCK_BEGIN},
  {'}', MARKER_TYPE::BLOCK_END},
  {'[', MARKER_TYPE::BRACKET_BEGIN},
  {']', MARKER_TYPE::BRACKET_END},
  {',', MARKER_TYPE::COMMA},
  {'#', MARKER_TYPE::COMMENT},
  {'(', MARKER_TYPE::PARENTHESIS_BEGIN},
  {')', MARKER_TYPE::PARENTHESIS_END},
  {'"', MARKER_TYPE::STR_QUOTE},
};
const std::map<MARKER_TYPE, std::string> Mapper::MARKER_TO_STR_NAME = {
  {MARKER_TYPE::BLOCK_BEGIN, "Block Begin"},
  {MARKER_TYPE::BLOCK_END, "Block End"},
  {MARKER_TYPE::BRACKET_BEGIN, "Bracket Begin"},
  {MARKER_TYPE::BRACKET_END, "Bracket End"},
  {MARKER_TYPE::COMMA, "Comma"},
  {MARKER_TYPE::COMMENT, "Comment"},
  {MARKER_TYPE::PARENTHESIS_BEGIN, "Parenthesis Begin"},
  {MARKER_TYPE::PARENTHESIS_END, "Parenthesis End"},
  {MARKER_TYPE::STR_QUOTE, "String Quote"},
};
const std::map<OPERATOR_TYPE, std::string> Mapper::OPERATOR_TO_STR = {
  {OPERATOR_TYPE::ASSIGNMENT, "="},
  {OPERATOR_TYPE::ADDITION, "+"},
  {OPERATOR_TYPE::ADDITION_ASSIGNMENT, "+="},
  {OPERATOR_TYPE::SUBTRACTION, "-"},
  {OPERATOR_TYPE::SUBTRACTION_ASSIGNMENT, "-="},
  {OPERATOR_TYPE::MULTIPLICATION, "*"},
  {OPERATOR_TYPE::MULTIPLICATION_ASSIGNMENT, "*="},
  {OPERATOR_TYPE::DIVISION, "/"},
  {OPERATOR_TYPE::DIVISION_ASSIGNMENT, "/="},
  {OPERATOR_TYPE::MODULUS, "%"},
  {OPERATOR_TYPE::MODULUS_ASSIGNMENT, "%="},
  {OPERATOR_TYPE::LESS_THAN, "<"},
  {OPERATOR_TYPE::LESS_THAN_EQUAL, "<="},
  {OPERATOR_TYPE::GREATER_THAN, ">"},
  {OPERATOR_TYPE::GREATER_THAN_EQUAL, ">="},
  {OPERATOR_TYPE::EQUAL, "=="},
  {OPERATOR_TYPE::NOT_EQUAL, "!="},
  {OPERATOR_TYPE::AND, "and"},
  {OPERATOR_TYPE::OR, "or"},
  {OPERATOR_TYPE::NOT, "not"},
  {OPERATOR_TYPE::MEMBER_ACCESS, ":"},
  {OPERATOR_TYPE::STATIC_MEMBER_ACCESS, "::"},
};
const std::map<OPERATOR_TYPE, std::string> Mapper::OPERATOR_TO_STR_NAME = {
  {OPERATOR_TYPE::ASSIGNMENT, "Assignment"},
  {OPERATOR_TYPE::ADDITION, "Addition"},
  {OPERATOR_TYPE::ADDITION_ASSIGNMENT, "Addition Assignment"},
  {OPERATOR_TYPE::SUBTRACTION, "Subtraction"},
  {OPERATOR_TYPE::SUBTRACTION_ASSIGNMENT, "Subtraction Assignment"},
  {OPERATOR_TYPE::MULTIPLICATION, "Multiplication"},
  {OPERATOR_TYPE::MULTIPLICATION_ASSIGNMENT, "Multiplication Assignment"},
  {OPERATOR_TYPE::DIVISION, "Division"},
  {OPERATOR_TYPE::DIVISION_ASSIGNMENT, "Division Assignment"},
  {OPERATOR_TYPE::MODULUS, "Modulus"},
  {OPERATOR_TYPE::MODULUS_ASSIGNMENT, "Modulus Assignment"},
  {OPERATOR_TYPE::LESS_THAN, "Less Than"},
  {OPERATOR_TYPE::LESS_THAN_EQUAL, "Less Than Equal"},
  {OPERATOR_TYPE::GREATER_THAN, "Greater Than"},
  {OPERATOR_TYPE::GREATER_THAN_EQUAL, "Greater Than Equal"},
  {OPERATOR_TYPE::EQUAL, "Equal"},
  {OPERATOR_TYPE::NOT_EQUAL, "Not Equal"},
  {OPERATOR_TYPE::AND, "And"},
  {OPERATOR_TYPE::OR, "Or"},
  {OPERATOR_TYPE::NOT, "Not"},
  {OPERATOR_TYPE::MEMBER_ACCESS, "Member Access"},
  {OPERATOR_TYPE::STATIC_MEMBER_ACCESS, "Static Member Access"},
};
const std::map<std::string, OPERATOR_TYPE> Mapper::STR_TO_OPERATOR = {
  {"=", OPERATOR_TYPE::ASSIGNMENT},
  {"+", OPERATOR_TYPE::ADDITION},
  {"+=", OPERATOR_TYPE::ADDITION_ASSIGNMENT},
  {"-", OPERATOR_TYPE::SUBTRACTION},
  {"-=", OPERATOR_TYPE::SUBTRACTION_ASSIGNMENT},
  {"*", OPERATOR_TYPE::MULTIPLICATION},
  {"*=", OPERATOR_TYPE::MULTIPLICATION_ASSIGNMENT},
  {"/", OPERATOR_TYPE::DIVISION},
  {"/=", OPERATOR_TYPE::DIVISION_ASSIGNMENT},
  {"%", OPERATOR_TYPE::MODULUS},
  {"%=", OPERATOR_TYPE::MODULUS_ASSIGNMENT},
  {"<", OPERATOR_TYPE::LESS_THAN},
  {"<=", OPERATOR_TYPE::LESS_THAN_EQUAL},
  {">", OPERATOR_TYPE::GREATER_THAN},
  {">=", OPERATOR_TYPE::GREATER_THAN_EQUAL},
  {"!=", OPERATOR_TYPE::NOT_EQUAL},
  {"==", OPERATOR_TYPE::EQUAL},
  {"and", OPERATOR_TYPE::AND},
  {"or", OPERATOR_TYPE::OR},
  {"not", OPERATOR_TYPE::NOT},
  {":", OPERATOR_TYPE::MEMBER_ACCESS},
  {"::", OPERATOR_TYPE::STATIC_MEMBER_ACCESS},
};
const std::map<TOKEN_TYPE, std::string> Mapper::TOKEN_TYPE_TO_STR_NAME = {
  {TOKEN_TYPE::IDENTIFIER, "Identifier"},
  {TOKEN_TYPE::ILLEGAL, "Illegal"},
  {TOKEN_TYPE::KEYWORD, "Keyword"},
  {TOKEN_TYPE::LITERAL, "Literal"},
  {TOKEN_TYPE::MARKER, "Marker"},
  {TOKEN_TYPE::OPERATOR, "Operator"},
};

KEYWORD_TYPE Mapper::get_keyword_enum_from_str(const std::string &str) {
  return STR_TO_KEYWORD.at(str);
}

std::string Mapper::get_keyword_name_from_enum(const KEYWORD_TYPE &keyword) {
  return KEYWORD_TO_STR_NAME.at(keyword);
}

std::string Mapper::get_literal_name_from_enum(const LITERAL_TYPE &literal) {
  return LITERAL_TO_STR_NAME.at(literal);
}

char Mapper::get_marker_char_from_enum(const MARKER_TYPE &marker) {
  return MARKER_TO_CHAR.at(marker);
}

MARKER_TYPE Mapper::get_marker_enum_from_char(const char &character) {
  return CHAR_TO_MARKER.at(character);
}

std::string Mapper::get_marker_name_from_enum(const MARKER_TYPE &marker) {
  return MARKER_TO_STR_NAME.at(marker);
}

OPERATOR_TYPE Mapper::get_operator_enum_from_str(const std::string &str) {
  return STR_TO_OPERATOR.at(str);
}

std::string Mapper::get_operator_name_from_enum(const OPERATOR_TYPE &operator_type) {
  return OPERATOR_TO_STR_NAME.at(operator_type);
}

std::string Mapper::get_operator_str_from_enum(const OPERATOR_TYPE &operator_type) {
  return OPERATOR_TO_STR.at(operator_type);
}

std::string Mapper::get_token_name_from_enum(const TOKEN_TYPE &token_type) {
  return TOKEN_TYPE_TO_STR_NAME.at(token_type);
}

bool Mapper::is_keyword(const std::string &data) {
  return STR_TO_KEYWORD.find(data) != STR_TO_KEYWORD.end();
}

bool Mapper::is_marker(const char &data) {
  return CHAR_TO_MARKER.find(data) != CHAR_TO_MARKER.end();
}

bool Mapper::is_operator(const std::string &data) {
  return STR_TO_OPERATOR.find(data) != STR_TO_OPERATOR.end();
}