#pragma once

#include "Expression.h"
#include "Statement.h"
#include "Variable.h"

class Checker {
  struct Entity {
    std::string name;
    std::string typing;
    std::string kind;
  };

  std::map<std::string, Entity> entities;

  bool is_declared_entity(std::string identifier) {
    return entities.find(identifier) != entities.end();
  }

  void check_var_reassignment(Expression *expression) {
    if (expression->expression != ExpressionKind::VAR_REASSIGNMENT) {
      throw std::runtime_error("DEV: Not a Reassignment Expression.");
    };

    Reassignment *reassignment = static_cast<Reassignment *>(expression);
    Entity entity = entities[reassignment->identifier];

    if (entity.kind == "Constant") {
      println("ERROR: Reassignment of Constant '" + reassignment->identifier + "'.");
      throw std::runtime_error("Variable Reassignment Error.");
    }

    if (is_declared_entity(reassignment->identifier) == false) {
      println("ERROR: Cannot reassign undeclared variable '" + reassignment->identifier + "'.");
      throw std::runtime_error("Variable Reassignment Error.");
    }

    Value *value = static_cast<Value *>(reassignment->value.get());

    std::string typing;

    if (
      value->expression == ExpressionKind::FN_CALL || 
      value->expression == ExpressionKind::IDENTIFIER
    ) {
      typing = "void";
    } else {
      typing = infer_typing(value->literal);
    }
    
    if (typing == "void") {
      println("WARNING: Assigning 'void' to variable '" + reassignment->identifier + "'.");
      println("- Function Calls, Identifiers, Structs and Vectors are handled as 'void'.");
      println("- Please make sure you are assigning the correct type to the variable.");
      println("- This will be fixed in the future.");
      println();
    } else if (entity.typing != typing) {
      println("ERROR: Cannot reassign variable '" + reassignment->identifier + "' of type '" + entity.typing + "' with value of type '" + typing + "'.");
      println();
      throw std::runtime_error("Variable Reassignment Error.");
    }
  }

  void create_entity(std::string identifier, std::string type, std::string kind) {
    if (is_declared_entity(identifier)) {
      Entity entity = entities[identifier];
      println("ERROR: " + entity.kind + " '" + identifier + "' has been already declared.");
      throw std::runtime_error("Variable Assignment Error.");
    }

    Entity entity;
    entity.name = identifier;
    entity.typing = type;
    entity.kind = kind;
    entities[identifier] = entity;
  }

  public:
    Statement check(Statement &input) {
      Statement program;

      for (size_t index = 0; index < input.children.size(); index++) {
        std::unique_ptr<Statement> child = std::move(input.children[index]);

        switch (child->kind) {
          case StatementKind::EXPRESSION: {
            Expression *expression = static_cast<Expression *>(child.get());
            if (expression->expression == ExpressionKind::VAR_REASSIGNMENT) {
              check_var_reassignment(expression);
            }
          } break;
          case StatementKind::STRUCT_DEFINITION: {
            StructDefinition *definition = static_cast<StructDefinition *>(child.get());
            create_entity(definition->name, definition->name, "Struct");
          } break;
          case StatementKind::VAL_DECLARATION:
          case StatementKind::VAR_DECLARATION: {
            Variable *variable = static_cast<Variable *>(child.get());
            std::string kind = child->kind == StatementKind::VAL_DECLARATION ? "Constant" : "Variable";
            create_entity(variable->name, variable->typing, kind);
          } break;
          default:
            break;
        }

        program.children.push_back(std::move(child));
      }

      return program;
    } 
};