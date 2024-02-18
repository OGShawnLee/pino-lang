#ifndef COMMONS_PARSER_H
#define COMMONS_PARSER_H

#include <map>

enum class StatementType {
  PROGRAM,
  VAR_DECLARATION,
  VAL_DECLARATION,
  VAR_REASSIGNMENT,
};

std::map<StatementType, std::string> STATEMENT_TYPE_NAME = {
  {StatementType::PROGRAM, "Program"},
  {StatementType::VAR_DECLARATION, "Variable Declaration"},
  {StatementType::VAL_DECLARATION, "Constant Declaration"},
  {StatementType::VAR_REASSIGNMENT, "Variable Reassignment"},
};

std::string get_statement_type_name(StatementType type) {
  return STATEMENT_TYPE_NAME.at(type);
}

#endif