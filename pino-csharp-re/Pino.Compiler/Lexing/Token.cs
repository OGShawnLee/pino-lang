using Pino.Compiler.Text;

namespace Pino.Compiler.Lexing;

public readonly record struct Token {
  public Token(TokenKind kind, SourceSpan span, TokenFlags flags = TokenFlags.None) {
    Kind = kind;
    Flags = flags;
    Span = span;
  }

  public TokenKind Kind { get; init; }

  public TokenFlags Flags { get; init; }

  public SourceSpan Span { get; init; }
}
