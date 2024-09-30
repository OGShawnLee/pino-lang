#pragma once

#include "Statement.h"
#include "Expression.h"

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
    std::unique_ptr<Expression> value;
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

class Struct : public Declaration {
  private:
    std::vector<std::unique_ptr<Variable>> fields;

  public:
    Struct() = default;

    void consume_keyword(Lexer::Stream &collection);
    void consume_field(Lexer::Stream &collection);
    void consume_fields(Lexer::Stream &collection);

    void print(const size_t &indentation) const override;
};

class Enum : public Declaration {
  private:
    std::vector<std::string> fields;

  public:
    Enum() = default;

    void consume_keyword(Lexer::Stream &collection);
    void consume_value(Lexer::Stream &collection);
    void consume_values(Lexer::Stream &collection);

    void print(const size_t &indentation) const override;
};