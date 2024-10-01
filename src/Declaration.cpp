#include "Declaration.h"
#include "Common.h"

std::map<Variable::Kind, std::string> Variable::KIND_NAME_MAPPING = {
  {Variable::Kind::CONSTANT_DECLARATION, "Constant Declaration"},
  {Variable::Kind::VARIABLE_DECLARATION, "Variable Declaration"},
  {Variable::Kind::PARAMETER_DECLARATION, "Parameter Declaration"},
};

Variable::Variable(Kind kind, std::string identifier, std::unique_ptr<Expression> value) {
  set_type(
    kind == Kind::PARAMETER_DECLARATION or kind == Kind::CONSTANT_DECLARATION
      ? Type::CONSTANT_DECLARATION
      : Type::VARIABLE_DECLARATION
  );
  this->kind = kind;
  this->identifier = identifier;
  this->value = std::move(value);
}

Variable::Variable(Kind kind, std::string identifier, std::string typing) {
  set_type(
    kind == Kind::PARAMETER_DECLARATION or kind == Kind::CONSTANT_DECLARATION
      ? Type::CONSTANT_DECLARATION
      : Type::VARIABLE_DECLARATION
  );
  this->kind = kind;
  this->identifier = identifier;
  this->typing = typing;
}

Function::Function(std::string identifier, std::vector<std::unique_ptr<Variable>> parameters) {
  set_type(Type::FUNCTION_DECLARATION);
  this->identifier = identifier;
  this->parameters = std::move(parameters);
}

Struct::Struct(std::string identifier, std::vector<std::unique_ptr<Variable>> fields) {
  set_type(Type::STRUCT_DECLARATION);
  this->identifier = identifier;
  this->fields = std::move(fields);
}

Enum::Enum(std::string identifier, std::vector<std::string> fields) {
  set_type(Type::ENUM_DECLARATION);
  this->identifier = identifier;
  this->fields = std::move(fields);
}

void Variable::print(const size_t &indentation = 0) const {
  std::string indent(indentation, ' ');

  println(indent + KIND_NAME_MAPPING.at(kind) + " {");
  println(indent + "  identifier: " + identifier);
  if (not typing.empty()) {
    println(indent + "  typing: " + typing);
  }
  if (value) {
    println(indent + "  value: {");
    value->print(indentation + 4);
    println(indent + "  }");
  }
  println(indent + "}");
}

void Function::print(const size_t &indentation = 0) const {
  std::string indent(indentation, ' ');

  println(indent + "Function Declaration {");
  println(indent + "  identifier: " + identifier);
  if (not parameters.empty()) {
    println(indent + "  parameters: [");
    for (const auto &parameter : parameters) {
      parameter->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  println(indent + "}");
}

void Struct::print(const size_t &indentation = 0) const {
  std::string indent(indentation, ' ');

  println(indent + "Struct Declaration {");
  println(indent + "  identifier: " + identifier);
  if (not fields.empty()) {
    println(indent + "  fields: [");
    for (const auto &field : fields) {
      field->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  println(indent + "}");
}

void Enum::print(const size_t &indentation = 0) const {
  std::string indent(indentation, ' ');

  println(indent + "Enum Declaration {");
  println(indent + "  identifier: " + identifier);
  if (not fields.empty()) {
    println(indent + "  fields: [");
    for (const auto &field : fields) {
      println(indent + "    " + field);
    }
    println(indent + "  ]");
  }
  println(indent + "}");
}
