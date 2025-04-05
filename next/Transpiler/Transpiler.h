#pragma once

#include "Parser.h"
#include "Parser/Statement/Expression/Value.h"

class Transpiler {
  static std::string replace(std::string str, std::string from, std::string to);
  
  static std::string get_str_value(const Value &value);

  static std::string handle_expression(const Expression &expression);
  
  static std::string handle_function(const Function &function);

  static std::string handle_variable(const Variable &variable);

  public:
    static std::string transpile_line(const std::string &line);

    static std::string transpile_statement(const std::shared_ptr<Statement> statement);
    
    static void transpile_file(const std::string &file_name, const std::string &output_file);
};