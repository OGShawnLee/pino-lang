#include "Statement.h"
#include "Declaration.h"
#include "Expression.h"

class Parser {
  public:
    static bool is_expression(Lexer::Stream &collection);
    static bool is_function_call(Lexer::Stream &collection);
    static bool is_function_lambda(Lexer::Stream &collection);
    static bool is_struct_instance(Lexer::Stream &collection);
    static bool is_vector(Lexer::Stream &collection);

    static std::vector<std::unique_ptr<Expression>> consume_arguments(Lexer::Stream &collection);
    static std::unique_ptr<Variable> consume_attribute(Lexer::Stream &collection);
    static void consume_attributes_and_methods(
      Lexer::Stream &collection, 
      std::vector<std::unique_ptr<Variable>> &attributes,
      std::vector<std::unique_ptr<Function>> &methods
    );
    static std::unique_ptr<Expression> consume_assignment(Lexer::Stream &collection);
    static std::string consume_enum_member(Lexer::Stream &collection);
    static std::vector<std::string> consume_enum_members(Lexer::Stream &collection);
    static Token::Keyword consume_keyword(Lexer::Stream &collection);
    static std::string consume_identifier(Lexer::Stream &collection);
    static std::unique_ptr<Variable> consume_parameter(Lexer::Stream &collection);
    static std::vector<std::unique_ptr<Variable>> consume_parameters(Lexer::Stream &collection);
    static std::unique_ptr<Variable> consume_property(Lexer::Stream &collection);
    static std::vector<std::unique_ptr<Variable>> consume_properties(Lexer::Stream &collection);
    static std::string consume_typing(Lexer::Stream &collection);

    static std::unique_ptr<Variable> extract_property(std::vector<std::unique_ptr<Variable>> &properties, const std::string &identifier);

    static std::unique_ptr<Statement> parse_block(Lexer::Stream &collection);
    static std::unique_ptr<ElseStatement> parse_else_statement(Lexer::Stream &collection);
    static std::unique_ptr<Enum> parse_enum(Lexer::Stream &collection);
    static std::unique_ptr<Expression> parse_expression(Lexer::Stream &collection);
    static std::unique_ptr<Function> parse_function(Lexer::Stream &collection);
    static std::unique_ptr<FunctionLambda> parse_function_lambda(Lexer::Stream &collection);
    static std::unique_ptr<FunctionCall> parse_function_call(Lexer::Stream &collection);
    static std::unique_ptr<IfStatement> parse_if_statement(Lexer::Stream &collection);
    static std::unique_ptr<TernaryExpression> parse_if_expression(Lexer::Stream &collection);
    static std::unique_ptr<Loop> parse_loop(Lexer::Stream &collection);
    static std::unique_ptr<MatchStatement> parse_match_statement(Lexer::Stream &collection);
    static std::unique_ptr<Variable> parse_variable(Lexer::Stream &collection);
    static std::unique_ptr<Vector> parse_vector(Lexer::Stream &collection);
    static std::unique_ptr<Struct> parse_struct(Lexer::Stream &collection);
    static std::unique_ptr<StructInstance> parse_struct_instance(Lexer::Stream &collection);
    static std::unique_ptr<Return> parse_return(Lexer::Stream &collection);
    static std::unique_ptr<WhenStatement> parse_when_statement(Lexer::Stream &collection);
    static Statement parse_file(const std::string &filename);
};
