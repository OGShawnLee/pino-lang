#pragma once

#include <map>
#include <string>
#include <vector>
#include "types.h"
#include "utils.h"

enum class Kind {
	IDENTIFIER,
	KEYWORD,
	LITERAL,
	MARKER,
	BINARY_OPERATOR,
};

std::map<Kind, std::string> KIND_NAME = {
	{Kind::IDENTIFIER, "Identifier"},
	{Kind::KEYWORD, "Keyword"},
	{Kind::LITERAL, "Literal"},
	{Kind::MARKER, "Marker"},
	{Kind::BINARY_OPERATOR, "Binary Operator"},
};

enum class BinaryOperator {
	PLUS,
	MINUS,
	MULTIPLY,
	DIVIDE,
	MODULUS,
	LESS_THAN,
	LESS_THAN_OR_EQUAL,
	GREATER_THAN,
	GREATER_THAN_OR_EQUAL,
	NOT_EQUAL,
	EQUAL,
	AND,
	OR,
};

std::map<char, BinaryOperator> BINARY_OPERATOR_CHAR = {
	{'+', BinaryOperator::PLUS},
	{'-', BinaryOperator::MINUS},
	{'*', BinaryOperator::MULTIPLY},
	{'/', BinaryOperator::DIVIDE},
	{'%', BinaryOperator::MODULUS},
	{'<', BinaryOperator::LESS_THAN},
	{'>', BinaryOperator::GREATER_THAN},
	{'!', BinaryOperator::NOT_EQUAL},
	{'=', BinaryOperator::EQUAL},
};

std::map<std::string, BinaryOperator> BINARY_OPERATOR_KEY = {
	{"+", BinaryOperator::PLUS},
	{"-", BinaryOperator::MINUS},
	{"*", BinaryOperator::MULTIPLY},
	{"/", BinaryOperator::DIVIDE},
	{"%", BinaryOperator::MODULUS},
	{"<", BinaryOperator::LESS_THAN},
	{"<=", BinaryOperator::LESS_THAN_OR_EQUAL},
	{">", BinaryOperator::GREATER_THAN},
	{">=", BinaryOperator::GREATER_THAN_OR_EQUAL},
	{"!=", BinaryOperator::NOT_EQUAL},
	{"==", BinaryOperator::EQUAL},
	{"and", BinaryOperator::AND},
	{"or", BinaryOperator::OR},
};

std::map<BinaryOperator, std::string> BINARY_OPERATOR_NAME = {
	{BinaryOperator::PLUS, "addition"},
	{BinaryOperator::MINUS, "subtraction"},
	{BinaryOperator::MULTIPLY, "multiplication"},
	{BinaryOperator::DIVIDE, "division"},
	{BinaryOperator::MODULUS, "modulus"},
	{BinaryOperator::LESS_THAN, "less than"},
	{BinaryOperator::GREATER_THAN, "greater than"},
	{BinaryOperator::AND, "and"},
	{BinaryOperator::OR, "or"},
};

enum class Keyword {
	VAR_KEYWORD,
	VAL_KEYWORD,
	FN_KEYWORD,
	IF_KEYWORD,
	ELSE_KEYWORD,
	LOOP_KEYWORD,
	IN_KEYWORD,
	RETURN_KEYWORD,
	STRUCT_KEYWORD,
	DO_KEYWORD,
	YIELD_KEYWORD,
};

std::map<std::string, Keyword> KEYWORD_KEY = {
	{"var", Keyword::VAR_KEYWORD},
	{"val", Keyword::VAL_KEYWORD},
	{"fn", Keyword::FN_KEYWORD},
	{"if", Keyword::IF_KEYWORD},
	{"else", Keyword::ELSE_KEYWORD},
	{"for", Keyword::LOOP_KEYWORD},
	{"in", Keyword::IN_KEYWORD},
	{"return", Keyword::RETURN_KEYWORD},
	{"struct", Keyword::STRUCT_KEYWORD},
	{"do", Keyword::DO_KEYWORD},
	{"yield", Keyword::YIELD_KEYWORD},
};

enum class Literal {
	BOOLEAN,
	INTEGER,
	FLOAT,
	STRING,
	VECTOR,
	STRUCT,
};

std::string infer_typing(Literal literal) {
	if (literal == Literal::STRING) {
		return "str";
	} else if (literal == Literal::INTEGER) {
		return "int";
	} else if (literal == Literal::FLOAT) {
		return "float";
	} else if (literal == Literal::BOOLEAN) {
		return "bool";
	} else {
		return "void";
	}
}

enum class Marker {
	COLON,
	COMMA,
	COMMENT,
	DOUBLE_QUOTE,
	EQUAL_SIGN,
	LEFT_BRACE,
	LEFT_BRACKET,
	LEFT_PARENTHESIS,
	RIGHT_BRACE,
	RIGHT_BRACKET,
	RIGHT_PARENTHESIS,
	PIPE,
};

std::map<char, Marker> MARKER_KEY = {
	{':', Marker::COLON},
	{',', Marker::COMMA},
	{'"', Marker::DOUBLE_QUOTE},
	{'=', Marker::EQUAL_SIGN},
	{'{', Marker::LEFT_BRACE},
	{'[', Marker::LEFT_BRACKET},
	{'(', Marker::LEFT_PARENTHESIS},
	{'}', Marker::RIGHT_BRACE},
	{']', Marker::RIGHT_BRACKET},
	{')', Marker::RIGHT_PARENTHESIS},
	{'#', Marker::COMMENT},
	{'|', Marker::PIPE},
};

class Token {
	public:
		Kind kind;
		Keyword keyword;
		Literal literal;
		Marker marker;
		BinaryOperator binary_operator;	
		std::string value;
		std::vector<std::string> injections;
		std::vector<Token> children;

		bool is_given_keyword(Keyword keyword) const {
			return kind == Kind::KEYWORD && this->keyword == keyword;
		}

		bool is_given_kind(Kind kind_a, Kind kind_b) const {
			return kind == kind_a || kind == kind_b;
		}

		bool is_given_literal(Literal literal) const {
			return kind == Kind::LITERAL && this->literal == literal;
		}

		bool is_given_marker(Marker marker) const {
			return kind == Kind::MARKER && this->marker == marker;
		}

		bool is_given_marker(Marker marker_a, Marker marker_b) const {
			return is_given_marker(marker_a) || is_given_marker(marker_b);
		}

		void print(size_t indentation = 0) const {
			std::string indent = get_indentation(indentation);

			println(indent + get_kind_name(kind) + " {");
			println(indent + "  value: " + value);
			
			if (injections.empty() == false) {
				println(indent + "  injections: [");
				for (std::string injection : injections) {
					println(indent + "    " + injection);
				}
				println(indent + "  ]");
			}

			if (children.empty() == false) {
				println(indent + "  children: [");
				for (Token child : children) {
					child.print(indentation + 4);
				}
				println(indent + "  ]");
			}

			println(indent + "}");
		}

		static Token as_binary_operator(BinaryOperator binary_operator, std::string buffer) {
			Token token;
			token.kind = Kind::BINARY_OPERATOR;
			token.binary_operator = binary_operator;
			token.value = buffer;
			return token;
		}

		static Token as_binary_operator(BinaryOperator binary_operator, char character) {
			Token token;
			token.kind = Kind::BINARY_OPERATOR;
			token.binary_operator = binary_operator;
			token.value = character;
			return token;
		}

		static Token as_identifier(std::string buffer) {
			Token token;
			token.kind = Kind::IDENTIFIER;
			token.value = buffer;
			return token;
		}

		static Token as_keyword(Keyword keyword, std::string buffer) {
			Token token;
			token.kind = Kind::KEYWORD;
			token.keyword = keyword;
			token.value = buffer;
			return token;
		}

		static Token as_literal(Literal literal, std::string buffer) {
			Token token;
			token.kind = Kind::LITERAL;
			token.literal = literal;
			token.value = buffer;
			return token;
		}

		static Token as_marker(Marker marker, char character) {
			Token token;
			token.kind = Kind::MARKER;
			token.marker = marker;
			token.value = character;
			return token;
		}

		static bool is_binary_operator(char character) {
			return BINARY_OPERATOR_CHAR.count(character) > 0;
		}

		static bool is_binary_operator(std::string buffer) {
			return BINARY_OPERATOR_KEY.count(buffer) > 0;
		}

		static bool is_bool_literal(std::string buffer) {
			return buffer == "true" || buffer == "false";
		}
	
		static bool is_int_literal(std::string buffer) {
			for (char character : buffer) {
				if (isdigit(character) == false) return false; 
			}
		
			return true;
		}

		static bool is_float_literal(std::string buffer) {
			bool has_decimal = false;

			for (char character : buffer) {
				if (character == '.') {
					if (has_decimal) {
						throw std::runtime_error(
							"USER: Invalid Float Literal: " + buffer + " has multiple decimal points."
						);
					}

					has_decimal = true;
					continue;
				}

				if (isdigit(character)) continue;

				return false;
			}

			return true;
		}
	
		static bool is_keyword(std::string buffer) {
			return KEYWORD_KEY.count(buffer) > 0;
		}
		
		static bool is_marker(char character) {
			return MARKER_KEY.count(character) > 0;
		}

		static BinaryOperator get_binary_operator(char character) {
			return BINARY_OPERATOR_CHAR.at(character);
		}

		static BinaryOperator get_binary_operator(std::string buffer) {
			if (is_binary_operator(buffer) == false) {
				throw std::runtime_error("DEV: Not a Binary Operator");
			}

			return BINARY_OPERATOR_KEY.at(buffer);
		}

		static std::string get_binary_operator_name(BinaryOperator binary_operator) {
			return BINARY_OPERATOR_NAME.at(binary_operator);
		}
	
		static Marker get_marker(char character) {
			if (is_marker(character)) {
				return MARKER_KEY.at(character);
			}
	
			throw std::runtime_error("DEV: Not a Marker"); 
		}
	
		static Keyword get_keyword(std::string buffer) {
			if (is_keyword(buffer)) {
				return KEYWORD_KEY.at(buffer);
			}
	
			throw std::runtime_error("DEV: Not a Keyword");
		}

		static std::string get_kind_name(Kind kind) {
			return KIND_NAME.at(kind);
		}
};

class Lexer {
	static std::string get_str_injection(std::string line, size_t index) {
		if (is_str_injection(line, index) == false) {
			throw std::runtime_error("DEV: Not a String Injection");
		}
		
		std::string buffer;
		
		for (size_t i = index + 1; i < line.length(); i++) {
			char character = line[i];

			if (is_valid_identifier_char(character) == false) {
				bool is_prop_access = character == ':';

				if (is_prop_access == false) return buffer;

				bool is_valid_char = is_next_char(line, i, [](char character) {
					return is_valid_identifier_char(character);
				});

				if (is_valid_char) {
					buffer += character;
					continue;
				}

				return buffer;
			} 

			buffer += character;
		}

		throw std::runtime_error("DEV: Not a String Injection");
	}

	static Peek<Token> get_arr_literal(std::string line, size_t index) {
		if (line[index] != '[') {
			throw std::runtime_error("DEV: Not an Array Literal");
		}

		bool is_empty = is_next_char(line, index, [](char character) {
			return character == ']';
		});

		Peek<Token> next;
		
		if (is_empty) {
			next.node.value = "[]";
			next.index = index + 1;
		} else {
			size_t end = index_of(line, ']', index);

			if (end == -1) {
				throw std::runtime_error("USER: Unterminated Array Literal");
			}

			next.node.value = line.substr(index, end - index + 1);
			next.node.children = lex_line(line.substr(index + 1, end - index - 1));
			next.index = end;
		}

		next.node.kind = Kind::LITERAL;
		next.node.literal = Literal::VECTOR;

		return next;
	}

	static Peek<Token> get_str_literal(std::string line, size_t index) {
		if (line[index] != '"') {
			throw std::runtime_error("DEV: Not a String Literal");
		}

		Peek<Token> next;
		std::string buffer;

		for (size_t i = index + 1; i < line.length(); i++) {
			char character = line[i];

			if (is_str_injection(line, i)) {
				std::string injection = get_str_injection(line, i);
				next.node.injections.push_back(injection);
			}
			
			if (character == '"') {
				next.node.value = buffer;
				next.node.literal = Literal::STRING;
				next.node.kind = Kind::LITERAL;
				next.index = i;
				return next;
			}

			buffer += character;
		}

		throw std::runtime_error("USER: Unterminated String Literal");
	}

	static Token handle_buffer(std::string buffer) {
		if (Token::is_keyword(buffer)) {
			return Token::as_keyword(Token::get_keyword(buffer), buffer);
		} 
		
		if (Token::is_bool_literal(buffer)) {
			return Token::as_literal(Literal::BOOLEAN, buffer);
		} 
		
		if (Token::is_int_literal(buffer)) {
			return Token::as_literal(Literal::INTEGER, buffer);
		} 

		if (Token::is_float_literal(buffer)) {
			return Token::as_literal(Literal::FLOAT, buffer);
		}

		if (Token::is_binary_operator(buffer)) {
			return Token::as_binary_operator(Token::get_binary_operator(buffer), buffer);
		}

		return Token::as_identifier(buffer);
	}

	static bool is_valid_identifier_char(char character) {
		return isalnum(character) || character == '_' || character == '$';
	}

	static bool is_str_injection(std::string line, size_t index) {
		if (line[index] != '$') {
			return false;
		}

		return is_next_char(line, index, [](char character) {
			return isdigit(character) == false && isalpha(character);
		});
	} 

	public:
		static std::vector<Token> lex_line(std::string line, size_t end_i = 0) {
			std::vector<Token> stream;
			std::string buffer;

			line += " ";

			if (end_i == 0) end_i = line.length();

			for (size_t i = 0; i < end_i; i++) {
				char character = line[i];

				if (Token::is_binary_operator(character)) {
					if (is_whitespace(buffer) == false) {
						stream.push_back(handle_buffer(buffer));
						buffer = "";
					}

					bool is_dual_char_operator = is_next_char(line, i, [](char character) {
						return Token::is_binary_operator(character);
					});

					if (is_dual_char_operator) {
						std::string dual_char_operator = std::string(1, character) + line[i + 1];
						BinaryOperator binary_operator = Token::get_binary_operator(dual_char_operator);
						stream.push_back(Token::as_binary_operator(binary_operator, dual_char_operator));
						i++;
						continue;
					}

					if (Token::is_marker(character) == false) {
						BinaryOperator binary_operator = Token::get_binary_operator(character);
						stream.push_back(Token::as_binary_operator(binary_operator, character));
						continue;
					}
				}

				if (is_whitespace(character)) {
					if (is_whitespace(buffer)) continue;

					stream.push_back(handle_buffer(buffer));
					buffer = "";
					
					continue;
				}

				if (Token::is_marker(character)) {
					Marker marker = Token::get_marker(character);

					if (is_whitespace(buffer) == false) {
						stream.push_back(handle_buffer(buffer));
						buffer = "";
					}
					
					switch (marker) {
						case Marker::LEFT_BRACKET: {
							Peek<Token> arr = get_arr_literal(line, i);
							stream.push_back(arr.node);
							i = arr.index;
						} break;
						case Marker::DOUBLE_QUOTE: {	
							Peek<Token> str = get_str_literal(line, i);
							stream.push_back(str.node);
							i = str.index;
						} break;
						case Marker::COMMENT: {
							return stream;
						} break;
						default:
							stream.push_back(Token::as_marker(marker, character));
							break;
					}

					continue;
				}

				buffer += character;
			}

			return stream;
		}

		static std::vector<Token> lex_file(std::string file_name) {
			std::vector<Token> stream;

			each_line(file_name, [&stream](std::string line) {
				std::vector<Token> tokens = lex_line(line);
				stream.insert(stream.end(), tokens.begin(), tokens.end());
			});

			return stream;
		}
};