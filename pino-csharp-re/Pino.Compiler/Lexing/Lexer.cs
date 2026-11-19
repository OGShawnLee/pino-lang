using System.Collections.Frozen;
using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Text;

namespace Pino.Compiler.Lexing;

/// <summary>
/// Error-tolerant lexer. <see cref="Lex"/> never throws on malformed user source: every
/// problem becomes a <see cref="Diagnostic"/> (and usually a <see cref="TokenKind.Error"/>
/// token) and scanning continues until <see cref="TokenKind.EOF"/>.
/// </summary>
/// <remarks>
/// Strings are scanned with an explicit mode stack (no desugaring to <c>+</c>):
/// <list type="bullet">
/// <item><c>"..."</c> emits <see cref="TokenKind.StringStart"/>, an optional
/// <see cref="TokenKind.StringPart"/> per literal run, and <see cref="TokenKind.StringEnd"/>.</item>
/// <item><c>"""..."""</c> works the same but may span lines.</item>
/// <item><c>$ident</c> emits <see cref="TokenKind.InterpStart"/> (the <c>$</c>),
/// <see cref="TokenKind.Identifier"/> and a zero-length <see cref="TokenKind.InterpEnd"/>.</item>
/// <item><c>$(expr)</c> emits <see cref="TokenKind.InterpStart"/> (<c>$(</c>), ordinary tokens
/// for <c>expr</c> (nested strings included) and <see cref="TokenKind.InterpEnd"/> (the matching <c>)</c>).</item>
/// </list>
/// Recovery keeps the token stream balanced: an unterminated string or interpolation is closed
/// with a zero-length <see cref="TokenKind.StringEnd"/> / <see cref="TokenKind.InterpEnd"/> at the
/// recovery point (end of line for <c>"..."</c>, EOF otherwise) plus a diagnostic.
/// </remarks>
public static class Lexer {
  public const string UnexpectedCharacterCode = "PN1001";
  public const string InvalidNumericSeparatorCode = "PN1002";
  public const string InvalidNumberSuffixCode = "PN1003";
  public const string EmptyRuneCode = "PN1004";
  public const string InvalidRuneEscapeCode = "PN1005";
  public const string MultiRuneLiteralCode = "PN1006";
  public const string UnterminatedRuneCode = "PN1007";

  // PN1008 was the temporary "strings not supported" stub from Ticket 002; it is retired.
  public const string UnterminatedStringCode = "PN1009";
  public const string UnterminatedMultilineStringCode = "PN1010";
  public const string InvalidStringEscapeCode = "PN1011";
  public const string UnterminatedStringEscapeCode = "PN1012";
  public const string UnterminatedInterpolationCode = "PN1013";

  private static readonly FrozenDictionary<string, TokenKind> s_words = new Dictionary<string, TokenKind> {
    ["as"] = TokenKind.AsKeyword,
    ["break"] = TokenKind.BreakKeyword,
    ["val"] = TokenKind.ValKeyword,
    ["continue"] = TokenKind.ContinueKeyword,
    ["else"] = TokenKind.ElseKeyword,
    ["enum"] = TokenKind.EnumKeyword,
    ["from"] = TokenKind.FromKeyword,
    ["fn"] = TokenKind.FnKeyword,
    ["if"] = TokenKind.IfKeyword,
    ["import"] = TokenKind.ImportKeyword,
    ["in"] = TokenKind.InKeyword,
    ["interface"] = TokenKind.InterfaceKeyword,
    ["is"] = TokenKind.IsKeyword,
    ["for"] = TokenKind.ForKeyword,
    ["match"] = TokenKind.MatchKeyword,
    ["module"] = TokenKind.ModuleKeyword,
    ["pub"] = TokenKind.PubKeyword,
    ["return"] = TokenKind.ReturnKeyword,
    ["static"] = TokenKind.StaticKeyword,
    ["struct"] = TokenKind.StructKeyword,
    ["then"] = TokenKind.ThenKeyword,
    ["var"] = TokenKind.VarKeyword,
    ["when"] = TokenKind.WhenKeyword,
    ["union"] = TokenKind.UnionKeyword,
    ["yield"] = TokenKind.YieldKeyword,
    ["test"] = TokenKind.TestKeyword,
    ["assert"] = TokenKind.AssertKeyword,
    ["and"] = TokenKind.And,
    ["or"] = TokenKind.Or,
    ["not"] = TokenKind.Not,
    ["true"] = TokenKind.TrueLiteral,
    ["false"] = TokenKind.FalseLiteral,
  }.ToFrozenDictionary();

  private static readonly FrozenDictionary<string, TokenKind>.AlternateLookup<ReadOnlySpan<char>> s_wordLookup =
    s_words.GetAlternateLookup<ReadOnlySpan<char>>();

  public static LexResult Lex(SourceText source) {
    ArgumentNullException.ThrowIfNull(source);

    var scanner = new Scanner(source.AsSpan());
    scanner.ScanAll();

    return new LexResult(
      source,
      scanner.Tokens.ToImmutable(),
      scanner.Trivia.ToImmutable(),
      scanner.Diagnostics.ToImmutableSorted());
  }

  private enum FrameKind {
    /// <summary>Content of a <c>"..."</c> string.</summary>
    SingleString,

    /// <summary>Content of a <c>"""..."""</c> string.</summary>
    MultilineString,

    /// <summary>Expression code inside <c>$( ... )</c>.</summary>
    Interpolation,
  }

  /// <summary>
  /// One entry of the scanner mode stack. The empty stack is ordinary code; a string frame scans
  /// string content; an interpolation frame scans ordinary code until its matching <c>)</c>.
  /// </summary>
  private struct Frame {
    public FrameKind Kind;

    /// <summary>Offset of the opener (<c>"</c>, <c>"""</c> or <c>$(</c>).</summary>
    public int Start;

    /// <summary>Open <c>(</c> count inside an interpolation (excluding the <c>$(</c> itself).</summary>
    public int ParenDepth;
  }

  private ref struct Scanner {
    private readonly ReadOnlySpan<char> _text;
    private readonly List<Frame> _frames;
    private int _pos;

    public readonly ImmutableArray<Token>.Builder Tokens;
    public readonly ImmutableArray<Trivia>.Builder Trivia;
    public readonly DiagnosticBag Diagnostics;

    public Scanner(ReadOnlySpan<char> text) {
      _text = text;
      _pos = 0;
      _frames = [];
      Tokens = ImmutableArray.CreateBuilder<Token>();
      Trivia = ImmutableArray.CreateBuilder<Trivia>();
      Diagnostics = new DiagnosticBag();
    }

    private readonly bool IsAtEnd => _pos >= _text.Length;

    private readonly bool IsAtLineEnd => IsAtEnd || _text[_pos] is '\n' or '\r';

    private readonly char Current => IsAtEnd ? '\0' : _text[_pos];

    private readonly char Peek(int offset) {
      int index = _pos + offset;
      return index < _text.Length ? _text[index] : '\0';
    }

    public void ScanAll() {
      while (!IsAtEnd) {
        if (_frames.Count > 0 && _frames[^1].Kind != FrameKind.Interpolation) {
          ScanStringContent();
        } else {
          ScanNext();
        }
      }

      // EOF closes every construct that is still open (innermost first).
      Unwind(0);
      Tokens.Add(new Token(TokenKind.EOF, new SourceSpan(_text.Length, 0)));
    }

    private void ScanNext() {
      char c = _text[_pos];
      int start = _pos;

      switch (c) {
        case '\n':
          if (TryRecoverAtLineEnd()) {
            return;
          }

          _pos++;
          AddTrivia(TriviaKind.Newline, start);
          return;
        case '\r':
          if (TryRecoverAtLineEnd()) {
            return;
          }

          _pos++;
          if (Current == '\n' && !IsAtEnd) {
            _pos++;
          }

          AddTrivia(TriviaKind.Newline, start);
          return;
        case '#':
          while (!IsAtLineEnd) {
            _pos++;
          }

          AddTrivia(TriviaKind.Comment, start);
          return;
        case '\'':
          ScanRune();
          return;
        case '"':
          BeginString();
          return;
        case '@': Single(TokenKind.At); return;
        case '{': Single(TokenKind.OpenBrace); return;
        case '}': Single(TokenKind.CloseBrace); return;
        case '[': Single(TokenKind.OpenBracket); return;
        case ']': Single(TokenKind.CloseBracket); return;
        case '(':
          if (_frames.Count > 0 && _frames[^1].Kind == FrameKind.Interpolation) {
            Frame frame = _frames[^1];
            frame.ParenDepth++;
            _frames[^1] = frame;
          }

          Single(TokenKind.OpenParen);
          return;
        case ')':
          if (_frames.Count > 0 && _frames[^1].Kind == FrameKind.Interpolation) {
            Frame frame = _frames[^1];
            if (frame.ParenDepth == 0) {
              _frames.RemoveAt(_frames.Count - 1);
              Single(TokenKind.InterpEnd);
              return;
            }

            frame.ParenDepth--;
            _frames[^1] = frame;
          }

          Single(TokenKind.CloseParen);
          return;
        case ',': Single(TokenKind.Comma); return;
        case '?': Single(TokenKind.Question); return;
        case '+': WithOptionalEquals(TokenKind.Plus, TokenKind.PlusAssign); return;
        case '-': WithOptionalEquals(TokenKind.Minus, TokenKind.MinusAssign); return;
        case '*': WithOptionalEquals(TokenKind.Star, TokenKind.StarAssign); return;
        case '/': WithOptionalEquals(TokenKind.Slash, TokenKind.SlashAssign); return;
        case '%': WithOptionalEquals(TokenKind.Percent, TokenKind.PercentAssign); return;
        case '<': WithOptionalEquals(TokenKind.Less, TokenKind.LessEqual); return;
        case '>': WithOptionalEquals(TokenKind.Greater, TokenKind.GreaterEqual); return;
        case ':':
          if (Peek(1) == ':') {
            Multi(2, TokenKind.ColonColon);
          } else {
            Single(TokenKind.Colon);
          }

          return;
        case '=':
          if (Peek(1) == '=') {
            Multi(Peek(2) == '=' ? 3 : 2, Peek(2) == '=' ? TokenKind.EqualEqualEqual : TokenKind.EqualEqual);
          } else if (Peek(1) == '>') {
            Multi(2, TokenKind.Arrow);
          } else {
            Single(TokenKind.Assign);
          }

          return;
        case '!':
          if (Peek(1) == '=') {
            Multi(Peek(2) == '=' ? 3 : 2, Peek(2) == '=' ? TokenKind.BangEqualEqual : TokenKind.BangEqual);
          } else {
            ScanUnexpected();
          }

          return;
      }

      if (IsWhitespace(c)) {
        while (!IsAtEnd && IsWhitespace(_text[_pos])) {
          _pos++;
        }

        AddTrivia(TriviaKind.Whitespace, start);
      } else if (IsAsciiDigit(c)) {
        ScanNumber();
      } else if (IsIdentifierStart(c)) {
        ScanWord();
      } else {
        ScanUnexpected();
      }
    }

    private void Single(TokenKind kind) => Multi(1, kind);

    private void Multi(int length, TokenKind kind) {
      int start = _pos;
      _pos += length;
      AddToken(kind, start);
    }

    private void WithOptionalEquals(TokenKind plain, TokenKind withEquals) {
      if (Peek(1) == '=') {
        Multi(2, withEquals);
      } else {
        Multi(1, plain);
      }
    }

    private void AddToken(TokenKind kind, int start) =>
      Tokens.Add(new Token(kind, new SourceSpan(start, _pos - start)));

    private void AddMissingToken(TokenKind kind) =>
      Tokens.Add(new Token(kind, new SourceSpan(_pos, 0)));

    private void AddTrivia(TriviaKind kind, int start) =>
      Trivia.Add(new Trivia(kind, new SourceSpan(start, _pos - start)));

    // ---------------------------------------------------------------------------------------
    // Strings and interpolation
    // ---------------------------------------------------------------------------------------

    private void BeginString() {
      int start = _pos;
      bool multiline = Peek(1) == '"' && Peek(2) == '"';
      _pos += multiline ? 3 : 1;
      AddToken(TokenKind.StringStart, start);
      _frames.Add(new Frame {
        Kind = multiline ? FrameKind.MultilineString : FrameKind.SingleString,
        Start = start,
      });
    }

    /// <summary>
    /// Scans string content until the next structural token (closing quote, interpolation
    /// opener, or a recovery point) is emitted. Literal runs become <see cref="TokenKind.StringPart"/>.
    /// </summary>
    private void ScanStringContent() {
      Frame frame = _frames[^1];
      bool multiline = frame.Kind == FrameKind.MultilineString;
      int partStart = _pos;

      while (!IsAtEnd) {
        char c = _text[_pos];

        if (multiline) {
          if (c == '"' && Peek(1) == '"' && Peek(2) == '"') {
            FlushPart(partStart);
            Multi(3, TokenKind.StringEnd);
            _frames.RemoveAt(_frames.Count - 1);
            return;
          }
        } else if (c == '"') {
          FlushPart(partStart);
          Single(TokenKind.StringEnd);
          _frames.RemoveAt(_frames.Count - 1);
          return;
        } else if (c is '\n' or '\r') {
          // A single-line string never swallows the rest of the file: recover at the line end.
          FlushPart(partStart);
          Unwind(_frames.Count - 1);
          return;
        }

        if (c == '\\') {
          ScanEscape();
          continue;
        }

        if (c == '$') {
          char next = Peek(1);
          if (next == '(') {
            FlushPart(partStart);
            int start = _pos;
            _pos += 2;
            AddToken(TokenKind.InterpStart, start);
            _frames.Add(new Frame { Kind = FrameKind.Interpolation, Start = start });
            return;
          }

          if (IsAsciiLetter(next) || next == '_') {
            FlushPart(partStart);
            ScanSimpleInterpolation();
            partStart = _pos;
            continue;
          }
        }

        _pos++;
      }

      // EOF: leave the frame open so Unwind reports it together with any enclosing frames.
      FlushPart(partStart);
    }

    private void FlushPart(int partStart) {
      if (_pos > partStart) {
        AddToken(TokenKind.StringPart, partStart);
      }
    }

    private void ScanSimpleInterpolation() {
      int start = _pos;
      _pos++; // '$'
      AddToken(TokenKind.InterpStart, start);

      int identStart = _pos;
      while (!IsAtEnd && (IsAsciiLetter(_text[_pos]) || IsAsciiDigit(_text[_pos]) || _text[_pos] == '_')) {
        _pos++;
      }

      AddToken(TokenKind.Identifier, identStart);
      AddMissingToken(TokenKind.InterpEnd);
    }

    private void ScanEscape() {
      int escapeStart = _pos;
      _pos++; // '\'

      if (IsAtLineEnd) {
        // Do not consume the line break: it stays available for recovery / content.
        Diagnostics.ReportError(
          UnterminatedStringEscapeCode,
          new SourceSpan(escapeStart, 1),
          "Unterminated escape sequence in string literal.");
        return;
      }

      if (Current is 'n' or 't' or 'r' or '"' or '\\' or '$') {
        _pos++;
        return;
      }

      AdvanceCodePoint();
      Diagnostics.ReportError(
        InvalidStringEscapeCode,
        new SourceSpan(escapeStart, _pos - escapeStart),
        $"Invalid escape sequence '{_text.Slice(escapeStart, _pos - escapeStart)}' in string literal.");
    }

    /// <summary>
    /// At a line break in code mode: if the innermost enclosing string is a single-line one,
    /// closes every frame down to (and including) it and reports whether it did so.
    /// </summary>
    private bool TryRecoverAtLineEnd() {
      for (int i = _frames.Count - 1; i >= 0; i--) {
        switch (_frames[i].Kind) {
          case FrameKind.SingleString:
            Unwind(i);
            return true;
          case FrameKind.MultilineString:
            return false;
        }
      }

      return false;
    }

    /// <summary>
    /// Pops frames (innermost first) until <paramref name="depth"/> remain, reporting each
    /// unclosed construct and emitting zero-length closing tokens so the stream stays balanced.
    /// </summary>
    private void Unwind(int depth) {
      while (_frames.Count > depth) {
        Frame frame = _frames[^1];
        _frames.RemoveAt(_frames.Count - 1);

        switch (frame.Kind) {
          case FrameKind.Interpolation:
            Diagnostics.ReportError(
              UnterminatedInterpolationCode,
              new SourceSpan(frame.Start, 2),
              "Unterminated string interpolation: missing ')'.");
            AddMissingToken(TokenKind.InterpEnd);
            break;
          case FrameKind.SingleString:
            Diagnostics.ReportError(
              UnterminatedStringCode,
              new SourceSpan(frame.Start, _pos - frame.Start),
              "Unterminated string literal: missing closing '\"'.");
            AddMissingToken(TokenKind.StringEnd);
            break;
          case FrameKind.MultilineString:
            Diagnostics.ReportError(
              UnterminatedMultilineStringCode,
              new SourceSpan(frame.Start, 3),
              "Unterminated multi-line string literal: missing closing '\"\"\"'.");
            AddMissingToken(TokenKind.StringEnd);
            break;
        }
      }
    }

    // ---------------------------------------------------------------------------------------
    // Other tokens
    // ---------------------------------------------------------------------------------------

    private void ScanUnexpected() {
      int start = _pos;
      AdvanceCodePoint();
      var span = new SourceSpan(start, _pos - start);
      Diagnostics.ReportError(
        UnexpectedCharacterCode,
        span,
        $"Unexpected character '{_text.Slice(span.Start, span.Length)}'.");
      AddToken(TokenKind.Error, start);
    }

    private void AdvanceCodePoint() {
      if (char.IsHighSurrogate(_text[_pos]) && _pos + 1 < _text.Length && char.IsLowSurrogate(_text[_pos + 1])) {
        _pos += 2;
      } else {
        _pos++;
      }
    }

    private void ScanWord() {
      int start = _pos;
      while (!IsAtEnd && IsIdentifierPart(_text[_pos])) {
        _pos++;
      }

      ReadOnlySpan<char> word = _text.Slice(start, _pos - start);
      TokenKind kind = s_wordLookup.TryGetValue(word, out TokenKind found) ? found : TokenKind.Identifier;
      AddToken(kind, start);
    }

    private void ScanNumber() {
      int start = _pos;
      bool isFloat = false;
      bool failed = false;

      ScanDigitRun();

      if (Current == '.' && (IsAsciiDigit(Peek(1)) || Peek(1) == '_')) {
        isFloat = true;
        _pos++;
        ScanDigitRun();
      }

      if (!IsAtEnd && (IsAsciiLetter(_text[_pos]) || _text[_pos] == '$')) {
        while (!IsAtEnd && IsIdentifierPart(_text[_pos])) {
          _pos++;
        }

        Diagnostics.ReportError(
          InvalidNumberSuffixCode,
          new SourceSpan(start, _pos - start),
          $"Invalid number literal '{_text.Slice(start, _pos - start)}': unexpected letters after digits.");
        failed = true;
      }

      AddToken(
        failed ? TokenKind.Error : isFloat ? TokenKind.FloatLiteral : TokenKind.IntLiteral,
        start);
    }

    private void ScanDigitRun() {
      int start = _pos;
      while (!IsAtEnd && (IsAsciiDigit(_text[_pos]) || _text[_pos] == '_')) {
        _pos++;
      }

      ReadOnlySpan<char> run = _text.Slice(start, _pos - start);
      if (run.Length > 0 && (run[0] == '_' || run[^1] == '_' || run.Contains("__".AsSpan(), StringComparison.Ordinal))) {
        Diagnostics.ReportError(
          InvalidNumericSeparatorCode,
          new SourceSpan(start, run.Length),
          $"Invalid '_' separator in number '{run}': separators must sit between digits.");
      }
    }

    private void ScanRune() {
      int start = _pos;
      _pos++; // opening quote
      bool valid = true;

      if (IsAtLineEnd) {
        Diagnostics.ReportError(UnterminatedRuneCode, new SourceSpan(start, 1), "Unterminated rune literal.");
        AddToken(TokenKind.Error, start);
        return;
      }

      if (Current == '\'') {
        _pos++;
        Diagnostics.ReportError(EmptyRuneCode, new SourceSpan(start, 2), "Empty rune literal.");
        AddToken(TokenKind.Error, start);
        return;
      }

      if (Current == '\\') {
        int escapeStart = _pos;
        _pos++;

        if (IsAtLineEnd) {
          Diagnostics.ReportError(
            UnterminatedRuneCode,
            new SourceSpan(start, _pos - start),
            "Unterminated escape sequence in rune literal.");
          AddToken(TokenKind.Error, start);
          return;
        }

        if (Current is 'n' or 't' or 'r' or '\'' or '\\') {
          _pos++;
        } else {
          AdvanceCodePoint();
          valid = false;
          Diagnostics.ReportError(
            InvalidRuneEscapeCode,
            new SourceSpan(escapeStart, _pos - escapeStart),
            $"Invalid escape sequence '{_text.Slice(escapeStart, _pos - escapeStart)}' in rune literal.");
        }
      } else {
        AdvanceCodePoint();
      }

      if (!IsAtEnd && Current == '\'') {
        _pos++;
        AddToken(valid ? TokenKind.RuneLiteral : TokenKind.Error, start);
        return;
      }

      int probe = _pos;
      while (probe < _text.Length && _text[probe] != '\'' && !IsWhitespaceOrNewline(_text[probe])) {
        probe++;
      }

      if (probe < _text.Length && _text[probe] == '\'') {
        _pos = probe + 1;
        if (valid) {
          Diagnostics.ReportError(
            MultiRuneLiteralCode,
            new SourceSpan(start, _pos - start),
            "Rune literal must contain exactly one rune.");
        }

        AddToken(TokenKind.Error, start);
        return;
      }

      Diagnostics.ReportError(
        UnterminatedRuneCode,
        new SourceSpan(start, _pos - start),
        "Unterminated rune literal.");
      AddToken(TokenKind.Error, start);
    }

    private static bool IsWhitespaceOrNewline(char c) => c is '\n' or '\r' || char.IsWhiteSpace(c);

    private static bool IsWhitespace(char c) => c is not ('\n' or '\r') && char.IsWhiteSpace(c);

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    private static bool IsAsciiLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsIdentifierStart(char c) => IsAsciiLetter(c) || c is '_' or '$';

    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || IsAsciiDigit(c);
  }
}
