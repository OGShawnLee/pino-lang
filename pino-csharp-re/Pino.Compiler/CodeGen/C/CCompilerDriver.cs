using System.Diagnostics;
using System.Text;

namespace Pino.Compiler.CodeGen.C;

public sealed record CBuildOptions(
    string CSource,
    string OutputExecutablePath,
    bool Release = false,
    bool DebugRc = false,
    string? EmitCPath = null,
    string? RepoRoot = null);

public sealed record CBuildResult(
    bool Succeeded,
    string CompilerUsed,
    string OutputPath,
    string? EmittedCPath,
    string Stdout,
    string Stderr,
    TimeSpan CompileElapsed);

public sealed record CExecResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut = false);

/// <summary>
/// Compiles generated C99 + <c>runtime-re/*.c</c> + <c>runtime/re.c</c> via bundled TCC (dev)
/// or GCC/Clang (<c>--release</c>) and runs compiled binaries.
/// </summary>
public static class CCompilerDriver {
  private static readonly string[] s_runtimeReSources = [
    "pino_alloc.c",
    "pino_str.c",
    "pino_io.c",
  ];

  public static string FindRepositoryRoot(string? startDirectory = null) {
    string[] seeds = startDirectory is not null
        ? [startDirectory, Environment.CurrentDirectory, AppContext.BaseDirectory]
        : [Environment.CurrentDirectory, AppContext.BaseDirectory];

    foreach (string seed in seeds) {
      if (string.IsNullOrWhiteSpace(seed)) {
        continue;
      }

      for (DirectoryInfo? dir = new(Path.GetFullPath(seed)); dir is not null; dir = dir.Parent) {
        if (File.Exists(Path.Combine(dir.FullName, "runtime-re", "pino_rt.h"))
            && File.Exists(Path.Combine(dir.FullName, "runtime", "re.c"))) {
          return dir.FullName;
        }
      }
    }

    throw new DirectoryNotFoundException(
        "Could not locate the Pino runtime directory (runtime-re/pino_rt.h and runtime/re.c).");
  }

  public static string? FindTcc(string repoRoot) {
    string bundled = Path.Combine(repoRoot, "tooling", "tcc", OperatingSystem.IsWindows() ? "tcc.exe" : "tcc");
    if (File.Exists(bundled)) {
      return bundled;
    }

    return FindOnPath("tcc");
  }

  public static string? FindReleaseCompiler() => FindOnPath("gcc") ?? FindOnPath("clang");

  public static string? FindOnPath(string toolName) {
    string[] extensions = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat"] : [string.Empty];
    string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
      foreach (string ext in extensions) {
        string candidate = Path.Combine(dir, toolName + ext);
        if (File.Exists(candidate)) {
          return candidate;
        }
      }
    }

    return null;
  }

  public static CBuildResult Build(CBuildOptions options) {
    ArgumentNullException.ThrowIfNull(options);
    string repoRoot = options.RepoRoot ?? FindRepositoryRoot();
    string runtimeReDir = Path.Combine(repoRoot, "runtime-re");
    string runtimeDir = Path.Combine(repoRoot, "runtime");

    string? tempDir = null;
    string cFilePath;
    if (!string.IsNullOrWhiteSpace(options.EmitCPath)) {
      cFilePath = Path.GetFullPath(options.EmitCPath);
      string? cParent = Path.GetDirectoryName(cFilePath);
      if (!string.IsNullOrEmpty(cParent)) {
        Directory.CreateDirectory(cParent);
      }
    } else {
      tempDir = Path.Combine(Path.GetTempPath(), "pino-build-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);
      cFilePath = Path.Combine(tempDir, "pino_program.c");
    }

    string outExe = Path.GetFullPath(options.OutputExecutablePath);
    string? outParent = Path.GetDirectoryName(outExe);
    if (!string.IsNullOrEmpty(outParent)) {
      Directory.CreateDirectory(outParent);
    }

    try {
      // Always emit BOM-less UTF-8 (TCC 0.9.27 rejects a UTF-8 BOM).
      File.WriteAllText(cFilePath, options.CSource, CEmitter.Utf8NoBom);

      var sw = Stopwatch.StartNew();
      if (options.Release) {
        string? cc = FindReleaseCompiler();
        if (cc is null) {
          sw.Stop();
          return new CBuildResult(
              Succeeded: false,
              CompilerUsed: "gcc/clang",
              OutputPath: outExe,
              EmittedCPath: options.EmitCPath,
              Stdout: string.Empty,
              Stderr: "Release build requires 'gcc' or 'clang' on PATH, but neither was found.",
              CompileElapsed: sw.Elapsed);
        }

        List<string> args = ["-O2", "-std=c99"];
        if (options.DebugRc) {
          args.Add("-DPINO_DEBUG_RC");
        }

        args.Add($"-I{runtimeReDir}");
        args.Add($"-I{runtimeDir}");
        args.Add(cFilePath);
        foreach (string src in s_runtimeReSources) {
          args.Add(Path.Combine(runtimeReDir, src));
        }

        args.Add(Path.Combine(runtimeDir, "re.c"));
        args.Add("-lm");
        args.Add("-o");
        args.Add(outExe);

        CExecResult res = RunProcess(cc, args, repoRoot, timeout: TimeSpan.FromMinutes(2));
        sw.Stop();
        return new CBuildResult(
            Succeeded: !res.TimedOut && res.ExitCode == 0,
            CompilerUsed: cc,
            OutputPath: outExe,
            EmittedCPath: options.EmitCPath,
            Stdout: res.Stdout,
            Stderr: res.Stderr,
            CompileElapsed: sw.Elapsed);
      } else {
        string? tcc = FindTcc(repoRoot);
        if (tcc is null) {
          sw.Stop();
          return new CBuildResult(
              Succeeded: false,
              CompilerUsed: "tcc",
              OutputPath: outExe,
              EmittedCPath: options.EmitCPath,
              Stdout: string.Empty,
              Stderr: $"TCC was not found under '{Path.Combine(repoRoot, "tooling", "tcc")}' or on PATH.",
              CompileElapsed: sw.Elapsed);
        }

        List<string> args = [];
        if (options.DebugRc) {
          args.Add("-DPINO_DEBUG_RC");
        }

        args.Add($"-I{runtimeReDir}");
        args.Add($"-I{runtimeDir}");
        args.Add(cFilePath);
        foreach (string src in s_runtimeReSources) {
          args.Add(Path.Combine(runtimeReDir, src));
        }

        args.Add(Path.Combine(runtimeDir, "re.c"));
        args.Add("-o");
        args.Add(outExe);

        CExecResult res = RunProcess(tcc, args, repoRoot, timeout: TimeSpan.FromMinutes(1));
        sw.Stop();
        return new CBuildResult(
            Succeeded: !res.TimedOut && res.ExitCode == 0,
            CompilerUsed: tcc,
            OutputPath: outExe,
            EmittedCPath: options.EmitCPath,
            Stdout: res.Stdout,
            Stderr: res.Stderr,
            CompileElapsed: sw.Elapsed);
      }
    } finally {
      if (tempDir is not null) {
        try {
          Directory.Delete(tempDir, recursive: true);
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
      }
    }
  }

  public static CExecResult RunExecutable(
      string executablePath,
      string? workingDirectory = null,
      bool failOnLeak = false,
      bool closeStdin = false,
      TimeSpan? timeout = null) {
    return RunProcess(
        executablePath,
        [],
        workingDirectory ?? Environment.CurrentDirectory,
        failOnLeak: failOnLeak,
        closeStdin: closeStdin,
        timeout: timeout ?? TimeSpan.FromSeconds(30));
  }

  public static CExecResult RunProcess(
      string fileName,
      IReadOnlyList<string> args,
      string workingDirectory,
      bool failOnLeak = false,
      bool closeStdin = false,
      TimeSpan? timeout = null) {
    var startInfo = new ProcessStartInfo(fileName) {
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      RedirectStandardInput = closeStdin,
      StandardOutputEncoding = CEmitter.Utf8NoBom,
      StandardErrorEncoding = CEmitter.Utf8NoBom,
      UseShellExecute = false,
      CreateNoWindow = true,
    };

    foreach (string arg in args) {
      startInfo.ArgumentList.Add(arg);
    }

    if (failOnLeak) {
      startInfo.Environment["PINO_FAIL_ON_LEAK"] = "1";
    }

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
    if (closeStdin) {
      process.StandardInput.Close();
    }

    Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
    Task<string> stderrTask = process.StandardError.ReadToEndAsync();

    TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
    if (!process.WaitForExit((int) effectiveTimeout.TotalMilliseconds)) {
      try {
        process.Kill(entireProcessTree: true);
      } catch (InvalidOperationException) {
      }

      process.WaitForExit();
      return new CExecResult(-1, string.Empty, "Process timed out.", TimedOut: true);
    }

    process.WaitForExit();
    return new CExecResult(
        process.ExitCode,
        stdoutTask.GetAwaiter().GetResult(),
        stderrTask.GetAwaiter().GetResult(),
        TimedOut: false);
  }
}
