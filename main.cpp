#include "transpiler.h"

int main() {
  std::vector<Token> stream = Lexer::tokenise("main.pino");
  for (Token token : stream) token.print();
  Statement statement = Parser::parse(stream);
  statement.print();
  Transpiler::transpile("main.pino", "index.js");
  return 0;
}