#pragma once

#include "Block.h"
#include "Function.h"
#include "Lambda.h"

Lambda::Lambda() {
  expression = ExpressionKind::LAMBDA;
}

bool Lambda::is_lambda(std::vector<Token> collection, size_t index) {
  return 
    collection[index].is_given_keyword(Keyword::FN_KEYWORD) &&
    is_next<Token>(collection, index, [](Token &token) {
      return token.is_given_marker(Marker::LEFT_PARENTHESIS);
    });
}

PeekPtr<Lambda> Lambda::build(std::vector<Token> collection, size_t index) {
  if (is_lambda(collection, index) == false) {
    throw std::runtime_error("DEV: Not a Lambda");
  }

  PeekPtr<Lambda> result;
  result.index = index;

  PeekStreamPtr<Variable> parameters = handle_parameters(collection, result.index + 1);
	result.node->parameters = std::move(parameters.nodes);
	result.index = parameters.index;

	PeekStreamPtr<Statement> block = Block::build(collection, result.index);
	result.node->children = std::move(block.nodes);
	result.index = block.index;

  return result;
}

void Lambda::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + "Lambda {");
  if (parameters.size() > 0) {
    println(indent + "  parameters: [");
    for (const std::unique_ptr<Variable> &parameter : parameters) {
      parameter->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  if (children.size() > 0) {
    println(indent + "  children: [");
    for (const std::unique_ptr<Statement> &child : children) {
      child->print(indentation + 4);
    }
    println(indent + "  ]");
  }
  println(indent + "}");
}