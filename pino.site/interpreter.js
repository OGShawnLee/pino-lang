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
      'var', 'val', 'fn', 'struct', 'enum', 'if', 'else', 'match', 'when', 'for', 'in', 'break', 'continue', 'return', 'true', 'false', 'then'
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
class Stmt {}
class Expr {}

class VarDecl extends Stmt {
  constructor(name, valueExpr, isConstant) {
    super();
    this.name = name;
    this.valueExpr = valueExpr;
    this.isConstant = isConstant;
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
  constructor(name, fields, methods) {
    super();
    this.name = name;
    this.fields = fields; // array of {name, type}
    this.methods = methods; // array of FnDecl
  }
}

class EnumDecl extends Stmt {
  constructor(name, members) {
    super();
    this.name = name;
    this.members = members; // array of string names
  }
}

class FnDecl extends Stmt {
  constructor(name, params, body) {
    super();
    this.name = name;
    this.params = params; // array of {name, type}
    this.body = body;
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
  constructor(elements, lenExpr, initExpr) {
    super();
    this.elements = elements; // array of Expr (or null)
    this.lenExpr = lenExpr;     // Expr for size (or null)
    this.initExpr = initExpr;   // Expr for values (or null)
  }
}

class FunctionLambdaExpression extends Expr {
  constructor(parameters, body) {
    super();
    this.parameters = parameters; // array of {name, type}
    this.body = body; // Block
  }
}

// Precedence-Climbing Parser
class Parser {
  constructor(tokens) {
    this.tokens = tokens;
    this.index = 0;
    this.scopes = [];
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
      
      let returnType = "";
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
    while (!this.isAtEnd()) {
      try {
        statements.push(this.statement());
      } catch (err) {
        this.popScope();
        throw err;
      }
    }
    this.popScope();
    return statements;
  }

  statement() {
    if (this.match(TokenType.KEYWORD, 'val')) return this.varDeclaration(true);
    if (this.match(TokenType.KEYWORD, 'var')) return this.varDeclaration(false);
    if (this.match(TokenType.KEYWORD, 'if')) return this.ifStatement();
    if (this.match(TokenType.KEYWORD, 'for')) return this.forStatement();
    if (this.match(TokenType.KEYWORD, 'match')) return this.matchStatement();
    if (this.match(TokenType.KEYWORD, 'struct')) return this.structDeclaration();
    if (this.match(TokenType.KEYWORD, 'enum')) return this.enumDeclaration();
    if (this.match(TokenType.KEYWORD, 'fn')) return this.fnDeclaration();
    if (this.match(TokenType.KEYWORD, 'return')) return this.returnStatement();
    if (this.match(TokenType.KEYWORD, 'break')) return new BreakStmt();
    if (this.match(TokenType.KEYWORD, 'continue')) return new ContinueStmt();

    return this.expressionStatement();
  }

  varDeclaration(isConstant) {
    const nameToken = this.consume(TokenType.IDENTIFIER, "Expect variable name");
    this.consume(TokenType.OPERATOR, "Expect '=' after variable name", '=');
    const valueExpr = this.expression();
    this.declareVariable(nameToken.value);
    return new VarDecl(nameToken.value, valueExpr, isConstant);
  }

  ifStatement() {
    // Condition (parentheses are optional in Pino)
    const condition = this.expression();
    this.consume(TokenType.DELIMITER, "Expect '{' before then branch body", '{');
    const thenBranch = this.block();

    const elseIfs = [];
    let elseBranch = null;

    while (this.match(TokenType.KEYWORD, 'else')) {
      if (this.match(TokenType.KEYWORD, 'if')) {
        const cond = this.expression();
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
    const iterableExpr = this.expression();
    this.pushScope();
    this.declareVariable(varToken.value);
    this.consume(TokenType.DELIMITER, "Expect '{' before loop body", '{');
    const body = this.block();
    this.popScope();
    return new ForStmt(varToken.value, iterableExpr, body, false);
  }

  matchStatement() {
    const condition = this.expression();
    this.consume(TokenType.DELIMITER, "Expect '{' to open match statement", '{');
    const branches = [];
    let alternate = null;

    while (!this.check(TokenType.DELIMITER, '}') && !this.isAtEnd()) {
      if (this.match(TokenType.KEYWORD, 'when')) {
        const conditions = [];
        do {
          conditions.push(this.expression());
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

  structDeclaration() {
    const nameToken = this.consume(TokenType.IDENTIFIER, "Expect struct name");
    this.consume(TokenType.DELIMITER, "Expect '{' after struct name", '{');
    
    const fields = [];
    const methods = [];

    while (!this.check(TokenType.DELIMITER, '}') && !this.isAtEnd()) {
      if (this.match(TokenType.KEYWORD, 'fn')) {
        methods.push(this.fnDeclaration());
      } else {
        const fieldNameToken = this.consume(TokenType.IDENTIFIER, "Expect field name");
        const fieldType = this.consumeTyping();
        fields.push({ name: fieldNameToken.value, type: fieldType });
        // Optional commas between fields
        this.match(TokenType.DELIMITER, ',');
      }
    }

    this.consume(TokenType.DELIMITER, "Expect '}' to close struct declaration", '}');
    return new StructDecl(nameToken.value, fields, methods);
  }

  enumDeclaration() {
    const nameToken = this.consume(TokenType.IDENTIFIER, "Expect enum name");
    this.consume(TokenType.DELIMITER, "Expect '{' after enum name", '{');
    const members = [];

    while (!this.check(TokenType.DELIMITER, '}') && !this.isAtEnd()) {
      const memberToken = this.consume(TokenType.IDENTIFIER, "Expect enum member name");
      members.push(memberToken.value);
      this.match(TokenType.DELIMITER, ',');
    }

    this.consume(TokenType.DELIMITER, "Expect '}' to close enum declaration", '}');
    return new EnumDecl(nameToken.value, members);
  }

  fnDeclaration() {
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

    this.pushScope();
    for (const p of params) {
      this.declareVariable(p.name);
    }
    this.consume(TokenType.DELIMITER, "Expect '{' to open function body", '{');
    const body = this.block();
    this.popScope();

    this.declareVariable(nameToken.value);

    return new FnDecl(nameToken.value, params, body);
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

  expression() {
    return this.ternary();
  }

  ternary() {
    return this.assignment();
  }

  assignment() {
    const expr = this.logicalOr();

    if (this.match(TokenType.OPERATOR)) {
      const opToken = this.previous();
      const opValue = opToken.value;
      if (['=', '+=', '-=', '*=', '/=', '%='].includes(opValue)) {
        const val = this.assignment();
        if (expr instanceof IdentifierExpr || expr instanceof BinaryExpr && expr.operator === ':') {
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

  logicalOr() {
    let expr = this.logicalAnd();
    while (this.match(TokenType.OPERATOR, '||')) {
      const op = this.previous().value;
      const right = this.logicalAnd();
      expr = new BinaryExpr(expr, op, right);
    }
    return expr;
  }

  logicalAnd() {
    let expr = this.equality();
    while (this.match(TokenType.OPERATOR, '&&')) {
      const op = this.previous().value;
      const right = this.equality();
      expr = new BinaryExpr(expr, op, right);
    }
    return expr;
  }

  equality() {
    let expr = this.comparison();
    while (this.match(TokenType.OPERATOR, '==') || this.match(TokenType.OPERATOR, '!=')) {
      const op = this.previous().value;
      const right = this.comparison();
      expr = new BinaryExpr(expr, op, right);
    }
    return expr;
  }

  comparison() {
    let expr = this.addition();
    while (this.match(TokenType.OPERATOR, '<') || this.match(TokenType.OPERATOR, '<=') || this.match(TokenType.OPERATOR, '>') || this.match(TokenType.OPERATOR, '>=')) {
      const op = this.previous().value;
      const right = this.addition();
      expr = new BinaryExpr(expr, op, right);
    }
    return expr;
  }

  addition() {
    let expr = this.multiplication();
    while (this.match(TokenType.OPERATOR, '+') || this.match(TokenType.OPERATOR, '-')) {
      const op = this.previous().value;
      const right = this.multiplication();
      expr = new BinaryExpr(expr, op, right);
    }
    return expr;
  }

  multiplication() {
    let expr = this.unary();
    while (this.match(TokenType.OPERATOR, '*') || this.match(TokenType.OPERATOR, '/') || this.match(TokenType.OPERATOR, '%')) {
      const op = this.previous().value;
      const right = this.unary();
      expr = new BinaryExpr(expr, op, right);
    }
    return expr;
  }

  unary() {
    if (this.match(TokenType.OPERATOR, '!') || this.match(TokenType.OPERATOR, '-')) {
      const op = this.previous().value;
      const right = this.unary();
      return new UnaryExpr(op, right);
    }
    return this.memberAccess();
  }

  memberAccess() {
    let expr = this.primary();

    while (true) {
      if (this.match(TokenType.OPERATOR, ':')) {
        const right = this.primary(); // can be method call or identifier
        expr = new BinaryExpr(expr, ':', right);
      } else if (this.match(TokenType.OPERATOR, '::')) {
        const right = this.primary();
        expr = new BinaryExpr(expr, '::', right);
      } else {
        break;
      }
    }

    return expr;
  }

  primary() {
    if (this.match(TokenType.KEYWORD, 'true')) return new LiteralExpr(true, 'BOOLEAN');
    if (this.match(TokenType.KEYWORD, 'false')) return new LiteralExpr(false, 'BOOLEAN');
    if (this.match(TokenType.KEYWORD, 'null')) return new LiteralExpr(null, 'NULL');

    if (this.match(TokenType.KEYWORD, 'if')) {
      const condition = this.expression();
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
        const prevToken = this.index - 2 >= 0 ? this.tokens[this.index - 2] : null;
        const isPrecededByStaticMemberAccess = prevToken && prevToken.value === '::';
        
        if (!isPrecededByStaticMemberAccess) {
          const next = this.peek();
          if (next && next.type === TokenType.DELIMITER && next.value === '{') {
            const nextNext = this.tokens[this.index + 1];
            if (nextNext && nextNext.type === TokenType.DELIMITER && nextNext.value === '}') {
              isStruct = true;
            } else if (nextNext && nextNext.type === TokenType.IDENTIFIER) {
              const nextNextNext = this.tokens[this.index + 2];
              if (nextNextNext && nextNextNext.type === TokenType.OPERATOR && nextNextNext.value === ':') {
                isStruct = true;
              }
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
        return new VectorExpr(null, lenExpr, initExpr);
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
class BreakException extends Error {}
class ContinueException extends Error {}

class Environment {
  constructor(parent = null) {
    this.parent = parent;
    this.records = new Map();
    this.constants = new Set();
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

// Tree-Walk Interpreter
class Interpreter {
  constructor(outputCallback = console.log, inputCallback = () => '') {
    this.outputCallback = outputCallback;
    this.inputCallback = inputCallback;
    this.globalEnv = new Environment();
    this.structs = new Map();
    this.enums = new Map();
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
      this.structs.set(stmt.name, stmt);
      // Also register struct constructor in env
      env.define(stmt.name, stmt, true);
    } else if (stmt instanceof EnumDecl) {
      this.enums.set(stmt.name, stmt.members);
      env.define(stmt.name, stmt, true);
    } else if (stmt instanceof FnDecl) {
      const callable = new PinoCallable(stmt, env);
      env.define(stmt.name, callable, true);
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
            const methodDecl = structDef.methods.find(m => m.name === right.callee.name);
            if (!methodDecl) throw new Error(`RUNTIME ERROR: Method '${right.callee.name}' not found on Struct '${target.structName}'.`);

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
              for (let i = 0; i < target.length; i++) {
                const arity = (func instanceof PinoCallable) ? func.fnDecl.params.length : -1;
                const args = arity === 2 ? [target[i], i] : [target[i]];
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
              const mapped = [];
              for (let i = 0; i < target.length; i++) {
                const arity = (func instanceof PinoCallable) ? func.fnDecl.params.length : -1;
                const args = arity === 2 ? [target[i], i] : [target[i]];
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
              const filtered = [];
              for (let i = 0; i < target.length; i++) {
                const arity = (func instanceof PinoCallable) ? func.fnDecl.params.length : -1;
                const args = arity === 2 ? [target[i], i] : [target[i]];
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

      // Static member access Enum::Member
      if (expr.operator === '::') {
        const enumName = expr.left.name;
        const memberName = expr.right.name;
        const members = this.enums.get(enumName);
        if (!members) throw new Error(`RUNTIME ERROR: Enum '${enumName}' not defined.`);
        if (!members.includes(memberName)) throw new Error(`RUNTIME ERROR: Enum member '${memberName}' not found on Enum '${enumName}'.`);
        return `${enumName}::${memberName}`;
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
      }

      // Standard binary arithmetic and comparison operators
      const left = this.evaluateExpression(expr.left, env);
      const right = this.evaluateExpression(expr.right, env);
      return this.evalOp(left, expr.operator, right);
    }

    if (expr instanceof FunctionLambdaExpression) {
      return new PinoCallable({ params: expr.parameters, body: expr.body }, env);
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

// Global entry point function to execute code string
function runPinoCode(sourceCode, onOutput, onInput) {
  try {
    const lexer = new Lexer(sourceCode);
    const tokens = lexer.tokenize();
    const parser = new Parser(tokens);
    const statements = parser.parse();
    const interpreter = new Interpreter(onOutput, onInput);
    interpreter.execute(statements);
  } catch (err) {
    onOutput(`[ERROR] ${err.message}\n`);
  }
}

// Export modules for node or browser usage
if (typeof module !== 'undefined' && module.exports) {
  module.exports = { Lexer, Parser, Interpreter, runPinoCode };
}
