#pragma once

#include <memory>
#include <string>
#include "Expression.h"
#include "Token/Literal.h"

class Value : public Expression {
  std::vector<std::string> injections;
  LITERAL_TYPE literal_type;
  std::string typing;
  std::string value;

public:
  Value(Literal literal);

  const std::vector<std::string> &get_injections() const;

  LITERAL_TYPE get_literal_type() const;

  std::string get_typing() const;

  std::string get_value() const;

  bool equals(const std::shared_ptr<Statement> &candidate) const override;
};