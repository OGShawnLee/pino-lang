#pragma once

#include <algorithm>
#include <fstream>
#include <functional>
#include <iostream>
#include <map>
#include <string>
#include <vector>

void each_line(const std::string &filename, const std::function<void(const std::string&)> &callback);

template<typename K, typename V>
std::map<V, K> get_inverted_map(const std::map<K, V> &original_map) {
  std::map<V, K> inverted_map;
  for (const auto &pair : original_map) {
    inverted_map[pair.second] = pair.first;
  }
  return inverted_map;
}

std::string get_string_from_input(const std::string &prompt);

bool has_escape_character(const std::string &line, size_t index);

bool is_equal_vector_content(const std::vector<std::string> &vec_a, const std::vector<std::string> &vec_b);

bool is_prev_char(const std::string &line, int index, std::function<bool(const char &)> predicate);

bool is_next_char(const std::string &line, int index, std::function<bool(const char &)> predicate);

bool is_whitespace(const char &character);

bool is_whitespace(const std::string &line);

void println(const std::string &line);

std::string to_str(const char &character, const std::size_t &size = 1);

std::string trim(const std::string &line);
