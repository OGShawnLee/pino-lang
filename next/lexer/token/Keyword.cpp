#pragma once

#include "../../Common.h"
#include "./Keyword.h"
#include "./Mapper.cpp"

Keyword::Keyword(KEYWORD_TYPE keyword, std::string data) : Token(
  TOKEN_TYPE::KEYWORD, 
  data, 
  Mapper::get_keyword_name_from_enum(keyword)
) {
  this->keyword = keyword;
}

KEYWORD_TYPE Keyword::get_keyword() const {
  return keyword;
}

void Keyword::print() const {
  println("Keyword {");
  println("  type: " + get_name());
  println("  data: " + get_data());
  println("}");
}
