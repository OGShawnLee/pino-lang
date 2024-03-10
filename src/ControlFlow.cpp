#pragma once

#include <memory>
#include "Block.cpp"
#include "ControlFlow.h"
#include "Expression.cpp"
#include "Statement.h"

ELSEStatement::ELSEStatement() {
  kind = StatementKind::ELSE_STATEMENT;
}

PeekPtr<ELSEStatement> ELSEStatement::build(std::vector<Token> collection, size_t index) {
  Keyword keyword = collection[index].keyword;
  if (keyword != Keyword::ELSE_KEYWORD) {
    throw std::runtime_error("DEV: Not an Else Statement");
  }

  PeekPtr<ELSEStatement> result;
  result.index = index;

  PeekStreamPtr<Statement> block = Block::build(collection, result.index);
  result.node->children = std::move(block.nodes);
  result.index = block.index;

  return result;
}

void ELSEStatement::print(size_t indentation) const {
  std::string indent = get_indentation(indentation);
  println(indent + "Else Statement {");
  if (children.size() > 0) {
    for (const std::unique_ptr<Statement> &child : children) {
      child->print(indentation + 2);
    }
  }
  println(indent + "}");
}

IFStatement::IFStatement() {
  kind = StatementKind::IF_STATEMENT;
}

PeekPtr<IFStatement> IFStatement::build(std::vector<Token> collection, size_t index) {
	Keyword keyword = collection[index].keyword;
	if (keyword != Keyword::IF_KEYWORD) {
		throw std::runtime_error("DEV: Not an If Statement");
	}

	PeekPtr<IFStatement> result;
	result.index = index;

  PeekPtr<Expression> condition = Expression::build(collection, result.index + 1);
  result.node->condition = std::move(condition.node);
  result.index = condition.index;

	// auto condition = peek<Token>(collection, result.index, [](Token &token) {
	// 	return token.kind == Kind::IDENTIFIER || token.is_given_literal(Literal::BOOLEAN);
	// });

	// result.node->condition = condition.node.value;
	// result.index = condition.index;

	PeekStreamPtr<Statement> block = Block::build(collection, result.index);
	result.node->children = std::move(block.nodes);
	result.index = block.index;

  bool has_else = is_next<Token>(collection, result.index, [](Token &token) {
    return token.keyword == Keyword::ELSE_KEYWORD;
  });

  if (has_else) {
    PeekPtr<ELSEStatement> else_block = ELSEStatement::build(collection, result.index + 1);
    result.node->else_block = std::move(else_block.node);
    result.index = else_block.index;
  }

	return result;
}

void IFStatement::print(size_t indentation) const {
	std::string indent = get_indentation(indentation);
	println(indent + "If Statement {");
	condition->print(indentation + 2);

	if (children.size() > 0) {
		for (const std::unique_ptr<Statement> &child : children) {
			child->print(indentation + 2);
		}
	}

	println(indent + "}");

  if (else_block != nullptr) {
    else_block->print(indentation);
  }
}