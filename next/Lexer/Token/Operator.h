#pragma once

#include <memory>
#include "Token.h"

class Operator : public Token {
  OPERATOR_TYPE operator_type;

  public:
    Operator(OPERATOR_TYPE operator_type);

    OPERATOR_TYPE get_marker_type() const;
    
    static Operator* from_base(const std::shared_ptr<Token> &base);
    
    void print() const override;
};