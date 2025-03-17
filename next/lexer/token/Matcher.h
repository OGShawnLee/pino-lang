#pragma once

#include <map>
#include <string>

class Matcher {
  static const std::string BOOLEAN_REGEX;
  static const std::string FLOAT_REGEX;
  static const std::string IDENTIFIER_REGEX;
  static const std::string INT_REGEX;

  public:
    static inline bool is_boolean(const std::string &str);

    static inline bool is_float(const std::string &str);

    static inline bool is_identifier(const std::string &str);

    static inline bool is_integer(const std::string &str);

    static inline bool is_keyword(const std::string &str);
};