#pragma once

#include <functional>
#include <map>
#include <vector>

class Lexer {
	public:
		class Token {
			public:
				enum class Type {
					ILLEGAL,
					IDENTIFIER,
					KEYWORD,
					LITERAL,
					MARKER,
					OPERATOR,
				};

				enum class Keyword {
					CONSTANT,
					VARIABLE,
					FUNCTION, RETURN,
					IMPORT, FROM, AS,
					STRUCT, STATIC, PUB,
					ENUM,
					IF, ELSE, MATCH, CASE,
					IN,
					LOOP, CONTINUE, BREAK,
				};

				enum class Literal {
					BOOLEAN,
					FLOAT,
					INTEGER,
					STRING,
				};

				enum class Marker {
					BLOCK_BEGIN, 
					BLOCK_END,
					BRACKET_BEGIN,
					BRACKET_END,
					COMMA,
					COMMENT,
					PARENTHESIS_BEGIN,
					PARENTHESIS_END,
					STR_QUOTE,
				};

				enum class Operator {
					ASSIGNMENT,
					ADDITION,
					ADDITION_ASSIGNMENT,
					SUBTRACTION,
					SUBTRACTION_ASSIGNMENT,
					MULTIPLICATION,
					MULTIPLICATION_ASSIGNMENT,
					DIVISION,
					DIVISION_ASSIGNMENT,
					MODULUS,
					MODULUS_ASSIGNMENT,
					LESS_THAN,
					LESS_THAN_EQUAL,
					GREATER_THAN,
					GREATER_THAN_EQUAL,
					EQUAL,
					NOT_EQUAL,
					AND,
					OR,
					NOT,
					MEMBER_ACCESS,
					STATIC_MEMBER_ACCESS,
				};

			private:
				Type type;
				std::string value;
				std::vector<std::string> injections;

			public:
				Token() = default;
				Token(const std::string &value, const Type &type);
				Token(const std::string &value, const Type &type, const std::vector<std::string> &injections);

				Type get_type() const;
				std::string get_value() const;
				Keyword get_keyword() const;

				bool is_given_marker(Marker marker) const;
				bool is_given_operator(Operator operation) const;

				void print() const;
		};

		class Stream {
			size_t index;
			std::vector<Token> collection;

			public:
				Stream(const std::vector<Token> &collection);

				const Token& current();
				const Token& consume();

				void next();

				bool has_next() const;
				bool is_next(const std::function<bool(const Token &)> &predicate) const;
		};

	private:
		static std::map<Token::Type, std::string> TYPE_NAME_MAPPING;
		static std::map<std::string, Token::Keyword> KEYWORD_MAPPING;
		static std::map<char, Token::Marker> MARKER_MAPPING;
		static std::map<std::string, Token::Operator> OPERATOR_MAPPING;

	static Token consume_buffer(std::string &buffer);
	static std::string consume_str_injection(const std::string &line, int &index);
	static Token build_operator(const std::string &buffer, int &index);
	static Token build_str_literal(const std::string &line, int &index);

	static void skip_single_line_comment(const std::string &line, int &index);

	static inline bool is_boolean_literal(const std::string &buffer);
	static inline bool is_float_literal(const std::string &buffer);
	static inline bool is_integer_literal(const std::string &buffer);
	static inline bool is_identifier(const char &character);
	static inline bool is_identifier(const std::string &buffer);
	static bool is_keyword(const std::string &buffer);
	static bool is_marker(const char &character);
	static bool is_next_char(const std::string &line, int index, std::function<bool(const char &)> predicate);
	static bool is_operator(const std::string &buffer);
	static inline bool is_str_injection(const std::string &line, size_t index);

	static Token::Marker get_marker(const char &character);

	public:
		static std::vector<Token> lex(const std::string &line);
		static Stream lex_file(const std::string &filename);
};
