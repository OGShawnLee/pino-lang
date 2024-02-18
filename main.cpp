#include "parser.h"

int main() {
  std::vector<Token> stream = Lexer::tokenise("main.pino");
  Statement statement = Parser::parse(stream);
  statement.print();
  return 0;
}