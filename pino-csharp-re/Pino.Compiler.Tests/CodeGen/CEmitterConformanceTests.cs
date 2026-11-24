using Pino.Compiler.Checking;
using Pino.Compiler.CodeGen.C;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.IR.Passes;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Checking;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.CodeGen;

/// <summary>
/// Compiles and executes 100% of <c>conformance/</c> <c>.out</c> cases and
/// <see cref="CheckerConformanceTests.RuntimeErrCases"/> via TCC with <c>-DPINO_DEBUG_RC</c>
/// and <c>PINO_FAIL_ON_LEAK=1</c> (zero skip list).
/// </summary>
public sealed class CEmitterConformanceTests {
  private static readonly string s_repoRoot = ParserTestHelpers.FindRepositoryRoot();
  private static readonly string s_conformanceRoot = Path.Combine(s_repoRoot, "conformance");

  private static readonly HashSet<string> s_mainFailureCases = new(StringComparer.OrdinalIgnoreCase) {
    "errors/main-bubble.pino",
    "errors/main-failure.pino",
    "errors/main-none.pino",
  };

  public static TheoryData<string> PositiveCases() => CheckerConformanceTests.PositiveCases();

  public static TheoryData<string> RuntimeErrCases() {
    var data = new TheoryData<string>();
    foreach (string relPath in CheckerConformanceTests.RuntimeErrCases.Keys
        .OrderBy(static k => k, StringComparer.OrdinalIgnoreCase)) {
      data.Add(relPath);
    }

    return data;
  }

  [Theory]
  [MemberData(nameof(PositiveCases))]
  public void PositiveConformanceCase_CompilesRunsAndMatchesOutWithZeroLeaks(string relativePath) {
    string fullPath = Path.Combine(s_conformanceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    string outPath = Path.ChangeExtension(fullPath, ".out");
    string expected = Normalize(File.ReadAllText(outPath));

    string tempDir = Path.Combine(Path.GetTempPath(), "pino-re-conf-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    try {
      (CBuildResult build, CExecResult? exec) = CompileAndRunCase(fullPath, tempDir);
      Assert.True(
          build.Succeeded,
          $"{relativePath}: TCC build failed ({build.CompilerUsed}):\n{build.Stderr}\n{build.Stdout}");

      CExecResult run = exec!;
      Assert.False(run.TimedOut, $"{relativePath}: execution timed out.");
      Assert.True(
          run.ExitCode == 0,
          $"{relativePath}: expected exit 0, got {run.ExitCode}.\n--- stdout ---\n{run.Stdout}\n--- stderr ---\n{run.Stderr}");
      Assert.DoesNotContain("LEAK:", run.Stderr, StringComparison.Ordinal);
      Assert.Equal(expected, Normalize(run.Stdout));
    } finally {
      TryDeleteDir(tempDir);
    }
  }

  [Theory]
  [MemberData(nameof(RuntimeErrCases))]
  public void RuntimeErrConformanceCase_ExitsWithExpectedCodeAndMessage(string relativePath) {
    string fullPath = Path.Combine(s_conformanceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    string errPath = Path.ChangeExtension(fullPath, ".err");
    string expectedErr = Normalize(File.ReadAllText(errPath));

    string tempDir = Path.Combine(Path.GetTempPath(), "pino-re-conferr-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    try {
      (CBuildResult build, CExecResult? exec) = CompileAndRunCase(fullPath, tempDir);
      Assert.True(
          build.Succeeded,
          $"{relativePath}: TCC build failed ({build.CompilerUsed}):\n{build.Stderr}\n{build.Stdout}");

      CExecResult run = exec!;
      Assert.False(run.TimedOut, $"{relativePath}: execution timed out.");

      int expectedExit = s_mainFailureCases.Contains(relativePath) ? 1 : 101;
      Assert.True(
          run.ExitCode == expectedExit,
          $"{relativePath}: expected exit {expectedExit}, got {run.ExitCode}.\n--- stdout ---\n{run.Stdout}\n--- stderr ---\n{run.Stderr}");

      if (string.Equals(expectedErr, "exit code 101", StringComparison.Ordinal)) {
        Assert.Equal(101, run.ExitCode);
      } else {
        Assert.Contains(expectedErr, run.Stderr, StringComparison.Ordinal);
      }
    } finally {
      TryDeleteDir(tempDir);
    }
  }

  private static (CBuildResult Build, CExecResult? Exec) CompileAndRunCase(string pinoPath, string tempDir) {
    string content = File.ReadAllText(pinoPath);
    ParseResult parsed = Parser.Parse(new SourceText(content, pinoPath));
    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
    Diagnostic[] errors = [.. compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)];
    Assert.Empty(errors);

    IrModule ir = IrLowerer.Lower(compilation);
    RcInsertPass.Run(ir);
    string cSource = CEmitter.Emit(ir, compilation);

    string exePath = Path.Combine(tempDir, OperatingSystem.IsWindows() ? "case.exe" : "case");
    CBuildResult build = CCompilerDriver.Build(new CBuildOptions(
        CSource: cSource,
        OutputExecutablePath: exePath,
        Release: false,
        DebugRc: true,
        RepoRoot: s_repoRoot));

    if (!build.Succeeded) {
      return (build, null);
    }

    CExecResult exec = CCompilerDriver.RunExecutable(
        exePath,
        workingDirectory: tempDir,
        failOnLeak: true,
        closeStdin: true,
        timeout: TimeSpan.FromSeconds(15));
    return (build, exec);
  }

  private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

  private static void TryDeleteDir(string path) {
    try {
      Directory.Delete(path, recursive: true);
    } catch (IOException) {
    } catch (UnauthorizedAccessException) {
    }
  }
}
