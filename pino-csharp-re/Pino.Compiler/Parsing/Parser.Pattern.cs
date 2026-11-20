using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Parsing;

public sealed partial class Parser {
  // D-169: label bindings (`@field`, `@field: name`) exist only in `match` (`when`) patterns. `is` patterns keep the
  // positional form, so ParsePattern(allowLabels: false) is used for them and for everything nested inside them.
  private PatternSyntax ParsePattern(bool allowLabels = true) {
    if (_depth >= MaxDepth) {
      ReportDepthExceeded();
      return new MissingPatternSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0));
    }

    _depth++;
    try {
      switch (Current.Kind) {
        case TokenKind.Identifier: {
            if (Peek(1).Kind == TokenKind.ColonColon) {
              Token typeToken = Consume();
              return ParseVariantPattern(typeToken, typeArgs: null, allowLabels);
            }

            if (Peek(1).Kind == TokenKind.OpenBracket
                && !HasLeadingNewline(Peek(1))
                && IsBalancedBracketFollowedBy(1, TokenKind.ColonColon)) {
              int savedPos = _pos;
              int savedNextId = _nextId;
              int savedLastError = _lastErrorTokenIndex;

              Token typeToken = Consume();
              if (TryParseTypeArgumentListAfterBase(out TypeArgumentListSyntax? typeArgs)
                  && Current.Kind == TokenKind.ColonColon) {
                return ParseVariantPattern(typeToken, typeArgs, allowLabels);
              }

              _pos = savedPos;
              _nextId = savedNextId;
              _lastErrorTokenIndex = savedLastError;
            }

            Token ident = Consume();
            return new IdentifierPatternSyntax(NextId(), ident.Span, ident);
          }

        case TokenKind.TrueLiteral:
        case TokenKind.FalseLiteral:
        case TokenKind.IntLiteral:
        case TokenKind.FloatLiteral:
        case TokenKind.RuneLiteral: {
            Token literal = Consume();
            var literalExpr = new LiteralExpressionSyntax(NextId(), literal.Span, literal);
            return new LiteralPatternSyntax(NextId(), literal.Span, literalExpr);
          }

        case TokenKind.Minus when Peek(1).Kind is TokenKind.IntLiteral or TokenKind.FloatLiteral: {
            Token minus = Consume();
            Token number = Consume();
            var literalExpr = new LiteralExpressionSyntax(NextId(), number.Span, number);
            SourceSpan span = SourceSpan.FromBounds(minus.Span.Start, number.Span.End);
            var unaryExpr = new UnaryExpressionSyntax(NextId(), span, minus, literalExpr);
            return new LiteralPatternSyntax(NextId(), span, unaryExpr);
          }

        case TokenKind.StringStart: {
            InterpolatedStringExpressionSyntax str = ParseInterpolatedString();
            return new LiteralPatternSyntax(NextId(), str.Span, str);
          }

        default: {
            ReportError(
                DiagnosticCodes.ExpectedPattern,
                Current.Span,
                $"Expected pattern, found {DescribeToken(Current)}.",
                help: !allowLabels && Current.Kind == TokenKind.At && Peek(1).Kind == TokenKind.Identifier
                    ? "label bindings ('@name') are only valid in 'when' patterns; 'is' patterns bind by position"
                    : null);
            return new MissingPatternSyntax(NextId(), new SourceSpan(CurrentMissingOffset(), 0));
          }
      }
    } finally {
      _depth--;
    }
  }

  private LabelBindingPatternSyntax ParseLabelBindingPattern() {
    Token atToken = Consume();
    Token fieldToken = ExpectIdentifier();
    Token? colonToken = null;
    Token? nameToken = null;
    if (Current.Kind == TokenKind.Colon) {
      colonToken = Consume();
      nameToken = ExpectIdentifier();
    }

    int end = (nameToken ?? colonToken ?? fieldToken).Span.End;
    return new LabelBindingPatternSyntax(
        NextId(),
        SourceSpan.FromBounds(atToken.Span.Start, Math.Max(end, atToken.Span.End)),
        atToken,
        fieldToken,
        colonToken,
        nameToken);
  }

  private VariantPatternSyntax ParseVariantPattern(Token typeToken, TypeArgumentListSyntax? typeArgs, bool allowLabels) {
    Token colonColon = Consume();
    Token variantToken = ExpectIdentifier();

    Token? openParen = null;
    Token? closeParen = null;
    var subPatterns = ImmutableArray.CreateBuilder<PatternSyntax>();
    var commas = ImmutableArray.CreateBuilder<Token>();

    if (Current.Kind == TokenKind.OpenParen && !HasLeadingNewline(Current)) {
      openParen = Consume();
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
              "Unexpected ',' before sub-pattern.");
          commas.Add(leadingComma);
          continue;
        }

        int beforePatId = _nextId;
        PatternSyntax subPattern = allowLabels && Current.Kind == TokenKind.At
            ? ParseLabelBindingPattern()
            : ParsePattern(allowLabels);
        if (subPattern is MissingPatternSyntax && _pos == startPos) {
          _nextId = beforePatId;
          break;
        }

        subPatterns.Add(subPattern);

        if (Current.Kind == TokenKind.Comma) {
          int commaIndex = _pos;
          Token comma = Consume();
          commas.Add(comma);
          if (Current.Kind == TokenKind.CloseParen) {
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
                "Expected sub-pattern before consecutive ','.");
          }
        }
      }

      closeParen = ExpectCloseWithRelated(TokenKind.CloseParen, openParen.Value);
    }

    int endOffset = closeParen is { } cp ? cp.Span.End : variantToken.Span.End;
    return new VariantPatternSyntax(
        NextId(),
        SourceSpan.FromBounds(typeToken.Span.Start, endOffset),
        typeToken,
        typeArgs,
        colonColon,
        variantToken,
        openParen,
        subPatterns.ToImmutable(),
        closeParen,
        commas.ToImmutable());
  }
}
