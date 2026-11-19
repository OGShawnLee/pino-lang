using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Lexing;

/// <summary>Lexes the repository's real <c>.pino</c> programs end to end.</summary>
public sealed class LexerCorpusTests {
  private const string LegacyRegexModule = "projects/pino-compiler/modules/Lexer.pino";

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

    string compilerDirectory = Path.Combine(s_repositoryRoot, "projects", "pino-compiler");
    foreach (string file in Directory.EnumerateFiles(compilerDirectory, "*.pino", SearchOption.AllDirectories)
      .Select(path => Path.GetRelativePath(s_repositoryRoot, path).Replace('\\', '/'))
      .Order(StringComparer.Ordinal)) {
      yield return file;
    }
  }

  private static (string Text, LexResult Result) LexFile(string relativePath) {
    string text = File.ReadAllText(Path.Combine(s_repositoryRoot, relativePath));
    return (text, Lexer.Lex(new SourceText(text)));
  }

  [Fact]
  public void CorpusContainsMainAndTheSelfHostedCompiler() {
    string[] files = [.. EnumerateCorpusFiles()];

    Assert.Contains("main.pino", files);
    Assert.Contains(LegacyRegexModule, files);
    Assert.Contains("projects/pino-compiler/main.pino", files);
    Assert.True(files.Length > 20, $"Expected the full self-host corpus, found {files.Length} files.");
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
      result.Diagnostics.Count(static d => d.Code == Lexer.UnexpectedCharacterCode),
      result.Tokens.Count(static t => t.Kind == TokenKind.Error));
  }

  [Theory]
  [MemberData(nameof(CorpusFiles))]
  public void EveryFileLexesWithoutDiagnosticsExceptTheKnownLegacyRegexEscapes(string relativePath) {
    (string text, LexResult result) = LexFile(relativePath);

    if (relativePath == LegacyRegexModule) {
      return; // Covered precisely by LegacyRegexModuleReportsOnlyUnknownEscapeErrors.
    }

    Assert.True(
      result.Diagnostics.IsEmpty,
      $"{relativePath}: {string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}@{d.Span.Start}+{d.Span.Length}: {d.Message}"))}");
  }

  [Fact]
  public void LegacyRegexModuleReportsOnlyUnknownEscapeErrors() {
    (string text, LexResult result) = LexFile(LegacyRegexModule);

    Assert.NotEmpty(result.Diagnostics);
    Assert.All(result.Diagnostics, static d => {
      Assert.Equal(Lexer.InvalidStringEscapeCode, d.Code);
      Assert.Equal(DiagnosticSeverity.Error, d.Severity);
      Assert.Equal(2, d.Span.Length);
    });

    // Every diagnostic is a '\' plus one regex metacharacter that Pino strings do not escape.
    HashSet<string> escapes = [.. result.Diagnostics.Select(d => text.Substring(d.Span.Start, d.Span.Length))];
    Assert.Superset(new HashSet<string> { "\\+", "\\*", "\\[", "\\]", "\\s" }, escapes);
    Assert.All(escapes, static e => Assert.Equal('\\', e[0]));
    Assert.DoesNotContain(escapes, static e => e is "\\n" or "\\t" or "\\r" or "\\\"" or "\\\\" or "\\$");

    // The rest of the file still lexes: the module's later declarations are present.
    Assert.DoesNotContain(result.Tokens, static t => t.Kind == TokenKind.Error);
    Assert.Equal(TokenKind.EOF, result.Tokens[^1].Kind);
    Assert.Contains(result.Tokens, static t => t.Kind == TokenKind.FnKeyword);
  }

  [Fact]
  public void MainProgramHasNoDiagnostics() {
    (_, LexResult result) = LexFile("main.pino");

    Assert.Empty(result.Diagnostics);
    Assert.Contains(result.Tokens, static t => t.Kind == TokenKind.StringStart);
  }
}
