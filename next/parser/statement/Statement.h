#pragma once

#include <memory>
#include <vector>
#include "./ParserMapper.h"

class Statement {
  STATEMENT_TYPE statement_type;
  std::vector<std::shared_ptr<Statement>> children;

  public:
    Statement(STATEMENT_TYPE statement_type) {
      this->statement_type = statement_type;
    }
    
    STATEMENT_TYPE get_type() const {
      return this->statement_type;
    }

    const std::vector<std::shared_ptr<Statement>>& get_children() const {
      return this->children;
    }

    virtual bool equals(const Statement &candidate) const {
      return this->statement_type == candidate.get_type();
    }
};