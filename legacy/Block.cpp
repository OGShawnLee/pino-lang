#pragma once

#include <memory>
#include "Block.h"
#include "ControlFlow.h"
#include "Expression.cpp"
#include "Function.cpp"
#include "Lexer.h"
#include "Loop.h"
#include "Statement.h"
#include "Struct.h"
#include "Variable.cpp"
#include "utils.h"

std::vector<std::unique_ptr<Statement>> Block::build_program(std::vector<Token> collection) {
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

        if (token.keyword == Keyword::RETURN_KEYWORD) {
          throw std::runtime_error("Return Statement outside of function body");
        }

        if (token.keyword == Keyword::STRUCT_KEYWORD) {
          PeekPtr<StructDefinition> struct_peek = StructDefinition::build(collection, i);
          program.push_back(std::move(struct_peek.node));
          i = struct_peek.index;
        }

        if (token.keyword == Keyword::DO_KEYWORD) {
          PeekPtr<DOBlock> do_peek = DOBlock::build(collection, i);
          program.push_back(std::move(do_peek.node));
          i = do_peek.index;
        }
      } break;
      default:
        break;
    }
  }			

  return program;
}

PeekStreamPtr<Statement> Block::build(
  std::vector<Token> collection, 
  size_t index
) {
  PeekStreamPtr<Statement> result;

  result.index = index;

  peek<Token>(collection, result.index, [](Token &token) {
    return token.marker == Marker::LEFT_BRACE;
  });

  for (size_t i = result.index + 1; i < collection.size(); i++) {
    Token token = collection[i];

    if (token.is_given_marker(Marker::RIGHT_BRACE)) {
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

        if (token.keyword == Keyword::RETURN_KEYWORD) {
          PeekPtr<ReturnStatement> return_peek = ReturnStatement::build(collection, i);
          result.nodes.push_back(std::move(return_peek.node));
          i = return_peek.index;
        }

        if (token.keyword == Keyword::STRUCT_KEYWORD) {
          throw std::runtime_error("USER: Struct Definition not allowed in block");
        }

        if (token.keyword == Keyword::DO_KEYWORD) {
          PeekPtr<DOBlock> do_peek = DOBlock::build(collection, i);
          result.nodes.push_back(std::move(do_peek.node));
          i = do_peek.index;
        }
      } break;
      default:
        break;
    }
  }			

  return result;
}