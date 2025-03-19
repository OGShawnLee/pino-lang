#pragma once

#include <memory>
#include <vector>
#include "ParserMapper.h"

class Statement {
  STATEMENT_TYPE statement_type;
  std::vector<std::shared_ptr<Statement>> children;

public:
  Statement(STATEMENT_TYPE statement_type);

  STATEMENT_TYPE get_type() const;

  const std::vector<std::shared_ptr<Statement>>& get_children() const;

  virtual bool equals(const Statement &candidate) const;
};