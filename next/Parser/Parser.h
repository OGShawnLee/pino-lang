#pragma once

#include "Statement.h"
#include "Expression.h"
#include "FunctionCall.h"
#include "Identifier.h"
#include "Lexer.h"
#include "Variable.h"

class Parser {
  static std::shared_ptr<Expression> consume_assignment(Stream &stream);

  static std::vector<std::shared_ptr<Expression>> consume_arguments(Stream &stream);
  
  static KEYWORD_TYPE consume_keyword(Stream &stream); 
  
  static bool is_expression(Stream &stream);

  static bool is_function_call(Stream &stream);

  static std::shared_ptr<Expression> parse_expression(Stream &stream); 

  static std::shared_ptr<FunctionCall> parse_function_call(Stream &stream); 

  static std::shared_ptr<Identifier> parse_identifier(Stream &stream); 

  static std::shared_ptr<Variable> parse_variable(Stream &stream);

  public:
    static std::shared_ptr<Statement> parse_line(const std::string &line);
};