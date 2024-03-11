#include "Parser.h"
#include "Checker.h"

std::string replace(std::string str, std::string from, std::string to) {
  size_t start_pos = 0;
  while((start_pos = str.find(from, start_pos)) != std::string::npos) {
    str.replace(start_pos, from.length(), to);
    start_pos += to.length();
  }
  return str;
}

class JSTranspiler {
  enum class BuiltInFN {
    PRINT_LN,
  };

  static std::map<std::string, BuiltInFN> BUILT_IN_FN_KEY;
  static std::map<BuiltInFN, std::string> BUILT_IN_FN_NAME;
  static std::map<BinaryOperator, std::string> BOOL_OPERATOR_NAME;

  static bool is_built_in_fn(std::string fn_name) {
    return BUILT_IN_FN_KEY.count(fn_name) > 0;
  }

  static std::string get_built_in_fn(std::string fn_name) {
    if (is_built_in_fn(fn_name) == false) {
      throw std::runtime_error("DEV: Not a Built-In Function");
    }

    return BUILT_IN_FN_NAME.at(BUILT_IN_FN_KEY.at(fn_name));
  }

  static bool is_bool_operator(BinaryOperator op) {
    return BOOL_OPERATOR_NAME.count(op) > 0;
  }

  static std::string get_bool_operator(BinaryOperator op) {
    if (BOOL_OPERATOR_NAME.count(op) == 0) {
      throw std::runtime_error("DEV: Not a Boolean Operator");
    }

    return BOOL_OPERATOR_NAME.at(op);
  }

  static std::string get_block(StreamPtr<Statement> &statements, bool endl = true) {
    std::string block = "{\n";

    for (size_t i = 0; i < statements.size(); i++) {
      std::unique_ptr<Statement> &statement = statements[i];
      block += transpile_statement(statement);
    }

    block += "}";
    block += endl ? "\n" : "";

    return block;
  }

  static std::string get_str_prop_access(std::string value) {
    return replace(value, ":", ".");
  }

  static std::string get_str_value(std::string value, StreamPtr<Identifier> &injections) {
    std::string str;

    if (injections.empty()) {
      return '"' + value + '"';
    } else {
      str = '`' + value + '`';
    }

    for (size_t i = 0; i < injections.size(); i++) {
      std::unique_ptr<Identifier> &id = injections[i];
      str = replace(str, "$" + id->name, "${" + get_str_prop_access(id->name) + "}");
    }

    return str;
  }

  static std::string get_struct_value(StreamPtr<Field> &fields) {
    std::string line = "{\n";

    for (size_t i = 0; i < fields.size(); i++) {
      std::unique_ptr<Field> &field = fields[i];
      line += "  " + field->name + ": " + get_value(field->value) + ",\n";
    }

    line += "}";

    return line;
  }

  static std::string get_value(std::unique_ptr<Expression> &expression) {
    switch (expression->expression) {
      case ExpressionKind::BINARY_EXPRESSION: {
        BinaryExpression *binary_expression = static_cast<BinaryExpression *>(expression.get());
        std::string operator_str = binary_expression->operator_str;

        if (is_bool_operator(binary_expression->operation)) {
          operator_str = get_bool_operator(binary_expression->operation);
        }

        return get_value(binary_expression->left) + " " + operator_str + " " + get_value(binary_expression->right);
      }
      case ExpressionKind::IDENTIFIER: {
        Identifier *identifier = static_cast<Identifier *>(expression.get());
        return get_str_prop_access(identifier->path_str);
      }
      case ExpressionKind::LITERAL: {
        Value *value = static_cast<Value *>(expression.get());

        if (value->literal == Literal::STRING) {
          String *str = static_cast<String *>(value);
          return get_str_value(str->value, str->injections);
        }

        if (value->literal == Literal::STRUCT) {
          Struct *dictionary = static_cast<Struct *>(value);
          return get_struct_value(dictionary->fields);
        }

        if (value->literal == Literal::VECTOR) {
          return value->value;
        }

        return value->value;
      }
      case ExpressionKind::FN_CALL: {
        FunctionCall *fn_call = static_cast<FunctionCall *>(expression.get());
        return fn_call->name + get_fn_call_arguments(fn_call->arguments);
      }
      case ExpressionKind::VAR_REASSIGNMENT: {
        Reassignment *reassignment = static_cast<Reassignment *>(expression.get());
        return reassignment->identifier + " = " + get_value(reassignment->value);
      }
      default:
        throw std::runtime_error("Unsupported Value");
    }
  }

  static std::string get_fn_call_arguments(StreamPtr<Expression> &arguments) {
    std::string line;

    if (arguments.empty()) {
      return "()";
    }

    if (arguments.size() == 1) {
      return "(" + get_value(arguments[0]) + ")";
    }

    line = "(" + get_value(arguments[0]);

    for (size_t i = 1; i < arguments.size() - 1; i++) {
      line += ", " + get_value(arguments[i]);
    }

    line += ", " + get_value(arguments[arguments.size() - 1]) + ")";

    return line;
  }

  static std::string get_fn_declaration_parameters(StreamPtr<Variable> &parameters) {
    std::string line;

    if (parameters.empty()) {
      return "()";
    }

    if (parameters.size() == 1) {
      Variable *variable = parameters[0].get();
      return "(" + variable->name + ")";
    }

    line = "(" + parameters[0]->name;
    
    for (size_t i = 1; i < parameters.size() - 1; i++) {
      Variable *variable = parameters[i].get();
      line += ", " + variable->name;
    }
    
    line += ", " + parameters[parameters.size() - 1]->name + ")";

    return line;
  }

  static std::string get_fn_declaration(std::unique_ptr<Statement> &statement) {
    if (statement->kind != StatementKind::FN_DECLARATION) {
      throw std::runtime_error("DEV: Not a Function Statement");
    }

    Function *fn = static_cast<Function *>(statement.get());
    std::string line = "function " + fn->name + get_fn_declaration_parameters(fn->parameters) + " " + get_block(fn->children);

    return line;
  }

  static std::string get_if_statement(std::unique_ptr<Statement> &statement) {
    if (statement->kind != StatementKind::IF_STATEMENT) {
      throw std::runtime_error("DEV: Not an If Statement");
    }

    IFStatement *if_statement = static_cast<IFStatement *>(statement.get());
    bool has_else = if_statement->else_block != nullptr;
    std::string line = "if (" + get_value(if_statement->condition) + ") " + get_block(if_statement->children, has_else == false);

    if (if_statement->else_block != nullptr) {
      line += " else " + get_block(if_statement->else_block->children);
    }

    return line;
  }

  static std::string get_loop_statement(std::unique_ptr<Statement> &statement) {
    if (statement->kind != StatementKind::LOOP_STATEMENT) {
      throw std::runtime_error("DEV: Not a Loop Statement");
    }

    Loop *loop = static_cast<Loop *>(statement.get());
    std::string line;

    if (loop->loop_type == LoopType::TIMES_LOOP) {
      line = "for (let i = 0; i < " + get_value(loop->limit) + "; i++) " + get_block(loop->children);
    } else {
      Identifier *index = static_cast<Identifier *>(loop->index.get());

      if (loop->limit->expression == ExpressionKind::LITERAL) {
        Value *value = static_cast<Value *>(loop->limit.get());

        if (value->literal == Literal::VECTOR) {
          Vector *vector = static_cast<Vector *>(value);
          line = "for (let " + index->name +  " of " + vector->value + ")" + get_block(loop->children);
          return line;
        }
      }

      line = "for (let " + index->name + " = 0; " + index->name + " < " + get_value(loop->limit) + "; " + index->name + "++) " + get_block(loop->children);
    }

    return line;
  }

  static std::string get_vector_init_block(std::unique_ptr<Expression> &node, std::string id) {
    std::string line;

    Value *value = static_cast<Value *>(node.get());

    if (value->literal != Literal::VECTOR) return line;

    Vector *vector = static_cast<Vector *>(value);

    if (vector->len != nullptr && vector->init != nullptr) {
      line += "for (let it = 0; it < " + get_value(vector->len) + "; it++) " + id + "[it] = " + get_value(vector->init) + ";\n";
    }

    return line;
  }

  static std::string get_variable_statement(std::unique_ptr<Statement> &statement) {
    if (
      statement->kind != StatementKind::VAL_DECLARATION && 
      statement->kind != StatementKind::VAR_DECLARATION
    ) {
      throw std::runtime_error("DEV: Not a Variable Statement");
    }
    
    Variable *variable = static_cast<Variable *>(statement.get());
    std::string keyword = statement->kind == StatementKind::VAL_DECLARATION ? "const" : "let";
    std::string line = keyword + " " + variable->name + " = " + get_value(variable->value) + ";\n";

    if (variable->value->expression == ExpressionKind::LITERAL) {
      line += get_vector_init_block(variable->value, variable->name);
    }

    return line;      
  }

  static std::string get_return_statement(std::unique_ptr<Statement> &statement) {
    if (statement->kind != StatementKind::RETURN_STATEMENT) {
      throw std::runtime_error("DEV: Not a Return Statement");
    }

    ReturnStatement *return_statement = static_cast<ReturnStatement *>(statement.get());

    if (return_statement->value == nullptr) {
      return "return;\n";
    }

    if (return_statement->value->expression == ExpressionKind::LITERAL) {
      Value *value = static_cast<Value *>(return_statement->value.get());

      if (value->literal == Literal::VECTOR) {
        std::string line = "const temp_arr = [];\n";
        line += get_vector_init_block(return_statement->value, "temp_arr");
        return line + "return temp_arr;\n";
      }

      return "return " + get_value(return_statement->value) + ";\n";
    }

    return "return " + get_value(return_statement->value) + ";\n";
  }

  static std::string transpile_expression(std::unique_ptr<Statement> &statement) {
    if (statement->kind != StatementKind::EXPRESSION) {
      throw std::runtime_error("DEV: Not an Expression Statement");
    }

    Expression *expression = static_cast<Expression *>(statement.get());
    std::string line;

    switch (expression->expression) {
      case ExpressionKind::FN_CALL: {
        FunctionCall *fn_call = static_cast<FunctionCall *>(expression);

        if (is_built_in_fn(fn_call->name)) {
          line = get_built_in_fn(fn_call->name) + get_fn_call_arguments(fn_call->arguments) + ";\n";
          break;
        }

        line = fn_call->name + get_fn_call_arguments(fn_call->arguments) + ";\n";
        break;
      }
      case ExpressionKind::VAR_REASSIGNMENT: {
        Reassignment *reassignment = static_cast<Reassignment *>(expression);
        line = reassignment->identifier + " = " + get_value(reassignment->value) + ";\n";
        break;
      }
      default:
        println("Unsupported Expression");
        return "";
    }

    return line;
  }

  static std::string transpile_statement(std::unique_ptr<Statement> &statement) {
    switch (statement->kind) {
      case StatementKind::EXPRESSION:
        return transpile_expression(statement);
      case StatementKind::FN_DECLARATION:
        return get_fn_declaration(statement);
      case StatementKind::IF_STATEMENT:
        return get_if_statement(statement);
      case StatementKind::LOOP_STATEMENT:
        return get_loop_statement(statement);
      case StatementKind::RETURN_STATEMENT:
        return get_return_statement(statement);
      case StatementKind::VAL_DECLARATION:
      case StatementKind::VAR_DECLARATION:
        return get_variable_statement(statement);
      case StatementKind::STRUCT_DEFINITION:
        // JavaScript does not support Structs
        return "";
      default:
        println("Unsupported Statement");
        return "";
    }
  }

  public:
    static void transpile(std::string file_name, std::string file_output) {
      Statement program = Parser::parse_file(file_name);
      Checker checker;
      std::string output;

      program = checker.check(program);

      for (size_t i = 0; i < program.children.size(); i++) {
        std::unique_ptr<Statement> &statement = program.children[i];
        output += transpile_statement(statement);
      }

      std::ofstream file;
      file.open(file_output);
      file << output;
      file.close();
    }
};

std::map<BinaryOperator, std::string> JSTranspiler::BOOL_OPERATOR_NAME = {
  {BinaryOperator::AND, "&&"},
  {BinaryOperator::OR, "||"},
  {BinaryOperator::NOT_EQUAL, "!=="},
  {BinaryOperator::LESS_THAN, "<"},
  {BinaryOperator::GREATER_THAN, ">"},
};

std::map<std::string, JSTranspiler::BuiltInFN> JSTranspiler::BUILT_IN_FN_KEY = {
  {"println", JSTranspiler::BuiltInFN::PRINT_LN},
};

std::map<JSTranspiler::BuiltInFN, std::string> JSTranspiler::BUILT_IN_FN_NAME = {
  {JSTranspiler::BuiltInFN::PRINT_LN, "console.log"},
};