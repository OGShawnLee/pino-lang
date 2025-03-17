#pragma once

#include "./Token.h"

class Operator : public Token {
  OPERATOR_TYPE operator_type;

  public:
    Operator(OPERATOR_TYPE operator_type, const std::string data);

    OPERATOR_TYPE get_marker_type() const;

    void print() const override;
};