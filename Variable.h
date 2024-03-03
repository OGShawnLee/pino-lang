#pragma once

#include <memory>
#include <vector>
#include "Expression.h"
#include "Lexer.h"
#include "Statement.h"

class Variable : public Statement {
	public:
		std::string name;
		BuiltInType type;
		std::unique_ptr<Expression> value;

		Variable();

		static PeekPtr<Variable> build(std::vector<Token> collection, size_t index);

		static PeekPtr<Variable> build_as_parameter(std::vector<Token> collection, size_t index);

		void print(size_t indentation = 0) const;
};
