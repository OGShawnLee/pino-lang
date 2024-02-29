#include "Lexer.h"

int main() {
  std::vector<Token> collection = Lexer::lex_file("main.pino");
  for (Token token : collection) token.print();
  return 0;
}