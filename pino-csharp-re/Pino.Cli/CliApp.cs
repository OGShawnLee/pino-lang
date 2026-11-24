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

namespace Pino.Cli;

/// <summary>The <c>pino-re</c> command line: checker, native C99 builder, and runner for the C# rewrite.</summary>
public static class CliApp {
  private const string Prefix = "pino-re: ";

  private const string Usage = """
      pino-re: the Pino compiler and front-end checker

      Usage:
        pino-re check <path>...   Lex, parse, and check .pino files (or directories,
                                  searched recursively) and print their diagnostics.
        pino-re run [opts] <file> Compile and execute a .pino file.
        pino-re build [opts] <file>
                                  Compile a .pino file to a native executable.
        pino-re tree <file>       Print the syntax tree of one file, then its diagnostics.
        pino-re explain <code>    Explain a diagnostic code, for example 'pino-re explain PN1018'.
        pino-re --help            Show this help (also -h, or no arguments).

      Options (before or after the paths):
        --color auto|always|never   Color the output (default: auto; off when output is
                                    redirected, NO_COLOR is set, or ANSI is unsupported).
        --voice pino|plain          Include the '= Pino:' notes, or leave them out
                                    (default: pino).

      Run options:
        --vm                        Run on the IR VM (default).
        --c                         Compile via TCC and run the native binary.
        --verify-rc                 Enable VM shadow-RC verification.
        --debug-rc                  Build with -DPINO_DEBUG_RC leak and UAF checking.

      Build options:
        -o <out>                    Output executable path.
        --release                   Build with gcc/clang -O2 instead of TCC.
        --debug-rc                  Build with -DPINO_DEBUG_RC leak and UAF checking.
        --emit-c <path>             Write the generated C99 source to <path>.

      Exit codes:
        0  no errors
        1  errors were found (or main returned Failure/None)
        2  bad usage or an unreadable path
        101 runtime panic
      """ + "\n";

  public static int Run(string[] args, TextWriter stdout, TextWriter stderr, CliEnvironment environment) {
    ArgumentNullException.ThrowIfNull(args);
    ArgumentNullException.ThrowIfNull(stdout);
    ArgumentNullException.ThrowIfNull(stderr);
    ArgumentNullException.ThrowIfNull(environment);

    try {
      return RunCore(args, stdout, stderr, environment);
    } catch (CliUsageException ex) {
      WriteError(stderr, ex.Message);
      return 2;
    } catch (Exception ex) {
      WriteError(stderr, "internal error: " + OneLine(ex.Message));
      return 2;
    }
  }

  private static int RunCore(string[] args, TextWriter stdout, TextWriter stderr, CliEnvironment environment) {
    if (args.Length == 0) {
      stdout.Write(Usage);
      return 0;
    }

    string colorMode = "auto";
    string voiceMode = "pino";
    string? command = null;
    var commandArgs = new List<string>();

    for (int i = 0; i < args.Length; i++) {
      string arg = args[i];
      if (arg is "--help" or "-h") {
        stdout.Write(Usage);
        return 0;
      }

      if (arg is "--color") {
        colorMode = ReadOptionValue(args, ref i, ["auto", "always", "never"]);
      } else if (arg is "--voice") {
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
      throw new CliUsageException("missing command (expected 'check', 'run', 'build', 'tree', or 'explain')");
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
      "check" => RunCheck(commandArgs, options, stdout),
      "tree" => RunTree(commandArgs, options, stdout),
      "explain" => RunExplain(commandArgs, stdout, stderr),
      "run" => RunProgram(commandArgs, options, stdout, stderr, environment),
      "build" => RunBuild(commandArgs, options, stdout, stderr),
      _ => throw new CliUsageException($"unknown command '{command}'"),
    };
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
      stderr.Write("Usage: pino-re explain <code>, for example 'pino-re explain PN1018'.\n");
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
  // Shared `run` dispatch (shared with 063 VM) & native C `run` / `build`
  // ---------------------------------------------------------------------------

  private static int RunProgram(
      List<string> args,
      DiagnosticRenderOptions diagOptions,
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
        case "--vm":
          useVm = true;
          break;
        case "--c":
          useC = true;
          break;
        case "--verify-rc":
          verifyRc = true;
          break;
        case "--debug-rc":
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

    if (files.Count == 0) {
      throw new CliUsageException("missing path (usage: pino-re run [--vm | --c] [--verify-rc] [--debug-rc] <file>)");
    }

    if (files.Count > 1) {
      throw new CliUsageException("'run' takes exactly one file");
    }

    string file = ValidateSingleFilePath(files[0], "run");

    if (!useC) {
      _ = verifyRc;
      stderr.Write("The VM engine isn't available in this build yet.\n");
      return 1;
    }

    return RunWithCEngine(file, debugRc, diagOptions, stdout, stderr, environment);
  }

  private static int RunBuild(
      List<string> args,
      DiagnosticRenderOptions diagOptions,
      TextWriter stdout,
      TextWriter stderr) {
    bool release = false;
    bool debugRc = false;
    string? outputPath = null;
    string? emitCPath = null;
    var files = new List<string>();

    for (int i = 0; i < args.Count; i++) {
      string arg = args[i];
      switch (arg) {
        case "--release":
          release = true;
          break;
        case "--debug-rc":
          debugRc = true;
          break;
        case "-o":
          outputPath = ReadAnyOptionValue(args, ref i);
          break;
        case "--emit-c":
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

    if (files.Count == 0) {
      throw new CliUsageException("missing path (usage: pino-re build <file> [-o <out>] [--release] [--debug-rc] [--emit-c <path>])");
    }

    if (files.Count > 1) {
      throw new CliUsageException("'build' takes exactly one file");
    }

    string file = ValidateSingleFilePath(files[0], "build");
    return BuildWithCEngine(file, outputPath, release, debugRc, emitCPath, diagOptions, stdout, stderr);
  }

  private static int RunWithCEngine(
      string filePath,
      bool debugRc,
      DiagnosticRenderOptions diagOptions,
      TextWriter stdout,
      TextWriter stderr,
      CliEnvironment environment) {
    if (!TryCompilePinoFileToC(filePath, diagOptions, stderr, out string? cSource)) {
      return 1;
    }

    string tempDir = Path.Combine(Path.GetTempPath(), "pino-re-run-" + Guid.NewGuid().ToString("N"));
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

      CExecResult exec = CCompilerDriver.RunExecutable(
          exePath,
          workingDirectory: Environment.CurrentDirectory,
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

  private static int BuildWithCEngine(
      string filePath,
      string? outputPath,
      bool release,
      bool debugRc,
      string? emitCPath,
      DiagnosticRenderOptions diagOptions,
      TextWriter stdout,
      TextWriter stderr) {
    if (!TryCompilePinoFileToC(filePath, diagOptions, stderr, out string? cSource)) {
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
      DiagnosticRenderOptions diagOptions,
      TextWriter diagWriter,
      out string? cSource) {
    string displayPath = ToDisplayPath(filePath);
    var source = new SourceText(ReadFile(filePath, displayPath), displayPath);
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

  private static string ValidateSingleFilePath(string path, string command) {
    if (Directory.Exists(path)) {
      throw new CliUsageException($"'{path}' is a directory; '{command}' takes exactly one file");
    }

    if (!File.Exists(path)) {
      throw new CliUsageException($"no such file or directory: '{path}'");
    }

    return path;
  }

  private static int RunCheck(List<string> paths, DiagnosticRenderOptions options, TextWriter stdout) {
    if (paths.Count == 0) {
      throw new CliUsageException("missing path (usage: pino-re check <path>...)");
    }

    List<InputFile> files = CollectFiles(paths);

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
      Compilation compilation = Compilation.Create(result, FileSystemModuleSource.Instance);
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

  private static int RunTree(List<string> paths, DiagnosticRenderOptions options, TextWriter stdout) {
    if (paths.Count == 0) {
      throw new CliUsageException("missing path (usage: pino-re tree <file>)");
    }

    if (paths.Count > 1) {
      throw new CliUsageException("'tree' takes exactly one file");
    }

    string path = paths[0];
    if (Directory.Exists(path)) {
      throw new CliUsageException($"'{path}' is a directory; 'tree' takes exactly one file");
    }

    if (!File.Exists(path)) {
      throw new CliUsageException($"no such file or directory: '{path}'");
    }

    string displayPath = ToDisplayPath(path);
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

  private static List<InputFile> CollectFiles(List<string> paths) {
    var byFullPath = new Dictionary<string, InputFile>(StringComparer.Ordinal);

    foreach (string path in paths) {
      if (File.Exists(path)) {
        Add(byFullPath, new InputFile(ToDisplayPath(path), Path.GetFullPath(path)));
      } else if (Directory.Exists(path)) {
        string root = ToDisplayPath(path).TrimEnd('/');
        IEnumerable<string> found;
        try {
          found = Directory.EnumerateFiles(path, "*.pino", SearchOption.AllDirectories).ToList();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
          throw new CliUsageException($"cannot read directory '{path}': {OneLine(ex.Message)}");
        }

        foreach (string file in found) {
          string relative = ToDisplayPath(Path.GetRelativePath(path, file));
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

  private readonly record struct InputFile(string DisplayPath, string FullPath);

  private sealed class CliUsageException(string message) : Exception(message);
}
