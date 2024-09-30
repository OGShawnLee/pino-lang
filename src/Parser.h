#include "Statement.h"
#include "Declaration.h"

class Parser {
  public:
    static std::unique_ptr<Function> parse_function(Lexer::Stream &collection);
    static std::unique_ptr<Variable> parse_variable(Lexer::Stream &collection);
    static Statement parse_file(const std::string &filename);
};