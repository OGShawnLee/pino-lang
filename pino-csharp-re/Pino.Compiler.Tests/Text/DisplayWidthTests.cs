using System.Text;

using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Text;

public sealed class DisplayWidthTests {
  [Theory]
  [InlineData(0x0301)] // Combining Acute Accent (NonSpacingMark)
  [InlineData(0x20DD)] // Combining Enclosing Circle (EnclosingMark)
  [InlineData(0x200D)] // Zero Width Joiner (Format)
  public void ZeroWidthCodePointsReturnZero(int codePoint) {
    Assert.Equal(0, DisplayWidth.GetWidth(new Rune(codePoint)));
  }

  [Theory]
  [InlineData(0x1100)] // Hangul Jamo
  [InlineData(0x4E16)] // '世' (CJK)
  [InlineData(0xAC00)] // '가' (Hangul Syllables)
  [InlineData(0xF900)] // CJK Compatibility Ideographs
  [InlineData(0xFE30)] // CJK Compatibility Forms
  [InlineData(0xFF01)] // Fullwidth Exclamation Mark
  [InlineData(0xFFE0)] // Fullwidth Cent Sign
  [InlineData(0x1F600)] // '😀' (Emoticons, surrogate pair)
  [InlineData(0x1F980)] // '🦀' (Supplemental Symbols and Pictographs, surrogate pair)
  [InlineData(0x20000)] // CJK Unified Ideographs Extension B (surrogate pair)
  public void WideCodePointsReturnTwo(int codePoint) {
    Assert.Equal(2, DisplayWidth.GetWidth(new Rune(codePoint)));
  }

  [Theory]
  [InlineData('a')]
  [InlineData('\u00E9')] // 'é'
  [InlineData('\u2605')] // '★'
  [InlineData('\uFFFD')] // Replacement character
  public void NarrowCodePointsReturnOne(char ch) {
    Assert.Equal(1, DisplayWidth.GetWidth(new Rune(ch)));
  }

  [Fact]
  public void StringWidthSumsCodePointsAndHandlesSurrogatePairsAndCombiningMarks() {
    Assert.Equal(7, DisplayWidth.GetStringWidth("e\u0301世界😀"));
  }
}
