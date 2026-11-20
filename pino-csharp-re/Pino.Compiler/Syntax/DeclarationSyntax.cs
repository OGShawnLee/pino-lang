using System.Collections.Immutable;

using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Syntax;

public sealed record VisibilitySyntax(
    int Id,
    SourceSpan Span,
    Token? AtToken,
    Token KeywordOrIdentifierToken) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    if (AtToken is { } at) {
      yield return at;
    }
    yield return KeywordOrIdentifierToken;
  }
}

public sealed record GenericParameterSyntax(
    int Id,
    SourceSpan Span,
    Token NameToken,
    Token? IsKeyword,
    TypeSyntax? ConstraintType,
    Token? CommaToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return NameToken;
    if (IsKeyword is { } isKeyword) {
      yield return isKeyword;
    }
    if (ConstraintType is not null) {
      yield return ConstraintType;
    }
    if (CommaToken is { } comma) {
      yield return comma;
    }
  }
}

public sealed record GenericDecoratorSyntax(
    int Id,
    SourceSpan Span,
    Token FirstAtToken,
    Token SecondAtToken,
    Token GenericKeywordToken,
    Token OpenBracketToken,
    ImmutableArray<GenericParameterSyntax> Parameters,
    Token CloseBracketToken) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return FirstAtToken;
    yield return SecondAtToken;
    yield return GenericKeywordToken;
    yield return OpenBracketToken;
    foreach (GenericParameterSyntax parameter in Parameters) {
      yield return parameter;
    }
    yield return CloseBracketToken;
  }
}

public sealed record ParameterSyntax(
    int Id,
    SourceSpan Span,
    Token NameToken,
    TypeSyntax? Type,
    Token? EqualsToken,
    ExpressionSyntax? DefaultValue,
    Token? CommaToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return NameToken;
    if (Type is not null) {
      yield return Type;
    }
    if (EqualsToken is { } equals) {
      yield return equals;
    }
    if (DefaultValue is not null) {
      yield return DefaultValue;
    }
    if (CommaToken is { } comma) {
      yield return comma;
    }
  }
}

public sealed record VariableDeclarationSyntax(
    int Id,
    SourceSpan Span,
    Token KeywordToken,
    Token NameToken,
    Token EqualsToken,
    ExpressionSyntax Initializer,
    TypeSyntax? Type = null,
    VisibilitySyntax? Visibility = null) : DeclarationSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    if (Visibility is not null) {
      yield return Visibility;
    }
    yield return KeywordToken;
    yield return NameToken;
    if (Type is not null) {
      yield return Type;
    }
    yield return EqualsToken;
    yield return Initializer;
  }
}

public sealed record FunctionDeclarationSyntax(
    int Id,
    SourceSpan Span,
    VisibilitySyntax? Visibility,
    Token FnKeyword,
    Token NameToken,
    Token? OpenParenToken,
    ImmutableArray<ParameterSyntax> Parameters,
    Token? CloseParenToken,
    TypeSyntax? ReturnType,
    BlockStatementSyntax Body,
    GenericDecoratorSyntax? GenericDecorator = null,
    Token? ArrowToken = null,
    Token? SecondaryArrowToken = null) : DeclarationSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    if (GenericDecorator is not null) {
      yield return GenericDecorator;
    }
    if (Visibility is not null) {
      yield return Visibility;
    }
    yield return FnKeyword;
    yield return NameToken;
    if (OpenParenToken is { } openParen) {
      yield return openParen;
    }
    foreach (ParameterSyntax parameter in Parameters) {
      yield return parameter;
    }
    if (CloseParenToken is { } closeParen) {
      yield return closeParen;
    }
    if (ReturnType is not null) {
      yield return ReturnType;
    }
    if (ArrowToken is { } arrow) {
      yield return arrow;
    }
    if (SecondaryArrowToken is { } secondaryArrow) {
      yield return secondaryArrow;
    }
    yield return Body;
  }
}

public sealed record ModuleDeclarationSyntax(
    int Id,
    SourceSpan Span,
    Token ModuleKeyword,
    Token NameToken) : DeclarationSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return ModuleKeyword;
    yield return NameToken;
  }
}

public sealed record TestDeclarationSyntax(
    int Id,
    SourceSpan Span,
    Token TestKeyword,
    ExpressionSyntax Name,
    BlockStatementSyntax Body) : DeclarationSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return TestKeyword;
    yield return Name;
    yield return Body;
  }
}

public sealed record FieldDeclarationSyntax(
    int Id,
    SourceSpan Span,
    VisibilitySyntax? Visibility,
    Token NameToken,
    TypeSyntax Type) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    if (Visibility is not null) {
      yield return Visibility;
    }
    yield return NameToken;
    yield return Type;
  }
}

public sealed record EmbeddedStructSyntax(
    int Id,
    SourceSpan Span,
    Token NameToken) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return NameToken;
  }
}

public sealed record MethodDeclarationSyntax(
    int Id,
    SourceSpan Span,
    GenericDecoratorSyntax? GenericDecorator,
    VisibilitySyntax? Visibility,
    Token? StaticKeyword,
    Token FnKeyword,
    Token NameToken,
    Token? OpenParenToken,
    ImmutableArray<ParameterSyntax> Parameters,
    Token? CloseParenToken,
    TypeSyntax? ReturnType,
    BlockStatementSyntax Body,
    Token? ArrowToken = null,
    Token? SecondaryArrowToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    if (GenericDecorator is not null) {
      yield return GenericDecorator;
    }
    if (Visibility is not null) {
      yield return Visibility;
    }
    if (StaticKeyword is { } staticKeyword) {
      yield return staticKeyword;
    }
    yield return FnKeyword;
    yield return NameToken;
    if (OpenParenToken is { } openParen) {
      yield return openParen;
    }
    foreach (ParameterSyntax parameter in Parameters) {
      yield return parameter;
    }
    if (CloseParenToken is { } closeParen) {
      yield return closeParen;
    }
    if (ReturnType is not null) {
      yield return ReturnType;
    }
    if (ArrowToken is { } arrow) {
      yield return arrow;
    }
    if (SecondaryArrowToken is { } secondaryArrow) {
      yield return secondaryArrow;
    }
    yield return Body;
  }
}

public sealed record StructDeclarationSyntax(
    int Id,
    SourceSpan Span,
    GenericDecoratorSyntax? GenericDecorator,
    VisibilitySyntax? Visibility,
    Token StructKeyword,
    Token NameToken,
    Token OpenBraceToken,
    ImmutableArray<SyntaxNode> Members,
    Token CloseBraceToken) : DeclarationSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    bool inlineGeneric = GenericDecorator is not null && GenericDecorator.Span.Start > StructKeyword.Span.Start;
    if (GenericDecorator is not null && !inlineGeneric) {
      yield return GenericDecorator;
    }
    if (Visibility is not null) {
      yield return Visibility;
    }
    yield return StructKeyword;
    yield return NameToken;
    if (GenericDecorator is not null && inlineGeneric) {
      yield return GenericDecorator;
    }
    yield return OpenBraceToken;
    foreach (SyntaxNode member in Members) {
      yield return member;
    }
    yield return CloseBraceToken;
  }
}

public sealed record InterfacePropertySyntax(
    int Id,
    SourceSpan Span,
    Token NameToken,
    TypeSyntax Type) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return NameToken;
    yield return Type;
  }
}

public sealed record InterfaceMethodSyntax(
    int Id,
    SourceSpan Span,
    Token FnKeyword,
    Token NameToken,
    Token? OpenParenToken,
    ImmutableArray<ParameterSyntax> Parameters,
    Token? CloseParenToken,
    TypeSyntax? ReturnType) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return FnKeyword;
    yield return NameToken;
    if (OpenParenToken is { } openParen) {
      yield return openParen;
    }
    foreach (ParameterSyntax parameter in Parameters) {
      yield return parameter;
    }
    if (CloseParenToken is { } closeParen) {
      yield return closeParen;
    }
    if (ReturnType is not null) {
      yield return ReturnType;
    }
  }
}

public sealed record InterfaceDeclarationSyntax(
    int Id,
    SourceSpan Span,
    GenericDecoratorSyntax? GenericDecorator,
    VisibilitySyntax? Visibility,
    Token InterfaceKeyword,
    Token NameToken,
    Token OpenBraceToken,
    ImmutableArray<SyntaxNode> Members,
    Token CloseBraceToken) : DeclarationSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    if (GenericDecorator is not null) {
      yield return GenericDecorator;
    }
    if (Visibility is not null) {
      yield return Visibility;
    }
    yield return InterfaceKeyword;
    yield return NameToken;
    yield return OpenBraceToken;
    foreach (SyntaxNode member in Members) {
      yield return member;
    }
    yield return CloseBraceToken;
  }
}

public sealed record EnumMemberSyntax(
    int Id,
    SourceSpan Span,
    Token NameToken,
    Token? CommaToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return NameToken;
    if (CommaToken is { } comma) {
      yield return comma;
    }
  }
}

public sealed record EnumDeclarationSyntax(
    int Id,
    SourceSpan Span,
    VisibilitySyntax? Visibility,
    Token EnumKeyword,
    Token NameToken,
    Token OpenBraceToken,
    ImmutableArray<SyntaxNode> Members,
    Token CloseBraceToken) : DeclarationSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    if (Visibility is not null) {
      yield return Visibility;
    }
    yield return EnumKeyword;
    yield return NameToken;
    yield return OpenBraceToken;
    foreach (SyntaxNode member in Members) {
      yield return member;
    }
    yield return CloseBraceToken;
  }
}

public sealed record UnionVariantFieldSyntax(
    int Id,
    SourceSpan Span,
    Token? NameToken,
    TypeSyntax Type,
    Token? CommaToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    if (NameToken is { } name) {
      yield return name;
    }
    yield return Type;
    if (CommaToken is { } comma) {
      yield return comma;
    }
  }
}

public sealed record UnionVariantSyntax(
    int Id,
    SourceSpan Span,
    Token NameToken,
    Token? OpenParenToken,
    ImmutableArray<UnionVariantFieldSyntax> Fields,
    Token? CloseParenToken) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return NameToken;
    if (OpenParenToken is { } openParen) {
      yield return openParen;
    }
    foreach (UnionVariantFieldSyntax field in Fields) {
      yield return field;
    }
    if (CloseParenToken is { } closeParen) {
      yield return closeParen;
    }
  }
}

public sealed record UnionDeclarationSyntax(
    int Id,
    SourceSpan Span,
    GenericDecoratorSyntax? GenericDecorator,
    VisibilitySyntax? Visibility,
    Token UnionKeyword,
    Token NameToken,
    Token OpenBraceToken,
    ImmutableArray<SyntaxNode> Variants,
    Token CloseBraceToken) : DeclarationSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    bool inlineGeneric = GenericDecorator is not null && GenericDecorator.Span.Start > UnionKeyword.Span.Start;
    if (GenericDecorator is not null && !inlineGeneric) {
      yield return GenericDecorator;
    }
    if (Visibility is not null) {
      yield return Visibility;
    }
    yield return UnionKeyword;
    yield return NameToken;
    if (GenericDecorator is not null && inlineGeneric) {
      yield return GenericDecorator;
    }
    yield return OpenBraceToken;
    foreach (SyntaxNode variant in Variants) {
      yield return variant;
    }
    yield return CloseBraceToken;
  }
}

public sealed record ErrorMemberSyntax(
    int Id,
    SourceSpan Span,
    ImmutableArray<Token> SkippedTokens) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    foreach (Token token in SkippedTokens) {
      yield return token;
    }
  }
}
