#pragma once

#include <memory>
#include "Expression.h"
#include "Function.cpp"
#include "Lambda.cpp"

Expression::Expression() {
  kind = StatementKind::EXPRESSION;
}

PeekPtr<Expression> Expression::build(std::vector<Token> collection, size_t index) {
  PeekPtr<Expression> result;

  if (BinaryExpression::is_binary_expression(collection, index)) {
    PeekPtr<BinaryExpression> binary = BinaryExpression::build(collection, index);
    result.node = std::move(binary.node);
    result.index = binary.index;
  } else if (Lambda::is_lambda(collection, index)) {
    PeekPtr<Lambda> lambda = Lambda::build(collection, index);
    result.node = std::move(lambda.node);
    result.index = lambda.index;
  } else if (FunctionCall::is_fn_call(collection, index)) {    
    PeekPtr<FunctionCall> fn_call = FunctionCall::build(collection, index);
    result.node = std::move(fn_call.node);
    result.index = fn_call.index;
  } else if (Reassignment::is_reassigment(collection, index)) {
    PeekPtr<Reassignment> reassignment = Reassignment::build(collection, index);
    result.node = std::move(reassignment.node);
    result.index = reassignment.index;
  } else if (Struct::is_struct(collection, index)) {
    PeekPtr<Struct> struct_literal = Struct::build(collection, index);
    result.node = std::move(struct_literal.node);
    result.index = struct_literal.index;
  } else if (collection[index].kind == Kind::IDENTIFIER) {
    PeekPtr<Identifier> identifier = Identifier::build(collection, index);
    result.node = std::move(identifier.node);
    result.index = identifier.index;
  } else if (collection[index].kind == Kind::LITERAL) {
    if (collection[index].literal == Literal::VECTOR) {
      PeekPtr<Vector> vector = Vector::build(collection, index);
      result.node = std::move(vector.node);
      result.index = vector.index;
      return result;
    }

    result.node = Value::from_literal(collection[index]);
    result.index = index;
  } else if (collection[index].is_given_keyword(Keyword::YIELD_KEYWORD)) {
    PeekPtr<Yield> yield = Yield::build(collection, index);
    result.node = std::move(yield.node);
    result.index = yield.index;
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
    PeekPtr<Identifier> identifier = Identifier::build(collection, index);
    result.node->left = std::move(identifier.node);
    result.index = identifier.index;
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
    Lambda::is_lambda(collection, index) ||
    FunctionCall::is_fn_call(collection, index) || 
    Reassignment::is_reassigment(collection, index) ||
    Struct::is_struct(collection, index) ||
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
        token.is_given_keyword(Keyword::FN_KEYWORD) ||
        token.kind == Kind::IDENTIFIER || 
        token.kind == Kind::LITERAL || 
        token.is_given_marker(Marker::RIGHT_PARENTHESIS);
    });

    result.index = next.index;

    if (next.node.kind == Kind::MARKER) {
      return result;
    }

    PeekPtr<Expression> expression = Expression::build(collection, result.index);
    result.nodes.push_back(std::move(expression.node));
    result.index = expression.index;

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

PeekPtr<Identifier> Identifier::build(std::vector<Token> collection, size_t index) {
  if (collection[index].kind != Kind::IDENTIFIER) {
    throw std::runtime_error("DEV: Not an Identifier");
  }

  PeekPtr<Identifier> result;
  result.node->name = collection[index].value;
  result.node->path_str = collection[index].value;
  result.index = index;

  bool struct_notation = is_next<Token>(collection, index, [](Token &token) {
    return token.is_given_marker(Marker::COLON);
  });

  if (struct_notation) {
    result.index += 2;
    PeekPtr<Identifier> path = build(collection, result.index);
    result.node->path_str = result.node->path_str + ":" + path.node->path_str;
    result.node->path.push_back(std::move(path.node));
    result.index = path.index;
  }

  bool vector_notation = is_next<Token>(collection, result.index, [](Token &token) {
    return token.kind == Kind::LITERAL && token.literal == Literal::VECTOR;
  });

  if (vector_notation) {
    result.index += 1;
    PeekPtr<Vector> vector = Vector::build(collection, result.index);
    result.node->path_str = result.node->path_str + vector.node->value;
    result.index = vector.index;
  }

  return result;
}

std::unique_ptr<Identifier> Identifier::from_identifier(Token token) {
  std::unique_ptr<Identifier> identifier = std::make_unique<Identifier>();
  identifier->name = token.value;
  identifier->path_str = token.value;
  return identifier;
}

std::unique_ptr<Identifier> Identifier::from_str(std::string name) {
  std::unique_ptr<Identifier> identifier = std::make_unique<Identifier>();
  identifier->name = name;
  identifier->path_str = name;
  return identifier;
}

void Identifier::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + "Identifier {");
  println(indent + "  name: " + name);
  println(indent + "  path_str: " + path_str);
  if (path.size() > 0) {
    println(indent + "  path: [");
    for (const std::unique_ptr<Identifier> &id : path) {
      id->print(indentation + 4);
    }
    println(indent + "  ]");
  }
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

  for (std::string name : injections) {
    std::unique_ptr<Identifier> id = Identifier::from_str(name);
    result.push_back(std::move(id));
  }

  return result;
}

bool Field::is_prop_shortcut(std::vector<Token> collection, size_t index) {
  return 
    collection[index].kind == Kind::IDENTIFIER &&
    is_next<Token>(collection, index, [](Token &token) {
      return token.is_given_marker(Marker::COLON);
    }) == false;
}

PeekPtr<Field> Field::build(std::vector<Token> collection, size_t index) {
  PeekPtr<Field> result;

  auto name = peek<Token>(collection, index, [](Token &token) {
    return token.kind == Kind::IDENTIFIER;
  });

  result.node->name = name.node.value;
  result.index = name.index;

  auto typing = peek<Token>(collection, result.index, [](Token &token) {
    return token.kind == Kind::IDENTIFIER;
  });

  result.node->typing = typing.node.value;
  result.index = typing.index;

  return result;
}

PeekPtr<Field> Field::build_as_property(std::vector<Token> collection, size_t index) {
  PeekPtr<Field> result;

  auto name = peek<Token>(collection, index, [](Token &token) {
    return token.kind == Kind::IDENTIFIER;
  });

  result.node->name = name.node.value;
  result.index = name.index;

  if (is_prop_shortcut(collection, result.index) == false) {
    auto colon = peek<Token>(collection, result.index, [](Token &token) {
      return token.is_given_marker(Marker::COLON);
    });

    result.index = colon.index;

    PeekPtr<Expression> value = Expression::build(collection, result.index + 1);
    result.node->value = std::move(value.node);
    result.index = value.index;
  } 

  bool is_comma = is_next<Token>(collection, result.index, [](Token &token) {
    return token.is_given_marker(Marker::COMMA);
  });

  if (is_comma) result.index += 1;

  return result;
}

void Field::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + "Field {");
  println(indent + "  name: " + name);
  
  if (typing.empty() == false) {
    println(indent + "  typing: " + typing);
  }

  if (value.get() != nullptr) {
    value->print(indentation + 2);
  }

  println(indent + "}");
}

Struct::Struct() {
  literal = Literal::STRUCT;
}

bool Struct::is_struct(std::vector<Token> collection, size_t index) {
  return 
    is_previous<Token>(collection, index, [](Token &token) {
      if (token.kind == Kind::KEYWORD && token.keyword == Keyword::RETURN_KEYWORD) 
        return true;

      return token.kind != Kind::KEYWORD;
    }) &&
    collection[index].kind == Kind::IDENTIFIER && 
    is_next<Token>(collection, index, [](Token &token) {
      return token.is_given_marker(Marker::LEFT_BRACE);
    });
}

PeekPtr<Struct> Struct::build(std::vector<Token> collection, size_t index) {
  if (is_struct(collection, index) == false) {
    throw std::runtime_error("DEV: Not a Struct Literal");
  }

  PeekPtr<Struct> result;
  
  result.node->name = collection[index].value;
  result.index = index;

  auto left_brace = peek<Token>(collection, index, [](Token &token) {
    return token.is_given_marker(Marker::LEFT_BRACE);
  });

  result.index = left_brace.index;

  while (true) {
    bool is_right_brace = is_next<Token>(collection, result.index, [](Token &token) {
      return token.is_given_marker(Marker::RIGHT_BRACE);
    });

    if (is_right_brace) {
      result.index += 1;
      return result;
    }
    
    PeekPtr<Field> field = Field::build_as_property(collection, result.index);
    result.node->fields.push_back(std::move(field.node));
    result.index = field.index;
  }
}

void Struct::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + "Struct {");
  println(indent + "  name: " + name);
  if (fields.size() > 0) {
    println(indent + "  fields: [");
    for (const std::unique_ptr<Field> &field : fields) {
      field->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  println(indent + "}");
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

void Vector::handle_children(std::vector<Token> children) {
  size_t index = 0;

  this->typing = infer_typing(children[0].literal);

  while (true) {
    PeekPtr<Expression> expression = Expression::build(children, index);
    this->children.push_back(std::move(expression.node));
    index = expression.index;

    if (index + 1 >= children.size()) {
      return;
    }

    auto comma = peek<Token>(children, index, [](Token &token) {
      return token.is_given_marker(Marker::COMMA);
    });

    if (comma.node.is_given_marker(Marker::COMMA)) {
      index = comma.index + 1;
    }
  }
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
  Token arr = collection[index];

  if (arr.is_given_literal(Literal::VECTOR) == false) {
    throw std::runtime_error("DEV: Not a Vector Literal");
  }

  PeekPtr<Vector> result;
  result.node->value = collection[index].value;

  bool is_empty = result.node->value == "[]";

  if (is_empty) {
    auto typing = peek<Token>(collection, index, [](Token &token) {
      return token.kind == Kind::IDENTIFIER;
    });

    result.node->typing = typing.node.value;
    result.index = result.node->handle_init_block(collection, typing.index);
  } else {
    result.node->handle_children(arr.children);
    result.index = index; 
  }

  return result;
}

void Vector::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + "Vector {");
  println(indent + "  typing: " + typing);
  if (len.get() != nullptr) {
    len->print(indentation + 2);
  }
  if (init.get() != nullptr) {
    init->print(indentation + 2);
  }
  if (children.size() > 0) {
    println(indent + "  value: " + value);
    println(indent + "  children: [");
    for (const std::unique_ptr<Expression> &child : children) {
      child->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  println(indent + "}");
}

PeekPtr<Yield> Yield::build(std::vector<Token> collection, size_t index) {
  if (collection[index].keyword != Keyword::YIELD_KEYWORD) {
    throw std::runtime_error("DEV: Not a Yield Statement");
  }

  PeekPtr<Yield> result;
  result.index = index;

  bool no_arguments = Expression::is_expression(collection, index + 1) == false;
  if (no_arguments) {
    return result;
  }

  while (true) {
    PeekPtr<Expression> argument = Expression::build(collection, result.index + 1);
    result.node->arguments.push_back(std::move(argument.node));
    result.index = argument.index;

    bool is_comma = is_next<Token>(collection, result.index, [](Token &token) {
      return token.is_given_marker(Marker::COMMA);
    });

    result.index += 1;

    if (is_comma == false) {
      return result;
    }
  }
}

void Yield::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + "Yield {");
  if (arguments.size() > 0) {
    println(indent + "  arguments: [");
    for (const std::unique_ptr<Expression> &argument : arguments) {
      argument->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  println(indent + "}");
}