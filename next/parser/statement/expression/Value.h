#include "./Expression.h"
#include "../../../lexer/token/Literal.h"

class Value : public Expression {
  LITERAL_TYPE literal_type;
  std::string typing;
  std::string value;

  public:
    Value(Literal literal) : Expression(EXPRESSION_TYPE::LITERAL) {
      this->literal_type = literal.get_literal_type();
      this->value = literal.get_data();
      this->typing = literal.get_name();
    }

    LITERAL_TYPE get_literal_type() const {
      return this->literal_type;
    }

    std::string get_typing() const {
      return this->typing;
    }

    std::string get_value() const {
      return this->value;
    }


    bool equals(const std::shared_ptr<Statement> &candidate) const override {
      if (candidate->get_type() != STATEMENT_TYPE::EXPRESSION) {
        return false;
      }

      const Expression& expression = static_cast<const Expression&>(*candidate);
      if (expression.get_expression_type() != EXPRESSION_TYPE::LITERAL) {
        return false;
      }

      return this->literal_type == static_cast<const Value&>(expression).get_literal_type();
    }
};