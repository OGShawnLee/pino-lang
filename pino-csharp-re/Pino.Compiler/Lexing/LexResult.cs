using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Text;

namespace Pino.Compiler.Lexing;

/// <summary>
/// Result of lexing a source file. <see cref="Tokens"/> never contains trivia (so the parser
/// can iterate it directly) and always ends with <see cref="TokenKind.EOF"/>. Tokens and
/// <see cref="Trivia"/> are each ordered by position and together cover every character of
/// <see cref="Source"/> exactly once, so LSP/formatter tooling can reconstruct the full text.
/// </summary>
public sealed record LexResult(
    SourceText Source,
    ImmutableArray<Token> Tokens,
    ImmutableArray<Trivia> Trivia,
    ImmutableArray<Diagnostic> Diagnostics) {
  public ReadOnlySpan<char> GetText(Token token) => Source.AsSpan(token.Span);

  public ReadOnlySpan<char> GetText(Trivia trivia) => Source.AsSpan(trivia.Span);
}
