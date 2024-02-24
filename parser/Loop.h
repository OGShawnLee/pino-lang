#pragma once

#include "Statement.h"
#include "Parser.h"
#include "Expression.h"
#include "global.h"

class Loop : public Statement {
  static Peek<LoopType> check_loop_type(std::vector<Token> stream, size_t index) {
    auto next = peek<Token>(
      stream,
      index,
      [](Token &token) {
        return token.is_given_marker(Marker::LEFT_BRACE) || token.is_given_keyword(Keyword::IN);
      },
      [](Token &token) {
        return std::runtime_error("Expected left brace or index keyword, but got " + token.value);
      },
      [](Token &token) {
        return std::runtime_error("Unexpected end of stream");
      }
    );

    Peek<LoopType> result;
    result.node = next.node.kind == Kind::KEYWORD ? LoopType::IN_LOOP : LoopType::TIMES_LOOP;
    result.index = next.index;
    return result;
  }

  public:
    LoopType loop_type;
    std::unique_ptr<Expression> length;
    std::unique_ptr<Expression> index;

    Loop() {
      this->kind = StatementType::LOOP_STATEMENT;
    }

    static PeekPtr<Loop> build(std::vector<Token> stream, size_t index) {
      if (stream[index].is_given_keyword(Keyword::LOOP) == false) {
        throw std::runtime_error("Expected for keyword, but got " + stream[index].value);
      }

      auto id_or_literal = peek<Token>(
        stream,
        index,
        [](Token &token) {
          return token.kind == Kind::IDENTIFIER || token.is_given_literal(Literal::INTEGER);
        },
        [](Token &token) {
          return std::runtime_error("Expected identifier or literal, but got " + token.value);
        },
        [](Token &token) {
          return std::runtime_error("Unexpected end of stream");
        }
      );

      Peek<LoopType> loop_type = check_loop_type(stream, id_or_literal.index);
      PeekPtr<Loop> result;
      result.node->loop_type = loop_type.node;

      if (id_or_literal.node.kind == Kind::IDENTIFIER) {
        result.node->index = Identifier::from_identifier(id_or_literal.node);
      } else {
        result.node->index = Value::from_literal(id_or_literal.node);
      }

      if (loop_type.node == LoopType::TIMES_LOOP) {
        PeekStreamPtr<Statement> body = Parser::parse_block(stream, loop_type.index);
        
        if (id_or_literal.node.kind == Kind::IDENTIFIER) {
          result.node->length = Identifier::from_identifier(id_or_literal.node);
        } else {
          result.node->length = Value::from_literal(id_or_literal.node);
        }

        result.node->body = std::move(body.nodes);
        result.index = body.index;
        return result;
      }

      id_or_literal = peek<Token>(
        stream,
        loop_type.index,
        [](Token &token) {
          return token.kind == Kind::IDENTIFIER || token.is_given_literal(Literal::INTEGER, Literal::STRING);
        },
        [](Token &token) {
          return std::runtime_error("Expected identifier, but got " + token.value);
        },
        [](Token &token) {
          return std::runtime_error("Unexpected end of stream");
        }
      );

      auto left_brace = check_left_brace(stream, id_or_literal.index);
      PeekStreamPtr<Statement> body = Parser::parse_block(stream, left_brace.index);

      if (id_or_literal.node.kind == Kind::IDENTIFIER) {
        result.node->length = Identifier::from_identifier(id_or_literal.node);
      } else {
        result.node->length = Value::from_literal(id_or_literal.node);
      }

      result.node->body = std::move(body.nodes);
      result.index = body.index;
      return result;
    }

    void print(size_t indentation) {
      std::string indentation_str = get_indentation(indentation);

      println(indentation_str + "Loop {");
      println(indentation_str + "  length: ");
      length->print(indentation + 2);
      println(indentation_str + "  body: [");
      for (auto &node : body) {
        node->print(indentation + 4);
      }
      println(indentation_str + "  ]");
      println(indentation_str + "}");
    }
};