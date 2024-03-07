#pragma once

#include <memory>
#include <vector>
#include "Field.h"
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
	STRUCT,
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
	{StatementKind::STRUCT, "Struct Definition"},
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

class StructDefinition : public Statement {
	public:
		std::string name;
		std::vector<Field> fields;

		StructDefinition() {
			kind = StatementKind::STRUCT;
		}

		static PeekPtr<StructDefinition> build(std::vector<Token> collection, size_t index) {
			if (collection[index].is_given_keyword(Keyword::STRUCT_KEYWORD) == false) {
				throw std::runtime_error("DEV: Not a StructDefinition Definition");
			}

			PeekPtr<StructDefinition> result;
			result.index = index;
			
			auto name = peek<Token>(collection, result.index, [](Token &token) {
				return token.kind == Kind::IDENTIFIER;
			});

			result.node->name = name.node.value;
			result.index = name.index;

			auto left_brace = peek<Token>(collection, result.index, [](Token &token) {
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

				Peek<Field> field = Field::build(collection, result.index);
				result.node->fields.push_back(field.node); 
				result.index = field.index;
			}

			throw std::runtime_error("DEV: Unexpected End of StructDefinition Definition");
		}

		void print(size_t indentation = 0) const {
			std::string indent = get_indentation(indentation);
			println(indent + "Struct Definition " + name + " {");
			
			if (fields.size() > 0) {
				for (const Field &field : fields) {
					field.print(indentation + 2);
				}
			}
			
			println(indent + "}");
		}
};