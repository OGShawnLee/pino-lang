#include "Statement.h"
#include "Common.h"
#include "Expression.h"

std::map<Statement::Type, std::string> Statement::TYPE_NAME_MAPPING = {
  {Type::PROGRAM, "Program"},
  {Type::BLOCK, "Block Statement"},
  {Type::CONSTANT_DECLARATION, "Constant Declaration"},
  {Type::VARIABLE_DECLARATION, "Variable Declaration"},
  {Type::FUNCTION_DECLARATION, "Function Declaration"},
  {Type::STRUCT_DECLARATION, "Struct Declaration"},
  {Type::ENUM_DECLARATION, "Enum Declaration"},
  {Type::RETURN, "Return Statement"},
};

Statement::Statement() {
  this->type = Type::PROGRAM;
}

Statement::Statement(Type type) {
  this->type = type;
}

Statement::Type Statement::get_type() const {
  return this->type;
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

Return::Return(std::unique_ptr<Expression> argument) {
  this->set_type(Type::RETURN);
  this->argument = std::move(argument);
}

void Return::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + TYPE_NAME_MAPPING.at(get_type())  + " {");
  if (this->argument != nullptr) {
    println(indent + "  argument: {");
    this->argument->print(indentation + 4);
    println(indent + "  }");
  }
  println(indent + "}");
}