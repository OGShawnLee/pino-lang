using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Parsing;

internal sealed partial class Parser {
  private BlockStatementSyntax ParseBlock() {
    if (_depth >= MaxDepth) {
      ReportDepthExceeded();
      return DrainBlockStatement();
    }

    _depth++;
    try {
      Token openBrace = Expect(TokenKind.OpenBrace);
      ImmutableArray<SyntaxNode> items = (openBrace.Flags & TokenFlags.Missing) == 0
          ? ParseBlockItems()
          : [];
      Token closeBrace = ExpectCloseWithRelated(TokenKind.CloseBrace, openBrace);
      return new BlockStatementSyntax(
          NextId(),
          SourceSpan.FromBounds(openBrace.Span.Start, closeBrace.Span.End),
          openBrace,
          items,
          closeBrace);
    } finally {
      _depth--;
    }
  }

  private RecoveryBlockSyntax ParseRecoveryBlock() {
    if (_depth >= MaxDepth) {
      ReportDepthExceeded();
      return DrainRecoveryBlock();
    }

    _depth++;
    try {
      Token openBrace = Expect(TokenKind.OpenBrace);
      ImmutableArray<SyntaxNode> items = (openBrace.Flags & TokenFlags.Missing) == 0
          ? ParseBlockItems()
          : [];
      Token closeBrace = ExpectCloseWithRelated(TokenKind.CloseBrace, openBrace);
      return new RecoveryBlockSyntax(
          NextId(),
          SourceSpan.FromBounds(openBrace.Span.Start, closeBrace.Span.End),
          openBrace,
          items,
          closeBrace);
    } finally {
      _depth--;
    }
  }

  private ImmutableArray<SyntaxNode> ParseBlockItems() {
    var items = ImmutableArray.CreateBuilder<SyntaxNode>();
    while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EOF)) {
      int startPos = _pos;
      SyntaxNode stmt = ParseStatementTemporary();
      items.Add(stmt);

      if (_pos == startPos && !IsAtEnd) {
        Token skipped = Consume();
        items.Add(new ErrorStatementSyntax(NextId(), skipped.Span, [skipped]));
      }
    }

    return items.ToImmutable();
  }

  // 023: temporary statement subset (expression statements, `yield`, `return`); ticket 023 replaces this.
  private SyntaxNode ParseStatementTemporary() {
    switch (Current.Kind) {
      case TokenKind.YieldKeyword: {
          Token yieldKeyword = Consume();
          ExpressionSyntax expr = ParseExpression(0, ExprContext.AllowBraceLiteral);
          return new YieldStatementSyntax(
              NextId(),
              SourceSpan.FromBounds(yieldKeyword.Span.Start, expr.Span.End),
              yieldKeyword,
              expr);
        }

      case TokenKind.ReturnKeyword: {
          Token returnKeyword = Consume();
          // D-142: `return` continues onto the next line and is bare only before `}` or `EOF`.
          if (Current.Kind is TokenKind.CloseBrace or TokenKind.EOF) {
            return new ReturnStatementSyntax(
                NextId(),
                returnKeyword.Span,
                returnKeyword,
                null);
          }

          ExpressionSyntax expr = ParseExpression(0, ExprContext.AllowBraceLiteral);
          return new ReturnStatementSyntax(
              NextId(),
              SourceSpan.FromBounds(returnKeyword.Span.Start, expr.Span.End),
              returnKeyword,
              expr);
        }

      case TokenKind.Error: {
          var skipped = ImmutableArray.CreateBuilder<Token>();
          while (Current.Kind == TokenKind.Error) {
            skipped.Add(Consume());
          }

          return new ErrorStatementSyntax(
              NextId(),
              SourceSpan.FromBounds(skipped[0].Span.Start, skipped[^1].Span.End),
              skipped.ToImmutable());
        }

      default: {
          if (!CanStartExpression(Current)) {
            ReportError(
                DiagnosticCodes.ExpectedStatement,
                Current.Span,
                $"Unexpected token {DescribeToken(Current)} in block.");
            return SkipToNextStatementStart();
          }

          ExpressionSyntax expr = ParseExpression(0, ExprContext.AllowBraceLiteral);
          return new ExpressionStatementSyntax(NextId(), expr.Span, expr);
        }
    }
  }

  // 023: temporary statement recovery; ticket 023 replaces this.
  private ErrorStatementSyntax SkipToNextStatementStart() {
    var skipped = ImmutableArray.CreateBuilder<Token>();
    skipped.Add(Consume());

    while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EOF)) {
      if (HasLeadingNewline(Current) || IsStatementSyncToken(Current.Kind)) {
        break;
      }

      skipped.Add(Consume());
    }

    return new ErrorStatementSyntax(
        NextId(),
        SourceSpan.FromBounds(skipped[0].Span.Start, skipped[^1].Span.End),
        skipped.ToImmutable());
  }

  private BlockStatementSyntax DrainBlockStatement() {
    Token openBrace = Current.Kind == TokenKind.OpenBrace
        ? Consume()
        : MissingToken(TokenKind.OpenBrace);
    ImmutableArray<SyntaxNode> items = DrainBlockBodyItems(out Token closeBrace);
    return new BlockStatementSyntax(
        NextId(),
        SourceSpan.FromBounds(openBrace.Span.Start, closeBrace.Span.End),
        openBrace,
        items,
        closeBrace);
  }

  private RecoveryBlockSyntax DrainRecoveryBlock() {
    Token openBrace = Current.Kind == TokenKind.OpenBrace
        ? Consume()
        : MissingToken(TokenKind.OpenBrace);
    ImmutableArray<SyntaxNode> items = DrainBlockBodyItems(out Token closeBrace);
    return new RecoveryBlockSyntax(
        NextId(),
        SourceSpan.FromBounds(openBrace.Span.Start, closeBrace.Span.End),
        openBrace,
        items,
        closeBrace);
  }

  private ImmutableArray<SyntaxNode> DrainBlockBodyItems(out Token closeBrace) {
    var skipped = ImmutableArray.CreateBuilder<Token>();
    int braceDepth = 0;

    while (!IsAtEnd) {
      if (Current.Kind == TokenKind.CloseBrace && braceDepth == 0) {
        closeBrace = Consume();
        if (skipped.Count == 0) {
          return [];
        }

        var errorStmt = new ErrorStatementSyntax(
            NextId(),
            SourceSpan.FromBounds(skipped[0].Span.Start, skipped[^1].Span.End),
            skipped.ToImmutable());
        return [errorStmt];
      }

      Token tok = Consume();
      skipped.Add(tok);
      if (tok.Kind == TokenKind.OpenBrace) {
        braceDepth++;
      } else if (tok.Kind == TokenKind.CloseBrace && braceDepth > 0) {
        braceDepth--;
      }
    }

    closeBrace = MissingToken(TokenKind.CloseBrace);
    if (skipped.Count == 0) {
      return [];
    }

    return [new ErrorStatementSyntax(
        NextId(),
        SourceSpan.FromBounds(skipped[0].Span.Start, skipped[^1].Span.End),
        skipped.ToImmutable())];
  }
}
