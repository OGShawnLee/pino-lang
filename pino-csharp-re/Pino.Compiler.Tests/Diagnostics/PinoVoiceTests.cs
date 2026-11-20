using System.Reflection;

using Pino.Compiler.Diagnostics;

namespace Pino.Compiler.Tests.Diagnostics;

public sealed class PinoVoiceTests {
  [Fact]
  public void AllNoteKeysExistInDiagnosticCodesAndValuesAreNonEmpty() {
    HashSet<string> validCodes = typeof(DiagnosticCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(static f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
        .Select(static f => (string) f.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    foreach ((string code, string note) in PinoVoice.Notes) {
      Assert.Contains(code, validCodes);
      Assert.False(string.IsNullOrWhiteSpace(note), $"Note for '{code}' must not be empty.");
    }
  }
}
