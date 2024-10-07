#include "Declaration.h"
#include "Expression.h"
#include "Common.h"

std::map<Expression::Kind, std::string> Expression::KIND_NAME_MAPPING = {
  {Expression::Kind::FUNCTION_CALL, "Function Call"},
  {Expression::Kind::IDENTIFIER, "Identifier"},
  {Expression::Kind::LITERAL, "Literal"},
};

Expression::Expression() {
  set_type(Type::EXPRESSION);
}

Expression::Expression(Kind kind, std::string value) {
  Expression();
  this->kind = kind;
  this->value = value;
}

void Expression::set_kind(Kind kind) {
  this->kind = kind;
}

void Expression::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + KIND_NAME_MAPPING.at(kind) + " {");
  println(indent + "  value: " + value);
  println(indent + "}");
}

BinaryExpression::BinaryExpression(std::unique_ptr<Expression> left, std::string operation, std::unique_ptr<Expression> right) {
  Expression(Kind::BINARY_EXPRESSION, "");
  this->left = std::move(left);
  this->operation = operation;
  this->right = std::move(right);
}

void BinaryExpression::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + "Binary Expression {");
  println(indent + "  left: {");
  left->print(indentation + 4);
  println(indent + "  }");
  println(indent + "  operation: " + operation);
  println(indent + "  right: {");
  right->print(indentation + 4);
  println(indent + "  }");
  println(indent + "}");
}

FunctionCall::FunctionCall(std::string callee, std::vector<std::unique_ptr<Expression>> arguments) {
  Expression(Kind::FUNCTION_CALL, callee);
  this->callee = callee;
  this->arguments = std::move(arguments);
}

void FunctionCall::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + "Function Call {");
  println(indent + "  callee: " + callee);
  if (not arguments.empty()) {
    println(indent + "  arguments: [");
    for (const auto &argument : arguments) {
      argument->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  println(indent + "}");
}

FunctionLambda::FunctionLambda(std::vector<std::unique_ptr<Variable>> parameters, std::unique_ptr<Statement> body) {
  Expression(Kind::FUNCTION_LAMBDA, "");
  this->parameters = std::move(parameters);
  this->body = std::move(body);
}

void FunctionLambda::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + "Function Lambda {");
  if (not parameters.empty()) {
    println(indent + "  parameters: [");
    for (const auto &parameter : parameters) {
      parameter->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  println(indent + "  body: {");
  body->print(indentation + 4);
  println(indent + "  }");
  println(indent + "}");
}

Vector::Vector() {
  Expression(Kind::VECTOR, "");
}

Vector::Vector(std::unique_ptr<Expression> len, std::unique_ptr<Expression> init, std::string typing) {
  this->set_type(Type::EXPRESSION);
  this->set_kind(Kind::VECTOR);
  this->len = std::move(len);
  this->init = std::move(init);
  this->typing = typing;
}

void Vector::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + "Vector {");
  
  if (not typing.empty()) {
    println(indent + "  typing: " + typing);
  }
  
  if (len) {
    println(indent + "  length: {");
    len->print(indentation + 4);
    println(indent + "  }");
  }

  if (init) {
    println(indent + "  init: {");
    init->print(indentation + 4);
    println(indent + "  }");
  }

  if (not children.empty()) {
    println(indent + "  elements: [");
    for (const auto &child : children) {
      child->print(indentation + 4);
    }
    println(indent + "  ]");
  }

  println(indent + "}");
}

StructInstance::StructInstance(std::string struct_name, std::vector<std::unique_ptr<Variable>> properties) {
  set_kind(Kind::STRUCT_INSTANCE);
  this->struct_name = struct_name;
  this->properties = std::move(properties);
}

void StructInstance::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + "Struct Instance {");
  println(indent + "  struct: " + struct_name);
  if (not properties.empty()) {
    println(indent + "  properties: [");
    for (const auto &property : properties) {
      property->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  println(indent + "}");
}

TernaryExpression::TernaryExpression(
  std::unique_ptr<Expression> condition, 
  std::unique_ptr<Expression> consequent,
  std::unique_ptr<Expression> alternate
) {
  set_kind(Kind::TERNARY_EXPRESSION);
  this->condition = std::move(condition);
  this->consequent = std::move(consequent);
  this->alternate = std::move(alternate);
}

void TernaryExpression::print(const size_t &indentation) const {
  std::string indent(indentation, ' ');

  println(indent + "Ternary Expression {");
  println(indent + "  condition: {");
  condition->print(indentation + 4);
  println(indent + "  }");
  println(indent + "  consequent: {");
  consequent->print(indentation + 4);
  println(indent + "  }");
  println(indent + "  alternate: {");
  alternate->print(indentation + 4);
  println(indent + "  }");
  println(indent + "}");
}