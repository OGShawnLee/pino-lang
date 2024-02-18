#ifndef LEXER_H
#define LEXER_H

#include <vector>
#include "commons/lexer.h"
#include "utils.h"
#include "types.h"

struct Token {
  Kind kind;
  std::string name;
  std::string value;
  size_t line;
  size_t column;

  void print() {
    println(get_kind_name(kind) + " {");
    println("  name: " + name);
    println("  value: " + value);
    println("  line: " + std::to_string(line));
    println("  column: " + std::to_string(column));
    println("}");
  }

  bool is_given_marker(Marker marker) {
    return kind == Kind::MARKER && name == get_marker_name(marker);
  }
};

class Lexer {
  static Token handle_buffer(std::string buffer) {
    Token token;
    token.value = buffer;

    if (is_keyword(buffer)) {
      token.kind = Kind::KEYWORD;
      token.name = get_keyword_name(buffer);
    } else if (is_bool_literal(buffer)) {
      token.kind = Kind::LITERAL;
      token.name = get_literal_name(Literal::BOOLEAN);
    } else if (is_int_literal(buffer)) {
      token.kind = Kind::LITERAL;
      token.name = get_literal_name(Literal::INTEGER);
    } else {
      token.kind = Kind::IDENTIFIER;
      token.name = "Identifier";
    }
    
    return token;
  }

  static Peek<Token> lex_str_literal(std::string line, size_t index_start) {
    Peek<Token> result;
    std::string buffer = "";

    for (size_t i = index_start + 1; i < line.length(); i++) {
      char character = line[i];

      if (character == '"') {
        result.node.kind = Kind::LITERAL;
        result.node.name = get_literal_name(Literal::STRING);
        result.node.value = buffer;
        result.node.column = index_start;
        result.index = i;
        return result;
      }

      buffer += character;
    }

    return result;
  }

  public:
    static std::vector<Token> tokenise_line(std::string line, size_t line_number = 0) {
      std::vector<Token> stream;
      std::string buffer = "";

      line += " ";

      for (size_t i = 0; i < line.length(); i++) {
        char character = line[i];

        if (is_whitespace(character)) {
          if (is_whitespace(buffer)) {
            continue;
          }

          Token token = handle_buffer(buffer);
          token.line = line_number;
          token.column = i;
          stream.push_back(token);
          buffer = "";

          continue;
        }

        if (is_marker(character)) {
          if (is_whitespace(buffer) == false) {
            Token token = handle_buffer(buffer);
            token.line = line_number;
            token.column = i;
            stream.push_back(token);
            buffer = "";
          }

          Marker marker = get_marker(character);

          switch (marker) {
            case Marker::DOUBLE_QUOTE: {
              Peek<Token> result = lex_str_literal(line, i);
              result.node.line = line_number;
              stream.push_back(result.node);
              i = result.index;
              break;
            }
            default:
              Token token;
              token.kind = Kind::MARKER;
              token.name = get_marker_name(marker);
              token.value = std::string(1, character);
              token.line = line_number;
              token.column = i;
              stream.push_back(token);
          }

          continue;
        }

        buffer += character;
      }

      return stream;
    }

    static std::vector<Token> tokenise(std::string file_name) {
      std::vector<Token> stream;
      size_t line_number = 0;

      each_line(file_name, [&](std::string line) {
        std::vector<Token> line_stream = tokenise_line(line, line_number++);
        stream.insert(stream.end(), line_stream.begin(), line_stream.end());
      });

      return stream;
    }
};

#endif