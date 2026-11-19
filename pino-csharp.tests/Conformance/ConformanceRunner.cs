using System.Diagnostics;
using System.Text;
using Pino;

namespace pino_csharp.tests.Conformance;

public enum ConformanceEngine {
  TreeWalk,
  VM,
  C
}

/// <summary>Raw outcome of running one case on one engine.</summary>
public sealed record EngineRun(bool Succeeded, string Stdout, string Message);

/// <summary>Verdict of an engine run compared against the case expectation.</summary>
public sealed record CaseVerdict(bool Passed, string Detail);

public sealed record KnownFailure(ConformanceEngine Engine, string CaseName, string Line, string File);

public static class ConformanceRunner {
  public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

  private static string? _root;

  // ---------------------------------------------------------------- repo layout

  public static string RepoRoot {
    get {
      if (_root != null) return _root;
      var dir = new DirectoryInfo(AppContext.BaseDirectory);
      while (dir != null) {
        if (Directory.Exists(Path.Combine(dir.FullName, "conformance")) &&
            Directory.Exists(Path.Combine(dir.FullName, "pino-csharp"))) {
          _root = dir.FullName;
          return _root;
        }
        dir = dir.Parent;
      }
      throw new DirectoryNotFoundException(
        $"Could not find a directory containing 'conformance/' and 'pino-csharp/' above '{AppContext.BaseDirectory}'.");
    }
  }

  public static string ConformanceDir => Path.Combine(RepoRoot, "conformance");

  private static bool IsInModulesDir(string relativePath) {
    var parts = relativePath.Split('/');
    for (int i = 0; i < parts.Length - 1; i++) {
      if (parts[i] == "modules") return true;
    }
    return false;
  }

  /// <summary>Every conformance/**/*.pino (relative to conformance/, '/' separators, sorted), excluding helper modules.</summary>
  public static List<string> DiscoverCases() {
    var root = ConformanceDir;
    var cases = new List<string>();
    foreach (var file in Directory.EnumerateFiles(root, "*.pino", SearchOption.AllDirectories)) {
      var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
      if (IsInModulesDir(rel)) continue;
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
    return slash < 0 ? "" : caseRelativePath.Substring(0, slash);
  }

  public static string ExpectedPath(string caseRelativePath, string extension) =>
    Path.ChangeExtension(CaseFullPath(caseRelativePath), extension);

  /// <summary>Returns the number of sibling .out/.err files present (a valid case has exactly 1).</summary>
  public static int ExpectationFileCount(string caseRelativePath) {
    int n = 0;
    if (File.Exists(ExpectedPath(caseRelativePath, ".out"))) n++;
    if (File.Exists(ExpectedPath(caseRelativePath, ".err"))) n++;
    return n;
  }

  // ---------------------------------------------------------------- known failures

  public static string KnownFailuresPath(string feature) =>
    Path.Combine(ConformanceDir, feature, "known-failures.txt");

  public static List<KnownFailure> LoadKnownFailures(string feature) {
    var path = KnownFailuresPath(feature);
    var result = new List<KnownFailure>();
    if (!File.Exists(path)) return result;
    foreach (var raw in File.ReadAllLines(path)) {
      var line = raw.Trim();
      if (line.Length == 0 || line.StartsWith('#')) continue;
      var body = line;
      int hash = body.IndexOf('#');
      if (hash >= 0) body = body.Substring(0, hash).Trim();
      var parts = body.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      if (parts.Length != 2 || !Enum.TryParse<ConformanceEngine>(parts[0], out var engine)) {
        throw new FormatException($"Malformed line '{line}' in {path}. Expected '<Engine> <case-file-name.pino>  # <reason>'.");
      }
      result.Add(new KnownFailure(engine, parts[1], line, path));
    }
    return result;
  }

  public static KnownFailure? FindKnownFailure(ConformanceEngine engine, string caseRelativePath) {
    var feature = FeatureOf(caseRelativePath);
    // Name relative to the feature directory ("x.pino", or "sub/x.pino" for nested cases).
    var name = caseRelativePath.Substring(feature.Length + 1);
    foreach (var kf in LoadKnownFailures(feature)) {
      if (kf.Engine == engine && kf.CaseName == name) return kf;
    }
    return null;
  }

  // ---------------------------------------------------------------- comparison

  public static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();

  /// <summary>Compares a run against the case's .out / .err expectation.</summary>
  public static CaseVerdict Judge(string caseRelativePath, EngineRun run) {
    int count = ExpectationFileCount(caseRelativePath);
    if (count != 1) {
      return new CaseVerdict(false, $"{caseRelativePath}: expected exactly one .out or .err sibling file, found {count}.");
    }

    var outPath = ExpectedPath(caseRelativePath, ".out");
    if (File.Exists(outPath)) {
      var expected = Normalize(File.ReadAllText(outPath));
      if (!run.Succeeded) {
        return new CaseVerdict(false, $"expected output but run failed: {run.Message}");
      }
      var actual = Normalize(run.Stdout);
      return actual == expected
        ? new CaseVerdict(true, "")
        : new CaseVerdict(false, $"stdout mismatch.\n--- expected ---\n{expected}\n--- actual ---\n{actual}");
    }

    var errText = Normalize(File.ReadAllText(ExpectedPath(caseRelativePath, ".err")));
    if (run.Succeeded) {
      return new CaseVerdict(false, $"expected failure containing \"{errText}\" but the run succeeded with stdout:\n{Normalize(run.Stdout)}");
    }
    return run.Message.Contains(errText)
      ? new CaseVerdict(true, "")
      : new CaseVerdict(false, $"failure message does not contain \"{errText}\". Actual message:\n{run.Message}");
  }

  public static CaseVerdict RunAndJudge(ConformanceEngine engine, string caseRelativePath) =>
    Judge(caseRelativePath, Run(engine, caseRelativePath));

  // ---------------------------------------------------------------- execution

  public static EngineRun Run(ConformanceEngine engine, string caseRelativePath) {
    var path = CaseFullPath(caseRelativePath);
    return engine switch {
      ConformanceEngine.TreeWalk => RunInProcess(path, useVM: false),
      ConformanceEngine.VM => RunInProcess(path, useVM: true),
      ConformanceEngine.C => RunC(path),
      _ => throw new ArgumentOutOfRangeException(nameof(engine))
    };
  }

  private static string DescribeException(Exception ex) {
    if (ex is PinoPanicException panic) return panic.PanicMessage;
    return ex.Message;
  }

  private static EngineRun RunInProcess(string path, bool useVM) {
    var originalOut = Console.Out;
    var originalIn = Console.In;
    var writer = new StringWriter();
    Console.SetOut(writer);
    Console.SetIn(new StringReader(""));
    try {
      Exception? failure = null;
      var worker = new Thread(() => {
        try {
          ExecuteFile(path, useVM);
        } catch (Exception ex) {
          failure = ex;
        }
      }, 64 * 1024 * 1024) { IsBackground = true };
      worker.Start();
      if (!worker.Join(Timeout)) {
        // The thread cannot be killed in-process; it is a background thread and is abandoned.
        return new EngineRun(false, writer.ToString(), "timeout");
      }
      string stdout;
      lock (writer) { stdout = writer.ToString(); }
      return failure == null
        ? new EngineRun(true, stdout, "")
        : new EngineRun(false, stdout, DescribeException(failure));
    } finally {
      Console.SetOut(originalOut);
      Console.SetIn(originalIn);
    }
  }

  /// <summary>Mirrors Program.RunFile (tree-walk and VM branches) without the VM warning banner.</summary>
  private static void ExecuteFile(string path, bool useVM) {
    var program = Parser.ParseFile(path);
    var checker = new Checker();
    checker.Check(program);

    bool hasMainFunc = false;
    bool hasExplicitMainCall = false;
    foreach (var stmt in program.Statements) {
      if (stmt is FunctionDeclaration fnDecl && fnDecl.Identifier == "main") hasMainFunc = true;
      if (stmt is FunctionCallExpression call && call.Callee == "main") hasExplicitMainCall = true;
    }

    var evaluator = new Evaluator();
    if (useVM) {
      var compiler = new Compiler();
      var vmFn = compiler.Compile(program);
      var vm = new VM(evaluator, evaluator.Globals);
      vm.Execute(vmFn);
    } else {
      evaluator.Execute(program);
    }

    if (hasMainFunc && !hasExplicitMainCall) {
      if (evaluator.Globals.Exists("main") && evaluator.Globals.Get("main") is IPinoCallable mainCallable && mainCallable.Arity == 0) {
        var result = mainCallable.Call(evaluator, new List<object?>());
        CheckMainResult(result);
      }
    }
  }

  /// <summary>Program.HandleMainResult exits with code 1 on Failure/None; here that is a failure with the same text.</summary>
  private static void CheckMainResult(object? result) {
    if (result is PinoUnionValue unionVal) {
      if (unionVal.VariantName == "Failure") {
        var payload = unionVal.Payload.Count > 0 ? unionVal.Payload[0] : "";
        throw new Exception($"Error: {payload}");
      }
      if (unionVal.VariantName == "None") {
        throw new Exception("Error: Option::None");
      }
    }
  }

  private sealed record ProcessResult(bool TimedOut, int ExitCode, string Stdout, string Stderr);

  private static ProcessResult RunProcess(string fileName, string arguments, string workingDirectory, bool closeStdin) {
    var startInfo = new ProcessStartInfo {
      FileName = fileName,
      Arguments = arguments,
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      RedirectStandardInput = closeStdin,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    using var process = new Process { StartInfo = startInfo };
    var stdout = new StringBuilder();
    var stderr = new StringBuilder();
    process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (stdout) stdout.Append(e.Data).Append('\n'); };
    process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderr) stderr.Append(e.Data).Append('\n'); };
    process.Start();
    if (closeStdin) process.StandardInput.Close();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    if (!process.WaitForExit((int) Timeout.TotalMilliseconds)) {
      try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
      process.WaitForExit();
      return new ProcessResult(true, -1, "", "");
    }
    process.WaitForExit(); // flush async readers
    lock (stdout) lock (stderr) {
      return new ProcessResult(false, process.ExitCode, stdout.ToString(), stderr.ToString());
    }
  }

  /// <summary>Mirrors Program.CompileAndRunWatch, with every path resolved from the repo root and nothing written to the CWD.</summary>
  private static EngineRun RunC(string path) {
    var root = RepoRoot;
    var tccPath = Path.Combine(root, "tooling", "tcc", "tcc.exe");
    if (!File.Exists(tccPath)) {
      return new EngineRun(false, "", $"TCC not found at {tccPath}");
    }

    string cCode;
    try {
      var program = Parser.ParseFile(path);
      var checker = new Checker();
      checker.Check(program);
      cCode = new TranspilerC().Transpile(program, checker);
    } catch (Exception ex) {
      return new EngineRun(false, "", DescribeException(ex));
    }

    var tempDir = Path.Combine(Path.GetTempPath(), "pino-conformance", Guid.NewGuid().ToString("N"));
    try {
      Directory.CreateDirectory(tempDir);
      var cFilePath = Path.Combine(tempDir, "pino_output.c");
      var exePath = Path.Combine(tempDir, "pino_output.exe");
      File.WriteAllText(cFilePath, cCode);

      var gcCPath = Path.Combine(root, "tooling", "gc", "gc.c");
      var gcIncDir = Path.Combine(root, "tooling", "gc", "include");
      var runtimeIncDir = Path.Combine(root, "runtime");
      var runtimeCPath = Path.Combine(root, "runtime", "runtime.c");
      var reCPath = Path.Combine(root, "runtime", "re.c");

      // Same arguments as Program.CompileAndRunWatch plus -I<root>: the generated C does `#include "runtime/runtime.h"`,
      // which the CLI resolves only because it writes pino_output.c into the repo root (its CWD). We write to a temp dir.
      var args = $"-I\"{root}\" -I\"{gcIncDir}\" -I\"{runtimeIncDir}\" \"{gcCPath}\" \"{cFilePath}\" \"{runtimeCPath}\" \"{reCPath}\" -o \"{exePath}\"";
      var tcc = RunProcess(tccPath, args, tempDir, closeStdin: false);
      if (tcc.TimedOut) return new EngineRun(false, "", "timeout");
      if (tcc.ExitCode != 0) {
        return new EngineRun(false, "", $"TCC compilation failed:\n{tcc.Stderr}{tcc.Stdout}".TrimEnd());
      }

      var exe = RunProcess(exePath, "", tempDir, closeStdin: true);
      if (exe.TimedOut) return new EngineRun(false, "", "timeout");
      if (exe.ExitCode != 0) {
        return new EngineRun(false, exe.Stdout, $"exit code {exe.ExitCode}\nstdout:\n{exe.Stdout}\nstderr:\n{exe.Stderr}".TrimEnd());
      }
      return new EngineRun(true, exe.Stdout, "");
    } finally {
      try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
    }
  }
}
