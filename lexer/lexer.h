#ifndef LEXER_H
#define LEXER_H

#include <vector>
#include "lexer_utils.h"
#include "../utils.h"
#include "../types.h"

class Token {
  public:
    Kind kind;
    std::string name;
    std::string value;
    size_t line;
    size_t column;

    static Token as_literal(Literal literal, std::string value, size_t column, size_t line) {
      Token token;
      token.kind = Kind::LITERAL;
      token.name = get_literal_name(literal);
      token.value = value;
      token.line = line;
      token.column = column;
      return token;
    }

    static Token as_marker(Marker marker, char character, size_t column, size_t line) {
      Token token;
      token.kind = Kind::MARKER;
      token.name = get_marker_name(marker);
      token.value = std::string(1, character);
      token.line = line;
      token.column = column;
      return token;
    }

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
  static Token handle_buffer(std::string buffer, size_t column, size_t line) {
    Token token;
    token.value = buffer;
    token.line = line;
    token.column = column;

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

  static Token handle_str_injection(std::string line, size_t index_start, size_t line_number) {
    std::string buffer = "";

    for (size_t i = index_start + 1; i < line.length(); i++) {
      char character = line[i];

      if (is_valid_identifier_char(character) == false) {
        Token token;
        token.kind = Kind::IDENTIFIER;
        token.name = "Identifier";
        token.value = buffer;
        token.line = line_number;
        token.column = index_start;
        return token;
      }

      buffer += character;
    }

    throw "Invalid String Injection";
  }

  static bool is_next_char_alpha(std::string line, size_t index) {
    return index + 1 < line.length() && isalpha(line[index + 1]);
  }

  static bool is_valid_identifier_char(char character) {
    return isalnum(character) || character == '_' || character == '$';
  }

  static PeekStream<Token> lex_str_literal(std::string line, size_t index_start, size_t line_number) {
    PeekStream<Token> result;
    std::string buffer = "";

    result.nodes.push_back(
      Token::as_marker(Marker::DOUBLE_QUOTE, '"', index_start, line_number)
    );

    for (size_t i = index_start + 1; i < line.length(); i++) {
      char character = line[i];

      if (is_marker(character)) {
        Marker marker = get_marker(character);

        if (marker == Marker::DOUBLE_QUOTE) {
          result.nodes.push_back(
            Token::as_literal(Literal::STRING, buffer, index_start, line_number)
          );
          result.nodes.push_back(
            Token::as_marker(Marker::DOUBLE_QUOTE, '"', i, line_number)
          );
          result.index = i;
          return result;
        }

        if (marker == Marker::DOLLAR_SIGN && is_next_char_alpha(line, i)) {
          result.nodes.push_back(
            handle_str_injection(line, i, line_number)
          );
        }
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

          Token token = handle_buffer(buffer, i, line_number);
          stream.push_back(token);
          buffer = "";

          continue;
        }

        if (is_marker(character)) {
          if (is_whitespace(buffer) == false) {
            Token token = handle_buffer(buffer, i, line_number);
            stream.push_back(token);
            buffer = "";
          }

          Marker marker = get_marker(character);

          switch (marker) {
            case Marker::DOUBLE_QUOTE: {
              PeekStream<Token> result = lex_str_literal(line, i, line_number);
              stream.insert(stream.end(), result.nodes.begin(), result.nodes.end());
              i = result.index;
              break;
            }
            default:
              Token token = Token::as_marker(marker, character, i, line_number);
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