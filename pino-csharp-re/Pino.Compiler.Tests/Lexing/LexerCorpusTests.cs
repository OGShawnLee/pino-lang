using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Lexing;

/// <summary>Lexes the repository's real <c>.pino</c> programs end to end.</summary>
public sealed class LexerCorpusTests {
  // D-020: compiler modules with unescaped regex backslashes (pending owner edits).
  private static readonly HashSet<string> s_expectedInvalidEscapeFiles = new(StringComparer.OrdinalIgnoreCase) {
    "projects/pino-compiler/modules/Lexer.pino",
    "projects/c-compiler/modules/lexer.pino",
  };

  private static readonly HashSet<string> s_errorTokenDiagnosticCodes = new(StringComparer.Ordinal) {
    DiagnosticCodes.UnexpectedCharacter,
    DiagnosticCodes.InvalidNumberSuffix,
    DiagnosticCodes.EmptyRune,
    DiagnosticCodes.InvalidRuneEscape,
    DiagnosticCodes.MultiRuneLiteral,
    DiagnosticCodes.UnterminatedRune,
    DiagnosticCodes.InvalidRuneSurrogate,
    DiagnosticCodes.KeywordInSimpleInterpolation,
  };

  // LEX-E*, STR-E*, and RUNE-E* rules whose errors belong to the parser or checker rather than the lexer.
  private static readonly HashSet<string> s_nonLexerErrorRules = new(StringComparer.Ordinal) {
    // Parser error: keywords lex as keyword tokens; using one as an identifier is rejected by the parser.
    "LEX-E1",
    // Parser error: '-' and adjacent tokens lex cleanly; ambiguous spacing is rejected by the parser.
    "LEX-E2",
    // Parser error: ',' lexes as a comma token; trailing commas are rejected by the parser.
    "LEX-E4",
    // Parser error: '=>' lexes cleanly; fat arrow in function/match syntax is rejected by the parser.
    "LEX-E5",
    // Checker error: string index assignment lexes and parses cleanly; immutability is enforced by the checker.
    "STR-E1",
  };

  private static readonly string s_repositoryRoot = FindRepositoryRoot();

  public static TheoryData<string> CorpusFiles {
    get {
      var data = new TheoryData<string>();
      foreach (string path in EnumerateCorpusFiles()) {
        data.Add(path);
      }

      return data;
    }
  }

  public static TheoryData<string> LexerErrorConformanceCases {
    get {
      var data = new TheoryData<string>();
      foreach (string path in EnumerateCorpusFiles()) {
        if (!HasSiblingErrFile(path)) {
          continue;
        }

        string text = File.ReadAllText(Path.Combine(s_repositoryRoot, path));
        if (GetCoveredRuleIds(text).Any(IsLexerErrorRule)) {
          data.Add(path);
        }
      }

      return data;
    }
  }

  private static bool HasSiblingErrFile(string relativePath) =>
    relativePath.StartsWith("conformance/", StringComparison.Ordinal)
    && File.Exists(Path.ChangeExtension(Path.Combine(s_repositoryRoot, relativePath), ".err"));

  private static IEnumerable<string> GetCoveredRuleIds(string text) {
    using var reader = new StringReader(text);
    while (reader.ReadLine() is { } line) {
      string trimmed = line.Trim();
      if (!trimmed.StartsWith('#')) {
        break;
      }

      const string prefix = "# covers:";
      if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
        string rest = trimmed[prefix.Length..];
        foreach (string part in rest.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
          yield return part;
        }
      }
    }
  }

  private static bool IsLexerErrorRule(string ruleId) =>
    (ruleId.StartsWith("LEX-E", StringComparison.Ordinal)
      || ruleId.StartsWith("STR-E", StringComparison.Ordinal)
      || ruleId.StartsWith("RUNE-E", StringComparison.Ordinal))
    && !s_nonLexerErrorRules.Contains(ruleId);

  private static bool ProducesErrorToken(Diagnostic diagnostic) =>
    s_errorTokenDiagnosticCodes.Contains(diagnostic.Code)
    || (diagnostic.Code == DiagnosticCodes.ControlCharacterInLiteral
      && diagnostic.Message.Contains("in rune literal", StringComparison.Ordinal));

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

  /// <summary>Repository-relative, forward-slash paths of every corpus file, in stable order.</summary>
  private static IEnumerable<string> EnumerateCorpusFiles() {
    yield return "main.pino";

    string[] directories = ["projects", "conformance", "pino.games"];
    foreach (string relativeDir in directories) {
      string fullDir = Path.Combine(s_repositoryRoot, relativeDir);
      if (!Directory.Exists(fullDir)) {
        continue;
      }

      foreach (string file in Directory.EnumerateFiles(fullDir, "*.pino", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(s_repositoryRoot, path).Replace('\\', '/'))
        .Order(StringComparer.Ordinal)) {
        yield return file;
      }
    }
  }

  private static (string Text, LexResult Result) LexFile(string relativePath) {
    string text = File.ReadAllText(Path.Combine(s_repositoryRoot, relativePath));
    return (text, Lexer.Lex(new SourceText(text)));
  }

  [Fact]
  public void CorpusContainsMainProjectsConformanceAndGames() {
    string[] files = [.. EnumerateCorpusFiles()];

    Assert.Contains("main.pino", files);
    Assert.Contains("projects/pino-compiler/modules/Lexer.pino", files);
    Assert.Contains("projects/c-compiler/modules/lexer.pino", files);
    Assert.Contains("projects/pino-compiler/main.pino", files);
    Assert.Contains(files, static f => f.StartsWith("conformance/", StringComparison.Ordinal));
    Assert.Contains(files, static f => f.StartsWith("pino.games/", StringComparison.Ordinal));
    Assert.True(files.Length > 200, $"Expected full repo corpus, found {files.Length} files.");
  }

  [Theory]
  [MemberData(nameof(CorpusFiles))]
  public void EveryFileHasWellFormedNonOverlappingSpansAndEndsWithEOF(string relativePath) {
    (string text, LexResult result) = LexFile(relativePath);

    Assert.Equal(TokenKind.EOF, result.Tokens[^1].Kind);
    Assert.Equal(new SourceSpan(text.Length, 0), result.Tokens[^1].Span);
    Assert.Equal(1, result.Tokens.Count(static t => t.Kind == TokenKind.EOF));

    // Tokens + trivia tile the source with no gaps and no overlaps (zero-length tokens aside).
    int position = 0;
    foreach (SourceSpan span in result.Tokens.Select(static t => t.Span)
      .Concat(result.Trivia.Select(static t => t.Span))
      .OrderBy(static s => s.Start).ThenBy(static s => s.Length)) {
      Assert.True(span.Start == position, $"{relativePath}: gap or overlap at offset {span.Start} (expected {position}).");
      position = span.End;
    }

    Assert.Equal(text.Length, position);

    Assert.Equal(
      result.Tokens.Count(static t => t.Kind == TokenKind.StringStart),
      result.Tokens.Count(static t => t.Kind == TokenKind.StringEnd));
    Assert.Equal(
      result.Tokens.Count(static t => t.Kind == TokenKind.InterpStart),
      result.Tokens.Count(static t => t.Kind == TokenKind.InterpEnd));

    // Error tokens only ever come from reported diagnostics (no silent error tokens).
    Assert.Equal(
      result.Diagnostics.Count(ProducesErrorToken),
      result.Tokens.Count(static t => t.Kind == TokenKind.Error));
  }

  [Theory]
  [MemberData(nameof(CorpusFiles))]
  public void EveryFileLexesWithoutDiagnosticsExceptTheKnownD020EscapeFiles(string relativePath) {
    // D-020: skip the two compiler modules (pending owner edits) and expected-error conformance cases (.err).
    if (s_expectedInvalidEscapeFiles.Contains(relativePath) || HasSiblingErrFile(relativePath)) {
      return;
    }

    (_, LexResult result) = LexFile(relativePath);

    Assert.True(
      result.Diagnostics.IsEmpty,
      $"{relativePath}: {string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}@{d.Span.Start}+{d.Span.Length}: {d.Message}"))}");
  }

  [Theory]
  [MemberData(nameof(LexerErrorConformanceCases))]
  public void ExpectedLexerErrorConformanceCasesReportAtLeastOneDiagnostic(string relativePath) {
    (_, LexResult result) = LexFile(relativePath);

    Assert.NotEmpty(result.Diagnostics);
  }

  // D-020
  [Theory]
  [InlineData("projects/pino-compiler/modules/Lexer.pino")]
  [InlineData("projects/c-compiler/modules/lexer.pino")]
  public void LegacyRegexModulesReportOnlyUnknownEscapeErrors(string relativePath) {
    (string text, LexResult result) = LexFile(relativePath);

    Assert.NotEmpty(result.Diagnostics);
    Assert.All(result.Diagnostics, static d => {
      Assert.Equal(DiagnosticCodes.InvalidStringEscape, d.Code);
      Assert.Equal(DiagnosticSeverity.Error, d.Severity);
      Assert.Equal(2, d.Span.Length);
    });

    // Every diagnostic is a '\' plus one regex metacharacter that Pino strings do not escape.
    HashSet<string> escapes = [.. result.Diagnostics.Select(d => text.Substring(d.Span.Start, d.Span.Length))];
    Assert.Superset(new HashSet<string> { "\\+", "\\*", "\\s" }, escapes);
    Assert.All(escapes, static e => Assert.Equal('\\', e[0]));
    Assert.DoesNotContain(escapes, static e => e is "\\n" or "\\t" or "\\r" or "\\\"" or "\\\\" or "\\$");

    // The rest of the file still lexes: the module's later declarations are present.
    Assert.DoesNotContain(result.Tokens, static t => t.Kind == TokenKind.Error);
    Assert.Equal(TokenKind.EOF, result.Tokens[^1].Kind);
    Assert.Contains(result.Tokens, static t => t.Kind == TokenKind.FnKeyword);
  }

  // D-020
  [Fact]
  public void UnknownEscapeConformanceCaseReportsOnlyInvalidStringEscape() {
    (_, LexResult result) = LexFile("conformance/strings/escape-unknown.pino");

    Diagnostic diagnostic = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.InvalidStringEscape, diagnostic.Code);
  }

  [Fact]
  public void MainProgramHasNoDiagnostics() {
    (_, LexResult result) = LexFile("main.pino");

    Assert.Empty(result.Diagnostics);
    Assert.Contains(result.Tokens, static t => t.Kind == TokenKind.StringStart);
  }
}
