#pragma once

#include "./Statement/Statement.h"
#include "./Statement/Expression/Expression.h"
#include "./Statement/Expression/Identifier.h"
#include "./Statement/Expression/FunctionCall.h"
#include "../Lexer/Lexer.h"

class Variable;

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