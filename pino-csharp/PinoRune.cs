namespace Pino;

public record struct PinoRune(int CodePoint) {
  public override string ToString() {
    return char.ConvertFromUtf32(CodePoint);
  }
}
