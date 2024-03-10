#pragma once

#include <memory>
#include <vector>
#include "Expression.cpp"
#include "Function.cpp"
#include "Lexer.h"
#include "Statement.h"

class Function;

Variable::Variable() {
  kind = StatementKind::VAR_DECLARATION;
}

PeekPtr<Variable> Variable::build(std::vector<Token> collection, size_t index) {
	PeekPtr<Variable> result;

	Keyword keyword = collection[index].keyword;
	if (keyword == Keyword::VAL_KEYWORD) {
		result.node->kind = StatementKind::VAL_DECLARATION;
	} else if (keyword == Keyword::VAR_KEYWORD)  {
		result.node->kind = StatementKind::VAR_DECLARATION;
	} else {
		throw std::runtime_error("DEV: Unexpected Keyword");
	}

	result.index = index;

	auto name = peek<Token>(collection, result.index, [](Token &token) {
		return token.kind == Kind::IDENTIFIER;
	});

	result.node->name = name.node.value;
	result.index = name.index;

	auto marker = peek<Token>(collection, result.index, [](Token &token) {
		return token.kind == Kind::MARKER && token.marker == Marker::EQUAL_SIGN;
	});

	result.index = marker.index;

	PeekPtr<Expression> expression = Expression::build(collection, result.index + 1);

	if (expression.node->expression == ExpressionKind::LITERAL) {
		Value *literal = static_cast<Value*>(expression.node.get());
		result.node->typing = infer_typing(literal->literal);
	} else {
		result.node->typing = "void";
	}

	result.node->value = std::move(expression.node);
	result.index = expression.index;

	return result;
}

PeekPtr<Variable> Variable::build_as_parameter(std::vector<Token> collection, size_t index) {
	PeekPtr<Variable> result;

	auto name = peek<Token>(collection, index, [](Token &token) {
		return token.kind == Kind::IDENTIFIER;
	});

	result.node->name = name.node.value;
	result.index = name.index;

	auto type = peek<Token>(collection, result.index, [](Token &token) {
		return token.kind == Kind::IDENTIFIER;
	});

	result.node->typing = type.node.value;
	result.index = type.index;

	return result;
}

void Variable::print(size_t indentation) const {
	std::string indent = get_indentation(indentation);
	println(indent + "Variable {");
	println(indent + "  name: " + name);
	println(indent + "  typing: " + typing);
	if (value.get() != nullptr) {
		value->print(indentation + 2);
	}
	println(indent + "}");
}