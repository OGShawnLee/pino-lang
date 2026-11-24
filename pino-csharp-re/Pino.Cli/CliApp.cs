using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using Pino.Compiler.Checking;
using Pino.Compiler.CodeGen.C;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.IR.Passes;
using Pino.Compiler.Lexing;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;
using Pino.Vm;

namespace Pino.Cli;

/// <summary>The <c>pino</c> command line: compiler, checker, native C99 builder, VM runner, and REPL.</summary>
public static partial class CliApp {
  private const string Prefix = "pino: ";

  // Single source of truth for option flags shared by the argument parsers and the generated Usage text.
  private const string OptColor = "--color";
  private const string OptVoice = "--voice";
  private const string OptHelp = "--help";
  private const string OptHelpShort = "-h";
  private const string OptVersion = "--version";
  private const string OptVm = "--vm";
  private const string OptC = "--c";
  private const string OptVerifyRc = "--verify-rc";
  private const string OptDebugRc = "--debug-rc";
  private const string OptOutput = "-o";
  private const string OptRelease = "--release";
  private const string OptEmitC = "--emit-c";
  private const string OptStrict = "--strict";
  private const string OptFilter = "--filter";

  internal sealed record CliOptionSpec(string Name, string? ValueSyntax, string Description);

  internal sealed record CliCommandSpec(
      string Signature,
      string Description,
      ImmutableArray<CliOptionSpec> Options);

  internal static readonly ImmutableArray<CliOptionSpec> GlobalOptions = [
    new CliOptionSpec(OptColor, "auto|always|never", "Color the output (default: auto; off when redirected or NO_COLOR is set)."),
    new CliOptionSpec(OptVoice, "pino|plain", "Show or hide Pino's friendly notes under errors (default: pino)."),
    new CliOptionSpec(OptHelp, null, "Show this help message (alias: -h)."),
    new CliOptionSpec(OptHelpShort, null, "Short alias for --help."),
    new CliOptionSpec(OptVersion, null, "Print the Pino version."),
  ];

  internal static readonly ImmutableArray<CliCommandSpec> CommandSpecs = [
    new CliCommandSpec("pino", "Run main.pino in the current directory, or show this help.", []),
    new CliCommandSpec("pino init [name]", "Create a new Pino project (main.pino, modules/, test/, .gitignore).", []),
    new CliCommandSpec("pino run [opts] [file]", "Run a .pino file (defaults to main.pino).", [
      new CliOptionSpec(OptVm, null, "Run with Pino's built-in runner (the default)."),
      new CliOptionSpec(OptC, null, "Compile to a native program, then run it."),
      new CliOptionSpec(OptVerifyRc, null, "Check for memory that is never freed while running on the built-in runner (slower)."),
      new CliOptionSpec(OptDebugRc, null, "With --c, build in checks for memory that is never freed or used after it's freed (slower)."),
    ]),
    new CliCommandSpec("pino build [opts] [file]", "Compile a .pino file to a native program (defaults to main.pino).", [
      new CliOptionSpec(OptOutput, "<out>", "Output program path."),
      new CliOptionSpec(OptRelease, null, "Build an optimized program with GCC or Clang."),
      new CliOptionSpec(OptDebugRc, null, "Build in checks for memory that is never freed or used after it's freed (slower)."),
      new CliOptionSpec(OptEmitC, "<path>", "Write the generated C code to <path>."),
    ]),
    new CliCommandSpec("pino compile [file] [out]", "Write the generated C code for a file (alias: c).", []),
    new CliCommandSpec("pino watch [opts] [file]", "Run a .pino file and rerun on save (defaults to main.pino).", [
      new CliOptionSpec(OptVm, null, "Run with Pino's built-in runner (the default)."),
      new CliOptionSpec(OptC, null, "Compile to a native program, then run it."),
      new CliOptionSpec(OptVerifyRc, null, "Check for memory that is never freed while running on the built-in runner (slower)."),
      new CliOptionSpec(OptDebugRc, null, "With --c, build in checks for memory that is never freed or used after it's freed (slower)."),
    ]),
    new CliCommandSpec("pino check <path>...", "Check .pino files or folders for errors.", [
      new CliOptionSpec(OptColor, "auto|always|never", "Color error and warning output."),
      new CliOptionSpec(OptVoice, "pino|plain", "Show or hide Pino's friendly notes under errors."),
    ]),
    new CliCommandSpec("pino repl [opts]", "Start an interactive Pino session.", [
      new CliOptionSpec(OptVerifyRc, null, "Report memory that is never freed on :quit (slower; for finding bugs)."),
    ]),
    new CliCommandSpec("pino play [game]", "Play a game from ./games/ or the installed games folder.", []),
    new CliCommandSpec("pino play update", "Download the official Pino games for this version.", []),
    new CliCommandSpec("pino test [opts] [path]", "Run test blocks in a file or folder (defaults to the current folder).", [
      new CliOptionSpec(OptVm, null, "Run with Pino's built-in runner (the default)."),
      new CliOptionSpec(OptC, null, "Compile to a native program, then run it."),
      new CliOptionSpec(OptStrict, null, "Treat memory leaks as test failures."),
      new CliOptionSpec(OptFilter, "<text>", "Run only tests whose name contains <text> (case-insensitive)."),
    ]),
    new CliCommandSpec("pino tree <file>", "Print the syntax tree of one file, then its errors.", []),
    new CliCommandSpec("pino explain <code>", "Explain an error or hint code, for example 'pino explain PN1018'.", []),
    new CliCommandSpec("pino version", "Print the Pino version (aliases: v, --version).", []),
    new CliCommandSpec("pino update", "Check for and install the latest Pino release.", []),
    new CliCommandSpec("pino help", "Show this help (aliases: h, --help, -h).", []),
  ];

  private static readonly string Usage = BuildUsage();

  private static string BuildUsage() {
    var sb = new StringBuilder();
    sb.Append("pino: the Pino compiler and toolchain\n\n");
    sb.Append("Usage:\n");
    foreach (CliCommandSpec cmd in CommandSpecs) {
      sb.Append("  ").Append(cmd.Signature.PadRight(28)).Append(cmd.Description).Append('\n');
      foreach (CliOptionSpec opt in cmd.Options) {
        string label = opt.ValueSyntax is null ? opt.Name : $"{opt.Name} {opt.ValueSyntax}";
        sb.Append("    ").Append(label.PadRight(26)).Append(opt.Description).Append('\n');
      }
    }

    sb.Append("\nGlobal options (before or after the command):\n");
    foreach (CliOptionSpec opt in GlobalOptions) {
      string label = opt.ValueSyntax is null ? opt.Name : $"{opt.Name} {opt.ValueSyntax}";
      sb.Append("  ").Append(label.PadRight(28)).Append(opt.Description).Append('\n');
    }

    sb.Append("\nExit codes:\n");
    sb.Append("  0   no errors\n");
    sb.Append("  1   errors were found (or main returned Failure/None)\n");
    sb.Append("  2   bad usage or an unreadable path\n");
    sb.Append("  101 runtime panic\n");
    return sb.ToString();
  }

  public static int Run(string[] args, TextWriter stdout, TextWriter stderr, CliEnvironment environment) =>
      Run(args, Console.In, stdout, stderr, environment);

  public static int Run(
      string[] args,
      TextReader stdin,
      TextWriter stdout,
      TextWriter stderr,
      CliEnvironment environment) {
    ArgumentNullException.ThrowIfNull(args);
    ArgumentNullException.ThrowIfNull(stdin);
    ArgumentNullException.ThrowIfNull(stdout);
    ArgumentNullException.ThrowIfNull(stderr);
    ArgumentNullException.ThrowIfNull(environment);

    try {
      return RunCore(args, stdin, stdout, stderr, environment);
    } catch (CliUsageException ex) {
      WriteError(stderr, ex.Message);
      return 2;
    } catch (Exception ex) {
      WriteError(stderr, "internal error: " + OneLine(ex.Message));
      return 2;
    }
  }

  private static int RunCore(
      string[] args,
      TextReader stdin,
      TextWriter stdout,
      TextWriter stderr,
      CliEnvironment environment) {
    if (args.Length == 0) {
      string defaultMain = Path.Combine(environment.EffectiveWorkingDirectory, "main.pino");
      if (File.Exists(defaultMain)) {
        var defaultDiagOptions = new DiagnosticRenderOptions {
          Color = ResolveColor("auto", environment),
          Voice = DiagnosticVoice.Pino,
          Notes = environment.Notes,
        };
        return RunOnVm(defaultMain, "main.pino", verifyRc: false, defaultDiagOptions, stdin, stdout, stderr, CancellationToken.None);
      }

      stdout.Write(Usage);
      return 0;
    }

    string colorMode = "auto";
    string voiceMode = "pino";
    string? command = null;
    var commandArgs = new List<string>();

    for (int i = 0; i < args.Length; i++) {
      string arg = args[i];
      if (arg is OptHelp or OptHelpShort) {
        stdout.Write(Usage);
        return 0;
      }

      if (arg is OptVersion && command is null) {
        return RunVersion([], stdout);
      }

      if (arg is OptColor) {
        colorMode = ReadOptionValue(args, ref i, ["auto", "always", "never"]);
      } else if (arg is OptVoice) {
        voiceMode = ReadOptionValue(args, ref i, ["pino", "plain"]);
      } else if (command is null) {
        if (arg.StartsWith('-')) {
          throw new CliUsageException($"unknown option '{arg}'");
        }

        command = arg;
      } else {
        commandArgs.Add(arg);
      }
    }

    if (command is null) {
      throw new CliUsageException(
          "missing command (expected 'init', 'run', 'build', 'compile', 'watch', 'check', 'repl', 'play', 'test', 'tree', 'explain', 'version', 'update', or 'help')");
    }

    var options = new DiagnosticRenderOptions {
      Color = ResolveColor(colorMode, environment),
      Voice = voiceMode == "plain" ? DiagnosticVoice.Plain : DiagnosticVoice.Pino,
      Notes = environment.Notes,
    };

    if (command is "check" or "tree" or "explain") {
      foreach (string arg in commandArgs) {
        if (arg.StartsWith('-')) {
          throw new CliUsageException($"unknown option '{arg}'");
        }
      }
    }

    return command switch {
      "help" or "h" => PrintUsage(stdout),
      "version" or "v" => RunVersion(commandArgs, stdout),
      "init" => RunInit(commandArgs, stdout, stderr, environment),
      "check" => RunCheck(commandArgs, options, stdout, environment),
      "tree" => RunTree(commandArgs, options, stdout, environment),
      "explain" => RunExplain(commandArgs, stdout, stderr),
      "run" => RunProgram(commandArgs, options, stdin, stdout, stderr, environment),
      "build" => RunBuild(commandArgs, options, stdout, stderr, environment),
      "compile" or "c" => RunCompile(commandArgs, options, stdout, stderr, environment),
      "watch" => RunWatch(commandArgs, options, stdin, stdout, stderr, environment),
      "repl" => RunRepl(commandArgs, options, stdin, stdout, stderr, environment),
      "play" => RunPlay(commandArgs, options, stdin, stdout, stderr, environment),
      "test" => RunTest(commandArgs, options, stdout, stderr, environment),
      "update" => RunUpdate(commandArgs, stdin, stdout, stderr, environment),
      _ => throw new CliUsageException($"unknown command '{command}'"),
    };
  }

  private static int PrintUsage(TextWriter stdout) {
    stdout.Write(Usage);
    return 0;
  }

  private static string ReadOptionValue(string[] args, ref int index, string[] allowed) {
    string option = args[index];
    if (index + 1 >= args.Length) {
      throw new CliUsageException($"missing value for option '{option}'");
    }

    string value = args[++index];
    if (Array.IndexOf(allowed, value) < 0) {
      throw new CliUsageException($"bad value '{value}' for option '{option}' (expected {string.Join(", ", allowed)})");
    }

    return value;
  }

  private static string ReadAnyOptionValue(List<string> args, ref int index) {
    string option = args[index];
    if (index + 1 >= args.Count) {
      throw new CliUsageException($"missing value for option '{option}'");
    }

    return args[++index];
  }

  private static bool ResolveColor(string mode, CliEnvironment environment) {
    switch (mode) {
      case "always":
        return true;
      case "never":
        return false;
      default:
        return !environment.IsOutputRedirected
            && environment.SupportsAnsi
            && string.IsNullOrEmpty(environment.GetEnvironmentVariable("NO_COLOR"));
    }
  }

  private static int RunExplain(List<string> codes, TextWriter stdout, TextWriter stderr) {
    if (codes.Count == 0) {
      stderr.Write("Usage: pino explain <code>, for example 'pino explain PN1018'.\n");
      return 1;
    }

    string code = codes[0];
    if (!DiagnosticExplanations.TryGet(code, out string? explanation)) {
      stderr.Write($"There's no explanation for '{code.ToUpperInvariant()}' yet.\n");
      return 1;
    }

    stdout.Write(explanation.ReplaceLineEndings("\n") + "\n");
    return 0;
  }

  // ---------------------------------------------------------------------------
  // `run` (IR VM or C engine) and `build` (C engine)
  // ---------------------------------------------------------------------------

  private static int RunProgram(
      List<string> args,
      DiagnosticRenderOptions diagOptions,
      TextReader stdin,
      TextWriter stdout,
      TextWriter stderr,
      CliEnvironment environment) {
    bool useVm = false;
    bool useC = false;
    bool verifyRc = false;
    bool debugRc = false;
    var files = new List<string>();

    for (int i = 0; i < args.Count; i++) {
      string arg = args[i];
      switch (arg) {
        case OptVm:
          useVm = true;
          break;
        case OptC:
          useC = true;
          break;
        case OptVerifyRc:
          verifyRc = true;
          break;
        case OptDebugRc:
          debugRc = true;
          break;
        default:
          if (arg.StartsWith('-')) {
            throw new CliUsageException($"unknown option '{arg}'");
          }

          files.Add(arg);
          break;
      }
    }

    if (useVm && useC) {
      throw new CliUsageException("cannot combine '--vm' and '--c'");
    }

    if (files.Count > 1) {
      throw new CliUsageException("'run' takes at most one file");
    }

    string requestedFile = files.Count == 1 ? files[0] : "main.pino";
    string file = ValidateSingleFilePath(requestedFile, "run", environment);

    // A flag that does not apply to the chosen engine is ignored: --verify-rc is VM-only, --debug-rc is C-only.
    return useC
        ? RunWithCEngine(file, requestedFile, debugRc, diagOptions, stdout, stderr, environment, CancellationToken.None)
        : RunOnVm(file, requestedFile, verifyRc, diagOptions, stdin, stdout, stderr, CancellationToken.None);
  }

  private static int RunBuild(
      List<string> args,
      DiagnosticRenderOptions diagOptions,
      TextWriter stdout,
      TextWriter stderr,
      CliEnvironment environment) {
    bool release = false;
    bool debugRc = false;
    string? outputPath = null;
    string? emitCPath = null;
    var files = new List<string>();

    for (int i = 0; i < args.Count; i++) {
      string arg = args[i];
      switch (arg) {
        case OptRelease:
          release = true;
          break;
        case OptDebugRc:
          debugRc = true;
          break;
        case OptOutput:
          outputPath = ReadAnyOptionValue(args, ref i);
          break;
        case OptEmitC:
          emitCPath = ReadAnyOptionValue(args, ref i);
          break;
        default:
          if (arg.StartsWith('-')) {
            throw new CliUsageException($"unknown option '{arg}'");
          }

          files.Add(arg);
          break;
      }
    }

    if (files.Count > 1) {
      throw new CliUsageException("'build' takes at most one file");
    }

    string requestedFile = files.Count == 1 ? files[0] : "main.pino";
    string file = ValidateSingleFilePath(requestedFile, "build", environment);
    string? resolvedOutput = outputPath is null ? null : ResolvePath(outputPath, environment);
    string? resolvedEmitC = emitCPath is null ? null : ResolvePath(emitCPath, environment);
    return BuildWithCEngine(file, requestedFile, resolvedOutput, release, debugRc, resolvedEmitC, diagOptions, stdout, stderr);
  }

  private static int RunWithCEngine(
      string filePath,
      string requestedDisplayPath,
      bool debugRc,
      DiagnosticRenderOptions diagOptions,
      TextWriter stdout,
      TextWriter stderr,
      CliEnvironment environment,
      CancellationToken cancellationToken) {
    if (!TryCompilePinoFileToC(filePath, requestedDisplayPath, diagOptions, stderr, out string? cSource)) {
      return 1;
    }

    string tempDir = Path.Combine(Path.GetTempPath(), "pino-run-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    string exeName = OperatingSystem.IsWindows() ? "pino_prog.exe" : "pino_prog";
    string exePath = Path.Combine(tempDir, exeName);

    try {
      CBuildResult build = CCompilerDriver.Build(new CBuildOptions(
          CSource: cSource!,
          OutputExecutablePath: exePath,
          Release: false,
          DebugRc: debugRc));
      if (!build.Succeeded) {
        WriteError(stderr, $"C compilation failed ({build.CompilerUsed}):\n{build.Stderr}{build.Stdout}".TrimEnd());
        return 1;
      }

      string? existingFailEnv = environment.GetEnvironmentVariable("PINO_FAIL_ON_LEAK");
      bool failOnLeak = debugRc && !string.Equals(existingFailEnv, "0", StringComparison.Ordinal);

      if (cancellationToken.CanBeCanceled) {
        return RunNativeProcessCancellable(
            exePath,
            environment.EffectiveWorkingDirectory,
            failOnLeak,
            stdout,
            stderr,
            cancellationToken);
      }

      CExecResult exec = CCompilerDriver.RunExecutable(
          exePath,
          workingDirectory: environment.EffectiveWorkingDirectory,
          failOnLeak: failOnLeak,
          closeStdin: false,
          timeout: TimeSpan.FromMinutes(5));

      if (!string.IsNullOrEmpty(exec.Stdout)) {
        stdout.Write(exec.Stdout);
      }

      if (!string.IsNullOrEmpty(exec.Stderr)) {
        stderr.Write(exec.Stderr);
      }

      return exec.ExitCode;
    } finally {
      try {
        Directory.Delete(tempDir, recursive: true);
      } catch (IOException) {
      } catch (UnauthorizedAccessException) {
      }
    }
  }

  private static int RunNativeProcessCancellable(
      string exePath,
      string workingDirectory,
      bool failOnLeak,
      TextWriter stdout,
      TextWriter stderr,
      CancellationToken cancellationToken) {
    var psi = new System.Diagnostics.ProcessStartInfo {
      FileName = exePath,
      WorkingDirectory = workingDirectory,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true,
    };
    if (failOnLeak) {
      psi.Environment["PINO_FAIL_ON_LEAK"] = "1";
    }

    using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(psi);
    if (process is null) {
      return 1;
    }

    using CancellationTokenRegistration reg = cancellationToken.Register(() => {
      try {
        if (!process.HasExited) {
          process.Kill(entireProcessTree: true);
        }
      } catch (Exception) {
      }
    });

    Task<string> outTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
    Task<string> errTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
    process.WaitForExit();
    Task.WaitAll(outTask, errTask);

    if (!string.IsNullOrEmpty(outTask.Result)) {
      stdout.Write(outTask.Result);
    }

    if (!string.IsNullOrEmpty(errTask.Result)) {
      stderr.Write(errTask.Result);
    }

    return process.ExitCode;
  }

  private static int BuildWithCEngine(
      string filePath,
      string requestedDisplayPath,
      string? outputPath,
      bool release,
      bool debugRc,
      string? emitCPath,
      DiagnosticRenderOptions diagOptions,
      TextWriter stdout,
      TextWriter stderr) {
    if (!TryCompilePinoFileToC(filePath, requestedDisplayPath, diagOptions, stderr, out string? cSource)) {
      return 1;
    }

    string fullInput = Path.GetFullPath(filePath);
    string resolvedOut = outputPath ?? Path.ChangeExtension(
        fullInput,
        OperatingSystem.IsWindows() ? ".exe" : null)!;

    CBuildResult build = CCompilerDriver.Build(new CBuildOptions(
        CSource: cSource!,
        OutputExecutablePath: resolvedOut,
        Release: release,
        DebugRc: debugRc,
        EmitCPath: emitCPath));

    if (!build.Succeeded) {
      WriteError(stderr, $"C compilation failed ({build.CompilerUsed}):\n{build.Stderr}{build.Stdout}".TrimEnd());
      return 1;
    }

    _ = stdout;
    return 0;
  }

  private static bool TryCompilePinoFileToC(
      string filePath,
      string requestedDisplayPath,
      DiagnosticRenderOptions diagOptions,
      TextWriter diagWriter,
      out string? cSource) {
    string displayPath = ToDisplayPath(requestedDisplayPath);
    var source = new SourceText(ReadFile(filePath, displayPath), ToDisplayPath(Path.GetFullPath(filePath)));
    ParseResult parsed = Parser.Parse(source);
    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);

    bool hasErrors = false;
    bool wroteDiag = false;
    foreach (SemanticModel model in compilation.SemanticModels) {
      if (model.Diagnostics.IsEmpty) {
        continue;
      }

      foreach (Diagnostic diag in model.Diagnostics) {
        if (diag.Severity == DiagnosticSeverity.Error) {
          hasErrors = true;
        }
      }

      if (wroteDiag) {
        diagWriter.Write('\n');
      }

      // Render all diagnostics (including PN1018 hints and warnings) without aborting on non-errors.
      diagWriter.Write(DiagnosticRenderer.RenderAll(model.Diagnostics, model.Tree.Source, diagOptions));
      wroteDiag = true;
    }

    if (hasErrors) {
      cSource = null;
      return false;
    }

    IrModule ir = IrLowerer.Lower(compilation);
    RcInsertPass.Run(ir);
    cSource = CEmitter.Emit(ir, compilation);
    return true;
  }

  private static string ResolvePath(string path, CliEnvironment environment) =>
      Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(environment.EffectiveWorkingDirectory, path));

  private static string ValidateSingleFilePath(string requestedPath, string command, CliEnvironment environment) {
    string resolved = ResolvePath(requestedPath, environment);
    if (Directory.Exists(resolved)) {
      throw new CliUsageException($"'{requestedPath}' is a directory; '{command}' takes exactly one file");
    }

    if (!File.Exists(resolved)) {
      throw new CliUsageException($"no such file or directory: '{requestedPath}'");
    }

    return resolved;
  }

  private static int RunCheck(List<string> paths, DiagnosticRenderOptions options, TextWriter stdout, CliEnvironment environment) {
    if (paths.Count == 0) {
      throw new CliUsageException("missing path (usage: pino check <path>...)");
    }

    List<InputFile> files = CollectFiles(paths, environment);

    // Read everything before printing so an unreadable file never leaves partial output behind.
    var sources = new List<SourceText>(files.Count);
    foreach (InputFile file in files) {
      sources.Add(new SourceText(ReadFile(file.FullPath, file.DisplayPath), file.DisplayPath));
    }

    var inputFullPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (InputFile file in files) {
      inputFullPaths.Add(file.FullPath);
    }

    var reportedExternalFullPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    int errors = 0;
    int warnings = 0;
    bool wroteFile = false;

    foreach (SourceText source in sources) {
      ParseResult result = Parser.Parse(source);
      Compilation compilation = Compilation.Create(result, new WorkingDirectoryModuleSource(environment.EffectiveWorkingDirectory));
      SemanticModel rootModel = compilation.RootModel;
      (int fileErrors, int fileWarnings) = Count(rootModel.Diagnostics);
      errors += fileErrors;
      warnings += fileWarnings;

      if (!rootModel.Diagnostics.IsEmpty) {
        if (wroteFile) {
          stdout.Write('\n');
        }

        stdout.Write(DiagnosticRenderer.RenderAll(rootModel.Diagnostics, source, options));
        wroteFile = true;
      }

      foreach (SemanticModel depModel in compilation.SemanticModels
          .OrderBy(static m => m.Tree.Source.FilePath ?? string.Empty, StringComparer.Ordinal)) {
        if (ReferenceEquals(depModel, rootModel) || depModel.Diagnostics.IsEmpty) {
          continue;
        }

        string? depPath = depModel.Tree.Source.FilePath;
        if (string.IsNullOrEmpty(depPath)) {
          continue;
        }

        string depFullPath = Path.GetFullPath(depPath);
        if (inputFullPaths.Contains(depFullPath) || !reportedExternalFullPaths.Add(depFullPath)) {
          continue;
        }

        (int depErrors, int depWarnings) = Count(depModel.Diagnostics);
        errors += depErrors;
        warnings += depWarnings;

        if (wroteFile) {
          stdout.Write('\n');
        }

        stdout.Write(DiagnosticRenderer.RenderAll(depModel.Diagnostics, depModel.Tree.Source, options));
        wroteFile = true;
      }
    }

    stdout.Write(string.Create(
        CultureInfo.InvariantCulture,
        $"checked {files.Count} file(s): {errors} error(s), {warnings} warning(s)\n"));
    return errors > 0 ? 1 : 0;
  }

  private static int RunTree(List<string> paths, DiagnosticRenderOptions options, TextWriter stdout, CliEnvironment environment) {
    if (paths.Count == 0) {
      throw new CliUsageException("missing path (usage: pino tree <file>)");
    }

    if (paths.Count > 1) {
      throw new CliUsageException("'tree' takes exactly one file");
    }

    string requestedPath = paths[0];
    string path = ValidateSingleFilePath(requestedPath, "tree", environment);

    string displayPath = ToDisplayPath(requestedPath);
    var source = new SourceText(ReadFile(path, displayPath), displayPath);
    ParseResult result = Parser.Parse(source);

    var sb = new StringBuilder();
    AppendTree(sb, result.Root, source);
    stdout.Write(sb.ToString());

    if (!result.Diagnostics.IsEmpty) {
      stdout.Write('\n');
      stdout.Write(DiagnosticRenderer.RenderAll(result.Diagnostics, source, options));
    }

    return Count(result).Errors > 0 ? 1 : 0;
  }

  // ---------------------------------------------------------------------------
  // run --vm: execute one file on the IR VM (ticket 063)
  // ---------------------------------------------------------------------------

  private static int RunOnVm(
      string path,
      string requestedDisplayPath,
      bool verifyRc,
      DiagnosticRenderOptions options,
      TextReader stdin,
      TextWriter stdout,
      TextWriter stderr,
      CancellationToken cancellationToken) {
    string displayPath = ToDisplayPath(requestedDisplayPath);
    var source = new SourceText(ReadFile(path, displayPath), ToDisplayPath(Path.GetFullPath(path)));
    ParseResult parsed = Parser.Parse(source);
    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);

    // Hints and warnings are printed but never stop the run; only errors do.
    int errors = 0;
    var seenModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var models = new List<SemanticModel> { compilation.RootModel };
    models.AddRange(compilation.SemanticModels
        .Where(m => !ReferenceEquals(m, compilation.RootModel))
        .OrderBy(static m => m.Tree.Source.FilePath ?? string.Empty, StringComparer.Ordinal));
    foreach (SemanticModel model in models) {
      string key = model.Tree.Source.FilePath ?? string.Empty;
      if (model.Diagnostics.IsEmpty || !seenModules.Add(key)) {
        continue;
      }

      errors += Count(model.Diagnostics).Errors;
      stderr.Write(DiagnosticRenderer.RenderAll(model.Diagnostics, model.Tree.Source, options));
    }

    if (errors > 0) {
      return 1;
    }

    IrModule module = IrLowerer.Lower(compilation);
    RcInsertPass.Run(module);
    IrVerificationResult verification = IrVerifier.VerifyAfterRc(module);
    if (!verification.IsValid) {
      stderr.Write("pino: the program could not be run because its IR failed verification: "
          + OneLine(string.Join("; ", verification.Errors.Take(3))) + "\n");
      return 1;
    }

    var host = new ConsoleVmHost(stdout, stderr, stdin);
    var vm = new PinoVm(
        module,
        host,
        new PinoVmOptions {
          VerifyRc = verifyRc,
          RootSource = source,
          CancellationToken = cancellationToken,
        });
    PinoVmResult result = vm.Run();
    stdout.Flush();

    if (result.RcError is { } rcError) {
      stderr.Write(rcError + "\n");
      return 1;
    }

    int exitCode = result.ExitCode;
    if (result.LeakReport is { } report) {
      if (report.CycleLeaks.Count > 0) {
        stderr.Write(report.Format());
      }

      if (!report.IsClean) {
        stderr.Write($"pino: {report.Leaks.Count} object(s) were still alive when the program ended.\n");
        if (exitCode == 0) {
          exitCode = 1;
        }
      }
    }

    return exitCode;
  }

  // ---------------------------------------------------------------------------
  // `repl`: interactive IR VM session (ticket 070)
  // ---------------------------------------------------------------------------

  private const string ReplHelp = """
      REPL commands:
        :help   Show this help message.
        :reset  Clear all bindings, functions, and types in the session.
        :quit   Exit the REPL.
      """ + "\n";

  private static int RunRepl(
      List<string> args,
      DiagnosticRenderOptions diagOptions,
      TextReader stdin,
      TextWriter stdout,
      TextWriter stderr,
      CliEnvironment environment) {
    bool verifyRc = false;
    foreach (string arg in args) {
      if (arg == OptVerifyRc) {
        verifyRc = true;
      } else if (arg.StartsWith('-')) {
        throw new CliUsageException($"unknown option '{arg}'");
      } else {
        throw new CliUsageException($"unexpected argument '{arg}' for 'repl'");
      }
    }

    var host = new ConsoleVmHost(stdout, stderr, stdin);
    var session = new VmSession(host, diagOptions, verifyRc: verifyRc);
    bool interactive = !environment.IsOutputRedirected;

    if (interactive) {
      stdout.Write("Pino REPL — type :help for commands, :quit to exit.\n");
      stdout.Flush();
    }

    var buffer = new StringBuilder();
    while (true) {
      if (interactive) {
        stdout.Write(buffer.Length == 0 ? "pino> " : "... ");
        stdout.Flush();
      }

      string? line = stdin.ReadLine();
      if (line is null) {
        if (buffer.Length > 0) {
          session.Submit(buffer.ToString());
          buffer.Clear();
        }

        break;
      }

      if (line.Length > 0 && line[0] == '\uFEFF') {
        line = line[1..];
      }

      if (buffer.Length == 0) {
        string trimmed = line.Trim();
        if (trimmed.Length == 0) {
          continue;
        }

        if (trimmed.StartsWith(':')) {
          switch (trimmed) {
            case ":help":
              stdout.Write(ReplHelp);
              stdout.Flush();
              continue;
            case ":reset":
              session.Reset();
              stdout.Write("Session reset.\n");
              stdout.Flush();
              continue;
            case ":quit":
              return FinishReplSession(session, stderr);
            default:
              stderr.Write($"Unknown REPL command '{trimmed}'. Type :help for available commands.\n");
              stderr.Flush();
              continue;
          }
        }
      }

      if (buffer.Length > 0) {
        buffer.Append('\n');
      }

      buffer.Append(line);
      if (VmSession.NeedsMoreInput(buffer.ToString())) {
        continue;
      }

      session.Submit(buffer.ToString());
      buffer.Clear();
    }

    return FinishReplSession(session, stderr);
  }

  private static int FinishReplSession(VmSession session, TextWriter stderr) {
    PinoVmResult shutdown = session.Shutdown();
    if (shutdown.RcError is { } rcError) {
      stderr.Write(rcError + "\n");
      stderr.Flush();
      return 1;
    }

    if (shutdown.LeakReport is { } report) {
      if (report.CycleLeaks.Count > 0) {
        stderr.Write(report.Format());
        stderr.Flush();
      }

      if (!report.IsClean) {
        stderr.Write($"pino: {report.Leaks.Count} object(s) were still alive when the REPL session ended.\n");
        stderr.Flush();
        return 1;
      }
    }

    return 0;
  }

  private sealed class ConsoleVmHost(TextWriter stdout, TextWriter stderr, TextReader? stdin = null) : IVmHost {
    public TextWriter Out { get; } = stdout;

    public TextWriter Err { get; } = stderr;

    public TextReader In { get; } = stdin ?? Console.In;

    public IVmFileSystem Fs { get; } = new DiskVmFileSystem();

    public Random Random { get; } = new();

    public double ClockMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);

    public void Clear() => Out.Write("\u001b[2J\u001b[H");
  }

  private sealed class DiskVmFileSystem : IVmFileSystem {
    public bool TryReadAllText(string path, out string content, out VmFileError error) {
      try {
        content = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        error = default;
        return true;
      } catch (Exception ex) {
        content = string.Empty;
        error = Describe(ex);
        return false;
      }
    }

    public bool TryWriteAllText(string path, string content, out VmFileError error) {
      try {
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        error = default;
        return true;
      } catch (Exception ex) {
        error = Describe(ex);
        return false;
      }
    }

    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static VmFileError Describe(Exception ex) => ex switch {
      FileNotFoundException or DirectoryNotFoundException => new VmFileError(VmFileErrorKind.NotFound, ex.Message),
      UnauthorizedAccessException => new VmFileError(VmFileErrorKind.PermissionDenied, ex.Message),
      _ => new VmFileError(VmFileErrorKind.Other, OneLine(ex.Message)),
    };
  }

  private static (int Errors, int Warnings) Count(ParseResult result) =>
      Count(result.Diagnostics);

  private static (int Errors, int Warnings) Count(ImmutableArray<Diagnostic> diagnostics) {
    int errors = 0;
    int warnings = 0;
    foreach (Diagnostic diagnostic in diagnostics) {
      if (diagnostic.Severity == DiagnosticSeverity.Error) {
        errors++;
      } else if (diagnostic.Severity == DiagnosticSeverity.Warning) {
        warnings++;
      }
    }

    return (errors, warnings);
  }

  private static void AppendTree(StringBuilder sb, SyntaxNode root, SourceText source) {
    var stack = new Stack<(SyntaxElement Element, int Depth)>();
    stack.Push((new SyntaxElement(root), 0));

    while (stack.Count > 0) {
      (SyntaxElement element, int depth) = stack.Pop();
      sb.Append(' ', depth * 2);

      if (element.IsToken) {
        Token token = element.AsToken();
        sb.Append(token.Kind.ToString());
        if ((token.Flags & TokenFlags.Missing) != 0) {
          sb.Append(" <missing>");
        } else {
          sb.Append(" '").Append(EscapeTokenText(TokenText(token, source))).Append('\'');
        }

        sb.Append('\n');
        continue;
      }

      SyntaxNode node = element.AsNode();
      string name = node.GetType().Name;
      if (name.EndsWith("Syntax", StringComparison.Ordinal) && name.Length > "Syntax".Length) {
        name = name[..^"Syntax".Length];
      }

      sb.Append(name).Append('\n');

      SyntaxElement[] children = node.GetChildren().ToArray();
      for (int i = children.Length - 1; i >= 0; i--) {
        stack.Push((children[i], depth + 1));
      }
    }
  }

  // Multi-line token text (a triple-quoted string part, for example) must not break the one-token-per-line tree.
  private static string EscapeTokenText(string text) =>
      text.Replace("\n", "\\n", StringComparison.Ordinal)
          .Replace("\r", "\\r", StringComparison.Ordinal)
          .Replace("\t", "\\t", StringComparison.Ordinal);

  private static string TokenText(Token token, SourceText source) {
    int start = Math.Clamp(token.Span.Start, 0, source.Length);
    int end = Math.Clamp(token.Span.End, start, source.Length);
    return source.Text.Substring(start, end - start);
  }

  private static List<InputFile> CollectFiles(List<string> paths, CliEnvironment environment) {
    var byFullPath = new Dictionary<string, InputFile>(StringComparer.Ordinal);

    foreach (string path in paths) {
      string resolved = ResolvePath(path, environment);
      if (File.Exists(resolved)) {
        Add(byFullPath, new InputFile(ToDisplayPath(path), resolved));
      } else if (Directory.Exists(resolved)) {
        string root = ToDisplayPath(path).TrimEnd('/');
        IEnumerable<string> found;
        try {
          found = Directory.EnumerateFiles(resolved, "*.pino", SearchOption.AllDirectories).ToList();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
          throw new CliUsageException($"cannot read directory '{path}': {OneLine(ex.Message)}");
        }

        foreach (string file in found) {
          string relative = ToDisplayPath(Path.GetRelativePath(resolved, file));
          Add(byFullPath, new InputFile(root + "/" + relative, Path.GetFullPath(file)));
        }
      } else {
        throw new CliUsageException($"no such file or directory: '{path}'");
      }
    }

    return byFullPath.Values
        .OrderBy(static f => f.DisplayPath, StringComparer.Ordinal)
        .ToList();
  }

  private static void Add(Dictionary<string, InputFile> byFullPath, InputFile file) {
    byFullPath.TryAdd(file.FullPath, file);
  }

  private static string ReadFile(string path, string displayPath) {
    try {
      return File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      throw new CliUsageException($"cannot read '{displayPath}': {OneLine(ex.Message)}");
    }
  }

  private static string ToDisplayPath(string path) {
    string display = path.Replace(Path.DirectorySeparatorChar, '/');
    return Path.AltDirectorySeparatorChar == '/' ? display : display.Replace(Path.AltDirectorySeparatorChar, '/');
  }

  private static void WriteError(TextWriter stderr, string message) {
    stderr.Write(Prefix + OneLine(message) + "\n");
  }

  private static string OneLine(string text) =>
      text.ReplaceLineEndings(" ").Trim();

  private sealed class WorkingDirectoryModuleSource(string workingDirectory) : IModuleSource {
    public bool TryResolveModule(
        string? importingFilePath,
        string moduleName,
        out ParseResult? parseResult,
        out string? expectedPath) {
      string? resolvedImporting = string.IsNullOrWhiteSpace(importingFilePath) || Path.IsPathRooted(importingFilePath)
          ? importingFilePath
          : Path.GetFullPath(Path.Combine(workingDirectory, importingFilePath));
      return FileSystemModuleSource.Instance.TryResolveModule(resolvedImporting, moduleName, out parseResult, out expectedPath);
    }

    public bool IsTestFile(string? filePath) => FileSystemModuleSource.Instance.IsTestFile(filePath);
  }

  private readonly record struct InputFile(string DisplayPath, string FullPath);

  private sealed class CliUsageException(string message) : Exception(message);
}
