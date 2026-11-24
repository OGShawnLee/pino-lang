using System.Text;

using Pino.Compiler.Checking;
using Pino.Compiler.CodeGen.C;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.IR.Passes;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;
using Pino.Vm;

namespace Pino.Compiler.Tests.Conformance;

public enum ConformanceEngine {
  ReVm,
  ReC,
}

/// <summary>Raw outcome of running one case on one engine.</summary>
public sealed record EngineRun(
    bool Succeeded,
    int ExitCode,
    string Stdout,
    string Stderr,
    string Message,
    bool IsBuildFailure = false,
    bool IsCompileTimeError = false);

/// <summary>Verdict of an engine run compared against the case expectation.</summary>
public sealed record CaseVerdict(bool Passed, string Detail);

public sealed record KnownFailure(
    ConformanceEngine Engine,
    string CaseName,
    string Line,
    string File,
    bool IsCrash = false);

public sealed record PreparedCase(
    string CaseRelativePath,
    SourceText Source,
    Compilation? Compilation,
    IrModule? Ir,
    EngineRun? CompileTimeOutcome);

public static class ConformanceRunner {
  public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

  /// <summary>
  /// Conformance <c>.err</c> cases whose failure occurs at runtime rather than during lexing,
  /// parsing, or static type checking. Verified non-stale by
  /// <see cref="ConformanceTests.RuntimeErrCasesAreNonStale"/>.
  /// </summary>
  public static readonly IReadOnlyDictionary<string, string> RuntimeErrCases =
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        ["errors/main-bubble.pino"] = "Runtime failure bubbled out of main via `?`.",
        ["errors/main-failure.pino"] = "Runtime failure returned from Result main.",
        ["errors/main-none.pino"] = "Runtime None returned from Option main.",
        ["errors/or-panic.pino"] = "Explicit runtime panic inside `or { ... }` recovery.",
        ["errors/panic-as-value.pino"] = "Explicit runtime `panic(...)` expression.",
        ["errors/panic-exit-code.pino"] = "Explicit runtime `panic(...)` exit-code check.",
        ["errors/panic-halts.pino"] = "Explicit runtime `panic(...)` halting execution.",
        ["errors/panic-nested.pino"] = "Explicit runtime `panic(...)` from nested call.",
        ["functions/err-mut-same-slot-runtime.pino"] = "Runtime D-184 exclusivity panic on aliased dynamic mut slots.",
        ["maps/err-missing-key.pino"] = "Runtime lookup of a missing map key.",
        ["pools/err-stale-index.pino"] = "Runtime panic on indexing a Pool[T] with a stale Handle[T].",
        ["stdlib-regex/err-invalid-pattern.pino"] = "Runtime regex compilation failure on invalid pattern string.",
        ["stdlib-string/err-substring-out-of-range.pino"] = "Runtime substring bounds violation on non-constant indices.",
        ["stdlib-vector/err-swap-out-of-range.pino"] = "Runtime vector swap bounds check on out-of-range index.",
        ["types/err-runtime-division-by-zero-float.pino"] = "Runtime float division by non-constant zero.",
        ["types/err-runtime-division-by-zero-int.pino"] = "Runtime int division by non-constant zero.",
        ["types/err-runtime-float-modulo-by-zero.pino"] = "Runtime float modulo by non-constant zero.",
        ["types/err-runtime-floor-division-by-zero.pino"] = "Runtime floor division by non-constant zero.",
        ["types/err-runtime-modulo-by-zero.pino"] = "Runtime modulo by non-constant zero.",
        ["types/err-runtime-int-min-div-overflow.pino"] = "Runtime INT64_MIN // -1 overflow (TYPE-E6).",
        ["vectors/err-index-out-of-range.pino"] = "Runtime vector bounds check on out-of-range index.",
      };

  /// <summary>
  /// Runtime <c>.err</c> cases where <c>main</c> returned a failure (exit code 1);
  /// every other runtime <c>.err</c> case panics (exit code 101).
  /// </summary>
  public static readonly IReadOnlySet<string> MainFailureCases =
      new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "errors/main-bubble.pino",
        "errors/main-failure.pino",
        "errors/main-none.pino",
      };

  private static string? s_root;

  // ---------------------------------------------------------------- repo layout

  public static string RepoRoot {
    get {
      if (s_root is not null) {
        return s_root;
      }

      DirectoryInfo? dir = new(AppContext.BaseDirectory);
      while (dir is not null) {
        if (Directory.Exists(Path.Combine(dir.FullName, "conformance"))
            && Directory.Exists(Path.Combine(dir.FullName, "pino-csharp-re"))) {
          s_root = dir.FullName;
          return s_root;
        }

        dir = dir.Parent;
      }

      throw new DirectoryNotFoundException(
          $"Could not find a directory containing 'conformance/' and 'pino-csharp-re/' above '{AppContext.BaseDirectory}'.");
    }
  }

  public static string ConformanceDir => Path.Combine(RepoRoot, "conformance");

  /// <summary>True when a directory segment named "modules" appears below the feature directory (depth 2 or more).</summary>
  private static bool IsInModulesDir(string relativePath) {
    string[] parts = relativePath.Split('/');
    for (int i = 1; i < parts.Length - 1; i++) {
      if (parts[i] == "modules") {
        return true;
      }
    }

    return false;
  }

  /// <summary>Every conformance/**/*.pino (relative to conformance/, '/' separators, sorted), excluding helper modules.</summary>
  public static List<string> DiscoverCases() {
    string root = ConformanceDir;
    var cases = new List<string>();
    foreach (string file in Directory.EnumerateFiles(root, "*.pino", SearchOption.AllDirectories)) {
      string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
      if (IsInModulesDir(rel)) {
        continue;
      }

      cases.Add(rel);
    }

    cases.Sort(StringComparer.Ordinal);
    return cases;
  }

  public static string CaseFullPath(string caseRelativePath) =>
      Path.Combine(ConformanceDir, caseRelativePath.Replace('/', Path.DirectorySeparatorChar));

  /// <summary>First path segment of the case, i.e. the feature directory.</summary>
  public static string FeatureOf(string caseRelativePath) {
    int slash = caseRelativePath.IndexOf('/');
    return slash < 0 ? string.Empty : caseRelativePath[..slash];
  }

  public static string ExpectedPath(string caseRelativePath, string extension) =>
      Path.ChangeExtension(CaseFullPath(caseRelativePath), extension);

  /// <summary>Returns the number of sibling .out/.err files present (a valid case has exactly 1).</summary>
  public static int ExpectationFileCount(string caseRelativePath) {
    int n = 0;
    if (File.Exists(ExpectedPath(caseRelativePath, ".out"))) {
      n++;
    }

    if (File.Exists(ExpectedPath(caseRelativePath, ".err"))) {
      n++;
    }

    return n;
  }

  // ---------------------------------------------------------------- known failures

  public static string KnownFailuresPath(string feature) =>
      Path.Combine(ConformanceDir, feature, "known-failures.txt");

  public static List<KnownFailure> LoadKnownFailures(string feature) {
    string path = KnownFailuresPath(feature);
    var result = new List<KnownFailure>();
    if (!File.Exists(path)) {
      return result;
    }

    foreach (string raw in File.ReadAllLines(path)) {
      string line = raw.Trim();
      if (line.Length == 0 || line.StartsWith('#')) {
        continue;
      }

      string body = line;
      bool isCrash = false;
      int hash = body.IndexOf('#');
      if (hash >= 0) {
        string comment = body[(hash + 1)..].TrimStart();
        isCrash = comment.StartsWith("CRASH", StringComparison.Ordinal);
        body = body[..hash].Trim();
      }

      string[] parts = body.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      if (parts.Length != 2 || !Enum.TryParse<ConformanceEngine>(parts[0], out ConformanceEngine engine)) {
        throw new FormatException(
            $"Malformed line '{line}' in {path}. Expected '<ReVm|ReC> <case-file-name.pino>  # <reason>'.");
      }

      result.Add(new KnownFailure(engine, parts[1], line, path, isCrash));
    }

    return result;
  }

  public static KnownFailure? FindKnownFailure(ConformanceEngine engine, string caseRelativePath) {
    string feature = FeatureOf(caseRelativePath);
    string name = caseRelativePath[(feature.Length + 1)..];
    foreach (KnownFailure kf in LoadKnownFailures(feature)) {
      if (kf.Engine == engine && kf.CaseName == name) {
        return kf;
      }
    }

    return null;
  }

  // ---------------------------------------------------------------- comparison

  public static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

  /// <summary>Compares a run against the case's .out / .err expectation.</summary>
  public static CaseVerdict Judge(string caseRelativePath, EngineRun run) {
    int count = ExpectationFileCount(caseRelativePath);
    if (count != 1) {
      return new CaseVerdict(
          false,
          $"{caseRelativePath}: expected exactly one .out or .err sibling file, found {count}.");
    }

    string outPath = ExpectedPath(caseRelativePath, ".out");
    if (File.Exists(outPath)) {
      string expected = Normalize(File.ReadAllText(outPath));
      if (!run.Succeeded) {
        return new CaseVerdict(false, $"expected output but run failed: {run.Message}");
      }

      string actual = Normalize(run.Stdout);
      return string.Equals(actual, expected, StringComparison.Ordinal)
          ? new CaseVerdict(true, string.Empty)
          : new CaseVerdict(false, $"stdout mismatch.\n--- expected ---\n{expected}\n--- actual ---\n{actual}");
    }

    string errText = Normalize(File.ReadAllText(ExpectedPath(caseRelativePath, ".err")));
    if (run.Succeeded) {
      return new CaseVerdict(
          false,
          $"expected failure containing \"{errText}\" but the run succeeded with stdout:\n{Normalize(run.Stdout)}");
    }

    if (run.IsBuildFailure) {
      return new CaseVerdict(
          false,
          $"build/verifier/RC/timeout failure cannot satisfy .err expectation \"{errText}\":\n{run.Message}");
    }

    if (RuntimeErrCases.ContainsKey(caseRelativePath)) {
      if (run.IsCompileTimeError) {
        return new CaseVerdict(
            false,
            $"runtime .err case produced compile-time error(s):\n{run.Message}");
      }

      int expectedExit = MainFailureCases.Contains(caseRelativePath) ? 1 : 101;
      if (run.ExitCode != expectedExit) {
        return new CaseVerdict(
            false,
            $"expected exit code {expectedExit} but got {run.ExitCode}.\n{run.Message}");
      }

      return run.Message.Contains(errText, StringComparison.Ordinal)
          ? new CaseVerdict(true, string.Empty)
          : new CaseVerdict(
              false,
              $"failure message does not contain \"{errText}\". Actual message:\n{run.Message}");
    }

    if (!run.IsCompileTimeError) {
      return new CaseVerdict(
          false,
          $"expected compile-time error containing \"{errText}\", but case compiled and failed at runtime:\n{run.Message}");
    }

    return run.Message.Contains(errText, StringComparison.Ordinal)
        ? new CaseVerdict(true, string.Empty)
        : new CaseVerdict(
            false,
            $"compile-time diagnostic(s) do not contain \"{errText}\". Actual diagnostics:\n{run.Message}");
  }

  // ---------------------------------------------------------------- frontend + execution

  /// <summary>
  /// Runs the shared frontend (lex, parse, check, no leaked internal types) and, for executable
  /// cases, lowers to IR, inserts RC operations, and verifies post-RC IR.
  /// </summary>
  public static PreparedCase PrepareCase(string caseRelativePath) {
    string path = CaseFullPath(caseRelativePath);
    string content = File.ReadAllText(path);
    var source = new SourceText(content, path);
    bool isOutCase = File.Exists(ExpectedPath(caseRelativePath, ".out"));
    bool isRuntimeErrCase = RuntimeErrCases.ContainsKey(caseRelativePath);

    ParseResult parsed = Parser.Parse(source);
    if (isOutCase) {
      Diagnostic[] nonHintParseDiags = [.. parsed.Diagnostics.Where(static d => d.Severity != DiagnosticSeverity.Hint)];
      if (nonHintParseDiags.Length > 0) {
        return new PreparedCase(
            caseRelativePath,
            source,
            null,
            null,
            new EngineRun(
                false,
                -1,
                string.Empty,
                string.Empty,
                FormatDiagnostics(nonHintParseDiags),
                IsCompileTimeError: true));
      }
    } else {
      Diagnostic[] parseErrors = [.. parsed.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)];
      if (parseErrors.Length > 0) {
        return new PreparedCase(
            caseRelativePath,
            source,
            null,
            null,
            new EngineRun(
                false,
                -1,
                string.Empty,
                string.Empty,
                FormatDiagnostics(parseErrors),
                IsCompileTimeError: true));
      }
    }

    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
    Diagnostic[] compErrors = [.. compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)];

    if (isOutCase) {
      if (compErrors.Length > 0) {
        return new PreparedCase(
            caseRelativePath,
            source,
            compilation,
            null,
            new EngineRun(
                false,
                -1,
                string.Empty,
                string.Empty,
                FormatDiagnostics(compErrors),
                IsCompileTimeError: true));
      }

      string? leakedTypeError = FindLeakedInternalType(caseRelativePath, compilation);
      if (leakedTypeError is not null) {
        return new PreparedCase(
            caseRelativePath,
            source,
            compilation,
            null,
            new EngineRun(
                false,
                -1,
                string.Empty,
                string.Empty,
                leakedTypeError,
                IsBuildFailure: true));
      }
    } else if (isRuntimeErrCase) {
      if (compErrors.Length > 0) {
        return new PreparedCase(
            caseRelativePath,
            source,
            compilation,
            null,
            new EngineRun(
                false,
                -1,
                string.Empty,
                string.Empty,
                FormatDiagnostics(compErrors),
                IsCompileTimeError: true));
      }
    } else {
      Diagnostic[] checkerErrors = [.. compErrors.Where(static d => d.Code.StartsWith("PN3", StringComparison.Ordinal))];
      if (checkerErrors.Length > 0) {
        return new PreparedCase(
            caseRelativePath,
            source,
            compilation,
            null,
            new EngineRun(
                false,
                -1,
                string.Empty,
                string.Empty,
                FormatDiagnostics(checkerErrors),
                IsCompileTimeError: true));
      }

      if (compErrors.Length > 0) {
        return new PreparedCase(
            caseRelativePath,
            source,
            compilation,
            null,
            new EngineRun(
                false,
                -1,
                string.Empty,
                string.Empty,
                FormatDiagnostics(compErrors),
                IsCompileTimeError: true));
      }

      return new PreparedCase(
          caseRelativePath,
          source,
          compilation,
          null,
          new EngineRun(
              true,
              0,
              string.Empty,
              string.Empty,
              "Expected compile-time checker error, but compilation succeeded with 0 errors."));
    }

    IrModule ir = IrLowerer.Lower(compilation);
    RcInsertPass.Run(ir);
    IrVerificationResult verification = IrVerifier.Verify(ir, IrVerifyStage.PostRc);
    if (!verification.IsValid) {
      return new PreparedCase(
          caseRelativePath,
          source,
          compilation,
          ir,
          new EngineRun(
              false,
              -1,
              string.Empty,
              string.Empty,
              "IR verification failed:\n" + string.Join("\n", verification.Errors),
              IsBuildFailure: true));
    }

    return new PreparedCase(caseRelativePath, source, compilation, ir, CompileTimeOutcome: null);
  }

  public static EngineRun Run(ConformanceEngine engine, PreparedCase prepared) {
    if (prepared.CompileTimeOutcome is not null) {
      return prepared.CompileTimeOutcome;
    }

    return engine switch {
      ConformanceEngine.ReVm => RunReVm(prepared),
      ConformanceEngine.ReC => RunReC(prepared),
      _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };
  }

  private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) {
    var sb = new StringBuilder();
    foreach (Diagnostic d in diagnostics) {
      if (sb.Length > 0) {
        sb.Append('\n');
      }

      sb.Append($"{d.Code}: {d.Message}");
      if (!string.IsNullOrWhiteSpace(d.Help)) {
        sb.Append('\n').Append(d.Help);
      }
    }

    return sb.ToString();
  }

  private static string? FindLeakedInternalType(string relativePath, Compilation compilation) {
    foreach (SemanticModel model in compilation.SemanticModels) {
      var stack = new Stack<SyntaxNode>();
      stack.Push(model.Tree.Root);
      while (stack.Count > 0) {
        SyntaxNode current = stack.Pop();
        if (current is ExpressionSyntax expr) {
          TypeSymbol? type = model.GetType(expr);
          if (type is null) {
            return $"{relativePath}: expression {expr.GetType().Name} has no resolved type.";
          }

          if (type is TypeVariableType) {
            return $"{relativePath}: leaked TypeVariableType on {expr.GetType().Name}.";
          }

          if (type is PendingReturnType) {
            return $"{relativePath}: leaked PendingReturnType on {expr.GetType().Name}.";
          }
        }

        foreach (SyntaxElement elem in current.GetChildren()) {
          if (elem.IsNode) {
            stack.Push(elem.AsNode());
          }
        }
      }
    }

    return null;
  }

  private static EngineRun RunReVm(PreparedCase prepared) {
    var host = new BufferedVmHost();
    using var cts = new CancellationTokenSource(Timeout);
    PinoVmResult result;
    try {
      var vm = new PinoVm(prepared.Ir!, host, new PinoVmOptions {
        VerifyRc = true,
        RootSource = prepared.Source,
        CancellationToken = cts.Token,
      });
      result = vm.Run();
    } catch (OperationCanceledException) {
      return new EngineRun(false, -1, host.StdOut, host.StdErr, "timeout", IsBuildFailure: true);
    }

    if (result.RcError is not null) {
      return new EngineRun(
          false,
          result.ExitCode,
          host.StdOut,
          host.StdErr,
          $"shadow-RC error: {result.RcError}",
          IsBuildFailure: true);
    }

    if (result.ExitCode != 0) {
      return new EngineRun(
          false,
          result.ExitCode,
          host.StdOut,
          host.StdErr,
          $"exit code {result.ExitCode}\nstdout:\n{host.StdOut}\nstderr:\n{host.StdErr}".TrimEnd());
    }

    if (result.LeakReport is { IsClean: false } leaky) {
      return new EngineRun(
          false,
          result.ExitCode,
          host.StdOut,
          host.StdErr,
          $"shadow-RC leaks:\n{leaky.Format()}",
          IsBuildFailure: true);
    }

    return new EngineRun(true, 0, host.StdOut, host.StdErr, string.Empty);
  }

  private static EngineRun RunReC(PreparedCase prepared) {
    string cSource = CEmitter.Emit(prepared.Ir!, prepared.Compilation);
    string tempDir = Path.Combine(Path.GetTempPath(), "pino-re-conformance", Guid.NewGuid().ToString("N"));
    try {
      Directory.CreateDirectory(tempDir);
      string exePath = Path.Combine(tempDir, OperatingSystem.IsWindows() ? "pino_re_case.exe" : "pino_re_case");
      CBuildResult build = CCompilerDriver.Build(new CBuildOptions(
          CSource: cSource,
          OutputExecutablePath: exePath,
          Release: false,
          DebugRc: true,
          RepoRoot: RepoRoot));
      if (!build.Succeeded) {
        return new EngineRun(
            false,
            -1,
            string.Empty,
            build.Stderr,
            $"TCC compilation failed ({build.CompilerUsed}):\n{build.Stderr}{build.Stdout}".TrimEnd(),
            IsBuildFailure: true);
      }

      CExecResult exec = CCompilerDriver.RunExecutable(
          exePath,
          workingDirectory: tempDir,
          failOnLeak: true,
          closeStdin: true,
          timeout: Timeout);
      if (exec.TimedOut) {
        return new EngineRun(false, -1, exec.Stdout, exec.Stderr, "timeout", IsBuildFailure: true);
      }

      if (exec.ExitCode != 0) {
        return new EngineRun(
            false,
            exec.ExitCode,
            exec.Stdout,
            exec.Stderr,
            $"exit code {exec.ExitCode}\nstdout:\n{exec.Stdout}\nstderr:\n{exec.Stderr}".TrimEnd());
      }

      if (exec.Stderr.Contains("LEAK:", StringComparison.Ordinal)) {
        return new EngineRun(
            false,
            exec.ExitCode,
            exec.Stdout,
            exec.Stderr,
            $"RC leak detected:\n{exec.Stderr}".TrimEnd(),
            IsBuildFailure: true);
      }

      return new EngineRun(true, 0, exec.Stdout, exec.Stderr, string.Empty);
    } finally {
      try {
        Directory.Delete(tempDir, recursive: true);
      } catch (IOException) {
      } catch (UnauthorizedAccessException) {
      }
    }
  }
}
