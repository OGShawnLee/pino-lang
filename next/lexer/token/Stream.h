#pragma once

#include <functional>
#include <memory>
#include <vector>
#include "./Token.h"

class Stream {
  size_t index;
  std::vector<std::shared_ptr<Token>> collection;

  public:
    Stream(const std::vector<std::shared_ptr<Token>> &collection);

    const std::shared_ptr<Token>& consume();
    
    const std::shared_ptr<Token>& current();
    
    bool has_next() const;
    
    bool is_next(const std::function<bool(const std::shared_ptr<Token> &)> &predicate) const;
    
    void increase_index();
    
    void print() const;
};
