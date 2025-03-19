#include "Identifier.h"

Identifier::Identifier(std::string name) : Expression(EXPRESSION_TYPE::IDENTIFIER) {
  this->name = name;
}

std::string Identifier::get_name() const {
  return this->name;
}

bool Identifier::equals(const std::shared_ptr<Statement> &candidate) const {
  if (candidate->get_type() != STATEMENT_TYPE::EXPRESSION) {
    return false;
  }

  const Expression &expression = static_cast<const Expression&>(*candidate);

  if (expression.get_expression_type() != EXPRESSION_TYPE::IDENTIFIER) {
    return false;
  }

  return this->name == static_cast<const Identifier&>(expression).get_name();
}