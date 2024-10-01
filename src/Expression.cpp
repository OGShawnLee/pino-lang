#include "Expression.h"
#include "Common.h"

std::map<Expression::Kind, std::string> Expression::KIND_NAME_MAPPING = {
  {Expression::Kind::ASSIGNMENT, "Assignment"},
  {Expression::Kind::IDENTIFIER, "Identifier"},
  {Expression::Kind::LITERAL, "Literal"},
};

Expression::Expression(Kind kind, std::string value) {
  set_type(Type::EXPRESSION);
  this->kind = kind;
  this->value = value;
}

void Expression::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + KIND_NAME_MAPPING.at(kind) + " {");
  println(indent + "  value: " + value);
  println(indent + "}");
}
