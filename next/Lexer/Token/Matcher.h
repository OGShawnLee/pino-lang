#pragma once

#include <map>
#include <string>

class Matcher {
  static const std::string BOOLEAN_REGEX;
  static const std::string FLOAT_REGEX;
  static const std::string IDENTIFIER_REGEX;
  static const std::string INT_REGEX;

  public:
    static bool is_boolean(const std::string &str);

    static bool is_float(const std::string &str);

    static bool is_identifier(const char &character);

    static bool is_identifier(const std::string &str);

    static bool is_integer(const std::string &str);

    static bool is_keyword(const std::string &str);

    static bool is_marker(const char &c);

    static bool is_operator(const std::string &str);

    static bool is_str_injection(const std::string &line, const size_t &index);
};