using System.Collections.Frozen;

namespace Pino.Compiler.Diagnostics;

public static class PinoVoice {
  // Keyed by diagnostic code (e.g. "PN2001"). The owner writes these lines; ship it empty.
  public static FrozenDictionary<string, string> Notes { get; } =
    FrozenDictionary<string, string>.Empty;
}
