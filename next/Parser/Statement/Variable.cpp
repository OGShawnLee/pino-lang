#include "Variable.h"
#include "Parser.h"
#include "Common.h"

Variable::Variable(
  std::string identifier, 
  std::shared_ptr<Expression> value, 
  std::string type,
  VARIABLE_KIND variable_kind
) : Statement(STATEMENT_TYPE::VARIABLE_DECLARATION) {
  this->identifier = identifier;
  this->value = value;
  this->type = type;
  this->variable_kind = variable_kind;
}

// @WARNING: This constructor is used for testing purposes only.
Variable::Variable(
  std::string identifier, 
  std::string value, 
  std::string type,
  VARIABLE_KIND variable_kind
) : Statement(STATEMENT_TYPE::VARIABLE_DECLARATION) {
  
  std::shared_ptr<Statement> statement = Parser::parse_line(value);
  
  if (statement->get_type() == STATEMENT_TYPE::EXPRESSION) {
    this->value = std::dynamic_pointer_cast<Expression>(statement);
  } else {
    throw std::runtime_error("PARSER-{Variable}: Invalid Expression");
  }
  
  this->identifier = identifier;
  this->type = type;
  this->variable_kind = variable_kind;
}

std::string Variable::get_identifier() const {
  return this->identifier;
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
  return
    this->identifier == variable.get_identifier() &&
    this->value->equals(variable.get_value()) && 
    this->type == variable.get_type() &&
    this->variable_kind == variable.get_variable_kind();
}