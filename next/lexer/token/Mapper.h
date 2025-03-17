#pragma once

#include <map>
#include <string>

enum class KEYWORD_TYPE {
  AS,
  BREAK,
  CONSTANT,
  CONTINUE, 
  ELSE, 
  ENUM,
  FROM, 
  FUNCTION, 
  IF, 
  IMPORT, 
  IN,
  LOOP, 
  MATCH, 
  PUB,
  RETURN,
  STATIC, 
  STRUCT, 
  THEN, 
  VARIABLE,
  WHEN,
};

enum class LITERAL_TYPE {
  BOOLEAN,
  FLOAT,
  INTEGER,
  STRING,
};

enum class TOKEN_TYPE {
  IDENTIFIER,
  ILLEGAL,
  KEYWORD,
  LITERAL,
  MARKER,
  OPERATOR,
};

class Mapper {
  static const std::map<KEYWORD_TYPE, std::string> KEYWORD_TO_STR_NAME;
  static const std::map<std::string, KEYWORD_TYPE> STR_TO_KEYWORD;
  static const std::map<LITERAL_TYPE, std::string> LITERAL_TO_STR_NAME;
  static const std::map<TOKEN_TYPE, std::string> TOKEN_TYPE_TO_STR_NAME;

  public:
    inline static KEYWORD_TYPE get_keyword_enum_from_str(const std::string &str);

    inline static std::string get_keyword_name_from_enum(const KEYWORD_TYPE &keyword);

    inline static std::string get_literal_name_from_enum(const LITERAL_TYPE &literal);

    inline static std::string get_token_name_from_enum(const TOKEN_TYPE &token_type);

    inline static bool is_keyword(const std::string &data);
};