#pragma once

#include <memory>
#include "Expression.h"

Expression::Expression() {
  kind = StatementKind::EXPRESSION;
}

PeekPtr<Expression> Expression::build(std::vector<Token> collection, size_t index) {
  PeekPtr<Expression> result;

  if (FunctionCall::is_fn_call(collection, index)) {    
    PeekPtr<FunctionCall> fn_call = FunctionCall::build(collection, index);
    result.node = std::move(fn_call.node);
    result.index = fn_call.index;
  } else if (Reassignment::is_reassigment(collection, index)) {
    PeekPtr<Reassignment> reassignment = Reassignment::build(collection, index);
    result.node = std::move(reassignment.node);
    result.index = reassignment.index;
  } else if (collection[index].kind == Kind::IDENTIFIER) {
    result.node = Identifier::from_identifier(collection[index]);
    result.index = index;
  } else if (collection[index].kind == Kind::LITERAL) {
    result.node = Value::from_literal(collection[index]);
    result.index = index;
  } else {
    throw std::runtime_error("USER: Not an Expression");
  }

  return result;
}

bool Expression::is_expression(std::vector<Token> collection, size_t index) {
  return 
    FunctionCall::is_fn_call(collection, index) || 
    Reassignment::is_reassigment(collection, index) ||
    collection[index].kind == Kind::IDENTIFIER || 
    collection[index].kind == Kind::LITERAL;
}

void Expression::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + get_expression_name(expression) + " {");
  
  if (children.size() > 0) {
    for (const std::unique_ptr<Statement> &child : children) {
      child->print(indentation + 2);
    }
  }
  
  println(indent + "}");
}

FunctionCall::FunctionCall() {
  expression = ExpressionKind::FN_CALL;
}

bool FunctionCall::is_fn_call(std::vector<Token> collection, size_t index) {
  return 
    collection[index].kind == Kind::IDENTIFIER && 
    is_next<Token>(collection, index, [](Token &token) {
      return token.is_given_marker(Marker::LEFT_PARENTHESIS);
    });
}

PeekStreamPtr<Expression> FunctionCall::handle_arguments(std::vector<Token> collection, size_t index) {
  auto marker = peek<Token>(collection, index, [](Token &token) {
    return token.is_given_marker(Marker::LEFT_PARENTHESIS);
  }); 

  PeekStreamPtr<Expression> result;

  result.index = marker.index;

  while (true) {
    auto next = peek<Token>(collection, result.index, [](Token &token) {
      return 
        token.kind == Kind::IDENTIFIER || 
        token.kind == Kind::LITERAL || 
        token.is_given_marker(Marker::RIGHT_PARENTHESIS);
    });

    result.index = next.index;

    if (next.node.kind == Kind::MARKER) {
      return result;
    }

    if (next.node.kind == Kind::IDENTIFIER) {
      std::unique_ptr<Identifier> id = Identifier::from_identifier(next.node);
      result.nodes.push_back(std::move(id));
    } else if (next.node.kind == Kind::LITERAL) {
      std::unique_ptr<Value> literal = Value::from_literal(next.node);
      result.nodes.push_back(std::move(literal));
    }

    auto marker = peek<Token>(collection, result.index, [](Token &token) {
      return token.is_given_marker(Marker::COMMA, Marker::RIGHT_PARENTHESIS);
    });

    result.index = marker.index;

    if (marker.node.is_given_marker(Marker::RIGHT_PARENTHESIS)) {
      return result;
    }
  }

  throw std::runtime_error("USER: Unterminated Function Call -> Missing Right Parenthesis");
}

PeekPtr<FunctionCall> FunctionCall::build(std::vector<Token> collection, size_t &index) {
  if (is_fn_call(collection, index) == false) {
    throw std::runtime_error("DEV: Not a Function Call");
  }

  PeekPtr<FunctionCall> result;

  result.node->name = collection[index].value;
  result.index = index;

  PeekStreamPtr<Expression> arguments = handle_arguments(collection, result.index);
  result.node->arguments = std::move(arguments.nodes);
  result.index = arguments.index;

  return result;
}

void FunctionCall::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + "FunctionCall {");
  println(indent + "  name: " + name);
  if (arguments.size() > 0) {
    println(indent + "  arguments: [");
    for (const std::unique_ptr<Expression> &argument : arguments) {
      argument->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  println(indent + "}");
}

Identifier::Identifier() {
  expression = ExpressionKind::IDENTIFIER;
}

std::unique_ptr<Identifier> Identifier::from_identifier(Token token) {
  std::unique_ptr<Identifier> identifier = std::make_unique<Identifier>();
  identifier->name = token.value;
  return identifier;
}

void Identifier::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + "Identifier {");
  println(indent + "  name: " + name);
  println(indent + "}");
}

String::String() {
  literal = Literal::STRING;
}

std::unique_ptr<String> String::from_string(Token token) {
  if (token.kind != Kind::LITERAL) {
    throw std::runtime_error("DEV: Not a String Literal Token");
  }

  std::unique_ptr<String> result = std::make_unique<String>();
  result->value = token.value;
  result->literal = token.literal;
  result->injections = handle_injections(token.injections);

  return result;
}

void String::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + "String {");
  println(indent + "  value: " + value);
  if (injections.empty() == false) {
    println(indent + "  injections: [");
    for (const std::unique_ptr<Identifier> &injection : injections) {
      injection->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  println(indent + "}");
}

Reassignment::Reassignment() {
  expression = ExpressionKind::VAR_REASSIGNMENT;
}

bool Reassignment::is_reassigment(std::vector<Token> collection, size_t index) {
  return 
    collection[index].kind == Kind::IDENTIFIER && 
    is_next<Token>(collection, index, [](Token &token) {
      return token.is_given_marker(Marker::EQUAL_SIGN);
    });
}

PeekPtr<Reassignment> Reassignment::build(std::vector<Token> collection, size_t index) {
  if (is_reassigment(collection, index) == false) {
    throw std::runtime_error("DEV: Not a Reassignment");
  }

  PeekPtr<Reassignment> result;

  result.node->identifier = collection[index].value;
  result.index = index;

  Peek<Token> equal = get_next<Token>(collection, index);
  PeekPtr<Expression> value = Expression::build(collection, equal.index + 1);
  result.node->value = std::move(value.node);
  result.index = value.index;

  return result;
}

void Reassignment::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + "Reassignment {");
  println(indent + "  identifier: " + identifier);
  value->print(indentation + 2);
  println(indent + "}");
}

std::vector<std::unique_ptr<Identifier>> String::handle_injections(std::vector<std::string> injections) {
  std::vector<std::unique_ptr<Identifier>> result;

  for (Token token : injections) {
    if (token.kind != Kind::IDENTIFIER) {
      throw std::runtime_error("Expected an identifier, but got " + token.value);
    }

    std::unique_ptr<Identifier> id = Identifier::from_identifier(token);
    result.push_back(std::move(id));
  }

  return result;
}

Value::Value() {
  expression = ExpressionKind::LITERAL;
}

std::unique_ptr<Value> Value::from_literal(Token token) {
  if (token.kind != Kind::LITERAL) {
    throw std::runtime_error("Expected a literal, but got " + token.value);
  }

  if (token.literal == Literal::STRING) {
    std::unique_ptr<String> result = String::from_string(token);
    return result;
  }

  std::unique_ptr<Value> result = std::make_unique<Value>();
  result->value = token.value;
  result->literal = token.literal;

  return result;
}

void Value::print(size_t indentation) const {
  std::string indentation_str = get_indentation(indentation);

  println(indentation_str + "Literal {");
  println(indentation_str + "  value: " + value);
  println(indentation_str + "}");
}