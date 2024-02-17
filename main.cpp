#include "lexer.h"

int main() {
  std::vector<Token> stream = Lexer::tokenise("main.pino");
  for (Token token : stream) token.print();
  return 0;
}