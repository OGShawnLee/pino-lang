#pragma once

#include "Statement.h"

class Declaration : public Statement {
  std::string identifier;

  public:
    Declaration(STATEMENT_TYPE statement_type, const std::string &identifier);

    std::string get_identifier() const;
};