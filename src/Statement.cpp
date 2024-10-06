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
  {Type::LOOP_STATEMENT, "Loop Statement"},
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

Loop::Loop(Kind kind, std::unique_ptr<Expression> begin, std::unique_ptr<Expression> end, std::unique_ptr<Statement> children) {
  set_type(Type::LOOP_STATEMENT);
  this->kind = kind;
  this->begin = std::move(begin);
  this->end = std::move(end);
  this->children = std::move(children);
}

std::map<Loop::Kind, std::string> Loop::KIND_NAME_MAPPING = {
  {Kind::FOR_IN_LOOP, "For In Loop"},
  {Kind::FOR_TIMES_LOOP, "For Times Loop"},
  {Kind::INFINITE_LOOP, "Infinite Loop"},
};

void Loop::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + TYPE_NAME_MAPPING.at(get_type()) + " {");
  println(indent + "  kind: " + KIND_NAME_MAPPING.at(kind));
  if (this->begin != nullptr) {
    println(indent + "  begin: {");
    this->begin->print(indentation + 4);
    println(indent + "  }");
  }
  if (this->end != nullptr) {
    println(indent + "  end: {");
    this->end->print(indentation + 4);
    println(indent + "  }");
  }
  println(indent + "  children: {");
  this->children->print(indentation + 4);
  println(indent + "  }");
  println(indent + "}");
}