#pragma once

#include "Statement.h"
#include "Expression.h"

class Declaration : public Statement {
  protected:
    std::string identifier;

  public:
    std::string get_identifier() const;
};

class Variable : public Declaration {
  public:
    enum class Kind {
      CONSTANT_DECLARATION,
      VARIABLE_DECLARATION,
      PARAMETER_DECLARATION,
      PROPERTY_DECLARATION,
    }; 

  private:
    Kind kind;
    std::unique_ptr<Expression> value;
    std::string typing;

    static std::map<Kind, std::string> KIND_NAME_MAPPING;

  public:
    Variable(Kind kind, std::string identifier, std::unique_ptr<Expression> value);
    Variable(Kind kind, std::string identifier, std::string typing);

    std::unique_ptr<Expression> extract_value();

    void print(const size_t &indentation) const override;
};

class Function : public Declaration {
  private:
    std::vector<std::unique_ptr<Variable>> parameters;
    std::unique_ptr<Statement> children;

  public:
    Function(
      std::string identifier,
      std::vector<std::unique_ptr<Variable>> parameters,
      std::unique_ptr<Statement> children
    );

    void print(const size_t &indentation) const override;
};

class Struct : public Declaration {
  private:
    std::vector<std::unique_ptr<Variable>> fields;
    std::vector<std::unique_ptr<Function>> methods;

  public:
    Struct(std::string name, std::vector<std::unique_ptr<Variable>> fields, std::vector<std::unique_ptr<Function>> methods);

    void print(const size_t &indentation) const override;
};

class Enum : public Declaration {
  private:
    std::vector<std::string> fields;

  public:
    Enum(std::string name, std::vector<std::string> fields);

    void print(const size_t &indentation) const override;
};