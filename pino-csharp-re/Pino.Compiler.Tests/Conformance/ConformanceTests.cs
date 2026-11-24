using System.Text;

namespace Pino.Compiler.Tests.Conformance;

/// <summary>
/// Unified conformance test suite for the new compiler (<see cref="ConformanceEngine.ReVm"/> and
/// <see cref="ConformanceEngine.ReC"/>). Each case runs at most once per engine per test run and
/// verifies exact <c>.out</c> stdout, <c>.err</c> diagnostics/exit codes, zero RC leaks on both
/// engines, and identical ReVm-vs-ReC output.
/// </summary>
public sealed class ConformanceTests {
  public static TheoryData<string> AllCases() {
    var data = new TheoryData<string>();
    foreach (string casePath in ConformanceRunner.DiscoverCases()) {
      data.Add(casePath);
    }

    return data;
  }

  [Theory]
  [MemberData(nameof(AllCases))]
  public void RunCase(string caseRelativePath) {
    PreparedCase prepared = ConformanceRunner.PrepareCase(caseRelativePath);
    var failures = new List<string>();
    var runs = new Dictionary<ConformanceEngine, EngineRun>();
    var hasKnownFailure = new HashSet<ConformanceEngine>();

    foreach (ConformanceEngine engine in Enum.GetValues<ConformanceEngine>()) {
      KnownFailure? known = ConformanceRunner.FindKnownFailure(engine, caseRelativePath);
      if (known is not null) {
        hasKnownFailure.Add(engine);
      }

      if (known is { IsCrash: true }) {
        continue; // crash-marked: skip running so the test host cannot crash
      }

      EngineRun run = ConformanceRunner.Run(engine, prepared);
      runs[engine] = run;
      CaseVerdict verdict = ConformanceRunner.Judge(caseRelativePath, run);

      if (known is not null) {
        if (verdict.Passed) {
          failures.Add($"Known failure now passes: remove '{known.Line}' from {known.File}");
        }

        continue;
      }

      if (!verdict.Passed) {
        failures.Add($"[{engine}] {caseRelativePath}: {verdict.Detail}");
      }
    }

    // Differential check: when both engines ran with no known failures on an executable case,
    // verify identical exit code and normalized stdout.
    if (failures.Count == 0
        && prepared.CompileTimeOutcome is null
        && hasKnownFailure.Count == 0
        && runs.TryGetValue(ConformanceEngine.ReVm, out EngineRun? vmRun)
        && runs.TryGetValue(ConformanceEngine.ReC, out EngineRun? cRun)) {
      if (vmRun.ExitCode != cRun.ExitCode) {
        failures.Add(
            $"[Differential] {caseRelativePath}: exit code mismatch (ReVm={vmRun.ExitCode}, ReC={cRun.ExitCode}).");
      }

      string vmOut = ConformanceRunner.Normalize(vmRun.Stdout);
      string cOut = ConformanceRunner.Normalize(cRun.Stdout);
      if (!string.Equals(vmOut, cOut, StringComparison.Ordinal)) {
        failures.Add(
            $"[Differential] {caseRelativePath}: stdout mismatch between ReVm and ReC.\n--- ReVm ---\n{vmOut}\n--- ReC ---\n{cOut}");
      }
    }

    Assert.True(failures.Count == 0, string.Join("\n\n", failures));
  }

  [Fact]
  public void RuntimeErrCasesAreNonStale() {
    List<string> allCases = ConformanceRunner.DiscoverCases();
    int outCount = allCases.Count(static c => File.Exists(ConformanceRunner.ExpectedPath(c, ".out")));
    Assert.True(outCount >= 421, $"Expected at least 421 .out conformance cases, found {outCount}.");

    var allNegative = new HashSet<string>(
        allCases.Where(static c => File.Exists(ConformanceRunner.ExpectedPath(c, ".err"))),
        StringComparer.OrdinalIgnoreCase);

    foreach ((string runtimeCase, string reason) in ConformanceRunner.RuntimeErrCases) {
      Assert.True(allNegative.Contains(runtimeCase), $"Stale RuntimeErrCases entry: {runtimeCase}");
      Assert.False(string.IsNullOrWhiteSpace(reason), $"Missing reason for RuntimeErrCases entry: {runtimeCase}");
    }
  }

  /// <summary>
  /// Regenerates <c>conformance/STATUS.md</c>. Only runs when <c>PINO_CONFORMANCE_STATUS=1</c>.
  /// Counts a case as passed only when the engine meets the expectation (known failures count as not passed).
  /// </summary>
  [Fact]
  public void GenerateStatusReport() {
    if (Environment.GetEnvironmentVariable("PINO_CONFORMANCE_STATUS") != "1") {
      return;
    }

    ConformanceEngine[] engines = Enum.GetValues<ConformanceEngine>();
    var passed = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
    var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);

    foreach (string casePath in ConformanceRunner.DiscoverCases()) {
      string feature = ConformanceRunner.FeatureOf(casePath);
      if (!passed.ContainsKey(feature)) {
        passed[feature] = new int[engines.Length];
        totals[feature] = 0;
      }

      totals[feature]++;
      PreparedCase prepared = ConformanceRunner.PrepareCase(casePath);
      for (int i = 0; i < engines.Length; i++) {
        KnownFailure? known = ConformanceRunner.FindKnownFailure(engines[i], casePath);
        if (known is { IsCrash: true }) {
          continue;
        }

        EngineRun run = ConformanceRunner.Run(engines[i], prepared);
        if (ConformanceRunner.Judge(casePath, run).Passed) {
          passed[feature][i]++;
        }
      }
    }

    var sb = new StringBuilder();
    sb.Append("# Conformance Status\n\n");
    sb.Append("Generated by `GenerateStatusReport` (see `conformance/README.md`). Cells are `passed/total`; known failures count as not passed.\n\n");
    sb.Append("| Feature | ReVm | ReC |\n");
    sb.Append("| :--- | :--- | :--- |\n");
    foreach (string feature in passed.Keys) {
      sb.Append($"| {feature} ");
      for (int i = 0; i < engines.Length; i++) {
        sb.Append($"| {passed[feature][i]}/{totals[feature]} ");
      }

      sb.Append("|\n");
    }

    File.WriteAllText(Path.Combine(ConformanceRunner.ConformanceDir, "STATUS.md"), sb.ToString(), new UTF8Encoding(false));
  }
}
