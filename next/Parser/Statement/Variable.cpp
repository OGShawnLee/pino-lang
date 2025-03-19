#include <stdexcept>
#include "Variable.h"
#include "Parser.h"

Variable::Variable(
  const std::string &identifier, 
  std::shared_ptr<Expression> value, 
  std::string type,
  VARIABLE_KIND variable_kind
) : Declaration(STATEMENT_TYPE::VARIABLE_DECLARATION, identifier) {
  this->value = value;
  this->type = type;
  this->variable_kind = variable_kind;
}

Variable::Variable(
  const std::string &identifier, 
  std::string type,
  VARIABLE_KIND variable_kind
) : Declaration(STATEMENT_TYPE::VARIABLE_DECLARATION, identifier) {
  this->type = type;
  this->variable_kind = variable_kind;
}

// @WARNING: This constructor is used for testing purposes only.
Variable::Variable(
  const std::string &identifier, 
  std::string value, 
  std::string type,
  VARIABLE_KIND variable_kind
) : Declaration(STATEMENT_TYPE::VARIABLE_DECLARATION, identifier) {
  
  std::shared_ptr<Statement> statement = Parser::parse_line(value);
  
  if (statement->get_type() == STATEMENT_TYPE::EXPRESSION) {
    this->value = std::dynamic_pointer_cast<Expression>(statement);
  } else {
    throw std::runtime_error("PARSER-{Variable}: Invalid Expression");
  }
  
  this->type = type;
  this->variable_kind = variable_kind;
}

const std::shared_ptr<Expression>& Variable::get_value() const {
  return this->value;
}

std::string Variable::get_type() const {
  return this->type;
}

VARIABLE_KIND Variable::get_variable_kind() const {
  return this->variable_kind;
}

bool Variable::equals(const std::shared_ptr<Statement> &candidate) const {
  if (
    candidate->get_type() != STATEMENT_TYPE::VARIABLE_DECLARATION and
    candidate->get_type() != STATEMENT_TYPE::CONSTANT_DECLARATION
  ) {
    return false;
  }

  const Variable &variable = static_cast<const Variable&>(*candidate);
  
  if (this->get_variable_kind() == VARIABLE_KIND::PARAMETER) {
    return 
      this->get_identifier() == variable.get_identifier() and
      this->get_type() == variable.get_type() and
      this->variable_kind == variable.get_variable_kind();
  }

  return
    this->get_identifier() == variable.get_identifier() and
    this->value->equals(variable.get_value()) and
    this->type == variable.get_type() and
    this->variable_kind == variable.get_variable_kind();
}