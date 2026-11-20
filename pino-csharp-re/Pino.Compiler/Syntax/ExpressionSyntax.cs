using System.Collections.Immutable;

using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Syntax;

public sealed record LiteralExpressionSyntax(
    int Id,
    SourceSpan Span,
    Token Token) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Token;
  }
}

public sealed record IdentifierExpressionSyntax(
    int Id,
    SourceSpan Span,
    Token IdentifierToken) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return IdentifierToken;
  }
}

public abstract record StringPartSyntax(int Id, SourceSpan Span) : SyntaxNode(Id, Span);

public sealed record StringTextPartSyntax(
    int Id,
    SourceSpan Span,
    Token TextToken) : StringPartSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return TextToken;
  }
}

public sealed record StringInterpolationPartSyntax(
    int Id,
    SourceSpan Span,
    Token InterpStartToken,
    ExpressionSyntax Expression,
    Token InterpEndToken) : StringPartSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return InterpStartToken;
    yield return Expression;
    yield return InterpEndToken;
  }
}

public sealed record InterpolatedStringExpressionSyntax(
    int Id,
    SourceSpan Span,
    Token StringStartToken,
    ImmutableArray<StringPartSyntax> Parts,
    Token StringEndToken) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return StringStartToken;
    foreach (StringPartSyntax part in Parts) {
      yield return part;
    }
    yield return StringEndToken;
  }
}

public sealed record ParenthesizedExpressionSyntax(
    int Id,
    SourceSpan Span,
    Token OpenParenToken,
    ExpressionSyntax Expression,
    Token CloseParenToken) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return OpenParenToken;
    yield return Expression;
    yield return CloseParenToken;
  }
}

public sealed record UnaryExpressionSyntax(
    int Id,
    SourceSpan Span,
    Token OperatorToken,
    ExpressionSyntax Operand) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return OperatorToken;
    yield return Operand;
  }
}

public sealed record BinaryExpressionSyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Left,
    Token OperatorToken,
    ExpressionSyntax Right,
    Token? SecondaryOperatorToken = null) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Left;
    yield return OperatorToken;
    if (SecondaryOperatorToken is { } secondary) {
      yield return secondary;
    }
    yield return Right;
  }
}

public sealed record IsPatternExpressionSyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Expression,
    Token IsKeyword,
    Token? NotKeyword,
    PatternSyntax Pattern) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Expression;
    yield return IsKeyword;
    if (NotKeyword is { } notKeyword) {
      yield return notKeyword;
    }
    yield return Pattern;
  }
}

public sealed record AssignmentExpressionSyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Target,
    Token OperatorToken,
    ExpressionSyntax Value) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Target;
    yield return OperatorToken;
    yield return Value;
  }
}

public sealed record IndexExpressionSyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Target,
    Token OpenBracketToken,
    ExpressionSyntax Index,
    Token CloseBracketToken) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Target;
    yield return OpenBracketToken;
    yield return Index;
    yield return CloseBracketToken;
  }
}

public sealed record MemberAccessExpressionSyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Target,
    Token ColonToken,
    Token MemberToken) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Target;
    yield return ColonToken;
    yield return MemberToken;
  }
}

public sealed record StaticAccessExpressionSyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Target,
    Token ColonColonToken,
    Token MemberToken,
    TypeArgumentListSyntax? TargetTypeArguments = null) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Target;
    if (TargetTypeArguments is not null) {
      yield return TargetTypeArguments;
    }
    yield return ColonColonToken;
    yield return MemberToken;
  }
}

public sealed record ArgumentSyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Expression,
    Token? CommaToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Expression;
    if (CommaToken is { } comma) {
      yield return comma;
    }
  }
}

public sealed record CallExpressionSyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Callee,
    Token OpenParenToken,
    ImmutableArray<ArgumentSyntax> Arguments,
    Token CloseParenToken,
    TypeArgumentListSyntax? TypeArguments = null) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Callee;
    if (TypeArguments is not null) {
      yield return TypeArguments;
    }
    yield return OpenParenToken;
    foreach (ArgumentSyntax argument in Arguments) {
      yield return argument;
    }
    yield return CloseParenToken;
  }
}

public sealed record BubbleExpressionSyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Expression,
    Token QuestionToken) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Expression;
    yield return QuestionToken;
  }
}

public sealed record RecoveryBlockSyntax(
    int Id,
    SourceSpan Span,
    Token OpenBraceToken,
    ImmutableArray<SyntaxNode> Items,
    Token CloseBraceToken) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return OpenBraceToken;
    foreach (SyntaxNode item in Items) {
      yield return item;
    }
    yield return CloseBraceToken;
  }
}

public sealed record RecoveryExpressionSyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Expression,
    Token OrKeyword,
    RecoveryBlockSyntax Block) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Expression;
    yield return OrKeyword;
    yield return Block;
  }
}

public sealed record VectorLiteralExpressionSyntax(
    int Id,
    SourceSpan Span,
    Token OpenBracketToken,
    ImmutableArray<ExpressionSyntax> Elements,
    Token CloseBracketToken,
    ImmutableArray<Token> CommaTokens = default) : ExpressionSyntax(Id, Span) {
  public ImmutableArray<Token> CommaTokens { get; init; } = CommaTokens.IsDefault ? [] : CommaTokens;

  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return OpenBracketToken;
    foreach (SyntaxElement item in InterleaveBySpan(Elements, CommaTokens)) {
      yield return item;
    }
    yield return CloseBracketToken;
  }
}

public sealed record VectorPropertySyntax(
    int Id,
    SourceSpan Span,
    Token NameToken,
    Token ColonToken,
    ExpressionSyntax Value,
    Token? CommaToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return NameToken;
    yield return ColonToken;
    yield return Value;
    if (CommaToken is { } comma) {
      yield return comma;
    }
  }
}

public sealed record VectorInitializerExpressionSyntax(
    int Id,
    SourceSpan Span,
    Token OpenBracketToken,
    Token CloseBracketToken,
    TypeSyntax ElementType,
    Token? OpenBraceToken,
    ImmutableArray<VectorPropertySyntax> Properties,
    Token? CloseBraceToken) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return OpenBracketToken;
    yield return CloseBracketToken;
    yield return ElementType;
    if (OpenBraceToken is { } openBrace) {
      yield return openBrace;
    }
    foreach (VectorPropertySyntax property in Properties) {
      yield return property;
    }
    if (CloseBraceToken is { } closeBrace) {
      yield return closeBrace;
    }
  }
}

public sealed record MapEntrySyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Key,
    Token ArrowToken,
    ExpressionSyntax Value,
    Token? CommaToken = null,
    Token? SecondaryArrowToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Key;
    yield return ArrowToken;
    if (SecondaryArrowToken is { } secondaryArrow) {
      yield return secondaryArrow;
    }
    yield return Value;
    if (CommaToken is { } comma) {
      yield return comma;
    }
  }
}

public sealed record MapLiteralExpressionSyntax(
    int Id,
    SourceSpan Span,
    MapTypeSyntax Type,
    Token? OpenBraceToken,
    ImmutableArray<MapEntrySyntax> Entries,
    Token? CloseBraceToken) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Type;
    if (OpenBraceToken is { } openBrace) {
      yield return openBrace;
    }
    foreach (MapEntrySyntax entry in Entries) {
      yield return entry;
    }
    if (CloseBraceToken is { } closeBrace) {
      yield return closeBrace;
    }
  }
}

public sealed record SetLiteralExpressionSyntax(
    int Id,
    SourceSpan Span,
    SetTypeSyntax Type,
    Token? OpenBraceToken,
    ImmutableArray<ExpressionSyntax> Elements,
    Token? CloseBraceToken,
    ImmutableArray<Token> CommaTokens = default) : ExpressionSyntax(Id, Span) {
  public ImmutableArray<Token> CommaTokens { get; init; } = CommaTokens.IsDefault ? [] : CommaTokens;

  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Type;
    if (OpenBraceToken is { } openBrace) {
      yield return openBrace;
    }
    foreach (SyntaxElement item in InterleaveBySpan(Elements, CommaTokens)) {
      yield return item;
    }
    if (CloseBraceToken is { } closeBrace) {
      yield return closeBrace;
    }
  }
}

public sealed record StructFieldInitSyntax(
    int Id,
    SourceSpan Span,
    Token NameToken,
    Token? ColonToken,
    ExpressionSyntax? Value,
    Token? CommaToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return NameToken;
    if (ColonToken is { } colon) {
      yield return colon;
    }
    if (Value is not null) {
      yield return Value;
    }
    if (CommaToken is { } comma) {
      yield return comma;
    }
  }
}

public sealed record StructLiteralExpressionSyntax(
    int Id,
    SourceSpan Span,
    TypeSyntax TypeName,
    Token OpenBraceToken,
    ImmutableArray<StructFieldInitSyntax> Fields,
    Token CloseBraceToken) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return TypeName;
    yield return OpenBraceToken;
    foreach (StructFieldInitSyntax field in Fields) {
      yield return field;
    }
    yield return CloseBraceToken;
  }
}

public sealed record TupleFieldSyntax(
    int Id,
    SourceSpan Span,
    Token NameToken,
    Token? ColonToken,
    ExpressionSyntax? Value,
    Token? CommaToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return NameToken;
    if (ColonToken is { } colon) {
      yield return colon;
    }
    if (Value is not null) {
      yield return Value;
    }
    if (CommaToken is { } comma) {
      yield return comma;
    }
  }
}

public sealed record TupleLiteralExpressionSyntax(
    int Id,
    SourceSpan Span,
    Token AtToken,
    Token OpenParenToken,
    ImmutableArray<TupleFieldSyntax> Fields,
    Token CloseParenToken) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return AtToken;
    yield return OpenParenToken;
    foreach (TupleFieldSyntax field in Fields) {
      yield return field;
    }
    yield return CloseParenToken;
  }
}

public sealed record LambdaExpressionSyntax(
    int Id,
    SourceSpan Span,
    Token FnKeyword,
    Token OpenParenToken,
    ImmutableArray<ParameterSyntax> Parameters,
    Token CloseParenToken,
    Token? ArrowToken,
    ExpressionSyntax? BodyExpression,
    BlockStatementSyntax? BodyBlock,
    Token? SecondaryArrowToken = null) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return FnKeyword;
    yield return OpenParenToken;
    foreach (ParameterSyntax parameter in Parameters) {
      yield return parameter;
    }
    yield return CloseParenToken;
    if (ArrowToken is { } arrow) {
      yield return arrow;
    }
    if (SecondaryArrowToken is { } secondaryArrow) {
      yield return secondaryArrow;
    }
    if (BodyExpression is not null) {
      yield return BodyExpression;
    }
    if (BodyBlock is not null) {
      yield return BodyBlock;
    }
  }
}

public sealed record IfExpressionSyntax(
    int Id,
    SourceSpan Span,
    Token IfKeyword,
    ExpressionSyntax Condition,
    Token ThenKeyword,
    ExpressionSyntax ThenBranch,
    Token ElseKeyword,
    ExpressionSyntax ElseBranch) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return IfKeyword;
    yield return Condition;
    yield return ThenKeyword;
    yield return ThenBranch;
    yield return ElseKeyword;
    yield return ElseBranch;
  }
}

public sealed record WhenArmSyntax(
    int Id,
    SourceSpan Span,
    Token WhenKeyword,
    ImmutableArray<PatternSyntax> Patterns,
    Token? ArrowToken,
    ExpressionSyntax? ExpressionBody,
    BlockStatementSyntax? BlockBody,
    ImmutableArray<Token> CommaTokens = default,
    Token? SecondaryArrowToken = null) : SyntaxNode(Id, Span) {
  public ImmutableArray<Token> CommaTokens { get; init; } = CommaTokens.IsDefault ? [] : CommaTokens;

  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return WhenKeyword;
    foreach (SyntaxElement item in InterleaveBySpan(Patterns, CommaTokens)) {
      yield return item;
    }
    if (ArrowToken is { } arrow) {
      yield return arrow;
    }
    if (SecondaryArrowToken is { } secondaryArrow) {
      yield return secondaryArrow;
    }
    if (ExpressionBody is not null) {
      yield return ExpressionBody;
    }
    if (BlockBody is not null) {
      yield return BlockBody;
    }
  }
}

public sealed record ElseArmSyntax(
    int Id,
    SourceSpan Span,
    Token ElseKeyword,
    Token? ArrowToken,
    ExpressionSyntax? ExpressionBody,
    BlockStatementSyntax? BlockBody,
    Token? SecondaryArrowToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return ElseKeyword;
    if (ArrowToken is { } arrow) {
      yield return arrow;
    }
    if (SecondaryArrowToken is { } secondaryArrow) {
      yield return secondaryArrow;
    }
    if (ExpressionBody is not null) {
      yield return ExpressionBody;
    }
    if (BlockBody is not null) {
      yield return BlockBody;
    }
  }
}

public sealed record ErrorMatchArmSyntax(
    int Id,
    SourceSpan Span,
    ImmutableArray<Token> SkippedTokens) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    foreach (Token token in SkippedTokens) {
      yield return token;
    }
  }
}

public sealed record MatchExpressionSyntax(
    int Id,
    SourceSpan Span,
    Token MatchKeyword,
    ExpressionSyntax Subject,
    Token OpenBraceToken,
    ImmutableArray<SyntaxNode> Arms,
    Token CloseBraceToken) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return MatchKeyword;
    yield return Subject;
    yield return OpenBraceToken;
    foreach (SyntaxNode arm in Arms) {
      yield return arm;
    }
    yield return CloseBraceToken;
  }
}

public sealed record MissingExpressionSyntax(
    int Id,
    SourceSpan Span) : ExpressionSyntax(Id, Span) {
  public override bool IsMissing => true;

  public override IEnumerable<SyntaxElement> GetChildren() => [];
}

public sealed record ErrorExpressionSyntax(
    int Id,
    SourceSpan Span,
    ImmutableArray<Token> SkippedTokens) : ExpressionSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    foreach (Token token in SkippedTokens) {
      yield return token;
    }
  }
}
