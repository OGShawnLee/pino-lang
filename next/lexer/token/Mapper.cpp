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

std::string Mapper::get_token_name_from_enum(const TOKEN_TYPE &token_type) {
  return TOKEN_TYPE_TO_STR_NAME.at(token_type);
}

bool Mapper::is_keyword(const std::string &data) {
  return STR_TO_KEYWORD.find(data) != STR_TO_KEYWORD.end();
}
