#pragma once

#include <vector>
#include <memory>
#include "parser_utils.h"

class Statement {
  public:
    StatementType kind;
    std::string name;
    std::string value;
    std::vector<std::unique_ptr<Statement>> body;

    virtual void print(size_t indentation = 0);
};
