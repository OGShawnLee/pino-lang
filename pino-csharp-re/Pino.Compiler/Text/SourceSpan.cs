namespace Pino.Compiler.Text;

public readonly record struct SourceSpan(int Start, int Length) {
  public static SourceSpan Empty => default;

  public int End => Start + Length;

  public bool IsEmpty => Length == 0;

  public bool Contains(int offset) => offset >= Start && offset < End;

  public bool Contains(SourceSpan other) => other.Start >= Start && other.End <= End;

  public static SourceSpan FromBounds(int start, int end) {
    ArgumentOutOfRangeException.ThrowIfNegative(start);
    ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
    return new SourceSpan(start, end - start);
  }

  public SourceSpan Merge(SourceSpan other) {
    int start = Math.Min(Start, other.Start);
    int end = Math.Max(End, other.End);
    return FromBounds(start, end);
  }
}
