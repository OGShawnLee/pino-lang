using System.Text.RegularExpressions;

using Pino.Cli;

namespace Pino.Compiler.Tests.Cli;

public sealed class CliAppTests : IDisposable {
  private const string LexerErrorSource = "val s = \"a\\qb\"\n";
  private const string ParserErrorSource = "fn foo(";

  private static readonly IReadOnlyDictionary<string, string> s_testNotes =
      new Dictionary<string, string>(StringComparer.Ordinal) {
        ["PN1011"] = "backslashes are picky in strings",
      };

  private readonly string _root = Path.Combine(Path.GetTempPath(), "pino-cli-" + Guid.NewGuid().ToString("N"));

  public CliAppTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try {
      Directory.Delete(_root, recursive: true);
    } catch (IOException) {
    } catch (UnauthorizedAccessException) {
    }
  }

  [Fact]
  public void CheckOnACleanFileSucceedsWithOnlyTheSummary() {
    string file = WriteFile("clean.pino", "val x = 1\n");

    RunResult result = Run(["check", file]);

    Assert.Equal(0, result.ExitCode);
    Assert.Equal("checked 1 file(s): 0 error(s), 0 warning(s)\n", result.Stdout);
    Assert.Equal("", result.Stderr);
  }

  [Fact]
  public void CheckOnACheckerErrorReportsPN3xxxAndExitsOne() {
    string file = WriteFile("bad_type.pino", "val x int = \"oops\"\n");

    RunResult result = Run(["check", file, "--color", "never"]);

    Assert.Equal(1, result.ExitCode);
    Assert.Contains("error[PN3102]", result.Stdout, StringComparison.Ordinal);
    Assert.Contains($"--> {Display(file)}:1:", result.Stdout, StringComparison.Ordinal);
    Assert.EndsWith("checked 1 file(s): 1 error(s), 0 warning(s)\n", result.Stdout, StringComparison.Ordinal);
    Assert.Equal("", result.Stderr);
  }

  [Fact]
  public void CheckOnAWarningOnlyFileExitsZeroAndCountsWarning() {
    string file = WriteFile("warn.pino", "for false {\n  println(\"never\")\n}\n");

    RunResult result = Run(["check", file, "--color", "never"]);

    Assert.Equal(0, result.ExitCode);
    Assert.Contains("warning[PN3550]", result.Stdout, StringComparison.Ordinal);
    Assert.EndsWith("checked 1 file(s): 0 error(s), 1 warning(s)\n", result.Stdout, StringComparison.Ordinal);
    Assert.Equal("", result.Stderr);
  }

  [Fact]
  public void CheckOnModuleImportErrorReportsDiagnosticOnceAcrossFiles() {
    WriteFile(Path.Combine("modules", "BadHelper.pino"), "pub fn main() int {\n  return 1\n}\n");
    string entry = WriteFile("entry.pino", "import BadHelper\nprintln(BadHelper::main())\n");

    RunResult result = Run(["check", entry, "--color", "never"]);

    Assert.Equal(1, result.ExitCode);
    Assert.Contains("error[PN3008]", result.Stdout, StringComparison.Ordinal);
    Assert.EndsWith("checked 1 file(s): 1 error(s), 0 warning(s)\n", result.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void CheckOnALexerErrorRendersTheDiagnostic() {
    string file = WriteFile("lex.pino", LexerErrorSource);

    RunResult result = Run(["check", file, "--color", "never"]);

    Assert.Equal(1, result.ExitCode);
    string display = Display(file);
    string expected =
        "error[PN1011]: Invalid escape sequence '\\q' in string literal.\n" +
        $" --> {display}:1:11\n" +
        "  |\n" +
        "1 | val s = \"a\\qb\"\n" +
        "  |           ^^\n" +
        "checked 1 file(s): 1 error(s), 0 warning(s)\n";
    Assert.Equal(expected, result.Stdout);
    Assert.Equal("", result.Stderr);
  }

  [Fact]
  public void CheckOnAParserErrorReportsPN2001() {
    string file = WriteFile("parse.pino", ParserErrorSource);

    RunResult result = Run(["check", file]);

    Assert.Equal(1, result.ExitCode);
    Assert.Contains("error[PN2001]", result.Stdout, StringComparison.Ordinal);
    Assert.Contains($"--> {Display(file)}:", result.Stdout);
    Assert.Matches(@"checked 1 file\(s\): [1-9]\d* error\(s\), 0 warning\(s\)\n$", result.Stdout);
  }

  [Fact]
  public void CheckOnADirectoryIsDeterministicAndCountsEveryFile() {
    string dir = Path.Combine(_root, "src");
    WriteFile(Path.Combine("src", "b", "z.pino"), ParserErrorSource);
    WriteFile(Path.Combine("src", "a.pino"), ParserErrorSource);
    WriteFile(Path.Combine("src", "b", "c", "y.pino"), ParserErrorSource);
    WriteFile(Path.Combine("src", "ignored.txt"), ParserErrorSource);

    RunResult first = Run(["check", dir]);
    RunResult second = Run(["check", dir]);

    Assert.Equal(1, first.ExitCode);
    Assert.Equal(first.Stdout, second.Stdout);

    string root = Display(dir);
    int indexA = first.Stdout.IndexOf($"--> {root}/a.pino:", StringComparison.Ordinal);
    int indexY = first.Stdout.IndexOf($"--> {root}/b/c/y.pino:", StringComparison.Ordinal);
    int indexZ = first.Stdout.IndexOf($"--> {root}/b/z.pino:", StringComparison.Ordinal);
    Assert.True(indexA >= 0 && indexA < indexY && indexY < indexZ, first.Stdout);
    Assert.DoesNotContain("ignored.txt", first.Stdout, StringComparison.Ordinal);

    int errors = Regex.Matches(first.Stdout, @"^error\[", RegexOptions.Multiline).Count;
    Assert.True(errors >= 3);
    Assert.EndsWith($"checked 3 file(s): {errors} error(s), 0 warning(s)\n", first.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void CheckSeparatesFilesWithOneEmptyLine() {
    string a = WriteFile("a.pino", ParserErrorSource);
    string b = WriteFile("b.pino", ParserErrorSource);

    RunResult result = Run(["check", a, b, "--color", "never"]);

    Assert.Matches(@"\n\nerror\[PN\d+\][^\n]*\n *--> [^\n]*b\.pino:", result.Stdout);
    Assert.DoesNotContain("\n\n\n", result.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void OptionsMayComeBeforeOrAfterThePaths() {
    string file = WriteFile("lex.pino", LexerErrorSource);

    RunResult after = Run(["check", file, "--color", "always", "--voice", "plain"]);
    RunResult before = Run(["--color", "always", "--voice", "plain", "check", file]);

    Assert.Equal(1, after.ExitCode);
    Assert.Contains("\x1b[", after.Stdout, StringComparison.Ordinal);
    Assert.Equal(after.Stdout, before.Stdout);
  }

  [Fact]
  public void ColorAlwaysEmitsAnsiAndNeverDoesNot() {
    string file = WriteFile("lex.pino", LexerErrorSource);

    RunResult always = Run(["check", file, "--color", "always"]);
    RunResult never = Run(["check", file, "--color", "never"]);

    Assert.Contains("\x1b[", always.Stdout, StringComparison.Ordinal);
    Assert.DoesNotContain("\x1b[", never.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void ColorAutoIsOffWhenOutputIsRedirected() {
    string file = WriteFile("lex.pino", LexerErrorSource);

    RunResult result = Run(["check", file, "--color", "auto"], isOutputRedirected: true, supportsAnsi: true);

    Assert.DoesNotContain("\x1b[", result.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void ColorAutoIsOffWhenNoColorIsSet() {
    string file = WriteFile("lex.pino", LexerErrorSource);

    RunResult result = Run(
        ["check", file],
        supportsAnsi: true,
        environmentVariables: new Dictionary<string, string> { ["NO_COLOR"] = "1" });

    Assert.DoesNotContain("\x1b[", result.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void ColorAutoIsOffWhenAnsiIsUnsupported() {
    string file = WriteFile("lex.pino", LexerErrorSource);

    RunResult result = Run(["check", file], isOutputRedirected: false, supportsAnsi: false);

    Assert.DoesNotContain("\x1b[", result.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void ColorAutoIsOnForATerminalWithUnsetOrEmptyNoColor() {
    string file = WriteFile("lex.pino", LexerErrorSource);

    RunResult unset = Run(["check", file], supportsAnsi: true);
    RunResult empty = Run(
        ["check", file],
        supportsAnsi: true,
        environmentVariables: new Dictionary<string, string> { ["NO_COLOR"] = "" });

    Assert.Contains("\x1b[", unset.Stdout, StringComparison.Ordinal);
    Assert.Contains("\x1b[", empty.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void VoicePinoPrintsThePinoNoteAndPlainDoesNot() {
    string file = WriteFile("lex.pino", LexerErrorSource);

    RunResult pino = Run(["check", file, "--voice", "pino", "--color", "never"], notes: s_testNotes);
    RunResult plain = Run(["check", file, "--voice", "plain", "--color", "never"], notes: s_testNotes);
    RunResult byDefault = Run(["check", file, "--color", "never"], notes: s_testNotes);

    Assert.Contains("= Pino: backslashes are picky in strings", pino.Stdout, StringComparison.Ordinal);
    Assert.DoesNotContain("= Pino:", plain.Stdout, StringComparison.Ordinal);
    Assert.Contains("= Pino:", byDefault.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void TreePrintsTheExactSyntaxTree() {
    string file = WriteFile("expr.pino", "val x = 1 + 2");

    RunResult result = Run(["tree", file]);

    const string Expected =
        "CompilationUnit\n" +
        "  VariableDeclaration\n" +
        "    ValKeyword 'val'\n" +
        "    Identifier 'x'\n" +
        "    Assign '='\n" +
        "    BinaryExpression\n" +
        "      LiteralExpression\n" +
        "        IntLiteral '1'\n" +
        "      Plus '+'\n" +
        "      LiteralExpression\n" +
        "        IntLiteral '2'\n" +
        "  EOF ''\n";
    Assert.Equal(0, result.ExitCode);
    Assert.Equal(Expected, result.Stdout);
    Assert.Equal("", result.Stderr);
  }

  [Fact]
  public void TreeEscapesNewlineCarriageReturnAndTabInTokenText() {
    string file = WriteFile("multi.pino", "val s = \"\"\"a\nb\r\n\tc\"\"\"");

    RunResult result = Run(["tree", file]);

    Assert.Equal(0, result.ExitCode);
    Assert.Contains("StringPart 'a\\nb\\r\\n\\tc'\n", result.Stdout, StringComparison.Ordinal);
    Assert.All(
        result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries),
        static line => Assert.DoesNotContain("\r", line, StringComparison.Ordinal));
    Assert.DoesNotContain("\t", result.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void TreeShowsMissingTokensAndThenTheDiagnostics() {
    string file = WriteFile("call.pino", "foo(a,");

    RunResult result = Run(["tree", file, "--color", "never"]);

    Assert.Equal(1, result.ExitCode);
    Assert.Contains("      CloseParen <missing>\n", result.Stdout, StringComparison.Ordinal);
    Assert.Contains("\n\nerror[PN2001]", result.Stdout, StringComparison.Ordinal);
    Assert.Contains($"--> {Display(file)}:1:", result.Stdout);
  }

  [Fact]
  public void UnknownCommandIsAUsageError() {
    AssertUsageError(["frobnicate"]);
  }

  [Fact]
  public void MissingCommandIsAUsageError() {
    AssertUsageError(["--color", "never"]);
  }

  [Fact]
  public void UnknownOptionIsAUsageError() {
    string file = WriteFile("clean.pino", "val x = 1\n");

    AssertUsageError(["check", file, "--bogus"]);
  }

  [Fact]
  public void MissingOptionValueIsAUsageError() {
    string file = WriteFile("clean.pino", "val x = 1\n");

    AssertUsageError(["check", file, "--color"]);
    AssertUsageError(["check", file, "--voice"]);
  }

  [Fact]
  public void BadOptionValueIsAUsageError() {
    string file = WriteFile("clean.pino", "val x = 1\n");

    AssertUsageError(["check", file, "--color", "rainbow"]);
    AssertUsageError(["check", file, "--voice", "loud"]);
  }

  [Fact]
  public void MissingPathIsAUsageError() {
    AssertUsageError(["check"]);
    AssertUsageError(["tree"]);
    AssertUsageError(["check", "--color", "never"]);
  }

  [Fact]
  public void NonexistentPathIsAUsageError() {
    string missing = Path.Combine(_root, "missing.pino");

    AssertUsageError(["check", missing]);
    AssertUsageError(["tree", missing]);
  }

  [Fact]
  public void NonexistentPathAmongRealOnesPrintsNothingToStdout() {
    string file = WriteFile("lex.pino", LexerErrorSource);

    RunResult result = Run(["check", file, Path.Combine(_root, "missing.pino")]);

    Assert.Equal(2, result.ExitCode);
    Assert.Equal("", result.Stdout);
    AssertOnePinoLine(result.Stderr);
  }

  [Fact]
  public void UnreadableFileIsAUsageError() {
    string file = WriteFile("locked.pino", "val x = 1\n");

    if (OperatingSystem.IsWindows()) {
      using var hold = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

      AssertUsageError(["check", file]);
      AssertUsageError(["tree", file]);
      return;
    }

    File.SetUnixFileMode(file, UnixFileMode.None);
    try {
      AssertUsageError(["check", file]);
      AssertUsageError(["tree", file]);
    } finally {
      File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
  }

  [Fact]
  public void TreeOnADirectoryIsAUsageError() {
    AssertUsageError(["tree", _root]);
  }

  [Fact]
  public void TreeOnSeveralFilesIsAUsageError() {
    string a = WriteFile("a.pino", "val x = 1\n");
    string b = WriteFile("b.pino", "val y = 2\n");

    AssertUsageError(["tree", a, b]);
  }

  [Theory]
  [InlineData("--help")]
  [InlineData("-h")]
  [InlineData("check", "--help")]
  public void HelpPrintsUsageAndSucceeds(params string[] args) {
    AssertUsage(Run(args));
  }

  private const string SemicolonExplanation = """
      PN1018: Pino doesn't use semicolons

      Pino ends a statement at the end of a line, or where the next statement
      begins, so it never needs a semicolon. If you type one out of habit, Pino
      ignores it and shows this hint, and your program still runs.

          val a = 1;   # the ';' is ignored, with this hint
          val a = 1    # the same program, without the hint

      Why: Pino was designed so that a semicolon can never be the reason your
      program fails. It was made for someone who once failed a university course
      over one.
      """;

  [Theory]
  [InlineData("PN1018")]
  [InlineData("pn1018")]
  public void ExplainPrintsTheExactTextAndSucceeds(string code) {
    RunResult result = Run(["explain", code]);

    Assert.Equal(0, result.ExitCode);
    Assert.Equal(SemicolonExplanation.ReplaceLineEndings("\n") + "\n", result.Stdout);
    Assert.Equal("", result.Stderr);
  }

  [Fact]
  public void ExplainWithAnUnknownCodeSaysSoAndExitsOne() {
    RunResult result = Run(["explain", "PN9999"]);

    Assert.Equal(1, result.ExitCode);
    Assert.Equal("There's no explanation for 'PN9999' yet.\n", result.Stderr);
    Assert.Equal("", result.Stdout);
  }

  [Fact]
  public void ExplainWithoutACodePrintsTheUsageLineAndExitsOne() {
    RunResult result = Run(["explain"]);

    Assert.Equal(1, result.ExitCode);
    Assert.Equal("Usage: pino explain <code>, for example 'pino explain PN1018'.\n", result.Stderr);
    Assert.Equal("", result.Stdout);
  }

  [Fact]
  public void CheckOnAFileWithOnlySemicolonHintsStillSucceeds() {
    string file = WriteFile("semi.pino", "val a = 1;\nprintln(a);\n");

    RunResult result = Run(["check", file, "--color", "never"]);

    Assert.Equal(0, result.ExitCode);
    Assert.Equal(2, Regex.Matches(result.Stdout, @"hint\[PN1018\]: Pino doesn't use semicolons, so this one was ignored\.").Count);
    Assert.Equal(2, Regex.Matches(result.Stdout, Regex.Escape("= help: you can delete it; run 'pino explain PN1018' to see why")).Count);
    Assert.EndsWith("checked 1 file(s): 0 error(s), 0 warning(s)\n", result.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void NoArgumentsPrintsUsageAndSucceeds() {
    AssertUsage(Run([]));
  }

  private static void AssertUsage(RunResult result) {
    Assert.Equal(0, result.ExitCode);
    Assert.Equal("", result.Stderr);
    Assert.Contains("check", result.Stdout, StringComparison.Ordinal);
    Assert.Contains("tree", result.Stdout, StringComparison.Ordinal);
    Assert.Contains("--color", result.Stdout, StringComparison.Ordinal);
    Assert.Contains("--voice", result.Stdout, StringComparison.Ordinal);
  }

  private void AssertUsageError(string[] args) {
    RunResult result = Run(args);

    Assert.Equal(2, result.ExitCode);
    Assert.Equal("", result.Stdout);
    AssertOnePinoLine(result.Stderr);
  }

  private static void AssertOnePinoLine(string stderr) {
    Assert.StartsWith("pino: ", stderr, StringComparison.Ordinal);
    Assert.EndsWith("\n", stderr, StringComparison.Ordinal);
    Assert.Single(stderr.TrimEnd('\n').Split('\n'));
  }

  private static string Display(string path) => path.Replace('\\', '/');

  private string WriteFile(string relativePath, string content) {
    string path = Path.Combine(_root, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
    return path;
  }

  private RunResult Run(
      string[] args,
      bool isOutputRedirected = false,
      bool supportsAnsi = false,
      IReadOnlyDictionary<string, string>? environmentVariables = null,
      IReadOnlyDictionary<string, string>? notes = null) {
    var stdout = new StringWriter();
    var stderr = new StringWriter();
    var environment = new CliEnvironment(
        name => environmentVariables is not null && environmentVariables.TryGetValue(name, out string? value) ? value : null,
        isOutputRedirected,
        supportsAnsi,
        notes ?? new Dictionary<string, string>(),
        WorkingDirectory: _root);

    int exitCode = CliApp.Run(args, stdout, stderr, environment);
    return new RunResult(exitCode, stdout.ToString(), stderr.ToString());
  }

  private sealed record RunResult(int ExitCode, string Stdout, string Stderr);
}
