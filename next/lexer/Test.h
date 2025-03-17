#pragma once

#include "./Lexer.cpp"
#include "./token/Keyword.h"
#include "../Common.h"

class Test {
  int count_passed = 0;
  int count_failed = 0;

  void run(const std::string &name, const std::function<bool()> &test) {
    if (test()) {
      this->count_passed++;
    } else {
      println("Test '" + name + "' failed");
      this->count_failed++;
    }
  }
  
  void test_identifier() {
    run("Lexer::Should identify an identifier", []() {
      return Lexer::lex_line("name").current()->equals(Token(TOKEN_TYPE::IDENTIFIER, "name"));
    });
    run("Lexer::Should identify an identifier with a number", []() {
      return Lexer::lex_line("name1").current()->equals(Token(TOKEN_TYPE::IDENTIFIER, "name1"));
    });
    run("Lexer::Should identify an identifier with a dollar sign", []() {
      return Lexer::lex_line("name$").current()->equals(Token(TOKEN_TYPE::IDENTIFIER, "name$"));
    });
    run("Lexer::Should identify an identifier with an underscore", []() {
      return Lexer::lex_line("name_").current()->equals(Token(TOKEN_TYPE::IDENTIFIER, "name_"));
    });
    run("Lexer::Should identify an identifier with an initial underscore", []() {
      return Lexer::lex_line("_name").current()->equals(Token(TOKEN_TYPE::IDENTIFIER, "_name"));
    });
    run("Lexer::Should identify an identifier with an initial dollar sign", []() {
      return Lexer::lex_line("$name").current()->equals(Token(TOKEN_TYPE::IDENTIFIER, "$name"));
    });
    run("Lexer::Should mark as illegal an identifier with an initial number", []() {
      return Lexer::lex_line("1name").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1name"));
    });
  }
  
  void test_keyword() {
    for (const auto &keyword : Mapper::STR_TO_KEYWORD) {
      run("Lexer::Should identify a " + keyword.first + " keyword", [keyword]() {
        return Lexer::lex_line(keyword.first).current()->equals(Keyword(keyword.second, keyword.first));
      });
    }
  }

  void test_literal() {
    // Boolean
    run("Lexer::Should identify a boolean literal", []() {
      return 
        Lexer::lex_line("true").current()->equals(Literal(LITERAL_TYPE::BOOLEAN, "true")) and 
        Lexer::lex_line("false").current()->equals(Literal(LITERAL_TYPE::BOOLEAN, "false"));
    });
    // Float
    run("Lexer::Should identify a float literal", []() {
      return Lexer::lex_line("1.0").current()->equals(Literal(LITERAL_TYPE::FLOAT, "1.0"));
    });
    run("Lexer::Should identify a float literal with underscores", []() {
      return 
        Lexer::lex_line("1_000.0").current()->equals(Literal(LITERAL_TYPE::FLOAT, "1_000.0")) and
        Lexer::lex_line("1_000_000.0").current()->equals(Literal(LITERAL_TYPE::FLOAT, "1_000_000.0")) and
        Lexer::lex_line("1_000_000_000.0").current()->equals(Literal(LITERAL_TYPE::FLOAT, "1_000_000_000.0")) and
        Lexer::lex_line("1.000_000").current()->equals(Literal(LITERAL_TYPE::FLOAT, "1.000_000")) and
        Lexer::lex_line("1.000_000_000").current()->equals(Literal(LITERAL_TYPE::FLOAT, "1.000_000_000")) and
        Lexer::lex_line("1_000.000_000").current()->equals(Literal(LITERAL_TYPE::FLOAT, "1_000.000_000")) and
        Lexer::lex_line("1_000_000.000_000").current()->equals(Literal(LITERAL_TYPE::FLOAT, "1_000_000.000_000"));
    });
    run("Lexer::Should mark as illegal a float with underscores before less or more than 3 digits", []() {
      return 
        Lexer::lex_line("1_00.0").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1_00.0")) and
        Lexer::lex_line("1_0000.0").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1_0000.0")) and
        Lexer::lex_line("1.0_00").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1.0_00")) and
        Lexer::lex_line("1.0_0000").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1.0_0000")) and
        Lexer::lex_line("1_00.0_00").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1_00.0_00")) and
        Lexer::lex_line("1_0000.0_0000").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1_0000.0_0000")) and
        Lexer::lex_line("1.0_00.0").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1.0_00.0")) and
        Lexer::lex_line("1.0_0000.0").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1.0_0000.0"));
    });
    run("Lexer::Should mark as illegal a float literal with an initial separator", []() {
      return Lexer::lex_line("_1.0").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "_1.0"));
    });
    run("Lexer::Should mark as illegal a float literal with an separator after the point", []() {
      return Lexer::lex_line("1._0").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1._0"));
    });
    run("Lexer::Should mark as illegal a float literal with a separator before the point", []() {
      return Lexer::lex_line("1_.0").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1_.0"));
    });
    run("Lexer::Should mark as illegal a float literal with a separator before and after the point", []() {
      return Lexer::lex_line("1_.0_").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1_.0_"));
    });
    // Integer
    run("Lexer::Should identify an integer literal", []() {
      return Lexer::lex_line("1").current()->equals(Literal(LITERAL_TYPE::INTEGER, "1"));
    });
    run("Lexer::Should identify an integer literal with underscores", []() {
      return 
        Lexer::lex_line("1_000").current()->equals(Literal(LITERAL_TYPE::INTEGER, "1_000")) and
        Lexer::lex_line("1_000_000").current()->equals(Literal(LITERAL_TYPE::INTEGER, "1_000_000")) and
        Lexer::lex_line("1_000_000_000").current()->equals(Literal(LITERAL_TYPE::INTEGER, "1_000_000_000"));
    });
    run("Lexer::Should mark as illegal an integer with underscores before less or more than 3 digits", []() {
      return 
        Lexer::lex_line("1_00").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1_00")) and
        Lexer::lex_line("1_0000").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1_0000")) and
        Lexer::lex_line("1_0_00").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1_0_00")) and
        Lexer::lex_line("1_0_0000").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1_0_0000")) and
        Lexer::lex_line("1_00_00").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1_00_00")) and
        Lexer::lex_line("1_0000_0000").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1_0000_0000"));
    });
    run("Lexer::Should mark as identifier an integer literal with an initial separator", []() {
      return Lexer::lex_line("_1").current()->equals(Token(TOKEN_TYPE::IDENTIFIER, "_1"));
    });
  }

  void test_marker() {
    for (const auto &pair : Mapper::CHAR_TO_MARKER) {
      run("Lexer::Should identify a " + to_str(pair.first) + " marker", [pair]() {
        return Lexer::lex_line(to_str(pair.first)).current()->equals(Marker(pair.second, pair.first));
      });
      run("Lexer::Should identify an " + to_str(pair.first) + " marker between other tokens", [&pair]() {
        Stream stream = Lexer::lex_line("1 " + to_str(pair.first) + " 1");
        return 
          stream.consume()->equals(Literal(LITERAL_TYPE::INTEGER, "1")) and
          stream.consume()->equals(Marker(pair.second, pair.first)) and
          stream.current()->equals(Literal(LITERAL_TYPE::INTEGER, "1"));
      });
      run("Lexer::Should identify an " + to_str(pair.first) + " marker between other tokens without spaces", [&pair]() {
        Stream stream = Lexer::lex_line("1" + to_str(pair.first) + "1");
        return 
          stream.consume()->equals(Literal(LITERAL_TYPE::INTEGER, "1")) and
          stream.consume()->equals(Marker(pair.second, pair.first)) and
          stream.current()->equals(Literal(LITERAL_TYPE::INTEGER, "1"));
      });
    }
  }

  void test_operator() {
    for (const auto &pair : Mapper::STR_TO_OPERATOR) {
      run("Lexer::Should identify a " + pair.first + " operator", [pair]() {
        return Lexer::lex_line(pair.first).current()->equals(Operator(pair.second, pair.first));
      });
      run("Lexer::Should identify an " + pair.first + " operator between other tokens", [&pair]() {
        Stream stream = Lexer::lex_line("1 " + pair.first + " 1");
        return 
          stream.consume()->equals(Literal(LITERAL_TYPE::INTEGER, "1")) and
          stream.consume()->equals(Operator(pair.second, pair.first)) and
          stream.current()->equals(Literal(LITERAL_TYPE::INTEGER, "1"));
      });

      if (pair.second == OPERATOR_TYPE::AND or pair.second == OPERATOR_TYPE::OR or pair.second == OPERATOR_TYPE::NOT) {
        run("Lexer::Should mark as illegal an " + pair.first + " operator without space", [&pair]() {
          return Lexer::lex_line("1" + pair.first + "1").current()->equals(Token(TOKEN_TYPE::ILLEGAL, "1" + pair.first + "1"));
        });
        continue;
      }

      run("Lexer::Should identify an " + pair.first + " operator between other tokens without spaces", [&pair]() {
        Stream stream = Lexer::lex_line("1" + pair.first + "1");
        return 
          stream.consume()->equals(Literal(LITERAL_TYPE::INTEGER, "1")) and
          stream.consume()->equals(Operator(pair.second, pair.first)) and
          stream.current()->equals(Literal(LITERAL_TYPE::INTEGER, "1"));
      });
    }
  }

  void print_results() {
    println("Tests passed: " + std::to_string(this->count_passed));
    println("Tests failed: " + std::to_string(this->count_failed));
  }

  public:
    void run_all() {
      test_identifier();
      test_keyword();
      test_literal();
      test_marker();
      test_operator();
      print_results();
    }
};
