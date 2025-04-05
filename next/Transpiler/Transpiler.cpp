#include <fstream>
#include "Transpiler.h"
#include "Statement/Variable.h"
#include "Statement/Expression/Value.h"
#include "Statement/Expression/FunctionCall.h"

std::string Transpiler::replace(std::string str, std::string from, std::string to) {
  size_t start_pos = 0;
  while((start_pos = str.find(from, start_pos)) != std::string::npos) {
    str.replace(start_pos, from.length(), to);
    start_pos += to.length();
  }
  return str;
}

 std::string Transpiler::get_str_value(const Value &value) {
    if (not (value.get_literal_type() == LITERAL_TYPE::STRING)) {
      throw std::runtime_error("DEV: Not a String Literal");
    }
  
    const std::vector<std::string> &injections = value.get_injections();
    
    if (injections.size() == 0) {
      return "\"" + value.get_value() + "\"";
    }

    std::string str_value = "f\"" + value.get_value() + "\"";

    for (const std::string &injection : value.get_injections()) {
      str_value = replace(str_value, "#" + injection, "{" + injection + "}");
    }

    return str_value;
  }

std::string Transpiler::handle_expression(const Expression &expression) {
  std::string output;

  switch (expression.get_expression_type()) {
    case EXPRESSION_TYPE::IDENTIFIER:
      output = static_cast<const Identifier&>(expression).get_name();
      break;
    case EXPRESSION_TYPE::LITERAL: {
      const Value& value = static_cast<const Value&>(expression);
      if (value.get_literal_type() == LITERAL_TYPE::STRING) {
        output = get_str_value(value);
      } else if (value.get_literal_type() == LITERAL_TYPE::BOOLEAN) {
        output = value.get_value() == "true" ? "True" : "False";
      } else {
        output = value.get_value();
      }
      break;
    }
    case EXPRESSION_TYPE::FUNCTION_CALL:
      const FunctionCall& function_call = static_cast<const FunctionCall&>(expression);
      output = function_call.get_callee() + "(";

      if (function_call.get_arguments().size() == 0) {
        output += ")";
        break;
      }

      if (function_call.get_arguments().size() == 1) {
        output += handle_expression(*function_call.get_arguments()[0]) + ")";
        break;
      }

      for (size_t i = 0; i < function_call.get_arguments().size() - 1; i++) {
        output += handle_expression(*function_call.get_arguments()[i]) + ", ";
      }

      output += handle_expression(*function_call.get_arguments().back()) + ")";
      break;
  }

  return output;
}

std::string Transpiler::handle_function(const Function &function) {
  std::string output = "def " + function.get_identifier() + "(";

  if (function.get_parameters().size() == 0) {
    output += "):\n";
  } else if (function.get_parameters().size() == 1) {
    output += function.get_parameters()[0]->get_identifier() + "):\n";
  } else {
    for (size_t i = 0; i < function.get_parameters().size() - 1; i++) {
      output += function.get_parameters()[i]->get_identifier() + ", ";
    }
    output += function.get_parameters().back()->get_identifier() + "):\n";
  }

  if (function.get_children().size() == 0) {
    output += "\tpass\n";
  }

  for (const std::shared_ptr<Statement> &child : function.get_children()) {
    output += "\t" + transpile_statement(child) + "\n";
  }

  return output;
}

std::string Transpiler::handle_variable(const Variable &variable) {
  return variable.get_identifier() + " = " + handle_expression(
    static_cast<const Expression&>(*variable.get_value())
  );
}

std::string Transpiler::transpile_line(const std::string &line) {
  return transpile_statement(Parser::parse_line(line));
}

std::string Transpiler::transpile_statement(const std::shared_ptr<Statement> statement) {
  std::string output;

  switch (statement->get_type()) {
    case STATEMENT_TYPE::PROGRAM:
      break;
    case STATEMENT_TYPE::CONSTANT_DECLARATION:
    case STATEMENT_TYPE::VARIABLE_DECLARATION:
      return handle_variable(static_cast<const Variable&>(*statement));
    case STATEMENT_TYPE::FUNCTION_DECLARATION:
      return handle_function(static_cast<const Function&>(*statement));
      break;
    case STATEMENT_TYPE::EXPRESSION:
      return handle_expression(static_cast<const Expression&>(*statement));
  }

  return output;
}

void Transpiler::transpile_file(const std::string &file_name, const std::string &output_file_name) {
  std::ofstream output_file(output_file_name);
  if (output_file.is_open()) {
    std::shared_ptr<Statement> program = Parser::parse_file(file_name);
    for (const std::shared_ptr<Statement> &statement : program->get_children()) {
      output_file << transpile_statement(statement) << "\n";
    }
    output_file.close();
  } else {
    throw std::runtime_error("Unable to open output file");
  }
}