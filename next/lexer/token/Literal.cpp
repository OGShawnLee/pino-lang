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

Literal::Literal(LITERAL_TYPE literal_type, std::string data, std::vector<std::string> injections) : Token(
  TOKEN_TYPE::LITERAL, 
  data,
  Mapper::get_literal_name_from_enum(literal_type)
) {
  this->literal_type = literal_type;
  this->injections = injections;
}

LITERAL_TYPE Literal::get_literal_type() const {
  return literal_type;
}

const std::vector<std::string>& Literal::get_injections() const {
  return injections;
}

bool Literal::equals(const Token &other) const {
  if (other.get_type() != TOKEN_TYPE::LITERAL) {
    return false;
  }

  const Literal &literal = static_cast<const Literal&>(other);

  return 
    this->get_literal_type() == literal.get_literal_type() and 
    this->get_data() == literal.get_data() && 
    is_equal_vector_content(injections, literal.get_injections());
}

void Literal::print() const {
  println("Literal {");
  println("  type: " + get_name());
  println("  data: " + get_data());

  if (injections.size() > 0) {
    println("  injections: [");
    for (const std::string &injection : injections) {
      println("    " + injection);
    }
    println("  ]");
  }

  println("}");
}
