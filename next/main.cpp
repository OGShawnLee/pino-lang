#include "Common.h"
#include "./Lexer/Lexer.cpp"
#include "./Lexer/Test.h"
#include "./Parser/Parser.cpp"

int main(int argc, char *argv[]) {
  if (argc > 1) {
    std::string command = argv[1];
    
    if (command == "t") {
      bool with_test_name = false;

      if (argc > 2) {
        if (std::string(argv[2]) == "-named") {
          with_test_name = true;
        }
      } 

      Test().run_all(with_test_name);
      return 0;
    }
  }

  println("Welcome to Pino REPL!");
  println("Type '.exit' to leave");
  println("Type '.t' to run tests");

  while (true) {
    std::string line = get_string_from_input(">: ");

    if (line == ".exit") {
      break;
    }

    if (line == ".t") {
      Test().run_all();
      continue;
    }

    Lexer::lex_line(line).print();
  }

  return 0;
}