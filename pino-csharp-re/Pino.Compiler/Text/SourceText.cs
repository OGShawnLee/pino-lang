namespace Pino.Compiler.Text;

public sealed class SourceText {
  private readonly int[] _lineStarts;

  public SourceText(string text, string? filePath = null) {
    ArgumentNullException.ThrowIfNull(text);
    Text = text;
    FilePath = filePath;
    _lineStarts = ComputeLineStarts(text);
  }

  public string Text { get; }

  public string? FilePath { get; }

  public int Length => Text.Length;

  public int LineCount => _lineStarts.Length;

  public ReadOnlySpan<char> AsSpan() => Text.AsSpan();

  public ReadOnlySpan<char> AsSpan(SourceSpan span) {
    ValidateSpan(span);
    return Text.AsSpan(span.Start, span.Length);
  }

  public LinePosition GetLineColumn(int offset) {
    ArgumentOutOfRangeException.ThrowIfNegative(offset);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, Text.Length);

    int index = Array.BinarySearch(_lineStarts, offset);
    int lineIndex = index >= 0 ? index : (~index) - 1;
    return new LinePosition(lineIndex + 1, offset - _lineStarts[lineIndex] + 1);
  }

  public LinePositionSpan GetLineSpan(SourceSpan span) {
    ValidateSpan(span);
    return new LinePositionSpan(GetLineColumn(span.Start), GetLineColumn(span.End));
  }

  private void ValidateSpan(SourceSpan span) {
    ArgumentOutOfRangeException.ThrowIfNegative(span.Start);
    ArgumentOutOfRangeException.ThrowIfNegative(span.Length);

    if (span.Start > Text.Length - span.Length) {
      throw new ArgumentOutOfRangeException(
          nameof(span),
          span,
          $"SourceSpan [{span.Start}, {span.Start + (long) span.Length}) is out of bounds for text length {Text.Length}.");
    }
  }

  private static int[] ComputeLineStarts(string text) {
    var lineStarts = new List<int> { 0 };

    for (int i = 0; i < text.Length; i++) {
      char ch = text[i];
      if (ch == '\r') {
        if (i + 1 < text.Length && text[i + 1] == '\n') {
          i++;
        }

        lineStarts.Add(i + 1);
      } else if (ch == '\n') {
        lineStarts.Add(i + 1);
      }
    }

    return [.. lineStarts];
  }
}
