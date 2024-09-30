#include "Statement.h"
#include "Common.h"

std::map<Statement::Type, std::string> Statement::TYPE_NAME_MAPPING = {
  {Type::PROGRAM, "Program"},
  {Type::CONSTANT_DECLARATION, "Constant Declaration"},
  {Type::VARIABLE_DECLARATION, "Variable Declaration"},
};

Statement::Statement() {
  this->type = Type::PROGRAM;
}

void Statement::set_type(const Type &type) {
  this->type = type;
}

void Statement::push(std::unique_ptr<Statement> child) {
  this->children.push_back(std::move(child));
}

void Statement::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + TYPE_NAME_MAPPING.at(type) + " {");
  if (this->children.size() > 0) {
    println(indent + "  children: [");
    for (const std::unique_ptr<Statement> &child : this->children) {
      child->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  println(indent + "}");
}
