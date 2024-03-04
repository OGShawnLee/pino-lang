#pragma once

#include <functional>
#include <memory>
#include "Statement.h"

class Block {
  public:
    static std::vector<std::unique_ptr<Statement>> build_program(std::vector<Token> collection);

    static PeekStreamPtr<Statement> build(std::vector<Token> collection, size_t index);
};
