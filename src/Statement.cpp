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
  {Type::IF_STATEMENT, "If Statement"},
  {Type::ELSE_STATEMENT, "Else Statement"},
  {Type::WHEN_STATEMENT, "When Statement"},
  {Type::MATCH_STATEMENT, "Match Statement"},
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

IfStatement::IfStatement(std::unique_ptr<Expression> condition, std::unique_ptr<Statement> children, std::unique_ptr<Statement> alternate) {
  set_type(Type::IF_STATEMENT);
  this->condition = std::move(condition);
  this->alternate = std::move(alternate);
  this->children = std::move(children);
}

void IfStatement::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + TYPE_NAME_MAPPING.at(get_type()) + " {");
  println(indent + "  condition: {");
  this->condition->print(indentation + 4);
  println(indent + "  }");
  println(indent + "  children: {");
  this->children->print(indentation + 4);
  println(indent + "  }");
  if (this->alternate != nullptr) {
    println(indent + "  alternate: {");
    this->alternate->print(indentation + 4);
    println(indent + "  }");
  } 
  println(indent + "}");
}

ElseStatement::ElseStatement(std::unique_ptr<Statement> children) {
  set_type(Type::ELSE_STATEMENT);
  this->children = std::move(children);
}

void ElseStatement::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + TYPE_NAME_MAPPING.at(get_type()) + " {");
  println(indent + "  children: {");
  this->children->print(indentation + 4);
  println(indent + "  }");
  println(indent + "}");
}

WhenStatement::WhenStatement(std::vector<std::unique_ptr<Expression>> conditions, std::unique_ptr<Statement> children) {
  set_type(Type::WHEN_STATEMENT);
  this->conditions = std::move(conditions);
  this->children = std::move(children);
}

void WhenStatement::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + TYPE_NAME_MAPPING.at(get_type()) + " {");
  println(indent + "  conditions: [");
  for (const std::unique_ptr<Expression> &condition : this->conditions) {
    condition->print(indentation + 4);
  }
  println(indent + "  ]");
  println(indent + "  children: {");
  this->children->print(indentation + 4);
  println(indent + "  }");
  println(indent + "}");
}

MatchStatement::MatchStatement(
  std::unique_ptr<Expression> condition, 
  std::vector<std::unique_ptr<WhenStatement>> branches,
  std::unique_ptr<ElseStatement> alternate
) {
  set_type(Type::MATCH_STATEMENT);
  this->condition = std::move(condition);
  this->children = std::move(branches);
  this->alternate = std::move(alternate);
}

void MatchStatement::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + TYPE_NAME_MAPPING.at(get_type()) + " {");
  println(indent + "  condition: {");
  this->condition->print(indentation + 4);
  println(indent + "  }");
  println(indent + "  branches: [");
  for (const std::unique_ptr<WhenStatement> &child : this->children) {
    child->print(indentation + 4);
  }
  println(indent + "  ]");
  if (this->alternate != nullptr) {
    println(indent + "  alternate: {");
    this->alternate->print(indentation + 4);
    println(indent + "  }");
  }
  println(indent + "}");
}