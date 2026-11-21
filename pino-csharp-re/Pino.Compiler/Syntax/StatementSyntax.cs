using System.Collections.Immutable;

using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Syntax;

public sealed record BlockStatementSyntax(
    int Id,
    SourceSpan Span,
    Token OpenBraceToken,
    ImmutableArray<SyntaxNode> Items,
    Token CloseBraceToken) : StatementSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return OpenBraceToken;
    foreach (SyntaxNode item in Items) {
      yield return item;
    }
    yield return CloseBraceToken;
  }
}

public sealed record ExpressionStatementSyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Expression) : StatementSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Expression;
  }
}

public sealed record DestructureFieldSyntax(
    int Id,
    SourceSpan Span,
    Token LabelToken,
    Token? ColonToken,
    Token? VariableToken,
    Token? CommaToken = null) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return LabelToken;
    if (ColonToken is { } colon) {
      yield return colon;
    }
    if (VariableToken is { } variable) {
      yield return variable;
    }
    if (CommaToken is { } comma) {
      yield return comma;
    }
  }
}

public sealed record TupleDestructuringStatementSyntax(
    int Id,
    SourceSpan Span,
    Token KeywordToken,
    Token AtToken,
    Token OpenParenToken,
    ImmutableArray<DestructureFieldSyntax> Fields,
    Token CloseParenToken,
    Token EqualsToken,
    ExpressionSyntax Initializer,
    Token? MutKeyword = null) : StatementSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    if (MutKeyword is { } mut && mut.Span.Start < KeywordToken.Span.Start) {
      yield return mut;
      yield return KeywordToken;
    } else {
      yield return KeywordToken;
      if (MutKeyword is { } mutAfter) {
        yield return mutAfter;
      }
    }
    yield return AtToken;
    yield return OpenParenToken;
    foreach (DestructureFieldSyntax field in Fields) {
      yield return field;
    }
    yield return CloseParenToken;
    yield return EqualsToken;
    yield return Initializer;
  }
}

public sealed record IfStatementSyntax(
    int Id,
    SourceSpan Span,
    Token IfKeyword,
    ExpressionSyntax Condition,
    BlockStatementSyntax ThenBranch,
    Token? ElseKeyword,
    StatementSyntax? ElseBranch,
    Token? ElseArrowToken = null,
    Token? ElseSecondaryArrowToken = null) : StatementSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return IfKeyword;
    yield return Condition;
    yield return ThenBranch;
    if (ElseKeyword is { } elseKeyword) {
      yield return elseKeyword;
    }
    if (ElseArrowToken is { } elseArrow) {
      yield return elseArrow;
    }
    if (ElseSecondaryArrowToken is { } elseSecondaryArrow) {
      yield return elseSecondaryArrow;
    }
    if (ElseBranch is not null) {
      yield return ElseBranch;
    }
  }
}

public sealed record MatchStatementSyntax(
    int Id,
    SourceSpan Span,
    Token MatchKeyword,
    ExpressionSyntax Subject,
    Token OpenBraceToken,
    ImmutableArray<SyntaxNode> Arms,
    Token CloseBraceToken) : StatementSyntax(Id, Span) {
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

public abstract record ForHeaderSyntax(int Id, SourceSpan Span) : SyntaxNode(Id, Span);

public sealed record ForConditionHeaderSyntax(
    int Id,
    SourceSpan Span,
    ExpressionSyntax Condition) : ForHeaderSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return Condition;
  }
}

public sealed record ForInHeaderSyntax(
    int Id,
    SourceSpan Span,
    Token FirstBindingToken,
    Token? CommaToken,
    Token? SecondBindingToken,
    Token InKeyword,
    ExpressionSyntax Collection) : ForHeaderSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return FirstBindingToken;
    if (CommaToken is { } comma) {
      yield return comma;
    }
    if (SecondBindingToken is { } second) {
      yield return second;
    }
    yield return InKeyword;
    yield return Collection;
  }
}

public sealed record ForStatementSyntax(
    int Id,
    SourceSpan Span,
    Token ForKeyword,
    ForHeaderSyntax? Header,
    BlockStatementSyntax Body) : StatementSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return ForKeyword;
    if (Header is not null) {
      yield return Header;
    }
    yield return Body;
  }
}

public sealed record BreakStatementSyntax(
    int Id,
    SourceSpan Span,
    Token BreakKeyword) : StatementSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return BreakKeyword;
  }
}

public sealed record ContinueStatementSyntax(
    int Id,
    SourceSpan Span,
    Token ContinueKeyword) : StatementSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return ContinueKeyword;
  }
}

public sealed record ReturnStatementSyntax(
    int Id,
    SourceSpan Span,
    Token ReturnKeyword,
    ExpressionSyntax? Expression) : StatementSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return ReturnKeyword;
    if (Expression is not null) {
      yield return Expression;
    }
  }
}

public sealed record YieldStatementSyntax(
    int Id,
    SourceSpan Span,
    Token YieldKeyword,
    ExpressionSyntax Expression) : StatementSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return YieldKeyword;
    yield return Expression;
  }
}

public sealed record AssertStatementSyntax(
    int Id,
    SourceSpan Span,
    Token AssertKeyword,
    Token? OpenParenToken,
    ExpressionSyntax Condition,
    Token? CommaToken,
    ExpressionSyntax? Message,
    Token? CloseParenToken) : StatementSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return AssertKeyword;
    if (OpenParenToken is { } openParen) {
      yield return openParen;
    }
    yield return Condition;
    if (CommaToken is { } comma) {
      yield return comma;
    }
    if (Message is not null) {
      yield return Message;
    }
    if (CloseParenToken is { } closeParen) {
      yield return closeParen;
    }
  }
}

public sealed record ImportStatementSyntax(
    int Id,
    SourceSpan Span,
    Token ImportKeyword,
    Token ModuleToken) : StatementSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return ImportKeyword;
    yield return ModuleToken;
  }
}

public sealed record FromImportStatementSyntax(
    int Id,
    SourceSpan Span,
    Token FromKeyword,
    Token ModuleToken,
    Token ImportKeyword,
    ImmutableArray<Token> Names,
    ImmutableArray<Token> CommaTokens = default) : StatementSyntax(Id, Span) {
  public ImmutableArray<Token> CommaTokens { get; init; } = CommaTokens.IsDefault ? [] : CommaTokens;

  public override IEnumerable<SyntaxElement> GetChildren() {
    yield return FromKeyword;
    yield return ModuleToken;
    yield return ImportKeyword;
    foreach (SyntaxElement item in InterleaveTokensBySpan(Names, CommaTokens)) {
      yield return item;
    }
  }
}

public sealed record MissingStatementSyntax(
    int Id,
    SourceSpan Span) : StatementSyntax(Id, Span) {
  public override bool IsMissing => true;

  public override IEnumerable<SyntaxElement> GetChildren() => [];
}

public sealed record ErrorStatementSyntax(
    int Id,
    SourceSpan Span,
    ImmutableArray<Token> SkippedTokens) : StatementSyntax(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    foreach (Token token in SkippedTokens) {
      yield return token;
    }
  }
}
