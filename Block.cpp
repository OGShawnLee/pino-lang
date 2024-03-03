#pragma once

#include <memory>
#include "Block.h"
#include "ControlFlow.h"
#include "Expression.cpp"
#include "Function.cpp"
#include "Lexer.h"
#include "Loop.h"
#include "Statement.h"
#include "Variable.cpp"
#include "utils.h"

std::vector<std::unique_ptr<Statement>> Block::build(std::vector<Token> collection) {
  std::vector<std::unique_ptr<Statement>> program;

  for (size_t i = 0; i < collection.size(); i++) {
    Token token = collection[i];

    if (Reassignment::is_reassigment(collection, i)) {
      PeekPtr<Reassignment> reassigment_peek = Reassignment::build(collection, i);
      program.push_back(std::move(reassigment_peek.node));
      i = reassigment_peek.index;
    }

    if (FunctionCall::is_fn_call(collection, i)) {
      PeekPtr<FunctionCall> fn_call_peek = FunctionCall::build(collection, i);
      program.push_back(std::move(fn_call_peek.node));
      i = fn_call_peek.index;
    }
    
    switch (token.kind) {
      case Kind::KEYWORD: {
        if (
          token.keyword == Keyword::VAL_KEYWORD || 
          token.keyword == Keyword::VAR_KEYWORD
        ) {
          PeekPtr<Variable> var_peek = Variable::build(collection, i);
          program.push_back(std::move(var_peek.node));
          i = var_peek.index;
        }

        if (token.keyword == Keyword::FN_KEYWORD) {
          PeekPtr<Function> fn_peek = Function::build(collection, i);
          program.push_back(std::move(fn_peek.node));
          i = fn_peek.index;
        }

        if (token.keyword == Keyword::IF_KEYWORD) {
          PeekPtr<IFStatement> if_peek = IFStatement::build(collection, i);
          program.push_back(std::move(if_peek.node));
          i = if_peek.index;
        }

        if (token.keyword == Keyword::LOOP_KEYWORD) {
          PeekPtr<Loop> loop_peek = Loop::build(collection, i);
          program.push_back(std::move(loop_peek.node));
          i = loop_peek.index;
        }
      } break;
      default:
        break;
    }
  }			

  return program;
}

PeekStreamPtr<Statement> Block::build(std::vector<Token> collection, size_t index) {
  PeekStreamPtr<Statement> result;

  result.index = index;

  return Block::build_with_break(
    collection, 
    result.index, 
    [](Token &token) {
      return token.marker == Marker::RIGHT_BRACE;
    }
  );
}

PeekStreamPtr<Statement> Block::build_with_break(
  std::vector<Token> collection, 
  size_t index,
  std::function<bool(Token &)> is_end_of_block
) {
  PeekStreamPtr<Statement> result;

  result.index = index;

  peek<Token>(collection, result.index, [](Token &token) {
    return token.marker == Marker::LEFT_BRACE;
  });

  for (size_t i = result.index + 1; i < collection.size(); i++) {
    Token token = collection[i];

    if (is_end_of_block(token)) {
      result.index = i;
      return result;
    }

    if (Reassignment::is_reassigment(collection, i)) {
      PeekPtr<Reassignment> reassigment_peek = Reassignment::build(collection, i);
      result.nodes.push_back(std::move(reassigment_peek.node));
      i = reassigment_peek.index;
    }

    if (FunctionCall::is_fn_call(collection, i)) {
      PeekPtr<FunctionCall> fn_call_peek = FunctionCall::build(collection, i);
      result.nodes.push_back(std::move(fn_call_peek.node));
      i = fn_call_peek.index;
    }
    
    switch (token.kind) {
      case Kind::KEYWORD: {
        if (
          token.keyword == Keyword::VAL_KEYWORD || 
          token.keyword == Keyword::VAR_KEYWORD
        ) {
          PeekPtr<Variable> var_peek = Variable::build(collection, i);
          result.nodes.push_back(std::move(var_peek.node));
          i = var_peek.index;
        }

        if (token.keyword == Keyword::FN_KEYWORD) {
          PeekPtr<Function> fn_peek = Function::build(collection, i);
          result.nodes.push_back(std::move(fn_peek.node));
          i = fn_peek.index;
        }

        if (token.keyword == Keyword::IF_KEYWORD) {
          PeekPtr<IFStatement> if_peek = IFStatement::build(collection, i);
          result.nodes.push_back(std::move(if_peek.node));
          i = if_peek.index;
        }

        if (token.keyword == Keyword::LOOP_KEYWORD) {
          PeekPtr<Loop> loop_peek = Loop::build(collection, i);
          result.nodes.push_back(std::move(loop_peek.node));
          i = loop_peek.index;
        }
      } break;
      default:
        break;
    }
  }			

  return result;
}