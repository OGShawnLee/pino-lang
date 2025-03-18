#include "../parser/Parser.h"

class Transpiler {
  static std::string handle_expression(const Expression &expression);
  static std::string handle_variable(const Variable &variable);
  
  public:
    static std::string transpile_line(const std::string &line);

    static std::string transpile_statement(const std::shared_ptr<Statement> statement);
    
    static void transpile_program_to_file(const std::shared_ptr<Statement>, const std::string &output_file);
};