#include <algorithm>
#include <iostream>
#include <fstream>
#include <functional>

void each_line(const std::string &filename, const std::function<void(const std::string&)> &callback) {
  std::ifstream file(filename);
  std::string line;
  
  while (std::getline(file, line)) {
    callback(line);
  }
}

bool is_whitespace(const char &character) {
  return character == ' ' or character == '\t' or character == '\n';
}

bool is_whitespace(const std::string &line) {
  return std::all_of(line.begin(), line.end(), [](const char &character) {
    return is_whitespace(character);
  });
}

void println(const std::string &line) {
  std::cout << line << std::endl;
}

std::string trim(const std::string &line) {
  std::string trimmed = line;
  trimmed.erase(0, trimmed.find_first_not_of(" \t\n"));
  trimmed.erase(trimmed.find_last_not_of(" \t\n") + 1);
  return trimmed;
}