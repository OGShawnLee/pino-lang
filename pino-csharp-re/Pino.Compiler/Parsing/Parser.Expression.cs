using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Parsing;

public sealed partial class Parser {
  [Flags]
  private enum ExprContext {
    None = 0,
    AllowBraceLiteral = 1 << 0,
    InCommaOptionalList = 1 << 1,
  }

  private ExpressionSyntax ParseExpression(int minBindingPower, ExprContext context) {
    if (_depth >= MaxDepth) {
      ReportDepthExceeded();
      return DrainExpressionTokens();
    }

    _depth++;
    try {
      ExpressionSyntax left = ParsePrefixOrPrimary(context);

      while (!IsAtEnd) {
        if (Current.Kind == TokenKind.OpenBracket && HasLeadingNewline(Current)) {
          break;
        }

        if (Current.Kind == TokenKind.OpenParen && HasLeadingNewline(Current)) {
          break;
        }

        // D-010: asymmetric '-' inside comma-optional lists.
        if (context.HasFlag(ExprContext.InCommaOptionalList)
            && minBindingPower <= 60
            && Current.Kind == TokenKind.Minus
            && Peek(1).Kind is not (TokenKind.EOF
                or TokenKind.CloseBracket
                or TokenKind.CloseParen
                or TokenKind.CloseBrace
                or TokenKind.Comma)) {
          bool spaceBefore = HasLeadingSpace(Current);
          bool spaceAfter = HasLeadingSpace(Peek(1));
          if (spaceBefore != spaceAfter) {
            ReportError(
                DiagnosticCodes.AmbiguousMinusInList,
                Current.Span,
                "ambiguous '-' in comma-optional list",
                help: "write 'a - b' to subtract or 'a, -b' for two elements");
            if (spaceBefore && !spaceAfter) {
              break;
            }
          }
        }

        // Level 9 (postfix): [index], [TypeArgs](args), [TypeArgs]::member, :member, ::member, (args), ?, or { ... }
        if (minBindingPower <= 90 && TryParsePostfixStep(ref left, context)) {
          continue;
        }

        // Level 4 (`is` / `is not` pattern)
        if (Current.Kind == TokenKind.IsKeyword) {
          if (minBindingPower > 40) {
            break;
          }

          Token isKeyword = Consume();
          Token? notKeyword = Current.Kind == TokenKind.Not ? Consume() : null;
          PatternSyntax pattern = ParsePattern(allowLabels: false);
          left = new IsPatternExpressionSyntax(
              NextId(),
              SourceSpan.FromBounds(left.Span.Start, pattern.Span.End),
              left,
              isKeyword,
              notKeyword,
              pattern);
          continue;
        }

        // Level 5 (`not in`)
        if (Current.Kind == TokenKind.Not && Peek(1).Kind == TokenKind.InKeyword) {
          if (minBindingPower > 50) {
            break;
          }

          Token notToken = Consume();
          Token inToken = Consume();
          ExpressionSyntax right = ParseExpression(51, context);
          left = new BinaryExpressionSyntax(
              NextId(),
              SourceSpan.FromBounds(left.Span.Start, right.Span.End),
              left,
              notToken,
              right,
              inToken);
          continue;
        }

        // Level 1 (right-associative assignment; never consume `=>` as `=`)
        if (IsAssignmentOperator(Current.Kind) && !IsFatArrow()) {
          if (minBindingPower > 10) {
            break;
          }

          Token opToken = Consume();
          ExpressionSyntax value = ParseExpression(10, context);
          left = new AssignmentExpressionSyntax(
              NextId(),
              SourceSpan.FromBounds(left.Span.Start, value.Span.End),
              left,
              opToken,
              value);
          continue;
        }

        int infixBP = GetBinaryLeftBindingPower(Current.Kind);
        if (infixBP == 0 || infixBP < minBindingPower) {
          break;
        }

        // Logical `or` when followed by `{` is postfix Level 9 (`or { ... }`), handled above.
        if (Current.Kind == TokenKind.Or && Peek(1).Kind == TokenKind.OpenBrace) {
          break;
        }

        Token binaryOp = Consume();
        ExpressionSyntax rhs = ParseExpression(infixBP + 1, context);
        left = new BinaryExpressionSyntax(
            NextId(),
            SourceSpan.FromBounds(left.Span.Start, rhs.Span.End),
            left,
            binaryOp,
            rhs);
      }

      return left;
    } finally {
      _depth--;
    }
  }

  private ExpressionSyntax ParsePrefixOrPrimary(ExprContext context) {
    switch (Current.Kind) {
      case TokenKind.Minus:
      case TokenKind.Not: {
          Token opToken = Consume();
          ExpressionSyntax operand = ParseExpression(80, context);
          return new UnaryExpressionSyntax(
              NextId(),
              SourceSpan.FromBounds(opToken.Span.Start, operand.Span.End),
              opToken,
              operand);
        }

      case TokenKind.IntLiteral:
      case TokenKind.FloatLiteral:
      case TokenKind.RuneLiteral:
      case TokenKind.TrueLiteral:
      case TokenKind.FalseLiteral: {
          Token literal = Consume();
          return new LiteralExpressionSyntax(NextId(), literal.Span, literal);
        }

      case TokenKind.StringStart:
        return ParseInterpolatedString();

      case TokenKind.OpenParen: {
          Token openParen = Consume();
          ExpressionSyntax inner = ParseExpression(0, ExprContext.AllowBraceLiteral);
          Token closeParen = ExpectCloseWithRelated(TokenKind.CloseParen, openParen);
          return new ParenthesizedExpressionSyntax(
              NextId(),
              SourceSpan.FromBounds(openParen.Span.Start, closeParen.Span.End),
              openParen,
              inner,
              closeParen);
        }

      case TokenKind.OpenBracket:
        return ParseVectorOrVectorInitializer(context);

      case TokenKind.At when Peek(1).Kind == TokenKind.OpenParen:
        return ParseTupleLiteral();

      case TokenKind.FnKeyword when Peek(1).Kind == TokenKind.OpenParen:
        return ParseLambda(context);

      case TokenKind.IfKeyword:
        return ParseIfExpression(context);

      case TokenKind.MatchKeyword:
        return ParseMatchExpression();

      case TokenKind.Identifier:
        return ParseIdentifierLedPrimary(context);

      case TokenKind.OpenBrace: {
          ReportError(
              DiagnosticCodes.UnexpectedBlockInExpression,
              Current.Span,
              "Unexpected '{' in expression position.",
              help: "use 'set[T] { ... }' for a set literal or 'map[K, V] { ... }' for a map literal");
          return ConsumeBalancedBracesAsErrorExpression();
        }

      case TokenKind.Error: {
          var skipped = ImmutableArray.CreateBuilder<Token>();
          while (Current.Kind == TokenKind.Error) {
            skipped.Add(Consume());
          }

          return new ErrorExpressionSyntax(
              NextId(),
              SourceSpan.FromBounds(skipped[0].Span.Start, skipped[^1].Span.End),
              skipped.ToImmutable());
        }

      default: {
          ReportError(
              DiagnosticCodes.ExpectedExpression,
              Current.Span,
              $"Expected expression, found {DescribeToken(Current)}.");
          return new MissingExpressionSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0));
        }
    }
  }

  private ExpressionSyntax ParseIdentifierLedPrimary(ExprContext context) {
    if (IsIdentifierText(Current, "map")
        && Peek(1).Kind == TokenKind.OpenBracket
        && !HasLeadingNewline(Peek(1))) {
      return ParseMapLiteral(context);
    }

    if (IsIdentifierText(Current, "set")
        && Peek(1).Kind == TokenKind.OpenBracket
        && !HasLeadingNewline(Peek(1))) {
      return ParseSetLiteral(context);
    }

    if (context.HasFlag(ExprContext.AllowBraceLiteral)) {
      if (Peek(1).Kind == TokenKind.OpenBrace) {
        Token nameToken = Consume();
        var namedType = new NamedTypeSyntax(NextId(), nameToken.Span, nameToken);
        return ParseStructLiteral(namedType);
      }

      if (Peek(1).Kind == TokenKind.ColonColon
          && Peek(2).Kind == TokenKind.Identifier
          && Peek(3).Kind == TokenKind.OpenBrace) {
        Token modToken = Consume();
        Token colonColon = Consume();
        Token nameToken = Consume();
        var qualifiedType = new QualifiedTypeSyntax(
            NextId(),
            SourceSpan.FromBounds(modToken.Span.Start, nameToken.Span.End),
            modToken,
            colonColon,
            nameToken);
        return ParseStructLiteral(qualifiedType);
      }

      if (Peek(1).Kind == TokenKind.OpenBracket
          && !HasLeadingNewline(Peek(1))
          && IsBalancedBracketFollowedBy(1, TokenKind.OpenBrace)) {
        int savedPos = _pos;
        int savedNextId = _nextId;
        int savedLastError = _lastErrorTokenIndex;

        Token nameToken = Consume();
        var baseType = new NamedTypeSyntax(NextId(), nameToken.Span, nameToken);
        if (TryParseTypeArgumentListAfterBase(out TypeArgumentListSyntax? typeArgs)
            && Current.Kind == TokenKind.OpenBrace) {
          var genericType = new GenericTypeSyntax(
              NextId(),
              SourceSpan.FromBounds(baseType.Span.Start, typeArgs.Span.End),
              baseType,
              typeArgs);
          return ParseStructLiteral(genericType);
        }

        _pos = savedPos;
        _nextId = savedNextId;
        _lastErrorTokenIndex = savedLastError;
      }

      if (Peek(1).Kind == TokenKind.ColonColon
          && Peek(2).Kind == TokenKind.Identifier
          && Peek(3).Kind == TokenKind.OpenBracket
          && !HasLeadingNewline(Peek(3))
          && IsBalancedBracketFollowedBy(3, TokenKind.OpenBrace)) {
        int savedPos = _pos;
        int savedNextId = _nextId;
        int savedLastError = _lastErrorTokenIndex;

        Token modToken = Consume();
        Token colonColon = Consume();
        Token nameToken = Consume();
        var qualifiedType = new QualifiedTypeSyntax(
            NextId(),
            SourceSpan.FromBounds(modToken.Span.Start, nameToken.Span.End),
            modToken,
            colonColon,
            nameToken);
        if (TryParseTypeArgumentListAfterBase(out TypeArgumentListSyntax? typeArgs)
            && Current.Kind == TokenKind.OpenBrace) {
          var genericType = new GenericTypeSyntax(
              NextId(),
              SourceSpan.FromBounds(qualifiedType.Span.Start, typeArgs.Span.End),
              qualifiedType,
              typeArgs);
          return ParseStructLiteral(genericType);
        }

        _pos = savedPos;
        _nextId = savedNextId;
        _lastErrorTokenIndex = savedLastError;
      }
    }

    Token ident = Consume();
    return new IdentifierExpressionSyntax(NextId(), ident.Span, ident);
  }

  private bool TryParsePostfixStep(ref ExpressionSyntax left, ExprContext context) {
    switch (Current.Kind) {
      case TokenKind.OpenBracket: {
          if (HasLeadingNewline(Current)) {
            return false;
          }

          if (TryParseTypeArgumentListSpeculative(out TypeArgumentListSyntax? typeArgs)) {
            if (Current.Kind == TokenKind.OpenParen && !HasLeadingNewline(Current)) {
              (Token openParen, ImmutableArray<ArgumentSyntax> args, Token closeParen) = ParseArgumentList();
              left = new CallExpressionSyntax(
                  NextId(),
                  SourceSpan.FromBounds(left.Span.Start, closeParen.Span.End),
                  left,
                  openParen,
                  args,
                  closeParen,
                  typeArgs);
              return true;
            }

            if (Current.Kind == TokenKind.ColonColon) {
              Token colonColon = Consume();
              Token member = ExpectIdentifier();
              left = new StaticAccessExpressionSyntax(
                  NextId(),
                  SourceSpan.FromBounds(left.Span.Start, member.Span.End),
                  left,
                  colonColon,
                  member,
                  typeArgs);
              return true;
            }

            // Type arguments not followed by `(` or `::`: roll back to parse `[expr]` as index (D-076).
            RollbackSpeculativeTypeArguments(typeArgs);
          }

          Token openBracket = Consume();
          ExpressionSyntax index = ParseExpression(0, ExprContext.AllowBraceLiteral);
          Token closeBracket = ExpectCloseWithRelated(TokenKind.CloseBracket, openBracket);
          left = new IndexExpressionSyntax(
              NextId(),
              SourceSpan.FromBounds(left.Span.Start, closeBracket.Span.End),
              left,
              openBracket,
              index,
              closeBracket);
          return true;
        }

      case TokenKind.OpenParen: {
          if (HasLeadingNewline(Current)) {
            return false;
          }

          (Token openParen, ImmutableArray<ArgumentSyntax> args, Token closeParen) = ParseArgumentList();
          left = new CallExpressionSyntax(
              NextId(),
              SourceSpan.FromBounds(left.Span.Start, closeParen.Span.End),
              left,
              openParen,
              args,
              closeParen);
          return true;
        }

      case TokenKind.Colon: {
          Token colon = Consume();
          Token member = ExpectIdentifier();
          var memberAccess = new MemberAccessExpressionSyntax(
              NextId(),
              SourceSpan.FromBounds(left.Span.Start, member.Span.End),
              left,
              colon,
              member);

          if ((member.Flags & TokenFlags.Missing) == 0
              && Current.Kind == TokenKind.OpenBracket
              && !HasLeadingNewline(Current)
              && TryParseTypeArgumentListSpeculative(out TypeArgumentListSyntax? typeArgs)) {
            if (Current.Kind == TokenKind.OpenParen && !HasLeadingNewline(Current)) {
              (Token openParen, ImmutableArray<ArgumentSyntax> args, Token closeParen) = ParseArgumentList();
              left = new CallExpressionSyntax(
                  NextId(),
                  SourceSpan.FromBounds(memberAccess.Span.Start, closeParen.Span.End),
                  memberAccess,
                  openParen,
                  args,
                  closeParen,
                  typeArgs);
              return true;
            }

            // D-076: `r:items[1]` without `(` is indexing, not generic method call.
            RollbackSpeculativeTypeArguments(typeArgs);
          }

          left = memberAccess;
          return true;
        }

      case TokenKind.ColonColon: {
          Token colonColon = Consume();
          Token member = ExpectIdentifier();
          left = new StaticAccessExpressionSyntax(
              NextId(),
              SourceSpan.FromBounds(left.Span.Start, member.Span.End),
              left,
              colonColon,
              member);
          return true;
        }

      case TokenKind.Question: {
          Token question = Consume();
          left = new BubbleExpressionSyntax(
              NextId(),
              SourceSpan.FromBounds(left.Span.Start, question.Span.End),
              left,
              question);
          return true;
        }

      case TokenKind.Or when Peek(1).Kind == TokenKind.OpenBrace: {
          Token orKeyword = Consume();
          RecoveryBlockSyntax block = ParseRecoveryBlock();
          left = new RecoveryExpressionSyntax(
              NextId(),
              SourceSpan.FromBounds(left.Span.Start, block.Span.End),
              left,
              orKeyword,
              block);
          return true;
        }

      default:
        _ = context;
        return false;
    }
  }

  private int _speculativeStartPos;
  private int _speculativeStartNextId;
  private int _speculativeLastError;

  private bool TryParseTypeArgumentListSpeculative([NotNullWhen(true)] out TypeArgumentListSyntax? typeArgs) {
    _speculativeStartPos = _pos;
    _speculativeStartNextId = _nextId;
    _speculativeLastError = _lastErrorTokenIndex;

    if (TryParseTypeArgumentListAfterBase(out typeArgs)) {
      return true;
    }

    RollbackSpeculativeTypeArguments(typeArgs);
    typeArgs = null;
    return false;
  }

  private bool TryParseTypeArgumentListAfterBase([NotNullWhen(true)] out TypeArgumentListSyntax? typeArgs) {
    bool prevSuppress = _suppressDiagnostics;
    bool prevSpecError = _speculativeError;
    _suppressDiagnostics = true;
    _speculativeError = false;

    typeArgs = ParseTypeArgumentList();
    bool ok = !_speculativeError
        && typeArgs.Arguments.Length > 0
        && (typeArgs.OpenBracketToken.Flags & TokenFlags.Missing) == 0
        && (typeArgs.CloseBracketToken.Flags & TokenFlags.Missing) == 0;

    _suppressDiagnostics = prevSuppress;
    _speculativeError = prevSpecError;
    return ok;
  }

  private void RollbackSpeculativeTypeArguments(TypeArgumentListSyntax? _) {
    _pos = _speculativeStartPos;
    _nextId = _speculativeStartNextId;
    _lastErrorTokenIndex = _speculativeLastError;
  }

  private bool IsBalancedBracketFollowedBy(int bracketOffset, TokenKind expectedAfter) {
    int depth = 0;
    int offset = bracketOffset;
    while (true) {
      Token tok = Peek(offset);
      if (tok.Kind == TokenKind.EOF) {
        return false;
      }

      if (tok.Kind == TokenKind.OpenBracket) {
        depth++;
      } else if (tok.Kind == TokenKind.CloseBracket) {
        depth--;
        if (depth == 0) {
          return Peek(offset + 1).Kind == expectedAfter;
        }
      }

      offset++;
    }
  }

  private (Token OpenParen, ImmutableArray<ArgumentSyntax> Args, Token CloseParen) ParseArgumentList() {
    Token openParen = Consume();
    var args = ImmutableArray.CreateBuilder<ArgumentSyntax>();

    while (Current.Kind is not (TokenKind.CloseParen or TokenKind.EOF)) {
      if (IsStatementSyncToken(Current.Kind) && !CanStartExpression(Current)) {
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
            "Unexpected ',' before argument.");
        var missingExpr = new MissingExpressionSyntax(NextId(), new SourceSpan(leadingComma.Span.Start, 0));
        args.Add(new ArgumentSyntax(NextId(), leadingComma.Span, missingExpr, leadingComma));
        continue;
      }

      int beforeExprId = _nextId;
      ExpressionSyntax argExpr = ParseExpression(0, ExprContext.AllowBraceLiteral | ExprContext.InCommaOptionalList);
      if (argExpr is MissingExpressionSyntax && _pos == startPos) {
        _nextId = beforeExprId;
        if (IsStatementSyncToken(Current.Kind) || IsCloserOrEOF(Current.Kind)) {
          break;
        }

        Token skipped = Consume();
        argExpr = new ErrorExpressionSyntax(NextId(), skipped.Span, [skipped]);
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
              "Expected argument before consecutive ','.");
        }
      }

      int endOffset = commaToken is { } c ? c.Span.End : argExpr.Span.End;
      args.Add(new ArgumentSyntax(
          NextId(),
          SourceSpan.FromBounds(argExpr.Span.Start, endOffset),
          argExpr,
          commaToken));

      if (commaToken is { } && Current.Kind == TokenKind.CloseParen) {
        break;
      }
    }

    Token closeParen = ExpectCloseWithRelated(TokenKind.CloseParen, openParen);
    return (openParen, args.ToImmutable(), closeParen);
  }

  private InterpolatedStringExpressionSyntax ParseInterpolatedString() {
    Token stringStart = Consume();
    var parts = ImmutableArray.CreateBuilder<StringPartSyntax>();

    while (Current.Kind is not (TokenKind.StringEnd or TokenKind.EOF)) {
      int startPos = _pos;
      if (Current.Kind == TokenKind.StringPart) {
        Token part = Consume();
        parts.Add(new StringTextPartSyntax(NextId(), part.Span, part));
      } else if (Current.Kind == TokenKind.InterpStart) {
        Token interpStart = Consume();
        int beforeExprId = _nextId;
        ExpressionSyntax expr = ParseExpression(0, ExprContext.AllowBraceLiteral);

        if (Current.Kind is not (TokenKind.InterpEnd or TokenKind.StringEnd or TokenKind.EOF)) {
          ReportError(
              DiagnosticCodes.ExpectedToken,
              Current.Span,
              $"Expected ')', found {DescribeToken(Current)}.");
          var combinedTokens = ImmutableArray.CreateBuilder<Token>();
          foreach (Token t in expr.DescendantTokens()) {
            if ((t.Flags & TokenFlags.Missing) == 0) {
              combinedTokens.Add(t);
            }
          }

          while (Current.Kind is not (TokenKind.InterpEnd or TokenKind.StringEnd or TokenKind.EOF)) {
            combinedTokens.Add(Consume());
          }

          if (combinedTokens.Count > 0) {
            _nextId = beforeExprId;
            expr = new ErrorExpressionSyntax(
                NextId(),
                SourceSpan.FromBounds(combinedTokens[0].Span.Start, combinedTokens[^1].Span.End),
                combinedTokens.ToImmutable());
          }
        }

        Token interpEnd = ExpectCloseWithRelated(TokenKind.InterpEnd, interpStart);
        parts.Add(new StringInterpolationPartSyntax(
            NextId(),
            SourceSpan.FromBounds(interpStart.Span.Start, interpEnd.Span.End),
            interpStart,
            expr,
            interpEnd));
      } else {
        ReportError(
            DiagnosticCodes.ExpectedToken,
            Current.Span,
            $"Unexpected token {DescribeToken(Current)} inside string literal.");
        Token skipped = Consume();
        parts.Add(new StringTextPartSyntax(NextId(), skipped.Span, skipped));
      }

      if (_pos == startPos && !IsAtEnd) {
        Token skipped = Consume();
        parts.Add(new StringTextPartSyntax(NextId(), skipped.Span, skipped));
      }
    }

    Token stringEnd = ExpectCloseWithRelated(TokenKind.StringEnd, stringStart);
    return new InterpolatedStringExpressionSyntax(
        NextId(),
        SourceSpan.FromBounds(stringStart.Span.Start, stringEnd.Span.End),
        stringStart,
        parts.ToImmutable(),
        stringEnd);
  }

  private ExpressionSyntax ParseVectorOrVectorInitializer(ExprContext context) {
    if (Peek(1).Kind == TokenKind.CloseBracket) {
      Token openBracket = Consume();
      Token closeBracket = Consume();
      if (!HasLeadingNewline(Current) && CanStartType(Current, disallowNewlineBeforeBracket: true)) {
        return ParseVectorInitializer(openBracket, closeBracket, context);
      }

      return new VectorLiteralExpressionSyntax(
          NextId(),
          SourceSpan.FromBounds(openBracket.Span.Start, closeBracket.Span.End),
          openBracket,
          [],
          closeBracket,
          []);
    }

    if (_depth >= MaxDepth) {
      ReportDepthExceeded();
      return DrainExpressionTokens();
    }

    _depth++;
    try {
      return ParseVectorLiteralElements();
    } finally {
      _depth--;
    }
  }

  private VectorInitializerExpressionSyntax ParseVectorInitializer(
      Token openBracket,
      Token closeBracket,
      ExprContext context) {
    TypeSyntax elementType = ParseType();
    Token? openBrace = null;
    Token? closeBrace = null;
    var props = ImmutableArray.CreateBuilder<VectorPropertySyntax>();

    if (context.HasFlag(ExprContext.AllowBraceLiteral) && Current.Kind == TokenKind.OpenBrace) {
      openBrace = Consume();
      bool pushed = PushOpenBrace(openBrace.Value);
      try {
        while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EOF)) {
          if (CheckColumn1Trigger()) {
            break;
          }

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
                "Unexpected ',' before vector property.");
            var missingSpan = new SourceSpan(leadingComma.Span.Start, 0);
            var missingName = new Token(TokenKind.Identifier, missingSpan, TokenFlags.Missing);
            var missingColon = new Token(TokenKind.Colon, missingSpan, TokenFlags.Missing);
            var missingVal = new MissingExpressionSyntax(NextId(), missingSpan);
            props.Add(new VectorPropertySyntax(
                NextId(),
                leadingComma.Span,
                missingName,
                missingColon,
                missingVal,
                leadingComma));
            continue;
          }

          if (Current.Kind != TokenKind.Identifier && Current.Kind != TokenKind.Colon) {
            ReportError(
                DiagnosticCodes.ExpectedIdentifier,
                Current.Span,
                $"Expected vector property name, found {DescribeToken(Current)}.");
            break;
          }

          Token propName = ExpectIdentifier();
          Token colon = Expect(TokenKind.Colon);
          ExpressionSyntax propValue = ParseExpression(0, ExprContext.AllowBraceLiteral);

          Token? commaToken = null;
          if (Current.Kind == TokenKind.Comma) {
            int commaIndex = _pos;
            Token comma = Consume();
            commaToken = comma;
            if (Current.Kind == TokenKind.CloseBrace) {
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
                  "Expected property before consecutive ','.");
            }
          }

          int endOffset = commaToken is { } c ? c.Span.End : propValue.Span.End;
          props.Add(new VectorPropertySyntax(
              NextId(),
              SourceSpan.FromBounds(propName.Span.Start, endOffset),
              propName,
              colon,
              propValue,
              commaToken));

          if (commaToken is { } && Current.Kind == TokenKind.CloseBrace) {
            break;
          }

          if (_pos == startPos) {
            break;
          }
        }
      } finally {
        PopOpenBrace(pushed);
      }

      closeBrace = ExpectCloseWithRelated(TokenKind.CloseBrace, openBrace.Value);
    }

    int finalEnd = closeBrace is { } cb ? cb.Span.End : elementType.Span.End;
    return new VectorInitializerExpressionSyntax(
        NextId(),
        SourceSpan.FromBounds(openBracket.Span.Start, finalEnd),
        openBracket,
        closeBracket,
        elementType,
        openBrace,
        props.ToImmutable(),
        closeBrace);
  }

  private VectorLiteralExpressionSyntax ParseVectorLiteralElements() {
    Token openBracket = Consume();
    var elements = ImmutableArray.CreateBuilder<ExpressionSyntax>();
    var commas = ImmutableArray.CreateBuilder<Token>();

    while (Current.Kind is not (TokenKind.CloseBracket or TokenKind.EOF)) {
      if (IsStatementSyncToken(Current.Kind) && !CanStartExpression(Current)) {
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
            "Unexpected ',' before element.");
        commas.Add(leadingComma);
        continue;
      }

      int beforeElemId = _nextId;
      ExpressionSyntax element = ParseExpression(0, ExprContext.AllowBraceLiteral | ExprContext.InCommaOptionalList);
      if (element is MissingExpressionSyntax && _pos == startPos) {
        _nextId = beforeElemId;
        if (IsStatementSyncToken(Current.Kind) || IsCloserOrEOF(Current.Kind)) {
          break;
        }

        Token skipped = Consume();
        element = new ErrorExpressionSyntax(NextId(), skipped.Span, [skipped]);
      }

      elements.Add(element);

      if (Current.Kind == TokenKind.Comma) {
        int commaIndex = _pos;
        Token comma = Consume();
        commas.Add(comma);
        if (Current.Kind == TokenKind.CloseBracket) {
          ReportErrorAt(
              commaIndex,
              DiagnosticCodes.TrailingComma,
              comma.Span,
              "Trailing comma is not allowed.",
              help: "Remove the trailing ','.");
          break;
        }

        if (Current.Kind == TokenKind.Comma) {
          ReportErrorAt(
              _pos,
              DiagnosticCodes.UnexpectedComma,
              Current.Span,
              "Expected item before consecutive ','.");
        }
      }
    }

    Token endBracket = ExpectCloseWithRelated(TokenKind.CloseBracket, openBracket);
    return new VectorLiteralExpressionSyntax(
        NextId(),
        SourceSpan.FromBounds(openBracket.Span.Start, endBracket.Span.End),
        openBracket,
        elements.ToImmutable(),
        endBracket,
        commas.ToImmutable());
  }

  private MapLiteralExpressionSyntax ParseMapLiteral(ExprContext context) {
    MapTypeSyntax mapType = ParseMapType();
    Token? openBrace = null;
    Token? closeBrace = null;
    var entries = ImmutableArray.CreateBuilder<MapEntrySyntax>();

    if (context.HasFlag(ExprContext.AllowBraceLiteral) && Current.Kind == TokenKind.OpenBrace) {
      openBrace = Consume();
      bool pushed = PushOpenBrace(openBrace.Value);
      try {
        while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EOF)) {
          if (CheckColumn1Trigger()) {
            break;
          }

          if (IsStatementSyncToken(Current.Kind) && !CanStartExpression(Current)) {
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
                "Unexpected ',' before map entry.");
            var missingSpan = new SourceSpan(leadingComma.Span.Start, 0);
            var missingKey = new MissingExpressionSyntax(NextId(), missingSpan);
            var missingArrow = new Token(TokenKind.Arrow, missingSpan, TokenFlags.Missing);
            var missingValue = new MissingExpressionSyntax(NextId(), missingSpan);
            entries.Add(new MapEntrySyntax(
                NextId(),
                leadingComma.Span,
                missingKey,
                missingArrow,
                missingValue,
                leadingComma));
            continue;
          }

          int beforeKeyId = _nextId;
          ExpressionSyntax key = ParseExpression(0, ExprContext.AllowBraceLiteral | ExprContext.InCommaOptionalList);
          if (key is MissingExpressionSyntax && _pos == startPos) {
            _nextId = beforeKeyId;
            if (IsStatementSyncToken(Current.Kind) || IsCloserOrEOF(Current.Kind)) {
              break;
            }

            Token skipped = Consume();
            key = new ErrorExpressionSyntax(NextId(), skipped.Span, [skipped]);
          }

          Token arrowToken;
          Token? secondaryArrow;
          if (!TryConsumeArrow(out arrowToken, out secondaryArrow)) {
            arrowToken = Expect(TokenKind.Arrow);
          }

          ExpressionSyntax value = ParseExpression(0, ExprContext.AllowBraceLiteral | ExprContext.InCommaOptionalList);

          Token? commaToken = null;
          if (Current.Kind == TokenKind.Comma) {
            int commaIndex = _pos;
            Token comma = Consume();
            commaToken = comma;
            if (Current.Kind == TokenKind.CloseBrace) {
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
                  "Expected map entry before consecutive ','.");
            }
          }

          int endOffset = commaToken is { } c
              ? c.Span.End
              : Math.Max(value.Span.End, (secondaryArrow ?? arrowToken).Span.End);
          entries.Add(new MapEntrySyntax(
              NextId(),
              SourceSpan.FromBounds(key.Span.Start, endOffset),
              key,
              arrowToken,
              value,
              commaToken,
              secondaryArrow));

          if (commaToken is { } && Current.Kind == TokenKind.CloseBrace) {
            break;
          }
        }
      } finally {
        PopOpenBrace(pushed);
      }

      closeBrace = ExpectCloseWithRelated(TokenKind.CloseBrace, openBrace.Value);
    }

    int finalEnd = closeBrace is { } cb ? cb.Span.End : mapType.Span.End;
    return new MapLiteralExpressionSyntax(
        NextId(),
        SourceSpan.FromBounds(mapType.Span.Start, finalEnd),
        mapType,
        openBrace,
        entries.ToImmutable(),
        closeBrace);
  }

  private SetLiteralExpressionSyntax ParseSetLiteral(ExprContext context) {
    SetTypeSyntax setType = ParseSetType();
    Token? openBrace = null;
    Token? closeBrace = null;
    var elements = ImmutableArray.CreateBuilder<ExpressionSyntax>();
    var commas = ImmutableArray.CreateBuilder<Token>();

    if (context.HasFlag(ExprContext.AllowBraceLiteral) && Current.Kind == TokenKind.OpenBrace) {
      openBrace = Consume();
      bool pushed = PushOpenBrace(openBrace.Value);
      try {
        while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EOF)) {
          if (CheckColumn1Trigger()) {
            break;
          }

          if (IsStatementSyncToken(Current.Kind) && !CanStartExpression(Current)) {
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
                "Unexpected ',' before set element.");
            commas.Add(leadingComma);
            continue;
          }

          int beforeElemId = _nextId;
          ExpressionSyntax element = ParseExpression(0, ExprContext.AllowBraceLiteral | ExprContext.InCommaOptionalList);
          if (element is MissingExpressionSyntax && _pos == startPos) {
            _nextId = beforeElemId;
            if (IsStatementSyncToken(Current.Kind) || IsCloserOrEOF(Current.Kind)) {
              break;
            }

            Token skipped = Consume();
            element = new ErrorExpressionSyntax(NextId(), skipped.Span, [skipped]);
          }

          elements.Add(element);

          if (Current.Kind == TokenKind.Comma) {
            int commaIndex = _pos;
            Token comma = Consume();
            commas.Add(comma);
            if (Current.Kind == TokenKind.CloseBrace) {
              ReportErrorAt(
                  commaIndex,
                  DiagnosticCodes.TrailingComma,
                  comma.Span,
                  "Trailing comma is not allowed.",
                  help: "Remove the trailing ','.");
              break;
            }

            if (Current.Kind == TokenKind.Comma) {
              ReportErrorAt(
                  _pos,
                  DiagnosticCodes.UnexpectedComma,
                  Current.Span,
                  "Expected element before consecutive ','.");
            }
          }
        }
      } finally {
        PopOpenBrace(pushed);
      }

      closeBrace = ExpectCloseWithRelated(TokenKind.CloseBrace, openBrace.Value);
    }

    int finalEnd = closeBrace is { } cb ? cb.Span.End : setType.Span.End;
    return new SetLiteralExpressionSyntax(
        NextId(),
        SourceSpan.FromBounds(setType.Span.Start, finalEnd),
        setType,
        openBrace,
        elements.ToImmutable(),
        closeBrace,
        commas.ToImmutable());
  }

  private StructLiteralExpressionSyntax ParseStructLiteral(TypeSyntax typeName) {
    Token openBrace = Consume();
    var fields = ImmutableArray.CreateBuilder<StructFieldInitSyntax>();
    bool pushed = PushOpenBrace(openBrace);
    try {
      while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EOF)) {
        if (CheckColumn1Trigger()) {
          break;
        }

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
              "Unexpected ',' before struct field.");
          Token missingName = new(TokenKind.Identifier, new SourceSpan(leadingComma.Span.Start, 0), TokenFlags.Missing);
          fields.Add(new StructFieldInitSyntax(
              NextId(),
              leadingComma.Span,
              missingName,
              null,
              null,
              leadingComma));
          continue;
        }

        if (Current.Kind != TokenKind.Identifier && Current.Kind != TokenKind.Colon) {
          ReportError(
              DiagnosticCodes.ExpectedIdentifier,
              Current.Span,
              $"Expected field name, found {DescribeToken(Current)}.");
          break;
        }

        Token fieldName = ExpectIdentifier();
        Token? colonToken = null;
        ExpressionSyntax? value = null;
        if (Current.Kind == TokenKind.Colon) {
          colonToken = Consume();
          value = ParseExpression(0, ExprContext.AllowBraceLiteral);
        }

        Token? commaToken = null;
        if (Current.Kind == TokenKind.Comma) {
          int commaIndex = _pos;
          Token comma = Consume();
          commaToken = comma;
          if (Current.Kind == TokenKind.CloseBrace) {
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

        int endOffset = commaToken is { } c
            ? c.Span.End
            : value is not null
                ? value.Span.End
                : colonToken is { } col
                    ? col.Span.End
                    : fieldName.Span.End;
        fields.Add(new StructFieldInitSyntax(
            NextId(),
            SourceSpan.FromBounds(fieldName.Span.Start, endOffset),
            fieldName,
            colonToken,
            value,
            commaToken));

        if (commaToken is { } && Current.Kind == TokenKind.CloseBrace) {
          break;
        }

        if (_pos == startPos) {
          break;
        }
      }
    } finally {
      PopOpenBrace(pushed);
    }

    Token closeBrace = ExpectCloseWithRelated(TokenKind.CloseBrace, openBrace);
    return new StructLiteralExpressionSyntax(
        NextId(),
        SourceSpan.FromBounds(typeName.Span.Start, closeBrace.Span.End),
        typeName,
        openBrace,
        fields.ToImmutable(),
        closeBrace);
  }

  private TupleLiteralExpressionSyntax ParseTupleLiteral() {
    Token atToken = Consume();
    Token openParen = Expect(TokenKind.OpenParen);
    var fields = ImmutableArray.CreateBuilder<TupleFieldSyntax>();

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
            "Unexpected ',' before tuple field.");
        Token missingName = new(TokenKind.Identifier, new SourceSpan(leadingComma.Span.Start, 0), TokenFlags.Missing);
        fields.Add(new TupleFieldSyntax(
            NextId(),
            leadingComma.Span,
            missingName,
            null,
            null,
            leadingComma));
        continue;
      }

      if (Current.Kind != TokenKind.Identifier && Current.Kind != TokenKind.Colon) {
        ReportError(
            DiagnosticCodes.ExpectedIdentifier,
            Current.Span,
            $"Expected tuple field name, found {DescribeToken(Current)}.");
        break;
      }

      Token fieldName = ExpectIdentifier();
      Token? colonToken = null;
      ExpressionSyntax? value = null;
      if (Current.Kind == TokenKind.Colon) {
        colonToken = Consume();
        value = ParseExpression(0, ExprContext.AllowBraceLiteral);
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
              "Expected tuple field before consecutive ','.");
        }
      }

      int endOffset = commaToken is { } c
          ? c.Span.End
          : value is not null
              ? value.Span.End
              : colonToken is { } col
                  ? col.Span.End
                  : fieldName.Span.End;
      fields.Add(new TupleFieldSyntax(
          NextId(),
          SourceSpan.FromBounds(fieldName.Span.Start, endOffset),
          fieldName,
          colonToken,
          value,
          commaToken));

      if (commaToken is { } && Current.Kind == TokenKind.CloseParen) {
        break;
      }

      if (_pos == startPos) {
        break;
      }
    }

    Token closeParen = ExpectCloseWithRelated(TokenKind.CloseParen, openParen);
    return new TupleLiteralExpressionSyntax(
        NextId(),
        SourceSpan.FromBounds(atToken.Span.Start, closeParen.Span.End),
        atToken,
        openParen,
        fields.ToImmutable(),
        closeParen);
  }

  private ExpressionSyntax ParseLambda(ExprContext context) {
    if (_depth >= MaxDepth) {
      ReportDepthExceeded();
      return DrainExpressionTokens();
    }

    _depth++;
    try {
      Token fnKeyword = Consume();
      (Token openParen, ImmutableArray<ParameterSyntax> parameters, Token closeParen) =
          ParseLambdaParameters();

      Token? arrowToken = null;
      Token? secondaryArrow = null;
      ExpressionSyntax? bodyExpr = null;
      BlockStatementSyntax? bodyBlock = null;

      if (TryConsumeArrow(out Token arrow, out Token? secondary)) {
        arrowToken = arrow;
        secondaryArrow = secondary;
        ExprContext bodyContext = context & ~ExprContext.InCommaOptionalList;
        bodyExpr = ParseExpression(0, bodyContext);
      } else {
        bodyBlock = ParseBlock();
      }

      int finalEnd = bodyBlock is not null
          ? bodyBlock.Span.End
          : bodyExpr is not null
              ? bodyExpr.Span.End
              : closeParen.Span.End;
      return new LambdaExpressionSyntax(
          NextId(),
          SourceSpan.FromBounds(fnKeyword.Span.Start, finalEnd),
          fnKeyword,
          openParen,
          parameters,
          closeParen,
          arrowToken,
          bodyExpr,
          bodyBlock,
          secondaryArrow);
    } finally {
      _depth--;
    }
  }

  private (Token OpenParen, ImmutableArray<ParameterSyntax> Parameters, Token CloseParen) ParseLambdaParameters() {
    Token openParen = Expect(TokenKind.OpenParen);
    var parameters = ImmutableArray.CreateBuilder<ParameterSyntax>();

    while (Current.Kind is not (TokenKind.CloseParen or TokenKind.EOF)) {
      if (IsStatementSyncToken(Current.Kind)
          && Current.Kind != TokenKind.Identifier) {
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
            "Unexpected ',' before lambda parameter.");
        Token missingName = new(TokenKind.Identifier, new SourceSpan(leadingComma.Span.Start, 0), TokenFlags.Missing);
        parameters.Add(new ParameterSyntax(
            NextId(),
            leadingComma.Span,
            missingName,
            null,
            null,
            null,
            leadingComma));
        continue;
      }

      if (Current.Kind != TokenKind.Identifier) {
        ReportError(
            DiagnosticCodes.ExpectedIdentifier,
            Current.Span,
            $"Expected parameter name, found {DescribeToken(Current)}.");
        break;
      }

      Token paramName = Consume();
      TypeSyntax? paramType = null;
      if (Current.Kind is not (TokenKind.Comma or TokenKind.CloseParen or TokenKind.EOF)
          && CanStartType(Current)) {
        paramType = ParseType();
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
              "Expected parameter before consecutive ','.");
        }
      }

      int endOffset = commaToken is { } c
          ? c.Span.End
          : paramType is not null
              ? paramType.Span.End
              : paramName.Span.End;
      parameters.Add(new ParameterSyntax(
          NextId(),
          SourceSpan.FromBounds(paramName.Span.Start, endOffset),
          paramName,
          paramType,
          null,
          null,
          commaToken));

      if (commaToken is { } && Current.Kind == TokenKind.CloseParen) {
        break;
      }

      if (_pos == startPos) {
        break;
      }
    }

    Token closeParen = ExpectCloseWithRelated(TokenKind.CloseParen, openParen);
    return (openParen, parameters.ToImmutable(), closeParen);
  }

  private IfExpressionSyntax ParseIfExpression(ExprContext context) {
    Token ifKeyword = Consume();
    ExpressionSyntax condition = ParseExpression(0, ExprContext.AllowBraceLiteral);
    Token thenKeyword = Expect(TokenKind.ThenKeyword);
    ExpressionSyntax thenBranch = ParseExpression(0, ExprContext.AllowBraceLiteral);
    Token elseKeyword = Expect(TokenKind.ElseKeyword);
    ExpressionSyntax elseBranch = ParseExpression(0, context);

    return new IfExpressionSyntax(
        NextId(),
        SourceSpan.FromBounds(ifKeyword.Span.Start, elseBranch.Span.End),
        ifKeyword,
        condition,
        thenKeyword,
        thenBranch,
        elseKeyword,
        elseBranch);
  }

  // A `{` right after `match` is the arm list of a match whose subject is missing; reading it as the subject would
  // swallow the arms and the closing brace, so it is reported and the braces are left for the arm list.
  private ExpressionSyntax ParseMatchSubject() {
    if (Current.Kind == TokenKind.OpenBrace) {
      ReportError(
          DiagnosticCodes.ExpectedExpression,
          Current.Span,
          "Expected match subject, found '{'.");
      return new MissingExpressionSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0));
    }

    return ParseExpression(0, ExprContext.None);
  }

  private MatchExpressionSyntax ParseMatchExpression() {
    Token matchKeyword = Consume();
    ExpressionSyntax subject = ParseMatchSubject();
    Token openBrace = Expect(TokenKind.OpenBrace);
    ImmutableArray<SyntaxNode> arms = ParseMatchBody(openBrace, isStatement: false);
    Token closeBrace = ExpectCloseWithRelated(TokenKind.CloseBrace, openBrace);

    return new MatchExpressionSyntax(
        NextId(),
        SourceSpan.FromBounds(matchKeyword.Span.Start, closeBrace.Span.End),
        matchKeyword,
        subject,
        openBrace,
        arms,
        closeBrace);
  }

  private ImmutableArray<SyntaxNode> ParseMatchBody(Token openBrace, bool isStatement) {
    if (_depth >= MaxDepth) {
      ReportDepthExceeded();
      if ((openBrace.Flags & TokenFlags.Missing) == 0) {
        ImmutableArray<Token> skipped = DrainUntilClosingBrace(
            stopAtOuterCloseBrace: true,
            consumeClosingBrace: false,
            out _);
        if (!skipped.IsEmpty) {
          return [
            new ErrorMatchArmSyntax(
                NextId(),
                SourceSpan.FromBounds(skipped[0].Span.Start, skipped[^1].Span.End),
                skipped)
          ];
        }
      }

      return [];
    }

    _depth++;
    try {
      var arms = ImmutableArray.CreateBuilder<SyntaxNode>();
      bool sawElse = false;

      if ((openBrace.Flags & TokenFlags.Missing) == 0
          || Current.Kind is TokenKind.WhenKeyword or TokenKind.ElseKeyword) {
        bool pushed = PushOpenBrace(openBrace);
        try {
          while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EOF)) {
            if (CheckColumn1Trigger()) {
              break;
            }

            int startPos = _pos;
            if (Current.Kind == TokenKind.WhenKeyword) {
              arms.Add(ParseWhenArm(isStatement));
            } else if (Current.Kind == TokenKind.ElseKeyword) {
              Token elseKeyword = Consume();
              if (sawElse) {
                ReportErrorAt(
                    _pos - 1,
                    DiagnosticCodes.DuplicateElseArm,
                    elseKeyword.Span,
                    isStatement
                        ? "Duplicate 'else' arm in match statement."
                        : "Duplicate 'else' arm in match expression.");
              }

              sawElse = true;
              (Token? arrow, Token? secondary, ExpressionSyntax? exprBody, BlockStatementSyntax? blockBody) =
                  ParseMatchArmBody(isStatement);
              int endOffset = blockBody is not null
                  ? blockBody.Span.End
                  : exprBody is not null
                      ? exprBody.Span.End
                      : (secondary ?? arrow ?? elseKeyword).Span.End;
              arms.Add(new ElseArmSyntax(
                  NextId(),
                  SourceSpan.FromBounds(elseKeyword.Span.Start, endOffset),
                  elseKeyword,
                  arrow,
                  exprBody,
                  blockBody,
                  secondary));
            } else {
              ReportError(
                  DiagnosticCodes.ExpectedToken,
                  Current.Span,
                  $"Expected 'when', 'else', or '}}', found {DescribeToken(Current)}.");
              var skipped = ImmutableArray.CreateBuilder<Token>();
              while (Current.Kind is not (TokenKind.WhenKeyword or TokenKind.ElseKeyword or TokenKind.CloseBrace or TokenKind.EOF)) {
                if (MatchesColumn1Trigger()) {
                  break;
                }

                skipped.Add(Consume());
              }

              if (skipped.Count > 0) {
                arms.Add(new ErrorMatchArmSyntax(
                    NextId(),
                    SourceSpan.FromBounds(skipped[0].Span.Start, skipped[^1].Span.End),
                    skipped.ToImmutable()));
              }
            }

            if (_pos == startPos && !IsAtEnd) {
              Token skipped = Consume();
              arms.Add(new ErrorMatchArmSyntax(NextId(), skipped.Span, [skipped]));
            }
          }
        } finally {
          PopOpenBrace(pushed);
        }
      }

      return arms.ToImmutable();
    } finally {
      _depth--;
    }
  }

  private WhenArmSyntax ParseWhenArm(bool isStatement) {
    Token whenKeyword = Consume();
    var patterns = ImmutableArray.CreateBuilder<PatternSyntax>();
    var commas = ImmutableArray.CreateBuilder<Token>();

    if (IsMatchArmBodyBoundary()) {
      ReportError(
          DiagnosticCodes.ExpectedPattern,
          Current.Span,
          $"Expected pattern after 'when', found {DescribeToken(Current)}.");
      patterns.Add(new MissingPatternSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0)));
    } else {
      while (!IsMatchArmBodyBoundary()) {
        int startPos = _pos;
        if (Current.Kind == TokenKind.Comma) {
          int commaIndex = _pos;
          Token leadingComma = Consume();
          ReportErrorAt(
              commaIndex,
              DiagnosticCodes.UnexpectedComma,
              leadingComma.Span,
              "Unexpected ',' before pattern.");
          commas.Add(leadingComma);
          continue;
        }

        int beforePatId = _nextId;
        PatternSyntax pat = ParsePattern();
        if (pat is MissingPatternSyntax && _pos == startPos) {
          _nextId = beforePatId;
          break;
        }

        patterns.Add(pat);

        if (Current.Kind == TokenKind.Comma) {
          int commaIndex = _pos;
          Token comma = Consume();
          commas.Add(comma);
          if (IsMatchArmBodyBoundary()) {
            ReportErrorAt(
                commaIndex,
                DiagnosticCodes.TrailingComma,
                comma.Span,
                "Trailing comma is not allowed.",
                help: "Remove the trailing ','.");
            break;
          }

          if (Current.Kind == TokenKind.Comma) {
            ReportErrorAt(
                _pos,
                DiagnosticCodes.UnexpectedComma,
                Current.Span,
                "Expected pattern before consecutive ','.");
          }
        }
      }

      if (patterns.Count == 0) {
        patterns.Add(new MissingPatternSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0)));
      }
    }

    (Token? arrow, Token? secondary, ExpressionSyntax? exprBody, BlockStatementSyntax? blockBody) =
        ParseMatchArmBody(isStatement);
    int endOffset = blockBody is not null
        ? blockBody.Span.End
        : exprBody is not null
            ? exprBody.Span.End
            : (secondary ?? arrow) is { } a
                ? a.Span.End
                : patterns[^1].Span.End;
    if (commas.Count > 0 && commas[^1].Span.End > endOffset) {
      endOffset = commas[^1].Span.End;
    }

    return new WhenArmSyntax(
        NextId(),
        SourceSpan.FromBounds(whenKeyword.Span.Start, endOffset),
        whenKeyword,
        patterns.ToImmutable(),
        arrow,
        exprBody,
        blockBody,
        commas.ToImmutable(),
        secondary);
  }

  private bool IsMatchArmBodyBoundary() =>
      Current.Kind is TokenKind.Arrow
          or TokenKind.OpenBrace
          or TokenKind.WhenKeyword
          or TokenKind.ElseKeyword
          or TokenKind.CloseBrace
          or TokenKind.EOF
      || IsFatArrow()
      || MatchesColumn1Trigger();

  private (Token? Arrow, Token? SecondaryArrow, ExpressionSyntax? ExprBody, BlockStatementSyntax? BlockBody) ParseMatchArmBody(bool isStatement) {
    if (isStatement) {
      if (Current.Kind == TokenKind.OpenBrace) {
        return (null, null, null, ParseBlock());
      }

      int arrowIndex = _pos;
      if (TryConsumeAnyArrowTokens(out Token stmtArrow, out Token? stmtSecondary)) {
        // MATCH-E7 (D-148): `->` arms are only allowed in `match` expressions.
        ReportErrorAt(
            arrowIndex,
            DiagnosticCodes.ExpectedToken,
            SourceSpan.FromBounds(stmtArrow.Span.Start, (stmtSecondary ?? stmtArrow).Span.End),
            "Expected '{' for match statement arm; '->' arms are only allowed in match expressions.");
        if (Current.Kind == TokenKind.OpenBrace) {
          return (stmtArrow, stmtSecondary, null, ParseBlock());
        }

        ExpressionSyntax recoveredExpr = ParseExpression(0, ExprContext.AllowBraceLiteral);
        return (stmtArrow, stmtSecondary, recoveredExpr, null);
      }

      ReportError(
          DiagnosticCodes.ExpectedToken,
          Current.Span,
          $"Expected '{{' for match statement arm, found {DescribeToken(Current)}.");
      return (null, null, null, null);
    }

    if (TryConsumeArrow(out Token arrow, out Token? secondary)) {
      if (Current.Kind == TokenKind.OpenBrace) {
        ReportError(
            DiagnosticCodes.ArrowBeforeMatchBlock,
            Current.Span,
            "Unexpected '{' after '->' in match arm; remove '->' for a block arm.");
        BlockStatementSyntax block = ParseBlock();
        return (arrow, secondary, null, block);
      }

      if (Current.Kind == TokenKind.ReturnKeyword) {
        ReportError(
            DiagnosticCodes.ExpectedExpression,
            Current.Span,
            "Expected expression after '->'; 'return' is not an expression (use a block arm '{ return ... }' instead).");
        var missingExpr = new MissingExpressionSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0));
        return (arrow, secondary, missingExpr, null);
      }

      ExpressionSyntax expr = ParseExpression(0, ExprContext.AllowBraceLiteral);
      return (arrow, secondary, expr, null);
    }

    if (Current.Kind == TokenKind.OpenBrace) {
      return (null, null, null, ParseBlock());
    }

    ReportError(
        DiagnosticCodes.ExpectedToken,
        Current.Span,
        $"Expected '->' or '{{' for match arm body, found {DescribeToken(Current)}.");
    return (null, null, null, null);
  }

  private ErrorExpressionSyntax ConsumeBalancedBracesAsErrorExpression() {
    var skipped = ImmutableArray.CreateBuilder<Token>();
    int braceDepth = 0;
    while (!IsAtEnd) {
      Token tok = Consume();
      skipped.Add(tok);
      if (tok.Kind == TokenKind.OpenBrace) {
        braceDepth++;
      } else if (tok.Kind == TokenKind.CloseBrace) {
        braceDepth--;
        if (braceDepth <= 0) {
          break;
        }
      }
    }

    return new ErrorExpressionSyntax(
        NextId(),
        SourceSpan.FromBounds(skipped[0].Span.Start, skipped[^1].Span.End),
        skipped.ToImmutable());
  }

  private ExpressionSyntax DrainExpressionTokens() {
    var skipped = ImmutableArray.CreateBuilder<Token>();
    var delimiters = new Stack<TokenKind>();

    while (!IsAtEnd) {
      TokenKind kind = Current.Kind;
      if (delimiters.Count == 0) {
        if (kind is TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.CloseBrace or TokenKind.InterpEnd or TokenKind.StringEnd or TokenKind.Comma) {
          break;
        }

        if (skipped.Count > 0 && HasLeadingNewline(Current) && IsStatementSyncToken(kind)) {
          break;
        }
      }

      Token tok = Consume();
      skipped.Add(tok);

      switch (tok.Kind) {
        case TokenKind.OpenParen:
          delimiters.Push(TokenKind.CloseParen);
          break;
        case TokenKind.OpenBracket:
          delimiters.Push(TokenKind.CloseBracket);
          break;
        case TokenKind.OpenBrace:
          delimiters.Push(TokenKind.CloseBrace);
          break;
        case TokenKind.InterpStart:
          delimiters.Push(TokenKind.InterpEnd);
          break;
        case TokenKind.StringStart:
          delimiters.Push(TokenKind.StringEnd);
          break;
        case TokenKind.CloseParen:
        case TokenKind.CloseBracket:
        case TokenKind.CloseBrace:
        case TokenKind.InterpEnd:
        case TokenKind.StringEnd:
          if (delimiters.Count > 0 && delimiters.Peek() == tok.Kind) {
            delimiters.Pop();
          }

          break;
      }
    }

    if (skipped.Count == 0) {
      return new MissingExpressionSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0));
    }

    return new ErrorExpressionSyntax(
        NextId(),
        SourceSpan.FromBounds(skipped[0].Span.Start, skipped[^1].Span.End),
        skipped.ToImmutable());
  }

  private static bool IsAssignmentOperator(TokenKind kind) =>
      kind is TokenKind.Assign
          or TokenKind.PlusAssign
          or TokenKind.MinusAssign
          or TokenKind.StarAssign
          or TokenKind.SlashAssign
          or TokenKind.SlashSlashAssign
          or TokenKind.PercentAssign;

  private static int GetBinaryLeftBindingPower(TokenKind kind) =>
      kind switch {
        TokenKind.Or => 20,
        TokenKind.And => 30,
        TokenKind.EqualEqual or TokenKind.BangEqual or TokenKind.EqualEqualEqual or TokenKind.BangEqualEqual => 40,
        TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual or TokenKind.InKeyword => 50,
        TokenKind.Plus or TokenKind.Minus => 60,
        TokenKind.Star or TokenKind.Slash or TokenKind.SlashSlash or TokenKind.Percent => 70,
        _ => 0,
      };
}
