using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Parsing;

public sealed partial class Parser {
  private const int MaxDepth = 512;

  private const TokenFlags AnySpace =
      TokenFlags.LeadingWhitespace | TokenFlags.LeadingNewline | TokenFlags.LeadingComment;

  private readonly LexResult _lex;
  private readonly ImmutableArray<Token> _tokens;
  private readonly DiagnosticBag _diagnostics = new();
  private readonly bool _enableColumn1Rule;
  private readonly List<Token> _openBraceStack = [];
  private readonly HashSet<int> _reportedErrorOffsets = [];

  private int _pos;
  private int _nextId;
  private int _depth;
  private int _lastErrorTokenIndex = -1;
  private int _column1TriggeredPos = -1;
  private bool _depthExceeded;
  private bool _suppressDiagnostics;
  private bool _speculativeError;

  private Parser(LexResult lex, bool enableColumn1Rule) {
    ArgumentNullException.ThrowIfNull(lex);
    _lex = lex;
    _tokens = lex.Tokens.IsDefaultOrEmpty
        ? [new Token(TokenKind.EOF, SourceSpan.Empty)]
        : lex.Tokens;
    _enableColumn1Rule = enableColumn1Rule;
  }

  private Token Current => Peek(0);

  private bool IsAtEnd => Current.Kind == TokenKind.EOF;

  public static Syntax.ParseResult Parse(SourceText source) {
    ArgumentNullException.ThrowIfNull(source);
    return Parse(Lexer.Lex(source));
  }

  public static Syntax.ParseResult Parse(LexResult lex) {
    ArgumentNullException.ThrowIfNull(lex);
    bool enableColumn1Rule = HasUnclosedBraces(lex.Tokens);
    var parser = new Parser(lex, enableColumn1Rule);
    CompilationUnitSyntax root = parser.ParseCompilationUnit();

    var allDiagnostics = new DiagnosticBag();
    allDiagnostics.AddRange(lex.Diagnostics);
    allDiagnostics.AddRange(parser._diagnostics);
    return new Syntax.ParseResult(
        lex.Source,
        lex,
        root,
        parser._nextId,
        allDiagnostics.ToImmutableSorted());
  }

  internal static ParseResult<ExpressionSyntax> ParseStandaloneExpression(LexResult lex) {
    ArgumentNullException.ThrowIfNull(lex);
    var parser = new Parser(lex, enableColumn1Rule: false);
    ExpressionSyntax expr = parser.ParseExpression(0, ExprContext.AllowBraceLiteral);
    return parser.CreateResult(expr);
  }

  internal static ParseResult<TypeSyntax> ParseStandaloneType(LexResult lex) {
    ArgumentNullException.ThrowIfNull(lex);
    var parser = new Parser(lex, enableColumn1Rule: false);
    TypeSyntax type = parser.ParseType();
    return parser.CreateResult(type);
  }

  internal static ParseResult<PatternSyntax> ParseStandalonePattern(LexResult lex) {
    ArgumentNullException.ThrowIfNull(lex);
    var parser = new Parser(lex, enableColumn1Rule: false);
    PatternSyntax pattern = parser.ParsePattern();
    return parser.CreateResult(pattern);
  }

  internal static ParseResult<BlockStatementSyntax> ParseStandaloneBlock(LexResult lex) {
    ArgumentNullException.ThrowIfNull(lex);
    var parser = new Parser(lex, enableColumn1Rule: false);
    BlockStatementSyntax block = parser.ParseBlock();
    return parser.CreateResult(block);
  }

  private CompilationUnitSyntax ParseCompilationUnit() {
    var items = ImmutableArray.CreateBuilder<SyntaxNode>();
    while (!IsAtEnd) {
      int startPos = _pos;
      ParseBlockItem(items, insideBlock: false);

      if (_pos == startPos && !IsAtEnd) {
        Token skipped = Consume();
        items.Add(new ErrorStatementSyntax(NextId(), skipped.Span, [skipped]));
      }
    }

    Token eofToken = Current;
    SourceSpan span = items.Count > 0
        ? SourceSpan.FromBounds(items[0].Span.Start, eofToken.Span.End)
        : eofToken.Span;
    return new CompilationUnitSyntax(NextId(), span, items.ToImmutable(), eofToken);
  }

  private static bool HasUnclosedBraces(ImmutableArray<Token> tokens) {
    if (tokens.IsDefaultOrEmpty) {
      return false;
    }

    int openCount = 0;
    int closeCount = 0;
    foreach (Token token in tokens) {
      if (token.Kind == TokenKind.OpenBrace) {
        openCount++;
      } else if (token.Kind == TokenKind.CloseBrace) {
        closeCount++;
      }
    }

    return openCount > closeCount;
  }

  private bool PushOpenBrace(Token openBrace) {
    if ((openBrace.Flags & TokenFlags.Missing) != 0) {
      return false;
    }

    _openBraceStack.Add(openBrace);
    return true;
  }

  private void PopOpenBrace(bool pushed) {
    if (pushed && _openBraceStack.Count > 0) {
      _openBraceStack.RemoveAt(_openBraceStack.Count - 1);
    }
  }

  private bool MatchesColumn1Trigger() {
    if (!_enableColumn1Rule || _openBraceStack.Count == 0 || IsAtEnd) {
      return false;
    }

    if (!IsAtColumn1(Current)) {
      return false;
    }

    return Current.Kind switch {
      TokenKind.FnKeyword => Peek(1).Kind == TokenKind.Identifier,
      TokenKind.StructKeyword => true,
      TokenKind.UnionKeyword => true,
      TokenKind.EnumKeyword => true,
      TokenKind.InterfaceKeyword => true,
      TokenKind.TestKeyword => true,
      TokenKind.ModuleKeyword => true,
      TokenKind.ImportKeyword => true,
      TokenKind.FromKeyword => true,
      TokenKind.PubKeyword => true,
      TokenKind.At => IsAdjacentDoubleAt(),
      _ => false,
    };
  }

  private bool CheckColumn1Trigger() {
    if (!MatchesColumn1Trigger()) {
      return false;
    }

    if (!_suppressDiagnostics && _column1TriggeredPos != _pos) {
      _column1TriggeredPos = _pos;
      var related = ImmutableArray.CreateBuilder<RelatedLocation>(_openBraceStack.Count);
      foreach (Token brace in _openBraceStack) {
        related.Add(new RelatedLocation(brace.Span, "this block is never closed"));
      }

      ReportErrorAt(
          _pos,
          DiagnosticCodes.ExpectedToken,
          new SourceSpan(Current.Span.Start, 0),
          "expected '}' before this declaration",
          related.ToImmutable());
    }

    return true;
  }

  private bool IsAtColumn1(Token token) =>
      (_pos == 0 || HasLeadingNewline(token))
      && _lex.Source.GetLineColumn(token.Span.Start).Column == 1;

  private bool IsAdjacentDoubleAt() =>
      Current.Kind == TokenKind.At
      && Peek(1).Kind == TokenKind.At
      && !HasLeadingSpace(Peek(1))
      && Current.Span.End == Peek(1).Span.Start;

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

  private Token MissingTokenAt(TokenKind kind, int offset) =>
      new(kind, new SourceSpan(offset, 0), TokenFlags.Missing);

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

  private bool TryConsumeAnyArrowTokens(out Token arrowToken, out Token? secondaryArrowToken) {
    if (Current.Kind == TokenKind.Arrow) {
      arrowToken = Consume();
      secondaryArrowToken = null;
      return true;
    }

    if (IsFatArrow()) {
      arrowToken = Consume();
      secondaryArrowToken = Consume();
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

    if (tokenIndex == _lastErrorTokenIndex || !_reportedErrorOffsets.Add(span.Start)) {
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
    if (_pos == _lastErrorTokenIndex || !_reportedErrorOffsets.Add(Current.Span.Start)) {
      return;
    }

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

  private bool IsStatementOrTopLevelSyncToken(bool insideBlock) {
    if (Current.Kind == TokenKind.EOF) {
      return true;
    }

    if (insideBlock && Current.Kind == TokenKind.CloseBrace) {
      return true;
    }

    if (Current.Kind == TokenKind.At) {
      return IsAdjacentDoubleAt()
          || IsIdentifierText(Peek(1), "pub_testing")
          || IsIdentifierText(Peek(1), "generic");
    }

    return Current.Kind is TokenKind.FnKeyword
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
        or TokenKind.PubKeyword;
  }

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
        TokenKind.Assign => "'='",
        TokenKind.Arrow => "'->'",
        TokenKind.ImportKeyword => "'import'",
        TokenKind.InKeyword => "'in'",
        TokenKind.ThenKeyword => "'then'",
        TokenKind.ElseKeyword => "'else'",
        TokenKind.InterpEnd => "')'",
        TokenKind.StringEnd => "'\"'",
        TokenKind.Identifier => "identifier",
        _ => kind.ToString(),
      };
}
