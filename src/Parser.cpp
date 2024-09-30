#include "Parser.h"
#include "Lexer.cpp"
#include "Statement.cpp"
#include "Declaration.cpp"

std::unique_ptr<Function> Parser::parse_function(const std::vector<Lexer::Token> &collection, size_t &index) {
  std::unique_ptr<Function> function = std::make_unique<Function>();

  function->consume_keyword(collection, index);
  function->consume_identifier(collection, index);
  function->consume_parameters(collection, index);

  return function;
}

std::unique_ptr<Variable> Parser::parse_variable(const std::vector<Lexer::Token> &collection, size_t &index) {
  std::unique_ptr<Variable> variable = std::make_unique<Variable>();

  variable->consume_keyword(collection, index);
  variable->consume_identifier(collection, index);
  variable->consume_value(collection, index);

  return variable;
}

Statement Parser::parse_file(const std::string &filename) {
  std::vector<Lexer::Token> collection = Lexer::lex_file(filename);
  Statement program;

  for (size_t i = 0; i < collection.size(); i++) {
    const Lexer::Token &token = collection[i];

    switch (token.get_type()) {
      case Lexer::Token::Type::KEYWORD:
        
        switch (token.get_keyword()) {
          case Lexer::Token::Keyword::CONSTANT:
          case Lexer::Token::Keyword::VARIABLE:
            program.push(
              std::move(parse_variable(collection, i))
            );
            continue;
          case Lexer::Token::Keyword::FUNCTION:
            program.push(
              std::move(parse_function(collection, i))
            );
            continue;
        }

        break;
      case Lexer::Token::Type::ILLEGAL:
        println("Illegal Token");
        token.print();
        break;
    }
  }

  return program;
}
