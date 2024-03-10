#pragma once

#include <vector>
#include "Lexer.h"
#include "Statement.h"
#include "Variable.h"

class ReturnStatement : public Statement {
	public:
		std::unique_ptr<Expression> value;

		ReturnStatement();

		static PeekPtr<ReturnStatement> build(std::vector<Token> collection, size_t index);
		
		void print(size_t indentation = 0) const;
};

class Function : public Statement {
	static PeekStreamPtr<Variable> handle_parameters(std::vector<Token> collection, size_t index);

	public:
		std::string name;
		std::vector<std::unique_ptr<Variable>> parameters;

		Function();

		static PeekPtr<Function> build(std::vector<Token> collection, size_t index);
		
		void print(size_t indentation = 0) const;
};