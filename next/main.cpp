#include <filesystem>
#include "Common.h"
#include "Parser.h"
#include "Transpiler.h"
#include "Test.h"

int main(int argc, char *argv[]) {
  std::string current_directory = std::filesystem::current_path().string();

  if (argc == 1) {
    std::string file_path = current_directory + "/main.pino";
    std::string file_output_path = current_directory + "/main.py";

    Transpiler::transpile_file(file_path, file_output_path);
    
    system(("python " + file_output_path).c_str());

    std::filesystem::remove(file_output_path);

    return 0;
  } 

  std::string command = std::string(argv[1]);

  if (command == "h" or command == "help") {
    println("Usage: [command]");
    println("Commands:");
    println("  h, help: Display this information");
    println("  repl: Start the Pino REPL (lex and print the input)");
    println("  run [file-name]: Run the given .pino file");
    println("  run <empty>: Run the main.pino file of your current directory");
    println("  t, test: Test your Pino installation");
    println("  t, test -show: Show the test names");
    println("  v, version: Display the current version of Pino");
    println("  <empty>: Run the main.pino file of your current directory");
    return 0;
  }

  if (command == "repl") {
    println("Welcome to the Pino REPL (Lexing Mode)");
    println("Type .exit to stop the REPL");
    
    while (true) {
      std::string line = get_string_from_input("> ");

      if (line.empty()) {
        continue;
      }

      if (line == ".exit") {
        break;
      }

      Stream(Lexer::lex_line(line)).print();
    }

    return 0;
  }

  if (command == "t" or command == "test") {
    bool should_test_name = argc == 3 and std::string(argv[2]) == "-show";
    Test test;
    test.run_all(should_test_name);
    return 0;
  }

  if (command == "v" or command == "version") {
    println("Pino version: pre-alpha");
    return 0;
  }

  if (command != "run") {
    println("Invalid command. Type 'h' or 'help' for more information.");
    return 1;
  }

  std::string file_name = argc == 2 ? "main.pino" : std::string(argv[2]);

  std::string file_path = current_directory + "/" + file_name;
  std::string file_output_path = current_directory + "/" + file_name.substr(0, file_name.find_last_of('.')) + ".py";
  Transpiler::transpile_file(file_path, file_output_path);

  system(("python " + file_output_path).c_str());

  std::filesystem::remove(file_output_path);

  return 0;
}