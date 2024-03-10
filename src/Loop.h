#pragma once

#include "Block.h"
#include "Expression.h"
#include "Statement.h"
#include "utils.h"

enum class LoopType {
  TIMES_LOOP,
  IN_LOOP,
};

class Loop : public Statement {
  static LoopType check_loop(std::vector<Token> stream, size_t index) {
    if (stream[index].is_given_keyword(Keyword::LOOP_KEYWORD) == false) {
      throw std::runtime_error("DEV: Not a Loop Statement");
    }

    // for 5 | for times
    auto id_or_literal = peek<Token>(stream, index, [](Token &token) {
      return token.kind == Kind::IDENTIFIER || token.is_given_literal(Literal::INTEGER);
    });

    // for ... { | for ... in   
    auto brace_or_in_keyword = peek<Token>(stream, id_or_literal.index, [](Token &token) {
      return token.is_given_marker(Marker::LEFT_BRACE) || token.is_given_keyword(Keyword::IN_KEYWORD);
    });


    // for ... {
    if (brace_or_in_keyword.node.is_given_marker(Marker::LEFT_BRACE)) {
      return LoopType::TIMES_LOOP;
    }

    // for ... in times | for ... in 5
    id_or_literal = peek<Token>(stream, brace_or_in_keyword.index, [](Token &token) {
      return token.kind == Kind::IDENTIFIER || token.is_given_literal(Literal::INTEGER);
    });
    
    auto marker = peek<Token>(stream, id_or_literal.index, [](Token &token) {
      return token.is_given_marker(Marker::LEFT_BRACE);
    });

    // for ... in ... {
    return LoopType::IN_LOOP;
  }

  static PeekPtr<Loop> handle_times_loop(std::vector<Token> stream, size_t index) {
    PeekPtr<Loop> result;
    Peek<Token> id_or_literal = get_next<Token>(stream, index);
    result.node->loop_type = LoopType::TIMES_LOOP;

    if (id_or_literal.node.kind == Kind::IDENTIFIER) {
      result.node->limit = Identifier::from_identifier(id_or_literal.node);
    } else {
      result.node->limit = Value::from_literal(id_or_literal.node);
    }
    
    PeekStreamPtr<Statement> body = Block::build(stream, id_or_literal.index);
    result.node->children = std::move(body.nodes);

    result.index = body.index;
    return result;
  }

  static PeekPtr<Loop> handle_in_loop(std::vector<Token> stream, size_t index) {
    PeekPtr<Loop> result;
    result.node->loop_type = LoopType::IN_LOOP;
    
    Peek<Token> id_or_literal = get_next<Token>(stream, index);
    if (id_or_literal.node.kind == Kind::IDENTIFIER) {
      result.node->index = Identifier::from_identifier(id_or_literal.node);
    } else {
      result.node->index = Value::from_literal(id_or_literal.node);
    }
    
    Peek<Token> in_keyword = get_next<Token>(stream, id_or_literal.index);
    
    id_or_literal = get_next<Token>(stream, in_keyword.index);
    if (id_or_literal.node.kind == Kind::IDENTIFIER) {
      result.node->limit = Identifier::from_identifier(id_or_literal.node);
    } else {
      result.node->limit = Value::from_literal(id_or_literal.node);
    }
    
    PeekStreamPtr<Statement> children = Block::build(stream, id_or_literal.index);
    result.node->children = std::move(children.nodes);
    
    result.index = children.index;
    return result;
  }
  
  public:
    LoopType loop_type;
    std::unique_ptr<Expression> index;
    std::unique_ptr<Expression> limit;

    Loop() {
      kind = StatementKind::LOOP_STATEMENT;
    }

    static PeekPtr<Loop> build(std::vector<Token> stream, size_t index) {
      LoopType type = check_loop(stream, index);
      return type == LoopType::IN_LOOP ? handle_in_loop(stream, index) : handle_times_loop(stream, index);
    }

    void print(size_t indentation) const {
      std::string indent = get_indentation(indentation);
      println(indent + "Loop {");

      if (loop_type == LoopType::IN_LOOP) {
        println(indent + "  index: {");
        index->print(indentation + 4);
        println(indent + "  }");
      } 

      println(indent + "  limit: {");
      limit->print(indentation + 4);
      println(indent + "  }");

      println(indent + "  body: {");
      if (children.size() > 0) {
        for (const std::unique_ptr<Statement> &child : children) {
          child->print(indentation + 4);
        }
      }
      println(indent + "  }");
      
      println(indent + "}");
    }
};
