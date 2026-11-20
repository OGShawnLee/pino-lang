using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Parsing;

public sealed partial class Parser {
  private BlockStatementSyntax ParseBlock() {
    if (_depth >= MaxDepth) {
      ReportDepthExceeded();
      return DrainBlockStatement();
    }

    _depth++;
    try {
      Token openBrace = Expect(TokenKind.OpenBrace);
      ImmutableArray<SyntaxNode> items;
      bool pushed = PushOpenBrace(openBrace);
      try {
        items = (openBrace.Flags & TokenFlags.Missing) == 0
            ? ParseBlockItems()
            : [];
      } finally {
        PopOpenBrace(pushed);
      }

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
      ImmutableArray<SyntaxNode> items;
      bool pushed = PushOpenBrace(openBrace);
      try {
        items = (openBrace.Flags & TokenFlags.Missing) == 0
            ? ParseBlockItems()
            : [];
      } finally {
        PopOpenBrace(pushed);
      }

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
      if (CheckColumn1Trigger()) {
        break;
      }

      int startPos = _pos;
      ParseBlockItem(items, insideBlock: true);

      if (_pos == startPos && !IsAtEnd) {
        Token skipped = Consume();
        items.Add(new ErrorStatementSyntax(NextId(), skipped.Span, [skipped]));
      }
    }

    return items.ToImmutable();
  }

  private void ParseBlockItem(ImmutableArray<SyntaxNode>.Builder items, bool insideBlock) {
    if (Current.Kind == TokenKind.Error) {
      items.Add(ConsumeErrorTokensAsStatement());
      return;
    }

    int beforePrefixId = _nextId;
    GenericDecoratorSyntax? genericDecorator = null;

    if (IsAdjacentDoubleAt() && !IsTestableStart()) {
      if (IsIdentifierText(Peek(2), "Generic")) {
        genericDecorator = ParseGenericDecorator();
      } else {
        items.Add(SkipUnknownDoubleAtDecorator());
        return;
      }
    } else if (Current.Kind == TokenKind.At && IsIdentifierText(Peek(1), "generic")) {
      genericDecorator = ParseDeprecatedGenericDecorator();
    }

    // D-163: `pub` or `@@Testable` on top-level declarations.
    VisibilityTokens? pendingVisibility = null;
    while (IsVisibilityStart()) {
      VisibilityTokens visibilityTokens = ParseVisibilityTokens();
      if (pendingVisibility is null) {
        pendingVisibility = visibilityTokens;
      } else {
        // MOD-E5: `pub` and `@@Testable` are mutually exclusive in either order.
        ReportErrorAt(
            _pos - 1,
            DiagnosticCodes.ExpectedStatement,
            visibilityTokens.Span,
            "'pub' and '@@Testable' cannot be combined on the same declaration.");
        var errTokens = ImmutableArray.CreateBuilder<Token>();
        if (genericDecorator is not null) {
          AppendRealTokens(errTokens, genericDecorator);
          genericDecorator = null;
          _nextId = beforePrefixId;
        }
        pendingVisibility.Value.AppendTo(errTokens);
        items.Add(MakeErrorStatementFromTokens(errTokens.ToImmutable()));
        beforePrefixId = _nextId;
        pendingVisibility = visibilityTokens;
      }
    }

    VisibilitySyntax? visibility = pendingVisibility is { } pv
        ? BuildVisibilitySyntax(pv)
        : null;

    if (genericDecorator is not null || visibility is not null) {
      switch (Current.Kind) {
        case TokenKind.FnKeyword:
          items.Add(ParseFunctionDeclaration(genericDecorator, visibility));
          return;

        case TokenKind.StructKeyword:
          items.Add(ParseStructDeclaration(genericDecorator, visibility));
          return;

        case TokenKind.InterfaceKeyword:
          items.Add(ParseInterfaceDeclaration(genericDecorator, visibility));
          return;

        case TokenKind.UnionKeyword:
          items.Add(ParseUnionDeclaration(genericDecorator, visibility));
          return;

        case TokenKind.EnumKeyword:
          if (genericDecorator is not null) {
            ReportError(
                DiagnosticCodes.ExpectedStatement,
                genericDecorator.Span,
                "'@@Generic' cannot be applied to enum declarations.");
            var decTokens = ImmutableArray.CreateBuilder<Token>();
            AppendRealTokens(decTokens, genericDecorator);
            _nextId = beforePrefixId;
            items.Add(MakeErrorStatementFromTokens(decTokens.ToImmutable()));
            visibility = pendingVisibility is { } epv
                ? BuildVisibilitySyntax(epv)
                : null;
          }

          items.Add(ParseEnumDeclaration(visibility));
          return;

        case TokenKind.ValKeyword or TokenKind.VarKeyword: {
            if (genericDecorator is not null) {
              ReportError(
                  DiagnosticCodes.ExpectedStatement,
                  genericDecorator.Span,
                  "'@@Generic' cannot be applied to variable declarations.");
              var decTokens = ImmutableArray.CreateBuilder<Token>();
              AppendRealTokens(decTokens, genericDecorator);
              _nextId = beforePrefixId;
              items.Add(MakeErrorStatementFromTokens(decTokens.ToImmutable()));
              visibility = pendingVisibility is { } vpv
                  ? BuildVisibilitySyntax(vpv)
                  : null;
            }

            if (Peek(1).Kind == TokenKind.At && Peek(2).Kind == TokenKind.OpenParen) {
              if (visibility is not null) {
                ReportError(
                    DiagnosticCodes.ExpectedStatement,
                    visibility.Span,
                    $"'{GetTokenText(visibility.KeywordOrIdentifierToken)}' cannot prefix tuple destructuring.");
                var visTokens = ImmutableArray.CreateBuilder<Token>();
                AppendRealTokens(visTokens, visibility);
                _nextId = visibility.Id;
                items.Add(MakeErrorStatementFromTokens(visTokens.ToImmutable()));
              }

              items.Add(ParseTupleDestructuringStatement());
              return;
            }

            items.Add(ParseVariableDeclaration(visibility));
            return;
          }

        default: {
            ReportError(
                DiagnosticCodes.ExpectedStatement,
                Current.Span,
                $"Expected declaration after modifier, found {DescribeToken(Current)}.");
            var modifierTokens = ImmutableArray.CreateBuilder<Token>();
            if (genericDecorator is not null) {
              AppendRealTokens(modifierTokens, genericDecorator);
            }
            if (visibility is not null) {
              AppendRealTokens(modifierTokens, visibility);
            }
            if (modifierTokens.Count > 0) {
              _nextId = beforePrefixId;
              items.Add(MakeErrorStatementFromTokens(modifierTokens.ToImmutable()));
            }

            return;
          }
      }
    }

    items.Add(ParseStatementOrDeclarationUnprefixed(insideBlock));
  }

  private SyntaxNode ParseStatementOrDeclarationUnprefixed(bool insideBlock) {
    if (_depth >= MaxDepth) {
      ReportDepthExceeded();
      return DrainStatementTokens(insideBlock);
    }

    _depth++;
    try {
      switch (Current.Kind) {
        case TokenKind.FnKeyword when Peek(1).Kind != TokenKind.OpenParen:
          return ParseFunctionDeclaration(genericDecorator: null, visibility: null);

        case TokenKind.StructKeyword:
          return ParseStructDeclaration(genericDecorator: null, visibility: null);

        case TokenKind.InterfaceKeyword:
          return ParseInterfaceDeclaration(genericDecorator: null, visibility: null);

        case TokenKind.UnionKeyword:
          return ParseUnionDeclaration(genericDecorator: null, visibility: null);

        case TokenKind.EnumKeyword:
          return ParseEnumDeclaration(visibility: null);

        case TokenKind.ModuleKeyword:
          return ParseModuleDeclaration();

        case TokenKind.TestKeyword:
          return ParseTestDeclaration();

        case TokenKind.ImportKeyword:
          return ParseImportStatement();

        case TokenKind.FromKeyword:
          return ParseFromImportStatement();

        case TokenKind.ValKeyword or TokenKind.VarKeyword:
          return Peek(1).Kind == TokenKind.At && Peek(2).Kind == TokenKind.OpenParen
              ? ParseTupleDestructuringStatement()
              : ParseVariableDeclaration(visibility: null);

        case TokenKind.IfKeyword:
          return ParseIfStatementOrExpression();

        case TokenKind.ForKeyword:
          return ParseForStatement();

        case TokenKind.MatchKeyword:
          return ParseMatchStatement();

        case TokenKind.ReturnKeyword:
          return ParseReturnStatement();

        case TokenKind.YieldKeyword:
          return ParseYieldStatement();

        case TokenKind.AssertKeyword:
          return ParseAssertStatement();

        case TokenKind.BreakKeyword: {
            Token breakKeyword = Consume();
            return new BreakStatementSyntax(NextId(), breakKeyword.Span, breakKeyword);
          }

        case TokenKind.ContinueKeyword: {
            Token continueKeyword = Consume();
            return new ContinueStatementSyntax(NextId(), continueKeyword.Span, continueKeyword);
          }

        // D-161: standalone `{ ... }` at statement start parses as a BlockStatementSyntax.
        case TokenKind.OpenBrace:
          return ParseBlock();

        default: {
            if (!CanStartExpression(Current)) {
              ReportError(
                  DiagnosticCodes.ExpectedStatement,
                  Current.Span,
                  insideBlock
                      ? $"Unexpected token {DescribeToken(Current)} in block."
                      : $"Unexpected token {DescribeToken(Current)}.");
              return SkipToNextStatementStart(insideBlock);
            }

            ExpressionSyntax expr = ParseExpression(0, ExprContext.AllowBraceLiteral);
            return new ExpressionStatementSyntax(NextId(), expr.Span, expr);
          }
      }
    } finally {
      _depth--;
    }
  }

  private ErrorStatementSyntax ConsumeErrorTokensAsStatement() {
    var skipped = ImmutableArray.CreateBuilder<Token>();
    while (Current.Kind == TokenKind.Error) {
      skipped.Add(Consume());
    }

    return MakeErrorStatementFromTokens(skipped.ToImmutable());
  }

  private ErrorStatementSyntax SkipToNextStatementStart(bool insideBlock) {
    var skipped = ImmutableArray.CreateBuilder<Token>();
    skipped.Add(Consume());

    while (!IsAtEnd) {
      if (insideBlock && Current.Kind == TokenKind.CloseBrace) {
        break;
      }

      if (MatchesColumn1Trigger()
          || HasLeadingNewline(Current)
          || IsStatementOrTopLevelSyncToken(insideBlock)) {
        break;
      }

      skipped.Add(Consume());
    }

    return MakeErrorStatementFromTokens(skipped.ToImmutable());
  }

  private ErrorStatementSyntax MakeErrorStatementFromTokens(ImmutableArray<Token> tokens) =>
      new(
          NextId(),
          SourceSpan.FromBounds(tokens[0].Span.Start, tokens[^1].Span.End),
          tokens);

  private static void AppendRealTokens(ImmutableArray<Token>.Builder builder, SyntaxNode node) {
    foreach (Token token in node.DescendantTokens()) {
      if ((token.Flags & TokenFlags.Missing) == 0) {
        builder.Add(token);
      }
    }
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

  private StatementSyntax DrainStatementTokens(bool insideBlock) {
    ImmutableArray<Token> skipped = DrainUntilClosingBrace(
        stopAtOuterCloseBrace: insideBlock,
        consumeClosingBrace: false,
        out _);
    return skipped.IsEmpty
        ? new MissingStatementSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0))
        : MakeErrorStatementFromTokens(skipped);
  }

  private ImmutableArray<SyntaxNode> DrainBlockBodyItems(out Token closeBrace) {
    ImmutableArray<Token> skipped = DrainUntilClosingBrace(
        stopAtOuterCloseBrace: true,
        consumeClosingBrace: true,
        out closeBrace);
    if (skipped.IsEmpty) {
      return [];
    }

    return [MakeErrorStatementFromTokens(skipped)];
  }

  private ImmutableArray<Token> DrainUntilClosingBrace(
      bool stopAtOuterCloseBrace,
      bool consumeClosingBrace,
      out Token closeBrace) {
    var skipped = ImmutableArray.CreateBuilder<Token>();
    int braceDepth = 0;

    while (!IsAtEnd) {
      if (stopAtOuterCloseBrace && Current.Kind == TokenKind.CloseBrace && braceDepth == 0) {
        closeBrace = consumeClosingBrace ? Consume() : Current;
        return skipped.ToImmutable();
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
    return skipped.ToImmutable();
  }
}
