using System.Text;

using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.IR.Passes;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Vm;

/// <summary>The outcome of running a Pino program in the sandboxed playground (<see cref="PlaygroundRunner"/>).</summary>
public sealed record PlaygroundResult(string StdOut, string StdErr, int ExitCode) {
  public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Sandboxed runner for the browser/WASM playground: captures stdout and stderr in memory, blocks host
/// filesystem access with <see cref="VmFileErrorKind.PermissionDenied"/>, and enforces a backward-branch
/// step budget so infinite loops terminate cleanly instead of freezing the browser tab.
/// </summary>
public static class PlaygroundRunner {
  public const long DefaultMaxSteps = 2_000_000;

  public const string StepLimitMessage =
      "Program stopped because it ran too many steps (possible infinite loop).";

  public const string FileAccessDeniedMessage =
      "File access is not available in the browser playground.";

  private static readonly DiagnosticRenderOptions s_plainDiagnostics = new() {
    Color = false,
    Voice = DiagnosticVoice.Plain,
  };

  /// <summary>
  /// Compiles and executes <paramref name="code"/> in the playground sandbox, returning captured
  /// stdout, stderr (plain rendered diagnostics, panics, or step-limit errors), and exit code.
  /// </summary>
  public static PlaygroundResult Run(
      string code,
      long maxSteps = DefaultMaxSteps,
      CancellationToken cancellationToken = default) {
    ArgumentNullException.ThrowIfNull(code);

    var source = new SourceText(code, "playground.pino");
    ParseResult parsed = Parser.Parse(source);
    Compilation compilation = Compilation.Create(parsed, NullModuleSource.Instance, cancellationToken);

    var stderrBuilder = new StringBuilder();
    bool hasErrors = false;
    foreach (Diagnostic diag in compilation.RootModel.Diagnostics) {
      if (diag.Severity == DiagnosticSeverity.Error) {
        hasErrors = true;
      }
    }

    if (!compilation.RootModel.Diagnostics.IsEmpty) {
      stderrBuilder.Append(DiagnosticRenderer.RenderAll(compilation.RootModel.Diagnostics, source, s_plainDiagnostics));
    }

    if (hasErrors) {
      return new PlaygroundResult(string.Empty, stderrBuilder.ToString(), 1);
    }

    IrModule module = IrLowerer.Lower(compilation);
    RcInsertPass.Run(module);
    IrVerificationResult verification = IrVerifier.VerifyAfterRc(module);
    if (!verification.IsValid) {
      stderrBuilder.Append("The program could not be run because its IR failed verification: ");
      stderrBuilder.Append(string.Join("; ", verification.Errors.Take(3)));
      stderrBuilder.Append('\n');
      return new PlaygroundResult(string.Empty, stderrBuilder.ToString(), 1);
    }

    var host = new PlaygroundVmHost();
    var vm = new PinoVm(
        module,
        host,
        new PinoVmOptions {
          VerifyRc = false,
          MaxSteps = maxSteps,
          CancellationToken = cancellationToken,
          RootSource = source,
        });

    PinoVmResult result = vm.Run();
    if (result.Reason == PinoVmStopReason.StepLimit) {
      stderrBuilder.Append(StepLimitMessage).Append('\n');
      return new PlaygroundResult(host.StdOut, stderrBuilder.ToString(), 1);
    }

    if (!string.IsNullOrEmpty(host.StdErr)) {
      stderrBuilder.Append(host.StdErr);
    }

    return new PlaygroundResult(host.StdOut, stderrBuilder.ToString(), result.ExitCode);
  }

  /// <summary>
  /// Legacy-compatible playground entry point: returns captured stdout on success, or stderr when
  /// compilation or execution failed.
  /// </summary>
  public static string Evaluate(
      string code,
      long maxSteps = DefaultMaxSteps,
      CancellationToken cancellationToken = default) {
    PlaygroundResult result = Run(code, maxSteps, cancellationToken);
    return result.ExitCode == 0 ? result.StdOut : result.StdErr;
  }

  private sealed class PlaygroundVmFileSystem : IVmFileSystem {
    public static readonly PlaygroundVmFileSystem Instance = new();

    public bool TryReadAllText(string path, out string content, out VmFileError error) {
      content = string.Empty;
      error = new VmFileError(VmFileErrorKind.PermissionDenied, FileAccessDeniedMessage);
      return false;
    }

    public bool TryWriteAllText(string path, string content, out VmFileError error) {
      error = new VmFileError(VmFileErrorKind.PermissionDenied, FileAccessDeniedMessage);
      return false;
    }

    public bool Exists(string path) => false;
  }

  private sealed class PlaygroundVmHost : IVmHost {
    private readonly StringBuilder _out = new();
    private readonly StringBuilder _err = new();

    public PlaygroundVmHost() {
      Out = new StringWriter(_out) { NewLine = "\n" };
      Err = new StringWriter(_err) { NewLine = "\n" };
    }

    public TextWriter Out { get; }

    public TextWriter Err { get; }

    public TextReader In { get; } = new StringReader(string.Empty);

    public IVmFileSystem Fs => PlaygroundVmFileSystem.Instance;

    public Random Random { get; } = new();

    public string StdOut => _out.ToString();

    public string StdErr => _err.ToString();

    public double ClockMs() => IVmHost.UtcNowMs();

    public void Sleep(int milliseconds) {
      // Never block the browser UI thread.
    }

    public void Clear() {
      // No-op in the browser playground.
    }
  }
}
