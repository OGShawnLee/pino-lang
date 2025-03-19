#include "Expression.h"

Expression::Expression(EXPRESSION_TYPE expression_type) : Statement(STATEMENT_TYPE::EXPRESSION) {
  this->expression_type = expression_type;
}

EXPRESSION_TYPE Expression::get_expression_type() const {
  return this->expression_type;
}

bool Expression::equals(const std::shared_ptr<Statement> &candidate) const {
  if (candidate->get_type() != STATEMENT_TYPE::EXPRESSION) {
    return false;
  }

  return this->expression_type == static_cast<const Expression&>(*candidate).get_expression_type();
}