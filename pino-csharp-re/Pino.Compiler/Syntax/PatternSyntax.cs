using System.Collections.Immutable;

using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Syntax;

public sealed record IdentifierPatternSyntax(
    int Id,
    SourceSpan Span,
    Token IdentifierToken) : PatternSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return IdentifierToken;
  }
}

public sealed record LiteralPatternSyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Literal) : PatternSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Literal;
  }
}

public sealed record VariantPatternSyntax(
    int Id,
    SourceSpan Span,
    Token TypeToken,
    TypeArgumentListSyntax? TypeArguments,
    Token ColonColonToken,
    Token VariantToken,
    Token? OpenParenToken,
    ImmutableArray<PatternSyntax> SubPatterns,
    Token? CloseParenToken,
    ImmutableArray<Token> CommaTokens = default) : PatternSyntax(Id, Span) {
  public ImmutableArray<Token> CommaTokens { get; init; } = CommaTokens.IsDefault ? [] : CommaTokens;

  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return TypeToken;
    if (TypeArguments is not null) {
      yield return TypeArguments;
    }
    yield return ColonColonToken;
    yield return VariantToken;
    if (OpenParenToken is { } openParen) {
      yield return openParen;
    }
    foreach (SyntaxElement item in InterleaveBySpan(SubPatterns, CommaTokens)) {
      yield return item;
    }
    if (CloseParenToken is { } closeParen) {
      yield return closeParen;
    }
  }
}

public sealed record MissingPatternSyntax(
    int Id,
    SourceSpan Span) : PatternSyntax(Id, Span) {
  public override bool IsMissing => true;

  public override IEnumerable<SyntaxElement> GetChildren() => [];
}
