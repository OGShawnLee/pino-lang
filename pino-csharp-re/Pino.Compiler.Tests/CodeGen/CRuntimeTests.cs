using System.Diagnostics;
using System.Text;

using Pino.Compiler.CodeGen.C;

namespace Pino.Compiler.Tests.CodeGen;

/// <summary>
/// Builds and runs <c>runtime-re/tests/test_runtime.c</c> with the repository's TCC (or system C
/// compiler fallback on Linux/macOS where TCC is not bundled, in normal and <c>-DPINO_DEBUG_RC</c>
/// modes) and, when a system C compiler (<c>gcc</c>/<c>clang</c>/<c>cc</c>) is on PATH, with
/// <c>-O2 -Wall -Wextra -Werror</c>.
/// </summary>
public sealed class CRuntimeTests : IDisposable {
  private static readonly string[] s_runtimeSources = [
    "runtime-re/pino_alloc.c",
    "runtime-re/pino_str.c",
    "runtime-re/pino_io.c",
  ];

  private readonly string _work = Path.Combine(Path.GetTempPath(), "pino-re-cruntime-" + Guid.NewGuid().ToString("N"));

  public CRuntimeTests() {
    Directory.CreateDirectory(_work);
  }

  public void Dispose() {
    try {
      Directory.Delete(_work, recursive: true);
    } catch (IOException) {
    } catch (UnauthorizedAccessException) {
    }
  }

  [Fact]
  public void TccNormalMode_BuildsAndAllRuntimeChecksPass() {
    string exe = BuildWithDevCompiler("normal", debug: false);

    ProcessResult run = Run(exe, [], FindRepositoryRoot(), failOnLeak: false);

    Assert.True(run.ExitCode == 0, Describe("runtime tests (normal mode)", run));
    Assert.Contains("OK:", run.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void TccDebugMode_BuildsAndReportsNoLeaksWithFailOnLeak() {
    string exe = BuildWithDevCompiler("debug", debug: true);

    ProcessResult run = Run(exe, [], FindRepositoryRoot(), failOnLeak: true);

    Assert.True(run.ExitCode == 0, Describe("runtime tests (-DPINO_DEBUG_RC, PINO_FAIL_ON_LEAK=1)", run));
    Assert.Contains("OK:", run.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void GccBuild_HasNoWarningsInRuntimeReAndPasses() {
    string? cc = CCompilerDriver.FindReleaseCompiler();
    if (cc is null) {
      return; // gcc/clang is optional; the dev-compiler tests above are the required ones.
    }

    string root = FindRepositoryRoot();
    foreach (bool debug in new[] { false, true }) {
      string tag = debug ? "gcc-debug" : "gcc-normal";
      string exe = ExePath(tag);

      // runtime/re.c is the shared legacy regex engine, which must stay untouched and has one
      // unused-variable warning of its own, so it is compiled to its own object without -Werror.
      string reObject = Path.Combine(_work, tag + "-re.o");
      ProcessResult re = Run(cc, ["-O2", "-Wall", "-Wextra", "-c", "runtime/re.c", "-o", reObject], root, failOnLeak: false);
      Assert.True(re.ExitCode == 0, Describe("cc runtime/re.c", re));

      List<string> args = ["-O2", "-Wall", "-Wextra", "-Werror"];
      if (debug) {
        args.Add("-DPINO_DEBUG_RC");
      }

      args.AddRange(["-Iruntime-re", "-Iruntime", "runtime-re/tests/test_runtime.c", .. s_runtimeSources, reObject, "-lm", "-o", exe]);
      ProcessResult build = Run(cc, args, root, failOnLeak: false);
      Assert.True(build.ExitCode == 0, Describe("cc build " + tag, build));

      ProcessResult run = Run(exe, [], root, failOnLeak: true);
      Assert.True(run.ExitCode == 0, Describe("cc run " + tag, run));
    }
  }

  [Fact]
  public void RuntimeRe_HasNoBomNoGcHeaderAndNoStdAtomic() {
    string dir = Path.Combine(FindRepositoryRoot(), "runtime-re");
    string[] files = [.. Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
        .Where(static f => f.EndsWith(".c", StringComparison.Ordinal) || f.EndsWith(".h", StringComparison.Ordinal))];
    Assert.NotEmpty(files);

    foreach (string file in files) {
      byte[] bytes = File.ReadAllBytes(file);
      bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
      Assert.False(hasBom, $"{file} starts with a UTF-8 BOM, which TCC rejects.");

      string text = Encoding.UTF8.GetString(bytes);
      Assert.DoesNotContain("#include <gc.h>", text, StringComparison.Ordinal);
      Assert.DoesNotContain("#include <stdatomic.h>", text, StringComparison.Ordinal);
    }
  }

  private string BuildWithDevCompiler(string tag, bool debug) {
    string root = FindRepositoryRoot();
    string exe = ExePath("dev-" + tag);
    string? tcc = CCompilerDriver.FindBundledTcc(root);
    if (tcc is not null) {
      List<string> args = [];
      if (!OperatingSystem.IsWindows()) {
        string? tccDir = Path.GetDirectoryName(tcc);
        if (!string.IsNullOrEmpty(tccDir)) {
          args.Add($"-B{tccDir}");
        }
      }

      if (debug) {
        args.Add("-DPINO_DEBUG_RC");
      }

      args.AddRange(["-Iruntime-re", "-Iruntime", "runtime-re/tests/test_runtime.c", .. s_runtimeSources, "runtime/re.c"]);
      if (!OperatingSystem.IsWindows()) {
        args.Add("-lm");
      }

      args.AddRange(["-o", exe]);

      ProcessResult build = Run(tcc, args, root, failOnLeak: false);
      Assert.True(build.ExitCode == 0, Describe("tcc build " + tag, build));
      return exe;
    }

    string? cc = CCompilerDriver.FindSystemCompiler();
    Assert.True(cc is not null, $"Neither bundled TCC nor a system C compiler (cc/clang/gcc) was found.");
    List<string> ccArgs = [];
    if (debug) {
      ccArgs.Add("-DPINO_DEBUG_RC");
    }

    ccArgs.AddRange(["-Iruntime-re", "-Iruntime", "runtime-re/tests/test_runtime.c", .. s_runtimeSources, "runtime/re.c", "-lm", "-o", exe]);

    ProcessResult ccBuild = Run(cc!, ccArgs, root, failOnLeak: false);
    Assert.True(ccBuild.ExitCode == 0, Describe("cc build " + tag, ccBuild));
    return exe;
  }

  private string ExePath(string tag) =>
      Path.Combine(_work, "pino_rt_" + tag + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

  private static ProcessResult Run(string exe, IReadOnlyList<string> args, string workingDirectory, bool failOnLeak) {
    ProcessStartInfo info = new(exe) {
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };
    foreach (string arg in args) {
      info.ArgumentList.Add(arg);
    }

    if (failOnLeak) {
      info.Environment["PINO_FAIL_ON_LEAK"] = "1";
    }

    using Process process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start '{exe}'.");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(TimeSpan.FromMinutes(2))) {
      process.Kill(entireProcessTree: true);
      throw new TimeoutException($"'{exe}' did not finish within two minutes.");
    }

    return new ProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
  }

  private static string Describe(string what, ProcessResult result) =>
      $"{what} failed with exit code {result.ExitCode}.\n--- stdout ---\n{result.Stdout}\n--- stderr ---\n{result.Stderr}";

  private static string FindRepositoryRoot() {
    for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) {
      if (File.Exists(Path.Combine(directory.FullName, "runtime-re", "pino_rt.h"))
          && File.Exists(Path.Combine(directory.FullName, "runtime", "re.c"))) {
        return directory.FullName;
      }
    }

    throw new DirectoryNotFoundException(
        $"Could not locate the repository root (runtime-re/pino_rt.h + runtime/re.c) above '{AppContext.BaseDirectory}'.");
  }

  private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
