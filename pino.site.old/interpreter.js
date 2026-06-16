// Pino Lang Client-side Interpreter in JavaScript
// Implements Tokenizer, Parser, and Tree-walk Evaluator to run Pino programs directly in browser.

class Token {
  constructor(type, value, line) {
    this.type = type;
    this.value = value;
    this.line = line;
  }
}

const TokenType = {
  EOF: 'EOF',
  NUMBER: 'NUMBER',
  STRING: 'STRING',
  IDENTIFIER: 'IDENTIFIER',
  KEYWORD: 'KEYWORD',
  OPERATOR: 'OPERATOR',
  DELIMITER: 'DELIMITER'
};

class Lexer {
  constructor(source) {
    this.source = source;
    this.index = 0;
    this.line = 1;
    this.tokens = [];
    this.keywords = new Set([
      'var', 'val', 'fn', 'struct', 'interface', 'enum', 'if', 'else', 'match', 'when', 'for', 'in', 'break', 'continue', 'return', 'true', 'false', 'then',
      'module', 'import', 'from', 'pub', 'static'
    ]);
  }

  tokenize() {
    while (this.index < this.source.length) {
      const char = this.source[this.index];

      // Newlines and Whitespace
      if (char === '\n') {
        this.line++;
        this.index++;
        continue;
      }
      if (/\s/.test(char)) {
        this.index++;
        continue;
      }

      // Comments (#)
      if (char === '#') {
        while (this.index < this.source.length && this.source[this.index] !== '\n') {
          this.index++;
        }
        continue;
      }

      // Numbers
      if (/\d/.test(char)) {
        this.tokens.push(this.readNumber());
        continue;
      }

      // Strings with interpolation ($var or $(expr))
      if (char === '"') {
        this.readString();
        continue;
      }

      // Identifiers & Keywords
      if (/[a-zA-Z_]/.test(char)) {
        const id = this.readIdentifier();
        if (this.keywords.has(id)) {
          this.tokens.push(new Token(TokenType.KEYWORD, id, this.line));
        } else {
          this.tokens.push(new Token(TokenType.IDENTIFIER, id, this.line));
        }
        continue;
      }

      if (this.matchAhead('+=') || this.matchAhead('-=') || this.matchAhead('*=') || this.matchAhead('/=') || this.matchAhead('%=') ||
        this.matchAhead('==') || this.matchAhead('!=') || this.matchAhead('<=') || this.matchAhead('>=') || this.matchAhead('::') || this.matchAhead('=>')) {
        const op = this.source.slice(this.index, this.index + 2);
        this.tokens.push(new Token(TokenType.OPERATOR, op, this.line));
        this.index += 2;
        continue;
      }

      if (char === '=' || char === '+' || char === '-' || char === '*' || char === '/' || char === '%' || char === '<' || char === '>' || char === ':') {
        this.tokens.push(new Token(TokenType.OPERATOR, char, this.line));
        this.index++;
        continue;
      }

      if (char === '{' || char === '}' || char === '(' || char === ')' || char === '[' || char === ']' || char === ',') {
        this.tokens.push(new Token(TokenType.DELIMITER, char, this.line));
        this.index++;
        continue;
      }

      throw new Error(`Lexical Error: Unexpected character '${char}' at line ${this.line}`);
    }

    this.tokens.push(new Token(TokenType.EOF, '', this.line));
    return this.tokens;
  }

  matchAhead(str) {
    return this.source.slice(this.index, this.index + str.length) === str;
  }

  readNumber() {
    let result = '';
    let hasDot = false;
    while (this.index < this.source.length) {
      const char = this.source[this.index];
      if (char === '_') {
        this.index++;
        continue;
      }
      if (char === '.') {
        if (hasDot) break;
        hasDot = true;
        result += '.';
        this.index++;
        continue;
      }
      if (/\d/.test(char)) {
        result += char;
        this.index++;
      } else {
        break;
      }
    }
    return new Token(TokenType.NUMBER, result, this.line);
  }

  readString() {
    this.index++; // skip opening double quote
    let currentPart = '';
    let first = true;

    const pushLiteral = () => {
      if (currentPart.length > 0 || first) {
        if (!first) {
          this.tokens.push(new Token(TokenType.OPERATOR, '+', this.line));
        }
        this.tokens.push(new Token(TokenType.STRING, currentPart, this.line));
        currentPart = '';
        first = false;
      }
    };

    while (this.index < this.source.length) {
      const char = this.source[this.index];
      if (char === '"') {
        this.index++; // skip closing quote
        pushLiteral();
        return;
      }

      if (char === '$') {
        // We have interpolation
        pushLiteral();
        this.index++; // skip '$'

        if (this.source[this.index] === '(') {
          this.index++; // skip '('
          let parenCount = 1;
          let exprSource = '';
          while (this.index < this.source.length && parenCount > 0) {
            const nextChar = this.source[this.index];
            if (nextChar === '(') parenCount++;
            if (nextChar === ')') parenCount--;
            if (parenCount > 0) {
              exprSource += nextChar;
            }
            this.index++;
          }
          // Tokenize the inner expression recursively and wrap in parenthesis tokens
          const subLexer = new Lexer(exprSource);
          const subTokens = subLexer.tokenize();
          subTokens.pop(); // remove sub-lexer EOF token

          if (!first) {
            this.tokens.push(new Token(TokenType.OPERATOR, '+', this.line));
          }
          this.tokens.push(new Token(TokenType.DELIMITER, '(', this.line));
          this.tokens.push(...subTokens);
          this.tokens.push(new Token(TokenType.DELIMITER, ')', this.line));
          first = false;
        } else if (/[a-zA-Z_]/.test(this.source[this.index])) {
          const id = this.readIdentifier();
          if (!first) {
            this.tokens.push(new Token(TokenType.OPERATOR, '+', this.line));
          }
          this.tokens.push(new Token(TokenType.IDENTIFIER, id, this.line));
          first = false;
        } else {
          currentPart += '$';
        }
        continue;
      }

      currentPart += char;
      this.index++;
    }

    throw new Error(`Lexical Error: Unterminated string literal at line ${this.line}`);
  }

  readIdentifier() {
    let result = '';
    while (this.index < this.source.length) {
      const char = this.source[this.index];
      if (/[a-zA-Z0-9_]/.test(char)) {
        result += char;
        this.index++;
      } else {
        break;
      }
    }
    return result;
  }
}

// AST Nodes
class Stmt { }
class Expr { }

class VarDecl extends Stmt {
  constructor(name, valueExpr, isConstant, isPublic = false) {
    super();
    this.name = name;
    this.valueExpr = valueExpr;
    this.isConstant = isConstant;
    this.isPublic = isPublic;
  }
}

class Block extends Stmt {
  constructor(statements) {
    super();
    this.statements = statements;
  }
}

class IfStmt extends Stmt {
  constructor(condition, thenBranch, elseIfs, elseBranch) {
    super();
    this.condition = condition;
    this.thenBranch = thenBranch;
    this.elseIfs = elseIfs; // array of {cond, body}
    this.elseBranch = elseBranch;
  }
}

class ForStmt extends Stmt {
  constructor(varName, iterableExpr, body, isInfinite) {
    super();
    this.varName = varName; // "it" or custom variable name
    this.iterableExpr = iterableExpr;
    this.body = body;
    this.isInfinite = isInfinite;
  }
}

class MatchStmt extends Stmt {
  constructor(condition, branches, alternate) {
    super();
    this.condition = condition;
    this.branches = branches; // array of {conditions[], body}
    this.alternate = alternate; // body for else block
  }
}

class StructDecl extends Stmt {
  constructor(name, fields, methods, inheritedStructs = [], isPublic = false) {
    super();
    this.name = name;
    this.fields = fields; // array of {name, type}
    this.methods = methods; // array of FnDecl
    this.inheritedStructs = inheritedStructs; // array of strings
    this.isPublic = isPublic;
  }
}

class InterfaceDecl extends Stmt {
  constructor(name, methods, isPublic = false) {
    super();
    this.name = name;
    this.methods = methods; // array of FnDecl
    this.isPublic = isPublic;
  }
}

class EnumDecl extends Stmt {
  constructor(name, members, isPublic = false) {
    super();
    this.name = name;
    this.members = members; // array of string names
    this.isPublic = isPublic;
  }
}

class FnDecl extends Stmt {
  constructor(name, params, body, returnType = "", isPublic = false) {
    super();
    this.name = name;
    this.params = params; // array of {name, type}
    this.body = body;
    this.returnType = returnType;
    this.isStatic = false;
    this.isPublic = isPublic;
  }
}

class ModuleDecl extends Stmt {
  constructor(name) {
    super();
    this.name = name;
  }
}

class ImportStmt extends Stmt {
  constructor(moduleName) {
    super();
    this.moduleName = moduleName;
  }
}

class FromImportStmt extends Stmt {
  constructor(moduleName, imports) {
    super();
    this.moduleName = moduleName;
    this.imports = imports;
  }
}

class ExprStmt extends Stmt {
  constructor(expression) {
    super();
    this.expression = expression;
  }
}

class ReturnStmt extends Stmt {
  constructor(argument) {
    super();
    this.argument = argument;
  }
}

class BreakStmt extends Stmt {
  constructor() { super(); }
}

class ContinueStmt extends Stmt {
  constructor() { super(); }
}

class LiteralExpr extends Expr {
  constructor(value, type) {
    super();
    this.value = value;
    this.type = type; // 'NUMBER', 'STRING', 'BOOLEAN', 'NULL'
  }
}

class IdentifierExpr extends Expr {
  constructor(name) {
    super();
    this.name = name;
  }
}

class BinaryExpr extends Expr {
  constructor(left, operator, right) {
    super();
    this.left = left;
    this.operator = operator;
    this.right = right;
  }
}

class UnaryExpr extends Expr {
  constructor(operator, right) {
    super();
    this.operator = operator;
    this.right = right;
  }
}

class TernaryExpr extends Expr {
  constructor(condition, consequent, alternate) {
    super();
    this.condition = condition;
    this.consequent = consequent;
    this.alternate = alternate;
  }
}

class CallExpr extends Expr {
  constructor(callee, args) {
    super();
    this.callee = callee; // Identifier expression or MemberAccess
    this.args = args;
  }
}

class StructInstanceExpr extends Expr {
  constructor(structName, initializers) {
    super();
    this.structName = structName;
    this.initializers = initializers; // Map/object of key -> Expr
  }
}

class VectorExpr extends Expr {
  constructor(elements, lenExpr, initExpr, typing = "") {
    super();
    this.elements = elements; // array of Expr (or null)
    this.lenExpr = lenExpr;     // Expr for size (or null)
    this.initExpr = initExpr;   // Expr for values (or null)
    this.typing = typing;
  }
}

class FunctionLambdaExpression extends Expr {
  constructor(parameters, body) {
    super();
    this.parameters = parameters; // array of {name, type}
    this.body = body; // Block
  }
}

class IndexAccessExpr extends Expr {
  constructor(target, index) {
    super();
    this.target = target;
    this.index = index;
  }
}

class MapExpr extends Expr {
  constructor(keyType, valType, entries) {
    super();
    this.keyType = keyType;
    this.valType = valType;
    this.entries = entries; // array of {key, value}
  }
}

// Precedence-Climbing Parser
class Parser {
  constructor(tokens) {
    this.tokens = tokens;
    this.index = 0;
    this.scopes = [];
  }

  isStructBlock(startIndex) {
    if (startIndex >= this.tokens.length) return false;
    const braceToken = this.tokens[startIndex];
    if (braceToken.type !== TokenType.DELIMITER || braceToken.value !== '{') return false;

    let nested = 0;
    let idx = startIndex;
    let hasPropInit = false;

    while (idx < this.tokens.length) {
      const tok = this.tokens[idx];
      if (tok.type === TokenType.DELIMITER && tok.value === '{') {
        nested++;
      } else if (tok.type === TokenType.DELIMITER && tok.value === '}') {
        nested--;
        if (nested === 0) {
          break;
        }
      } else if (nested === 1) {
        if (tok.type === TokenType.KEYWORD) {
          return false;
        }
        if (tok.type === TokenType.OPERATOR) {
          const val = tok.value;
          if (val === '=' || val === '+=' || val === '-=' || val === '*=' || val === '/=' || val === '%=') {
            return false;
          }
        }
        if (tok.type === TokenType.IDENTIFIER) {
          const nextTok = this.tokens[idx + 1];
          if (nextTok && nextTok.type === TokenType.OPERATOR && nextTok.value === ':') {
            hasPropInit = true;
          }
        }
      }
      idx++;
    }
    return hasPropInit;
  }

  pushScope() {
    this.scopes.push(new Set());
  }

  popScope() {
    if (this.scopes.length > 0) {
      this.scopes.pop();
    }
  }

  declareVariable(name) {
    if (this.scopes.length > 0) {
      this.scopes[this.scopes.length - 1].add(name);
    }
  }

  isDeclared(name) {
    for (let i = this.scopes.length - 1; i >= 0; i--) {
      if (this.scopes[i].has(name)) return true;
    }
    return false;
  }

  containsUndeclaredIt(expr) {
    if (!expr) return false;
    if (expr instanceof IdentifierExpr) {
      return expr.name === 'it' && !this.isDeclared('it');
    }
    if (expr instanceof BinaryExpr) {
      return this.containsUndeclaredIt(expr.left) || this.containsUndeclaredIt(expr.right);
    }
    if (expr instanceof UnaryExpr) {
      return this.containsUndeclaredIt(expr.right);
    }
    if (expr instanceof TernaryExpr) {
      return this.containsUndeclaredIt(expr.condition) ||
        this.containsUndeclaredIt(expr.consequent) ||
        this.containsUndeclaredIt(expr.alternate);
    }
    if (expr instanceof CallExpr) {
      for (const arg of expr.args) {
        if (this.containsUndeclaredIt(arg)) return true;
      }
      return false;
    }
    if (expr instanceof VectorExpr) {
      if (expr.elements) {
        for (const el of expr.elements) {
          if (this.containsUndeclaredIt(el)) return true;
        }
      }
      if (expr.lenExpr && this.containsUndeclaredIt(expr.lenExpr)) return true;
      if (expr.initExpr && this.containsUndeclaredIt(expr.initExpr)) return true;
      return false;
    }
    if (expr instanceof StructInstanceExpr) {
      for (const key in expr.initializers) {
        if (this.containsUndeclaredIt(expr.initializers[key])) return true;
      }
      return false;
    }
    if (expr instanceof FunctionLambdaExpression) {
      return false;
    }
    if (expr instanceof IndexAccessExpr) {
      return this.containsUndeclaredIt(expr.target) || this.containsUndeclaredIt(expr.index);
    }
    if (expr instanceof MapExpr) {
      for (const entry of expr.entries) {
        if (this.containsUndeclaredIt(entry.key) || this.containsUndeclaredIt(entry.value)) return true;
      }
      return false;
    }
    return false;
  }

  peek() {
    return this.tokens[this.index];
  }

  previous() {
    return this.tokens[this.index - 1];
  }

  isAtEnd() {
    return this.peek().type === TokenType.EOF;
  }

  advance() {
    if (!this.isAtEnd()) this.index++;
    return this.previous();
  }

  check(type, val = null) {
    if (this.isAtEnd()) return false;
    const token = this.peek();
    if (token.type !== type) return false;
    if (val !== null && token.value !== val) return false;
    return true;
  }

  match(type, val = null) {
    if (this.check(type, val)) {
      this.advance();
      return true;
    }
    return false;
  }

  consume(type, message, val = null) {
    if (this.check(type, val)) return this.advance();
    throw new Error(`Parse Error: ${message} (Line ${this.peek().line}, found '${this.peek().value}')`);
  }

  consumeTyping() {
    if (this.match(TokenType.IDENTIFIER, 'map')) {
      this.consume(TokenType.DELIMITER, "Expect '[' after 'map' in type signature", '[');
      const keyType = this.consumeTyping();
      this.consume(TokenType.DELIMITER, "Expect ',' between map types in type signature", ',');
      const valType = this.consumeTyping();
      this.consume(TokenType.DELIMITER, "Expect ']' after map types in type signature", ']');
      return `map[${keyType}, ${valType}]`;
    }

    if (this.match(TokenType.DELIMITER, '[')) {
      this.consume(TokenType.DELIMITER, "Expect ']' for array type", ']');
      const elemType = this.consumeTyping();
      return "[]" + elemType;
    }

    if (this.match(TokenType.KEYWORD, 'fn')) {
      this.consume(TokenType.DELIMITER, "Expect '(' for function type", '(');
      const paramTypes = [];
      while (!this.check(TokenType.DELIMITER, ')') && !this.isAtEnd()) {
        paramTypes.push(this.consumeTyping());
        this.match(TokenType.DELIMITER, ',');
      }
      this.consume(TokenType.DELIMITER, "Expect ')' after function type parameters", ')');

      let returnType = " any";
      if (this.check(TokenType.DELIMITER, '[') ||
        this.check(TokenType.KEYWORD, 'fn') ||
        this.check(TokenType.IDENTIFIER)) {
        returnType = " " + this.consumeTyping();
      }

      return `fn(${paramTypes.join(', ')})${returnType}`;
    }

    const typeToken = this.consume(TokenType.IDENTIFIER, "Expect type identifier");
    return typeToken.value;
  }

  parse() {
    this.pushScope();
    const statements = [];
    let first = true;
    while (!this.isAtEnd()) {
      try {
        const stmt = this.statement();
        if (stmt) {
          if (stmt instanceof ModuleDecl && !first) {
            throw new Error("Parse Error: 'module' declaration must be the first statement in the file");
          }
          statements.push(stmt);
          first = false;
        }
      } catch (err) {
        this.popScope();
        throw err;
      }
    }
    this.popScope();
    return statements;
  }

  statement() {
    let isPublic = false;
    if (this.match(TokenType.KEYWORD, 'pub')) {
      isPublic = true;
    }

    if (this.match(TokenType.KEYWORD, 'val')) return this.varDeclaration(true, isPublic);
    if (this.match(TokenType.KEYWORD, 'var')) return this.varDeclaration(false, isPublic);
    if (this.match(TokenType.KEYWORD, 'struct')) return this.structDeclaration(isPublic);
    if (this.match(TokenType.KEYWORD, 'interface')) return this.interfaceDeclaration(isPublic);
    if (this.match(TokenType.KEYWORD, 'enum')) return this.enumDeclaration(isPublic);
    if (this.match(TokenType.KEYWORD, 'fn')) return this.fnDeclaration(isPublic);
    if (this.match(TokenType.KEYWORD, 'static')) {
      throw new Error("Parse Error: 'static' modifier is only valid inside struct definitions.");
    }

    if (isPublic) {
      throw new Error("Parse Error: 'pub' can only prefix declarations (var, val, fn, struct, interface, enum)");
    }

    if (this.match(TokenType.KEYWORD, 'module')) {
      const nameToken = this.consume(TokenType.IDENTIFIER, "Expect module name");
      return new ModuleDecl(nameToken.value);
    }
    if (this.match(TokenType.KEYWORD, 'import')) {
      const nameToken = this.consume(TokenType.IDENTIFIER, "Expect module name");
      this.declareVariable(nameToken.value);
      return new ImportStmt(nameToken.value);
    }
    if (this.match(TokenType.KEYWORD, 'from')) {
      const moduleToken = this.consume(TokenType.IDENTIFIER, "Expect module name");
      this.consume(TokenType.KEYWORD, "Expect 'import' after module name", 'import');
      const imports = [];
      while (true) {
        const impToken = this.consume(TokenType.IDENTIFIER, "Expect imported member name");
        imports.push(impToken.value);
        this.declareVariable(impToken.value);
        if (this.match(TokenType.DELIMITER, ',')) {
          continue;
        }
        break;
      }
      return new FromImportStmt(moduleToken.value, imports);
    }

    if (this.match(TokenType.KEYWORD, 'if')) return this.ifStatement();
    if (this.match(TokenType.KEYWORD, 'for')) return this.forStatement();
    if (this.match(TokenType.KEYWORD, 'match')) return this.matchStatement();
    if (this.match(TokenType.KEYWORD, 'return')) return this.returnStatement();
    if (this.match(TokenType.KEYWORD, 'break')) return new BreakStmt();
    if (this.match(TokenType.KEYWORD, 'continue')) return new ContinueStmt();

    return this.expressionStatement();
  }

  varDeclaration(isConstant, isPublic = false) {
    const nameToken = this.consume(TokenType.IDENTIFIER, "Expect variable name");
    this.consume(TokenType.OPERATOR, "Expect '=' after variable name", '=');
    const valueExpr = this.expression();
    this.declareVariable(nameToken.value);
    return new VarDecl(nameToken.value, valueExpr, isConstant, isPublic);
  }

  ifStatement() {
    // Condition (parentheses are optional in Pino)
    const condition = this.expression(false);
    this.consume(TokenType.DELIMITER, "Expect '{' before then branch body", '{');
    const thenBranch = this.block();

    const elseIfs = [];
    let elseBranch = null;

    while (this.match(TokenType.KEYWORD, 'else')) {
      if (this.match(TokenType.KEYWORD, 'if')) {
        const cond = this.expression(false);
        this.consume(TokenType.DELIMITER, "Expect '{' before else-if body", '{');
        const body = this.block();
        elseIfs.push({ cond, body });
      } else {
        this.consume(TokenType.DELIMITER, "Expect '{' before else body", '{');
        elseBranch = this.block();
        break;
      }
    }

    return new IfStmt(condition, thenBranch, elseIfs, elseBranch);
  }

  block() {
    this.pushScope();
    const statements = [];
    while (!this.check(TokenType.DELIMITER, '}') && !this.isAtEnd()) {
      statements.push(this.statement());
    }
    this.consume(TokenType.DELIMITER, "Expect '}' after block", '}');
    this.popScope();
    return new Block(statements);
  }

  forStatement() {
    // Check if infinite loop: for { ... }
    if (this.check(TokenType.DELIMITER, '{')) {
      this.advance();
      const body = this.block();
      return new ForStmt('it', null, body, true);
    }

    // Otherwise, it could be for time in 5 { ... } or for item in vector { ... }
    const varToken = this.consume(TokenType.IDENTIFIER, "Expect iterator variable name");
    this.consume(TokenType.KEYWORD, "Expect 'in' after iterator variable", 'in');
    const iterableExpr = this.expression(false);
    this.pushScope();
    this.declareVariable(varToken.value);
    this.consume(TokenType.DELIMITER, "Expect '{' before loop body", '{');
    const body = this.block();
    this.popScope();
    return new ForStmt(varToken.value, iterableExpr, body, false);
  }

  matchStatement() {
    const condition = this.expression(false);
    this.consume(TokenType.DELIMITER, "Expect '{' to open match statement", '{');
    const branches = [];
    let alternate = null;

    while (!this.check(TokenType.DELIMITER, '}') && !this.isAtEnd()) {
      if (this.match(TokenType.KEYWORD, 'when')) {
        const conditions = [];
        do {
          conditions.push(this.expression(false));
        } while (this.match(TokenType.DELIMITER, ','));

        this.consume(TokenType.DELIMITER, "Expect '{' after when conditions", '{');
        const body = this.block();
        branches.push({ conditions, body });
      } else if (this.match(TokenType.KEYWORD, 'else')) {
        this.consume(TokenType.DELIMITER, "Expect '{' after else", '{');
        alternate = this.block();
      } else {
        throw new Error(`Parse Error: Expect 'when' or 'else' in match body.`);
      }
    }

    this.consume(TokenType.DELIMITER, "Expect '}' to close match statement", '}');
    return new MatchStmt(condition, branches, alternate);
  }

  structDeclaration(isPublic = false) {
    const nameToken = this.consume(TokenType.IDENTIFIER, "Expect struct name");
    this.consume(TokenType.DELIMITER, "Expect '{' after struct name", '{');

    const fields = [];
    const methods = [];
    const inheritedStructs = [];

    while (!this.check(TokenType.DELIMITER, '}') && !this.isAtEnd()) {
      let isStatic = false;
      if (this.match(TokenType.KEYWORD, 'static')) {
        isStatic = true;
        if (!this.match(TokenType.KEYWORD, 'fn')) {
          throw new Error("Parse Error: 'static' keyword can only modify function declarations inside structs.");
        }
      }

      if (isStatic || this.match(TokenType.KEYWORD, 'fn')) {
        const fn = this.fnDeclaration();
        fn.isStatic = isStatic;
        methods.push(fn);
      } else {
        const fieldNameToken = this.consume(TokenType.IDENTIFIER, "Expect field name");
        const fieldName = fieldNameToken.value;
        if (fieldName.length > 0 && fieldName[0] === fieldName[0].toUpperCase()) {
          inheritedStructs.push(fieldName);
        } else {
          const fieldType = this.consumeTyping();
          fields.push({ name: fieldName, type: fieldType });
        }
        // Optional commas between fields
        this.match(TokenType.DELIMITER, ',');
      }
    }

    this.consume(TokenType.DELIMITER, "Expect '}' to close struct declaration", '}');
    return new StructDecl(nameToken.value, fields, methods, inheritedStructs, isPublic);
  }

  interfaceDeclaration(isPublic = false) {
    const nameToken = this.consume(TokenType.IDENTIFIER, "Expect interface name");
    this.consume(TokenType.DELIMITER, "Expect '{' after interface name", '{');

    const methods = [];
    while (!this.check(TokenType.DELIMITER, '}') && !this.isAtEnd()) {
      if (this.match(TokenType.KEYWORD, 'fn')) {
        methods.push(this.interfaceMethodSignature());
      } else {
        throw new Error(`Parse Error: Expected method signature in interface body, found '${this.peek().value}'`);
      }
      this.match(TokenType.DELIMITER, ',');
    }

    this.consume(TokenType.DELIMITER, "Expect '}' to close interface declaration", '}');
    return new InterfaceDecl(nameToken.value, methods, isPublic);
  }

  interfaceMethodSignature() {
    const nameToken = this.consume(TokenType.IDENTIFIER, "Expect method name");
    this.consume(TokenType.DELIMITER, "Expect '(' after method name", '(');
    const params = [];
    if (!this.check(TokenType.DELIMITER, ')')) {
      do {
        const paramName = this.consume(TokenType.IDENTIFIER, "Expect parameter name").value;
        const paramType = this.consumeTyping();
        params.push({ name: paramName, type: paramType });
      } while (this.match(TokenType.DELIMITER, ',') || this.check(TokenType.IDENTIFIER));
    }
    this.consume(TokenType.DELIMITER, "Expect ')' after parameter list", ')');

    let returnType = "";
    if (this.check(TokenType.DELIMITER, '[') ||
      this.check(TokenType.KEYWORD, 'fn') ||
      this.check(TokenType.IDENTIFIER)) {
      returnType = this.consumeTyping();
    }

    return new FnDecl(nameToken.value, params, null, returnType, false);
  }

  enumDeclaration(isPublic = false) {
    const nameToken = this.consume(TokenType.IDENTIFIER, "Expect enum name");
    this.consume(TokenType.DELIMITER, "Expect '{' after enum name", '{');
    const members = [];

    while (!this.check(TokenType.DELIMITER, '}') && !this.isAtEnd()) {
      const memberToken = this.consume(TokenType.IDENTIFIER, "Expect enum member name");
      members.push(memberToken.value);
      this.match(TokenType.DELIMITER, ',');
    }

    this.consume(TokenType.DELIMITER, "Expect '}' to close enum declaration", '}');
    return new EnumDecl(nameToken.value, members, isPublic);
  }

  fnDeclaration(isPublic = false) {
    const nameToken = this.consume(TokenType.IDENTIFIER, "Expect function name");

    // Parameters are in parentheses
    this.consume(TokenType.DELIMITER, "Expect '(' after function name", '(');
    const params = [];
    if (!this.check(TokenType.DELIMITER, ')')) {
      do {
        const paramName = this.consume(TokenType.IDENTIFIER, "Expect parameter name").value;
        const paramType = this.consumeTyping();
        params.push({ name: paramName, type: paramType });
      } while (this.match(TokenType.DELIMITER, ',') || this.check(TokenType.IDENTIFIER));
    }
    this.consume(TokenType.DELIMITER, "Expect ')' after parameter list", ')');

    let returnType = "";
    if (this.check(TokenType.DELIMITER, '[') ||
      this.check(TokenType.KEYWORD, 'fn') ||
      this.check(TokenType.IDENTIFIER)) {
      returnType = this.consumeTyping();
    }

    this.pushScope();
    for (const p of params) {
      this.declareVariable(p.name);
    }
    this.consume(TokenType.DELIMITER, "Expect '{' to open function body", '{');
    const body = this.block();
    this.popScope();

    this.declareVariable(nameToken.value);

    return new FnDecl(nameToken.value, params, body, returnType, isPublic);
  }

  returnStatement() {
    let expr = null;
    if (!this.check(TokenType.DELIMITER, '}') && !this.check(TokenType.KEYWORD, 'break') && !this.check(TokenType.KEYWORD, 'continue')) {
      expr = this.expression();
    }
    return new ReturnStmt(expr);
  }

  expressionStatement() {
    const expr = this.expression();
    return new ExprStmt(expr);
  }

  expression(allowStruct = true, allowMemberAccess = true) {
    return this.ternary(allowStruct, allowMemberAccess);
  }

  ternary(allowStruct = true, allowMemberAccess = true) {
    return this.assignment(allowStruct, allowMemberAccess);
  }

  assignment(allowStruct = true, allowMemberAccess = true) {
    const expr = this.logicalOr(allowStruct, allowMemberAccess);

    if (this.match(TokenType.OPERATOR)) {
      const opToken = this.previous();
      const opValue = opToken.value;
      if (['=', '+=', '-=', '*=', '/=', '%='].includes(opValue)) {
        const val = this.assignment(allowStruct, allowMemberAccess);
        if (expr instanceof IdentifierExpr || (expr instanceof BinaryExpr && expr.operator === ':') || expr instanceof IndexAccessExpr) {
          return new BinaryExpr(expr, opValue, val);
        }
        throw new Error(`Parse Error: Invalid assignment target at line ${opToken.line}`);
      } else {
        // Not assignment operator, backtrack token pointer
        this.index--;
      }
    }
    return expr;
  }

  logicalOr(allowStruct = true, allowMemberAccess = true) {
    let expr = this.logicalAnd(allowStruct, allowMemberAccess);
    while (this.match(TokenType.OPERATOR, '||')) {
      const op = this.previous().value;
      const right = this.logicalAnd(allowStruct, allowMemberAccess);
      expr = new BinaryExpr(expr, op, right);
    }
    return expr;
  }

  logicalAnd(allowStruct = true, allowMemberAccess = true) {
    let expr = this.equality(allowStruct, allowMemberAccess);
    while (this.match(TokenType.OPERATOR, '&&')) {
      const op = this.previous().value;
      const right = this.equality(allowStruct, allowMemberAccess);
      expr = new BinaryExpr(expr, op, right);
    }
    return expr;
  }

  equality(allowStruct = true, allowMemberAccess = true) {
    let expr = this.comparison(allowStruct, allowMemberAccess);
    while (this.match(TokenType.OPERATOR, '==') || this.match(TokenType.OPERATOR, '!=')) {
      const op = this.previous().value;
      const right = this.comparison(allowStruct, allowMemberAccess);
      expr = new BinaryExpr(expr, op, right);
    }
    return expr;
  }

  comparison(allowStruct = true, allowMemberAccess = true) {
    let expr = this.addition(allowStruct, allowMemberAccess);
    while (this.match(TokenType.OPERATOR, '<') || this.match(TokenType.OPERATOR, '<=') ||
      this.match(TokenType.OPERATOR, '>') || this.match(TokenType.OPERATOR, '>=') ||
      this.match(TokenType.KEYWORD, 'in')) {
      const op = this.previous().value;
      const right = this.addition(allowStruct, allowMemberAccess);
      expr = new BinaryExpr(expr, op, right);
    }
    return expr;
  }

  addition(allowStruct = true, allowMemberAccess = true) {
    let expr = this.multiplication(allowStruct, allowMemberAccess);
    while (this.match(TokenType.OPERATOR, '+') || this.match(TokenType.OPERATOR, '-')) {
      const op = this.previous().value;
      const right = this.multiplication(allowStruct, allowMemberAccess);
      expr = new BinaryExpr(expr, op, right);
    }
    return expr;
  }

  multiplication(allowStruct = true, allowMemberAccess = true) {
    let expr = this.unary(allowStruct, allowMemberAccess);
    while (this.match(TokenType.OPERATOR, '*') || this.match(TokenType.OPERATOR, '/') || this.match(TokenType.OPERATOR, '%')) {
      const op = this.previous().value;
      const right = this.unary(allowStruct, allowMemberAccess);
      expr = new BinaryExpr(expr, op, right);
    }
    return expr;
  }

  unary(allowStruct = true, allowMemberAccess = true) {
    if (this.match(TokenType.OPERATOR, '!') || this.match(TokenType.OPERATOR, '-')) {
      const op = this.previous().value;
      const right = this.unary(allowStruct, allowMemberAccess);
      return new UnaryExpr(op, right);
    }
    return this.postfix(allowStruct, allowMemberAccess);
  }

  postfix(allowStruct = true, allowMemberAccess = true) {
    let expr = this.basePrimary(allowStruct, allowMemberAccess);

    while (true) {
      if (this.match(TokenType.DELIMITER, '[')) {
        const indexExpr = this.expression();
        this.consume(TokenType.DELIMITER, "Expect ']' to close index access", ']');
        expr = new IndexAccessExpr(expr, indexExpr);
      } else if (this.peek().type === TokenType.OPERATOR && this.peek().value === ':') {
        if (!allowMemberAccess) {
          break;
        }
        this.advance(); // consume ':'
        const nextToken = this.consume(TokenType.IDENTIFIER, "Expect member name after ':'");
        let right = new IdentifierExpr(nextToken.value);
        if (this.match(TokenType.DELIMITER, '(')) {
          const args = [];
          if (!this.check(TokenType.DELIMITER, ')')) {
            do {
              let arg = this.expression();
              if (this.containsUndeclaredIt(arg)) {
                arg = new FunctionLambdaExpression([{ name: 'it', type: 'int' }], new Block([new ReturnStmt(arg)]));
              }
              args.push(arg);
            } while (this.match(TokenType.DELIMITER, ','));
          }
          this.consume(TokenType.DELIMITER, "Expect ')' after function arguments", ')');
          right = new CallExpr(right, args);
        }
        expr = new BinaryExpr(expr, ':', right);
      } else if (this.match(TokenType.OPERATOR, '::')) {
        const nextToken = this.consume(TokenType.IDENTIFIER, "Expect member name after '::'");
        let right = new IdentifierExpr(nextToken.value);

        let isStruct = false;
        if (nextToken.value.length > 0 && nextToken.value[0] === nextToken.value[0].toUpperCase()) {
          const next = this.peek();
          if (next && next.type === TokenType.DELIMITER && next.value === '{') {
            const nextNext = this.tokens[this.index + 1];
            if (nextNext && nextNext.type === TokenType.DELIMITER && nextNext.value === '}') {
              isStruct = false;
            } else {
              if (this.isStructBlock(this.index)) {
                isStruct = true;
              }
            }
          }
        }

        if (isStruct && this.match(TokenType.DELIMITER, '{')) {
          const initializers = {};
          while (!this.check(TokenType.DELIMITER, '}') && !this.isAtEnd()) {
            const propName = this.consume(TokenType.IDENTIFIER, "Expect property name").value;
            this.consume(TokenType.OPERATOR, "Expect ':' after property name", ':');
            const value = this.expression();
            initializers[propName] = value;
            this.match(TokenType.DELIMITER, ',');
          }
          this.consume(TokenType.DELIMITER, "Expect '}' after struct initializer list", '}');
          right = new StructInstanceExpr(nextToken.value, initializers);
        } else if (this.match(TokenType.DELIMITER, '(')) {
          const args = [];
          if (!this.check(TokenType.DELIMITER, ')')) {
            do {
              let arg = this.expression();
              if (this.containsUndeclaredIt(arg)) {
                arg = new FunctionLambdaExpression([{ name: 'it', type: 'int' }], new Block([new ReturnStmt(arg)]));
              }
              args.push(arg);
            } while (this.match(TokenType.DELIMITER, ','));
          }
          this.consume(TokenType.DELIMITER, "Expect ')' after function arguments", ')');
          right = new CallExpr(right, args);
        }

        expr = new BinaryExpr(expr, '::', right);
      } else {
        break;
      }
    }

    return expr;
  }

  mapExpression() {
    this.consume(TokenType.IDENTIFIER, "Expect 'map' identifier", 'map');
    this.consume(TokenType.DELIMITER, "Expect '[' after 'map'", '[');
    const keyType = this.consumeTyping();
    this.consume(TokenType.DELIMITER, "Expect ',' separator in map types", ',');
    const valType = this.consumeTyping();
    this.consume(TokenType.DELIMITER, "Expect ']' after map types", ']');
    this.consume(TokenType.DELIMITER, "Expect '{' to start map initializer", '{');

    const entries = [];
    while (!this.check(TokenType.DELIMITER, '}') && !this.isAtEnd()) {
      if (this.match(TokenType.DELIMITER, ',')) {
        continue;
      }
      const keyExpr = this.expression(true, false); // allowStruct = true, allowMemberAccess = false
      this.consume(TokenType.OPERATOR, "Expect ':' after map key expression", ':');
      const valExpr = this.expression();
      entries.push({ key: keyExpr, value: valExpr });
    }
    this.consume(TokenType.DELIMITER, "Expect '}' to close map initializer", '}');
    return new MapExpr(keyType, valType, entries);
  }

  basePrimary(allowStruct = true, allowMemberAccess = true) {
    if (this.check(TokenType.IDENTIFIER, 'map') && this.tokens[this.index + 1]?.value === '[') {
      return this.mapExpression();
    }
    if (this.match(TokenType.KEYWORD, 'true')) return new LiteralExpr(true, 'BOOLEAN');
    if (this.match(TokenType.KEYWORD, 'false')) return new LiteralExpr(false, 'BOOLEAN');
    if (this.match(TokenType.KEYWORD, 'null')) return new LiteralExpr(null, 'NULL');

    if (this.match(TokenType.KEYWORD, 'if')) {
      const condition = this.expression(false);
      this.consume(TokenType.KEYWORD, "Expect 'then' in ternary expression", 'then');
      const consequent = this.expression();
      this.consume(TokenType.KEYWORD, "Expect 'else' in ternary expression", 'else');
      const alternate = this.expression();
      return new TernaryExpr(condition, consequent, alternate);
    }

    if (this.match(TokenType.KEYWORD, 'fn')) {
      let parameters = [];
      if (this.match(TokenType.DELIMITER, '(')) {
        if (!this.check(TokenType.DELIMITER, ')')) {
          do {
            const paramName = this.consume(TokenType.IDENTIFIER, "Expect parameter name").value;
            const paramType = this.consumeTyping();
            parameters.push({ name: paramName, type: paramType });
          } while (this.match(TokenType.DELIMITER, ',') || this.check(TokenType.IDENTIFIER));
        }
        this.consume(TokenType.DELIMITER, "Expect ')' after parameter list", ')');
      }

      this.pushScope();
      for (const p of parameters) {
        this.declareVariable(p.name);
      }
      let body;
      if (this.match(TokenType.OPERATOR, '=>')) {
        const expr = this.expression();
        body = new Block([new ReturnStmt(expr)]);
      } else {
        this.consume(TokenType.DELIMITER, "Expect '{' before function lambda body", '{');
        body = this.block();
      }
      this.popScope();

      return new FunctionLambdaExpression(parameters, body);
    }

    if (this.match(TokenType.NUMBER)) {
      const val = this.previous().value;
      return new LiteralExpr(val.includes('.') ? parseFloat(val) : parseInt(val, 10), 'NUMBER');
    }

    if (this.match(TokenType.STRING)) {
      return new LiteralExpr(this.previous().value, 'STRING');
    }

    if (this.match(TokenType.IDENTIFIER)) {
      const idToken = this.previous();
      const idName = idToken.value;

      // Check for struct initialization: StructName { field: val, field: val }
      // In Pino: Point { x: 1, y: 2 }
      let isStruct = false;
      if (idName.length > 0 && idName[0] === idName[0].toUpperCase()) {
        const next = this.peek();
        if (next && next.type === TokenType.DELIMITER && next.value === '{') {
          const nextNext = this.tokens[this.index + 1];
          if (nextNext && nextNext.type === TokenType.DELIMITER && nextNext.value === '}') {
            const prevToken = this.index - 2 >= 0 ? this.tokens[this.index - 2] : null;
            const isPrecededByStaticMemberAccess = prevToken && prevToken.value === '::';
            if (!isPrecededByStaticMemberAccess) {
              isStruct = true;
            }
          } else {
            if (this.isStructBlock(this.index)) {
              isStruct = true;
            }
          }
        }
      }

      if (isStruct && this.match(TokenType.DELIMITER, '{')) {
        const initializers = {};
        while (!this.check(TokenType.DELIMITER, '}') && !this.isAtEnd()) {
          const propName = this.consume(TokenType.IDENTIFIER, "Expect property name").value;
          this.consume(TokenType.OPERATOR, "Expect ':' after property name", ':');
          const value = this.expression();
          initializers[propName] = value;
          this.match(TokenType.DELIMITER, ',');
        }
        this.consume(TokenType.DELIMITER, "Expect '}' after struct initializer list", '}');
        return new StructInstanceExpr(idName, initializers);
      }

      // Check for function/method call
      if (this.match(TokenType.DELIMITER, '(')) {
        const args = [];
        if (!this.check(TokenType.DELIMITER, ')')) {
          do {
            let arg = this.expression();
            if (this.containsUndeclaredIt(arg)) {
              arg = new FunctionLambdaExpression([{ name: 'it', type: 'int' }], new Block([new ReturnStmt(arg)]));
            }
            args.push(arg);
          } while (this.match(TokenType.DELIMITER, ','));
        }
        this.consume(TokenType.DELIMITER, "Expect ')' after function arguments", ')');
        return new CallExpr(new IdentifierExpr(idName), args);
      }

      return new IdentifierExpr(idName);
    }

    // Vectors: [1, 2, 3] or []type { len: X, init: Y }
    if (this.match(TokenType.DELIMITER, '[')) {
      if (this.match(TokenType.DELIMITER, ']')) {
        // Vector initialization constructor: []type { len: limit, init: expr }
        const typeToken = this.consume(TokenType.IDENTIFIER, "Expect vector element type");
        this.consume(TokenType.DELIMITER, "Expect '{' for vector init block", '{');

        let lenExpr = null;
        let initExpr = null;

        while (!this.check(TokenType.DELIMITER, '}') && !this.isAtEnd()) {
          const propName = this.consume(TokenType.IDENTIFIER, "Expect 'len' or 'init' parameter").value;
          this.consume(TokenType.OPERATOR, "Expect ':' after property name", ':');
          const value = this.expression();
          if (propName === 'len') lenExpr = value;
          else if (propName === 'init') initExpr = value;
          this.match(TokenType.DELIMITER, ',');
        }
        this.consume(TokenType.DELIMITER, "Expect '}' after vector init parameters", '}');
        return new VectorExpr(null, lenExpr, initExpr, typeToken.value);
      }

      // List literal [1, 2, 3]
      const elements = [];
      if (!this.check(TokenType.DELIMITER, ']')) {
        do {
          elements.push(this.expression());
        } while (this.match(TokenType.DELIMITER, ',') || (!this.check(TokenType.DELIMITER, ']') && !this.isAtEnd()));
      }
      this.consume(TokenType.DELIMITER, "Expect ']' to close list literal", ']');
      return new VectorExpr(elements, null, null);
    }

    if (this.match(TokenType.DELIMITER, '(')) {
      const expr = this.expression();
      this.consume(TokenType.DELIMITER, "Expect ')' to close parenthesis", ')');
      return expr;
    }

    throw new Error(`Parse Error: Unexpected token '${this.peek().value}' at line ${this.peek().line}`);
  }
}

// Exception classes for Control Flow
class ReturnException extends Error {
  constructor(value) {
    super();
    this.value = value;
  }
}
class BreakException extends Error { }
class ContinueException extends Error { }

class Environment {
  constructor(parent = null) {
    this.parent = parent;
    this.records = new Map();
    this.constants = new Set();
    this.publicExports = new Set();
  }

  define(name, value, isConstant = false) {
    this.records.set(name, value);
    if (isConstant) {
      this.constants.add(name);
    }
  }

  exists(name) {
    if (this.records.has(name)) return true;
    if (this.parent) return this.parent.exists(name);
    return false;
  }

  assign(name, value) {
    if (this.records.has(name)) {
      if (this.constants.has(name)) {
        throw new Error(`RUNTIME ERROR: Cannot reassign constant variable '${name}'.`);
      }
      this.records.set(name, value);
      return;
    }
    if (this.parent) {
      this.parent.assign(name, value);
      return;
    }
    throw new Error(`RUNTIME ERROR: Variable '${name}' is not defined.`);
  }

  get(name) {
    if (this.records.has(name)) {
      return this.records.get(name);
    }
    if (this.parent) {
      return this.parent.get(name);
    }
    throw new Error(`RUNTIME ERROR: Variable '${name}' is not defined.`);
  }
}

class StructInstance {
  constructor(structName, fields = {}) {
    this.structName = structName;
    this.fields = fields;
  }
}

class PinoCallable {
  constructor(fnDecl, closure) {
    this.fnDecl = fnDecl;
    this.closure = closure;
  }

  call(interpreter, args) {
    const localEnv = new Environment(this.closure);
    for (let i = 0; i < this.fnDecl.params.length; i++) {
      localEnv.define(this.fnDecl.params[i].name, args[i], false);
    }

    try {
      interpreter.executeBlock(this.fnDecl.body.statements, localEnv);
    } catch (err) {
      if (err instanceof ReturnException) {
        return err.value;
      }
      throw err;
    }
    return null;
  }
}

class PinoModule {
  constructor(name, environment, publicExports) {
    this.name = name;
    this.environment = environment;
    this.publicExports = publicExports; // Set
  }
}

// Tree-Walk Interpreter
class Interpreter {
  constructor(outputCallback = console.log, inputCallback = () => '') {
    this.outputCallback = outputCallback;
    this.inputCallback = inputCallback;
    this.globalEnv = new Environment();
    this.structs = new Map();
    this.enums = new Map();
    this.moduleCache = new Map();
    this.currentlyLoadingModules = new Set();
    this.initGlobals();
  }

  initGlobals() {
    this.globalEnv.define('print', (args) => {
      this.outputCallback(args.map(arg => this.formatVal(arg)).join(' '));
      return null;
    }, true);

    this.globalEnv.define('println', (args) => {
      this.outputCallback(args.map(arg => this.formatVal(arg)).join(' ') + '\n');
      return null;
    }, true);

    this.globalEnv.define('readline', (args) => {
      if (args && args.length > 0) {
        this.outputCallback(args[0]);
      }
      const val = this.inputCallback();
      return val;
    }, true);

    this.globalEnv.define('int', (args) => {
      const val = args[0];
      if (typeof val === 'number') return Math.floor(val);
      return parseInt(val, 10);
    }, true);

    this.globalEnv.define('float', (args) => {
      const val = args[0];
      if (typeof val === 'number') return val;
      return parseFloat(val);
    }, true);

    this.globalEnv.define('rand', (args) => {
      if (args.length === 0) {
        return Math.random();
      }
      const max = args[0];
      return Math.floor(Math.random() * max);
    }, true);

    this.globalEnv.define('time', (args) => {
      return Date.now();
    }, true);

    this.globalEnv.define('sleep', (args) => {
      const ms = args[0];
      const start = Date.now();
      while (Date.now() - start < ms) {
        // Busy wait
      }
      return null;
    }, true);

    this.globalEnv.define('type', (args) => {
      const val = args[0];
      if (val === null || val === undefined) return 'null';
      if (typeof val === 'boolean') return 'bool';
      if (typeof val === 'number') {
        return Number.isInteger(val) ? 'int' : 'float';
      }
      if (typeof val === 'string') {
        const parts = val.split('::');
        if (parts.length === 2 && this.enums.has(parts[0])) {
          const members = this.enums.get(parts[0]);
          if (members.includes(parts[1])) {
            return 'enum';
          }
        }
        return 'string';
      }
      if (Array.isArray(val)) return 'vector';
      if (val instanceof Map) return 'map';
      if (val instanceof StructInstance) return 'struct';
      if (val instanceof PinoCallable || typeof val === 'function') return 'function';
      return typeof val;
    }, true);

    this.globalEnv.define('str', (args) => {
      return this.formatVal(args[0]);
    }, true);

    this.globalEnv.define('clear', (args) => {
      this.outputCallback('\f');
      return null;
    }, true);
  }

  formatVal(val) {
    if (val === null || val === undefined) return 'null';
    if (Array.isArray(val)) {
      return '[' + val.map(v => this.formatVal(v)).join(', ') + ']';
    }
    if (val instanceof Map) {
      const entries = Array.from(val.entries()).map(([k, v]) => {
        const keyStr = typeof k === 'string' ? `"${k}"` : this.formatVal(k);
        const valStr = typeof v === 'string' ? `"${v}"` : this.formatVal(v);
        return `${keyStr}: ${valStr}`;
      });
      return '{' + entries.join(', ') + '}';
    }
    if (val instanceof StructInstance) {
      const fieldsStr = Object.entries(val.fields)
        .map(([k, v]) => `${k}: ${this.formatVal(v)}`)
        .join(', ');
      return `${val.structName} { ${fieldsStr} }`;
    }
    if (val instanceof PinoCallable) {
      return `fn(${val.fnDecl.params.map(p => p.name).join(', ')})`;
    }
    return val.toString();
  }

  execute(statements) {
    for (const stmt of statements) {
      this.evaluateStatement(stmt, this.globalEnv);
    }
  }

  evaluateStatement(stmt, env) {
    if (stmt instanceof VarDecl) {
      const val = stmt.valueExpr ? this.evaluateExpression(stmt.valueExpr, env) : null;
      env.define(stmt.name, val, stmt.isConstant);
      if (stmt.isPublic) {
        env.publicExports.add(stmt.name);
      }
    } else if (stmt instanceof Block) {
      this.executeBlock(stmt.statements, new Environment(env));
    } else if (stmt instanceof IfStmt) {
      const cond = this.evaluateExpression(stmt.condition, env);
      if (this.isTruthy(cond)) {
        this.evaluateStatement(stmt.thenBranch, env);
      } else {
        let matched = false;
        for (const elseIf of stmt.elseIfs) {
          const elseCond = this.evaluateExpression(elseIf.cond, env);
          if (this.isTruthy(elseCond)) {
            this.evaluateStatement(elseIf.body, env);
            matched = true;
            break;
          }
        }
        if (!matched && stmt.elseBranch) {
          this.evaluateStatement(stmt.elseBranch, env);
        }
      }
    } else if (stmt instanceof ForStmt) {
      this.executeForLoop(stmt, env);
    } else if (stmt instanceof MatchStmt) {
      this.executeMatch(stmt, env);
    } else if (stmt instanceof StructDecl) {
      const consolidatedFields = [];
      const consolidatedMethods = [];

      if (stmt.inheritedStructs) {
        for (const parentName of stmt.inheritedStructs) {
          const parentStruct = env.get(parentName);
          if (parentStruct instanceof StructDecl) {
            consolidatedFields.push(...parentStruct.fields);
            consolidatedMethods.push(...parentStruct.methods);
          } else {
            throw new Error(`RUNTIME ERROR: Parent struct '${parentName}' is not defined.`);
          }
        }
      }

      for (const field of stmt.fields) {
        const idx = consolidatedFields.findIndex(f => f.name === field.name);
        if (idx !== -1) consolidatedFields.splice(idx, 1);
        consolidatedFields.push(field);
      }
      for (const method of stmt.methods) {
        const idx = consolidatedMethods.findIndex(m => m.name === method.name);
        if (idx !== -1) consolidatedMethods.splice(idx, 1);
        consolidatedMethods.push(method);
      }

      const structDef = new StructDecl(stmt.name, consolidatedFields, consolidatedMethods, stmt.inheritedStructs, stmt.isPublic);
      this.structs.set(stmt.name, structDef);
      env.define(stmt.name, structDef, true);
      if (stmt.isPublic) {
        env.publicExports.add(stmt.name);
      }
    } else if (stmt instanceof EnumDecl) {
      this.enums.set(stmt.name, stmt.members);
      env.define(stmt.name, stmt, true);
      if (stmt.isPublic) {
        env.publicExports.add(stmt.name);
      }
    } else if (stmt instanceof FnDecl) {
      const callable = new PinoCallable(stmt, env);
      env.define(stmt.name, callable, true);
      if (stmt.isPublic) {
        env.publicExports.add(stmt.name);
      }
    } else if (stmt instanceof ModuleDecl) {
      // Handled during load/resolution, ignored during sequential execution.
    } else if (stmt instanceof ImportStmt) {
      const module = this.resolveAndLoadModule(stmt.moduleName);
      env.define(stmt.moduleName, module, true);
    } else if (stmt instanceof FromImportStmt) {
      const fromModule = this.resolveAndLoadModule(stmt.moduleName);
      for (const name of stmt.imports) {
        if (!fromModule.publicExports.has(name)) {
          throw new Error(`RUNTIME ERROR: Module '${stmt.moduleName}' does not export '${name}' (or it is private).`);
        }
        env.define(name, fromModule.environment.get(name), true);
      }
    } else if (stmt instanceof ExprStmt) {
      this.evaluateExpression(stmt.expression, env);
    } else if (stmt instanceof ReturnStmt) {
      const val = stmt.argument ? this.evaluateExpression(stmt.argument, env) : null;
      throw new ReturnException(val);
    } else if (stmt instanceof BreakStmt) {
      throw new BreakException();
    } else if (stmt instanceof ContinueStmt) {
      throw new ContinueException();
    }
  }

  executeBlock(statements, env) {
    for (const stmt of statements) {
      this.evaluateStatement(stmt, env);
    }
  }

  executeForLoop(stmt, env) {
    if (stmt.isInfinite) {
      while (true) {
        try {
          this.evaluateStatement(stmt.body, new Environment(env));
        } catch (err) {
          if (err instanceof BreakException) break;
          if (err instanceof ContinueException) continue;
          throw err;
        }
      }
      return;
    }

    const iterable = this.evaluateExpression(stmt.iterableExpr, env);
    if (typeof iterable === 'number') {
      for (let i = 0; i < iterable; i++) {
        const loopEnv = new Environment(env);
        loopEnv.define(stmt.varName, i, false);
        try {
          this.evaluateStatement(stmt.body, loopEnv);
        } catch (err) {
          if (err instanceof BreakException) break;
          if (err instanceof ContinueException) continue;
          throw err;
        }
      }
    } else if (Array.isArray(iterable)) {
      for (const item of iterable) {
        const loopEnv = new Environment(env);
        loopEnv.define(stmt.varName, item, false);
        try {
          this.evaluateStatement(stmt.body, loopEnv);
        } catch (err) {
          if (err instanceof BreakException) break;
          if (err instanceof ContinueException) continue;
          throw err;
        }
      }
    } else {
      throw new Error(`RUNTIME ERROR: Cannot iterate over non-iterable type.`);
    }
  }

  executeMatch(stmt, env) {
    const val = this.evaluateExpression(stmt.condition, env);
    let matched = false;

    for (const branch of stmt.branches) {
      for (const condExpr of branch.conditions) {
        const condVal = this.evaluateExpression(condExpr, env);
        if (val === condVal) {
          this.evaluateStatement(branch.body, env);
          matched = true;
          break;
        }
      }
      if (matched) break;
    }

    if (!matched && stmt.alternate) {
      this.evaluateStatement(stmt.alternate, env);
    }
  }

  resolveAndLoadModule(moduleName) {
    if (this.moduleCache.has(moduleName)) {
      return this.moduleCache.get(moduleName);
    }

    const source = this.getModuleSource(moduleName);
    if (!source) {
      throw new Error(`Runtime Error: Module '${moduleName}' not found.`);
    }

    if (this.currentlyLoadingModules.has(moduleName)) {
      throw new Error(`Runtime Error: Circular dependency detected while importing module '${moduleName}'.`);
    }
    this.currentlyLoadingModules.add(moduleName);

    try {
      const lexer = new Lexer(source);
      const tokens = lexer.tokenize();
      const parser = new Parser(tokens);
      const statements = parser.parse();

      const moduleEnv = new Environment(this.globalEnv);

      for (const stmt of statements) {
        if (stmt instanceof ModuleDecl) {
          if (stmt.name !== moduleName) {
            throw new Error(`Runtime Error: Module name mismatch. Declared '${stmt.name}' in file, but imported as '${moduleName}'.`);
          }
          continue;
        }
        this.evaluateStatement(stmt, moduleEnv);
      }

      const pinoModule = new PinoModule(moduleName, moduleEnv, moduleEnv.publicExports);
      this.moduleCache.set(moduleName, pinoModule);
      return pinoModule;
    } finally {
      this.currentlyLoadingModules.delete(moduleName);
    }
  }

  getModuleSource(moduleName) {
    if (this.getModuleSourceCallback) {
      return this.getModuleSourceCallback(moduleName);
    }
    const lowerName = moduleName.toLowerCase();
    if (globalThis.pinoModules) {
      return globalThis.pinoModules[lowerName] || globalThis.pinoModules[moduleName];
    }
    return null;
  }

  evaluateExpression(expr, env) {
    if (expr instanceof LiteralExpr) {
      return expr.value;
    }

    if (expr instanceof IdentifierExpr) {
      return env.get(expr.name);
    }

    if (expr instanceof UnaryExpr) {
      const right = this.evaluateExpression(expr.right, env);
      if (expr.operator === '!') return !this.isTruthy(right);
      if (expr.operator === '-') return -right;
      return null;
    }

    if (expr instanceof TernaryExpr) {
      const cond = this.evaluateExpression(expr.condition, env);
      if (this.isTruthy(cond)) {
        return this.evaluateExpression(expr.consequent, env);
      } else {
        return this.evaluateExpression(expr.alternate, env);
      }
    }

    if (expr instanceof BinaryExpr) {
      // Handle member access object:property or method call
      if (expr.operator === ':') {
        const target = this.evaluateExpression(expr.left, env);
        if (target instanceof StructInstance) {
          const right = expr.right;
          if (right instanceof CallExpr && right.callee instanceof IdentifierExpr) {
            // Struct method invocation
            const structDef = this.structs.get(target.structName);
            if (!structDef) throw new Error(`RUNTIME ERROR: Struct definition for '${target.structName}' not found.`);
            const methodDecl = structDef.methods.find(m => m.name === right.callee.name && !m.isStatic);
            if (!methodDecl) throw new Error(`RUNTIME ERROR: Instance method '${right.callee.name}' not found on Struct '${target.structName}'.`);

            // Method closure binding
            const methodEnv = new Environment(env);
            // Copy fields as local variables
            for (const [key, value] of Object.entries(target.fields)) {
              methodEnv.define(key, value, false);
            }
            methodEnv.define('self', target, true);
            methodEnv.define('this', target, true);

            const methodArgs = right.args.map(a => this.evaluateExpression(a, env));
            const callable = new PinoCallable(methodDecl, methodEnv);
            const result = callable.call(this, methodArgs);

            // Copy back updated local variables back to instance fields
            for (const key of Object.keys(target.fields)) {
              target.fields[key] = methodEnv.get(key);
            }

            return result;
          } else if (right instanceof IdentifierExpr) {
            if (right.name in target.fields) {
              return target.fields[right.name];
            }
            throw new Error(`RUNTIME ERROR: Property '${right.name}' does not exist on Struct '${target.structName}'.`);
          }
        } else if (Array.isArray(target)) {
          // List operations
          const right = expr.right;
          if (right instanceof IdentifierExpr && (right.name === 'length' || right.name === 'len')) {
            return target.length;
          }

          if (right instanceof CallExpr && right.callee instanceof IdentifierExpr) {
            const methodName = right.callee.name;
            const methodArgs = right.args.map(a => this.evaluateExpression(a, env));

            if (methodName === 'each') {
              const func = methodArgs[0];
              if (!func || (typeof func !== 'function' && !(func instanceof PinoCallable))) {
                throw new Error("RUNTIME ERROR: each() expects a callable argument.");
              }
              const arity = (func instanceof PinoCallable) ? func.fnDecl.params.length : -1;
              const args = arity === 2 ? [null, 0] : [null];
              for (let i = 0; i < target.length; i++) {
                args[0] = target[i];
                if (arity === 2) args[1] = i;
                if (typeof func === 'function') {
                  func(args);
                } else {
                  func.call(this, args);
                }
              }
              return null;
            }

            if (methodName === 'map') {
              const func = methodArgs[0];
              if (!func || (typeof func !== 'function' && !(func instanceof PinoCallable))) {
                throw new Error("RUNTIME ERROR: map() expects a callable argument.");
              }
              const arity = (func instanceof PinoCallable) ? func.fnDecl.params.length : -1;
              const args = arity === 2 ? [null, 0] : [null];
              const mapped = [];
              for (let i = 0; i < target.length; i++) {
                args[0] = target[i];
                if (arity === 2) args[1] = i;
                if (typeof func === 'function') {
                  mapped.push(func(args));
                } else {
                  mapped.push(func.call(this, args));
                }
              }
              return mapped;
            }

            if (methodName === 'filter') {
              const func = methodArgs[0];
              if (!func || (typeof func !== 'function' && !(func instanceof PinoCallable))) {
                throw new Error("RUNTIME ERROR: filter() expects a callable argument.");
              }
              const arity = (func instanceof PinoCallable) ? func.fnDecl.params.length : -1;
              const args = arity === 2 ? [null, 0] : [null];
              const filtered = [];
              for (let i = 0; i < target.length; i++) {
                args[0] = target[i];
                if (arity === 2) args[1] = i;
                let res;
                if (typeof func === 'function') {
                  res = func(args);
                } else {
                  res = func.call(this, args);
                }
                if (this.isTruthy(res)) {
                  filtered.push(target[i]);
                }
              }
              return filtered;
            }

            if (methodName === 'find') {
              const func = methodArgs[0];
              if (!func || (typeof func !== 'function' && !(func instanceof PinoCallable))) {
                throw new Error("RUNTIME ERROR: find() expects a callable argument.");
              }
              const arity = (func instanceof PinoCallable) ? func.fnDecl.params.length : -1;
              const args = arity === 2 ? [null, 0] : [null];
              for (let i = 0; i < target.length; i++) {
                args[0] = target[i];
                if (arity === 2) args[1] = i;
                let res;
                if (typeof func === 'function') {
                  res = func(args);
                } else {
                  res = func.call(this, args);
                }
                if (this.isTruthy(res)) {
                  return target[i];
                }
              }
              return null;
            }

            if (methodName === 'find_index') {
              const func = methodArgs[0];
              if (!func || (typeof func !== 'function' && !(func instanceof PinoCallable))) {
                throw new Error("RUNTIME ERROR: find_index() expects a callable argument.");
              }
              const arity = (func instanceof PinoCallable) ? func.fnDecl.params.length : -1;
              const args = arity === 2 ? [null, 0] : [null];
              for (let i = 0; i < target.length; i++) {
                args[0] = target[i];
                if (arity === 2) args[1] = i;
                let res;
                if (typeof func === 'function') {
                  res = func(args);
                } else {
                  res = func.call(this, args);
                }
                if (this.isTruthy(res)) {
                  return i;
                }
              }
              return -1;
            }

            if (methodName === 'any') {
              const func = methodArgs[0];
              if (!func || (typeof func !== 'function' && !(func instanceof PinoCallable))) {
                throw new Error("RUNTIME ERROR: any() expects a callable argument.");
              }
              const arity = (func instanceof PinoCallable) ? func.fnDecl.params.length : -1;
              const args = arity === 2 ? [null, 0] : [null];
              for (let i = 0; i < target.length; i++) {
                args[0] = target[i];
                if (arity === 2) args[1] = i;
                let res;
                if (typeof func === 'function') {
                  res = func(args);
                } else {
                  res = func.call(this, args);
                }
                if (this.isTruthy(res)) {
                  return true;
                }
              }
              return false;
            }

            if (methodName === 'all') {
              const func = methodArgs[0];
              if (!func || (typeof func !== 'function' && !(func instanceof PinoCallable))) {
                throw new Error("RUNTIME ERROR: all() expects a callable argument.");
              }
              const arity = (func instanceof PinoCallable) ? func.fnDecl.params.length : -1;
              const args = arity === 2 ? [null, 0] : [null];
              for (let i = 0; i < target.length; i++) {
                args[0] = target[i];
                if (arity === 2) args[1] = i;
                let res;
                if (typeof func === 'function') {
                  res = func(args);
                } else {
                  res = func.call(this, args);
                }
                if (!this.isTruthy(res)) {
                  return false;
                }
              }
              return true;
            }

            if (methodName === 'push' || methodName === 'add') {
              if (methodArgs.length < 1) {
                throw new Error("RUNTIME ERROR: push() expects an item to add.");
              }
              target.push(methodArgs[0]);
              return target;
            }

            if (methodName === 'pop') {
              return target.pop();
            }

            throw new Error(`RUNTIME ERROR: Vector has no method '${methodName}'.`);
          }
        } else if (target instanceof Map) {
          const right = expr.right;
          if (right instanceof IdentifierExpr && (right.name === 'length' || right.name === 'len')) {
            return target.size;
          }

          if (right instanceof CallExpr && right.callee instanceof IdentifierExpr) {
            const methodName = right.callee.name;
            const methodArgs = right.args.map(a => this.evaluateExpression(a, env));

            if (methodName === 'keys') {
              if (methodArgs.length !== 0) throw new Error("RUNTIME ERROR: keys() expects 0 arguments.");
              return Array.from(target.keys());
            }
            if (methodName === 'values') {
              if (methodArgs.length !== 0) throw new Error("RUNTIME ERROR: values() expects 0 arguments.");
              return Array.from(target.values());
            }
            if (methodName === 'remove') {
              if (methodArgs.length !== 1) throw new Error("RUNTIME ERROR: remove() expects 1 argument.");
              const key = methodArgs[0];
              if (key === null || key === undefined) throw new Error("RUNTIME ERROR: remove() key cannot be null.");
              if (target.has(key)) {
                const removedVal = target.get(key);
                target.delete(key);
                return removedVal;
              }
              return null;
            }
            throw new Error(`RUNTIME ERROR: Map has no method '${methodName}'.`);
          }
        } else if (typeof target === 'string') {
          const right = expr.right;
          if (right instanceof IdentifierExpr && (right.name === 'length' || right.name === 'len')) {
            return target.length;
          }

          if (right instanceof CallExpr && right.callee instanceof IdentifierExpr) {
            const methodName = right.callee.name;
            const methodArgs = right.args.map(a => this.evaluateExpression(a, env));

            if (methodName === 'lower') {
              if (methodArgs.length !== 0) throw new Error("RUNTIME ERROR: lower() expects 0 arguments.");
              return target.toLowerCase();
            }
            if (methodName === 'upper') {
              if (methodArgs.length !== 0) throw new Error("RUNTIME ERROR: upper() expects 0 arguments.");
              return target.toUpperCase();
            }
            if (methodName === 'trim') {
              if (methodArgs.length !== 0) throw new Error("RUNTIME ERROR: trim() expects 0 arguments.");
              return target.trim();
            }
            if (methodName === 'contains') {
              if (methodArgs.length !== 1 || typeof methodArgs[0] !== 'string') {
                throw new Error("RUNTIME ERROR: contains() expects 1 string argument.");
              }
              return target.includes(methodArgs[0]);
            }
            if (methodName === 'split') {
              if (methodArgs.length !== 1 || typeof methodArgs[0] !== 'string') {
                throw new Error("RUNTIME ERROR: split() expects 1 string argument.");
              }
              return target.split(methodArgs[0]);
            }
            if (methodName === 'replace') {
              if (methodArgs.length !== 2 || typeof methodArgs[0] !== 'string' || typeof methodArgs[1] !== 'string') {
                throw new Error("RUNTIME ERROR: replace() expects 2 string arguments.");
              }
              return target.split(methodArgs[0]).join(methodArgs[1]);
            }
            throw new Error(`RUNTIME ERROR: String has no method '${methodName}'.`);
          }
        }
        throw new Error(`RUNTIME ERROR: Invalid member access.`);
      }

      // Static member access Enum::Member, Module::Member, or Struct::StaticMethod
      if (expr.operator === '::') {
        const leftVal = this.evaluateExpression(expr.left, env);

        if (leftVal instanceof PinoModule) {
          const right = expr.right;
          if (right instanceof CallExpr && right.callee instanceof IdentifierExpr) {
            const memberName = right.callee.name;
            if (!leftVal.publicExports.has(memberName)) {
              throw new Error(`RUNTIME ERROR: Member '${memberName}' is not exported by module '${leftVal.name}' (or is private).`);
            }
            const fn = leftVal.environment.get(memberName);
            const methodArgs = right.args.map(a => this.evaluateExpression(a, env));
            if (typeof fn === 'function') {
              return fn(methodArgs);
            }
            if (fn instanceof PinoCallable) {
              return fn.call(this, methodArgs);
            }
            throw new Error(`RUNTIME ERROR: '${memberName}' is not callable.`);
          } else if (right instanceof IdentifierExpr) {
            const memberName = right.name;
            if (!leftVal.publicExports.has(memberName)) {
              throw new Error(`RUNTIME ERROR: Member '${memberName}' is not exported by module '${leftVal.name}' (or is private).`);
            }
            return leftVal.environment.get(memberName);
          } else if (right instanceof StructInstanceExpr) {
            const structName = right.structName;
            if (!leftVal.publicExports.has(structName)) {
              throw new Error(`RUNTIME ERROR: Member '${structName}' is not exported by module '${leftVal.name}' (or is private).`);
            }
            const structDecl = leftVal.environment.get(structName);
            if (!structDecl || !(structDecl instanceof StructDecl)) {
              throw new Error(`RUNTIME ERROR: '${structName}' is not a struct in module '${leftVal.name}'.`);
            }
            const fields = {};
            for (const field of structDecl.fields) {
              fields[field.name] = null;
            }
            for (const [key, valueExpr] of Object.entries(right.initializers)) {
              fields[key] = this.evaluateExpression(valueExpr, env);
            }
            return new StructInstance(structName, fields);
          }
          throw new Error("RUNTIME ERROR: Right side of '::' must be a member name, function call, or struct instance.");
        }

        if (leftVal instanceof StructDecl) {
          const right = expr.right;
          if (right instanceof CallExpr && right.callee instanceof IdentifierExpr) {
            const calleeName = right.callee.name;
            const methodDecl = leftVal.methods.find(m => m.name === calleeName && m.isStatic);
            if (!methodDecl) {
              throw new Error(`RUNTIME ERROR: Struct '${leftVal.name}' has no static method '${calleeName}'.`);
            }
            const methodArgs = right.args.map(a => this.evaluateExpression(a, env));
            const callable = new PinoCallable(methodDecl, env);
            return callable.call(this, methodArgs);
          }
          if (right instanceof IdentifierExpr) {
            const methodName = right.name;
            const methodDecl = leftVal.methods.find(m => m.name === methodName && m.isStatic);
            if (!methodDecl) {
              throw new Error(`RUNTIME ERROR: Struct '${leftVal.name}' has no static method '${methodName}'.`);
            }
            return new PinoCallable(methodDecl, env);
          }
          throw new Error("RUNTIME ERROR: Right side of '::' for a struct must be a static method name or static method call.");
        }

        if (leftVal instanceof EnumDecl) {
          const memberName = expr.right.name;
          if (!leftVal.members.includes(memberName)) {
            throw new Error(`RUNTIME ERROR: Enum member '${memberName}' not found on Enum '${leftVal.name}'.`);
          }
          return `${leftVal.name}::${memberName}`;
        }

        throw new Error("RUNTIME ERROR: Left side of '::' must evaluate to a module, struct, or enum.");
      }

      // Assignment and compound assignments
      if (['=', '+=', '-=', '*=', '/=', '%='].includes(expr.operator)) {
        const val = this.evaluateExpression(expr.right, env);
        if (expr.left instanceof IdentifierExpr) {
          let targetVal = val;
          if (expr.operator !== '=') {
            const currentVal = env.get(expr.left.name);
            targetVal = this.evalOp(currentVal, expr.operator.slice(0, -1), val);
          }
          env.assign(expr.left.name, targetVal);
          return targetVal;
        }

        if (expr.left instanceof BinaryExpr && expr.left.operator === ':') {
          const structInstance = this.evaluateExpression(expr.left.left, env);
          if (!(structInstance instanceof StructInstance)) {
            throw new Error(`RUNTIME ERROR: Cannot assign to property of non-struct object.`);
          }
          const propId = expr.left.right.name;
          let targetVal = val;
          if (expr.operator !== '=') {
            const currentVal = structInstance.fields[propId];
            targetVal = this.evalOp(currentVal, expr.operator.slice(0, -1), val);
          }
          structInstance.fields[propId] = targetVal;
          return targetVal;
        }

        if (expr.left instanceof IndexAccessExpr) {
          const target = this.evaluateExpression(expr.left.target, env);
          const indexVal = this.evaluateExpression(expr.left.index, env);
          if (Array.isArray(target)) {
            if (indexVal < 0 || indexVal >= target.length) {
              throw new Error(`RUNTIME ERROR: Index ${indexVal} out of range for vector of size ${target.length}.`);
            }
            let targetVal = val;
            if (expr.operator !== '=') {
              const currentVal = target[indexVal];
              targetVal = this.evalOp(currentVal, expr.operator.slice(0, -1), val);
            }
            target[indexVal] = targetVal;
            return targetVal;
          }
          if (target instanceof Map) {
            if (indexVal === null || indexVal === undefined) {
              throw new Error("RUNTIME ERROR: Map key cannot be null.");
            }
            let targetVal = val;
            if (expr.operator !== '=') {
              if (!target.has(indexVal)) {
                throw new Error(`RUNTIME ERROR: Key '${indexVal}' not found in map.`);
              }
              const currentVal = target.get(indexVal);
              targetVal = this.evalOp(currentVal, expr.operator.slice(0, -1), val);
            }
            target.set(indexVal, targetVal);
            return targetVal;
          }
          throw new Error("RUNTIME ERROR: Cannot assign to index of non-vector and non-map object.");
        }
      }

      // Standard binary arithmetic and comparison operators
      const left = this.evaluateExpression(expr.left, env);
      const right = this.evaluateExpression(expr.right, env);
      return this.evalOp(left, expr.operator, right);
    }

    if (expr instanceof FunctionLambdaExpression) {
      return new PinoCallable({ params: expr.parameters, body: expr.body }, env);
    }

    if (expr instanceof IndexAccessExpr) {
      const targetVal = this.evaluateExpression(expr.target, env);
      const indexVal = this.evaluateExpression(expr.index, env);
      if (Array.isArray(targetVal)) {
        if (indexVal < 0 || indexVal >= targetVal.length) {
          throw new Error(`RUNTIME ERROR: Index ${indexVal} out of range for vector of size ${targetVal.length}.`);
        }
        return targetVal[indexVal];
      }
      if (typeof targetVal === 'string') {
        if (indexVal < 0 || indexVal >= targetVal.length) {
          throw new Error(`RUNTIME ERROR: Index ${indexVal} out of range for string of length ${targetVal.length}.`);
        }
        return targetVal[indexVal];
      }
      if (targetVal instanceof Map) {
        if (indexVal === null || indexVal === undefined) {
          throw new Error("RUNTIME ERROR: Map key cannot be null.");
        }
        if (!targetVal.has(indexVal)) {
          throw new Error(`RUNTIME ERROR: Key '${indexVal}' not found in map.`);
        }
        return targetVal.get(indexVal);
      }
      throw new Error("RUNTIME ERROR: Cannot apply index access to non-vector, non-string, and non-map object.");
    }

    if (expr instanceof MapExpr) {
      const map = new Map();
      for (const entry of expr.entries) {
        const k = this.evaluateExpression(entry.key, env);
        if (k === null || k === undefined) {
          throw new Error("RUNTIME ERROR: Map key cannot be null.");
        }
        const v = this.evaluateExpression(entry.value, env);
        map.set(k, v);
      }
      return map;
    }

    if (expr instanceof StructInstanceExpr) {
      const structDecl = this.structs.get(expr.structName);
      if (!structDecl) throw new Error(`RUNTIME ERROR: Struct '${expr.structName}' is not defined.`);
      const fields = {};
      // Initialize fields with default values
      for (const field of structDecl.fields) {
        fields[field.name] = null;
      }
      // Populate fields
      for (const [key, valueExpr] of Object.entries(expr.initializers)) {
        fields[key] = this.evaluateExpression(valueExpr, env);
      }
      return new StructInstance(expr.structName, fields);
    }

    if (expr instanceof CallExpr) {
      // Evaluate function
      const fn = this.evaluateExpression(expr.callee, env);
      const args = expr.args.map(a => this.evaluateExpression(a, env));
      if (typeof fn === 'function') {
        return fn(args);
      }
      if (fn instanceof PinoCallable) {
        return fn.call(this, args);
      }
      throw new Error(`RUNTIME ERROR: Target is not callable.`);
    }

    if (expr instanceof VectorExpr) {
      if (expr.elements) {
        return expr.elements.map(e => this.evaluateExpression(e, env));
      } else {
        // Init block syntax: []type { len: limit, init: expr }
        const length = this.evaluateExpression(expr.lenExpr, env);
        const result = [];
        for (let i = 0; i < length; i++) {
          const initEnv = new Environment(env);
          initEnv.define('it', i, true);
          const val = this.evaluateExpression(expr.initExpr, initEnv);
          if (typeof val === 'function') {
            result.push(val([i]));
          } else if (val instanceof PinoCallable) {
            result.push(val.call(this, [i]));
          } else {
            result.push(val);
          }
        }
        return result;
      }
    }

    throw new Error(`RUNTIME ERROR: Unknown expression type.`);
  }

  evalOp(left, op, right) {
    switch (op) {
      case '+': return left + right;
      case '-': return left - right;
      case '*': return left * right;
      case '/': return left / right;
      case '%': return left % right;
      case '==': return left === right;
      case '!=': return left !== right;
      case '<': return left < right;
      case '<=': return left <= right;
      case '>': return left > right;
      case '>=': return left >= right;
      case '&&': return this.isTruthy(left) && this.isTruthy(right);
      case '||': return this.isTruthy(left) || this.isTruthy(right);
      case 'in': {
        if (right instanceof Map) {
          return left !== null && left !== undefined && right.has(left);
        }
        if (Array.isArray(right)) {
          return right.includes(left);
        }
        if (typeof right === 'string') {
          if (typeof left !== 'string') {
            throw new Error("RUNTIME ERROR: Left side of 'in' operator must be a string when right side is a string.");
          }
          return right.includes(left);
        }
        throw new Error(`RUNTIME ERROR: 'in' operator not supported for type '${typeof right}'.`);
      }
      default:
        throw new Error(`RUNTIME ERROR: Unsupported operator '${op}'.`);
    }
  }

  isTruthy(val) {
    if (val === null || val === undefined) return false;
    if (typeof val === 'boolean') return val;
    return true;
  }
}

class TypeChecker {
  constructor() {
    this.structs = new Map();
    this.interfaces = new Map();
    this.enums = new Map();
    this.functions = new Map();
    this.scopes = [];
    this.moduleCheckers = new Map();
    this.currentlyCheckingModules = new Set();
    this.currentStruct = null;
    this.inStaticMethod = false;

    this.builtInFunctions = new Map([
      ["println", "fn(...)"],
      ["readline", "fn(...) string"],
      ["int", "fn(...) int"],
      ["float", "fn(...) float"],
      ["rand", "fn(...) float"],
      ["time", "fn() int"],
      ["sleep", "fn(int)"],
      ["type", "fn(any) string"],
      ["str", "fn(any) string"],
      ["clear", "fn()"]
    ]);
  }

  findStruct(name) {
    if (this.structs.has(name)) return this.structs.get(name);
    for (const modChecker of this.moduleCheckers.values()) {
      const s = modChecker.findStruct(name);
      if (s && s.isPublic) return s;
    }
    return null;
  }

  findInterface(name) {
    if (this.interfaces.has(name)) return this.interfaces.get(name);
    for (const modChecker of this.moduleCheckers.values()) {
      const i = modChecker.findInterface(name);
      if (i && i.isPublic) return i;
    }
    return null;
  }

  findEnum(name) {
    if (this.enums.has(name)) return this.enums.get(name);
    for (const modChecker of this.moduleCheckers.values()) {
      const e = modChecker.findEnum(name);
      if (e && e.isPublic) return e;
    }
    return null;
  }

  check(statements) {
    this.pushScope();

    // Pass 1: Gather global symbols
    for (const stmt of statements) {
      if (stmt instanceof StructDecl) {
        this.structs.set(stmt.name, stmt);
      } else if (stmt instanceof InterfaceDecl) {
        this.interfaces.set(stmt.name, stmt);
      } else if (stmt instanceof EnumDecl) {
        this.enums.set(stmt.name, stmt);
      } else if (stmt instanceof FnDecl) {
        this.functions.set(stmt.name, stmt);
      }
    }

    // Pass 2: Check statements
    for (const stmt of statements) {
      this.checkStatement(stmt);
    }

    this.popScope();
  }

  resolveAndCheckModule(moduleName) {
    if (this.moduleCheckers.has(moduleName)) return;

    if (this.currentlyCheckingModules.has(moduleName)) {
      throw new Error(`TYPE CHECK ERROR: Circular dependency detected while type checking module '${moduleName}'.`);
    }
    this.currentlyCheckingModules.add(moduleName);

    try {
      const source = globalThis.pinoModules?.[moduleName.toLowerCase()];
      if (!source) {
        throw new Error(`TYPE CHECK ERROR: Module '${moduleName}' source not found.`);
      }

      const lexer = new Lexer(source);
      const tokens = lexer.tokenize();
      const parser = new Parser(tokens);
      const statements = parser.parse();

      const checker = new TypeChecker();
      checker.check(statements);
      this.moduleCheckers.set(moduleName, checker);
    } finally {
      this.currentlyCheckingModules.delete(moduleName);
    }
  }

  pushScope() {
    this.scopes.push(new Map());
  }

  popScope() {
    if (this.scopes.length > 0) {
      this.scopes.pop();
    }
  }

  declareVariable(name, type) {
    if (this.scopes.length > 0) {
      this.scopes[this.scopes.length - 1].set(name, type);
    }
  }

  resolveIdentifierType(name) {
    for (let i = this.scopes.length - 1; i >= 0; i--) {
      if (this.scopes[i].has(name)) {
        return this.scopes[i].get(name);
      }
    }

    if (this.functions.has(name)) {
      return this.getFunctionSignatureString(this.functions.get(name));
    }

    if (this.builtInFunctions.has(name)) {
      return this.builtInFunctions.get(name);
    }

    if (this.findStruct(name)) return name;
    if (this.findInterface(name)) return name;

    return "any";
  }

  resolveFunctionReturnType(callee) {
    if (this.functions.has(callee)) {
      return this.inferFunctionReturnType(this.functions.get(callee));
    }

    if (this.builtInFunctions.has(callee)) {
      const sig = this.builtInFunctions.get(callee);
      const lastSpace = sig.lastIndexOf(' ');
      if (lastSpace !== -1) {
        return sig.substring(lastSpace + 1);
      }
      return "any";
    }

    const idType = this.resolveIdentifierType(callee);
    if (idType.startsWith("fn(")) {
      const closingParen = idType.lastIndexOf(')');
      if (closingParen !== -1 && closingParen < idType.length - 1) {
        return idType.substring(closingParen + 1).trim();
      }
    }

    return "any";
  }

  getFunctionSignatureString(fn = null, parameters = null, lambda = null) {
    const paramTypes = [];
    const paramsList = fn ? fn.params : (parameters || (lambda ? lambda.parameters : null));
    if (paramsList) {
      for (const p of paramsList) {
        paramTypes.push(p.type || "any");
      }
    }
    let retType = "any";
    if (fn) {
      retType = this.inferFunctionReturnType(fn);
    } else if (lambda) {
      this.pushScope();
      for (const p of lambda.parameters) {
        this.declareVariable(p.name, p.type || "any");
      }
      const returns = this.findReturnStatements(lambda.body);
      if (returns.length > 0) {
        retType = returns[0].argument ? this.inferType(returns[0].argument) : "any";
      }
      this.popScope();
    }
    return `fn(${paramTypes.join(', ')}) ${retType}`;
  }

  checkStatement(statement) {
    if (!statement) return;

    if (statement instanceof VarDecl) {
      const valType = statement.valueExpr ? this.inferType(statement.valueExpr) : "any";
      this.declareVariable(statement.name, valType);
      if (statement.valueExpr) {
        this.checkExpression(statement.valueExpr);
      }
    }
    else if (statement instanceof FnDecl) {
      this.pushScope();
      if (this.currentStruct && !this.inStaticMethod) {
        this.declareVariable("this", this.currentStruct.name);
        this.declareVariable("self", this.currentStruct.name);
        const { allFields } = this.resolveStructMembers(this.currentStruct.name);
        for (const field of allFields) {
          this.declareVariable(field.name, field.type);
        }
      }
      for (const param of statement.params) {
        this.declareVariable(param.name, param.type);
      }
      if (statement.body) {
        this.checkStatement(statement.body);
      }
      this.popScope();
      this.inferFunctionReturnType(statement);
    }
    else if (statement instanceof StructDecl) {
      const oldStruct = this.currentStruct;
      const oldStatic = this.inStaticMethod;
      this.currentStruct = statement;
      for (const method of statement.methods) {
        this.inStaticMethod = method.isStatic;
        this.checkStatement(method);
      }
      this.currentStruct = oldStruct;
      this.inStaticMethod = oldStatic;
    }
    else if (statement instanceof InterfaceDecl) {
      // Checked globally, nothing to check inside
    }
    else if (statement instanceof Block) {
      this.pushScope();
      for (const s of statement.statements) {
        this.checkStatement(s);
      }
      this.popScope();
    }
    else if (statement instanceof IfStmt) {
      this.checkExpression(statement.condition);
      this.checkStatement(statement.thenBranch);
      if (statement.elseIfs) {
        for (const branch of statement.elseIfs) {
          this.checkExpression(branch.cond);
          this.checkStatement(branch.body);
        }
      }
      if (statement.elseBranch) {
        this.checkStatement(statement.elseBranch);
      }
    }
    else if (statement instanceof ForStmt) {
      this.pushScope();
      const colType = statement.iterableExpr ? this.inferType(statement.iterableExpr) : "any";
      let loopVarType = "any";
      if (colType.startsWith("[]")) {
        loopVarType = colType.substring(2);
      } else if (colType === "int" || colType === "float" || colType === "number") {
        loopVarType = "number";
      }
      this.declareVariable(statement.varName, loopVarType);
      if (statement.iterableExpr) {
        this.checkExpression(statement.iterableExpr);
      }
      this.checkStatement(statement.body);
      this.popScope();
    }
    else if (statement instanceof MatchStmt) {
      this.checkExpression(statement.condition);
      for (const branch of statement.branches) {
        for (const cond of branch.conditions) {
          this.checkExpression(cond);
        }
        this.checkStatement(branch.body);
      }
      if (statement.alternate) {
        this.checkStatement(statement.alternate);
      }
    }
    else if (statement instanceof ImportStmt) {
      this.resolveAndCheckModule(statement.moduleName);
      this.declareVariable(statement.moduleName, "module");
    }
    else if (statement instanceof FromImportStmt) {
      this.resolveAndCheckModule(statement.moduleName);
      const modChecker = this.moduleCheckers.get(statement.moduleName);
      if (modChecker) {
        for (const name of statement.imports) {
          const type = modChecker.resolveIdentifierType(name);
          this.declareVariable(name, type);
        }
      }
    }
    else if (statement instanceof ReturnStmt) {
      if (statement.argument) {
        this.checkExpression(statement.argument);
      }
    }
    else if (statement instanceof ExprStmt) {
      this.checkExpression(statement.expression);
    }
    else if (statement instanceof Expr) {
      this.checkExpression(statement);
    }
  }

  checkExpression(expr) {
    if (!expr) return;

    if (expr instanceof IdentifierExpr) {
      if (this.currentStruct && this.inStaticMethod) {
        if (expr.name === "this" || expr.name === "self") {
          throw new Error(`TYPE CHECK ERROR: Cannot access '${expr.name}' from static method in struct '${this.currentStruct.name}'.`);
        }
        let isLocal = false;
        for (let i = this.scopes.length - 1; i >= 0; i--) {
          if (this.scopes[i].has(expr.name)) {
            isLocal = true;
            break;
          }
        }
        if (!isLocal) {
          const { allFields } = this.resolveStructMembers(this.currentStruct.name);
          if (allFields.some(f => f.name === expr.name)) {
            throw new Error(`TYPE CHECK ERROR: Cannot access instance field '${expr.name}' from static method in struct '${this.currentStruct.name}'.`);
          }
        }
      }
    }

    if (expr instanceof BinaryExpr) {
      this.checkExpression(expr.left);
      this.checkExpression(expr.right);

      if (expr.operator === '=') {
        const leftType = this.inferType(expr.left);
        const rightType = this.inferType(expr.right);
        if (!this.isCompatible(rightType, leftType)) {
          throw new Error(`TYPE CHECK ERROR: Cannot assign type '${rightType}' to target of type '${leftType}'.`);
        }
      }
      else if (expr.operator === ':') {
        const leftType = this.inferType(expr.left);
        const structDecl = this.findStruct(leftType);
        if (structDecl) {
          const { allMethods } = this.resolveStructMembers(leftType);
          if (expr.right instanceof CallExpr && expr.right.callee instanceof IdentifierExpr) {
            const calleeName = expr.right.callee.name;
            const method = allMethods.find(m => m.name === calleeName);
            if (method) {
              if (method.isStatic) {
                throw new Error(`TYPE CHECK ERROR: Cannot call static method '${calleeName}' of struct '${leftType}' on an instance.`);
              }
              // Validate arguments
              const argTypes = expr.right.args.map(a => this.inferType(a));
              if (method.params.length !== argTypes.length) {
                throw new Error(`TYPE CHECK ERROR: Method '${calleeName}' expected ${method.params.length} arguments, but got ${argTypes.length}.`);
              }
              for (let i = 0; i < method.params.length; i++) {
                if (!this.isCompatible(argTypes[i], method.params[i].type)) {
                  throw new Error(`TYPE CHECK ERROR: Argument ${i + 1} for method '${calleeName}' expected type '${method.params[i].type}', but got '${argTypes[i]}'.`);
                }
              }
            } else {
              throw new Error(`TYPE CHECK ERROR: Struct '${leftType}' does not have method '${calleeName}'.`);
            }
          } else if (expr.right instanceof IdentifierExpr) {
            const propName = expr.right.name;
            const method = allMethods.find(m => m.name === propName);
            if (method && method.isStatic) {
              throw new Error(`TYPE CHECK ERROR: Cannot access static method '${propName}' as instance member.`);
            }
          }
        }
      }
      else if (expr.operator === '::') {
        if (expr.left instanceof IdentifierExpr) {
          const structName = expr.left.name;
          const structDecl = this.findStruct(structName);
          if (structDecl) {
            const { allMethods } = this.resolveStructMembers(structName);
            if (expr.right instanceof CallExpr && expr.right.callee instanceof IdentifierExpr) {
              const calleeName = expr.right.callee.name;
              const method = allMethods.find(m => m.name === calleeName);
              if (!method) {
                throw new Error(`TYPE CHECK ERROR: Struct '${structName}' has no static method '${calleeName}'.`);
              }
              if (!method.isStatic) {
                throw new Error(`TYPE CHECK ERROR: Method '${calleeName}' of struct '${structName}' is not static.`);
              }
              // Validate arguments
              const argTypes = expr.right.args.map(a => this.inferType(a));
              if (method.params.length !== argTypes.length) {
                throw new Error(`TYPE CHECK ERROR: Static method '${calleeName}' expected ${method.params.length} arguments, but got ${argTypes.length}.`);
              }
              for (let i = 0; i < method.params.length; i++) {
                if (!this.isCompatible(argTypes[i], method.params[i].type)) {
                  throw new Error(`TYPE CHECK ERROR: Argument ${i + 1} for static method '${calleeName}' expected type '${method.params[i].type}', but got '${argTypes[i]}'.`);
                }
              }
            } else if (expr.right instanceof IdentifierExpr) {
              const methodName = expr.right.name;
              const method = allMethods.find(m => m.name === methodName);
              if (!method) {
                throw new Error(`TYPE CHECK ERROR: Struct '${structName}' has no static method '${methodName}'.`);
              }
              if (!method.isStatic) {
                throw new Error(`TYPE CHECK ERROR: Method '${methodName}' of struct '${structName}' is not static.`);
              }
            } else {
              throw new Error(`TYPE CHECK ERROR: Invalid static member access on struct '${structName}'.`);
            }
          }
        }
      }
    }
    else if (expr instanceof TernaryExpr) {
      this.checkExpression(expr.condition);
      this.checkExpression(expr.consequent);
      this.checkExpression(expr.alternate);
    }
    else if (expr instanceof VectorExpr) {
      if (expr.elements) {
        for (const el of expr.elements) {
          this.checkExpression(el);
        }
      }
      if (expr.lenExpr) this.checkExpression(expr.lenExpr);
      if (expr.initExpr) this.checkExpression(expr.initExpr);
    }
    else if (expr instanceof StructInstanceExpr) {
      const structDecl = this.findStruct(expr.structName);
      if (!structDecl) {
        throw new Error(`TYPE CHECK ERROR: Struct '${expr.structName}' is not defined.`);
      } else {
        const { allFields } = this.resolveStructMembers(expr.structName);
        for (const [key, valExpr] of Object.entries(expr.initializers)) {
          const field = allFields.find(f => f.name === key);
          if (!field) {
            throw new Error(`TYPE CHECK ERROR: Struct '${expr.structName}' does not have field '${key}'.`);
          }
          this.checkExpression(valExpr);
          const propType = this.inferType(valExpr);
          if (!this.isCompatible(propType, field.type)) {
            throw new Error(`TYPE CHECK ERROR: Cannot assign type '${propType}' to field '${key}' of type '${field.type}' in struct '${expr.structName}'.`);
          }
        }
      }
    }
    else if (expr instanceof CallExpr) {
      const argTypes = expr.args.map(a => this.inferType(a));
      for (const arg of expr.args) {
        this.checkExpression(arg);
      }

      if (expr.callee instanceof IdentifierExpr) {
        const calleeName = expr.callee.name;
        if (this.functions.has(calleeName)) {
          const fnDecl = this.functions.get(calleeName);
          if (fnDecl.params.length !== argTypes.length) {
            throw new Error(`TYPE CHECK ERROR: Function '${calleeName}' expected ${fnDecl.params.length} arguments, but got ${argTypes.length}.`);
          }
          for (let i = 0; i < fnDecl.params.length; i++) {
            if (!this.isCompatible(argTypes[i], fnDecl.params[i].type)) {
              throw new Error(`TYPE CHECK ERROR: Argument ${i + 1} for function '${calleeName}' expected type '${fnDecl.params[i].type}', but got '${argTypes[i]}'.`);
            }
          }
        }
      }
    }
    else if (expr instanceof FunctionLambdaExpression) {
      this.pushScope();
      for (const param of expr.parameters) {
        this.declareVariable(param.name, param.type);
      }
      this.checkStatement(expr.body);
      this.popScope();
    }
    else if (expr instanceof IndexAccessExpr) {
      this.checkExpression(expr.target);
      this.checkExpression(expr.index);
    }
    else if (expr instanceof MapExpr) {
      for (const entry of expr.entries) {
        this.checkExpression(entry.key);
        this.checkExpression(entry.value);
        const kType = this.inferType(entry.key);
        const vType = this.inferType(entry.value);
        if (!this.isCompatible(kType, expr.keyType)) {
          throw new Error(`TYPE CHECK ERROR: Map key expected type '${expr.keyType}', but got '${kType}'.`);
        }
        if (!this.isCompatible(vType, expr.valType)) {
          throw new Error(`TYPE CHECK ERROR: Map value expected type '${expr.valType}', but got '${vType}'.`);
        }
      }
    }
  }

  inferType(expr) {
    if (expr instanceof LiteralExpr) {
      if (expr.type === 'BOOLEAN') return 'bool';
      if (expr.type === 'NUMBER') return 'number';
      if (expr.type === 'STRING') return 'string';
      return 'any';
    }

    if (expr instanceof IdentifierExpr) {
      return this.resolveIdentifierType(expr.name);
    }

    if (expr instanceof VectorExpr) {
      if (expr.elements && expr.elements.length > 0) {
        const firstType = this.inferType(expr.elements[0]);
        return "[]" + firstType;
      }
      if (expr.typing) {
        return "[]" + expr.typing;
      }
      return "[]any";
    }

    if (expr instanceof StructInstanceExpr) {
      return expr.structName;
    }

    if (expr instanceof MapExpr) {
      return `map[${expr.keyType}, ${expr.valType}]`;
    }

    if (expr instanceof CallExpr) {
      if (expr.callee instanceof IdentifierExpr) {
        return this.resolveFunctionReturnType(expr.callee.name);
      }
      return "any";
    }

    if (expr instanceof BinaryExpr) {
      if (expr.operator === ':') {
        const leftType = this.inferType(expr.left);
        const structDecl = this.findStruct(leftType);
        if (structDecl) {
          const { allFields, allMethods } = this.resolveStructMembers(leftType);
          if (expr.right instanceof IdentifierExpr) {
            const field = allFields.find(f => f.name === expr.right.name);
            if (field) return field.type;
            const method = allMethods.find(m => m.name === expr.right.name && !m.isStatic);
            if (method) return this.getFunctionSignatureString(method);
          } else if (expr.right instanceof CallExpr && expr.right.callee instanceof IdentifierExpr) {
            const method = allMethods.find(m => m.name === expr.right.callee.name && !m.isStatic);
            if (method) return this.inferFunctionReturnType(method);
          }
        }
        if (leftType.startsWith("[]")) {
          const elemType = leftType.substring(2);
          if (expr.right instanceof IdentifierExpr && (expr.right.name === "len" || expr.right.name === "length")) {
            return "number";
          }
          if (expr.right instanceof CallExpr && expr.right.callee instanceof IdentifierExpr) {
            const callee = expr.right.callee.name;
            if (callee === "map") {
              if (expr.right.args.length > 0) {
                const callbackType = this.inferType(expr.right.args[0]);
                if (callbackType.startsWith("fn(")) {
                  const lastClose = callbackType.lastIndexOf(')');
                  if (lastClose !== -1 && lastClose < callbackType.length - 1) {
                    const retType = callbackType.substring(lastClose + 1).Trim ? callbackType.substring(lastClose + 1).Trim() : callbackType.substring(lastClose + 1).trim();
                    return "[]" + retType;
                  }
                }
              }
              return "[]any";
            }
            if (callee === "filter" || callee === "push" || callee === "add") {
              return leftType;
            }
            if (callee === "pop" || callee === "find") {
              return elemType;
            }
            if (callee === "find_index") {
              return "number";
            }
            if (callee === "any" || callee === "all") {
              return "bool";
            }
            if (callee === "each") {
              return "any";
            }
          }
        }
        if (leftType.startsWith("map[")) {
          if (expr.right instanceof IdentifierExpr && (expr.right.name === "len" || expr.right.name === "length")) {
            return "number";
          }
          if (expr.right instanceof CallExpr && expr.right.callee instanceof IdentifierExpr) {
            const callee = expr.right.callee.name;
            const commaIdx = leftType.indexOf(',');
            if (commaIdx !== -1) {
              const keyType = leftType.substring(4, commaIdx).trim();
              const valType = leftType.substring(commaIdx + 1, leftType.length - 1).trim();
              if (callee === "keys") {
                return "[]" + keyType;
              }
              if (callee === "values") {
                return "[]" + valType;
              }
              if (callee === "remove") {
                return valType;
              }
            }
          }
        }
        if (leftType === "string") {
          if (expr.right instanceof IdentifierExpr && (expr.right.name === "len" || expr.right.name === "length")) {
            return "number";
          }
          if (expr.right instanceof CallExpr && expr.right.callee instanceof IdentifierExpr) {
            const callee = expr.right.callee.name;
            if (callee === "lower" || callee === "upper" || callee === "trim" || callee === "replace") {
              return "string";
            }
            if (callee === "contains") {
              return "bool";
            }
            if (callee === "split") {
              return "[]string";
            }
          }
        }
        return "any";
      }

      if (expr.operator === '::') {
        if (expr.left instanceof IdentifierExpr) {
          const modName = expr.left.name;
          if (this.findEnum(modName)) {
            return modName;
          }
          const structDecl = this.findStruct(modName);
          if (structDecl) {
            const { allMethods } = this.resolveStructMembers(modName);
            if (expr.right instanceof IdentifierExpr) {
              const method = allMethods.find(m => m.name === expr.right.name && m.isStatic);
              if (method) return this.getFunctionSignatureString(method);
            } else if (expr.right instanceof CallExpr && expr.right.callee instanceof IdentifierExpr) {
              const method = allMethods.find(m => m.name === expr.right.callee.name && m.isStatic);
              if (method) return this.inferFunctionReturnType(method);
            }
            return "any";
          }
          const modChecker = this.moduleCheckers.get(modName);
          if (modChecker) {
            if (expr.right instanceof StructInstanceExpr) {
              return expr.right.structName;
            }
            if (expr.right instanceof CallExpr && expr.right.callee instanceof IdentifierExpr) {
              return modChecker.resolveFunctionReturnType(expr.right.callee.name);
            }
            if (expr.right instanceof IdentifierExpr) {
              return modChecker.resolveIdentifierType(expr.right.name);
            }
          }
        }
        return "any";
      }

      if (expr.operator === '+') {
        if (this.inferType(expr.left) === "string" || this.inferType(expr.right) === "string") {
          return "string";
        }
      }

      if (['==', '!=', '<', '<=', '>', '>=', 'in'].includes(expr.operator)) {
        return "bool";
      }

      return this.inferType(expr.left);
    }

    if (expr instanceof TernaryExpr) {
      return this.inferType(expr.consequent);
    }

    if (expr instanceof FunctionLambdaExpression) {
      return this.getFunctionSignatureString(null, null, expr);
    }

    if (expr instanceof IndexAccessExpr) {
      const targetType = this.inferType(expr.target);
      if (targetType.startsWith("[]")) {
        return targetType.substring(2);
      }
      if (targetType.startsWith("map[")) {
        const commaIdx = targetType.indexOf(',');
        if (commaIdx !== -1) {
          return targetType.substring(commaIdx + 1, targetType.length - 1).trim();
        }
      }
      if (targetType === "string") {
        return "string";
      }
      return "any";
    }

    return "any";
  }

  parseFunctionSignature(signature) {
    if (typeof signature !== 'string' || !signature.startsWith("fn(")) return null;

    let depth = 1;
    let closingParenIdx = -1;
    for (let i = 3; i < signature.length; i++) {
      if (signature[i] === '(') depth++;
      else if (signature[i] === ')') {
        depth--;
        if (depth === 0) {
          closingParenIdx = i;
          break;
        }
      }
    }

    if (closingParenIdx === -1) return null;

    const paramsStr = signature.substring(3, closingParenIdx).trim();
    let returnType = signature.substring(closingParenIdx + 1).trim();
    if (!returnType) {
      returnType = "any";
    }

    let paramsList = [];
    if (paramsStr === "...") {
      paramsList = null;
    } else if (paramsStr) {
      let nestedDepth = 0;
      let start = 0;
      for (let i = 0; i < paramsStr.length; i++) {
        const c = paramsStr[i];
        if (c === '(' || c === '[') nestedDepth++;
        else if (c === ')' || c === ']') nestedDepth--;
        else if (c === ',' && nestedDepth === 0) {
          paramsList.push(paramsStr.substring(start, i).trim());
          start = i + 1;
        }
      }
      paramsList.push(paramsStr.substring(start).trim());
    }

    return { paramsList, returnType };
  }

  isCompatible(srcType, destType) {
    if (destType === "any" || srcType === "any" || !destType) {
      return true;
    }

    if (srcType === destType) {
      return true;
    }

    if ((srcType === "int" || srcType === "float" || srcType === "number") &&
      (destType === "int" || destType === "float" || destType === "number")) {
      return true;
    }

    if (typeof srcType === 'string' && srcType.startsWith("fn(") &&
      typeof destType === 'string' && destType.startsWith("fn(")) {
      const srcSig = this.parseFunctionSignature(srcType);
      const destSig = this.parseFunctionSignature(destType);
      if (!srcSig || !destSig) {
        return false;
      }

      if (!this.isCompatible(srcSig.returnType, destSig.returnType)) {
        return false;
      }

      if (destSig.paramsList === null) {
        return true;
      }
      if (srcSig.paramsList === null) {
        return false;
      }
      if (srcSig.paramsList.length !== destSig.paramsList.length) {
        return false;
      }
      for (let i = 0; i < srcSig.paramsList.length; i++) {
        if (!this.isCompatible(destSig.paramsList[i], srcSig.paramsList[i])) {
          return false;
        }
      }
      return true;
    }

    const interfaceDecl = this.findInterface(destType);
    if (interfaceDecl) {
      const structDecl = this.findStruct(srcType);
      if (structDecl) {
        return this.implementsInterface(structDecl, interfaceDecl);
      }
      return false;
    }

    return false;
  }

  resolveStructMembers(structName) {
    const allFields = [];
    const allMethods = [];

    const structDecl = this.findStruct(structName);
    if (!structDecl) return { allFields, allMethods };

    if (structDecl.inheritedStructs) {
      for (const parentName of structDecl.inheritedStructs) {
        const parent = this.resolveStructMembers(parentName);
        allFields.push(...parent.allFields);
        allMethods.push(...parent.allMethods);
      }
    }

    for (const field of structDecl.fields) {
      const idx = allFields.findIndex(f => f.name === field.name);
      if (idx !== -1) allFields.splice(idx, 1);
      allFields.push(field);
    }
    for (const method of structDecl.methods) {
      const idx = allMethods.findIndex(m => m.name === method.name);
      if (idx !== -1) allMethods.splice(idx, 1);
      allMethods.push(method);
    }

    return { allFields, allMethods };
  }

  implementsInterface(structDecl, interfaceDecl) {
    const { allMethods } = this.resolveStructMembers(structDecl.name);
    const instanceMethods = allMethods.filter(m => !m.isStatic);
    for (const reqMethod of interfaceDecl.methods) {
      const implMethod = instanceMethods.find(m => m.name === reqMethod.name);
      if (!implMethod) {
        return false;
      }

      if (implMethod.params.length !== reqMethod.params.length) {
        return false;
      }

      for (let i = 0; i < reqMethod.params.length; i++) {
        const reqParamType = reqMethod.params[i].type;
        const implParamType = implMethod.params[i].type;
        if (!this.isCompatible(implParamType, reqParamType)) {
          return false;
        }
      }

      const reqRetType = this.inferFunctionReturnType(reqMethod);
      const implRetType = this.inferFunctionReturnType(implMethod);
      if (!this.isCompatible(implRetType, reqRetType)) {
        return false;
      }
    }

    return true;
  }

  inferFunctionReturnType(fn) {
    if (fn.returnType) {
      if (!fn.body) {
        return fn.returnType;
      }
      this.pushScope();
      for (const p of fn.params) {
        this.declareVariable(p.name, p.type || "any");
      }
      const returns = this.findReturnStatements(fn.body);
      for (const ret of returns) {
        const retType = ret.argument ? this.inferType(ret.argument) : "any";
        if (!this.isCompatible(retType, fn.returnType)) {
          throw new Error(`TYPE CHECK ERROR: Function '${fn.name}' declared return type '${fn.returnType}', but returned '${retType}'.`);
        }
      }
      this.popScope();
      return fn.returnType;
    }

    if (!fn.body) {
      return "any";
    }

    this.pushScope();
    for (const p of fn.params) {
      this.declareVariable(p.name, p.type || "any");
    }

    const returns2 = this.findReturnStatements(fn.body);
    if (returns2.length === 0) {
      this.popScope();
      return "any";
    }

    let firstRetType = "any";
    let first = true;
    for (const ret of returns2) {
      const retType = ret.argument ? this.inferType(ret.argument) : "any";
      if (first) {
        firstRetType = retType;
        first = false;
      } else {
        if (!this.isCompatible(retType, firstRetType) && !this.isCompatible(firstRetType, retType)) {
          throw new Error(`TYPE CHECK ERROR: Function '${fn.name}' has conflicting return types '${firstRetType}' and '${retType}'.`);
        }
      }
    }

    this.popScope();
    return firstRetType;
  }

  findReturnStatements(stmt) {
    const list = [];
    this.findReturnStatementsRecursive(stmt, list);
    return list;
  }

  findReturnStatementsRecursive(stmt, list) {
    if (!stmt) return;

    if (stmt instanceof ReturnStmt) {
      list.push(stmt);
      return;
    }

    if (stmt instanceof Block) {
      for (const s of stmt.statements) {
        this.findReturnStatementsRecursive(s, list);
      }
    } else if (stmt instanceof IfStmt) {
      this.findReturnStatementsRecursive(stmt.thenBranch, list);
      if (stmt.elseIfs) {
        for (const branch of stmt.elseIfs) {
          this.findReturnStatementsRecursive(branch.body, list);
        }
      }
      if (stmt.elseBranch) {
        this.findReturnStatementsRecursive(stmt.elseBranch, list);
      }
    } else if (stmt instanceof ForStmt) {
      this.findReturnStatementsRecursive(stmt.body, list);
    } else if (stmt instanceof MatchStmt) {
      for (const branch of stmt.branches) {
        this.findReturnStatementsRecursive(branch.body, list);
      }
      if (stmt.alternate) {
        this.findReturnStatementsRecursive(stmt.alternate, list);
      }
    }
  }
}

// Global entry point function to execute code string
function runPinoCode(sourceCode, onOutput, onInput) {
  try {
    const lexer = new Lexer(sourceCode);
    const tokens = lexer.tokenize();
    const parser = new Parser(tokens);
    const statements = parser.parse();

    // Static Type Checker
    const checker = new TypeChecker();
    checker.check(statements);

    const interpreter = new Interpreter(onOutput, onInput);
    interpreter.execute(statements);
  } catch (err) {
    onOutput(`[ERROR] ${err.message}\n`);
  }
}

// Export modules for node or browser usage
if (typeof module !== 'undefined' && module.exports) {
  module.exports = { Lexer, Parser, Environment, PinoModule, Interpreter, TypeChecker, runPinoCode };
}
