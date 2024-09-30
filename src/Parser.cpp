#include "Parser.h"
#include "Lexer.cpp"
#include "Statement.cpp"
#include "Declaration.cpp"

std::unique_ptr<Function> Parser::parse_function(Lexer::Stream &collection) {
  std::unique_ptr<Function> function = std::make_unique<Function>();

  function->consume_keyword(collection);
  function->consume_identifier(collection);
  function->consume_parameters(collection);

  return function;
}

std::unique_ptr<Variable> Parser::parse_variable(Lexer::Stream &collection) {
  std::unique_ptr<Variable> variable = std::make_unique<Variable>();

  variable->consume_keyword(collection);
  variable->consume_identifier(collection);
  variable->consume_value(collection);

  return variable;
}

Statement Parser::parse_file(const std::string &filename) {
  Lexer::Stream collection = Lexer::lex_file(filename);
  Statement program;

  while (collection.has_next()) {
    const Lexer::Token &token = collection.current();

    switch (token.get_type()) {
      case (Token::Type::KEYWORD): 
        switch (token.get_keyword()) {
          case Lexer::Token::Keyword::CONSTANT:
          case Lexer::Token::Keyword::VARIABLE:
            program.push(std::move(parse_variable(collection)));
            continue;
          case Lexer::Token::Keyword::FUNCTION:
            program.push(std::move(parse_function(collection)));
            continue;
        }
        break;
      case Lexer::Token::Type::ILLEGAL:
        println("Illegal Token");
        token.print();
        break;
    }


    collection.next();
  }

  return program;
}
