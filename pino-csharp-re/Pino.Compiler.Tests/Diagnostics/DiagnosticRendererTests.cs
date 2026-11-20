using System.Collections.Frozen;
using System.Text.RegularExpressions;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Diagnostics;

public sealed partial class DiagnosticRendererTests {
  private static readonly FrozenDictionary<string, string> s_testNotes =
      new Dictionary<string, string>(StringComparer.Ordinal) {
        [DiagnosticCodes.InvalidStringEscape] = "backslashes are picky in strings",
      }.ToFrozenDictionary(StringComparer.Ordinal);

  public sealed record GoldenCase(
      string Name,
      Diagnostic Diagnostic,
      SourceText Source,
      DiagnosticRenderOptions? Options,
      string Expected);

  public static TheoryData<GoldenCase> GoldenCases {
    get {
      var data = new TheoryData<GoldenCase>();
      foreach (GoldenCase golden in BuildGoldenCases()) {
        data.Add(golden);
      }

      return data;
    }
  }

  [Theory]
  [MemberData(nameof(GoldenCases))]
  public void GoldenOutputMatchesSpecification(GoldenCase golden) {
    string actual = DiagnosticRenderer.Render(golden.Diagnostic, golden.Source, golden.Options);
    Assert.Equal(golden.Expected, actual);
  }

  [Theory]
  [MemberData(nameof(GoldenCases))]
  public void ColoredOutputStripsToPlainOutput(GoldenCase golden) {
    DiagnosticRenderOptions colorOptions = (golden.Options ?? new DiagnosticRenderOptions()) with {
      Color = true,
    };

    string colored = DiagnosticRenderer.Render(golden.Diagnostic, golden.Source, colorOptions);
    Assert.Contains("\x1b[", colored, StringComparison.Ordinal);
    Assert.Equal(golden.Expected, AnsiEscapeRegex().Replace(colored, string.Empty));
  }

  [Fact]
  public void RenderAllOrdersBySpanStartThenCodeAndSeparatesWithBlankLine() {
    var source = new SourceText("val x = 1\nval y = 2\n", "order.pino");
    Diagnostic[] diagnostics = [
      new(DiagnosticCodes.UnexpectedCharacter, DiagnosticSeverity.Error, new SourceSpan(10, 3), "second line"),
      new("PN1099", DiagnosticSeverity.Warning, new SourceSpan(0, 3), "first span B"),
      new(DiagnosticCodes.UnexpectedCharacter, DiagnosticSeverity.Error, new SourceSpan(0, 3), "first span A"),
    ];

    string actual = DiagnosticRenderer.RenderAll(diagnostics, source);
    string expected =
        "error[PN1001]: first span A\n"
        + " --> order.pino:1:1\n"
        + "  |\n"
        + "1 | val x = 1\n"
        + "  | ^^^\n"
        + "\n"
        + "warning[PN1099]: first span B\n"
        + " --> order.pino:1:1\n"
        + "  |\n"
        + "1 | val x = 1\n"
        + "  | ^^^\n"
        + "\n"
        + "error[PN1001]: second line\n"
        + " --> order.pino:2:1\n"
        + "  |\n"
        + "2 | val y = 2\n"
        + "  | ^^^\n";

    Assert.Equal(expected, actual);
  }

  [Fact]
  public void RenderAllColoredStripsToPlain() {
    var source = new SourceText("val x = 1\nval y = 2\n", "order.pino");
    Diagnostic[] diagnostics = [
      new(DiagnosticCodes.UnexpectedCharacter, DiagnosticSeverity.Error, new SourceSpan(10, 3), "second line"),
      new(DiagnosticCodes.UnexpectedCharacter, DiagnosticSeverity.Error, new SourceSpan(0, 3), "first span"),
    ];

    string plain = DiagnosticRenderer.RenderAll(diagnostics, source);
    string colored = DiagnosticRenderer.RenderAll(diagnostics, source, new DiagnosticRenderOptions { Color = true });

    Assert.Contains("\x1b[", colored, StringComparison.Ordinal);
    Assert.Equal(plain, AnsiEscapeRegex().Replace(colored, string.Empty));
  }

  [Fact]
  public void EndToEndLexerDiagnosticRendersUnderlineUnderBackslash() {
    var source = new SourceText("val s = \"a\\qb\"", "demo.pino");
    LexResult lex = Lexer.Lex(source);
    Diagnostic diagnostic = Assert.Single(lex.Diagnostics);

    string rendered = DiagnosticRenderer.Render(diagnostic, source);
    string[] lines = rendered.Split('\n');

    string sourceLine = Assert.Single(lines, static l => l.StartsWith("1 | ", StringComparison.Ordinal));
    string underlineLine = Assert.Single(lines, static l => l.StartsWith("  | ", StringComparison.Ordinal) && l.Contains('^'));

    Assert.Equal("1 | val s = \"a\\qb\"", sourceLine);
    int backslashIndex = sourceLine.IndexOf('\\');
    int caretIndex = underlineLine.IndexOf('^');
    Assert.Equal(backslashIndex, caretIndex);
  }

  [Fact]
  public void RobustnessEveryConformanceErrFileRendersWithoutThrowing() {
    string repoRoot = FindRepositoryRoot();
    string conformanceDir = Path.Combine(repoRoot, "conformance");
    if (!Directory.Exists(conformanceDir)) {
      return;
    }

    var plainOptions = new DiagnosticRenderOptions { Color = false };
    var colorOptions = new DiagnosticRenderOptions { Color = true };

    foreach (string pinoFile in Directory.EnumerateFiles(conformanceDir, "*.pino", SearchOption.AllDirectories)) {
      string errFile = Path.ChangeExtension(pinoFile, ".err");
      if (!File.Exists(errFile)) {
        continue;
      }

      string text = File.ReadAllText(pinoFile);
      string relativePath = Path.GetRelativePath(repoRoot, pinoFile).Replace('\\', '/');
      var source = new SourceText(text, relativePath);
      LexResult lex = Lexer.Lex(source);

      foreach (Diagnostic diagnostic in lex.Diagnostics) {
        string expectedSeverity = diagnostic.Severity switch {
          DiagnosticSeverity.Error => "error",
          DiagnosticSeverity.Warning => "warning",
          DiagnosticSeverity.Info => "info",
          DiagnosticSeverity.Hint => "hint",
          _ => "error",
        };

        string plain = DiagnosticRenderer.Render(diagnostic, source, plainOptions);
        Assert.StartsWith(expectedSeverity, plain, StringComparison.Ordinal);

        string colored = DiagnosticRenderer.Render(diagnostic, source, colorOptions);
        string stripped = AnsiEscapeRegex().Replace(colored, string.Empty);
        Assert.StartsWith(expectedSeverity, stripped, StringComparison.Ordinal);
        Assert.Equal(plain, stripped);
      }
    }
  }

  private static IEnumerable<GoldenCase> BuildGoldenCases() {
    // 1. single-line span
    yield return new GoldenCase(
        "SingleLineSpan",
        new Diagnostic(
            DiagnosticCodes.InvalidStringEscape,
            DiagnosticSeverity.Error,
            new SourceSpan(10, 2),
            "unknown escape sequence '\\q'"),
        new SourceText("val s = \"a\\qb\"", "demo.pino"),
        null,
        "error[PN1011]: unknown escape sequence '\\q'\n"
        + " --> demo.pino:1:11\n"
        + "  |\n"
        + "1 | val s = \"a\\qb\"\n"
        + "  |           ^^\n");

    // 2. zero-width span at end of line
    yield return new GoldenCase(
        "ZeroWidthAtEndOfLine",
        new Diagnostic(
            DiagnosticCodes.UnterminatedString,
            DiagnosticSeverity.Error,
            new SourceSpan(11, 0),
            "expected closing quote"),
        new SourceText("val s = \"hi", "eof.pino"),
        null,
        "error[PN1009]: expected closing quote\n"
        + " --> eof.pino:1:12\n"
        + "  |\n"
        + "1 | val s = \"hi\n"
        + "  |            ^\n");

    // 3. tab before the span
    yield return new GoldenCase(
        "TabBeforeSpan",
        new Diagnostic(
            DiagnosticCodes.UnexpectedCharacter,
            DiagnosticSeverity.Error,
            new SourceSpan(3, 1),
            "unexpected character '@'"),
        new SourceText("\ta\t@", "tabs.pino"),
        null,
        "error[PN1001]: unexpected character '@'\n"
        + " --> tabs.pino:1:4\n"
        + "  |\n"
        + "1 |     a   @\n"
        + "  |         ^\n");

    // 4. wide characters (世界) and a combining mark (e + U+0301) before the span
    yield return new GoldenCase(
        "WideAndCombiningBeforeSpan",
        new Diagnostic(
            DiagnosticCodes.UnexpectedCharacter,
            DiagnosticSeverity.Error,
            new SourceSpan(4, 1),
            "unexpected character '@'"),
        new SourceText("世界e\u0301@", "wide.pino"),
        null,
        "error[PN1001]: unexpected character '@'\n"
        + " --> wide.pino:1:5\n"
        + "  |\n"
        + "1 | 世界e\u0301@\n"
        + "  |      ^\n");

    // 5. raw ESC (U+001B) shown as 
    yield return new GoldenCase(
        "RawEscControlChar",
        new Diagnostic(
            DiagnosticCodes.ControlCharacterInLiteral,
            DiagnosticSeverity.Error,
            new SourceSpan(1, 1),
            "control character in literal"),
        new SourceText("a\u001bb", "ctrl.pino"),
        null,
        "error[PN1016]: control character in literal\n"
        + " --> ctrl.pino:1:2\n"
        + "  |\n"
        + "1 | a\uFFFDb\n"
        + "  |  ^\n");

    // 6. related span on the same line
    yield return new GoldenCase(
        "RelatedOnSameLine",
        new Diagnostic(
            DiagnosticCodes.UnexpectedCharacter,
            DiagnosticSeverity.Error,
            new SourceSpan(8, 1),
            "duplicate binding",
            [new RelatedLocation(new SourceSpan(4, 1), "first bound here")]),
        new SourceText("let x = x", "same.pino"),
        null,
        "error[PN1001]: duplicate binding\n"
        + " --> same.pino:1:9\n"
        + "  |\n"
        + "1 | let x = x\n"
        + "  |         ^\n"
        + "  |     - first bound here\n");

    // 7. related span on the adjacent line (no ...)
    yield return new GoldenCase(
        "RelatedOnAdjacentLine",
        new Diagnostic(
            DiagnosticCodes.UnexpectedCharacter,
            DiagnosticSeverity.Error,
            new SourceSpan(14, 1),
            "redefined 'x'",
            [new RelatedLocation(new SourceSpan(4, 1), "originally here")]),
        new SourceText("let x = 1\nlet x = 2", "adj.pino"),
        null,
        "error[PN1001]: redefined 'x'\n"
        + " --> adj.pino:2:5\n"
        + "  |\n"
        + "1 | let x = 1\n"
        + "  |     - originally here\n"
        + "2 | let x = 2\n"
        + "  |     ^\n");

    // 8. related span on a distant line (...)
    yield return new GoldenCase(
        "RelatedOnDistantLine",
        new Diagnostic(
            DiagnosticCodes.UnexpectedCharacter,
            DiagnosticSeverity.Error,
            new SourceSpan(26, 1),
            "redefined 'x'",
            [new RelatedLocation(new SourceSpan(4, 1), "originally here")]),
        new SourceText("let x = 1\nline2\nline3\nlet x = 2", "dist.pino"),
        null,
        "error[PN1001]: redefined 'x'\n"
        + " --> dist.pino:4:5\n"
        + "  |\n"
        + "1 | let x = 1\n"
        + "  |     - originally here\n"
        + "...\n"
        + "4 | let x = 2\n"
        + "  |     ^\n");

    // 9. related location in another file
    yield return new GoldenCase(
        "RelatedInAnotherFile",
        new Diagnostic(
            DiagnosticCodes.UnexpectedCharacter,
            DiagnosticSeverity.Error,
            new SourceSpan(4, 1),
            "mismatched type",
            [new RelatedLocation(new SourceSpan(0, 3), "declared as int", "types.pino")]),
        new SourceText("let x = \"hi\"", "main.pino"),
        null,
        "error[PN1001]: mismatched type\n"
        + " --> main.pino:1:5\n"
        + "  |\n"
        + "1 | let x = \"hi\"\n"
        + "  |     ^\n"
        + "  = note: declared as int (in types.pino)\n");

    // 10. help with a Pino note under Voice = Pino (with a test notes dictionary)
    yield return new GoldenCase(
        "HelpWithPinoNote",
        new Diagnostic(
            DiagnosticCodes.InvalidStringEscape,
            DiagnosticSeverity.Error,
            new SourceSpan(10, 2),
            "unknown escape sequence '\\q'",
            Help: "write '\\\\' for a literal backslash"),
        new SourceText("val s = \"a\\qb\"", "demo.pino"),
        new DiagnosticRenderOptions { Voice = DiagnosticVoice.Pino, Notes = s_testNotes },
        "error[PN1011]: unknown escape sequence '\\q'\n"
        + " --> demo.pino:1:11\n"
        + "  |\n"
        + "1 | val s = \"a\\qb\"\n"
        + "  |           ^^\n"
        + "  = help: write '\\\\' for a literal backslash\n"
        + "  = Pino: backslashes are picky in strings\n");

    // 11. the same diagnostic under Voice = Plain
    yield return new GoldenCase(
        "HelpWithPlainVoice",
        new Diagnostic(
            DiagnosticCodes.InvalidStringEscape,
            DiagnosticSeverity.Error,
            new SourceSpan(10, 2),
            "unknown escape sequence '\\q'",
            Help: "write '\\\\' for a literal backslash"),
        new SourceText("val s = \"a\\qb\"", "demo.pino"),
        new DiagnosticRenderOptions { Voice = DiagnosticVoice.Plain, Notes = s_testNotes },
        "error[PN1011]: unknown escape sequence '\\q'\n"
        + " --> demo.pino:1:11\n"
        + "  |\n"
        + "1 | val s = \"a\\qb\"\n"
        + "  |           ^^\n"
        + "  = help: write '\\\\' for a literal backslash\n");

    // 12. Voice = Pino with no note for the code
    yield return new GoldenCase(
        "PinoVoiceWithMissingCodeNote",
        new Diagnostic(
            DiagnosticCodes.UnexpectedCharacter,
            DiagnosticSeverity.Warning,
            new SourceSpan(0, 1),
            "unused token"),
        new SourceText("x", "warn.pino"),
        new DiagnosticRenderOptions { Voice = DiagnosticVoice.Pino, Notes = s_testNotes },
        "warning[PN1001]: unused token\n"
        + " --> warn.pino:1:1\n"
        + "  |\n"
        + "1 | x\n"
        + "  | ^\n");

    // 13a. multi-line span, adjacent
    yield return new GoldenCase(
        "MultiLineAdjacent",
        new Diagnostic(
            DiagnosticCodes.UnterminatedMultilineString,
            DiagnosticSeverity.Error,
            new SourceSpan(8, 12),
            "unclosed block"),
        new SourceText("val s = \"\"\"\n  hello\"", "ml1.pino"),
        null,
        "error[PN1010]: unclosed block\n"
        + " --> ml1.pino:1:9\n"
        + "  |\n"
        + "1 | val s = \"\"\"\n"
        + "  |         ^^^\n"
        + "2 |   hello\"\n"
        + "  |   ^^^^^^\n");

    // 13b. multi-line span, with a gap
    yield return new GoldenCase(
        "MultiLineWithGap",
        new Diagnostic(
            DiagnosticCodes.UnterminatedMultilineString,
            DiagnosticSeverity.Error,
            new SourceSpan(8, 22),
            "unclosed block"),
        new SourceText("val s = \"\"\"\nmid1\nmid2\n  end\"", "ml2.pino"),
        null,
        "error[PN1010]: unclosed block\n"
        + " --> ml2.pino:1:9\n"
        + "  |\n"
        + "1 | val s = \"\"\"\n"
        + "  |         ^^^\n"
        + "...\n"
        + "4 |   end\"\n"
        + "  |   ^^^^\n");

    // 14. long line truncated on both sides
    string longPrefix = new('a', 20);
    string longSuffix = new('b', 20);
    yield return new GoldenCase(
        "LongLineTruncatedBothSides",
        new Diagnostic(
            DiagnosticCodes.UnexpectedCharacter,
            DiagnosticSeverity.Info,
            new SourceSpan(20, 3),
            "bad slice"),
        new SourceText(longPrefix + "XYZ" + longSuffix, "long.pino"),
        new DiagnosticRenderOptions { MaxLineWidth = 10 },
        "info[PN1001]: bad slice\n"
        + " --> long.pino:1:21\n"
        + "  |\n"
        + "1 | ...aaaaaXYZbb...\n"
        + "  |         ^^^\n");

    // 15. gutter width 2 (primary on line 9, related on line 10)
    string nineLines = "l1\nl2\nl3\nl4\nl5\nl6\nl7\nl8\nlet a = 1\nlet b = 2\n";
    int line9Offset = nineLines.IndexOf("let a", StringComparison.Ordinal);
    int line10Offset = nineLines.IndexOf("let b", StringComparison.Ordinal);
    yield return new GoldenCase(
        "GutterWidthTwo",
        new Diagnostic(
            DiagnosticCodes.UnexpectedCharacter,
            DiagnosticSeverity.Hint,
            new SourceSpan(line9Offset + 4, 1),
            "consider renaming",
            [new RelatedLocation(new SourceSpan(line10Offset + 4, 1), "conflicts here")]),
        new SourceText(nineLines, "gutter.pino"),
        null,
        "hint[PN1001]: consider renaming\n"
        + "  --> gutter.pino:9:5\n"
        + "   |\n"
        + " 9 | let a = 1\n"
        + "   |     ^\n"
        + "10 | let b = 2\n"
        + "   |     - conflicts here\n");

    // 16. CRLF source (no \r in the output)
    yield return new GoldenCase(
        "CrlfSource",
        new Diagnostic(
            DiagnosticCodes.UnexpectedCharacter,
            DiagnosticSeverity.Error,
            new SourceSpan(6, 1),
            "bad char",
            [new RelatedLocation(new SourceSpan(0, 1), "first line")]),
        new SourceText("abcd\r\nef\r\n", "crlf.pino"),
        null,
        "error[PN1001]: bad char\n"
        + " --> crlf.pino:2:1\n"
        + "  |\n"
        + "1 | abcd\n"
        + "  | - first line\n"
        + "2 | ef\n"
        + "  | ^\n");

    // 17. FilePath == null printing <input>
    yield return new GoldenCase(
        "NullFilePathPrintsInput",
        new Diagnostic(
            DiagnosticCodes.UnexpectedCharacter,
            DiagnosticSeverity.Error,
            new SourceSpan(0, 1),
            "unexpected '@'"),
        new SourceText("@"),
        null,
        "error[PN1001]: unexpected '@'\n"
        + " --> <input>:1:1\n"
        + "  |\n"
        + "1 | @\n"
        + "  | ^\n");
  }

  private static string FindRepositoryRoot() {
    for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) {
      if (File.Exists(Path.Combine(directory.FullName, "main.pino"))
          && Directory.Exists(Path.Combine(directory.FullName, "projects", "pino-compiler"))) {
        return directory.FullName;
      }
    }

    throw new DirectoryNotFoundException(
        $"Could not locate the repository root (main.pino + projects/pino-compiler) above '{AppContext.BaseDirectory}'.");
  }

  [GeneratedRegex(@"\x1b\[[0-9;]*m")]
  private static partial Regex AnsiEscapeRegex();
}
