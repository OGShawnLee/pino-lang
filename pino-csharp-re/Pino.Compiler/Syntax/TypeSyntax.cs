using System.Collections.Immutable;

using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Syntax;

public sealed record NamedTypeSyntax(
    int Id,
    SourceSpan Span,
    Token NameToken) : TypeSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return NameToken;
  }
}

public sealed record QualifiedTypeSyntax(
    int Id,
    SourceSpan Span,
    Token ModuleToken,
    Token ColonColonToken,
    Token NameToken) : TypeSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return ModuleToken;
    yield return ColonColonToken;
    yield return NameToken;
  }
}

public sealed record TypeArgumentListSyntax(
    int Id,
    SourceSpan Span,
    Token OpenBracketToken,
    ImmutableArray<TypeSyntax> Arguments,
    Token CloseBracketToken,
    ImmutableArray<Token> CommaTokens = default) : SyntaxNode(Id, Span) {
  public ImmutableArray<Token> CommaTokens { get; init; } = CommaTokens.IsDefault ? [] : CommaTokens;

  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return OpenBracketToken;
    foreach (SyntaxElement item in InterleaveBySpan(Arguments, CommaTokens)) {
      yield return item;
    }
    yield return CloseBracketToken;
  }
}

public sealed record GenericTypeSyntax(
    int Id,
    SourceSpan Span,
    TypeSyntax BaseType,
    TypeArgumentListSyntax TypeArguments) : TypeSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return BaseType;
    yield return TypeArguments;
  }
}

public sealed record VectorTypeSyntax(
    int Id,
    SourceSpan Span,
    Token OpenBracketToken,
    Token CloseBracketToken,
    TypeSyntax ElementType) : TypeSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return OpenBracketToken;
    yield return CloseBracketToken;
    yield return ElementType;
  }
}

public sealed record MapTypeSyntax(
    int Id,
    SourceSpan Span,
    Token MapKeywordToken,
    Token OpenBracketToken,
    TypeSyntax KeyType,
    Token CommaToken,
    TypeSyntax ValueType,
    Token CloseBracketToken) : TypeSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return MapKeywordToken;
    yield return OpenBracketToken;
    yield return KeyType;
    yield return CommaToken;
    yield return ValueType;
    yield return CloseBracketToken;
  }
}

public sealed record SetTypeSyntax(
    int Id,
    SourceSpan Span,
    Token SetKeywordToken,
    Token OpenBracketToken,
    TypeSyntax ElementType,
    Token CloseBracketToken) : TypeSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return SetKeywordToken;
    yield return OpenBracketToken;
    yield return ElementType;
    yield return CloseBracketToken;
  }
}

public sealed record FunctionTypeParameterSyntax(
    int Id,
    SourceSpan Span,
    Token? MutKeyword,
    TypeSyntax Type,
    Token? CommaToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    if (MutKeyword is { } mut) {
      yield return mut;
    }
    yield return Type;
    if (CommaToken is { } comma) {
      yield return comma;
    }
  }
}

public sealed record FunctionTypeSyntax(
    int Id,
    SourceSpan Span,
    Token FnKeyword,
    Token? OpenParenToken,
    ImmutableArray<FunctionTypeParameterSyntax> Parameters,
    Token? CloseParenToken,
    Token? ArrowToken,
    TypeSyntax? ReturnType,
    Token? SecondaryArrowToken = null) : TypeSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return FnKeyword;
    if (OpenParenToken is { } openParen) {
      yield return openParen;
    }
    foreach (FunctionTypeParameterSyntax parameter in Parameters) {
      yield return parameter;
    }
    if (CloseParenToken is { } closeParen) {
      yield return closeParen;
    }
    if (ArrowToken is { } arrow) {
      yield return arrow;
    }
    if (SecondaryArrowToken is { } secondaryArrow) {
      yield return secondaryArrow;
    }
    if (ReturnType is not null) {
      yield return ReturnType;
    }
  }
}

public sealed record TupleFieldTypeSyntax(
    int Id,
    SourceSpan Span,
    Token NameToken,
    TypeSyntax Type,
    Token? CommaToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return NameToken;
    yield return Type;
    if (CommaToken is { } comma) {
      yield return comma;
    }
  }
}

public sealed record TupleTypeSyntax(
    int Id,
    SourceSpan Span,
    Token AtToken,
    Token OpenParenToken,
    ImmutableArray<TupleFieldTypeSyntax> Fields,
    Token CloseParenToken) : TypeSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return AtToken;
    yield return OpenParenToken;
    foreach (TupleFieldTypeSyntax field in Fields) {
      yield return field;
    }
    yield return CloseParenToken;
  }
}

public sealed record MissingTypeSyntax(
    int Id,
    SourceSpan Span) : TypeSyntax(Id, Span) {
  public override bool IsMissing => true;

  public override IEnumerable<SyntaxElement> GetChildren() => [];
}
