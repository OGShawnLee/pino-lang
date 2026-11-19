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

  /// <summary>
  /// Returns the slice of <see cref="Trivia"/> immediately preceding <see cref="Tokens"/>[<paramref name="tokenIndex"/>]
  /// (between the previous token's end and this token's start) using binary search.
  /// </summary>
  public ReadOnlySpan<Trivia> GetLeadingTrivia(int tokenIndex) {
    ArgumentOutOfRangeException.ThrowIfNegative(tokenIndex);
    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(tokenIndex, Tokens.Length);

    int startOffset = tokenIndex == 0 ? 0 : Tokens[tokenIndex - 1].Span.End;
    int endOffset = Tokens[tokenIndex].Span.Start;
    if (Trivia.IsEmpty || startOffset >= endOffset) {
      return ReadOnlySpan<Trivia>.Empty;
    }

    int first = LowerBound(Trivia, startOffset);
    int after = LowerBound(Trivia, endOffset);
    return Trivia.AsSpan(first, after - first);
  }

  private static int LowerBound(ImmutableArray<Trivia> trivia, int offset) {
    int lo = 0;
    int hi = trivia.Length;
    while (lo < hi) {
      int mid = lo + ((hi - lo) / 2);
      if (trivia[mid].Span.Start < offset) {
        lo = mid + 1;
      } else {
        hi = mid;
      }
    }

    return lo;
  }
}
