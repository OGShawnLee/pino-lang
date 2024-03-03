#pragma once

#include <memory>
#include <vector>
#include "Expression.cpp"
#include "Function.cpp"
#include "Lexer.h"
#include "Statement.h"

class Function;

std::map<Literal, BuiltInType> LITERAL_TYPE = {
	{Literal::BOOLEAN, BuiltInType::BOOL},
	{Literal::INTEGER, BuiltInType::INT},
	{Literal::STRING, BuiltInType::STR},
};

std::map<BuiltInType, std::string> TYPE_NAME = {
	{BuiltInType::BOOL, "bool"},
	{BuiltInType::INT, "int"},
	{BuiltInType::STR, "str"},
	{BuiltInType::VOID, "void"},
};

std::string get_type_name(BuiltInType type) {
	return TYPE_NAME.at(type);
}

BuiltInType infer_literal_type(Literal kind) {
	return LITERAL_TYPE.at(kind);
}

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

	auto value = peek<Token>(collection, result.index, [](Token &token) {
		return token.kind == Kind::LITERAL || token.kind == Kind::IDENTIFIER;
	});

	if (value.node.kind == Kind::LITERAL) {
		result.node->type = infer_literal_type(value.node.literal);
		result.node->value = std::move(
			Value::from_literal(value.node)
		);
	} else {
		result.node->type = BuiltInType::VOID;
		result.node->value = std::move(
			Identifier::from_identifier(value.node)
		);
	}

	result.index = value.index;

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
		return token.kind == Kind::BUILT_IN_TYPE;
	});

	result.node->type = type.node.type;
	result.index = type.index;

	return result;
}

void Variable::print(size_t indentation) const {
	std::string indent = get_indentation(indentation);
	println(indent + "Variable {");
	println(indent + "  name: " + name);
	println(indent + "  type: " + get_type_name(type));
	if (value.get() != nullptr) {
		if (value->expression == ExpressionKind::IDENTIFIER) {
			Identifier *identifier = static_cast<Identifier*>(value.get());
			identifier->print(indentation + 2);
		} else if (value->expression == ExpressionKind::LITERAL) {
			Value *literal = static_cast<Value*>(value.get());
			literal->print(indentation + 2);
		}
	}
	println(indent + "}");
}