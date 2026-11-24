using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

using Pino.Vm;

namespace Pino.Wasm;

/// <summary>JS-exported bridge for the Pino web playground, backed by <see cref="PlaygroundRunner"/>.</summary>
[SupportedOSPlatform("browser")]
public static partial class Playground {
  /// <summary>
  /// Compiles and executes <paramref name="code"/> in the playground sandbox, returning a JSON object
  /// <c>{ "stdout": "...", "stderr": "...", "exitCode": 0 }</c>.
  /// </summary>
  [JSExport]
  public static string Run(string code) {
    PlaygroundResult result = PlaygroundRunner.Run(code);
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream)) {
      writer.WriteStartObject();
      writer.WriteString("stdout", result.StdOut);
      writer.WriteString("stderr", result.StdErr);
      writer.WriteNumber("exitCode", result.ExitCode);
      writer.WriteEndObject();
    }

    return Encoding.UTF8.GetString(stream.ToArray());
  }

  /// <summary>
  /// Legacy-compatible entry point: returns stdout on success, or stderr when the program failed.
  /// </summary>
  [JSExport]
  public static string Evaluate(string code) => PlaygroundRunner.Evaluate(code);
}
