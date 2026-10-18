namespace Pino;

public record struct PinoRune(int CodePoint) {
  public override string ToString() {
    if (CodePoint >= 0 && CodePoint <= 0xFFFF) {
      return ((char)CodePoint).ToString();
    }
    if (CodePoint > 0xFFFF && CodePoint <= 0x10FFFF) {
      return char.ConvertFromUtf32(CodePoint);
    }
    return "\uFFFD";
  }
}
