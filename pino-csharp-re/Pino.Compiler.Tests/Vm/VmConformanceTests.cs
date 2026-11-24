using Pino.Compiler.Tests.Checking;
using Pino.Vm;

using Xunit.Abstractions;

namespace Pino.Compiler.Tests.Vm;

/// <summary>
/// Runs every <c>conformance/**/*.out</c> case and every runtime <c>.err</c> case on <see cref="PinoVm"/> with shadow-RC
/// verification on (<c>design.md</c> §4.3, §8). There is no skip list: every case must pass.
/// </summary>
public sealed class VmConformanceTests(ITestOutputHelper output) {
  /// <summary>Runtime <c>.err</c> cases where <c>main</c> returned a failure (exit code 1); every other case panics (101).</summary>
  private static readonly HashSet<string> s_mainFailureCases = new(StringComparer.OrdinalIgnoreCase) {
    "errors/main-bubble.pino",
    "errors/main-failure.pino",
    "errors/main-none.pino",
  };

  public static TheoryData<string> OutCases() {
    var data = new TheoryData<string>();
    foreach (string file in Directory.EnumerateFiles(VmTestHarness.ConformanceRoot, "*.pino", SearchOption.AllDirectories)
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      if (File.Exists(Path.ChangeExtension(file, ".out"))) {
        data.Add(ToRelative(file));
      }
    }

    return data;
  }

  public static TheoryData<string> ErrCases() {
    var data = new TheoryData<string>();
    foreach (string rel in CheckerConformanceTests.RuntimeErrCases.Keys.OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      data.Add(rel);
    }

    return data;
  }

  [Theory]
  [MemberData(nameof(OutCases))]
  public void OutCase_MatchesStdoutWithNoNonCycleLeaks(string relativePath) {
    string failure = RunOutCase(relativePath);
    Assert.True(failure.Length == 0, failure);
  }

  [Theory]
  [MemberData(nameof(ErrCases))]
  public void RuntimeErrCase_FailsWithExpectedTextAndExitCode(string relativePath) {
    string failure = RunErrCase(relativePath);
    Assert.True(failure.Length == 0, failure);
  }

  [Fact]
  public void Sweep_Runs100PercentOfOutAndRuntimeErrCasesWithShadowRc() {
    var failures = new List<string>();
    int outTotal = 0;
    int outPassed = 0;
    int errTotal = 0;
    int errPassed = 0;
    int plainLeaks = 0;
    int cycleLeaks = 0;

    foreach (string file in Directory.EnumerateFiles(VmTestHarness.ConformanceRoot, "*.pino", SearchOption.AllDirectories)
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      if (!File.Exists(Path.ChangeExtension(file, ".out"))) {
        continue;
      }

      outTotal++;
      string rel = ToRelative(file);
      string failure = RunOutCase(rel, out int leaks, out int cycles);
      plainLeaks += leaks;
      cycleLeaks += cycles;
      if (failure.Length == 0) {
        outPassed++;
      } else {
        failures.Add(failure);
      }
    }

    foreach (string rel in CheckerConformanceTests.RuntimeErrCases.Keys.OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      errTotal++;
      string failure = RunErrCase(rel);
      if (failure.Length == 0) {
        errPassed++;
      } else {
        failures.Add(failure);
      }
    }

    output.WriteLine($"VM sweep: .out {outPassed}/{outTotal}, .err {errPassed}/{errTotal}, shadow-RC leaks {plainLeaks}, CYCLE-LEAKs {cycleLeaks}.");
    Assert.True(
        failures.Count == 0,
        $"VM sweep: .out {outPassed}/{outTotal}, .err {errPassed}/{errTotal}, leaks {plainLeaks}, cycle leaks {cycleLeaks}.\n"
        + string.Join("\n\n", failures.Take(25)));
    Assert.True(outTotal >= 421, $"Expected at least 421 .out cases, found {outTotal}.");
    Assert.Equal(CheckerConformanceTests.RuntimeErrCases.Count, errTotal);
    Assert.Equal(0, plainLeaks);
  }

  private static string RunOutCase(string relativePath) => RunOutCase(relativePath, out _, out _);

  private static string RunOutCase(string relativePath, out int plainLeaks, out int cycleLeaks) {
    plainLeaks = 0;
    cycleLeaks = 0;
    string fullPath = Path.Combine(VmTestHarness.ConformanceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    string expected = VmTestHarness.Normalize(File.ReadAllText(Path.ChangeExtension(fullPath, ".out")));
    VmRun run;
    try {
      run = VmTestHarness.RunFile(fullPath);
    } catch (Exception ex) {
      return $"{relativePath}: {ex.GetType().Name}: {ex.Message}";
    }

    PinoVmResult result = run.Result;
    if (result.LeakReport is { } report) {
      plainLeaks = report.Leaks.Count;
      cycleLeaks = report.CycleLeaks.Count;
    }

    if (result.RcError is not null) {
      return $"{relativePath}: shadow-RC error: {result.RcError}";
    }

    if (result.ExitCode != 0) {
      return $"{relativePath}: exited with code {result.ExitCode}; stderr:\n{run.Stderr}";
    }

    string actual = VmTestHarness.Normalize(run.Stdout);
    if (actual != expected) {
      return $"{relativePath}: stdout mismatch.\n--- expected ---\n{expected}\n--- actual ---\n{actual}";
    }

    if (result.LeakReport is { IsClean: false } leaky) {
      return $"{relativePath}: shadow-RC leaks:\n{leaky.Format()}";
    }

    return string.Empty;
  }

  private static string RunErrCase(string relativePath) {
    string fullPath = Path.Combine(VmTestHarness.ConformanceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    string expected = VmTestHarness.Normalize(File.ReadAllText(Path.ChangeExtension(fullPath, ".err")));
    VmRun run;
    try {
      run = VmTestHarness.RunFile(fullPath);
    } catch (Exception ex) {
      return $"{relativePath}: {ex.GetType().Name}: {ex.Message}";
    }

    if (run.Result.RcError is not null) {
      return $"{relativePath}: shadow-RC error: {run.Result.RcError}";
    }

    int expectedExit = s_mainFailureCases.Contains(relativePath) ? 1 : 101;
    if (run.Result.ExitCode != expectedExit) {
      return $"{relativePath}: expected exit code {expectedExit} but got {run.Result.ExitCode}; stderr:\n{run.Stderr}";
    }

    string message = $"exit code {run.Result.ExitCode}\nstdout:\n{run.Stdout}\nstderr:\n{run.Stderr}";
    if (!message.Contains(expected, StringComparison.Ordinal)) {
      return $"{relativePath}: failure message does not contain \"{expected}\". Actual message:\n{message}";
    }

    return string.Empty;
  }

  private static string ToRelative(string fullPath) =>
      Path.GetRelativePath(VmTestHarness.ConformanceRoot, fullPath).Replace('\\', '/');
}
