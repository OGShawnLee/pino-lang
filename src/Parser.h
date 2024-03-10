#pragma once

#include <memory>
#include "ControlFlow.cpp"
#include "Function.h"
#include "Lexer.h"
#include "Statement.h"
#include "Variable.h"

class Parser {
	public:
		static Statement parse_file(std::string file_name) {
			std::vector<Token> collection = Lexer::lex_file(file_name);
			Statement program;
			program.kind = StatementKind::PROGRAM;

			std::vector<std::unique_ptr<Statement>> children = Block::build_program(collection);
			program.children = std::move(children);

			return program;
		}
};