#pragma once

#include <memory>
#include <string>
#include "Declaration.h"
#include "Expression/Expression.h"

class Variable : public Declaration {
  std::shared_ptr<Expression> value;
  std::string type;
  VARIABLE_KIND variable_kind;

  public:
    Variable(
      const std::string &identifier, 
      std::shared_ptr<Expression> value, 
      std::string type,
      VARIABLE_KIND variable_kind = VARIABLE_KIND::VARIABLE
    );
    
    Variable(
      const std::string &identifier, 
      std::string type,
      VARIABLE_KIND variable_kind = VARIABLE_KIND::PARAMETER
    );

    // @WARNING: This constructor is used for testing purposes only.
    Variable(
      const std::string &identifier, 
      std::string value, 
      std::string type,
      VARIABLE_KIND variable_kind = VARIABLE_KIND::VARIABLE 
    );

    const std::shared_ptr<Expression>& get_value() const;

    std::string get_type() const;

    VARIABLE_KIND get_variable_kind() const;
    
    bool equals(const std::shared_ptr<Statement> &candidate) const;
};