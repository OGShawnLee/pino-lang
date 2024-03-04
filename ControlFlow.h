#pragma once

#include <memory>
#include "Expression.h"
#include "Statement.h"

class ELSEStatement : public Statement {
	public:
		ELSEStatement();

		static PeekPtr<ELSEStatement> build(std::vector<Token> collection, size_t index);
	
		void print(size_t indentation = 0) const;
};

class IFStatement : public Statement {
	public:
		std::unique_ptr<Expression> condition;
		std::unique_ptr<ELSEStatement> else_block;

		IFStatement();

		static PeekPtr<IFStatement> build(std::vector<Token> collection, size_t index);
	
		void print(size_t indentation = 0) const;
};
