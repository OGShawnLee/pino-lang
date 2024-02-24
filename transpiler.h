#ifndef TRANSPILER_H
#define TRANSPILER_H

#include "./parser/ControlFlow.h"
#include "./parser/Function.h"
#include "./parser/Parser.cpp"
#include "./parser/Variable.h"
#include "./lexer/lexer.h"
#include <map>

enum class BuiltInFn {
  PRINT_LN
};

std::map<std::string, BuiltInFn> BUILT_IN_FN_KEY = {
  {"println", BuiltInFn::PRINT_LN}
};

std::map<BuiltInFn, std::string> BUILT_IN_FN_JS_NAME = {
  {BuiltInFn::PRINT_LN, "console.log"}
};

bool is_built_in_fn(std::string str) {
  return BUILT_IN_FN_KEY.find(str) != BUILT_IN_FN_KEY.end();
}

BuiltInFn get_built_in_fn(std::string str) {
  if (is_built_in_fn(str)) {
    return BUILT_IN_FN_KEY.at(str);
  }

  throw "Not a built-in function";
}

std::string get_built_in_fn_name(std::string str) {
  BuiltInFn fn = get_built_in_fn(str);
  return BUILT_IN_FN_JS_NAME.at(fn);
}

std::string replace(std::string str, std::string from, std::string to) {
  size_t start_pos = 0;
  while((start_pos = str.find(from, start_pos)) != std::string::npos) {
    str.replace(start_pos, from.length(), to);
    start_pos += to.length();
  }
  return str;
}

class Transpiler {
  static std::string transpile_argument(std::unique_ptr<Expression> &argument) {
    switch (argument->type) {
      case ExpressionType::IDENTIFIER: {
        Identifier *identifier = static_cast<Identifier *>(argument.get());
        return identifier->name;
      }
      case ExpressionType::LITERAL: {
        Value *value = static_cast<Value *>(argument.get());
        
        if (value->literal == Literal::STRING) {
          String *string = static_cast<String *>(argument.get());
          return transpile_str(string);
        }

        return value->value;
      }
    }

    throw "Invalid Argument Type";
  }

  static std::string transpile_arguments(std::vector<std::unique_ptr<Expression>> &arguments) {
    std::string output = "";

    if (arguments.size() == 0) {
      return output + ");\n";
    }

    if (arguments.size() == 1) {
      return output + transpile_argument(arguments[0]) +  ");\n";
    }

    output += transpile_argument(arguments[0]) + ", ";
    for (size_t i = 1; i < arguments.size() - 1; i++) {
      output += transpile_argument(arguments[i]) + ", ";
    }
    output += transpile_argument(arguments[arguments.size() - 1]) + ");\n";
    return output;
  }

  static std::string transpile_str(String *string) {
    if (string->body.size() > 0) {
      for (std::unique_ptr<Identifier> &identifier : string->body) {
        std::string replace_value = "${" + identifier->name + "}"; 
        string->value = replace(string->value, "$" + identifier->name, replace_value);
      }

      return "`" + string->value + "`" ;
    }

    return "\"" + string->value + "\"";
  }

  static std::string transpile_statement(Statement *statement, size_t indent = 0) {
    std::string indentation = get_indentation(indent);
    switch (statement->kind) {
      case StatementType::IF_STATEMENT: {
        IFStatement *if_statement = static_cast<IFStatement *>(statement);
        std::string output = indentation + "if (" + if_statement->condition + ") {\n";
        for (std::unique_ptr<Statement> &statement : if_statement->body) {
          output += transpile_statement(statement.get(), indent + 2);
        }

        if (if_statement->else_statement != nullptr) {
          output += indentation + "} else {\n";
          for (std::unique_ptr<Statement> &statement : if_statement->else_statement->body) {
            output += transpile_statement(statement.get(), indent + 2);
          }
        }

        output += indentation + "}\n";
        return output;
      }
      case StatementType::FUNCTION_DEFINITION: {
        FunctionDefinition *fn_def = static_cast<FunctionDefinition *>(statement);
        std::string output = indentation + "function " + fn_def->name + "(";

        if (fn_def->parameters.size() == 0) {
          output += ") {\n";
          for (std::unique_ptr<Statement> &statement : fn_def->body) {
            output += transpile_statement(statement.get(), indent + 2);
          }
          output += indentation + "}\n";
          return output;
        }

        if (fn_def->parameters.size() == 1) {
          output += fn_def->parameters[0].name + ") {\n";
          for (std::unique_ptr<Statement> &statement : fn_def->body) {
            output += transpile_statement(statement.get(), indent + 2);
          }
          output += indentation + "}\n";
          return output;
        }

        output += fn_def->parameters[0].name + ", ";
        for (size_t i = 1; i < fn_def->parameters.size() - 1; i++) {
          output += fn_def->parameters[i].name + ", ";
        }
        output += fn_def->parameters[fn_def->parameters.size() - 1].name + ") {\n";
        for (std::unique_ptr<Statement> &statement : fn_def->body) {
          output += transpile_statement(statement.get(), indent + 2);
        }
        output += indentation + "}\n";
        return output;
      }
      case StatementType::FUNCTION_CALL: {
        FunctionCall *fn_call = static_cast<FunctionCall *>(statement);

        std::string output;
        if (is_built_in_fn(fn_call->name)) {
          output = indentation + get_built_in_fn_name(fn_call->name) + "(";
        } else {
          output = indentation + fn_call->name + "(";
        }

        output += transpile_arguments(fn_call->arguments);
        return output;
      }
      case StatementType::LOOP_STATEMENT: {
        Loop *loop = static_cast<Loop *>(statement);
        std::string output = "";

        if (loop->times->type == ExpressionType::IDENTIFIER) {
          Identifier *identifier = static_cast<Identifier *>(loop->times.get());
          output = indentation + "for (let i = 0; i < " + identifier->name + "; i++) {\n";
        } else if (loop->times->type == ExpressionType::LITERAL) {
          Value *value = static_cast<Value *>(loop->times.get());
          output = indentation + "for (let i = 0; i < " + value->value + "; i++) {\n";
        }

        for (std::unique_ptr<Statement> &statement : loop->body) {
          output += transpile_statement(statement.get(), indent + 2);
        }
        
        output += indentation + "}\n";
        return output;
      }
      case StatementType::VAL_DECLARATION:
      case StatementType::VAR_DECLARATION:
      case StatementType::VAR_REASSIGNMENT: {
        Variable *variable = static_cast<Variable *>(statement);
        std::string output = indentation;

        if (variable->kind == StatementType::VAL_DECLARATION) {
          output += "const ";
        } else if (variable->kind == StatementType::VAR_DECLARATION) {
          output += "let ";
        }

        if (variable->value->literal == Literal::STRING) {
          String *string = static_cast<String *>(variable->value.get());
          output += variable->name + " = " + transpile_str(string) + ";\n";
          return output;
        }

        output += variable->name + " = " + variable->value->value + ";\n";
        return output;
      }
    }

    throw "Invalid statement";
  }

  public:
    static void transpile(std::string filename, std::string output_filename = "main.js") {
      std::vector<Token> stream = Lexer::tokenise(filename);
      Statement statement = Parser::parse(stream);

      std::string output = "";

      for (std::unique_ptr<Statement> &statement : statement.body) {
        switch (statement->kind) {
          case StatementType::IF_STATEMENT:
          case StatementType::FUNCTION_CALL:
          case StatementType::FUNCTION_DEFINITION:
          case StatementType::LOOP_STATEMENT:
          case StatementType::VAL_DECLARATION:
          case StatementType::VAR_DECLARATION:
          case StatementType::VAR_REASSIGNMENT:
            output += transpile_statement(statement.get());
            break;
          default:
            println("Invalid statement type");
        }
      }

      std::ofstream file(output_filename);
      file << output;
      file.close();
    }
};

#endif