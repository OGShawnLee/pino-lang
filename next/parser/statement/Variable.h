#pragma once

#include "./Statement.h"
#include "./expression/Expression.h"
#include <memory>
#include <string>

// Forward declaration of Parser class
class Parser;

class Variable : public Statement {
  std::string identifier;
  std::shared_ptr<Expression> value;
  std::string type;
  VARIABLE_KIND variable_kind;

  public:
    Variable(
      std::string identifier, 
      std::shared_ptr<Expression> value, 
      std::string type,
      VARIABLE_KIND variable_kind = VARIABLE_KIND::VARIABLE
    );

    // @WARNING: This constructor is used for testing purposes only.
    Variable(
      std::string identifier, 
      std::string value, 
      std::string type,
      VARIABLE_KIND variable_kind = VARIABLE_KIND::VARIABLE 
    );

    std::string get_identifier() const;

    const std::shared_ptr<Expression>& get_value() const;

    std::string get_type() const;

    VARIABLE_KIND get_variable_kind() const;
    
    bool equals(const std::shared_ptr<Statement> &candidate) const;
};