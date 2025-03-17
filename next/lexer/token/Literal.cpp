#pragma once

#include <regex>
#include "./Literal.h"
#include "./Mapper.cpp"
#include "../../Common.h"

Literal::Literal(LITERAL_TYPE literal_type, std::string data) : Token(
  TOKEN_TYPE::LITERAL, 
  data,
  Mapper::get_literal_name_from_enum(literal_type)
) {
  this->literal_type = literal_type;
}

LITERAL_TYPE Literal::get_literal_type() const {
  return literal_type;
}

void Literal::print() const {
  println("Literal {");
  println("  type: " + get_name());
  println("  data: " + get_data());
  println("}");
}
