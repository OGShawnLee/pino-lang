#pragma once

#include "./Stream.h"
#include "./Token.cpp"

Stream::Stream(const std::vector<std::shared_ptr<Token>> &collection) {
  this->index = 0;
  this->collection = std::move(collection);
}

const std::shared_ptr<Token>& Stream::current() {
  return this->collection[this->index];
}

const std::shared_ptr<Token>& Stream::consume() {
  return this->collection[this->index++];
}

void Stream::increase_index() {
  this->index++;
}

void Stream::print() const {
  for (const std::shared_ptr<Token> &token : this->collection) {
    token->print();
  }
}

bool Stream::has_next() const {
  return this->index < this->collection.size();
}

bool Stream::is_empty() const {
  return this->collection.empty();
}

bool Stream::is_next(const std::function<bool(const std::shared_ptr<Token> &)> &predicate) const {
  return this->index + 1 < this->collection.size() and predicate(this->collection[this->index + 1]);
}
