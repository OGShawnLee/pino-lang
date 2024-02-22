#pragma once

#include "Statement.h"
#include "global.h"

class Parser {
  public:
    static PeekStreamPtr<Statement> parse_block(std::vector<Token> stream, size_t index);

    static Statement parse(std::vector<Token> stream);
};
