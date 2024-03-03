#pragma once

#include <functional>
#include <memory>
#include "Statement.h"

class Block {
  static PeekStreamPtr<Statement> build_with_break(
    std::vector<Token> collection,
    size_t index,
    std::function<bool(Token &)> is_end_of_block
  );

  public:
    static std::vector<std::unique_ptr<Statement>> build(std::vector<Token> collection);

    static PeekStreamPtr<Statement> build(std::vector<Token> collection, size_t index);
};
