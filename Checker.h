#pragma once

#include "Expression.h"
#include "Statement.h"
#include "Variable.h"

class Checker {
  struct Entity {
    std::string name;
    std::string type;
  };

  std::map<std::string, Entity> entities;

  bool is_declared_variable(std::string identifier) {
    return entities.find(identifier) != entities.end();
  }

  void check_var_reassignment(Expression *expression) {
    if (expression->expression != ExpressionKind::VAR_REASSIGNMENT) {
      throw std::runtime_error("DEV: Not a Reassignment Expression.");
    };

    Reassignment *reassignment = static_cast<Reassignment *>(expression);

    if (is_declared_variable(reassignment->identifier)) return;

    println("ERROR: Cannot reassign undeclared variable '" + reassignment->identifier + "'.");
    throw std::runtime_error("Variable Reassignment Error.");
  }

  void create_entity(std::string identifier, std::string type) {
    Entity entity;
    entity.name = identifier;
    entity.type = type;
    entities[identifier] = entity;
  }

  public:
    Statement check(Statement &input) {
      Statement program;

      for (size_t index = 0; index < input.children.size(); index++) {
        std::unique_ptr<Statement> child = std::move(input.children[index]);

        if (child->kind == StatementKind::VAR_DECLARATION) {
          Variable *variable = static_cast<Variable *>(child.get());
          create_entity(variable->name, variable->typing);
        }

        if (child->kind == StatementKind::EXPRESSION) {
          Expression *expression = static_cast<Expression *>(child.get());
          if (expression->expression == ExpressionKind::VAR_REASSIGNMENT) {
            check_var_reassignment(expression);
          }
        }

        program.children.push_back(std::move(child));
      }

      return program;
    } 
};