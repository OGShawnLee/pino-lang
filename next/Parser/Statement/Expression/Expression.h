#pragma once

#include "Statement/Statement.h"

class Expression : public Statement {
  EXPRESSION_TYPE expression_type;

public:
  Expression(EXPRESSION_TYPE expression_type);

  EXPRESSION_TYPE get_expression_type() const;

  virtual bool equals(const std::shared_ptr<Statement> &candidate) const;
};