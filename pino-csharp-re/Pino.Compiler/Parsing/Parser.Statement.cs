using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Parsing;

public sealed partial class Parser {
  private bool IsTupleDestructuringAfterBindingKeyword(bool hasLeadingMut) {
    int offset = !hasLeadingMut && Peek(1).Kind == TokenKind.MutKeyword ? 2 : 1;
    return Peek(offset).Kind == TokenKind.At && Peek(offset + 1).Kind == TokenKind.OpenParen;
  }

  private Token? ConsumeLeadingBindingMutIfPresent() {
    if (Current.Kind != TokenKind.MutKeyword || Peek(1).Kind is not (TokenKind.ValKeyword or TokenKind.VarKeyword)) {
      return null;
    }

    int mutIndex = _pos;
    Token mutToken = Consume();
    if (Current.Kind == TokenKind.ValKeyword) {
      ReportErrorAt(
          mutIndex,
          DiagnosticCodes.InvalidMutModifierPlacement,
          mutToken.Span,
          "'mut val' is not valid; write 'val mut' instead.",
          help: "write 'val mut' with 'mut' after 'val'");
    } else {
      ReportErrorAt(
          mutIndex,
          DiagnosticCodes.InvalidMutModifierPlacement,
          mutToken.Span,
          "'mut var' is not valid; 'var' bindings are already mutable.",
          help: "remove 'mut' before 'var'");
    }

    return mutToken;
  }

  private VariableDeclarationSyntax ParseVariableDeclaration(
      VisibilitySyntax? visibility,
      Token? leadingMutKeyword = null) {
    Token keywordToken = Consume();
    Token? mutKeyword = leadingMutKeyword;

    if (mutKeyword is null && Current.Kind == TokenKind.MutKeyword) {
      int mutIndex = _pos;
      Token mk = Consume();
      mutKeyword = mk;
      if (keywordToken.Kind == TokenKind.VarKeyword) {
        ReportErrorAt(
            mutIndex,
            DiagnosticCodes.InvalidMutModifierPlacement,
            mk.Span,
            "'var mut' is not valid; 'var' bindings are already mutable.",
            help: "remove 'mut' after 'var'");
      }
    }

    Token nameToken = ExpectIdentifier();

    // D-012: an optional type follows the name, with no colon: `val n int = 12`. A token that can start a
    // type (and is not `=`) begins the annotation; a type on the next line is not read as an annotation, so a
    // missing `=` still recovers with PN2001 instead of swallowing the next statement.
    TypeSyntax? type = null;
    if (Current.Kind != TokenKind.Assign && !HasLeadingNewline(Current) && CanStartType(Current)) {
      type = ParseType();
    }

    Token equalsToken = Expect(TokenKind.Assign);
    ExpressionSyntax initializer = ParseExpression(0, ExprContext.AllowBraceLiteral);

    int start = MinModifierStart(null, visibility, keywordToken, mutKeyword, keywordToken.Span.Start);
    int end = Math.Max(equalsToken.Span.End, initializer.Span.End);
    return new VariableDeclarationSyntax(
        NextId(),
        SourceSpan.FromBounds(start, end),
        keywordToken,
        nameToken,
        equalsToken,
        initializer,
        Type: type,
        Visibility: visibility,
        MutKeyword: mutKeyword);
  }

  private TupleDestructuringStatementSyntax ParseTupleDestructuringStatement(Token? leadingMutKeyword = null) {
    Token keywordToken = Consume();
    Token? mutKeyword = leadingMutKeyword;

    if (mutKeyword is null && Current.Kind == TokenKind.MutKeyword) {
      int mutIndex = _pos;
      Token consumedMut = Consume();
      mutKeyword = consumedMut;
      if (keywordToken.Kind == TokenKind.ValKeyword) {
        ReportErrorAt(
            mutIndex,
            DiagnosticCodes.InvalidMutModifierPlacement,
            consumedMut.Span,
            "'val mut' cannot be used with tuple destructuring; use 'var @(...)' instead.",
            help: "replace 'val mut @(...)' with 'var @(...)'");
      } else {
        ReportErrorAt(
            mutIndex,
            DiagnosticCodes.InvalidMutModifierPlacement,
            consumedMut.Span,
            "'var mut' is not valid; 'var' bindings are already mutable.",
            help: "remove 'mut' after 'var'");
      }
    }

    Token atToken = Consume();
    Token openParen = Consume();
    var fields = ImmutableArray.CreateBuilder<DestructureFieldSyntax>();

    while (Current.Kind is not (TokenKind.CloseParen or TokenKind.EOF)) {
      if (IsStatementSyncToken(Current.Kind) && Current.Kind != TokenKind.Identifier) {
        break;
      }

      int startPos = _pos;
      if (Current.Kind == TokenKind.Comma) {
        int commaIndex = _pos;
        Token leadingComma = Consume();
        ReportErrorAt(
            commaIndex,
            DiagnosticCodes.UnexpectedComma,
            leadingComma.Span,
            "Unexpected ',' before destructuring field.");
        Token missingLabel = MissingTokenAt(TokenKind.Identifier, leadingComma.Span.Start);
        fields.Add(new DestructureFieldSyntax(
            NextId(),
            leadingComma.Span,
            missingLabel,
            null,
            null,
            leadingComma));
        continue;
      }

      if (Current.Kind != TokenKind.Identifier) {
        ReportError(
            DiagnosticCodes.ExpectedIdentifier,
            Current.Span,
            $"Expected destructuring field name, found {DescribeToken(Current)}.");
        break;
      }

      Token labelToken = Consume();
      Token? colonToken = null;
      Token? variableToken = null;
      if (Current.Kind == TokenKind.Colon) {
        colonToken = Consume();
        variableToken = ExpectIdentifier();
      }

      Token? commaToken = null;
      if (Current.Kind == TokenKind.Comma) {
        int commaIndex = _pos;
        Token comma = Consume();
        commaToken = comma;
        if (Current.Kind == TokenKind.CloseParen) {
          ReportErrorAt(
              commaIndex,
              DiagnosticCodes.TrailingComma,
              comma.Span,
              "Trailing comma is not allowed.",
              help: "Remove the trailing ','.");
        } else if (Current.Kind == TokenKind.Comma) {
          ReportErrorAt(
              _pos,
              DiagnosticCodes.UnexpectedComma,
              Current.Span,
              "Expected field before consecutive ','.");
        }
      }

      int fieldEnd = commaToken is { } c
          ? c.Span.End
          : variableToken is { } v
              ? v.Span.End
              : colonToken is { } col
                  ? col.Span.End
                  : labelToken.Span.End;
      fields.Add(new DestructureFieldSyntax(
          NextId(),
          SourceSpan.FromBounds(labelToken.Span.Start, fieldEnd),
          labelToken,
          colonToken,
          variableToken,
          commaToken));

      if (commaToken is { } && Current.Kind == TokenKind.CloseParen) {
        break;
      }

      if (_pos == startPos) {
        break;
      }
    }

    Token closeParen = ExpectCloseWithRelated(TokenKind.CloseParen, openParen);
    Token equalsToken = Expect(TokenKind.Assign);
    ExpressionSyntax initializer = ParseExpression(0, ExprContext.AllowBraceLiteral);
    int finalEnd = Math.Max(equalsToken.Span.End, initializer.Span.End);

    int finalStart = mutKeyword is { } mk && mk.Span.Start < keywordToken.Span.Start
        ? mk.Span.Start
        : keywordToken.Span.Start;
    return new TupleDestructuringStatementSyntax(
        NextId(),
        SourceSpan.FromBounds(finalStart, finalEnd),
        keywordToken,
        atToken,
        openParen,
        fields.ToImmutable(),
        closeParen,
        equalsToken,
        initializer,
        MutKeyword: mutKeyword);
  }

  private StatementSyntax ParseIfStatementOrExpression() {
    if (_depth >= MaxDepth) {
      ReportDepthExceeded();
      ExpressionSyntax drained = DrainExpressionTokens();
      return new ExpressionStatementSyntax(NextId(), drained.Span, drained);
    }

    _depth++;
    try {
      Token ifKeyword = Consume();
      ExpressionSyntax condition = ParseExpression(0, ExprContext.None);

      if (Current.Kind == TokenKind.ThenKeyword) {
        Token thenKeyword = Consume();
        ExpressionSyntax thenExpr = ParseExpression(0, ExprContext.AllowBraceLiteral);
        Token elseKw = Expect(TokenKind.ElseKeyword);
        ExpressionSyntax elseExpr = ParseExpression(0, ExprContext.AllowBraceLiteral);
        var ifExpr = new IfExpressionSyntax(
            NextId(),
            SourceSpan.FromBounds(ifKeyword.Span.Start, elseExpr.Span.End),
            ifKeyword,
            condition,
            thenKeyword,
            thenExpr,
            elseKw,
            elseExpr);
        return new ExpressionStatementSyntax(NextId(), ifExpr.Span, ifExpr);
      }

      BlockStatementSyntax thenBranch = ParseRequiredIfBranch("Expected '{' to begin 'if' branch.");

      Token? elseKeyword = null;
      Token? elseArrowToken = null;
      Token? elseSecondaryArrowToken = null;
      StatementSyntax? elseBranch = null;

      if (Current.Kind == TokenKind.ElseKeyword) {
        elseKeyword = Consume();
        if (Current.Kind == TokenKind.IfKeyword) {
          elseBranch = ParseIfStatementOrExpression();
        } else if (Current.Kind == TokenKind.OpenBrace) {
          elseBranch = ParseBlock();
        } else if (TryConsumeAnyArrowTokens(out Token arrow, out Token? secondary)) {
          elseArrowToken = arrow;
          elseSecondaryArrowToken = secondary;
          ReportErrorAt(
              _pos - (secondary is null ? 1 : 2),
              DiagnosticCodes.ElseArrowAfterIf,
              SourceSpan.FromBounds(arrow.Span.Start, (secondary ?? arrow).Span.End),
              "Unexpected '->' after 'else'; 'else' in an 'if' statement requires a '{ ... }' block.");
          ExpressionSyntax expr = ParseExpression(0, ExprContext.AllowBraceLiteral);
          elseBranch = new ExpressionStatementSyntax(NextId(), expr.Span, expr);
        } else {
          elseBranch = ParseRequiredIfBranch("Expected '{' to begin 'else' branch.");
        }
      }

      int endOffset = elseBranch?.Span.End ?? thenBranch.Span.End;
      return new IfStatementSyntax(
          NextId(),
          SourceSpan.FromBounds(ifKeyword.Span.Start, endOffset),
          ifKeyword,
          condition,
          thenBranch,
          elseKeyword,
          elseBranch,
          elseArrowToken,
          elseSecondaryArrowToken);
    } finally {
      _depth--;
    }
  }

  private BlockStatementSyntax ParseRequiredIfBranch(string message) {
    if (Current.Kind == TokenKind.OpenBrace) {
      return ParseBlock();
    }

    ReportError(DiagnosticCodes.UnbracedIfBranch, Current.Span, message);

    if (Current.Kind is TokenKind.ElseKeyword or TokenKind.CloseBrace or TokenKind.EOF
        || MatchesColumn1Trigger()) {
      int offset = CurrentMissingOffset();
      return new BlockStatementSyntax(
          NextId(),
          new SourceSpan(offset, 0),
          MissingTokenAt(TokenKind.OpenBrace, offset),
          [],
          MissingTokenAt(TokenKind.CloseBrace, offset));
    }

    var branchItems = ImmutableArray.CreateBuilder<SyntaxNode>();
    ParseBlockItem(branchItems, insideBlock: true);
    ImmutableArray<SyntaxNode> items = branchItems.ToImmutable();
    int start = items.Length > 0 ? items[0].Span.Start : CurrentMissingOffset();
    int end = items.Length > 0 ? items[^1].Span.End : start;
    return new BlockStatementSyntax(
        NextId(),
        SourceSpan.FromBounds(start, end),
        MissingTokenAt(TokenKind.OpenBrace, start),
        items,
        MissingTokenAt(TokenKind.CloseBrace, end));
  }

  private ForStatementSyntax ParseForStatement() {
    Token forKeyword = Consume();
    ForHeaderSyntax? header = null;

    if (Current.Kind == TokenKind.OpenBrace) {
      // Infinite loop: `for { ... }`
    } else if (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.InKeyword) {
      Token first = Consume();
      Token inKw = Consume();
      ExpressionSyntax coll = ParseExpression(0, ExprContext.None);
      header = new ForInHeaderSyntax(
          NextId(),
          SourceSpan.FromBounds(first.Span.Start, coll.Span.End),
          first,
          null,
          null,
          inKw,
          coll);
    } else if (Current.Kind == TokenKind.Identifier
        && Peek(1).Kind == TokenKind.Comma
        && Peek(2).Kind == TokenKind.Identifier
        && Peek(3).Kind == TokenKind.InKeyword) {
      Token first = Consume();
      Token comma = Consume();
      Token second = Consume();
      Token inKw = Consume();
      ExpressionSyntax coll = ParseExpression(0, ExprContext.None);
      header = new ForInHeaderSyntax(
          NextId(),
          SourceSpan.FromBounds(first.Span.Start, coll.Span.End),
          first,
          comma,
          second,
          inKw,
          coll);
    } else if (Current.Kind == TokenKind.Identifier
        && Peek(1).Kind == TokenKind.Identifier
        && Peek(2).Kind == TokenKind.InKeyword) {
      // D-001: comma between the two loop bindings is optional.
      Token first = Consume();
      Token second = Consume();
      Token inKw = Consume();
      ExpressionSyntax coll = ParseExpression(0, ExprContext.None);
      header = new ForInHeaderSyntax(
          NextId(),
          SourceSpan.FromBounds(first.Span.Start, coll.Span.End),
          first,
          null,
          second,
          inKw,
          coll);
    } else if (Current.Kind == TokenKind.Identifier
        && Peek(1).Kind == TokenKind.Comma
        && Peek(2).Kind == TokenKind.InKeyword) {
      Token first = Consume();
      int commaIndex = _pos;
      Token comma = Consume();
      ReportErrorAt(
          commaIndex,
          DiagnosticCodes.TrailingComma,
          comma.Span,
          "Trailing comma is not allowed in 'for' loop bindings.",
          help: "Remove the trailing ','.");
      Token inKw = Consume();
      ExpressionSyntax coll = ParseExpression(0, ExprContext.None);
      header = new ForInHeaderSyntax(
          NextId(),
          SourceSpan.FromBounds(first.Span.Start, coll.Span.End),
          first,
          comma,
          null,
          inKw,
          coll);
    } else {
      ExpressionSyntax condition = ParseExpression(0, ExprContext.None);
      header = new ForConditionHeaderSyntax(NextId(), condition.Span, condition);
    }

    BlockStatementSyntax body = ParseBlock();
    return new ForStatementSyntax(
        NextId(),
        SourceSpan.FromBounds(forKeyword.Span.Start, body.Span.End),
        forKeyword,
        header,
        body);
  }

  private MatchStatementSyntax ParseMatchStatement() {
    Token matchKeyword = Consume();
    ExpressionSyntax subject = ParseMatchSubject();
    Token openBrace = Expect(TokenKind.OpenBrace);
    ImmutableArray<SyntaxNode> arms = ParseMatchBody(openBrace, isStatement: true);
    Token closeBrace = ExpectCloseWithRelated(TokenKind.CloseBrace, openBrace);

    return new MatchStatementSyntax(
        NextId(),
        SourceSpan.FromBounds(matchKeyword.Span.Start, closeBrace.Span.End),
        matchKeyword,
        subject,
        openBrace,
        arms,
        closeBrace);
  }

  private ReturnStatementSyntax ParseReturnStatement() {
    Token returnKeyword = Consume();
    // D-142: `return` continues onto the next line and is bare only before `}` or `EOF`.
    if (Current.Kind is TokenKind.CloseBrace or TokenKind.EOF || MatchesColumn1Trigger()) {
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

  private YieldStatementSyntax ParseYieldStatement() {
    Token yieldKeyword = Consume();
    ExpressionSyntax expr = ParseExpression(0, ExprContext.AllowBraceLiteral);
    return new YieldStatementSyntax(
        NextId(),
        SourceSpan.FromBounds(yieldKeyword.Span.Start, expr.Span.End),
        yieldKeyword,
        expr);
  }

  private AssertStatementSyntax ParseAssertStatement() {
    Token assertKeyword = Consume();
    ExpressionSyntax condition = ParseExpression(0, ExprContext.AllowBraceLiteral);
    return new AssertStatementSyntax(
        NextId(),
        SourceSpan.FromBounds(assertKeyword.Span.Start, condition.Span.End),
        assertKeyword,
        null,
        condition,
        null,
        null,
        null);
  }

  private ImportStatementSyntax ParseImportStatement() {
    Token importKeyword = Consume();
    Token moduleToken = ExpectIdentifier();
    return new ImportStatementSyntax(
        NextId(),
        SourceSpan.FromBounds(importKeyword.Span.Start, moduleToken.Span.End),
        importKeyword,
        moduleToken);
  }

  private FromImportStatementSyntax ParseFromImportStatement() {
    Token fromKeyword = Consume();
    Token moduleToken = ExpectIdentifier();
    Token importKeyword = Expect(TokenKind.ImportKeyword);

    var names = ImmutableArray.CreateBuilder<Token>();
    var commas = ImmutableArray.CreateBuilder<Token>();

    if (Current.Kind == TokenKind.Comma) {
      int commaIndex = _pos;
      Token leadingComma = Consume();
      ReportErrorAt(
          commaIndex,
          DiagnosticCodes.UnexpectedComma,
          leadingComma.Span,
          "Unexpected ',' before imported name.");
      commas.Add(leadingComma);
    }

    names.Add(ExpectIdentifier());

    while (!IsAtEnd) {
      if (MatchesColumn1Trigger()) {
        break;
      }

      if (Current.Kind == TokenKind.Comma) {
        int commaIndex = _pos;
        Token comma = Consume();
        commas.Add(comma);

        if (Current.Kind == TokenKind.Comma) {
          ReportErrorAt(
              _pos,
              DiagnosticCodes.UnexpectedComma,
              Current.Span,
              "Expected imported name before consecutive ','.");
          continue;
        }

        if (Current.Kind != TokenKind.Identifier) {
          ReportErrorAt(
              commaIndex,
              DiagnosticCodes.TrailingComma,
              comma.Span,
              "Trailing comma is not allowed in 'from ... import'.",
              help: "Remove the trailing ','.");
          break;
        }

        names.Add(Consume());
        continue;
      }

      if (HasLeadingNewline(Current) || Current.Kind != TokenKind.Identifier) {
        break;
      }

      names.Add(Consume());
    }

    int endOffset = names[^1].Span.End;
    if (commas.Count > 0 && commas[^1].Span.End > endOffset) {
      endOffset = commas[^1].Span.End;
    }

    return new FromImportStatementSyntax(
        NextId(),
        SourceSpan.FromBounds(fromKeyword.Span.Start, endOffset),
        fromKeyword,
        moduleToken,
        importKeyword,
        names.ToImmutable(),
        commas.ToImmutable());
  }
}
