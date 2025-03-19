#include <algorithm>
#include <fstream>
#include <iostream>
#include "Common.h"

void each_line(const std::string &filename, const std::function<void(const std::string&)> &callback) {
  std::ifstream file(filename);
  std::string line;
  while (std::getline(file, line)) {
    callback(line);
  }
}

std::string get_string_from_input(const std::string &prompt) {
  std::string input;
  std::cout << prompt;
  std::getline(std::cin, input);
  return input;
}

bool is_equal_vector_content(const std::vector<std::string> &vec_a, const std::vector<std::string> &vec_b) {
  if (vec_a.size() != vec_b.size()) {
    return false;
  }
  for (size_t i = 0; i < vec_a.size(); i++) {
    if (vec_a[i] != vec_b[i]) {
      return false;
    }
  }
  return true;
}

bool is_prev_char(const std::string &line, int index, std::function<bool(const char &)> predicate) {
  if (index - 1 < 0) return false;
  return predicate(line[index - 1]);
}

bool is_next_char(const std::string &line, int index, std::function<bool(const char &)> predicate) {
  if (index + 1 >= line.size()) return false;
  return predicate(line[index + 1]);
}

bool is_whitespace(const char &character) {
  return character == ' ' || character == '\t' || character == '\n';
}

bool is_whitespace(const std::string &line) {
  return std::all_of(line.begin(), line.end(), [](const char &character) {
    return is_whitespace(character);
  });
}

bool has_escape_character(const std::string &line, size_t index) {
  return is_prev_char(line, index, [](const char &character) {
    return character == '\\';
  });
}

void println(const std::string &line) {
  std::cout << line << std::endl;
}

std::string to_str(const char &character, const std::size_t &size) {
  return std::string(size, character);
}

std::string trim(const std::string &line) {
  std::string trimmed = line;
  trimmed.erase(0, trimmed.find_first_not_of(" \t\n"));
  trimmed.erase(trimmed.find_last_not_of(" \t\n") + 1);
  return trimmed;
}
