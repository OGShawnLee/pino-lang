using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Parsing;

internal sealed partial class Parser {
  private const int MaxDepth = 512;

  private const TokenFlags AnySpace =
      TokenFlags.LeadingWhitespace | TokenFlags.LeadingNewline | TokenFlags.LeadingComment;

  private readonly LexResult _lex;
  private readonly ImmutableArray<Token> _tokens;
  private readonly DiagnosticBag _diagnostics = new();

  private int _pos;
  private int _nextId;
  private int _depth;
  private int _lastErrorTokenIndex = -1;
  private bool _depthExceeded;
  private bool _suppressDiagnostics;
  private bool _speculativeError;

  private Parser(LexResult lex) {
    ArgumentNullException.ThrowIfNull(lex);
    _lex = lex;
    _tokens = lex.Tokens.IsDefaultOrEmpty
        ? [new Token(TokenKind.EOF, SourceSpan.Empty)]
        : lex.Tokens;
  }

  private Token Current => Peek(0);

  private bool IsAtEnd => Current.Kind == TokenKind.EOF;

  internal static ParseResult<ExpressionSyntax> ParseStandaloneExpression(LexResult lex) {
    ArgumentNullException.ThrowIfNull(lex);
    var parser = new Parser(lex);
    ExpressionSyntax expr = parser.ParseExpression(0, ExprContext.AllowBraceLiteral);
    return parser.CreateResult(expr);
  }

  internal static ParseResult<TypeSyntax> ParseStandaloneType(LexResult lex) {
    ArgumentNullException.ThrowIfNull(lex);
    var parser = new Parser(lex);
    TypeSyntax type = parser.ParseType();
    return parser.CreateResult(type);
  }

  internal static ParseResult<PatternSyntax> ParseStandalonePattern(LexResult lex) {
    ArgumentNullException.ThrowIfNull(lex);
    var parser = new Parser(lex);
    PatternSyntax pattern = parser.ParsePattern();
    return parser.CreateResult(pattern);
  }

  internal static ParseResult<BlockStatementSyntax> ParseStandaloneBlock(LexResult lex) {
    ArgumentNullException.ThrowIfNull(lex);
    var parser = new Parser(lex);
    BlockStatementSyntax block = parser.ParseBlock();
    return parser.CreateResult(block);
  }

  private ParseResult<TNode> CreateResult<TNode>(TNode node) where TNode : SyntaxNode =>
      new(_lex, node, _nextId, _pos, _diagnostics.ToImmutableSorted());

  private int NextId() => _nextId++;

  private Token Peek(int offset) {
    int index = _pos + offset;
    if (index < 0) {
      return _tokens[0];
    }

    return index >= _tokens.Length ? _tokens[^1] : _tokens[index];
  }

  private Token Consume() {
    Token current = Current;
    if (_pos < _tokens.Length - 1) {
      _pos++;
    }

    return current;
  }

  private int CurrentMissingOffset() =>
      _pos > 0 ? _tokens[_pos - 1].Span.End : Current.Span.Start;

  private Token MissingToken(TokenKind kind) =>
      new(kind, new SourceSpan(CurrentMissingOffset(), 0), TokenFlags.Missing);

  private Token Expect(TokenKind expected) {
    if (Current.Kind == expected) {
      return Consume();
    }

    ReportErrorAt(
        _pos,
        DiagnosticCodes.ExpectedToken,
        Current.Span,
        $"Expected {DescribeTokenKind(expected)}, found {DescribeToken(Current)}.");
    return MissingToken(expected);
  }

  private Token ExpectCloseWithRelated(TokenKind expected, Token opener) {
    if (Current.Kind == expected) {
      return Consume();
    }

    ImmutableArray<RelatedLocation> related = (opener.Flags & TokenFlags.Missing) == 0
        ? [new RelatedLocation(opener.Span, "opened here")]
        : [];
    ReportErrorAt(
        _pos,
        DiagnosticCodes.ExpectedToken,
        Current.Span,
        $"Expected {DescribeTokenKind(expected)}, found {DescribeToken(Current)}.",
        related);
    return MissingToken(expected);
  }

  private Token ExpectIdentifier() {
    if (Current.Kind == TokenKind.Identifier) {
      return Consume();
    }

    string message = IsKeyword(Current.Kind)
        ? $"Expected identifier, found reserved keyword '{GetTokenText(Current)}'."
        : $"Expected identifier, found {DescribeToken(Current)}.";
    ReportErrorAt(_pos, DiagnosticCodes.ExpectedIdentifier, Current.Span, message);
    return MissingToken(TokenKind.Identifier);
  }

  private bool TryConsumeArrow(out Token arrowToken, out Token? secondaryArrowToken) {
    if (Current.Kind == TokenKind.Arrow) {
      arrowToken = Consume();
      secondaryArrowToken = null;
      return true;
    }

    if (IsFatArrow()) {
      int assignIndex = _pos;
      Token assign = Consume();
      Token greater = Consume();
      ReportErrorAt(
          assignIndex,
          DiagnosticCodes.DeprecatedFatArrow,
          SourceSpan.FromBounds(assign.Span.Start, greater.Span.End),
          "'=>' is not valid; use '->' instead.",
          help: "replace '=>' with '->'");
      arrowToken = assign;
      secondaryArrowToken = greater;
      return true;
    }

    arrowToken = default;
    secondaryArrowToken = null;
    return false;
  }

  private bool IsFatArrow() =>
      Current.Kind == TokenKind.Assign
      && Peek(1).Kind == TokenKind.Greater
      && !HasLeadingSpace(Peek(1))
      && Current.Span.End == Peek(1).Span.Start;

  private void ReportError(
      string code,
      SourceSpan span,
      string message,
      ImmutableArray<RelatedLocation> related = default,
      string? help = null) =>
      ReportErrorAt(_pos, code, span, message, related, help);

  private void ReportErrorAt(
      int tokenIndex,
      string code,
      SourceSpan span,
      string message,
      ImmutableArray<RelatedLocation> related = default,
      string? help = null) {
    if (_suppressDiagnostics) {
      _speculativeError = true;
      return;
    }

    if (_depthExceeded) {
      return;
    }

    if (tokenIndex >= 0 && tokenIndex < _tokens.Length && _tokens[tokenIndex].Kind == TokenKind.Error) {
      _lastErrorTokenIndex = tokenIndex;
      return;
    }

    if (tokenIndex == _lastErrorTokenIndex) {
      return;
    }

    _lastErrorTokenIndex = tokenIndex;
    _diagnostics.ReportError(code, span, message, related, help: help);
  }

  private void ReportDepthExceeded() {
    if (_suppressDiagnostics) {
      _speculativeError = true;
      return;
    }

    if (_depthExceeded) {
      return;
    }

    _depthExceeded = true;
    _lastErrorTokenIndex = _pos;
    _diagnostics.ReportError(
        DiagnosticCodes.MaxNestingDepthExceeded,
        Current.Span,
        $"Maximum nesting depth ({MaxDepth}) exceeded.");
  }

  private static bool HasLeadingNewline(Token token) =>
      (token.Flags & TokenFlags.LeadingNewline) != 0;

  private static bool HasLeadingSpace(Token token) =>
      (token.Flags & AnySpace) != 0;

  private string GetTokenText(Token token) =>
      (token.Flags & TokenFlags.Missing) != 0
          ? string.Empty
          : _lex.GetText(token).ToString();

  private bool IsIdentifierText(Token token, string expected) =>
      token.Kind == TokenKind.Identifier
      && (token.Flags & TokenFlags.Missing) == 0
      && _lex.GetText(token).SequenceEqual(expected.AsSpan());

  private static bool IsCloserOrEOF(TokenKind kind) =>
      kind is TokenKind.CloseParen
          or TokenKind.CloseBracket
          or TokenKind.CloseBrace
          or TokenKind.InterpEnd
          or TokenKind.StringEnd
          or TokenKind.EOF;

  private static bool IsStatementSyncToken(TokenKind kind) =>
      kind is TokenKind.FnKeyword
          or TokenKind.StructKeyword
          or TokenKind.EnumKeyword
          or TokenKind.UnionKeyword
          or TokenKind.InterfaceKeyword
          or TokenKind.ValKeyword
          or TokenKind.VarKeyword
          or TokenKind.IfKeyword
          or TokenKind.ForKeyword
          or TokenKind.MatchKeyword
          or TokenKind.ReturnKeyword
          or TokenKind.BreakKeyword
          or TokenKind.ContinueKeyword
          or TokenKind.YieldKeyword
          or TokenKind.ImportKeyword
          or TokenKind.FromKeyword
          or TokenKind.ModuleKeyword
          or TokenKind.TestKeyword
          or TokenKind.AssertKeyword
          or TokenKind.PubKeyword
          or TokenKind.StaticKeyword
          or TokenKind.WhenKeyword
          or TokenKind.ElseKeyword
          or TokenKind.CloseBrace
          or TokenKind.EOF;

  private bool CanStartExpression(Token token) =>
      token.Kind switch {
        TokenKind.Identifier => true,
        TokenKind.IntLiteral => true,
        TokenKind.FloatLiteral => true,
        TokenKind.RuneLiteral => true,
        TokenKind.TrueLiteral => true,
        TokenKind.FalseLiteral => true,
        TokenKind.StringStart => true,
        TokenKind.OpenParen => true,
        TokenKind.OpenBracket => true,
        TokenKind.Minus => true,
        TokenKind.Not => true,
        TokenKind.IfKeyword => true,
        TokenKind.MatchKeyword => true,
        TokenKind.OpenBrace => true,
        TokenKind.Error => true,
        TokenKind.FnKeyword => Peek(1).Kind == TokenKind.OpenParen,
        TokenKind.At => Peek(1).Kind == TokenKind.OpenParen,
        _ => false,
      };

  private bool CanStartType(Token token, bool disallowNewlineBeforeBracket = false) =>
      token.Kind switch {
        TokenKind.Identifier => true,
        TokenKind.FnKeyword => true,
        TokenKind.OpenBracket => (!disallowNewlineBeforeBracket || !HasLeadingNewline(token))
            && Peek(1).Kind == TokenKind.CloseBracket,
        TokenKind.At => Peek(1).Kind == TokenKind.OpenParen,
        _ => false,
      };

  private static bool IsKeyword(TokenKind kind) =>
      kind is TokenKind.AsKeyword
          or TokenKind.BreakKeyword
          or TokenKind.ValKeyword
          or TokenKind.ContinueKeyword
          or TokenKind.ElseKeyword
          or TokenKind.EnumKeyword
          or TokenKind.FromKeyword
          or TokenKind.FnKeyword
          or TokenKind.IfKeyword
          or TokenKind.ImportKeyword
          or TokenKind.InKeyword
          or TokenKind.InterfaceKeyword
          or TokenKind.IsKeyword
          or TokenKind.ForKeyword
          or TokenKind.MatchKeyword
          or TokenKind.ModuleKeyword
          or TokenKind.PubKeyword
          or TokenKind.ReturnKeyword
          or TokenKind.StaticKeyword
          or TokenKind.StructKeyword
          or TokenKind.ThenKeyword
          or TokenKind.VarKeyword
          or TokenKind.WhenKeyword
          or TokenKind.UnionKeyword
          or TokenKind.YieldKeyword
          or TokenKind.TestKeyword
          or TokenKind.AssertKeyword
          or TokenKind.And
          or TokenKind.Or
          or TokenKind.Not
          or TokenKind.TrueLiteral
          or TokenKind.FalseLiteral;

  private string DescribeToken(Token token) =>
      token.Kind switch {
        TokenKind.EOF => "end of file",
        TokenKind.StringStart => "string literal",
        TokenKind.StringEnd => "end of string",
        TokenKind.InterpStart => "interpolation",
        TokenKind.InterpEnd => "')'",
        _ => $"'{GetTokenText(token)}'",
      };

  private static string DescribeTokenKind(TokenKind kind) =>
      kind switch {
        TokenKind.OpenParen => "'('",
        TokenKind.CloseParen => "')'",
        TokenKind.OpenBracket => "'['",
        TokenKind.CloseBracket => "']'",
        TokenKind.OpenBrace => "'{'",
        TokenKind.CloseBrace => "'}'",
        TokenKind.Comma => "','",
        TokenKind.Colon => "':'",
        TokenKind.ColonColon => "'::'",
        TokenKind.Arrow => "'->'",
        TokenKind.ThenKeyword => "'then'",
        TokenKind.ElseKeyword => "'else'",
        TokenKind.InterpEnd => "')'",
        TokenKind.StringEnd => "'\"'",
        TokenKind.Identifier => "identifier",
        _ => kind.ToString(),
      };
}
