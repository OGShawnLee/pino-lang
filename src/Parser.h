#include "Statement.h"
#include "Declaration.h"

class Parser {
  public:
    static std::unique_ptr<Variable> parse_variable(const std::vector<Lexer::Token> &collection, size_t &index);
    static Statement parse_file(const std::string &filename);
};