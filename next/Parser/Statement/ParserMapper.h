#pragma once

#include <map>
#include <string>
#include "Mapper.h"

enum class BUILT_IN_TYPE {
  BOOLEAN,
  FLOAT,
  INTEGER,
  STRING,
};

enum class STATEMENT_TYPE {
  CONSTANT_DECLARATION,
  EXPRESSION,
  PROGRAM,
  VARIABLE_DECLARATION,
};

enum class EXPRESSION_TYPE {
  FUNCTION_CALL,
  IDENTIFIER,
  LITERAL,
};

enum class VARIABLE_KIND {
  CONSTANT,
  VARIABLE,
};

class ParserMapper {
  static const std::map<EXPRESSION_TYPE, std::string> EXPRESSION_TYPE_TO_STR_NAME;
  static const std::map<STATEMENT_TYPE, std::string> STATEMENT_TYPE_TO_STR_NAME;
  static const std::map<BUILT_IN_TYPE, std::string> BUILT_IN_TYPE_TO_STR_NAME;
  static const std::map<LITERAL_TYPE, BUILT_IN_TYPE> LITERAL_TYPE_TO_BUILT_IN_TYPE;

  public:    
    static std::string get_expression_name_from_enum(const EXPRESSION_TYPE &type);
    
    static std::string get_statement_name_from_enum(const STATEMENT_TYPE &type);

    static BUILT_IN_TYPE infer_built_in_type_from_literal_type(const LITERAL_TYPE &type);
};