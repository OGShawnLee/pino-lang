#pragma once

#include <memory>
#include <vector>
#include "Expression.h"

class Field {
	public:
		std::string name;
		std::string type;
		std::unique_ptr<Expression> value;
	
		Field();

		static Peek<Field> build_as_property(std::vector<Token> collection, size_t index);

		static Peek<Field> build(std::vector<Token> collection, size_t index);

		void print(size_t indentation = 0) const;
};