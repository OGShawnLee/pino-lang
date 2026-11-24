using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pino.Compiler.CodeGen.C;

public sealed record CBuildOptions(
    string CSource,
    string OutputExecutablePath,
    bool Release = false,
    bool DebugRc = false,
    string? EmitCPath = null,
    string? RepoRoot = null,
    string? ExecutableDirectory = null,
    string? WorkingDirectory = null,
    string? PathOverride = null,
    OSPlatform? TargetPlatform = null);

public sealed record CBuildResult(
    bool Succeeded,
    string CompilerUsed,
    string OutputPath,
    string? EmittedCPath,
    string Stdout,
    string Stderr,
    TimeSpan CompileElapsed,
    bool IsUserFacingError = false);

public sealed record CExecResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut = false);

/// <summary>
/// Compiles generated C99 + <c>runtime-re/*.c</c> via bundled TCC (dev),
/// falling back to the system C compiler (<c>cc</c>, <c>clang</c>, <c>gcc</c>) when no bundled TCC
/// exists, or GCC/Clang (<c>--release</c>), and runs compiled binaries.
/// </summary>
public static class CCompilerDriver {
  private static readonly string[] s_runtimeReSources = [
    "pino_alloc.c",
    "pino_str.c",
    "pino_io.c",
    "pino_regex.c",
  ];

  public static bool HasRuntimeLayout(string? directory) =>
      !string.IsNullOrWhiteSpace(directory)
      && File.Exists(Path.Combine(directory, "runtime-re", "pino_rt.h"))
      && File.Exists(Path.Combine(directory, "runtime-re", "pino_regex.c"));

  public static string? GetDefaultExecutableDirectory() {
    string? processPath = Environment.ProcessPath;
    if (!string.IsNullOrWhiteSpace(processPath)) {
      string? processDir = Path.GetDirectoryName(Path.GetFullPath(processPath));
      if (!string.IsNullOrWhiteSpace(processDir)) {
        return processDir;
      }
    }

    return AppContext.BaseDirectory;
  }

  public static string FindRepositoryRoot(string? startDirectory = null, string? executableDirectory = null) {
    // 1. Installed layout: the folder of the running pino executable wins whenever it holds
    //    runtime-re/pino_rt.h and runtime-re/pino_regex.c, regardless of the current working directory.
    string? exeDir = executableDirectory ?? GetDefaultExecutableDirectory();
    if (!string.IsNullOrWhiteSpace(exeDir)) {
      string fullExeDir = Path.GetFullPath(exeDir);
      if (HasRuntimeLayout(fullExeDir)) {
        return fullExeDir;
      }
    }

    // 2. Development layout: walk up from the start/current directory, then from the
    //    executable/AppContext base directory.
    string?[] seeds = startDirectory is not null
        ? [startDirectory, Environment.CurrentDirectory, exeDir, AppContext.BaseDirectory]
        : [Environment.CurrentDirectory, exeDir, AppContext.BaseDirectory];

    foreach (string? seed in seeds) {
      if (string.IsNullOrWhiteSpace(seed)) {
        continue;
      }

      for (DirectoryInfo? dir = new(Path.GetFullPath(seed)); dir is not null; dir = dir.Parent) {
        if (HasRuntimeLayout(dir.FullName)) {
          return dir.FullName;
        }
      }
    }

    throw new DirectoryNotFoundException(
        "Could not locate the Pino runtime directory (runtime-re/pino_rt.h and runtime-re/pino_regex.c).");
  }

  public static string? FindBundledTcc(string repoRoot, OSPlatform? platform = null) {
    OSPlatform os = platform ?? CurrentPlatform();
    if (os != OSPlatform.Windows) {
      return null;
    }

    string bundled = Path.Combine(repoRoot, "tooling", "tcc", "tcc.exe");
    return File.Exists(bundled) ? bundled : null;
  }

  public static string? FindTcc(string repoRoot, string? pathOverride = null, OSPlatform? platform = null) =>
      FindBundledTcc(repoRoot, platform) ?? FindOnPath("tcc", pathOverride, platform);

  public static string? FindSystemCompiler(string? pathOverride = null, OSPlatform? platform = null) =>
      FindOnPath("cc", pathOverride, platform)
      ?? FindOnPath("clang", pathOverride, platform)
      ?? FindOnPath("gcc", pathOverride, platform);

  public static string? FindReleaseCompiler(string? pathOverride = null, OSPlatform? platform = null) =>
      FindOnPath("gcc", pathOverride, platform)
      ?? FindOnPath("clang", pathOverride, platform)
      ?? FindOnPath("cc", pathOverride, platform);

  public static string? FindOnPath(string toolName, string? pathOverride = null, OSPlatform? platform = null) {
    OSPlatform os = platform ?? CurrentPlatform();
    string[] extensions = os == OSPlatform.Windows ? [".exe", ".cmd", ".bat"] : [string.Empty];
    string path = pathOverride ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
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

  public static string FormatMissingCompilerMessage(OSPlatform? platform = null) {
    OSPlatform os = platform ?? CurrentPlatform();
    if (os == OSPlatform.OSX) {
      return "pino: --c needs a C compiler. Install Apple's command line tools with: xcode-select --install";
    }

    if (os == OSPlatform.Windows) {
      return "pino: --c needs TinyCC, which should come with Pino. Reinstall Pino, or put gcc or clang on your PATH.";
    }

    return "pino: --c needs a C compiler and the C library headers (for example: sudo apt install build-essential).";
  }

  public static string FormatMissingLinuxHeadersMessage() =>
      "pino: --c needs the C library headers (for example: sudo apt install libc6-dev, or glibc-devel on Fedora).";

  public static bool LooksLikeMissingLinuxLibcHeaders(string stderr) {
    if (string.IsNullOrWhiteSpace(stderr)) {
      return false;
    }

    return stderr.Contains("crt1.o", StringComparison.Ordinal)
        || stderr.Contains("crti.o", StringComparison.Ordinal)
        || stderr.Contains("crtn.o", StringComparison.Ordinal)
        || stderr.Contains("include file 'stdio.h' not found", StringComparison.Ordinal)
        || stderr.Contains("include file 'stdlib.h' not found", StringComparison.Ordinal)
        || stderr.Contains("include file 'stdint.h' not found", StringComparison.Ordinal)
        || stderr.Contains("stdio.h: No such file or directory", StringComparison.Ordinal)
        || stderr.Contains("'stdio.h' file not found", StringComparison.Ordinal);
  }

  private static OSPlatform CurrentPlatform() =>
      OperatingSystem.IsWindows()
          ? OSPlatform.Windows
          : OperatingSystem.IsMacOS()
              ? OSPlatform.OSX
              : OSPlatform.Linux;

  public static CBuildResult Build(CBuildOptions options) {
    ArgumentNullException.ThrowIfNull(options);
    OSPlatform targetPlatform = options.TargetPlatform ?? CurrentPlatform();
    string repoRoot = options.RepoRoot ?? FindRepositoryRoot(options.WorkingDirectory, options.ExecutableDirectory);
    string runtimeReDir = Path.Combine(repoRoot, "runtime-re");

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
        string? cc = FindReleaseCompiler(options.PathOverride, targetPlatform);
        if (cc is null) {
          sw.Stop();
          return new CBuildResult(
              Succeeded: false,
              CompilerUsed: "gcc/clang",
              OutputPath: outExe,
              EmittedCPath: options.EmitCPath,
              Stdout: string.Empty,
              Stderr: FormatMissingCompilerMessage(targetPlatform),
              CompileElapsed: sw.Elapsed,
              IsUserFacingError: true);
        }

        List<string> args = BuildSystemCompilerArgs(
            cFilePath,
            runtimeReDir,
            outExe,
            optimize: true,
            debugRc: options.DebugRc);

        CExecResult res = RunProcess(cc, args, repoRoot, timeout: TimeSpan.FromMinutes(2));
        sw.Stop();
        return FinishCompileResult(res, cc, outExe, options.EmitCPath, sw.Elapsed, targetPlatform);
      } else {
        string? bundledTcc = FindBundledTcc(repoRoot, targetPlatform);
        if (bundledTcc is not null) {
          List<string> args = BuildTccArgs(
              bundledTcc,
              isBundled: true,
              cFilePath,
              runtimeReDir,
              outExe,
              options.DebugRc,
              targetPlatform);

          CExecResult res = RunProcess(bundledTcc, args, repoRoot, timeout: TimeSpan.FromMinutes(1));
          sw.Stop();
          return FinishCompileResult(res, bundledTcc, outExe, options.EmitCPath, sw.Elapsed, targetPlatform);
        }

        string? systemCc = FindSystemCompiler(options.PathOverride, targetPlatform);
        if (systemCc is not null) {
          List<string> args = BuildSystemCompilerArgs(
              cFilePath,
              runtimeReDir,
              outExe,
              optimize: false,
              debugRc: options.DebugRc);

          CExecResult res = RunProcess(systemCc, args, repoRoot, timeout: TimeSpan.FromMinutes(2));
          sw.Stop();
          return FinishCompileResult(res, systemCc, outExe, options.EmitCPath, sw.Elapsed, targetPlatform);
        }

        string? pathTcc = FindOnPath("tcc", options.PathOverride, targetPlatform);
        if (pathTcc is not null) {
          List<string> args = BuildTccArgs(
              pathTcc,
              isBundled: false,
              cFilePath,
              runtimeReDir,
              outExe,
              options.DebugRc,
              targetPlatform);

          CExecResult res = RunProcess(pathTcc, args, repoRoot, timeout: TimeSpan.FromMinutes(1));
          sw.Stop();
          return FinishCompileResult(res, pathTcc, outExe, options.EmitCPath, sw.Elapsed, targetPlatform);
        }

        sw.Stop();
        return new CBuildResult(
            Succeeded: false,
            CompilerUsed: "none",
            OutputPath: outExe,
            EmittedCPath: options.EmitCPath,
            Stdout: string.Empty,
            Stderr: FormatMissingCompilerMessage(targetPlatform),
            CompileElapsed: sw.Elapsed,
            IsUserFacingError: true);
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

  private static List<string> BuildSystemCompilerArgs(
      string cFilePath,
      string runtimeReDir,
      string outExe,
      bool optimize,
      bool debugRc) {
    List<string> args = [];
    if (optimize) {
      args.Add("-O2");
    }

    args.Add("-std=c99");
    if (debugRc) {
      args.Add("-DPINO_DEBUG_RC");
    }

    args.Add($"-I{runtimeReDir}");
    args.Add(cFilePath);
    foreach (string src in s_runtimeReSources) {
      args.Add(Path.Combine(runtimeReDir, src));
    }

    args.Add("-lm");
    args.Add("-o");
    args.Add(outExe);
    return args;
  }

  private static List<string> BuildTccArgs(
      string tccPath,
      bool isBundled,
      string cFilePath,
      string runtimeReDir,
      string outExe,
      bool debugRc,
      OSPlatform targetPlatform) {
    List<string> args = [];
    if (isBundled && targetPlatform != OSPlatform.Windows) {
      string? tccDir = Path.GetDirectoryName(tccPath);
      if (!string.IsNullOrEmpty(tccDir)) {
        args.Add($"-B{tccDir}");
      }
    }

    if (debugRc) {
      args.Add("-DPINO_DEBUG_RC");
    }

    args.Add($"-I{runtimeReDir}");
    args.Add(cFilePath);
    foreach (string src in s_runtimeReSources) {
      args.Add(Path.Combine(runtimeReDir, src));
    }

    if (targetPlatform != OSPlatform.Windows) {
      args.Add("-lm");
    }

    args.Add("-o");
    args.Add(outExe);
    return args;
  }

  private static CBuildResult FinishCompileResult(
      CExecResult res,
      string compilerUsed,
      string outExe,
      string? emitCPath,
      TimeSpan elapsed,
      OSPlatform targetPlatform) {
    bool succeeded = !res.TimedOut && res.ExitCode == 0;
    if (!succeeded && targetPlatform == OSPlatform.Linux && LooksLikeMissingLinuxLibcHeaders(res.Stderr)) {
      string detail = res.Stderr.Trim();
      string message = string.IsNullOrEmpty(detail)
          ? FormatMissingLinuxHeadersMessage()
          : FormatMissingLinuxHeadersMessage() + "\n" + detail;
      return new CBuildResult(
          Succeeded: false,
          CompilerUsed: compilerUsed,
          OutputPath: outExe,
          EmittedCPath: emitCPath,
          Stdout: res.Stdout,
          Stderr: message,
          CompileElapsed: elapsed,
          IsUserFacingError: true);
    }

    return new CBuildResult(
        Succeeded: succeeded,
        CompilerUsed: compilerUsed,
        OutputPath: outExe,
        EmittedCPath: emitCPath,
        Stdout: res.Stdout,
        Stderr: res.Stderr,
        CompileElapsed: elapsed);
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
