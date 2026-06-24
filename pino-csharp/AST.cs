using System.Collections.Generic;

namespace Pino;

// Base AST Node
public abstract record ASTNode;

// --- STATEMENTS ---
public abstract record Statement : ASTNode;

public record ProgramStatement(List<Statement> Statements) : Statement;

public record BlockStatement(List<Statement> Statements) : Statement;

public record ReturnStatement(Expression? Argument) : Statement;

public enum LoopKind {
  ForIn,
  ForTimes,
  Infinite
}

public record LoopStatement(LoopKind Kind, Expression? Begin, Expression? End, Statement Body, string? KeyVar = null) : Statement;

public record IfStatement(Expression Condition, Statement Consequent, Statement? Alternate) : Statement;

public record ElseStatement(Statement Body) : Statement;

public record WhenStatement(List<Expression> Conditions, Statement Body) : Statement;

public record MatchStatement(Expression Condition, List<WhenStatement> Branches, ElseStatement? Alternate) : Statement;

// --- DECLARATIONS ---
public abstract record Declaration(string Identifier, bool IsPublic = false) : Statement;

public enum VariableKind {
  Constant,
  Variable,
  Parameter,
  Property
}

public record VariableDeclaration(VariableKind Kind, string Identifier, Expression? Value, string Typing = "", bool IsPublic = false) : Declaration(Identifier, IsPublic);

public record FunctionDeclaration(string Identifier, List<VariableDeclaration> Parameters, Statement? Body, string ReturnType = "", bool IsStatic = false, bool IsPublic = false) : Declaration(Identifier, IsPublic);

public record StructDeclaration(string Identifier, List<VariableDeclaration> Fields, List<FunctionDeclaration> Methods, List<string> InheritedStructs, List<string>? GenericParams = null, bool IsPublic = false) : Declaration(Identifier, IsPublic);

public record InterfaceDeclaration(string Identifier, List<VariableDeclaration> Fields, List<FunctionDeclaration> Methods, bool IsPublic = false) : Declaration(Identifier, IsPublic);

public record EnumDeclaration(string Identifier, List<string> Members, bool IsPublic = false) : Declaration(Identifier, IsPublic);

public record ModuleDeclaration(string Identifier) : Statement;

public record ImportStatement(string ModuleName) : Statement;

public record FromImportStatement(string ModuleName, List<string> Imports) : Statement;

// --- EXPRESSIONS ---
public abstract record Expression : Statement {
  public int Distance { get; set; } = -1;
  public string? InferredType { get; set; }
}

public record LiteralExpression(string Value, LiteralType LiteralType, List<string>? Injections = null) : Expression;

public record IdentifierExpression(string Name) : Expression;

public record BinaryExpression(Expression Left, OperatorType Operator, Expression Right) : Expression;

public record TernaryExpression(Expression Condition, Expression Consequent, Expression Alternate) : Expression;

public record VectorExpression(List<Expression>? Elements, Expression? Len = null, Expression? Init = null, string Typing = "") : Expression;

public record StructInstanceExpression : Expression {
  public string StructName { get; set; }
  public List<VariableDeclaration> Properties { get; init; }
  public List<string>? GenericArgs { get; init; }
  public StructInstanceExpression(string structName, List<VariableDeclaration> properties, List<string>? genericArgs = null) {
    StructName = structName;
    Properties = properties;
    GenericArgs = genericArgs;
  }
}

public record FunctionCallExpression(string Callee, List<Expression> Arguments) : Expression;

public record FunctionLambdaExpression(List<VariableDeclaration> Parameters, Statement Body) : Expression;

public record IndexAccessExpression(Expression Target, Expression Index) : Expression;

public record MapExpression(string KeyType, string ValueType, List<KeyValuePair<Expression, Expression>> Entries) : Expression;
