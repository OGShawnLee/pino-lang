#pragma once

#include <memory>
#include "Expression.h"

Expression::Expression() {
  kind = StatementKind::EXPRESSION;
}

PeekPtr<Expression> Expression::build(std::vector<Token> collection, size_t index) {
  PeekPtr<Expression> result;

  if (BinaryExpression::is_binary_expression(collection, index)) {
    PeekPtr<BinaryExpression> binary = BinaryExpression::build(collection, index);
    result.node = std::move(binary.node);
    result.index = binary.index;
  } else if (FunctionCall::is_fn_call(collection, index)) {    
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
    if (collection[index].literal == Literal::VECTOR) {
      PeekPtr<Vector> vector = Vector::build(collection, index);
      result.node = std::move(vector.node);
      result.index = vector.index;
      return result;
    }

    result.node = Value::from_literal(collection[index]);
    result.index = index;
  } else {
    throw std::runtime_error("USER: Not an Expression");
  }

  return result;
}

BinaryExpression::BinaryExpression() {
  expression = ExpressionKind::BINARY_EXPRESSION;
}

PeekPtr<BinaryExpression> BinaryExpression::build(std::vector<Token> collection, size_t index) {
  if (is_binary_expression(collection, index) == false) {
    throw std::runtime_error("DEV: Not a Binary Expression");
  }

  PeekPtr<BinaryExpression> result;

  if (FunctionCall::is_fn_call(collection, index)) {
    PeekPtr<FunctionCall> fn_call = FunctionCall::build(collection, index);
    result.node->left = std::move(fn_call.node);
    result.index = fn_call.index;
  } else if (Reassignment::is_reassigment(collection, index)) {
    PeekPtr<Reassignment> reassignment = Reassignment::build(collection, index);
    result.node->left = std::move(reassignment.node);
    result.index = reassignment.index;
  } else if (collection[index].kind == Kind::IDENTIFIER) {
    result.node->left = Identifier::from_identifier(collection[index]);
    result.index = index;
  } else if (collection[index].kind == Kind::LITERAL) {
    result.node->left = Value::from_literal(collection[index]);
    result.index = index;
  } else {
    throw std::runtime_error("USER: Not an Expression");
  }

  auto operation = peek<Token>(collection, result.index, [](Token &token) {
    return token.kind == Kind::BINARY_OPERATOR;
  });

  result.node->operation = operation.node.binary_operator;
  result.node->operator_str = operation.node.value;
  result.index = operation.index + 1;

  PeekPtr<Expression> right = Expression::build(collection, result.index);
  result.node->right = std::move(right.node);
  result.index = right.index;

  return result;
}

bool BinaryExpression::is_binary_expression(std::vector<Token> collection, size_t index) {
  if (FunctionCall::is_fn_call(collection, index) || Reassignment::is_reassigment(collection, index)) {
    return is_next<Token>(collection, index + 1, [](Token &token) {
      return token.kind == Kind::BINARY_OPERATOR;
    });
  }

  return 
    collection[index].is_given_kind(Kind::IDENTIFIER, Kind::LITERAL) && 
    is_next<Token>(collection, index, [](Token &token) {
      return token.kind == Kind::BINARY_OPERATOR;
    });
}

void BinaryExpression::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + get_expression_name(expression)  + " {");
  left->print(indentation + 2);
  println(indent + "  operation: " + Token::get_binary_operator_name(operation));
  right->print(indentation + 2);
  println(indent + "}");
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

Vector::Vector() {
  literal = Literal::VECTOR;
}

size_t Vector::handle_init_block(std::vector<Token> collection, size_t index) {
  auto left_brace = peek<Token>(collection, index, [](Token &token) {
    return token.is_given_marker(Marker::LEFT_BRACE);
  });

  index = left_brace.index;

  auto len = peek<Token>(collection, index, [](Token &token) {
    return token.kind == Kind::IDENTIFIER && token.value == "len";
  });

  index = len.index;

  auto colon = peek<Token>(collection, index, [](Token &token) {
    return token.is_given_marker(Marker::COLON);
  });

  index = colon.index;

  PeekPtr<Expression> len_value = Expression::build(collection, index + 1);
  this->len = std::move(len_value.node);

  index = len_value.index;


  bool has_init = is_next<Token>(collection, index, [](Token &token) {
    return token.is_given_marker(Marker::COMMA);
  });

  if (has_init == false) {
    return index;
  }

  index += 1;

  auto init = peek<Token>(collection, index, [](Token &token) {
    return token.kind == Kind::IDENTIFIER && token.value == "init";
  });

  index = init.index;

  auto init_colon = peek<Token>(collection, init.index, [](Token &token) {
    return token.is_given_marker(Marker::COLON);
  });

  index = init_colon.index;

  PeekPtr<Expression> init_value = Expression::build(collection, index + 1);
  this->init = std::move(init_value.node);

  index = init_value.index;

  auto right_brace = peek<Token>(collection, index, [](Token &token) {
    return token.is_given_marker(Marker::RIGHT_BRACE);
  });

  index = right_brace.index;

  return index;
}

PeekPtr<Vector> Vector::build(std::vector<Token> collection, size_t index) {
  if (collection[index].is_given_literal(Literal::VECTOR) == false) {
    throw std::runtime_error("DEV: Not a Vector Literal");
  }

  PeekPtr<Vector> result;
  result.node->value = collection[index].value;

  auto typing = peek<Token>(collection, index, [](Token &token) {
    return token.kind == Kind::BUILT_IN_TYPE;
  });

  result.node->typing = typing.node.type;
  result.index = result.node->handle_init_block(collection, typing.index);

  return result;
}

void Vector::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + "Vector {");
  if (len.get() != nullptr) {
    len->print(indentation + 2);
  }
  if (init.get() != nullptr) {
    init->print(indentation + 2);
  }
  println(indent + "}");
}