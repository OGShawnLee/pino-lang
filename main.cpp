// #include "Parser.h"
#include "Transpiler.h"

int main() {
  // std::vector<Token> tokens = Lexer::lex_file("main.pino");
  // for (Token token : tokens) {
    // token.print();
  // }    

  // Statement program = Parser::parse_file("main.pino");
  // program.print();

  JSTranspiler::transpile("main.pino", "index.js");
  
  return 0;
}