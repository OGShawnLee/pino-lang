#ifndef UTILS_h
#define UTILS_h

#include <iostream>
#include <fstream>
#include <functional>

std::string trim(std::string str);

void each_line(std::string filename, std::function<void(std::string)> callback) {
  std::ifstream file(filename);
  std::string line;
  while (std::getline(file, line)) {
    callback(line);
  }
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
