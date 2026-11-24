using System.Diagnostics;
using System.Globalization;
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

namespace Pino.Cli;

public static partial class CliApp {
  private const string AnsiReset = "\x1b[0m";
  private const string AnsiGreenBold = "\x1b[1;32m";
  private const string AnsiRedBold = "\x1b[1;31m";
  private const string AnsiYellowBold = "\x1b[1;33m";

  private const string TestFailStartMarker = "\x1fPINO_TEST_FAIL\t";
  private const string TestFailEndMarker = "\n\x1fPINO_TEST_FAIL_END";
  private const string TestLeakMarker = "\x1fPINO_TEST_LEAK\t";

  private static readonly HashSet<string> s_skippedTestDirs = new(StringComparer.OrdinalIgnoreCase) {
    "bin",
    "obj",
    "tooling",
    ".git",
  };

  private sealed record DiscoveredTestFile(
      string DisplayPath,
      string FullPath,
      bool IsExplicitAbsolutePath,
      string? PreReadContent = null);

  private sealed record SingleTestOutcome(
      bool Panicked,
      bool IsAssert,
      string? FailureMessage,
      string? PanicFile,
      int PanicLine,
      string CapturedStdout,
      IReadOnlyList<string> LeakedTypes,
      bool IsRcViolation = false);

  // ---------------------------------------------------------------------------
  // `test [opts] [path]` (ticket 080, D-188, TEST-1..14)
  // ---------------------------------------------------------------------------

  private static int RunTest(
      List<string> args,
      DiagnosticRenderOptions diagOptions,
      TextWriter stdout,
      TextWriter stderr,
      CliEnvironment environment) {
    bool useVm = false;
    bool useC = false;
    bool strict = false;
    string? filter = null;
    var paths = new List<string>();

    for (int i = 0; i < args.Count; i++) {
      string arg = args[i];
      switch (arg) {
        case OptVm:
          useVm = true;
          break;
        case OptC:
          useC = true;
          break;
        case OptStrict:
          strict = true;
          break;
        case OptFilter:
          filter = ReadAnyOptionValue(args, ref i);
          break;
        default:
          if (arg.StartsWith('-')) {
            throw new CliUsageException($"unknown option '{arg}'");
          }

          paths.Add(arg);
          break;
      }
    }

    if (useVm && useC) {
      throw new CliUsageException("cannot combine '--vm' and '--c'");
    }

    if (paths.Count > 1) {
      throw new CliUsageException("'test' takes at most one file or directory");
    }

    List<DiscoveredTestFile> files = DiscoverTestFiles(paths.Count == 1 ? paths[0] : null, environment);
    var stopwatch = Stopwatch.StartNew();

    int totalPassed = 0;
    int totalFailed = 0;
    int totalWithLeaks = 0;
    bool wroteSection = false;
    var moduleSource = new WorkingDirectoryModuleSource(environment.EffectiveWorkingDirectory);

    foreach (DiscoveredTestFile file in files) {
      string content = file.PreReadContent ?? ReadFile(file.FullPath, file.DisplayPath);
      var source = new SourceText(content, file.DisplayPath);
      ParseResult parsed = Parser.Parse(source);
      Compilation compilation = Compilation.Create(parsed, moduleSource);

      int compileErrors = 0;
      var diagBlocks = new List<string>();
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

        compileErrors += Count(model.Diagnostics).Errors;
        if (Count(model.Diagnostics).Errors > 0) {
          diagBlocks.Add(DiagnosticRenderer.RenderAll(model.Diagnostics, model.Tree.Source, diagOptions));
        }
      }

      if (compileErrors > 0) {
        int parsedTestCount = CountParsedTests(parsed, filter);
        int failedCount = Math.Max(1, parsedTestCount);

        if (wroteSection) {
          stdout.Write('\n');
        }

        stdout.Write(file.DisplayPath + "\n");
        for (int d = 0; d < diagBlocks.Count; d++) {
          if (d > 0) {
            stdout.Write('\n');
          }

          stdout.Write(diagBlocks[d]);
        }

        wroteSection = true;
        totalFailed += failedCount;
        continue;
      }

      if (!HasAnyTestDeclaration(parsed)) {
        continue;
      }

      IrModule ir = IrLowerer.Lower(compilation, includeTests: true);
      var selectedTests = new List<(int Index, IrTestEntry Entry)>();
      for (int idx = 0; idx < ir.Tests.Count; idx++) {
        IrTestEntry entry = ir.Tests[idx];
        if (filter is null || entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) {
          selectedTests.Add((idx, entry));
        }
      }

      if (selectedTests.Count == 0) {
        continue;
      }

      RcInsertPass.Run(ir);
      IrVerificationResult verification = IrVerifier.VerifyAfterRc(ir);
      if (!verification.IsValid) {
        if (wroteSection) {
          stdout.Write('\n');
        }

        stdout.Write(file.DisplayPath + "\n");
        stdout.Write("  internal error: IR verification failed: " + OneLine(string.Join("; ", verification.Errors.Take(3))) + "\n");
        wroteSection = true;
        totalFailed += selectedTests.Count;
        continue;
      }

      if (wroteSection) {
        stdout.Write('\n');
      }

      stdout.Write(file.DisplayPath + "\n");
      wroteSection = true;

      if (useC) {
        RunTestsWithCEngine(
            ir,
            compilation,
            file,
            selectedTests,
            strict,
            diagOptions.Color,
            stdout,
            environment,
            ref totalPassed,
            ref totalFailed,
            ref totalWithLeaks);
      } else {
        RunTestsWithVmEngine(
            ir,
            source,
            file,
            selectedTests,
            strict,
            diagOptions.Color,
            stdout,
            environment,
            ref totalPassed,
            ref totalFailed,
            ref totalWithLeaks);
      }
    }

    stopwatch.Stop();
    _ = stderr;

    if (totalPassed == 0 && totalFailed == 0) {
      stdout.Write("No tests found.\n");
      return 0;
    }

    var summaryParts = new List<string>(3);
    if (totalPassed > 0) {
      summaryParts.Add(string.Create(CultureInfo.InvariantCulture, $"{totalPassed} passed"));
    }

    if (totalFailed > 0) {
      summaryParts.Add(string.Create(CultureInfo.InvariantCulture, $"{totalFailed} failed"));
    }

    if (totalWithLeaks > 0) {
      summaryParts.Add(string.Create(CultureInfo.InvariantCulture, $"{totalWithLeaks} with leaks"));
    }

    stdout.Write(string.Create(
        CultureInfo.InvariantCulture,
        $"\n{string.Join(", ", summaryParts)} ({stopwatch.Elapsed.TotalSeconds:0.00} s)\n"));
    return totalFailed > 0 ? 1 : 0;
  }

  private static void RunTestsWithVmEngine(
      IrModule ir,
      SourceText source,
      DiscoveredTestFile file,
      List<(int Index, IrTestEntry Entry)> selectedTests,
      bool strict,
      bool useColor,
      TextWriter stdout,
      CliEnvironment environment,
      ref int totalPassed,
      ref int totalFailed,
      ref int totalWithLeaks) {
    foreach ((_, IrTestEntry entry) in selectedTests) {
      var host = new BufferedVmHost();
      var vm = new PinoVm(
          ir,
          host,
          new PinoVmOptions {
            VerifyRc = true,
            RootSource = source,
          });
      PinoVmResult result = vm.RunTest(entry.FuncId);

      SingleTestOutcome outcome;
      if (result.Reason == PinoVmStopReason.Panicked) {
        outcome = new SingleTestOutcome(
            Panicked: true,
            IsAssert: result.PanicIsAssert,
            FailureMessage: result.PanicMessage ?? string.Empty,
            PanicFile: result.PanicFile,
            PanicLine: result.PanicLine,
            CapturedStdout: host.StdOut,
            LeakedTypes: []);
      } else if (result.RcError is { } rcError) {
        outcome = new SingleTestOutcome(
            Panicked: true,
            IsAssert: false,
            FailureMessage: rcError,
            PanicFile: null,
            PanicLine: 0,
            CapturedStdout: host.StdOut,
            LeakedTypes: [],
            IsRcViolation: true);
      } else if (result.ExitCode != 0) {
        outcome = new SingleTestOutcome(
            Panicked: true,
            IsAssert: false,
            FailureMessage: string.Create(CultureInfo.InvariantCulture, $"exited with code {result.ExitCode}"),
            PanicFile: null,
            PanicLine: 0,
            CapturedStdout: host.StdOut,
            LeakedTypes: []);
      } else {
        IReadOnlyList<string> leakedTypes = result.LeakReport is { AllLeaks.Count: > 0 } report
            ? [.. report.AllLeaks.Select(static l => l.EffectiveDisplayTypeName)]
            : [];
        outcome = new SingleTestOutcome(
            Panicked: false,
            IsAssert: false,
            FailureMessage: null,
            PanicFile: null,
            PanicLine: 0,
            CapturedStdout: host.StdOut,
            LeakedTypes: leakedTypes);
      }

      WriteTestOutcome(
          entry.Name,
          outcome,
          file,
          strict,
          useColor,
          stdout,
          environment,
          ref totalPassed,
          ref totalFailed,
          ref totalWithLeaks);
    }
  }

  private static void RunTestsWithCEngine(
      IrModule ir,
      Compilation compilation,
      DiscoveredTestFile file,
      List<(int Index, IrTestEntry Entry)> selectedTests,
      bool strict,
      bool useColor,
      TextWriter stdout,
      CliEnvironment environment,
      ref int totalPassed,
      ref int totalFailed,
      ref int totalWithLeaks) {
    string cSource = CEmitter.Emit(ir, compilation, new CEmitOptions(EmitTestRunner: true));
    string tempDir = Path.Combine(Path.GetTempPath(), "pino-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    string exePath = Path.Combine(tempDir, OperatingSystem.IsWindows() ? "pino_tests.exe" : "pino_tests");

    try {
      CBuildResult build = CCompilerDriver.Build(new CBuildOptions(
          CSource: cSource,
          OutputExecutablePath: exePath,
          Release: false,
          DebugRc: true));
      if (!build.Succeeded) {
        stdout.Write($"  C compilation failed ({build.CompilerUsed}):\n{build.Stderr}{build.Stdout}".TrimEnd() + "\n");
        totalFailed += selectedTests.Count;
        return;
      }

      foreach ((int index, IrTestEntry entry) in selectedTests) {
        CExecResult exec = CCompilerDriver.RunProcess(
            exePath,
            ["index", index.ToString(CultureInfo.InvariantCulture)],
            workingDirectory: environment.EffectiveWorkingDirectory,
            failOnLeak: false,
            closeStdin: true,
            timeout: TimeSpan.FromMinutes(2));

        SingleTestOutcome outcome = ParseCTestResult(exec);
        WriteTestOutcome(
            entry.Name,
            outcome,
            file,
            strict,
            useColor,
            stdout,
            environment,
            ref totalPassed,
            ref totalFailed,
            ref totalWithLeaks);
      }
    } finally {
      try {
        Directory.Delete(tempDir, recursive: true);
      } catch (IOException) {
      } catch (UnauthorizedAccessException) {
      }
    }
  }

  private static SingleTestOutcome ParseCTestResult(CExecResult exec) {
    string capturedStdout = exec.Stdout.Replace("\r\n", "\n", StringComparison.Ordinal);
    string normalizedStderr = exec.Stderr.Replace("\r\n", "\n", StringComparison.Ordinal);

    if (exec.TimedOut) {
      return new SingleTestOutcome(
          Panicked: true,
          IsAssert: false,
          FailureMessage: "test timed out",
          PanicFile: null,
          PanicLine: 0,
          CapturedStdout: capturedStdout,
          LeakedTypes: []);
    }

    int failIdx = normalizedStderr.IndexOf(TestFailStartMarker, StringComparison.Ordinal);
    if (failIdx >= 0) {
      int headerStart = failIdx + TestFailStartMarker.Length;
      int headerEnd = normalizedStderr.IndexOf('\n', headerStart);
      if (headerEnd >= 0) {
        string header = normalizedStderr[headerStart..headerEnd];
        string[] parts = header.Split('\t');
        bool isAssert = parts.Length >= 1 && string.Equals(parts[0], "assert", StringComparison.Ordinal);
        string? panicFile = parts.Length >= 2 && parts[1].Length > 0 ? parts[1] : null;
        int panicLine = parts.Length >= 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ln)
            ? ln
            : 0;

        int msgStart = headerEnd + 1;
        int endMarkerIdx = normalizedStderr.IndexOf(TestFailEndMarker, msgStart, StringComparison.Ordinal);
        string message = endMarkerIdx >= msgStart
            ? normalizedStderr[msgStart..endMarkerIdx]
            : normalizedStderr[msgStart..].TrimEnd('\n');

        return new SingleTestOutcome(
            Panicked: true,
            IsAssert: isAssert,
            FailureMessage: message,
            PanicFile: panicFile,
            PanicLine: panicLine,
            CapturedStdout: capturedStdout,
            LeakedTypes: []);
      }
    }

    if (exec.ExitCode == 103) {
      return new SingleTestOutcome(
          Panicked: true,
          IsAssert: false,
          FailureMessage: normalizedStderr.Trim(),
          PanicFile: null,
          PanicLine: 0,
          CapturedStdout: capturedStdout,
          LeakedTypes: [],
          IsRcViolation: true);
    }

    if (exec.ExitCode != 0) {
      string msg = !string.IsNullOrWhiteSpace(normalizedStderr)
          ? normalizedStderr.Trim()
          : string.Create(CultureInfo.InvariantCulture, $"exited with code {exec.ExitCode}");
      return new SingleTestOutcome(
          Panicked: true,
          IsAssert: false,
          FailureMessage: msg,
          PanicFile: null,
          PanicLine: 0,
          CapturedStdout: capturedStdout,
          LeakedTypes: []);
    }

    var leaks = new List<string>();
    foreach (string line in normalizedStderr.Split('\n')) {
      if (line.StartsWith(TestLeakMarker, StringComparison.Ordinal)) {
        leaks.Add(line[TestLeakMarker.Length..]);
      }
    }

    return new SingleTestOutcome(
        Panicked: false,
        IsAssert: false,
        FailureMessage: null,
        PanicFile: null,
        PanicLine: 0,
        CapturedStdout: capturedStdout,
        LeakedTypes: leaks);
  }

  private static void WriteTestOutcome(
      string testName,
      SingleTestOutcome outcome,
      DiscoveredTestFile file,
      bool strict,
      bool useColor,
      TextWriter stdout,
      CliEnvironment environment,
      ref int totalPassed,
      ref int totalFailed,
      ref int totalWithLeaks) {
    string passTag = useColor ? $"{AnsiGreenBold}[PASS]{AnsiReset}" : "[PASS]";
    string failTag = useColor ? $"{AnsiRedBold}[FAIL]{AnsiReset}" : "[FAIL]";
    string warnLabel = useColor ? $"{AnsiYellowBold}warning:{AnsiReset}" : "warning:";
    string errLabel = useColor ? $"{AnsiRedBold}error:{AnsiReset}" : "error:";

    if (outcome.Panicked) {
      totalFailed++;
      string locSuffix = string.Empty;
      if (!string.IsNullOrEmpty(outcome.PanicFile) && outcome.PanicLine > 0) {
        string formattedFile = FormatTestLocationFile(outcome.PanicFile, environment, file.IsExplicitAbsolutePath);
        locSuffix = string.Create(CultureInfo.InvariantCulture, $"  ({formattedFile}:{outcome.PanicLine})");
      }

      stdout.Write($"  {failTag} {testName}{locSuffix}\n");
      string message = outcome.FailureMessage ?? string.Empty;
      string[] msgLines = SplitDetailLines(message);
      if (outcome.IsAssert) {
        foreach (string line in msgLines) {
          stdout.Write($"         {line}\n");
        }
      } else if (outcome.IsRcViolation) {
        foreach (string line in msgLines) {
          stdout.Write($"         {line}\n");
        }
      } else if (msgLines.Length > 0) {
        stdout.Write($"         panicked: {msgLines[0]}\n");
        for (int i = 1; i < msgLines.Length; i++) {
          stdout.Write($"         {msgLines[i]}\n");
        }
      } else {
        stdout.Write("         panicked:\n");
      }

      WriteCapturedOutput(stdout, outcome.CapturedStdout);
      return;
    }

    if (outcome.LeakedTypes.Count > 0) {
      totalWithLeaks++;
      string countPhrase = outcome.LeakedTypes.Count == 1
          ? "1 object was"
          : string.Create(CultureInfo.InvariantCulture, $"{outcome.LeakedTypes.Count} objects were");

      if (strict) {
        totalFailed++;
        stdout.Write($"  {failTag} {testName}\n");
        stdout.Write($"         {errLabel} {countPhrase} still alive when this test ended:\n");
        foreach (string leakedType in outcome.LeakedTypes) {
          stdout.Write($"           - {leakedType}\n");
        }

        WriteCapturedOutput(stdout, outcome.CapturedStdout);
      } else {
        totalPassed++;
        stdout.Write($"  {passTag} {testName}\n");
        stdout.Write($"         {warnLabel} {countPhrase} still alive when this test ended:\n");
        foreach (string leakedType in outcome.LeakedTypes) {
          stdout.Write($"           - {leakedType}\n");
        }
      }

      return;
    }

    totalPassed++;
    stdout.Write($"  {passTag} {testName}\n");
  }

  private static void WriteCapturedOutput(TextWriter stdout, string captured) {
    if (string.IsNullOrEmpty(captured)) {
      return;
    }

    foreach (string line in SplitDetailLines(captured)) {
      stdout.Write($"         {line}\n");
    }
  }

  private static string[] SplitDetailLines(string text) {
    string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
    if (normalized.EndsWith('\n')) {
      normalized = normalized[..^1];
    }

    return normalized.Length == 0 ? [] : normalized.Split('\n');
  }

  private static string FormatTestLocationFile(string rawPath, CliEnvironment environment, bool isExplicitAbsolutePath) {
    string normalized = ToDisplayPath(rawPath);
    if (!isExplicitAbsolutePath && Path.IsPathRooted(rawPath)) {
      string rel = ToDisplayPath(Path.GetRelativePath(environment.EffectiveWorkingDirectory, rawPath));
      if (!rel.StartsWith("../", StringComparison.Ordinal) && rel != "..") {
        return rel;
      }
    }

    return normalized;
  }

  private static List<DiscoveredTestFile> DiscoverTestFiles(string? requestedPath, CliEnvironment environment) {
    if (requestedPath is not null) {
      string resolved = ResolvePath(requestedPath, environment);
      if (File.Exists(resolved)) {
        return [
          new DiscoveredTestFile(
              ToDisplayPath(requestedPath),
              resolved,
              IsExplicitAbsolutePath: Path.IsPathRooted(requestedPath)),
        ];
      }

      if (Directory.Exists(resolved)) {
        string prefix = ToDisplayPath(requestedPath).TrimEnd('/');
        if (prefix == ".") {
          prefix = string.Empty;
        }

        return DiscoverTestFilesInDirectory(
            resolved,
            prefix,
            isExplicitAbsolutePath: Path.IsPathRooted(requestedPath),
            parentInTestFolder: IsNamedTestDirectory(resolved));
      }

      throw new CliUsageException($"no such file or directory: '{requestedPath}'");
    }

    string workDir = Path.GetFullPath(environment.EffectiveWorkingDirectory);
    return DiscoverTestFilesInDirectory(
        workDir,
        displayPrefix: string.Empty,
        isExplicitAbsolutePath: false,
        parentInTestFolder: IsNamedTestDirectory(workDir));
  }

  private static List<DiscoveredTestFile> DiscoverTestFilesInDirectory(
      string rootDir,
      string displayPrefix,
      bool isExplicitAbsolutePath,
      bool parentInTestFolder) {
    var discovered = new List<DiscoveredTestFile>();
    var stack = new Stack<(string DirPath, bool InTestFolder)>();
    stack.Push((rootDir, parentInTestFolder));

    while (stack.Count > 0) {
      (string currentDir, bool inTestFolder) = stack.Pop();

      IEnumerable<string> subDirs;
      IEnumerable<string> pinoFiles;
      try {
        subDirs = Directory.EnumerateDirectories(currentDir);
        pinoFiles = Directory.EnumerateFiles(currentDir, "*.pino");
      } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        throw new CliUsageException($"cannot read directory '{ToDisplayPath(currentDir)}': {OneLine(ex.Message)}");
      }

      foreach (string subDir in subDirs) {
        string name = Path.GetFileName(subDir);
        if (s_skippedTestDirs.Contains(name)) {
          continue;
        }

        stack.Push((subDir, inTestFolder || string.Equals(name, "test", StringComparison.OrdinalIgnoreCase)));
      }

      foreach (string filePath in pinoFiles) {
        string fullPath = Path.GetFullPath(filePath);
        string relative = ToDisplayPath(Path.GetRelativePath(rootDir, fullPath));
        string displayPath = string.IsNullOrEmpty(displayPrefix)
            ? relative
            : $"{displayPrefix}/{relative}";
        string fileName = Path.GetFileName(fullPath);

        if (inTestFolder || fileName.EndsWith("_test.pino", StringComparison.OrdinalIgnoreCase)) {
          discovered.Add(new DiscoveredTestFile(displayPath, fullPath, isExplicitAbsolutePath));
          continue;
        }

        string content = ReadFile(fullPath, displayPath);
        if (!content.Contains("test", StringComparison.Ordinal)) {
          continue;
        }

        ParseResult parsed = Parser.Parse(new SourceText(content, displayPath));
        if (HasAnyTestDeclaration(parsed)) {
          discovered.Add(new DiscoveredTestFile(displayPath, fullPath, isExplicitAbsolutePath, content));
        }
      }
    }

    discovered.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.DisplayPath, b.DisplayPath));
    return discovered;
  }

  private static bool IsNamedTestDirectory(string dirPath) {
    string name = Path.GetFileName(dirPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    return string.Equals(name, "test", StringComparison.OrdinalIgnoreCase);
  }

  private static bool HasAnyTestDeclaration(ParseResult parsed) {
    foreach (SyntaxNode item in parsed.Root.Items) {
      if (item is TestDeclarationSyntax) {
        return true;
      }
    }

    return false;
  }

  private static int CountParsedTests(ParseResult parsed, string? filter) {
    int count = 0;
    foreach (SyntaxNode item in parsed.Root.Items) {
      if (item is not TestDeclarationSyntax testDecl) {
        continue;
      }

      if (filter is null) {
        count++;
        continue;
      }

      string name = ExtractParsedTestName(parsed, testDecl);
      if (name.Contains(filter, StringComparison.OrdinalIgnoreCase)) {
        count++;
      }
    }

    return count;
  }

  private static string ExtractParsedTestName(ParseResult parsed, TestDeclarationSyntax decl) {
    if (decl.Name is InterpolatedStringExpressionSyntax interp) {
      var sb = new StringBuilder();
      foreach (StringPartSyntax part in interp.Parts) {
        if (part is StringTextPartSyntax textPart) {
          sb.Append(parsed.Lex.GetText(textPart.TextToken));
        }
      }

      return sb.ToString();
    }

    SourceText source = parsed.Source;
    int start = Math.Clamp(decl.Name.Span.Start, 0, source.Length);
    int end = Math.Clamp(decl.Name.Span.End, start, source.Length);
    return source.Text[start..end].Trim();
  }
}
