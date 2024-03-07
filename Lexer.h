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
	BUILT_IN_TYPE,
	BINARY_OPERATOR,
};

std::map<Kind, std::string> KIND_NAME = {
	{Kind::IDENTIFIER, "Identifier"},
	{Kind::KEYWORD, "Keyword"},
	{Kind::LITERAL, "Literal"},
	{Kind::MARKER, "Marker"},
	{Kind::BUILT_IN_TYPE, "Type"},
	{Kind::BINARY_OPERATOR, "Binary Operator"},
};

enum class BuiltInType {
	BOOL,
	INT,
	STR,
	VOID,
};

std::map<std::string, BuiltInType> BUILT_IN_TYPE_KEY = {
	{"bool", BuiltInType::BOOL},
	{"int", BuiltInType::INT},
	{"str", BuiltInType::STR},
	{"void", BuiltInType::VOID},
};

enum class BinaryOperator {
	PLUS,
	MINUS,
	MULTIPLY,
	DIVIDE,
	MODULUS,
	LESS_THAN,
	GREATER_THAN,
	NOT_EQUAL,
	AND,
	OR,
};

std::map<std::string, BinaryOperator> BINARY_OPERATOR_KEY = {
	{"+", BinaryOperator::PLUS},
	{"-", BinaryOperator::MINUS},
	{"*", BinaryOperator::MULTIPLY},
	{"/", BinaryOperator::DIVIDE},
	{"%", BinaryOperator::MODULUS},
	{"<", BinaryOperator::LESS_THAN},
	{">", BinaryOperator::GREATER_THAN},
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
};

enum class Literal {
	BOOLEAN,
	INTEGER,
	STRING,
	VECTOR,
};

enum class Marker {
	COLON,
	COMMA,
	DOUBLE_QUOTE,
	EQUAL_SIGN,
	LEFT_BRACE,
	LEFT_BRACKET,
	LEFT_PARENTHESIS,
	RIGHT_BRACE,
	RIGHT_BRACKET,
	RIGHT_PARENTHESIS,
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
};

class Token {
	public:
		Kind kind;
		Keyword keyword;
		Literal literal;
		Marker marker;
		BinaryOperator binary_operator;	
		BuiltInType type;
		std::string value;
		std::vector<std::string> injections;

		Token(BinaryOperator binary_operator, std::string buffer) {
			this->kind = Kind::BINARY_OPERATOR;
			this->binary_operator = binary_operator;
			this->value = buffer;
		}

		Token(BuiltInType type, std::string buffer) {
			this->kind = Kind::BUILT_IN_TYPE;
			this->type = type;
			this->value = buffer;
		}
		
		Token(Keyword keyword, std::string buffer) {
			this->kind = Kind::KEYWORD;
			this->value = buffer;
			this->keyword = keyword;
		}

		Token(Literal literal, std::string buffer) {
			this->kind = Kind::LITERAL;
			this->literal = literal;
			this->value = buffer;
		}

		Token(Marker marker, char character) {
			this->kind = Kind::MARKER;
			this->marker = marker;
			this->value = std::string(1, character);
		}

		Token(std::string buffer) {
			this->kind = Kind::IDENTIFIER;
			this->value = buffer;
		}

		Token() {}

		bool is_given_keyword(Keyword keyword) const {
			return kind == Kind::KEYWORD && this->keyword == keyword;
		}

		bool is_given_kind(Kind kind_a, Kind kind_b) const {
			return kind == kind_a || kind == kind_b;
		}

		bool is_given_marker(Marker marker) const {
			return kind == Kind::MARKER && this->marker == marker;
		}

		bool is_given_marker(Marker marker_a, Marker marker_b) const {
			return is_given_marker(marker_a) || is_given_marker(marker_b);
		}

		bool is_given_literal(Literal literal) const {
			return kind == Kind::LITERAL && this->literal == literal;
		}

		void print() const {
			println(get_kind_name(kind) + " {");
			println("  value: " + value);

			if (injections.empty() == false) {
				println("  injections: [");
				for (std::string injection : injections) {
					println("    " + injection);
				}
				println("  ]");
			} 
			
			println("}");
		}

		static bool is_binary_operator(std::string buffer) {
			return BINARY_OPERATOR_KEY.count(buffer) > 0;
		}

		static bool is_built_in_type(std::string buffer) {
			return BUILT_IN_TYPE_KEY.count(buffer) > 0;
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
	
		static bool is_keyword(std::string buffer) {
			return KEYWORD_KEY.count(buffer) > 0;
		}
		
		static bool is_marker(char character) {
			return MARKER_KEY.count(character) > 0;
		}

		static BinaryOperator get_binary_operator(std::string buffer) {
			return BINARY_OPERATOR_KEY.at(buffer);
		}

		static std::string get_binary_operator_name(BinaryOperator binary_operator) {
			return BINARY_OPERATOR_NAME.at(binary_operator);
		}

		static BuiltInType get_built_in_type(std::string buffer) {
			return BUILT_IN_TYPE_KEY.at(buffer);
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

		auto marker = peek(line, index, [](char character) {
			return character == ']';
		});

		Peek<Token> next;
		next.node.value = "[]";
		next.node.kind = Kind::LITERAL;
		next.node.literal = Literal::VECTOR;
		next.index = marker.index;

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
			return Token(Token::get_keyword(buffer), buffer);
		} 
		
		if (Token::is_bool_literal(buffer)) {
			return Token(Literal::BOOLEAN, buffer);
		} 
		
		if (Token::is_int_literal(buffer)) {
			return Token(Literal::INTEGER, buffer);
		} 

		if (Token::is_binary_operator(buffer)) {
			return Token(Token::get_binary_operator(buffer), buffer);
		}

		if (Token::is_built_in_type(buffer)) {
			return Token(Token::get_built_in_type(buffer), buffer);
		}

		return Token(buffer);
	}

	static bool is_valid_identifier_char(char character) {
		return isalnum(character) || character == '_' || character == '$';
	}

	static bool is_str_injection(std::string line, size_t index) {
		if (line[index] != '$') {
			return false;
		}
		
		if (index + 1 >= line.length() || isdigit(line[index + 1])) {
			return false;
		}

		return true;
	} 

	public:
		static std::vector<Token> lex_line(std::string line) {
			std::vector<Token> stream;
			std::string buffer;

			line += " ";
			
			for (size_t i = 0; i < line.length(); i++) {
				char character = line[i];

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
						default:
							stream.push_back(Token(marker, character));
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