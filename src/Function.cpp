#pragma once

#include <vector>
#include "Function.h"
#include "Parser.h"
#include "Statement.h"
#include "Variable.cpp"
#include "types.h"
#include "utils.h"

class Variable;

ReturnStatement::ReturnStatement() {
	kind = StatementKind::RETURN_STATEMENT;
}

PeekPtr<ReturnStatement> ReturnStatement::build(std::vector<Token> collection, size_t index) {
	Keyword keyword = collection[index].keyword;
	if (keyword != Keyword::RETURN_KEYWORD) {
		throw std::runtime_error("DEV: Not a Return Statement");
	}

	PeekPtr<ReturnStatement> result;
	result.index = index;

	if (Expression::is_expression(collection, index + 1)) {
		PeekPtr<Expression> expression = Expression::build(collection, index + 1);
		result.node->value = std::move(expression.node);
		result.index = expression.index;
	}

	return result;
}

void ReturnStatement::print(size_t indentation) const {
	std::string indent = get_indentation(indentation);
	println(indent + "ReturnStatement {");

	if (value != nullptr) {
		println(indent + "  value: ");
		value->print(indentation + 4);
	}

	println(indent + "}");
}

Function::Function() {
  kind = StatementKind::FN_DECLARATION;
}

PeekPtr<Function> Function::build(std::vector<Token> collection, size_t index) {	
	Keyword keyword = collection[index].keyword;
	if (keyword != Keyword::FN_KEYWORD) {
		throw std::runtime_error("DEV: Not a Function Definition");
	}

	PeekPtr<Function> result;
	result.index = index;

	auto name = peek<Token>(collection, result.index, [](Token &token) {
		return token.kind == Kind::IDENTIFIER;
	});

	result.node->name = name.node.value;
	result.index = name.index;

	PeekStreamPtr<Variable> parameters = handle_parameters(collection, result.index + 1);
	result.node->parameters = std::move(parameters.nodes);
	result.index = parameters.index;

	PeekStreamPtr<Statement> block = Block::build(collection, result.index);
	result.node->children = std::move(block.nodes);
	result.index = block.index;

	return result;
}

PeekStreamPtr<Variable> Function::handle_parameters(std::vector<Token> collection, size_t index) {
	if (collection[index].is_given_marker(Marker::LEFT_PARENTHESIS) == false) {
		throw std::runtime_error("DEV: Not a Function Definition -> Expecting Left Parenthesis");
	}

	PeekStreamPtr<Variable> result;
	result.index = index;

	while (true) {
		auto id_or_right_paren = peek<Token>(collection, result.index, [](Token &token) {
			return token.kind == Kind::IDENTIFIER || token.is_given_marker(Marker::RIGHT_PARENTHESIS);
		});

		result.index = id_or_right_paren.index;

		if (id_or_right_paren.node.is_given_marker(Marker::RIGHT_PARENTHESIS)) {
			return result;
		} 

		PeekPtr<Variable> parameter = Variable::build_as_parameter(collection, id_or_right_paren.index - 1);
		result.nodes.push_back(std::move(parameter.node));
		result.index = parameter.index;

		auto comma_or_right_paren = peek<Token>(collection, result.index, [](Token &token) {
			return token.is_given_marker(Marker::COMMA, Marker::RIGHT_PARENTHESIS);
		});

		result.index = comma_or_right_paren.index;

		if (comma_or_right_paren.node.is_given_marker(Marker::RIGHT_PARENTHESIS)) {
			return result;
		} 
	}

	throw std::runtime_error("DEV: Unterminated Function Declaration Parameters");
}

void Function::print(size_t indentation) const {
	std::string indent = get_indentation(indentation);
	println(indent + "Function {");
	println(indent + "  name: " + name);
	
	if (parameters.size() > 0) {
		println(indent + "  parameters: [");
		for (const std::unique_ptr<Variable> &parameter : parameters) {
			parameter->print(indentation + 4);
		}
		println(indent + "  ]");
	}

	if (children.size() > 0) {
		println(indent + "  body: [");
		for (const std::unique_ptr<Statement> &child : children) {
			child->print(indentation + 4);
		}
		println(indent + "  ]");
	}

	println(indent + "}");
}