using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;

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
    ["mut"] = TokenKind.MutKeyword,
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
    try {
      scanner.ScanAll();

      return new LexResult(
        source,
        scanner.Tokens.ToImmutableArrayAndReturn(),
        scanner.Trivia.ToImmutableArrayAndReturn(),
        scanner.Diagnostics.ToImmutableSorted());
    } finally {
      scanner.Dispose();
    }
  }

  private ref struct PooledList<T> where T : unmanaged {
    private T[] _buffer;
    private int _count;

    public PooledList(int initialCapacity) {
      _buffer = ArrayPool<T>.Shared.Rent(Math.Max(16, initialCapacity));
      _count = 0;
    }

    public void Add(T item) {
      if (_count == _buffer.Length) {
        Grow();
      }

      _buffer[_count++] = item;
    }

    public ImmutableArray<T> ToImmutableArrayAndReturn() {
      if (_count == 0) {
        ReturnBuffer();
        return [];
      }

      var exact = new T[_count];
      _buffer.AsSpan(0, _count).CopyTo(exact);
      ReturnBuffer();
      return ImmutableCollectionsMarshal.AsImmutableArray(exact);
    }

    public void ReturnBuffer() {
      T[] buffer = _buffer;
      if (buffer.Length > 0) {
        _buffer = [];
        ArrayPool<T>.Shared.Return(buffer);
      }
    }

    private void Grow() {
      T[] next = ArrayPool<T>.Shared.Rent(_buffer.Length * 2);
      _buffer.AsSpan(0, _count).CopyTo(next);
      ArrayPool<T>.Shared.Return(_buffer);
      _buffer = next;
    }
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
    private TokenFlags _pendingFlags;

    public PooledList<Token> Tokens;
    public PooledList<Trivia> Trivia;
    public readonly DiagnosticBag Diagnostics;

    public Scanner(ReadOnlySpan<char> text) {
      _text = text;
      _pos = 0;
      _pendingFlags = TokenFlags.None;
      _frames = [];
      Tokens = new PooledList<Token>(text.Length / 5);
      Trivia = new PooledList<Trivia>(text.Length / 8);
      Diagnostics = new DiagnosticBag();
    }

    public void Dispose() {
      Tokens.ReturnBuffer();
      Trivia.ReturnBuffer();
    }

    private readonly bool IsAtEnd => _pos >= _text.Length;

    private readonly bool IsAtLineEnd => IsAtEnd || _text[_pos] is '\n' or '\r';

    private readonly char Current => IsAtEnd ? '\0' : _text[_pos];

    private readonly char Peek(int offset) {
      int index = _pos + offset;
      return index < _text.Length ? _text[index] : '\0';
    }

    public void ScanAll() {
      if (!IsAtEnd && _text[_pos] == '\uFEFF') {
        _pos++;
        AddTrivia(TriviaKind.ByteOrderMark, 0);
      }

      while (!IsAtEnd) {
        if (_frames.Count > 0 && _frames[^1].Kind != FrameKind.Interpolation) {
          ScanStringContent();
        } else {
          ScanNext();
        }
      }

      // EOF closes every construct that is still open (innermost first).
      Unwind(0);
      Tokens.Add(new Token(TokenKind.EOF, new SourceSpan(_text.Length, 0), _pendingFlags));
      _pendingFlags = TokenFlags.None;
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
        case '-':
          if (Peek(1) == '>') {
            Multi(2, TokenKind.Arrow);
          } else {
            WithOptionalEquals(TokenKind.Minus, TokenKind.MinusAssign);
          }

          return;
        case '*': WithOptionalEquals(TokenKind.Star, TokenKind.StarAssign); return;
        case '/':
          if (Peek(1) == '/') {
            Multi(Peek(2) == '=' ? 3 : 2, Peek(2) == '=' ? TokenKind.SlashSlashAssign : TokenKind.SlashSlash);
          } else {
            WithOptionalEquals(TokenKind.Slash, TokenKind.SlashAssign);
          }

          return;
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
          WithOptionalEquals(TokenKind.Assign, TokenKind.EqualEqual);
          return;
        case '!':
          if (Peek(1) == '=') {
            Multi(2, TokenKind.BangEqual);
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

    private void AddToken(TokenKind kind, int start) {
      Tokens.Add(new Token(kind, new SourceSpan(start, _pos - start), _pendingFlags));
      _pendingFlags = TokenFlags.None;
    }

    private void AddMissingToken(TokenKind kind) {
      Tokens.Add(new Token(kind, new SourceSpan(_pos, 0), _pendingFlags | TokenFlags.Missing));
      _pendingFlags = TokenFlags.None;
    }

    private void AddTrivia(TriviaKind kind, int start) {
      Trivia.Add(new Trivia(kind, new SourceSpan(start, _pos - start)));
      _pendingFlags |= kind switch {
        TriviaKind.Newline => TokenFlags.LeadingNewline,
        TriviaKind.Whitespace => TokenFlags.LeadingWhitespace,
        TriviaKind.Comment => TokenFlags.LeadingComment,
        _ => TokenFlags.None,
      };
    }

    // ---------------------------------------------------------------------------------------
    // Strings and interpolation
    // ---------------------------------------------------------------------------------------

    private void BeginString() {
      int start = _pos;
      bool multiline = Peek(1) == '"' && Peek(2) == '"';
      _pos += multiline ? 3 : 1;
      if (multiline && FindInnermostSingleLineStringIndex() >= 0) {
        Diagnostics.ReportError(
          DiagnosticCodes.MultilineStringInSingleLineInterpolation,
          new SourceSpan(start, 3),
          "Multi-line string literal is not allowed inside a single-line string interpolation.");
      }

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
            int quoteCount = 3;
            while (Peek(quoteCount) == '"') {
              quoteCount++;
            }

            // D-090: in a run of 3..5 trailing quotes, the last 3 close the string.
            if (quoteCount <= 5) {
              _pos += quoteCount - 3;
            }

            FlushPart(partStart);
            Multi(3, TokenKind.StringEnd);
            _frames.RemoveAt(_frames.Count - 1);
            return;
          }

          if (c is '\n' or '\r') {
            int singleIndex = FindInnermostSingleLineStringIndex();
            if (singleIndex >= 0) {
              FlushPart(partStart);
              Unwind(singleIndex);
              return;
            }
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

        if (IsDisallowedControlChar(c)) {
          Diagnostics.ReportError(
            DiagnosticCodes.ControlCharacterInLiteral,
            new SourceSpan(_pos, 1),
            $"Unescaped control character '{FormatForMessage(_text.Slice(_pos, 1))}' in string literal; remove it or use a valid escape sequence.");
          _pos++;
          continue;
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

      ReadOnlySpan<char> word = _text.Slice(identStart, _pos - identStart);
      if (s_wordLookup.TryGetValue(word, out _)) {
        Diagnostics.ReportError(
          DiagnosticCodes.KeywordInSimpleInterpolation,
          new SourceSpan(start, _pos - start),
          $"Keyword '{word.ToString()}' cannot be used in simple '$name' interpolation; write '\\$' for a literal dollar, or put an expression in parentheses, such as '$(1 + 2)'.");
        AddToken(TokenKind.Error, identStart);
      } else {
        AddToken(TokenKind.Identifier, identStart);
      }

      AddToken(TokenKind.InterpEnd, _pos);
    }

    private void ScanEscape() {
      int escapeStart = _pos;
      _pos++; // '\'

      if (IsAtLineEnd) {
        // Do not consume the line break: it stays available for recovery / content.
        Diagnostics.ReportError(
          DiagnosticCodes.UnterminatedStringEscape,
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
        DiagnosticCodes.InvalidStringEscape,
        new SourceSpan(escapeStart, _pos - escapeStart),
        $"Invalid escape sequence '{FormatForMessage(_text.Slice(escapeStart, _pos - escapeStart))}' in string literal.");
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
            if (!HasSingleLineStringBelow(i)) {
              return false;
            }
            break;
        }
      }

      return false;
    }

    private readonly int FindInnermostSingleLineStringIndex() {
      for (int i = _frames.Count - 1; i >= 0; i--) {
        if (_frames[i].Kind == FrameKind.SingleString) {
          return i;
        }
      }

      return -1;
    }

    private readonly bool HasSingleLineStringBelow(int index) {
      for (int i = index - 1; i >= 0; i--) {
        if (_frames[i].Kind == FrameKind.SingleString) {
          return true;
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
              DiagnosticCodes.UnterminatedInterpolation,
              new SourceSpan(frame.Start, 2),
              "Unterminated string interpolation: missing ')'.");
            AddMissingToken(TokenKind.InterpEnd);
            break;
          case FrameKind.SingleString:
            Diagnostics.ReportError(
              DiagnosticCodes.UnterminatedString,
              new SourceSpan(frame.Start, _pos - frame.Start),
              "Unterminated string literal: missing closing '\"'.");
            AddMissingToken(TokenKind.StringEnd);
            break;
          case FrameKind.MultilineString:
            Diagnostics.ReportError(
              DiagnosticCodes.UnterminatedMultilineString,
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
        DiagnosticCodes.UnexpectedCharacter,
        span,
        $"Unexpected character '{FormatForMessage(_text.Slice(span.Start, span.Length))}'.");
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

      (_, SourceSpan? intInvalidSep) = ScanDigitRun();
      SourceSpan? fracInvalidSep = null;

      if (Current == '.' && (IsAsciiDigit(Peek(1)) || Peek(1) == '_')) {
        isFloat = true;
        _pos++;
        bool fracHasDigit;
        (fracHasDigit, fracInvalidSep) = ScanDigitRun();
        if (!fracHasDigit) {
          failed = true;
        }
      }

      if (!IsAtEnd && IsAsciiLetter(_text[_pos])) {
        while (!IsAtEnd && IsIdentifierPart(_text[_pos])) {
          _pos++;
        }

        Diagnostics.ReportError(
          DiagnosticCodes.InvalidNumberSuffix,
          new SourceSpan(start, _pos - start),
          $"Invalid number literal '{FormatForMessage(_text.Slice(start, _pos - start))}': unexpected letters after digits.");
        failed = true;
      } else if ((intInvalidSep ?? fracInvalidSep) is SourceSpan invalidSep) {
        Diagnostics.ReportError(
          DiagnosticCodes.InvalidNumericSeparator,
          invalidSep,
          $"Invalid '_' separator in number '{FormatForMessage(_text.Slice(invalidSep.Start, invalidSep.Length))}': separators must sit between digits.");
      }

      AddToken(
        failed ? TokenKind.Error : isFloat ? TokenKind.FloatLiteral : TokenKind.IntLiteral,
        start);
    }

    private (bool HasDigit, SourceSpan? InvalidSeparatorSpan) ScanDigitRun() {
      int start = _pos;
      bool hasDigit = false;
      while (!IsAtEnd && (IsAsciiDigit(_text[_pos]) || _text[_pos] == '_')) {
        if (IsAsciiDigit(_text[_pos])) {
          hasDigit = true;
        }

        _pos++;
      }

      ReadOnlySpan<char> run = _text.Slice(start, _pos - start);
      SourceSpan? invalidSpan = run.Length > 0 && (run[0] == '_' || run[^1] == '_' || run.Contains("__".AsSpan(), StringComparison.Ordinal))
        ? new SourceSpan(start, run.Length)
        : null;
      return (hasDigit, invalidSpan);
    }

    private void ScanRune() {
      int start = _pos;
      _pos++; // opening quote
      bool valid = true;

      if (IsAtLineEnd) {
        Diagnostics.ReportError(DiagnosticCodes.UnterminatedRune, new SourceSpan(start, 1), "Unterminated rune literal.");
        AddToken(TokenKind.Error, start);
        return;
      }

      if (Current == '\'') {
        _pos++;
        Diagnostics.ReportError(DiagnosticCodes.EmptyRune, new SourceSpan(start, 2), "Empty rune literal.");
        AddToken(TokenKind.Error, start);
        return;
      }

      if (Current == '\\') {
        int escapeStart = _pos;
        _pos++;

        if (IsAtLineEnd) {
          Diagnostics.ReportError(
            DiagnosticCodes.UnterminatedRune,
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
            DiagnosticCodes.InvalidRuneEscape,
            new SourceSpan(escapeStart, _pos - escapeStart),
            $"Invalid escape sequence '{FormatForMessage(_text.Slice(escapeStart, _pos - escapeStart))}' in rune literal.");
        }
      } else {
        int runeStart = _pos;
        AdvanceCodePoint();
        if (_pos - runeStart == 1 && char.IsSurrogate(_text[runeStart])) {
          valid = false;
          Diagnostics.ReportError(
            DiagnosticCodes.InvalidRuneSurrogate,
            new SourceSpan(runeStart, 1),
            $"Invalid unpaired UTF-16 surrogate '{FormatForMessage(_text.Slice(runeStart, 1))}' in rune literal.");
        } else if (_pos - runeStart == 1 && IsDisallowedControlChar(_text[runeStart])) {
          valid = false;
          Diagnostics.ReportError(
            DiagnosticCodes.ControlCharacterInLiteral,
            new SourceSpan(runeStart, 1),
            $"Unescaped control character '{FormatForMessage(_text.Slice(runeStart, 1))}' in rune literal; remove it or use a valid escape sequence.");
        }
      }

      if (!IsAtEnd && Current == '\'') {
        _pos++;
        AddToken(valid ? TokenKind.RuneLiteral : TokenKind.Error, start);
        return;
      }

      int probe = _pos;
      while (probe < _text.Length && _text[probe] != '\'' && _text[probe] is not ('\n' or '\r')) {
        if (_text[probe] == '\\' && probe + 1 < _text.Length && _text[probe + 1] is not ('\n' or '\r')) {
          probe += 2;
        } else {
          probe++;
        }
      }

      if (probe < _text.Length && _text[probe] == '\'') {
        _pos = probe + 1;
        if (valid) {
          Diagnostics.ReportError(
            DiagnosticCodes.MultiRuneLiteral,
            new SourceSpan(start, _pos - start),
            "Rune literal must contain exactly one rune.");
        }

        AddToken(TokenKind.Error, start);
        return;
      }

      Diagnostics.ReportError(
        DiagnosticCodes.UnterminatedRune,
        new SourceSpan(start, _pos - start),
        "Unterminated rune literal.");
      AddToken(TokenKind.Error, start);
    }

    private static string FormatForMessage(ReadOnlySpan<char> text) {
      bool needsEscape = false;
      for (int i = 0; i < text.Length; i++) {
        if (char.IsControl(text[i]) || IsUnpairedSurrogate(text, i)) {
          needsEscape = true;
          break;
        }
      }

      if (!needsEscape) {
        return text.ToString();
      }

      var sb = new StringBuilder(text.Length + 8);
      for (int i = 0; i < text.Length; i++) {
        char c = text[i];
        if (char.IsControl(c) || IsUnpairedSurrogate(text, i)) {
          sb.Append($"U+{(int) c:X4}");
        } else {
          sb.Append(c);
        }
      }

      return sb.ToString();
    }

    private static bool IsUnpairedSurrogate(ReadOnlySpan<char> text, int index) {
      char c = text[index];
      if (char.IsHighSurrogate(c)) {
        return index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]);
      }

      if (char.IsLowSurrogate(c)) {
        return index == 0 || !char.IsHighSurrogate(text[index - 1]);
      }

      return false;
    }

    private static bool IsDisallowedControlChar(char c) => c <= '\u001F' && c is not ('\t' or '\r' or '\n');

    private static bool IsWhitespace(char c) => c is not ('\n' or '\r') && char.IsWhiteSpace(c);

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    private static bool IsAsciiLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsIdentifierStart(char c) => IsAsciiLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || IsAsciiDigit(c);
  }
}
