#pragma once

#include "parser_utils.h"
#include "../utils.h"

class Statement {
  public:
    StatementType kind;
    std::string name;
    std::string value;
    std::vector<std::unique_ptr<Statement>> body;

    virtual void print(size_t indentation = 0) {
      std::string indentation_str = get_indentation(indentation);

      println(indentation_str + get_statement_type_name(kind) + " {");
      if (name != "") {
        println(indentation_str + "  name: " + name);
      }
      if (value != "") {
        println(indentation_str + "  value: " + value);
      }
      for (std::unique_ptr<Statement> &statement : body) {
        statement->print(indentation + 2);
      }
      println(indentation_str + "}");
    }
};
