#pragma once

#include "Statement.h"

class Expression : public Statement {
  public:
    enum class Kind {
      ASSIGNMENT,
      IDENTIFIER,
      LITERAL,
    };

    Expression(Kind kind, std::string value);
    
  private:
    Kind kind;
    std::string value;

    static std::map<Kind, std::string> KIND_NAME_MAPPING;

  public:
    void print(const size_t &indentation) const override;
};
