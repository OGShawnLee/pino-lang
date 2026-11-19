using System.Buffers;

namespace Pino.Compiler.Text;

/// <summary>
/// Immutable source buffer with precomputed line starts. All offsets and spans are 0-based
/// UTF-16 code units, and all <see cref="LinePosition"/> line/column values are 1-based UTF-16
/// code units.
/// </summary>
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

  /// <summary>Total length of <see cref="Text"/> in UTF-16 code units.</summary>
  public int Length => Text.Length;

  public int LineCount => _lineStarts.Length;

  public ReadOnlySpan<char> AsSpan() => Text.AsSpan();

  public ReadOnlySpan<char> AsSpan(SourceSpan span) {
    ValidateSpan(span);
    return Text.AsSpan(span.Start, span.Length);
  }

  /// <summary>
  /// Converts a 0-based UTF-16 code-unit <paramref name="offset"/> into a 1-based UTF-16
  /// <see cref="LinePosition"/>.
  /// </summary>
  public LinePosition GetLineColumn(int offset) {
    ArgumentOutOfRangeException.ThrowIfNegative(offset);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, Text.Length);

    int index = Array.BinarySearch(_lineStarts, offset);
    int lineIndex = index >= 0 ? index : (~index) - 1;
    return new LinePosition(lineIndex + 1, offset - _lineStarts[lineIndex] + 1);
  }

  /// <summary>
  /// Converts a 1-based UTF-16 <see cref="LinePosition"/> to a 0-based UTF-16 code-unit offset
  /// in <see cref="Text"/>. Out-of-range lines and columns are clamped to valid bounds; never throws.
  /// </summary>
  public int GetOffset(LinePosition position) {
    if (position.Line < 1) {
      return 0;
    }

    if (position.Line > _lineStarts.Length) {
      return Text.Length;
    }

    int lineIndex = position.Line - 1;
    int lineStart = _lineStarts[lineIndex];
    int maxOffset = lineIndex + 1 < _lineStarts.Length ? _lineStarts[lineIndex + 1] - 1 : Text.Length;
    if (position.Column <= 1) {
      return lineStart;
    }

    long candidate = (long) lineStart + ((long) position.Column - 1);
    return candidate >= maxOffset ? maxOffset : (int) candidate;
  }

  /// <summary>
  /// Attempts to convert a 1-based UTF-16 <see cref="LinePosition"/> to a 0-based UTF-16 code-unit
  /// offset in <see cref="Text"/>. Returns <see langword="false"/> without throwing if
  /// <paramref name="position"/> is outside the document's valid line/column bounds (setting
  /// <paramref name="offset"/> to the clamped offset).
  /// </summary>
  public bool TryGetOffset(LinePosition position, out int offset) {
    if (position.Line < 1 || position.Line > _lineStarts.Length || position.Column < 1) {
      offset = GetOffset(position);
      return false;
    }

    int lineIndex = position.Line - 1;
    int lineStart = _lineStarts[lineIndex];
    int maxOffset = lineIndex + 1 < _lineStarts.Length ? _lineStarts[lineIndex + 1] - 1 : Text.Length;
    long candidate = (long) lineStart + ((long) position.Column - 1);
    if (candidate > maxOffset) {
      offset = maxOffset;
      return false;
    }

    offset = (int) candidate;
    return true;
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
    int initialCapacity = Math.Max(16, text.Length / 32);
    int[] rented = ArrayPool<int>.Shared.Rent(initialCapacity);
    int count = 0;

    try {
      rented[count++] = 0;

      for (int i = 0; i < text.Length; i++) {
        char ch = text[i];
        if (ch == '\r') {
          if (i + 1 < text.Length && text[i + 1] == '\n') {
            i++;
          }

          if (count == rented.Length) {
            rented = GrowPoolBuffer(rented, count);
          }

          rented[count++] = i + 1;
        } else if (ch == '\n') {
          if (count == rented.Length) {
            rented = GrowPoolBuffer(rented, count);
          }

          rented[count++] = i + 1;
        }
      }

      int[] result = new int[count];
      rented.AsSpan(0, count).CopyTo(result);
      return result;
    } finally {
      ArrayPool<int>.Shared.Return(rented);
    }
  }

  private static int[] GrowPoolBuffer(int[] current, int count) {
    int[] next = ArrayPool<int>.Shared.Rent(current.Length * 2);
    current.AsSpan(0, count).CopyTo(next);
    ArrayPool<int>.Shared.Return(current);
    return next;
  }
}
