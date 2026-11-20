using System.Globalization;
using System.Text;

namespace Pino.Compiler.Text;

internal static class DisplayWidth {
  public static int GetWidth(Rune rune) {
    UnicodeCategory category = Rune.GetUnicodeCategory(rune);
    if (category is UnicodeCategory.NonSpacingMark
        or UnicodeCategory.EnclosingMark
        or UnicodeCategory.Format) {
      return 0;
    }

    int cp = rune.Value;
    if (cp is (>= 0x1100 and <= 0x115F)
        or (>= 0x2E80 and <= 0xA4CF)
        or (>= 0xAC00 and <= 0xD7A3)
        or (>= 0xF900 and <= 0xFAFF)
        or (>= 0xFE30 and <= 0xFE4F)
        or (>= 0xFF00 and <= 0xFF60)
        or (>= 0xFFE0 and <= 0xFFE6)
        or (>= 0x1F300 and <= 0x1F64F)
        or (>= 0x1F900 and <= 0x1F9FF)
        or (>= 0x20000 and <= 0x3FFFD)) {
      return 2;
    }

    return 1;
  }

  public static int GetStringWidth(ReadOnlySpan<char> text) {
    int width = 0;
    int index = 0;
    while (index < text.Length) {
      Rune.DecodeFromUtf16(text[index..], out Rune rune, out int charsConsumed);
      width += GetWidth(rune);
      index += charsConsumed;
    }

    return width;
  }
}
