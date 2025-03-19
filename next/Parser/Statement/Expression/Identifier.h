#pragma once

#include <string>
#include "Expression.h"

class Identifier : public Expression {
  std::string name;

public:
  Identifier(std::string name);

  std::string get_name() const;

  bool equals(const std::shared_ptr<Statement> &candidate) const override;
};