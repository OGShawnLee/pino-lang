#include "Parser.h"
#include "Lexer.cpp"
#include "Statement.cpp"
#include "Declaration.cpp"
#include "Expression.cpp"

std::unique_ptr<Enum> Parser::parse_enum(Lexer::Stream &collection) {
  std::unique_ptr<Enum> enumeration = std::make_unique<Enum>();

  enumeration->consume_keyword(collection);
  enumeration->consume_identifier(collection);
  enumeration->consume_values(collection);

  return enumeration;
}

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

std::unique_ptr<Struct> Parser::parse_struct(Lexer::Stream &collection) {
  std::unique_ptr<Struct> structure = std::make_unique<Struct>();

  structure->consume_keyword(collection);
  structure->consume_identifier(collection);
  structure->consume_fields(collection);

  return structure;
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
          case Lexer::Token::Keyword::STRUCT:
            program.push(std::move(parse_struct(collection)));
            continue;
          case Lexer::Token::Keyword::ENUM:
            program.push(std::move(parse_enum(collection)));
            continue;
        }
        break; 
      case Token::Type::IDENTIFIER:
      case Token::Type::LITERAL: {
        program.push(std::move(Expression::build(collection)));
        continue;
      }
      case Lexer::Token::Type::ILLEGAL:
        println("Illegal Token");
        token.print();
        break;
    }


    collection.next();
  }

  return program;
}
