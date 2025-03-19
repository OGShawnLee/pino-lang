#pragma once

#include "Lexer.h"
#include "Statement/Function.h"
#include "Statement/Variable.h"
#include "Statement/Expression/Expression.h"
#include "Statement/Expression/FunctionCall.h"
#include "Statement/Expression/Identifier.h"

class Parser {
  static std::shared_ptr<Expression> consume_assignment(Stream &stream);

  static std::vector<std::shared_ptr<Expression>> consume_arguments(Stream &stream);
  
  static KEYWORD_TYPE consume_keyword(Stream &stream); 

  static std::shared_ptr<Variable> consume_parameter(Stream &stream);

  static std::vector<std::shared_ptr<Variable>> consume_parameters(Stream &stream);

  static std::string consume_typing(Stream &stream);
  
  static bool is_expression(Stream &stream);

  static bool is_function_call(Stream &stream);

  static std::shared_ptr<Statement> parse_block(Stream &stream);

  static std::shared_ptr<Expression> parse_expression(Stream &stream); 

  static std::shared_ptr<Function> parse_function(Stream &stream); 

  static std::shared_ptr<FunctionCall> parse_function_call(Stream &stream); 

  static std::shared_ptr<Identifier> parse_identifier(Stream &stream); 

  static std::shared_ptr<Variable> parse_variable(Stream &stream);

  public:
    static std::shared_ptr<Statement> parse_line(const std::string &line);
};