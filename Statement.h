#pragma once

#include <memory>
#include <vector>
#include "Lexer.h"

enum class StatementKind {
	PROGRAM,
	VAL_DECLARATION,
	VAR_DECLARATION,
	FN_DECLARATION,
	IF_STATEMENT,
	ELSE_STATEMENT,
	EXPRESSION,
	LOOP_STATEMENT,
	RETURN_STATEMENT,
};

std::map<StatementKind, std::string> STATEMENT_NAME = {
	{StatementKind::PROGRAM, "Program"},
	{StatementKind::VAL_DECLARATION, "Const Declaration"},
	{StatementKind::VAR_DECLARATION, "Variable Declaration"},
	{StatementKind::FN_DECLARATION, "Function Declaration"},
	{StatementKind::IF_STATEMENT, "If Statement"},
	{StatementKind::ELSE_STATEMENT, "Else Statement"},
	{StatementKind::EXPRESSION, "Expression"},
	{StatementKind::LOOP_STATEMENT, "Loop Statement"},
	{StatementKind::RETURN_STATEMENT, "Return Statement"},
};

class Statement {
	static std::string get_kind_name(StatementKind kind) {
		return STATEMENT_NAME.at(kind);
	}

	public:
		StatementKind kind;
		std::vector<std::unique_ptr<Statement>> children;

		virtual void print(size_t indentation = 0) const {
			std::string indent = get_indentation(indentation);
			println(indent + get_kind_name(kind) + " {");
			
			if (children.size() > 0) {
				for (const std::unique_ptr<Statement> &child : children) {
					child->print(indentation + 2);
				}
			}
			
			println(indent + "}");
		}
};