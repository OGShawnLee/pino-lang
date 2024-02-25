#pragma once

#include "Statement.h"
#include "global.h"

class Loop : public Statement {
  static LoopType check_loop(std::vector<Token> stream, size_t index) {
    if (stream[index].is_given_keyword(Keyword::LOOP) == false) {
      throw std::runtime_error("DEV: EXPECTED LOOP KEYWORD");
    }

    // for 5 | for times
    auto id_or_literal = peek<Token>(
      stream, 
      index, 
      [](Token &token) {
        return token.kind == Kind::IDENTIFIER || token.is_given_literal(Literal::INTEGER);
      },
      [](Token &token) {
        return std::runtime_error("Expected Identifier or Integer Literal. Got " + token.value);
      },
      [](Token &token) {
        return std::runtime_error("Incomplete Loop Statement.");
      }
    );

    // for ... { | for ... in   
    auto brace_or_in_keyword = peek<Token>(
      stream, 
      id_or_literal.index, 
      [](Token &token) {
        return token.is_given_marker(Marker::LEFT_BRACE) || token.is_given_keyword(Keyword::IN);
      },
      [](Token &token) {
        return std::runtime_error("Expected 'in' keyword or '{'. Got " + token.value);
      },
      [](Token &token) {
        return std::runtime_error("Incomplete Loop Statement.");
      }
    );

    // for ... {
    if (brace_or_in_keyword.node.is_given_marker(Marker::LEFT_BRACE)) {
      return LoopType::TIMES_LOOP;
    }

    // for ... in times | for ... in 5
    id_or_literal = peek<Token>(
      stream, 
      brace_or_in_keyword.index, 
      [](Token &token) {
        return token.kind == Kind::IDENTIFIER || token.is_given_literal(Literal::INTEGER);
      },
      [](Token &token) {
        return std::runtime_error("Expected Identifier or Integer Literal. Got " + token.value);
      },
      [](Token &token) {
        return std::runtime_error("Incomplete Loop Statement.");
      }
    );
    
    check_left_brace(stream, id_or_literal.index);
    
    // for ... in ... {
    return LoopType::IN_LOOP;
  }

  static PeekPtr<Loop> handle_times_loop(std::vector<Token> stream, size_t index) {
    PeekPtr<Loop> result;
    Peek<Token> id_or_literal = get_next_token(stream, index);
    result.node->loop_type = LoopType::TIMES_LOOP;

    if (id_or_literal.node.kind == Kind::IDENTIFIER) {
      result.node->limit = Identifier::from_identifier(id_or_literal.node);
    } else {
      result.node->limit = Value::from_literal(id_or_literal.node);
    }
    
    Peek<Token> brace = check_left_brace(stream, id_or_literal.index);
    PeekStreamPtr<Statement> body = Parser::parse_block(stream, brace.index);
    result.node->body = std::move(body.nodes);
    
    result.index = body.index;
    return result;
  }

  static PeekPtr<Loop> handle_in_loop(std::vector<Token> stream, size_t index) {
    PeekPtr<Loop> result;
    result.node->loop_type = LoopType::IN_LOOP;
    
    Peek<Token> id_or_literal = get_next_token(stream, index);
    if (id_or_literal.node.kind == Kind::IDENTIFIER) {
      result.node->index = Identifier::from_identifier(id_or_literal.node);
    } else {
      result.node->index = Value::from_literal(id_or_literal.node);
    }
    
    Peek<Token> in_keyword = get_next_token(stream, id_or_literal.index);
    
    id_or_literal = get_next_token(stream, in_keyword.index);
    if (id_or_literal.node.kind == Kind::IDENTIFIER) {
      result.node->limit = Identifier::from_identifier(id_or_literal.node);
    } else {
      result.node->limit = Value::from_literal(id_or_literal.node);
    }
    
    Peek<Token> brace = check_left_brace(stream, id_or_literal.index);
    
    PeekStreamPtr<Statement> body = Parser::parse_block(stream, brace.index);
    result.node->body = std::move(body.nodes);
    
    result.index = body.index;
    return result;
  }
  
  public:
    LoopType loop_type;
    std::unique_ptr<Expression> index;
    std::unique_ptr<Expression> limit;

    Loop() {
      kind = StatementType::LOOP_STATEMENT;
    }

    static PeekPtr<Loop> build(std::vector<Token> stream, size_t index) {
      LoopType type = check_loop(stream, index);
      return type == LoopType::IN_LOOP ? handle_in_loop(stream, index) : handle_times_loop(stream, index);
    }
};
