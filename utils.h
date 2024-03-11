#pragma once

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
Peek<T> get_next(std::vector<T> stream, size_t index) {
  if (index + 1 > stream.size()) {
    throw std::runtime_error("USER: Unexpected End of Input");
  }

  Peek<T> result;
  result.node = stream[index + 1];
  result.index = index + 1;
  return result;
}

Peek<char> peek(
  std::string line,
  size_t index,
  std::function<bool(char)> is_valid_node
) {
  if (index + 1 > line.length()) {
    throw std::runtime_error("Unexpected EOF");
  }

  if (is_valid_node(line[index + 1])) {
    Peek<char> result;
    result.node = line[index + 1];
    result.index = index + 1;
    return result;
  }

  throw std::runtime_error("Unexpected Token");
}

template <typename T>
Peek<T> peek(
  std::vector<T> stream,
  size_t index,
  std::function<bool(T&)> is_valid_node
) {
  if (index + 1 > stream.size()) {
    throw std::runtime_error("Unexpected EOF");
  }

  if (is_valid_node(stream[index + 1])) {
    Peek<T> result;
    result.node = stream[index + 1];
    result.index = index + 1;
    return result;
  }

  throw std::runtime_error("Unexpected Token");
}

size_t index_of(std::string line, char character, size_t index) {
  for (size_t i = index; i < line.length(); i++) {
    if (line[i] == character) {
      return i;
    }
  }

  return -1;
}

template <typename T>
bool is_previous(
  std::vector<T> stream,
  size_t index,
  std::function<bool(T&)> is_valid_node
) {
  if (index - 1 < 0) {
    return false;
  }

  return is_valid_node(stream[index - 1]);
}

bool is_next_char(
  std::string stream,
  size_t index,
  std::function<bool(char)> is_valid_node
) {
  if (index + 1 > stream.size()) {
    return false;
  }

  return is_valid_node(stream[index + 1]);
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

void println(std::string str = "") {
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
