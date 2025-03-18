#pragma once

#include "../../Common.h"
#include "./Keyword.h"
#include "./Mapper.cpp"

Keyword::Keyword(KEYWORD_TYPE keyword) : Token(
  TOKEN_TYPE::KEYWORD, 
  Mapper::get_keyword_str_from_enum(keyword), 
  Mapper::get_keyword_name_from_enum(keyword)
) {
  this->keyword = keyword;
}

KEYWORD_TYPE Keyword::get_keyword() const {
  return keyword;
}

Keyword* Keyword::from_base(const std::shared_ptr<Token> &base) {
  return dynamic_cast<Keyword*>(base.get());
}

void Keyword::print() const {
  println("Keyword {");
  println("  type: " + get_name());
  println("  data: " + get_data());
  println("}");
}
