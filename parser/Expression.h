#pragma once

#include "Statement.h"
#include "global.h"

class Expression : public Statement {
  public:
    ExpressionType type;

    Expression() {
      kind = StatementType::EXPRESSION;
    }
};

class Identifier : public Expression {
  public:
    std::string name;

    Identifier() {
      type = ExpressionType::IDENTIFIER;
    }

    static std::unique_ptr<Identifier> from_identifier(Token token) {
      if (token.kind != Kind::IDENTIFIER) {
        throw std::runtime_error("Expected an identifier, but got " + token.value);
      }

      std::unique_ptr<Identifier> result = std::make_unique<Identifier>();
      result->name = token.value;
      return result;
    }

    void print(size_t indentation) {
      std::string indentation_str = get_indentation(indentation);

      println(indentation_str + "Identifier {");
      println(indentation_str + "  name: " + name);
      println(indentation_str + "}");
    }
};

class Value : public Expression {
  public:
    std::string value;
    Literal literal;

    Value() {
      type = ExpressionType::LITERAL;
    }

    static std::unique_ptr<Value> from_literal(Token token) {
      if (token.kind != Kind::LITERAL) {
        throw std::runtime_error("Expected a literal, but got " + token.value);
      }

      std::unique_ptr<Value> result = std::make_unique<Value>();
      result->value = token.value;
      result->literal = get_literal(token.name);
      return result;
    }

    static PeekPtr<Value> build(std::vector<Token> stream, size_t index);

    void print(size_t indentation) {
      std::string indentation_str = get_indentation(indentation);

      println(indentation_str + "Value {");
      println(indentation_str + "  value: " + value);
      println(indentation_str + "  literal: " + get_literal_name(literal));
      println(indentation_str + "}");
    }
};

class String : public Value {
  public:
    std::vector<std::unique_ptr<Identifier>> body;

    String() {
      literal = Literal::STRING;
    }

  static PeekPtr<String> build(std::vector<Token> stream, size_t index) {
    Token current = stream[index];
    if (current.is_given_marker(Marker::DOUBLE_QUOTE) == false) {
      std::runtime_error("not a string literal " + current.value);
    }

    PeekPtr<String> result;

    for (size_t i = index + 1; i < stream.size(); i++) {
      Token token = stream[i];

      if (token.kind == Kind::IDENTIFIER) {
        std::unique_ptr<Identifier> id = std::make_unique<Identifier>();
        id->name = token.value;
        result.node->body.push_back(std::move(id));
      }

      if (token.kind == Kind::LITERAL) {
        result.node->value = token.value;
      }

      if (token.is_given_marker(Marker::DOUBLE_QUOTE)) {
        result.index = i;
        return result;
      }
    }

    throw std::runtime_error("Unterminated String Literal");
  }

  void print(size_t indentation) {
    std::string indentation_str = get_indentation(indentation);

    println(indentation_str + "String {");
    println(indentation_str + "  value: " + value);
    if (body.size() > 0) {
      println(indentation_str + "  body: [");
      for (size_t i = 0; i < body.size(); i++) {
        println(indentation_str + "    " + body[i]->name);
      }
      println(indentation_str + "  ]");
    }
    println(indentation_str + "}");
  }
};

PeekPtr<Value> Value::build(std::vector<Token> stream, size_t index) {
  PeekPtr<Value> result;

  auto equal = peek<Token>(
    stream,
    index,
    [](Token &token) {
      return token.is_given_marker(Marker::EQUAL_SIGN);
    },
    [](Token &token) {
      return std::runtime_error("Expected equal sign, but got " + token.value);
    },
    [](Token &token) {
      return std::runtime_error("Unexpected end of stream");
    }
  );

  auto next = peek<Token>(
    stream,
    equal.index,
    [](Token &token) {
      if (token.is_given_marker(Marker::DOUBLE_QUOTE)) {
        return true;
      }

      return token.kind == Kind::LITERAL || token.kind == Kind::IDENTIFIER;
    },
    [](Token &token) {
      return std::runtime_error("Expected a literal or identifier, but got " + token.value);
    },
    [](Token &token) {
      return std::runtime_error("Unexpected end of stream");
    }
  );

  if (next.node.is_given_marker(Marker::DOUBLE_QUOTE)) {
    PeekPtr<String> str = String::build(stream, next.index);
    result.node.reset(str.node.release());
    result.index = str.index;
  } else {
    result.index = next.index;
    result.node->literal = get_literal(next.node.name);
    result.node->value = next.node.value;
  }

  return result;
}