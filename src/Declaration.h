#pragma once

#include "Statement.h"

class Declaration : public Statement {
  protected:
    std::string identifier;

  public:
    virtual void consume_keyword(
      const std::vector<Lexer::Token> &collection, size_t &index
    ) = 0;
    void consume_identifier(
      const std::vector<Lexer::Token> &collection, size_t &index
    );
};

class Variable : public Declaration {
  public:
    enum class Kind {
      CONSTANT_DECLARATION,
      VARIABLE_DECLARATION,
      PARAMETER_DECLARATION,
    }; 

  private:
    Kind kind;
    std::string value;
    std::string typing;

    static std::map<Kind, std::string> KIND_NAME_MAPPING;

  public:
    Variable() = default;

    void consume_keyword(
      const std::vector<Lexer::Token> &collection, size_t &index
    );
    void consume_typing(
      const std::vector<Lexer::Token> &collection, size_t &index
    );
    void consume_value(
      const std::vector<Lexer::Token> &collection, size_t &index
    );

    void print(const size_t &indentation) const override;
};

class Function : public Declaration {
  private:
    std::vector<std::unique_ptr<Variable>> parameters;

  public:
    Function() = default;

    void consume_keyword(
      const std::vector<Lexer::Token> &collection, size_t &index
    );
    void consume_parameter(
      const std::vector<Lexer::Token> &collection, size_t &index
    );
    void consume_parameters(
      const std::vector<Lexer::Token> &collection, size_t &index
    );

    void print(const size_t &indentation) const override;
};
