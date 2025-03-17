#pragma once

#include <algorithm>
#include <fstream>
#include <functional>
#include <iostream>
#include <vector>

inline void each_line(const std::string &filename, const std::function<void(const std::string&)> &callback) {
  std::ifstream file(filename);
  std::string line;
  
  while (std::getline(file, line)) {
    callback(line);
  }
}

inline std::string get_string_from_input(const std::string &prompt) {
	std::string input;
	std::cout << prompt;
	std::getline(std::cin, input);
	return input;
}

inline bool is_equal_vector_content(
  const std::vector<std::string> &vec_a, 
  const std::vector<std::string> &vec_b
) {
  if (vec_a.size() != vec_b.size()) {
    return false;
  }

  for (int i = 0; i < vec_a.size(); i++) {
    if (vec_a[i] != vec_b[i]) {
      return false;
    }
  }

  return true;
}

inline bool is_prev_char(const std::string &line, int index, std::function<bool(const char &)> predicate) {
  if (index - 1 < 0) return false;
  return predicate(line[index - 1]);
}

inline bool is_next_char(const std::string &line, int index, std::function<bool(const char &)> predicate) {
  if (index + 1 > line.size()) return false;
  return predicate(line[index + 1]);
}

inline bool is_whitespace(const char &character) {
  return character == ' ' or character == '\t' or character == '\n';
}

inline bool is_whitespace(const std::string &line) {
  return std::all_of(line.begin(), line.end(), [](const char &character) {
    return is_whitespace(character);
  });
}

inline bool has_escape_character(const std::string &line, size_t index) {
  return is_prev_char(line, index, [](const char &character) {
    return character == '\\';
  });
}

inline void println(const std::string &line) {
  std::cout << line << std::endl;
}

inline std::string to_str(const char &character, const std::size_t &size = 1) {
  return std::string(size, character);
}

inline std::string trim(const std::string &line) {
  std::string trimmed = line;
  trimmed.erase(0, trimmed.find_first_not_of(" \t\n"));
  trimmed.erase(trimmed.find_last_not_of(" \t\n") + 1);
  return trimmed;
}