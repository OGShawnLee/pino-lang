using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Parsing;

public sealed partial class Parser {
  private TypeSyntax ParseType() {
    if (_depth >= MaxDepth) {
      ReportDepthExceeded();
      return new MissingTypeSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0));
    }

    _depth++;
    try {
      switch (Current.Kind) {
        case TokenKind.OpenBracket:
          return ParseVectorType();

        case TokenKind.FnKeyword:
          return ParseFunctionType();

        case TokenKind.At when Peek(1).Kind == TokenKind.OpenParen:
          return ParseTupleType();

        case TokenKind.Identifier: {
            if (IsIdentifierText(Current, "map") && Peek(1).Kind == TokenKind.OpenBracket) {
              return ParseMapType();
            }

            if (IsIdentifierText(Current, "set") && Peek(1).Kind == TokenKind.OpenBracket) {
              return ParseSetType();
            }

            Token firstIdent = Consume();
            TypeSyntax baseType;
            if (Current.Kind == TokenKind.ColonColon) {
              Token colonColon = Consume();
              Token member = ExpectIdentifier();
              baseType = new QualifiedTypeSyntax(
                  NextId(),
                  SourceSpan.FromBounds(firstIdent.Span.Start, member.Span.End),
                  firstIdent,
                  colonColon,
                  member);
            } else {
              // D-162: `void` written as a type is an ordinary NamedTypeSyntax; the checker restricts where it may appear.
              baseType = new NamedTypeSyntax(NextId(), firstIdent.Span, firstIdent);
            }

            if (Current.Kind == TokenKind.OpenBracket && !HasLeadingNewline(Current)) {
              TypeArgumentListSyntax typeArgs = ParseTypeArgumentList();
              return new GenericTypeSyntax(
                  NextId(),
                  SourceSpan.FromBounds(baseType.Span.Start, typeArgs.Span.End),
                  baseType,
                  typeArgs);
            }

            return baseType;
          }

        default: {
            ReportError(
                DiagnosticCodes.ExpectedType,
                Current.Span,
                $"Expected type, found {DescribeToken(Current)}.");
            return new MissingTypeSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0));
          }
      }
    } finally {
      _depth--;
    }
  }

  private VectorTypeSyntax ParseVectorType() {
    Token openBracket = Consume();
    Token closeBracket = ExpectCloseWithRelated(TokenKind.CloseBracket, openBracket);
    TypeSyntax elementType = ParseType();
    return new VectorTypeSyntax(
        NextId(),
        SourceSpan.FromBounds(openBracket.Span.Start, elementType.Span.End),
        openBracket,
        closeBracket,
        elementType);
  }

  private MapTypeSyntax ParseMapType() {
    Token mapToken = Consume();
    Token openBracket = Expect(TokenKind.OpenBracket);
    TypeSyntax keyType = ParseType();
    Token comma = Expect(TokenKind.Comma);
    TypeSyntax valueType = ParseType();
    Token closeBracket = ExpectCloseWithRelated(TokenKind.CloseBracket, openBracket);
    return new MapTypeSyntax(
        NextId(),
        SourceSpan.FromBounds(mapToken.Span.Start, closeBracket.Span.End),
        mapToken,
        openBracket,
        keyType,
        comma,
        valueType,
        closeBracket);
  }

  private SetTypeSyntax ParseSetType() {
    Token setToken = Consume();
    Token openBracket = Expect(TokenKind.OpenBracket);
    TypeSyntax elementType = ParseType();
    Token closeBracket = ExpectCloseWithRelated(TokenKind.CloseBracket, openBracket);
    return new SetTypeSyntax(
        NextId(),
        SourceSpan.FromBounds(setToken.Span.Start, closeBracket.Span.End),
        setToken,
        openBracket,
        elementType,
        closeBracket);
  }

  private TypeArgumentListSyntax ParseTypeArgumentList() {
    Token openBracket = Consume();
    var args = ImmutableArray.CreateBuilder<TypeSyntax>();
    var commas = ImmutableArray.CreateBuilder<Token>();

    while (Current.Kind is not (TokenKind.CloseBracket or TokenKind.EOF)) {
      if (IsStatementSyncToken(Current.Kind) && !CanStartType(Current)) {
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
            "Unexpected ',' before type argument.");
        commas.Add(leadingComma);
        continue;
      }

      int beforeArgId = _nextId;
      TypeSyntax arg = ParseType();
      if (arg is MissingTypeSyntax && _pos == startPos) {
        _nextId = beforeArgId;
        break;
      }

      args.Add(arg);

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
              "Expected type argument before consecutive ','.");
        }
      }
    }

    if (args.Count == 0) {
      ReportError(
          DiagnosticCodes.ExpectedType,
          Current.Span,
          "Expected at least one type argument.");
    }

    Token closeBracket = ExpectCloseWithRelated(TokenKind.CloseBracket, openBracket);
    return new TypeArgumentListSyntax(
        NextId(),
        SourceSpan.FromBounds(openBracket.Span.Start, closeBracket.Span.End),
        openBracket,
        args.ToImmutable(),
        closeBracket,
        commas.ToImmutable());
  }

  private FunctionTypeSyntax ParseFunctionType() {
    Token fnKeyword = Consume();
    Token? openParen = null;
    Token? closeParen = null;
    var parameters = ImmutableArray.CreateBuilder<FunctionTypeParameterSyntax>();

    if (Current.Kind == TokenKind.OpenParen) {
      openParen = Consume();
      while (Current.Kind is not (TokenKind.CloseParen or TokenKind.EOF)) {
        if (IsStatementSyncToken(Current.Kind) && !CanStartType(Current)) {
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
              "Unexpected ',' before parameter type.");
          var missingType = new MissingTypeSyntax(NextId(), new SourceSpan(leadingComma.Span.Start, 0));
          parameters.Add(new FunctionTypeParameterSyntax(
              NextId(),
              leadingComma.Span,
              MutKeyword: null,
              missingType,
              leadingComma));
          continue;
        }

        Token? mutKeyword = Current.Kind == TokenKind.MutKeyword ? Consume() : null;
        int beforeParamId = _nextId;
        TypeSyntax paramType = ParseType();
        if (mutKeyword is null && paramType is MissingTypeSyntax && _pos == startPos) {
          _nextId = beforeParamId;
          break;
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
                "Expected type before consecutive ','.");
          }
        }

        int paramStart = mutKeyword?.Span.Start ?? paramType.Span.Start;
        int paramEnd = commaToken is { } c
            ? c.Span.End
            : Math.Max(paramType.Span.End, mutKeyword?.Span.End ?? 0);
        parameters.Add(new FunctionTypeParameterSyntax(
            NextId(),
            SourceSpan.FromBounds(paramStart, paramEnd),
            mutKeyword,
            paramType,
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

    Token? arrowToken = null;
    Token? secondaryArrow = null;
    TypeSyntax? returnType = null;

    if (TryConsumeArrow(out Token arrow, out Token? secondary)) {
      arrowToken = arrow;
      secondaryArrow = secondary;
      returnType = ParseType();
    }

    int endOffset = returnType is not null
        ? returnType.Span.End
        : (secondaryArrow ?? arrowToken) is { } a
            ? a.Span.End
            : closeParen is { } cp
                ? cp.Span.End
                : fnKeyword.Span.End;
    return new FunctionTypeSyntax(
        NextId(),
        SourceSpan.FromBounds(fnKeyword.Span.Start, endOffset),
        fnKeyword,
        openParen,
        parameters.ToImmutable(),
        closeParen,
        arrowToken,
        returnType,
        secondaryArrow);
  }

  private TupleTypeSyntax ParseTupleType() {
    Token atToken = Consume();
    Token openParen = Expect(TokenKind.OpenParen);
    var fields = ImmutableArray.CreateBuilder<TupleFieldTypeSyntax>();

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
            "Unexpected ',' before tuple field type.");
        Token missingName = new(TokenKind.Identifier, new SourceSpan(leadingComma.Span.Start, 0), TokenFlags.Missing);
        var missingType = new MissingTypeSyntax(NextId(), new SourceSpan(leadingComma.Span.Start, 0));
        fields.Add(new TupleFieldTypeSyntax(
            NextId(),
            leadingComma.Span,
            missingName,
            missingType,
            leadingComma));
        continue;
      }

      if (Current.Kind != TokenKind.Identifier) {
        ReportError(
            DiagnosticCodes.ExpectedIdentifier,
            Current.Span,
            $"Expected tuple field name, found {DescribeToken(Current)}.");
        break;
      }

      Token fieldName = Consume();
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
              "Expected tuple field before consecutive ','.");
        }
      }

      int endOffset = commaToken is { } c ? c.Span.End : fieldType.Span.End;
      fields.Add(new TupleFieldTypeSyntax(
          NextId(),
          SourceSpan.FromBounds(fieldName.Span.Start, endOffset),
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

    Token closeParen = ExpectCloseWithRelated(TokenKind.CloseParen, openParen);
    return new TupleTypeSyntax(
        NextId(),
        SourceSpan.FromBounds(atToken.Span.Start, closeParen.Span.End),
        atToken,
        openParen,
        fields.ToImmutable(),
        closeParen);
  }
}
