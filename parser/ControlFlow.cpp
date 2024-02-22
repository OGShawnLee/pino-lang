#include "ControlFlow.h"
#include "Parser.h"

ELSEStatement::ELSEStatement() {
  kind = StatementType::ELSE_STATEMENT;
  name = get_statement_type_name(kind);
}

PeekPtr<ELSEStatement> ELSEStatement::build(std::vector<Token> stream, size_t index) {
  PeekPtr<ELSEStatement> result;
  Token current = stream[index];

  if (current.is_given_keyword(Keyword::ELSE) == false) {
    throw std::runtime_error("Expected else keyword, but got " + current.value);
  }

  auto marker = peek<Token>(
    stream,
    index,
    [](Token &token) {
      return token.is_given_marker(Marker::LEFT_BRACE);
    },
    [](Token &token) {
      return std::runtime_error("Expected left brace, but got " + token.value);
    },
    [](Token &token) {
      return std::runtime_error("Unterminated else statement");
    }
  );

  PeekStreamPtr<Statement> body = Parser::parse_block(stream, marker.index + 1);
  result.node->body = std::move(body.nodes);
  result.index = body.index;
  return result;
}

IFStatement::IFStatement() {
  kind = StatementType::IF_STATEMENT;
  name = get_statement_type_name(kind);
}

PeekPtr<IFStatement> IFStatement::build(std::vector<Token> stream, size_t index) {
  PeekPtr<IFStatement> result;
  Token current = stream[index];

  if (current.is_given_keyword(Keyword::IF) == false) {
    throw std::runtime_error("Expected if keyword, but got " + current.value);
  }

  auto condition = peek<Token>(
    stream,
    index,
    [](Token &token) {
      return token.is_given_literal(Literal::BOOLEAN) || token.kind == Kind::IDENTIFIER;
    },
    [](Token &token) {
      return std::runtime_error("Expected condition, but got " + token.value);
    },
    [](Token &token) {
      return std::runtime_error("Unterminated if statement");
    }
  );

  result.node->condition = condition.node.value;

  auto marker = peek<Token>(
    stream,
    condition.index,
    [](Token &token) {
      return token.is_given_marker(Marker::LEFT_BRACE);
    },
    [](Token &token) {
      return std::runtime_error("Expected left brace, but got " + token.value);
    },
    [](Token &token) {
      return std::runtime_error("Unterminated if statement");
    }
  );

  PeekStreamPtr<Statement> body = Parser::parse_block(stream, marker.index + 1);
  result.node->body = std::move(body.nodes);
  result.index = body.index;

  bool else_keyword = is_next<Token>(
    stream,
    result.index,
    [](Token &token) {
      return token.is_given_keyword(Keyword::ELSE);
    }
  );

  if (else_keyword) {
    PeekPtr<ELSEStatement> else_statement = ELSEStatement::build(stream, result.index + 1);
    result.node->else_statement = std::move(else_statement.node);
    result.index = else_statement.index;
  }

  return result;
}