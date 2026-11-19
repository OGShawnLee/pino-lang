using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Text;

public sealed class SourceTextTests {
  [Fact]
  public void ConstructorThrowsOnNullText() {
    Assert.Throws<ArgumentNullException>(() => new SourceText(null!));
  }

  [Fact]
  public void EmptyTextHasSingleLineAndMapsZeroEOF() {
    var source = new SourceText(string.Empty, "empty.pino");

    Assert.Equal(0, source.Length);
    Assert.Equal(1, source.LineCount);
    Assert.Equal("empty.pino", source.FilePath);
    Assert.True(source.AsSpan().IsEmpty);
    Assert.Equal(new LinePosition(1, 1), source.GetLineColumn(0));
    Assert.Equal(
        new LinePositionSpan(new LinePosition(1, 1), new LinePosition(1, 1)),
        source.GetLineSpan(SourceSpan.Empty));
  }

  [Fact]
  public void LineFeedOffsetsMapToOneBasedLineAndColumnIncludingEOF() {
    var source = new SourceText("ab\ncd\ne");

    Assert.Equal(3, source.LineCount);
    Assert.Equal(new LinePosition(1, 1), source.GetLineColumn(0)); // 'a'
    Assert.Equal(new LinePosition(1, 2), source.GetLineColumn(1)); // 'b'
    Assert.Equal(new LinePosition(1, 3), source.GetLineColumn(2)); // '\n'
    Assert.Equal(new LinePosition(2, 1), source.GetLineColumn(3)); // 'c'
    Assert.Equal(new LinePosition(2, 2), source.GetLineColumn(4)); // 'd'
    Assert.Equal(new LinePosition(2, 3), source.GetLineColumn(5)); // '\n'
    Assert.Equal(new LinePosition(3, 1), source.GetLineColumn(6)); // 'e'
    Assert.Equal(new LinePosition(3, 2), source.GetLineColumn(7)); // EOF
  }

  [Fact]
  public void CRLFAndStandaloneCRLineEndingsMapConsistently() {
    var crlf = new SourceText("ab\r\ncd\r\n");
    Assert.Equal(3, crlf.LineCount);
    Assert.Equal(new LinePosition(1, 1), crlf.GetLineColumn(0)); // 'a'
    Assert.Equal(new LinePosition(1, 3), crlf.GetLineColumn(2)); // '\r'
    Assert.Equal(new LinePosition(1, 4), crlf.GetLineColumn(3)); // '\n'
    Assert.Equal(new LinePosition(2, 1), crlf.GetLineColumn(4)); // 'c'
    Assert.Equal(new LinePosition(3, 1), crlf.GetLineColumn(8)); // EOF after trailing CRLF

    var cr = new SourceText("ab\rcd\r");
    Assert.Equal(3, cr.LineCount);
    Assert.Equal(new LinePosition(1, 1), cr.GetLineColumn(0)); // 'a'
    Assert.Equal(new LinePosition(1, 3), cr.GetLineColumn(2)); // '\r'
    Assert.Equal(new LinePosition(2, 1), cr.GetLineColumn(3)); // 'c'
    Assert.Equal(new LinePosition(3, 1), cr.GetLineColumn(6)); // EOF after trailing CR
  }

  [Fact]
  public void TrailingNewlineCreatesEmptyLastLineForEOF() {
    var lf = new SourceText("hello\n");
    var crlf = new SourceText("hello\r\n");

    Assert.Equal(2, lf.LineCount);
    Assert.Equal(2, crlf.LineCount);
    Assert.Equal(new LinePosition(2, 1), lf.GetLineColumn(lf.Length));
    Assert.Equal(new LinePosition(2, 1), crlf.GetLineColumn(crlf.Length));
  }

  [Fact]
  public void MixedLineEndingsArePrecomputedCorrectly() {
    var source = new SourceText("a\r\nb\nc\rd\n\re");

    Assert.Equal(6, source.LineCount);
    Assert.Equal(new LinePosition(1, 1), source.GetLineColumn(0));  // 'a'
    Assert.Equal(new LinePosition(2, 1), source.GetLineColumn(3));  // 'b'
    Assert.Equal(new LinePosition(3, 1), source.GetLineColumn(5));  // 'c'
    Assert.Equal(new LinePosition(4, 1), source.GetLineColumn(7));  // 'd'
    Assert.Equal(new LinePosition(5, 1), source.GetLineColumn(9));  // '\r' after '\n'
    Assert.Equal(new LinePosition(6, 1), source.GetLineColumn(10)); // 'e'
  }

  [Fact]
  public void UTF16SurrogatePairsPreserveLineAndColumnMappingAcrossLineEndings() {
    const string pine = "\U0001F332"; // '🌲' (2 UTF-16 code units)
    var lf = new SourceText($"val t = \"{pine}\"\nval x = 1");
    var crlf = new SourceText($"val t = \"{pine}\"\r\nval x = 1");

    int pineStart = lf.Text.IndexOf(pine, StringComparison.Ordinal);
    var pineSpan = new SourceSpan(pineStart, pine.Length);

    Assert.Equal(2, pine.Length);
    Assert.Equal(pine, lf.AsSpan(pineSpan).ToString());
    Assert.Equal(pine, crlf.AsSpan(pineSpan).ToString());

    LinePositionSpan expectedPineLineSpan = new(new LinePosition(1, 10), new LinePosition(1, 12));
    Assert.Equal(expectedPineLineSpan, lf.GetLineSpan(pineSpan));
    Assert.Equal(expectedPineLineSpan, crlf.GetLineSpan(pineSpan));

    int lfLine2Start = lf.Text.IndexOf("val x", StringComparison.Ordinal);
    int crlfLine2Start = crlf.Text.IndexOf("val x", StringComparison.Ordinal);
    Assert.Equal(new LinePosition(2, 1), lf.GetLineColumn(lfLine2Start));
    Assert.Equal(new LinePosition(2, 1), crlf.GetLineColumn(crlfLine2Start));

    var direct = new SourceText($"{pine}a\nb");
    Assert.Equal(new LinePosition(1, 1), direct.GetLineColumn(0));
    Assert.Equal(new LinePosition(1, 3), direct.GetLineColumn(2)); // 'a' after UTF-16 surrogate pair
    Assert.Equal(new LinePosition(2, 1), direct.GetLineColumn(4)); // 'b'
    Assert.Equal(new LinePosition(2, 2), direct.GetLineColumn(5)); // EOF
  }

  [Fact]
  public void OutOfRangeOffsetsAndSpansThrowArgumentOutOfRangeException() {
    var source = new SourceText("abc");

    Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLineColumn(-1));
    Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLineColumn(4));

    Assert.Throws<ArgumentOutOfRangeException>(() => source.AsSpan(new SourceSpan(-1, 1)));
    Assert.Throws<ArgumentOutOfRangeException>(() => source.AsSpan(new SourceSpan(0, -1)));
    Assert.Throws<ArgumentOutOfRangeException>(() => source.AsSpan(new SourceSpan(2, 2)));
    Assert.Throws<ArgumentOutOfRangeException>(() => source.AsSpan(new SourceSpan(int.MaxValue, 1)));

    Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLineSpan(new SourceSpan(-1, 0)));
    Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLineSpan(new SourceSpan(3, 1)));
    Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLineSpan(new SourceSpan(int.MaxValue, 2)));
  }

  [Fact]
  public void AsSpanAndGetLineSpanExtractExpectedSlices() {
    var source = new SourceText("fn main() {\n  return 42\n}");
    var span = SourceSpan.FromBounds(14, 23); // "return 42"

    Assert.Equal("return 42", source.AsSpan(span).ToString());
    Assert.Equal(
        new LinePositionSpan(new LinePosition(2, 3), new LinePosition(2, 12)),
        source.GetLineSpan(span));
  }

  [Fact]
  public void SourceSpanOperationsBehaveAsExpected() {
    var left = new SourceSpan(2, 4);   // [2, 6)
    var right = new SourceSpan(8, 3);  // [8, 11)
    var inner = new SourceSpan(3, 2);  // [3, 5)

    Assert.Equal(6, left.End);
    Assert.False(left.IsEmpty);
    Assert.True(SourceSpan.Empty.IsEmpty);

    Assert.True(left.Contains(2));
    Assert.True(left.Contains(5));
    Assert.False(left.Contains(1));
    Assert.False(left.Contains(6));
    Assert.False(SourceSpan.Empty.Contains(0));

    Assert.True(left.Contains(left));
    Assert.True(left.Contains(inner));
    Assert.False(left.Contains(right));

    Assert.Equal(new SourceSpan(2, 9), left.Merge(right));
    Assert.Equal(new SourceSpan(2, 9), right.Merge(left));
    Assert.Equal(left, left.Merge(inner));

    Assert.Throws<ArgumentOutOfRangeException>(() => SourceSpan.FromBounds(-1, 2));
    Assert.Throws<ArgumentOutOfRangeException>(() => SourceSpan.FromBounds(5, 3));
  }

  [Theory]
  [InlineData("")]
  [InlineData("abc")]
  [InlineData("ab\ncd\ne")]
  [InlineData("ab\r\ncd\r\n")]
  [InlineData("ab\rcd\r")]
  [InlineData("a\r\nb\nc\rd\n\re")]
  [InlineData("\U0001F332a\r\nb\U0001F332c")]
  public void GetOffsetAndTryGetOffsetRoundTripEveryOffsetWithGetLineColumn(string text) {
    var source = new SourceText(text);

    for (int offset = 0; offset <= source.Length; offset++) {
      LinePosition pos = source.GetLineColumn(offset);
      Assert.Equal(offset, source.GetOffset(pos));
      Assert.True(source.TryGetOffset(pos, out int tryOffset));
      Assert.Equal(offset, tryOffset);
    }
  }

  [Fact]
  public void GetOffsetClampsOutOfRangePositionsWithoutThrowing() {
    var source = new SourceText("ab\r\ncd");

    Assert.Equal(0, source.GetOffset(new LinePosition(0, 1)));
    Assert.Equal(0, source.GetOffset(new LinePosition(-5, 10)));
    Assert.Equal(0, source.GetOffset(new LinePosition(1, 0)));
    Assert.Equal(0, source.GetOffset(new LinePosition(1, -3)));
    Assert.Equal(3, source.GetOffset(new LinePosition(1, 100)));
    Assert.Equal(4, source.GetOffset(new LinePosition(2, 0)));
    Assert.Equal(source.Length, source.GetOffset(new LinePosition(2, int.MaxValue)));
    Assert.Equal(source.Length, source.GetOffset(new LinePosition(3, 1)));
    Assert.Equal(source.Length, source.GetOffset(new LinePosition(int.MaxValue, 1)));

    Assert.False(source.TryGetOffset(new LinePosition(0, 1), out int off1));
    Assert.Equal(0, off1);
    Assert.False(source.TryGetOffset(new LinePosition(1, 5), out int off2));
    Assert.Equal(3, off2);
    Assert.False(source.TryGetOffset(new LinePosition(3, 1), out int off3));
    Assert.Equal(source.Length, off3);
  }
}
