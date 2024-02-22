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

class Transpiler {
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

        if (fn_call->arguments.size() == 0) {
          output += ");\n";
          return output;
        }

        if (fn_call->arguments.size() == 1) {
          output += fn_call->arguments[0]->name + ");\n";
          return output;
        }

        output += fn_call->arguments[0]->name + ", ";
        for (size_t i = 1; i < fn_call->arguments.size() - 1; i++) {
          output += fn_call->arguments[i]->name + ", ";
        }
        output += fn_call->arguments[fn_call->arguments.size() - 1]->name + ");\n";
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

        if (variable->type == "String") {
          output += variable->name + " = \"" + variable->value + "\";\n";
          return output;
        }

        output += variable->name + " = " + variable->value + ";\n";
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