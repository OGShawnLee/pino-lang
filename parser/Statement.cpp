#pragma once

#include "Statement.h"

void Statement::print(size_t indentation) {
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
