#pragma once

#include "Statement.h"

class Declaration : public Statement {
  protected:
    std::string identifier;

  public:
    virtual void consume_keyword(Lexer::Stream &collection) = 0;
    void consume_identifier(Lexer::Stream &collection);
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

    void consume_keyword(Lexer::Stream &collection);
    void consume_typing(Lexer::Stream &collection);
    void consume_value(Lexer::Stream &collection);

    void print(const size_t &indentation) const override;
};

class Function : public Declaration {
  private:
    std::vector<std::unique_ptr<Variable>> parameters;

  public:
    Function() = default;

    void consume_keyword(Lexer::Stream &collection);
    void consume_parameter(Lexer::Stream &collection);
    void consume_parameters(Lexer::Stream &collection);

    void print(const size_t &indentation) const override;
};
