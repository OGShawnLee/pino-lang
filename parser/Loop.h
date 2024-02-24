#pragma once

#include "Statement.h"
#include "Parser.h"
#include "Expression.h"
#include "global.h"

class Loop : public Statement {
  public:
    std::unique_ptr<Expression> times;

    Loop() {
      this->kind = StatementType::LOOP_STATEMENT;
    }

    static PeekPtr<Loop> build(std::vector<Token> stream, size_t index) {
      Token keyword = stream[index];
      if (keyword.is_given_keyword(Keyword::LOOP) == false) {
        throw std::runtime_error("Expected for keyword, but got " + keyword.value);
      }

      auto times = peek<Token>(
        stream,
        index,
        [](Token &token) {
          return token.kind == Kind::IDENTIFIER || token.is_given_literal(Literal::INTEGER);
        },
        [](Token &token) {
          return std::runtime_error("Expected times, but got " + token.value);
        },
        [](Token &token) {
          return std::runtime_error("Unexpected end of stream");
        }
      );

      Peek<Token> left_brace = check_left_brace(stream, times.index);
      PeekPtr<Loop> result;
      PeekStreamPtr<Statement> body = Parser::parse_block(stream, left_brace.index);

      if (times.node.kind == Kind::IDENTIFIER) {
        result.node->times = Identifier::from_identifier(times.node);
      } else {
        result.node->times = Value::from_literal(times.node);
      }
      
      result.node->body = std::move(body.nodes);
      result.index = body.index;
      return result;  
    }

    void print(size_t indentation) {
      std::string indentation_str = get_indentation(indentation);

      println(indentation_str + "Loop {");
      println(indentation_str + "  times: ");
      times->print(indentation + 2);
      println(indentation_str + "  body: [");
      for (auto &node : body) {
        node->print(indentation + 4);
      }
      println(indentation_str + "  ]");
      println(indentation_str + "}");
    }
};