#include "Declaration.h"

Declaration::Declaration(STATEMENT_TYPE statement_type, const std::string &identifier)  : Statement(statement_type) {
  this->identifier = identifier;
}

std::string Declaration::get_identifier() const {
  return identifier;
}