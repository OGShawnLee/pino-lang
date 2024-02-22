#include "ControlFlow.h"
#include "Function.h"
#include "Parser.h"
#include "Statement.h"
#include "Variable.h"

bool is_function_call(std::vector<Token> stream, size_t index) {
  return is_next<Token>(stream, index, [](Token &token) {
    return token.is_given_marker(Marker::LEFT_PARENTHESIS);
  });
}

FunctionDefinition::FunctionDefinition() {
  kind = StatementType::FUNCTION_DEFINITION;
  name = get_statement_type_name(kind);
  return_type = "void";
}

Peek<Parameter> FunctionDefinition::parse_parameter(std::vector<Token> stream, size_t index) {
  Peek<Parameter> result;
  Token current = stream[index];

  if (current.kind != Kind::IDENTIFIER) {
    throw std::runtime_error("Expected identifier, but got " + current.value);
  }

  result.node.name = current.value;

  auto type = peek<Token>(
    stream,
    index,
    [](Token &token) {
      return token.kind == Kind::BUILT_IN_TYPE;
    },
    [](Token &token) {
      return std::runtime_error("Expected type, but got " + token.value);
    },
    [](Token &token) {
      return std::runtime_error("Unexpected end of stream");
    }
  );

  result.node.type = type.node.value;

  auto marker = peek<Token>(
    stream,
    type.index,
    [](Token &token) {
      return token.is_given_marker(Marker::COMMA, Marker::RIGHT_PARENTHESIS);
    },
    [](Token &token) {
      return std::runtime_error("Expected comma or right parenthesis, but got " + token.value);
    },
    [](Token &token) {
      return std::runtime_error("Unexpected end of function parameter");
    }
  );

  if (marker.node.is_given_marker(Marker::COMMA)) {
    result.index = marker.index;
  } else {
    result.index = marker.index - 1;
  }

  return result;
}

PeekStream<Parameter> FunctionDefinition::parse_parameters(std::vector<Token> stream, size_t index) {
  PeekStream<Parameter> result;

  for (size_t i = index + 1; i < stream.size(); i++) {
    Token token = stream[i];

    if (token.is_given_marker(Marker::RIGHT_PARENTHESIS)) {
      result.index = i;
      return result;
    }

    Peek<Parameter> parameter = parse_parameter(stream, i);
    result.nodes.push_back(parameter.node);
    i = parameter.index;
  }

  return result;
  // throw std::runtime_error("Unterminated function definition");
}

PeekPtr<FunctionDefinition> FunctionDefinition::build(std::vector<Token> stream, size_t index) {
  PeekPtr<FunctionDefinition> result;

  Token current = stream[index];
  if (current.is_given_keyword(Keyword::FUNCTION) == false) {
    throw std::runtime_error("Expected function keyword, but got " + current.value);
  }

  Peek<std::string> name = Entity::get_name(stream, index);
  result.node->name = name.node;

  auto left_parenthesis = check_left_parenthesis(stream, name.index);

  PeekStream<Parameter> parameters = parse_parameters(stream, left_parenthesis.index);
  result.index = parameters.index;
  result.node->parameters = std::move(parameters.nodes);

  auto left_brace = check_left_brace(stream, parameters.index);

  PeekStreamPtr<Statement> body = Parser::parse_block(stream, left_brace.index + 1);
  result.node->body = std::move(body.nodes);
  result.index = body.index;

  return result;
}

void FunctionDefinition::print(size_t indentation) {
  std::string indentation_str = get_indentation(indentation);

  println(indentation_str + get_statement_type_name(kind) + " {");
  println(indentation_str + "  name: " + name);
  println(indentation_str + "  return_type: " + return_type);
  println(indentation_str + "  parameters: [");
  for (Parameter &parameter : parameters) {
    println(indentation_str + "    {");
    println(indentation_str + "      name: " + parameter.name);
    println(indentation_str + "      type: " + parameter.type);
    println(indentation_str + "    }");
  }
  println(indentation_str + "  ]");
  println(indentation_str + "  body: [");
  for (std::unique_ptr<Statement> &statement : body) {
    statement->print(indentation + 4);
  }
  println(indentation_str + "  ]");
  println(indentation_str + "}");
}

PeekStreamPtr<Statement> FunctionCall::parse_arguments(std::vector<Token> stream, size_t index) {
  PeekStreamPtr<Statement> result;

  // + 1 to skip the left parenthesis
  for (size_t i = index + 1; i < stream.size(); i++) {
    Token token = stream[i];

    switch (token.kind) {
      case Kind::LITERAL: {
        Statement *literal = new Statement();
        literal->name = token.value;
        literal->kind = StatementType::EXPRESSION;
        result.nodes.push_back(std::unique_ptr<Statement>(literal));
        break;
      }
      case Kind::IDENTIFIER: {
        if (is_function_call(stream, i)) {
          PeekPtr<FunctionCall> function = FunctionCall::build(stream, i);
          result.nodes.push_back(std::move(function.node));
          i = function.index;
          break;
        }

        if (Variable::is_reassignment(stream, i)) {
          PeekPtr<Variable> variable = Variable::build(stream, i, true);
          result.nodes.push_back(std::move(variable.node));
          i = variable.index;
          break;
        }

        Statement *identifier = new Statement();
        identifier->name = token.value;
        identifier->kind = StatementType::EXPRESSION;
        result.nodes.push_back(std::unique_ptr<Statement>(identifier));
        break;
      }
      case Kind::MARKER:
        if (token.is_given_marker(Marker::RIGHT_PARENTHESIS)) {
          result.index = i;
          return result;
        }

        if (token.is_given_marker(Marker::COMMA)) {
          continue;
        }

        if (token.is_given_marker(Marker::DOUBLE_QUOTE)) {
          Peek<Token> str_literal = parse_str_literal(stream, i);
          Statement *literal = new Statement();
          literal->name = '"' + str_literal.node.value + '"';
          literal->kind = StatementType::EXPRESSION;
          result.nodes.push_back(std::unique_ptr<Statement>(literal));
          i = str_literal.index;
          continue;
        }
      default:
        throw std::runtime_error("Unexpected token " + token.value);
    }

  }

  throw std::runtime_error("Unterminated function call");
}

FunctionCall::FunctionCall() {
  kind = StatementType::FUNCTION_CALL;
  name = get_statement_type_name(kind);
}

PeekPtr<FunctionCall> FunctionCall::build(std::vector<Token> stream, size_t index) {
  PeekPtr<FunctionCall> result;
  Token current = stream[index];

  result.index = index;
  result.node->name = current.value;
  result.node->kind = StatementType::FUNCTION_CALL;

  PeekStreamPtr<Statement> arguments = parse_arguments(stream, index + 1);
  result.index = arguments.index;
  result.node->arguments = std::move(arguments.nodes);

  return result;
}

void FunctionCall::print(size_t indentation) {
  std::string indentation_str = get_indentation(indentation);

  println(indentation_str + get_statement_type_name(kind) + " {");
  println(indentation_str + "  name: " + name);
  println(indentation_str + "  arguments: [");
  for (std::unique_ptr<Statement> &argument : arguments) {
    argument->print(indentation + 4);
  }
  println(indentation_str + "  ]");
  println(indentation_str + "}");
}