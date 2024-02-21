#ifndef TRANSPILER_H
#define TRANSPILER_H

#include "parser.h"

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
  public:
    static void transpile(std::string filename, std::string output_filename = "main.js") {
      std::vector<Token> stream = Lexer::tokenise(filename);
      Statement statement = Parser::parse(stream);

      std::string output = "";

      for (std::unique_ptr<Statement> &statement : statement.body) {
        switch (statement->kind) {
          case StatementType::FUNCTION_CALL: {
            Function *fn_call = static_cast<Function *>(statement.get());

            if (is_built_in_fn(fn_call->name) == false) {
              throw "Not a built-in function";
            }

            output += get_built_in_fn_name(fn_call->name) + "(";

            if (fn_call->arguments.size() == 0) {
              output += ");\n";
              break;
            }

            if (fn_call->arguments.size() == 1) {
              output += fn_call->arguments[0]->name + ");\n";
              break;
            }

            output += fn_call->arguments[0]->name + ", ";
            for (size_t i = 1; i < fn_call->arguments.size() - 1; i++) {
              output += fn_call->arguments[i]->name + ", ";
            }
            output += fn_call->arguments[fn_call->arguments.size() - 1]->name + ");\n";
            break;
          }
          case StatementType::VAL_DECLARATION:
          case StatementType::VAR_DECLARATION:
          case StatementType::VAR_REASSIGNMENT:
            Variable *variable = static_cast<Variable *>(statement.get());
            if (variable->kind == StatementType::VAL_DECLARATION) {
              output += "const ";
            } else if (variable->kind == StatementType::VAR_DECLARATION) {
              output += "let ";
            }

            if (variable->type == "String") {
              output += variable->name + " = \"" + variable->value + "\";\n";
              break;
            }

            output += variable->name + " = " + variable->value + ";\n";
            break;
        }
      }

      std::ofstream file(output_filename);
      file << output;
      file.close();
    }
};

#endif