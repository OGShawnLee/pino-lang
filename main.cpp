#include "Transpiler.h"

int main() {
  Statement program = Parser::parse_file("main.pino");
  program.print();

  // JSTranspiler::transpile("main.pino", "index.js");
  
  return 0;
}