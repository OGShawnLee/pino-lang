#include "Statement.h"

Statement::Statement(STATEMENT_TYPE statement_type) {
  this->statement_type = statement_type;
}

STATEMENT_TYPE Statement::get_type() const {
  return this->statement_type;
}

const std::vector<std::shared_ptr<Statement>>& Statement::get_children() const {
  return this->children;
}

const std::shared_ptr<std::shared_ptr<Statement>> Statement::push(
  const std::shared_ptr<Statement> &child
) {
  this->children.push_back(child);
  return std::make_shared<std::shared_ptr<Statement>>(child);
}

bool Statement::equals(const Statement &candidate) const {
  return this->statement_type == candidate.get_type();
}

void Statement::set_children(const std::vector<std::shared_ptr<Statement>> &children) {
  this->children = children;
}