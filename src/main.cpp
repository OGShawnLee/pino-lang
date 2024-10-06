#include "Parser.cpp"

int main(int argc, char **argv) {
	if (argc == 1) {
		Parser::parse_file("main.pino").print(0);
	} else {
		std::string command = std::string(argv[1]);

		if (command == "h" or command == "help") {
			println("Usage: [command]");
			println("Commands:");
			println("  h, help: Display this information");
			println("  l, lex [filename]: Display the lexed tokens of a .pino file");
			println("  l, lex <empty>: Display the lexed tokens of the main.pino file");
			println("  p, parse [filename]: Display the parsed statements of a .pino file");
			println("  p, parse <empty>: Display the parsed statements of the main.pino file");
			println("  repl: Start the Pino REPL (parse and print the input)");
			println("  repl -l | <empty>: Lex and print input");
			println("  repl -p: Parse and print input (not yet implemented)");
			println("  <empty>: Display the parsed statements of the main.pino file");
			return 0;
		}

		if (command == "repl") {
			std::string flag = argc == 3 ? std::string(argv[2]) : "-l";

			if (flag != "-l" and flag != "-p") {
				println("Invalid Flag");
				return 1;
			}

			if (flag == "-l") {
				println("Welcome to the Pino REPL (Lexing Mode)");
				println("Type .exit to stop the REPL");
				
				while (true) {
					std::string line = get_string("> ");

					if (line.empty()) {
						continue;
					}

					if (line == ".exit") {
						break;
					}

					Lexer::Stream(Lexer::lex_line(line)).print();
				}
			}

			if (flag == "-p") {
				println("Welcome to the Pino REPL (Parsing Mode)");
				println("Type .exit to stop the REPL");
				println("Parsing Mode is not yet implemented");
			}

			return 0;
		}

		std::string filename = argc == 2 ? "main.pino" : std::string(argv[2]);

		if (filename.find(".pino") == std::string::npos) {
			println("Invalid File Extension (must be .pino)");
			return 1;
		}	

		if (command == "l" or command == "lex") {	
			Lexer::lex_file(filename).print();
			return 0;
		} 
		
		if (command == "p" or command == "parse") {
			Parser::parse_file(filename).print(0);
			return 0;
		}

		println("Invalid Command");
	}

	return 0;
}
