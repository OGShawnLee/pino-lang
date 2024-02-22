// #include "./lexer/lexer.h"
// #include "./parser/Parser.cpp"
#include "transpiler.h"

int main() {
  // auto stream = Lexer::tokenise("main.pino");
  // for (auto token : stream) token.print();
  // auto ast = Parser::parse(stream);
  // ast.print();
  Transpiler::transpile("main.pino", "index.js");
  return 0;
}