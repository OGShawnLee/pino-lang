#include "Common.h"
#include "./lexer/Lexer.cpp"
#include "./lexer/Test.h"

int main(int argc, char *argv[]) {
  if (argc > 1) {
    std::string command = argv[1];
    
    if (command == "t") {
      Test().run_all();
      return 0;
    }
  }

  println("Welcome to Pino REPL!");
  println("Type '.exit' to leave");

  while (true) {
    std::string line = get_string_from_input(">: ");

    if (line == ".exit") {
      break;
    }

    Lexer::lex_line(line).print();
  }

  return 0;
}