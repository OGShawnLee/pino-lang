#include "Declaration.h"
#include "Variable.h"

class Function : public Declaration {
  std::vector<std::shared_ptr<Variable>> parameters;

  public:
    Function(
      const std::string &identifier,
      const std::vector<std::shared_ptr<Variable>> &parameters,
      const std::vector<std::shared_ptr<Statement>> &children
    );

    const std::vector<std::shared_ptr<Variable>>& get_parameters() const;

    bool equals(const std::shared_ptr<Statement> &candidate) const;
};