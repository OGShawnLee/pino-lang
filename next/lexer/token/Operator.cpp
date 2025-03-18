#pragma once

#include "./Operator.h"
#include "../../Common.h"

Operator::Operator(OPERATOR_TYPE operator_type, const std::string data) : Token(
  TOKEN_TYPE::MARKER, 
  data,
  Mapper::get_operator_name_from_enum(operator_type)
) {
  this->operator_type = operator_type;
}

OPERATOR_TYPE Operator::get_marker_type() const {
  return this->operator_type;
}

Operator* Operator::from_base(const std::shared_ptr<Token> &base) {
  return dynamic_cast<Operator*>(base.get());
}

void Operator::print() const {
  println("Operator {");
  println("  type: " + this->get_name());
  println("  data: " + this->get_data());
  println("}");
}