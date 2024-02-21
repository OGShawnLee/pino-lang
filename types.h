#ifndef TYPES_H
#define TYPES_H

#include <memory>

template <typename T>
struct Peek {
  T node; 
  size_t index;
};

template <typename T>
struct PeekPtr {
  std::unique_ptr<T> node;
  size_t index;

  PeekPtr() {
    node = std::make_unique<T>();
    index = 0;
  }
};

template <typename T>
struct PeekStream {
  std::vector<T> nodes;
  size_t index;
};

template <typename T>
struct PeekStreamPtr {
  std::vector<std::unique_ptr<T>> nodes;
  size_t index;
};

#endif