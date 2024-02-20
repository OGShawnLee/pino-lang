#ifndef TRANSPILER_H
#define TRANSPILER_H

#include "parser.h"

class Transpiler {
  public:
    static void transpile(std::string filename, std::string output_filename = "main.js") {
      std::vector<Token> stream = Lexer::tokenise(filename);
      Statement statement = Parser::parse(stream);

      std::string output = "";

      for (std::unique_ptr<Statement> &statement : statement.body) {
        switch (statement->kind) {
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