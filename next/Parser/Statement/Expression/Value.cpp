#include "Value.h"

Value::Value(Literal literal) : Expression(EXPRESSION_TYPE::LITERAL) {
  this->literal_type = literal.get_literal_type();
  this->typing = literal.get_name();
  this->value = literal.get_data();
}

LITERAL_TYPE Value::get_literal_type() const {
  return this->literal_type;
}

std::string Value::get_typing() const {
  return this->typing;
}

std::string Value::get_value() const {
  return this->value;
}

bool Value::equals(const std::shared_ptr<Statement> &candidate) const {
  if (candidate->get_type() != STATEMENT_TYPE::EXPRESSION) {
    return false;
  }

  const Expression& expression = static_cast<const Expression&>(*candidate);
  if (expression.get_expression_type() != EXPRESSION_TYPE::LITERAL) {
    return false;
  }

  return this->literal_type == static_cast<const Value&>(expression).get_literal_type();
}