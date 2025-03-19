#include "Function.h"

Function::Function(
  const std::string &identifier,
  const std::vector<std::shared_ptr<Variable>> &parameters,
  const std::vector<std::shared_ptr<Statement>> &children
) : Declaration(STATEMENT_TYPE::FUNCTION_DECLARATION, identifier) {
  this->parameters = parameters;
  this->set_children(children);
}

const std::vector<std::shared_ptr<Variable>>& Function::get_parameters() const {
  return parameters;
}

bool Function::equals(const std::shared_ptr<Statement> &candidate) const {
  if (candidate->get_type() != STATEMENT_TYPE::FUNCTION_DECLARATION) {
    return false;
  }

  std::shared_ptr<Function> function = std::dynamic_pointer_cast<Function>(candidate);

  if (this->get_identifier() != function->get_identifier()) {
    return false;
  }

  if (this->parameters.size() != function->parameters.size()) {
    return false;
  }

  for (int i = 0; i < this->parameters.size(); i++) {
    if (not this->parameters[i]->equals(function->parameters[i])) {
      return false;
    }
  }

  if (this->get_children().size() != function->get_children().size()) {
    return false;
  }

  for (int i = 0; i < this->get_children().size(); i++) {
    std::shared_ptr<Statement> local_child = this->get_children()[i];
    const Function &function_child = static_cast<Function&>(*function->get_children()[i]);
    if (not local_child->equals(function_child)) {
      return false;
    }
  }

  return true;
}