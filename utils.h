#ifndef UTILS_h
#define UTILS_h

#include <iostream>
#include <fstream>
#include <functional>
#include "types.h"

std::string trim(std::string str);

void each_line(std::string filename, std::function<void(std::string)> callback) {
  std::ifstream file(filename);
  std::string line;
  while (std::getline(file, line)) {
    callback(line);
  }
}

std::string get_indentation(size_t indentation) {
  std::string result = "";
  for (size_t i = 0; i < indentation; i++) {
    result += " ";
  }
  return result;
}

bool is_whitespace(char character) {
  return character == ' ' || character == '\t' || character == '\n';
}

bool is_whitespace(std::string str) {
  for (int i = 0; i < str.length(); i++) {
    if (is_whitespace(str[i]) == false) return false;
  }

  return true;
}

template <typename T>
Peek<T> peek(
  std::vector<T> stream,
  size_t index,
  std::function<bool(T&)> is_valid_node,
  std::function<std::runtime_error(T&)> on_error,
  std::function<std::runtime_error(T&)> on_end_of_stream
) {
  if (index + 1 > stream.size()) {
    throw on_end_of_stream(stream[index + 1]);
  }

  if (is_valid_node(stream[index + 1])) {
    Peek<T> result;
    result.node = stream[index + 1];
    result.index = index + 1;
    return result;
  }

  throw on_error(stream[index]);
}

template <typename T>
bool is_next(
  std::vector<T> stream,
  size_t index,
  std::function<bool(T&)> is_valid_node
) {
  if (index + 1 > stream.size()) {
    return false;
  }

  return is_valid_node(stream[index + 1]);
}

void println(std::string str) {
  std::cout << str << std::endl;
}

void println(char character) {
  std::cout << character << std::endl;
}

std::string trim(std::string str) {
  int start = 0;
  int end = str.length() - 1;
  while (is_whitespace(str[start])) start++;
  while (is_whitespace(str[end])) end--;
  return str.substr(start, end - start + 1);
}

#endif
