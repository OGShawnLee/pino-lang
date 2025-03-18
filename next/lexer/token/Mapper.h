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

enum class MARKER_TYPE {
  BLOCK_BEGIN, 
  BLOCK_END,
  BRACKET_BEGIN,
  BRACKET_END,
  COMMA,
  COMMENT,
  PARENTHESIS_BEGIN,
  PARENTHESIS_END,
  STR_QUOTE,
};

enum class OPERATOR_TYPE {
  ASSIGNMENT,
  ADDITION,
  ADDITION_ASSIGNMENT,
  SUBTRACTION,
  SUBTRACTION_ASSIGNMENT,
  MULTIPLICATION,
  MULTIPLICATION_ASSIGNMENT,
  DIVISION,
  DIVISION_ASSIGNMENT,
  MODULUS,
  MODULUS_ASSIGNMENT,
  LESS_THAN,
  LESS_THAN_EQUAL,
  GREATER_THAN,
  GREATER_THAN_EQUAL,
  EQUAL,
  NOT_EQUAL,
  AND,
  OR,
  NOT,
  MEMBER_ACCESS,
  STATIC_MEMBER_ACCESS,
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
  static const std::map<LITERAL_TYPE, std::string> LITERAL_TO_STR_NAME;
  static const std::map<MARKER_TYPE, char> MARKER_TO_CHAR;
  static const std::map<MARKER_TYPE, std::string> MARKER_TO_STR_NAME;
  static const std::map<OPERATOR_TYPE, std::string> OPERATOR_TO_STR_NAME;
  static const std::map<TOKEN_TYPE, std::string> TOKEN_TYPE_TO_STR_NAME;
  static const std::map<KEYWORD_TYPE, std::string> KEYWORD_TO_STR_NAME;
  
  public:
    static const std::map<std::string, KEYWORD_TYPE> STR_TO_KEYWORD;
    static const std::map<char, MARKER_TYPE> CHAR_TO_MARKER;
    static const std::map<std::string, OPERATOR_TYPE> STR_TO_OPERATOR;

    inline static KEYWORD_TYPE get_keyword_enum_from_str(const std::string &str);

    inline static std::string get_keyword_name_from_enum(const KEYWORD_TYPE &keyword);

    inline static std::string get_literal_name_from_enum(const LITERAL_TYPE &literal);

    inline static char get_marker_char_from_enum(const MARKER_TYPE &marker);

    inline static MARKER_TYPE get_marker_enum_from_char(const char &character);

    inline static std::string get_marker_name_from_enum(const MARKER_TYPE &marker);

    inline static OPERATOR_TYPE get_operator_enum_from_str(const std::string &str);

    inline static std::string get_operator_name_from_enum(const OPERATOR_TYPE &operator_type);

    inline static std::string get_token_name_from_enum(const TOKEN_TYPE &token_type);

    inline static bool is_keyword(const std::string &data);

    inline static bool is_marker(const char &data);

    inline static bool is_operator(const std::string &data);
};
