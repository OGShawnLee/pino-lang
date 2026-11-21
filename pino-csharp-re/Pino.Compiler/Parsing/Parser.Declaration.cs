using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Parsing;

public sealed partial class Parser {
  private readonly record struct VisibilityTokens(Token? AtToken, Token? SecondAtToken, Token KeywordOrIdent) {
    public SourceSpan Span => SourceSpan.FromBounds((AtToken ?? KeywordOrIdent).Span.Start, KeywordOrIdent.Span.End);

    public void AppendTo(ImmutableArray<Token>.Builder tokens) {
      if (AtToken is { } at) {
        tokens.Add(at);
      }

      if (SecondAtToken is { } secondAt) {
        tokens.Add(secondAt);
      }

      tokens.Add(KeywordOrIdent);
    }
  }

  // D-163: visibility is `pub` or `@@Testable` (two adjacent `@` tokens and the identifier `Testable`).
  private bool IsTestableStart() => IsAdjacentDoubleAt() && IsIdentifierText(Peek(2), "Testable");

  // The old spelling `@pub_testing` is still recognized so it can be diagnosed and recovered as `@@Testable`.
  private bool IsDeprecatedPubTestingStart() =>
      Current.Kind == TokenKind.At && IsIdentifierText(Peek(1), "pub_testing");

  private bool IsVisibilityStart() =>
      Current.Kind == TokenKind.PubKeyword || IsTestableStart() || IsDeprecatedPubTestingStart();

  private VisibilityTokens ParseVisibilityTokens() {
    if (Current.Kind == TokenKind.PubKeyword) {
      return new VisibilityTokens(null, null, Consume());
    }

    if (IsTestableStart()) {
      Token firstAt = Consume();
      Token secondAt = Consume();
      return new VisibilityTokens(firstAt, secondAt, Consume());
    }

    int atIndex = _pos;
    Token atToken = Consume();
    Token identToken = Consume();
    ReportErrorAt(
        atIndex,
        DiagnosticCodes.DeprecatedVisibilitySyntax,
        SourceSpan.FromBounds(atToken.Span.Start, identToken.Span.End),
        "'@pub_testing' is not valid; use '@@Testable' instead.",
        help: "replace '@pub_testing' with '@@Testable'");
    return new VisibilityTokens(atToken, null, identToken);
  }

  private VisibilitySyntax BuildVisibilitySyntax(VisibilityTokens tokens) =>
      new(NextId(), tokens.Span, tokens.AtToken, tokens.KeywordOrIdent, tokens.SecondAtToken);

  private GenericDecoratorSyntax ParseGenericDecorator() {
    Token firstAt = Consume();
    Token secondAt = Consume();
    Token genericKeyword = Consume();
    return ParseGenericParameterList(firstAt, secondAt, genericKeyword);
  }

  private GenericDecoratorSyntax ParseDeprecatedGenericDecorator() {
    int atIndex = _pos;
    Token firstAt = Consume();
    Token secondAt = MissingTokenAt(TokenKind.At, firstAt.Span.End);
    Token genericKeyword = Consume();
    ReportErrorAt(
        atIndex,
        DiagnosticCodes.DeprecatedGenericSyntax,
        SourceSpan.FromBounds(firstAt.Span.Start, genericKeyword.Span.End),
        "'@generic[...]' is not valid; use '@@Generic[...]' instead.",
        help: "replace '@generic[...]' with '@@Generic[...]'");
    return ParseGenericParameterList(firstAt, secondAt, genericKeyword);
  }

  private GenericDecoratorSyntax ParseInlineGenericParameters(string kindName) {
    int bracketIndex = _pos;
    int start = Current.Span.Start;
    Token firstAt = MissingTokenAt(TokenKind.At, start);
    Token secondAt = MissingTokenAt(TokenKind.At, start);
    Token genericKeyword = MissingTokenAt(TokenKind.Identifier, start);

    GenericDecoratorSyntax decorator = ParseGenericParameterList(firstAt, secondAt, genericKeyword, reportEmpty: false);
    ReportErrorAt(
        bracketIndex,
        DiagnosticCodes.DeprecatedGenericSyntax,
        decorator.Span,
        $"Inline generic parameters on '{kindName}' are not valid; use '@@Generic[...]' before the declaration.",
        help: $"move type parameters to '@@Generic[...]' before '{kindName}'");
    return decorator;
  }

  private GenericDecoratorSyntax ParseGenericParameterList(
      Token firstAt,
      Token secondAt,
      Token genericKeyword,
      bool reportEmpty = true) {
    Token openBracket = Expect(TokenKind.OpenBracket);
    var parameters = ImmutableArray.CreateBuilder<GenericParameterSyntax>();

    if ((openBracket.Flags & TokenFlags.Missing) == 0) {
      while (Current.Kind is not (TokenKind.CloseBracket or TokenKind.EOF)) {
        if (MatchesColumn1Trigger()) {
          break;
        }

        if (IsStatementSyncToken(Current.Kind) && Current.Kind is not (TokenKind.Identifier or TokenKind.IsKeyword)) {
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
              "Unexpected ',' before generic parameter.");
          Token missingName = MissingTokenAt(TokenKind.Identifier, leadingComma.Span.Start);
          parameters.Add(new GenericParameterSyntax(
              NextId(),
              leadingComma.Span,
              missingName,
              null,
              null,
              leadingComma));
          continue;
        }

        if (Current.Kind != TokenKind.Identifier) {
          ReportError(
              DiagnosticCodes.ExpectedIdentifier,
              Current.Span,
              $"Expected generic parameter name, found {DescribeToken(Current)}.");
          break;
        }

        Token nameToken = Consume();
        Token? isKeyword = null;
        TypeSyntax? constraintType = null;
        if (Current.Kind == TokenKind.IsKeyword) {
          isKeyword = Consume();
          constraintType = ParseType();
        }

        Token? commaToken = null;
        if (Current.Kind == TokenKind.Comma) {
          int commaIndex = _pos;
          Token comma = Consume();
          commaToken = comma;
          if (Current.Kind == TokenKind.CloseBracket) {
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
                "Expected generic parameter before consecutive ','.");
          }
        }

        int paramEnd = commaToken is { } c
            ? c.Span.End
            : constraintType is not null
                ? constraintType.Span.End
                : isKeyword is { } ik
                    ? ik.Span.End
                    : nameToken.Span.End;
        parameters.Add(new GenericParameterSyntax(
            NextId(),
            SourceSpan.FromBounds(nameToken.Span.Start, paramEnd),
            nameToken,
            isKeyword,
            constraintType,
            commaToken));

        if (commaToken is { } && Current.Kind == TokenKind.CloseBracket) {
          break;
        }

        if (_pos == startPos) {
          break;
        }
      }

      if (reportEmpty && parameters.Count == 0) {
        ReportError(
            DiagnosticCodes.EmptyDeclarationBody,
            SourceSpan.FromBounds(openBracket.Span.Start, Current.Span.End),
            "Generic parameter list cannot be empty.");
      }
    }

    Token closeBracket = ExpectCloseWithRelated(TokenKind.CloseBracket, openBracket);
    int startOffset = (firstAt.Flags & TokenFlags.Missing) == 0
        ? firstAt.Span.Start
        : openBracket.Span.Start;
    return new GenericDecoratorSyntax(
        NextId(),
        SourceSpan.FromBounds(startOffset, closeBracket.Span.End),
        firstAt,
        secondAt,
        genericKeyword,
        openBracket,
        parameters.ToImmutable(),
        closeBracket);
  }

  private ErrorStatementSyntax SkipUnknownDoubleAtDecorator() {
    var skipped = ImmutableArray.CreateBuilder<Token>();
    int startIndex = _pos;
    skipped.Add(Consume()); // @
    skipped.Add(Consume()); // @
    if (Current.Kind == TokenKind.Identifier) {
      skipped.Add(Consume());
    }

    ReportErrorAt(
        startIndex,
        DiagnosticCodes.ExpectedStatement,
        SourceSpan.FromBounds(skipped[0].Span.Start, skipped[^1].Span.End),
        $"Unknown decorator '{string.Concat(skipped.Select(GetTokenText))}'.");

    if (Current.Kind == TokenKind.OpenBracket && !HasLeadingNewline(Current)) {
      int bracketDepth = 0;
      while (!IsAtEnd) {
        Token tok = Consume();
        skipped.Add(tok);
        if (tok.Kind == TokenKind.OpenBracket) {
          bracketDepth++;
        } else if (tok.Kind == TokenKind.CloseBracket) {
          bracketDepth--;
          if (bracketDepth <= 0) {
            break;
          }
        }
      }
    }

    return MakeErrorStatementFromTokens(skipped.ToImmutable());
  }

  private FunctionDeclarationSyntax ParseFunctionDeclaration(
      GenericDecoratorSyntax? genericDecorator,
      VisibilitySyntax? visibility) {
    Token fnKeyword = Consume();
    Token nameToken = ExpectIdentifier();
    (Token? openParen, ImmutableArray<ParameterSyntax> parameters, Token? closeParen) =
        ParseOptionalParameterList();

    TypeSyntax? returnType = CanStartType(Current, disallowNewlineBeforeBracket: true)
        ? ParseType()
        : null;

    (Token? arrowToken, Token? secondaryArrowToken, BlockStatementSyntax body) =
        ParseFunctionOrMethodBody();

    int start = genericDecorator?.Span.Start ?? visibility?.Span.Start ?? fnKeyword.Span.Start;
    return new FunctionDeclarationSyntax(
        NextId(),
        SourceSpan.FromBounds(start, body.Span.End),
        visibility,
        fnKeyword,
        nameToken,
        openParen,
        parameters,
        closeParen,
        returnType,
        body,
        genericDecorator,
        arrowToken,
        secondaryArrowToken);
  }

  private (Token? ArrowToken, Token? SecondaryArrowToken, BlockStatementSyntax Body) ParseFunctionOrMethodBody() {
    if (Current.Kind == TokenKind.OpenBrace) {
      return (null, null, ParseBlock());
    }

    int arrowIndex = _pos;
    if (TryConsumeAnyArrowTokens(out Token arrow, out Token? secondary)) {
      ReportErrorAt(
          arrowIndex,
          DiagnosticCodes.ExpectedBlockBody,
          SourceSpan.FromBounds(arrow.Span.Start, (secondary ?? arrow).Span.End),
          "Named functions and methods must have a '{ ... }' block body, not '-> expr'.",
          help: "wrap the body in '{ return ... }'");

      if (Current.Kind == TokenKind.OpenBrace) {
        return (arrow, secondary, ParseBlock());
      }

      ExpressionSyntax expr = ParseExpression(0, ExprContext.AllowBraceLiteral);
      var exprStmt = new ExpressionStatementSyntax(NextId(), expr.Span, expr);
      var body = new BlockStatementSyntax(
          NextId(),
          expr.Span,
          MissingTokenAt(TokenKind.OpenBrace, expr.Span.Start),
          [exprStmt],
          MissingTokenAt(TokenKind.CloseBrace, expr.Span.End));
      return (arrow, secondary, body);
    }

    return (null, null, ParseBlock());
  }

  private (Token? OpenParen, ImmutableArray<ParameterSyntax> Parameters, Token? CloseParen) ParseOptionalParameterList() {
    if (Current.Kind != TokenKind.OpenParen) {
      return (null, [], null);
    }

    Token openParen = Consume();
    var parameters = ImmutableArray.CreateBuilder<ParameterSyntax>();

    while (Current.Kind is not (TokenKind.CloseParen or TokenKind.EOF)) {
      if (MatchesColumn1Trigger()) {
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
            "Unexpected ',' before parameter.");
        Token missingName = MissingTokenAt(TokenKind.Identifier, leadingComma.Span.Start);
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

      Token? mutKeyword = Current.Kind == TokenKind.MutKeyword ? Consume() : null;
      if (Current.Kind != TokenKind.Identifier) {
        ReportError(
            DiagnosticCodes.ExpectedIdentifier,
            Current.Span,
            $"Expected parameter name, found {DescribeToken(Current)}.");
        if (mutKeyword is { } mk) {
          Token missingName = MissingTokenAt(TokenKind.Identifier, mk.Span.End);
          parameters.Add(new ParameterSyntax(
              NextId(),
              mk.Span,
              missingName,
              null,
              null,
              null,
              null,
              mk));
          continue;
        }

        break;
      }

      Token paramName = Consume();
      TypeSyntax? paramType = null;
      if (Current.Kind is not (TokenKind.Assign or TokenKind.Comma or TokenKind.CloseParen or TokenKind.EOF)
          && CanStartType(Current)) {
        paramType = ParseType();
      }

      Token? equalsToken = null;
      ExpressionSyntax? defaultValue = null;
      if (Current.Kind == TokenKind.Assign) {
        equalsToken = Consume();
        defaultValue = ParseExpression(0, ExprContext.AllowBraceLiteral | ExprContext.InCommaOptionalList);
      } else if (paramType is null) {
        // open decision: typeless default parameters (`x = 1`) leave ParameterSyntax.Type null; non-default parameters still require a type.
        ReportError(
            DiagnosticCodes.ExpectedType,
            Current.Span,
            $"Expected type for parameter '{GetTokenText(paramName)}', found {DescribeToken(Current)}.");
        paramType = new MissingTypeSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0));
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

      int paramStart = (mutKeyword ?? paramName).Span.Start;
      int paramEnd = commaToken is { } c
          ? c.Span.End
          : defaultValue is not null
              ? defaultValue.Span.End
              : equalsToken is { } eq
                  ? eq.Span.End
                  : paramType is not null
                      ? paramType.Span.End
                      : paramName.Span.End;
      parameters.Add(new ParameterSyntax(
          NextId(),
          SourceSpan.FromBounds(paramStart, paramEnd),
          paramName,
          paramType,
          equalsToken,
          defaultValue,
          commaToken,
          mutKeyword));

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

  private StructDeclarationSyntax ParseStructDeclaration(
      GenericDecoratorSyntax? genericDecorator,
      VisibilitySyntax? visibility) {
    Token structKeyword = Consume();
    Token nameToken = ExpectIdentifier();

    if (genericDecorator is null
        && (nameToken.Flags & TokenFlags.Missing) == 0
        && Current.Kind == TokenKind.OpenBracket
        && !HasLeadingNewline(Current)) {
      genericDecorator = ParseInlineGenericParameters("struct");
    }

    Token openBrace = Expect(TokenKind.OpenBrace);
    var members = ImmutableArray.CreateBuilder<SyntaxNode>();

    if ((openBrace.Flags & TokenFlags.Missing) == 0) {
      bool pushed = PushOpenBrace(openBrace);
      try {
        while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EOF)) {
          if (CheckColumn1Trigger()) {
            break;
          }

          int startPos = _pos;
          ParseStructMember(members);

          if (_pos == startPos && !IsAtEnd) {
            Token skipped = Consume();
            members.Add(new ErrorMemberSyntax(NextId(), skipped.Span, [skipped]));
          }
        }
      } finally {
        PopOpenBrace(pushed);
      }
    }

    Token closeBrace = ExpectCloseWithRelated(TokenKind.CloseBrace, openBrace);
    int start = genericDecorator is not null && genericDecorator.Span.Start < structKeyword.Span.Start
        ? genericDecorator.Span.Start
        : visibility?.Span.Start ?? structKeyword.Span.Start;
    return new StructDeclarationSyntax(
        NextId(),
        SourceSpan.FromBounds(start, closeBrace.Span.End),
        genericDecorator,
        visibility,
        structKeyword,
        nameToken,
        openBrace,
        members.ToImmutable(),
        closeBrace);
  }

  private void ParseStructMember(ImmutableArray<SyntaxNode>.Builder members) {
    if (Current.Kind == TokenKind.Error) {
      members.Add(ConsumeErrorTokensAsMember());
      return;
    }

    int beforeMemberId = _nextId;
    GenericDecoratorSyntax? memberGeneric = null;
    Token? staticKeyword = null;
    Token? mutKeyword = null;
    int mutTokenIndex = -1;

    if (IsAdjacentDoubleAt() && !IsTestableStart()) {
      if (IsIdentifierText(Peek(2), "Generic")) {
        memberGeneric = ParseGenericDecorator();
      } else {
        ErrorStatementSyntax err = SkipUnknownDoubleAtDecorator();
        members.Add(new ErrorMemberSyntax(err.Id, err.Span, err.SkippedTokens));
        return;
      }
    } else if (Current.Kind == TokenKind.At && IsIdentifierText(Peek(1), "generic")) {
      memberGeneric = ParseDeprecatedGenericDecorator();
    }

    // D-163 / D-179: `[pub | @@Testable] [static | mut]` on struct fields and methods (`pub mut` valid; `mut pub` / `static mut` / `mut static` -> PN2022).
    VisibilityTokens? pendingVisibility = null;
    while (IsVisibilityStart() || Current.Kind is TokenKind.StaticKeyword or TokenKind.MutKeyword) {
      if (IsVisibilityStart()) {
        if (staticKeyword is not null) {
          break;
        }

        VisibilityTokens visibilityTokens = ParseVisibilityTokens();
        if (mutKeyword is { } mk && mutTokenIndex >= 0) {
          ReportInvalidMutModifierPlacement(mutTokenIndex, mk.Span);
        }

        if (pendingVisibility is null) {
          pendingVisibility = visibilityTokens;
        } else {
          ReportErrorAt(
              _pos - 1,
              DiagnosticCodes.ExpectedStatement,
              visibilityTokens.Span,
              "'pub' and '@@Testable' cannot be combined on the same member.");
          var errTokens = ImmutableArray.CreateBuilder<Token>();
          if (memberGeneric is not null) {
            AppendRealTokens(errTokens, memberGeneric);
            memberGeneric = null;
          }

          if (mutKeyword is { } mkBefore && mkBefore.Span.Start < pendingVisibility.Value.Span.Start) {
            errTokens.Add(mkBefore);
            mutKeyword = null;
            mutTokenIndex = -1;
          }

          pendingVisibility.Value.AppendTo(errTokens);
          if (mutKeyword is { } mkAfter) {
            errTokens.Add(mkAfter);
            mutKeyword = null;
            mutTokenIndex = -1;
          }

          _nextId = beforeMemberId;
          members.Add(MakeErrorMemberFromTokens(errTokens.ToImmutable()));
          beforeMemberId = _nextId;
          pendingVisibility = visibilityTokens;
        }
      } else if (Current.Kind == TokenKind.StaticKeyword) {
        if (staticKeyword is not null) {
          ReportError(
              DiagnosticCodes.ExpectedStatement,
              Current.Span,
              "Duplicate 'static' modifier on struct member.");
          FlushStructMemberPrefix(members, ref beforeMemberId, ref memberGeneric, ref pendingVisibility, ref staticKeyword, ref mutKeyword, ref mutTokenIndex);
        }

        staticKeyword = Consume();
        if (mutKeyword is { } mk && mutTokenIndex >= 0) {
          ReportInvalidMutModifierPlacement(mutTokenIndex, mk.Span);
        }
      } else {
        if (mutKeyword is not null) {
          ReportInvalidMutModifierPlacement(_pos, Current.Span);
          FlushStructMemberPrefix(members, ref beforeMemberId, ref memberGeneric, ref pendingVisibility, ref staticKeyword, ref mutKeyword, ref mutTokenIndex);
        }

        mutTokenIndex = _pos;
        mutKeyword = Consume();
        if (staticKeyword is not null) {
          ReportInvalidMutModifierPlacement(mutTokenIndex, mutKeyword.Value.Span);
        }
      }
    }

    VisibilitySyntax? memberVisibility = pendingVisibility is { } pv
        ? BuildVisibilitySyntax(pv)
        : null;

    if (Current.Kind == TokenKind.FnKeyword) {
      Token fnKeyword = Consume();
      Token methodName = ExpectIdentifier();
      (Token? openParen, ImmutableArray<ParameterSyntax> parameters, Token? closeParen) =
          ParseOptionalParameterList();
      TypeSyntax? returnType = CanStartType(Current, disallowNewlineBeforeBracket: true)
          ? ParseType()
          : null;
      (Token? arrowToken, Token? secondaryArrowToken, BlockStatementSyntax body) =
          ParseFunctionOrMethodBody();

      int start = MinModifierStart(memberGeneric, memberVisibility, staticKeyword, mutKeyword, fnKeyword.Span.Start);
      members.Add(new MethodDeclarationSyntax(
          NextId(),
          SourceSpan.FromBounds(start, body.Span.End),
          memberGeneric,
          memberVisibility,
          staticKeyword,
          fnKeyword,
          methodName,
          openParen,
          parameters,
          closeParen,
          returnType,
          body,
          arrowToken,
          secondaryArrowToken,
          mutKeyword));
      return;
    }

    if (memberGeneric is not null || staticKeyword is not null) {
      ReportError(
          DiagnosticCodes.ExpectedStatement,
          Current.Span,
          staticKeyword is not null
              ? "Expected 'fn' after 'static' in struct member."
              : "'@@Generic' can only decorate methods in a struct body.");
      var prefixTokens = ImmutableArray.CreateBuilder<Token>();
      if (memberGeneric is not null) {
        AppendRealTokens(prefixTokens, memberGeneric);
      }

      AppendStructMemberModifiersByStart(prefixTokens, memberVisibility, staticKeyword, mutKeyword);
      if (prefixTokens.Count > 0) {
        _nextId = beforeMemberId;
        members.Add(MakeErrorMemberFromTokens(prefixTokens.ToImmutable()));
      }
      return;
    }

    if (Current.Kind == TokenKind.Identifier) {
      Token memberName = Consume();
      bool hasTypeOnSameLine = !HasLeadingNewline(Current)
          && CanStartType(Current)
          && !(Current.Kind == TokenKind.FnKeyword && Peek(1).Kind == TokenKind.Identifier);

      if (hasTypeOnSameLine) {
        TypeSyntax fieldType = ParseType();
        int start = MinModifierStart(null, memberVisibility, null, mutKeyword, memberName.Span.Start);
        members.Add(new FieldDeclarationSyntax(
            NextId(),
            SourceSpan.FromBounds(start, fieldType.Span.End),
            memberVisibility,
            memberName,
            fieldType,
            mutKeyword));
        return;
      }

      if (memberVisibility is not null || mutKeyword is not null) {
        ReportError(
            DiagnosticCodes.ExpectedType,
            Current.Span,
            $"Expected field type for '{GetTokenText(memberName)}', found {DescribeToken(Current)}.");
        var missingType = new MissingTypeSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0));
        int start = MinModifierStart(null, memberVisibility, null, mutKeyword, memberName.Span.Start);
        members.Add(new FieldDeclarationSyntax(
            NextId(),
            SourceSpan.FromBounds(start, memberName.Span.End),
            memberVisibility,
            memberName,
            missingType,
            mutKeyword));
        return;
      }

      members.Add(new EmbeddedStructSyntax(NextId(), memberName.Span, memberName));
      return;
    }

    if (memberVisibility is not null || mutKeyword is not null) {
      ReportError(
          DiagnosticCodes.ExpectedStatement,
          Current.Span,
          mutKeyword is not null && memberVisibility is null
              ? $"Expected field or method after 'mut' modifier, found {DescribeToken(Current)}."
              : $"Expected field or method after visibility modifier, found {DescribeToken(Current)}.");
      var visTokens = ImmutableArray.CreateBuilder<Token>();
      AppendStructMemberModifiersByStart(visTokens, memberVisibility, null, mutKeyword);
      _nextId = beforeMemberId;
      members.Add(MakeErrorMemberFromTokens(visTokens.ToImmutable()));
      return;
    }

    ReportError(
        DiagnosticCodes.ExpectedStatement,
        Current.Span,
        $"Expected field, embedded struct, or method in struct body, found {DescribeToken(Current)}.");
    members.Add(SkipToNextMemberStart());
  }

  private void ReportInvalidMutModifierPlacement(int mutTokenIndex, SourceSpan mutSpan) =>
      ReportErrorAt(
          mutTokenIndex,
          DiagnosticCodes.InvalidMutModifierPlacement,
          mutSpan,
          "'mut' must appear after 'pub' ('pub mut'), and cannot be combined with 'static'.",
          help: "write 'pub mut <field>' for a public mutable field, or remove 'static'/'mut'");

  private static int MinModifierStart(
      GenericDecoratorSyntax? genericDecorator,
      VisibilitySyntax? visibility,
      Token? staticKeyword,
      Token? mutKeyword,
      int fallbackStart) {
    int start = fallbackStart;
    if (genericDecorator is not null && genericDecorator.Span.Start < start) {
      start = genericDecorator.Span.Start;
    }
    if (visibility is not null && visibility.Span.Start < start) {
      start = visibility.Span.Start;
    }
    if (staticKeyword is { } st && st.Span.Start < start) {
      start = st.Span.Start;
    }
    if (mutKeyword is { } mk && mk.Span.Start < start) {
      start = mk.Span.Start;
    }
    return start;
  }

  private static void AppendStructMemberModifiersByStart(
      ImmutableArray<Token>.Builder builder,
      VisibilitySyntax? visibility,
      Token? staticKeyword,
      Token? mutKeyword) {
    foreach (SyntaxElement element in ModifierOrdering.YieldByStart(visibility, staticKeyword, mutKeyword)) {
      if (element.IsNode) {
        AppendRealTokens(builder, element.AsNode());
      } else if ((element.AsToken().Flags & TokenFlags.Missing) == 0) {
        builder.Add(element.AsToken());
      }
    }
  }

  private void FlushStructMemberPrefix(
      ImmutableArray<SyntaxNode>.Builder members,
      ref int beforeMemberId,
      ref GenericDecoratorSyntax? memberGeneric,
      ref VisibilityTokens? pendingVisibility,
      ref Token? staticKeyword,
      ref Token? mutKeyword,
      ref int mutTokenIndex) {
    var errTokens = ImmutableArray.CreateBuilder<Token>();
    if (memberGeneric is not null) {
      AppendRealTokens(errTokens, memberGeneric);
      memberGeneric = null;
    }

    var modifiers = new List<(int Start, Token Token)>();
    if (pendingVisibility is { } pv) {
      if (pv.AtToken is { } at) {
        modifiers.Add((at.Span.Start, at));
      }
      if (pv.SecondAtToken is { } sat) {
        modifiers.Add((sat.Span.Start, sat));
      }
      modifiers.Add((pv.KeywordOrIdent.Span.Start, pv.KeywordOrIdent));
      pendingVisibility = null;
    }
    if (staticKeyword is { } st) {
      modifiers.Add((st.Span.Start, st));
      staticKeyword = null;
    }
    if (mutKeyword is { } mk) {
      modifiers.Add((mk.Span.Start, mk));
      mutKeyword = null;
      mutTokenIndex = -1;
    }

    modifiers.Sort(static (a, b) => a.Start.CompareTo(b.Start));
    foreach ((_, Token tok) in modifiers) {
      errTokens.Add(tok);
    }

    if (errTokens.Count > 0) {
      _nextId = beforeMemberId;
      members.Add(MakeErrorMemberFromTokens(errTokens.ToImmutable()));
      beforeMemberId = _nextId;
    }
  }

  private InterfaceDeclarationSyntax ParseInterfaceDeclaration(
      GenericDecoratorSyntax? genericDecorator,
      VisibilitySyntax? visibility) {
    Token interfaceKeyword = Consume();
    Token nameToken = ExpectIdentifier();
    Token openBrace = Expect(TokenKind.OpenBrace);
    var members = ImmutableArray.CreateBuilder<SyntaxNode>();

    if ((openBrace.Flags & TokenFlags.Missing) == 0) {
      bool pushed = PushOpenBrace(openBrace);
      try {
        while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EOF)) {
          if (CheckColumn1Trigger()) {
            break;
          }

          int startPos = _pos;
          if (Current.Kind == TokenKind.Error) {
            members.Add(ConsumeErrorTokensAsMember());
            continue;
          }

          Token? mutKeyword = Current.Kind == TokenKind.MutKeyword ? Consume() : null;
          if (Current.Kind == TokenKind.FnKeyword) {
            Token fnKeyword = Consume();
            Token methodName = ExpectIdentifier();
            (Token? openParen, ImmutableArray<ParameterSyntax> parameters, Token? closeParen) =
                ParseOptionalParameterList();
            TypeSyntax? returnType = !HasLeadingNewline(Current)
                && CanStartType(Current, disallowNewlineBeforeBracket: true)
                ? ParseType()
                : null;
            int startOffset = mutKeyword?.Span.Start ?? fnKeyword.Span.Start;
            int endOffset = returnType?.Span.End
                ?? closeParen?.Span.End
                ?? methodName.Span.End;
            members.Add(new InterfaceMethodSyntax(
                NextId(),
                SourceSpan.FromBounds(startOffset, endOffset),
                fnKeyword,
                methodName,
                openParen,
                parameters,
                closeParen,
                returnType,
                mutKeyword));
          } else if (Current.Kind == TokenKind.Identifier) {
            Token propName = Consume();
            TypeSyntax propType = ParseType();
            int startOffset = mutKeyword?.Span.Start ?? propName.Span.Start;
            members.Add(new InterfacePropertySyntax(
                NextId(),
                SourceSpan.FromBounds(startOffset, propType.Span.End),
                propName,
                propType,
                mutKeyword));
          } else if (mutKeyword is { } mk) {
            ReportError(
                DiagnosticCodes.ExpectedStatement,
                Current.Span,
                $"Expected property or method signature after 'mut' in interface body, found {DescribeToken(Current)}.");
            members.Add(new ErrorMemberSyntax(NextId(), mk.Span, [mk]));
          } else {
            ReportError(
                DiagnosticCodes.ExpectedStatement,
                Current.Span,
                $"Expected property or method signature in interface body, found {DescribeToken(Current)}.");
            members.Add(SkipToNextMemberStart());
          }

          if (_pos == startPos && !IsAtEnd) {
            Token skipped = Consume();
            members.Add(new ErrorMemberSyntax(NextId(), skipped.Span, [skipped]));
          }
        }
      } finally {
        PopOpenBrace(pushed);
      }
    }

    Token closeBrace = ExpectCloseWithRelated(TokenKind.CloseBrace, openBrace);
    int start = genericDecorator?.Span.Start ?? visibility?.Span.Start ?? interfaceKeyword.Span.Start;
    return new InterfaceDeclarationSyntax(
        NextId(),
        SourceSpan.FromBounds(start, closeBrace.Span.End),
        genericDecorator,
        visibility,
        interfaceKeyword,
        nameToken,
        openBrace,
        members.ToImmutable(),
        closeBrace);
  }

  private EnumDeclarationSyntax ParseEnumDeclaration(VisibilitySyntax? visibility) {
    Token enumKeyword = Consume();
    Token nameToken = ExpectIdentifier();
    Token openBrace = Expect(TokenKind.OpenBrace);
    var members = ImmutableArray.CreateBuilder<SyntaxNode>();
    bool hasMember = false;

    if ((openBrace.Flags & TokenFlags.Missing) == 0) {
      bool pushed = PushOpenBrace(openBrace);
      try {
        while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EOF)) {
          if (CheckColumn1Trigger()) {
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
                "Unexpected ',' before enum member.");
            members.Add(new ErrorMemberSyntax(NextId(), leadingComma.Span, [leadingComma]));
            continue;
          }

          if (Current.Kind == TokenKind.Error) {
            members.Add(ConsumeErrorTokensAsMember());
            continue;
          }

          if (Current.Kind != TokenKind.Identifier) {
            ReportError(
                DiagnosticCodes.ExpectedIdentifier,
                Current.Span,
                $"Expected enum member name, found {DescribeToken(Current)}.");
            members.Add(SkipToNextMemberStart());
            continue;
          }

          Token memberName = Consume();
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
                  "Expected enum member before consecutive ','.");
            }
          }

          int memberEnd = commaToken is { } c ? c.Span.End : memberName.Span.End;
          members.Add(new EnumMemberSyntax(
              NextId(),
              SourceSpan.FromBounds(memberName.Span.Start, memberEnd),
              memberName,
              commaToken));
          hasMember = true;

          if (commaToken is { } && Current.Kind == TokenKind.CloseBrace) {
            break;
          }

          if (_pos == startPos && !IsAtEnd) {
            Token skipped = Consume();
            members.Add(new ErrorMemberSyntax(NextId(), skipped.Span, [skipped]));
          }
        }

        if (!hasMember && !MatchesColumn1Trigger()) {
          ReportError(
              DiagnosticCodes.EmptyDeclarationBody,
              SourceSpan.FromBounds(openBrace.Span.Start, Current.Span.End),
              "Enum declaration cannot be empty.");
        }
      } finally {
        PopOpenBrace(pushed);
      }
    }

    Token closeBrace = ExpectCloseWithRelated(TokenKind.CloseBrace, openBrace);
    int start = visibility?.Span.Start ?? enumKeyword.Span.Start;
    return new EnumDeclarationSyntax(
        NextId(),
        SourceSpan.FromBounds(start, closeBrace.Span.End),
        visibility,
        enumKeyword,
        nameToken,
        openBrace,
        members.ToImmutable(),
        closeBrace);
  }

  private UnionDeclarationSyntax ParseUnionDeclaration(
      GenericDecoratorSyntax? genericDecorator,
      VisibilitySyntax? visibility) {
    Token unionKeyword = Consume();
    Token nameToken = ExpectIdentifier();

    if (genericDecorator is null
        && (nameToken.Flags & TokenFlags.Missing) == 0
        && Current.Kind == TokenKind.OpenBracket
        && !HasLeadingNewline(Current)) {
      genericDecorator = ParseInlineGenericParameters("union");
    }

    Token openBrace = Expect(TokenKind.OpenBrace);
    var variants = ImmutableArray.CreateBuilder<SyntaxNode>();
    bool hasVariant = false;
    bool? unionUsesNamedPayloads = null;

    if ((openBrace.Flags & TokenFlags.Missing) == 0) {
      bool pushed = PushOpenBrace(openBrace);
      try {
        while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EOF)) {
          if (CheckColumn1Trigger()) {
            break;
          }

          int startPos = _pos;
          if (Current.Kind == TokenKind.Error) {
            variants.Add(ConsumeErrorTokensAsMember());
            continue;
          }

          if (Current.Kind != TokenKind.Identifier) {
            ReportError(
                DiagnosticCodes.ExpectedIdentifier,
                Current.Span,
                $"Expected union variant name, found {DescribeToken(Current)}.");
            variants.Add(SkipToNextMemberStart());
            continue;
          }

          UnionVariantSyntax variant = ParseUnionVariant(ref unionUsesNamedPayloads);
          variants.Add(variant);
          hasVariant = true;

          if (_pos == startPos && !IsAtEnd) {
            Token skipped = Consume();
            variants.Add(new ErrorMemberSyntax(NextId(), skipped.Span, [skipped]));
          }
        }

        if (!hasVariant && !MatchesColumn1Trigger()) {
          ReportError(
              DiagnosticCodes.EmptyDeclarationBody,
              SourceSpan.FromBounds(openBrace.Span.Start, Current.Span.End),
              "Union declaration cannot be empty.");
        }
      } finally {
        PopOpenBrace(pushed);
      }
    }

    Token closeBrace = ExpectCloseWithRelated(TokenKind.CloseBrace, openBrace);
    int start = genericDecorator is not null && genericDecorator.Span.Start < unionKeyword.Span.Start
        ? genericDecorator.Span.Start
        : visibility?.Span.Start ?? unionKeyword.Span.Start;
    return new UnionDeclarationSyntax(
        NextId(),
        SourceSpan.FromBounds(start, closeBrace.Span.End),
        genericDecorator,
        visibility,
        unionKeyword,
        nameToken,
        openBrace,
        variants.ToImmutable(),
        closeBrace);
  }

  private UnionVariantSyntax ParseUnionVariant(ref bool? unionUsesNamedPayloads) {
    Token variantName = Consume();
    Token? openParen = null;
    Token? closeParen = null;
    var fields = ImmutableArray.CreateBuilder<UnionVariantFieldSyntax>();
    bool? variantIsNamed = null;

    if (Current.Kind == TokenKind.OpenParen && !HasLeadingNewline(Current)) {
      openParen = Consume();
      while (Current.Kind is not (TokenKind.CloseParen or TokenKind.EOF)) {
        if (MatchesColumn1Trigger()) {
          break;
        }

        if (IsStatementSyncToken(Current.Kind) && Current.Kind is not (TokenKind.Identifier or TokenKind.FnKeyword)) {
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
              "Unexpected ',' before union payload field.");
          var missingSpan = new SourceSpan(leadingComma.Span.Start, 0);
          fields.Add(new UnionVariantFieldSyntax(
              NextId(),
              leadingComma.Span,
              null,
              new MissingTypeSyntax(NextId(), missingSpan),
              leadingComma));
          continue;
        }

        if (!CanStartType(Current)) {
          ReportError(
              DiagnosticCodes.ExpectedType,
              Current.Span,
              $"Expected union payload type or field, found {DescribeToken(Current)}.");
          break;
        }

        // D-146: `Variant(a B)` is one named field; positional payloads require commas (`Pair(int, string)`).
        bool fieldIsNamed = Current.Kind == TokenKind.Identifier && CanStartType(Peek(1));
        if (variantIsNamed is null) {
          variantIsNamed = fieldIsNamed;
        } else if (variantIsNamed.Value != fieldIsNamed) {
          ReportError(
              DiagnosticCodes.MixedUnionPayloadForms,
              Current.Span,
              "Cannot mix named and positional payload fields in a union variant.");
        }

        Token? fieldName = null;
        if (fieldIsNamed) {
          fieldName = Consume();
        }

        TypeSyntax fieldType = ParseType();

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
                "Expected payload before consecutive ','.");
          }
        } else if (!fieldIsNamed
            && Current.Kind is not (TokenKind.CloseParen or TokenKind.EOF)
            && CanStartType(Current)) {
          ReportError(
              DiagnosticCodes.ExpectedToken,
              Current.Span,
              $"Expected ',' between positional union payload types, found {DescribeToken(Current)}.");
        }

        int fieldStart = fieldName is { } fn ? fn.Span.Start : fieldType.Span.Start;
        int fieldEnd = commaToken is { } c ? c.Span.End : fieldType.Span.End;
        fields.Add(new UnionVariantFieldSyntax(
            NextId(),
            SourceSpan.FromBounds(fieldStart, fieldEnd),
            fieldName,
            fieldType,
            commaToken));

        if (commaToken is { } && Current.Kind == TokenKind.CloseParen) {
          break;
        }

        if (_pos == startPos) {
          break;
        }
      }

      closeParen = ExpectCloseWithRelated(TokenKind.CloseParen, openParen.Value);
    }

    int variantEnd = closeParen is { } cp ? cp.Span.End : variantName.Span.End;
    var variant = new UnionVariantSyntax(
        NextId(),
        SourceSpan.FromBounds(variantName.Span.Start, variantEnd),
        variantName,
        openParen,
        fields.ToImmutable(),
        closeParen);

    if (variantIsNamed is { } isNamed) {
      if (unionUsesNamedPayloads is null) {
        unionUsesNamedPayloads = isNamed;
      } else if (unionUsesNamedPayloads.Value != isNamed) {
        // D-103: mixing positional and named variants in one union is PN2015.
        ReportErrorAt(
            _pos - 1,
            DiagnosticCodes.MixedUnionPayloadForms,
            variant.Span,
            "Cannot mix named and positional variants in the same union declaration.");
      }
    }

    return variant;
  }

  private ModuleDeclarationSyntax ParseModuleDeclaration() {
    Token moduleKeyword = Consume();
    Token nameToken = ExpectIdentifier();
    return new ModuleDeclarationSyntax(
        NextId(),
        SourceSpan.FromBounds(moduleKeyword.Span.Start, nameToken.Span.End),
        moduleKeyword,
        nameToken);
  }

  private TestDeclarationSyntax ParseTestDeclaration() {
    Token testKeyword = Consume();
    ExpressionSyntax name = Current.Kind == TokenKind.StringStart
        ? ParseInterpolatedString()
        : ExpectTestNameString();
    BlockStatementSyntax body = ParseBlock();
    return new TestDeclarationSyntax(
        NextId(),
        SourceSpan.FromBounds(testKeyword.Span.Start, body.Span.End),
        testKeyword,
        name,
        body);
  }

  private MissingExpressionSyntax ExpectTestNameString() {
    ReportError(
        DiagnosticCodes.ExpectedExpression,
        Current.Span,
        $"Expected string literal for test name, found {DescribeToken(Current)}.");
    return new MissingExpressionSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0));
  }

  private ErrorMemberSyntax ConsumeErrorTokensAsMember() {
    var skipped = ImmutableArray.CreateBuilder<Token>();
    while (Current.Kind == TokenKind.Error) {
      skipped.Add(Consume());
    }

    return MakeErrorMemberFromTokens(skipped.ToImmutable());
  }

  private ErrorMemberSyntax SkipToNextMemberStart() {
    var skipped = ImmutableArray.CreateBuilder<Token>();
    skipped.Add(Consume());

    while (Current.Kind is not (TokenKind.CloseBrace or TokenKind.EOF)) {
      if (MatchesColumn1Trigger()) {
        break;
      }

      if (Current.Kind is TokenKind.FnKeyword or TokenKind.StaticKeyword or TokenKind.MutKeyword or TokenKind.PubKeyword
          || IsAdjacentDoubleAt()
          || IsDeprecatedPubTestingStart()
          || (Current.Kind == TokenKind.At && IsIdentifierText(Peek(1), "generic"))) {
        break;
      }

      if (HasLeadingNewline(Current) && Current.Kind == TokenKind.Identifier) {
        break;
      }

      skipped.Add(Consume());
    }

    return MakeErrorMemberFromTokens(skipped.ToImmutable());
  }

  private ErrorMemberSyntax MakeErrorMemberFromTokens(ImmutableArray<Token> tokens) =>
      new(
          NextId(),
          SourceSpan.FromBounds(tokens[0].Span.Start, tokens[^1].Span.End),
          tokens);
}
