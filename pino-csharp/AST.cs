using System.Collections.Generic;
using System.Linq;

namespace Pino;

// Base AST Node
public abstract record ASTNode {
  public bool IsPrelude { get; set; } = false;
}

// --- STATEMENTS ---
public abstract record Statement : ASTNode;

public record ProgramStatement(List<Statement> Statements) : Statement {
  public string FilePath { get; set; } = "";
}

public record BlockStatement(List<Statement> Statements) : Statement;

public record ReturnStatement(Expression? Argument) : Statement;

public record YieldStatement(Expression Value) : Statement;

public enum LoopKind {
  ForIn,
  ForTimes,
  Infinite,
  While
}

public record LoopStatement : Statement {
  public LoopKind Kind { get; set; }
  public Expression? Begin { get; set; }
  public Expression? End { get; set; }
  public Statement Body { get; set; }
  public string? KeyVar { get; set; }

  public LoopStatement(LoopKind kind, Expression? begin, Expression? end, Statement body, string? keyVar = null) {
    Kind = kind;
    Begin = begin;
    End = end;
    Body = body;
    KeyVar = keyVar;
  }
}

public record IfStatement(Expression Condition, Statement Consequent, Statement? Alternate) : Statement;

public record ElseStatement(Statement Body) : Statement;

public record WhenStatement(List<Pattern> Conditions, Statement Body) : Statement;

public record MatchStatement(Expression Condition, List<WhenStatement> Branches, ElseStatement? Alternate) : Expression;

// --- DECLARATIONS ---
public abstract record Declaration(string Identifier, bool IsPublic = false) : Statement;

public enum VariableKind {
  Constant,
  Variable,
  Parameter,
  Property
}

public record GenericParam(string Name, string? Constraint = null);

public record VariableDeclaration(VariableKind Kind, string Identifier, Expression? Value, string Typing = "", bool IsPublic = false) : Declaration(Identifier, IsPublic);

public record FunctionDeclaration(string Identifier, List<VariableDeclaration> Parameters, Statement? Body, string ReturnType = "", bool IsStatic = false, bool IsPublic = false, List<GenericParam>? GenericParams = null) : Declaration(Identifier, IsPublic) {
  public List<VariableDeclaration>? TupleReturnType { get; set; } = null;
  public string InferredReturnType { get; set; } = "";
  public string ResolvedReturnType => string.IsNullOrEmpty(InferredReturnType) ? ReturnType : InferredReturnType;
}

public record StructDeclaration(string Identifier, List<VariableDeclaration> Fields, List<FunctionDeclaration> Methods, List<string> InheritedStructs, List<GenericParam>? GenericParams = null, bool IsPublic = false) : Declaration(Identifier, IsPublic);

public record InterfaceDeclaration(string Identifier, List<VariableDeclaration> Fields, List<FunctionDeclaration> Methods, List<GenericParam>? GenericParams = null, bool IsPublic = false) : Declaration(Identifier, IsPublic);

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

public record IdentifierExpression : Expression {
  public string Name { get; set; }
  public IdentifierExpression(string name) {
    Name = name;
  }
}

public record BinaryExpression(Expression Left, OperatorType Operator, Expression Right) : Expression;

public record UnaryExpression(OperatorType Operator, Expression Right) : Expression;

public record BubbleExpression(Expression Value) : Expression;

public record IsExpression(Expression Value, Pattern Pattern, bool IsNot) : Expression;

public record RecoveryExpression(Expression Value, Statement Body) : Expression;

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

public record FunctionCallExpression : Expression {
  public string Callee { get; set; }
  public List<Expression> Arguments { get; init; }
  public List<string>? GenericArgs { get; init; }
  public FunctionCallExpression(string callee, List<Expression> arguments, List<string>? genericArgs = null) {
    Callee = callee;
    Arguments = arguments;
    GenericArgs = genericArgs;
  }
}

public record FunctionLambdaExpression(List<VariableDeclaration> Parameters, Statement Body) : Expression {
  public string LambdaId { get; set; } = "";
  public HashSet<string> FreeVars { get; } = new HashSet<string>();
}

public record IndexAccessExpression(Expression Target, Expression Index) : Expression;

public record MapExpression(string KeyType, string ValueType, List<KeyValuePair<Expression, Expression>> Entries) : Expression;

// --- UNION & PATTERN DEVELOPMENTS ---
public record UnionVariant(string Identifier, List<string> AssociatedTypes);
public record UnionDeclaration(string Identifier, List<UnionVariant> Variants, List<GenericParam>? GenericParams = null, bool IsPublic = false) : Declaration(Identifier, IsPublic);

public abstract record Pattern;
public record LiteralPattern(Expression Value) : Pattern;
public record IdentifierPattern(string Name) : Pattern;
public record VariantPattern : Pattern {
  public string UnionName { get; set; }
  public string VariantName { get; init; }
  public List<Pattern> SubPatterns { get; init; }
  public VariantPattern(string unionName, string variantName, List<Pattern> subPatterns) {
    UnionName = unionName;
    VariantName = variantName;
    SubPatterns = subPatterns;
  }
}

// --- TUPLE DEVELOPMENTS ---
public record TupleField(string Label, Expression Value) : ASTNode;
public record TupleLiteralExpression(List<TupleField> Fields) : Expression;
public record TupleDestructureField(string Label, string Identifier);
public record TupleDestructuringDeclaration(VariableKind Kind, List<TupleDestructureField> Fields, Expression Value) : Statement;

public class PinoTupleResult {
  public Dictionary<string, object?> Fields { get; }
  public PinoTupleResult(Dictionary<string, object?> fields) {
    Fields = fields;
  }
  public override string ToString() {
    return "(" + string.Join(", ", Fields.Select(f => $"{f.Key}: {f.Value}")) + ")";
  }
}
