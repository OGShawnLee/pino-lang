#ifndef UTILS_PARSER_H
#define UTILS_PARSER_H

#include <map>

enum class StatementType {
  PROGRAM,
  VAR_DECLARATION,
  VAL_DECLARATION,
  VAR_REASSIGNMENT,
  EXPRESSION,
  FUNCTION_CALL,
  IF_STATEMENT,
};

std::map<StatementType, std::string> STATEMENT_TYPE_NAME = {
  {StatementType::PROGRAM, "Program"},
  {StatementType::VAR_DECLARATION, "Variable Declaration"},
  {StatementType::VAL_DECLARATION, "Constant Declaration"},
  {StatementType::VAR_REASSIGNMENT, "Variable Reassignment"},
  {StatementType::FUNCTION_CALL, "Function Call"},
  {StatementType::EXPRESSION, "Expression"},
  {StatementType::IF_STATEMENT, "If Statement"},
};

std::string get_statement_type_name(StatementType type) {
  return STATEMENT_TYPE_NAME.at(type);
}

#endif