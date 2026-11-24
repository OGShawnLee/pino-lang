using System.Text;

namespace Pino.Compiler.Tests.Conformance;

/// <summary>
/// Shared runner logic for sharded conformance test classes (<see cref="ConformanceEngine.ReVm"/> and
/// <see cref="ConformanceEngine.ReC"/>). Each case runs at most once per engine per test run and
/// verifies exact <c>.out</c> stdout, <c>.err</c> diagnostics/exit codes, zero RC leaks on both
/// engines, and identical ReVm-vs-ReC output.
/// </summary>
public abstract class ConformanceShardBase {
  public const int ShardCount = 8;

  public static TheoryData<string> ShardCases(int shardIndex) {
    var data = new TheoryData<string>();
    List<string> all = ConformanceRunner.DiscoverCases();
    for (int i = shardIndex; i < all.Count; i += ShardCount) {
      data.Add(all[i]);
    }

    return data;
  }

  protected static void RunCaseCore(string caseRelativePath) {
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
    // verify identical exit code, normalized stdout, and normalized runtime panic/failure stderr.
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

      if (vmRun.ExitCode != 0 || cRun.ExitCode != 0) {
        string vmErr = NormalizeRuntimeStderr(caseRelativePath, vmRun.Stderr);
        string cErr = NormalizeRuntimeStderr(caseRelativePath, cRun.Stderr);
        if (!string.Equals(vmErr, cErr, StringComparison.Ordinal)) {
          failures.Add(
              $"[Differential] {caseRelativePath}: stderr mismatch between ReVm and ReC.\n--- ReVm ---\n{vmErr}\n--- ReC ---\n{cErr}");
        }
      }
    }

    Assert.True(failures.Count == 0, string.Join("\n\n", failures));
  }

  /// <summary>
  /// Normalizes runtime panic/failure stderr for cross-engine comparison: unifies line endings and
  /// file-path separators, strips the engine-specific backtrace section (open <c>D-081</c>), and
  /// masks the engine-specific regex error detail (<c>D-029</c>).
  /// </summary>
  private static string NormalizeRuntimeStderr(string caseRelativePath, string stderr) {
    string normalized = ConformanceRunner.Normalize(stderr).Replace('\\', '/');
    int btIndex = normalized.IndexOf("\nstack backtrace:", StringComparison.Ordinal);
    if (btIndex >= 0) {
      normalized = normalized[..btIndex];
    } else {
      int frameIndex = normalized.IndexOf("\n  0:", StringComparison.Ordinal);
      if (frameIndex >= 0) {
        normalized = normalized[..frameIndex];
      }
    }

    if (string.Equals(caseRelativePath, "stdlib-regex/err-invalid-pattern.pino", StringComparison.OrdinalIgnoreCase)) {
      int prefixEnd = normalized.IndexOf("': ", StringComparison.Ordinal);
      int lineEnd = prefixEnd >= 0 ? normalized.IndexOf("'\n", prefixEnd, StringComparison.Ordinal) : -1;
      if (prefixEnd >= 0 && lineEnd > prefixEnd) {
        normalized = string.Concat(normalized.AsSpan(0, prefixEnd + 3), "<D-029>.", normalized.AsSpan(lineEnd));
      }
    }

    return normalized.TrimEnd();
  }
}

public sealed class ConformanceShard0Tests : ConformanceShardBase {
  public static TheoryData<string> Cases() => ShardCases(0);

  [Theory]
  [MemberData(nameof(Cases))]
  public void RunCase(string caseRelativePath) => RunCaseCore(caseRelativePath);
}

public sealed class ConformanceShard1Tests : ConformanceShardBase {
  public static TheoryData<string> Cases() => ShardCases(1);

  [Theory]
  [MemberData(nameof(Cases))]
  public void RunCase(string caseRelativePath) => RunCaseCore(caseRelativePath);
}

public sealed class ConformanceShard2Tests : ConformanceShardBase {
  public static TheoryData<string> Cases() => ShardCases(2);

  [Theory]
  [MemberData(nameof(Cases))]
  public void RunCase(string caseRelativePath) => RunCaseCore(caseRelativePath);
}

public sealed class ConformanceShard3Tests : ConformanceShardBase {
  public static TheoryData<string> Cases() => ShardCases(3);

  [Theory]
  [MemberData(nameof(Cases))]
  public void RunCase(string caseRelativePath) => RunCaseCore(caseRelativePath);
}

public sealed class ConformanceShard4Tests : ConformanceShardBase {
  public static TheoryData<string> Cases() => ShardCases(4);

  [Theory]
  [MemberData(nameof(Cases))]
  public void RunCase(string caseRelativePath) => RunCaseCore(caseRelativePath);
}

public sealed class ConformanceShard5Tests : ConformanceShardBase {
  public static TheoryData<string> Cases() => ShardCases(5);

  [Theory]
  [MemberData(nameof(Cases))]
  public void RunCase(string caseRelativePath) => RunCaseCore(caseRelativePath);
}

public sealed class ConformanceShard6Tests : ConformanceShardBase {
  public static TheoryData<string> Cases() => ShardCases(6);

  [Theory]
  [MemberData(nameof(Cases))]
  public void RunCase(string caseRelativePath) => RunCaseCore(caseRelativePath);
}

public sealed class ConformanceShard7Tests : ConformanceShardBase {
  public static TheoryData<string> Cases() => ShardCases(7);

  [Theory]
  [MemberData(nameof(Cases))]
  public void RunCase(string caseRelativePath) => RunCaseCore(caseRelativePath);
}

/// <summary>
/// Conformance suite invariants and status report generation.
/// </summary>
public sealed class ConformanceTests {
  [Fact]
  public void ShardsCoverAllCasesExactlyOnce() {
    List<string> expected = ConformanceRunner.DiscoverCases();
    TheoryData<string>[] shards = [
      ConformanceShard0Tests.Cases(),
      ConformanceShard1Tests.Cases(),
      ConformanceShard2Tests.Cases(),
      ConformanceShard3Tests.Cases(),
      ConformanceShard4Tests.Cases(),
      ConformanceShard5Tests.Cases(),
      ConformanceShard6Tests.Cases(),
      ConformanceShard7Tests.Cases(),
    ];

    var seen = new List<string>(expected.Count);
    foreach (TheoryData<string> shard in shards) {
      foreach (string row in shard) {
        seen.Add(row);
      }
    }

    Assert.Equal(expected.Count, seen.Count);
    seen.Sort(StringComparer.Ordinal);
    Assert.Equal(expected, seen);
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
