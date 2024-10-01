#include "Statement.h"
#include "Declaration.h"
#include "Expression.h"

class Parser {
  public:
    static bool is_binary_expression(Lexer::Stream &collection);
    static bool is_expression(Lexer::Stream &collection);
    static bool is_function_call(Lexer::Stream &collection);

    static std::unique_ptr<Variable> consume_attribute(Lexer::Stream &collection);
    static std::vector<std::unique_ptr<Variable>> consume_attributes(Lexer::Stream &collection);
    static std::unique_ptr<Expression> consume_assignment(Lexer::Stream &collection);
    static std::string consume_enum_member(Lexer::Stream &collection);
    static std::vector<std::string> consume_enum_members(Lexer::Stream &collection);
    static Token::Keyword consume_keyword(Lexer::Stream &collection);
    static std::string consume_identifier(Lexer::Stream &collection);
    static std::unique_ptr<Variable> consume_parameter(Lexer::Stream &collection);
    static std::vector<std::unique_ptr<Variable>> consume_parameters(Lexer::Stream &collection);
    static std::string consume_typing(Lexer::Stream &collection);

    static std::unique_ptr<Enum> parse_enum(Lexer::Stream &collection);
    static std::unique_ptr<Expression> parse_expression(Lexer::Stream &collection);
    static std::unique_ptr<Function> parse_function(Lexer::Stream &collection);
    static std::unique_ptr<Variable> parse_variable(Lexer::Stream &collection);
    static std::unique_ptr<Struct> parse_struct(Lexer::Stream &collection);
    static Statement parse_file(const std::string &filename);
};
