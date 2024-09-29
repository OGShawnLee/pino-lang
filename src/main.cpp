#include "Lexer.cpp"

int main() {
    std::vector<Lexer::Token> collection = Lexer::lex_file("main.pino");

    for (Lexer::Token token : collection) {
        token.print();
    }

    return 0; 
}