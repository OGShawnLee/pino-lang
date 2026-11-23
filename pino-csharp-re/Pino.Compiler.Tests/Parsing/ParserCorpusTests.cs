using System.Diagnostics;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Lexing;
using Pino.Compiler.Text;

using Xunit.Abstractions;

namespace Pino.Compiler.Tests.Parsing;

/// <summary>Parses every repository <c>.pino</c> file and checks invariants and expected diagnostics.</summary>
public sealed class ParserCorpusTests(ITestOutputHelper output) {
  public sealed record AllowedDiagnostics(IReadOnlySet<string> Codes, string Reason);

  private static readonly string s_repositoryRoot = ParserTestHelpers.FindRepositoryRoot();

  public static readonly IReadOnlyDictionary<string, AllowedDiagnostics> Allowlist =
      new Dictionary<string, AllowedDiagnostics>(StringComparer.OrdinalIgnoreCase) {
        ["projects/c-compiler/modules/checker.pino"] = Allow(
            "Uses legacy '=>' match arms instead of '->' (D-100).",
            DiagnosticCodes.DeprecatedFatArrow),
        ["projects/c-compiler/modules/compiler.pino"] = Allow(
            "Uses legacy '=>' match arms instead of '->' (D-100).",
            DiagnosticCodes.DeprecatedFatArrow),
        ["projects/c-compiler/modules/lexer.pino"] = Allow(
            "Uses D-020 unescaped regex backslashes and legacy 'fn (T) R' type syntax without '->' (D-100).",
            DiagnosticCodes.InvalidStringEscape,
            DiagnosticCodes.ExpectedType),
        ["projects/c-compiler/modules/parser.pino"] = Allow(
            "Uses legacy map ':' entries (MAP-E4) and '=>' arms (D-100).",
            DiagnosticCodes.ExpectedToken,
            DiagnosticCodes.ExpectedIdentifier,
            DiagnosticCodes.DeprecatedFatArrow),
        ["projects/c-compiler/modules/precompiler.pino"] = Allow(
            "Uses legacy '=>' match arms instead of '->' (D-100).",
            DiagnosticCodes.DeprecatedFatArrow),
        ["projects/c-compiler/modules/transformer.pino"] = Allow(
            "Uses legacy '=>' match arms instead of '->' (D-100).",
            DiagnosticCodes.DeprecatedFatArrow),
        ["projects/c-compiler/modules/translator.pino"] = Allow(
            "Uses legacy map ':' entries (MAP-E4) and '=>' match arms (D-100).",
            DiagnosticCodes.ExpectedToken,
            DiagnosticCodes.DeprecatedFatArrow),
        ["projects/c-compiler/modules/utility.pino"] = Allow(
            "Uses deprecated '@generic[...]' (D-072) and legacy 'fn (T) R' type syntax without '->' (D-100).",
            DiagnosticCodes.ExpectedType,
            DiagnosticCodes.DeprecatedGenericSyntax),
        ["projects/pino-compiler/modules/Checker.pino"] = Allow(
            "Uses legacy '=>' match arms instead of '->' (D-100).",
            DiagnosticCodes.DeprecatedFatArrow),
        ["projects/pino-compiler/modules/Entities.pino"] = Allow(
            "Uses legacy '=>' match arms instead of '->' (D-100).",
            DiagnosticCodes.DeprecatedFatArrow),
        ["projects/pino-compiler/modules/Environment.pino"] = Allow(
            "Uses legacy '=>' match arms (D-100) and legacy map ':' entries (MAP-E4).",
            DiagnosticCodes.ExpectedToken,
            DiagnosticCodes.ExpectedStatement,
            DiagnosticCodes.DeprecatedFatArrow),
        ["projects/pino-compiler/modules/Utility.pino"] = Allow(
            "Uses '@pub_testing' instead of '@@Testable' (D-163).",
            DiagnosticCodes.DeprecatedVisibilitySyntax),
        ["projects/pino-compiler/modules/Lexer.pino"] = Allow(
            "Uses D-020 unescaped regex backslashes.",
            DiagnosticCodes.InvalidStringEscape),
        ["projects/pino-compiler/modules/Lexing.pino"] = Allow(
            "Uses legacy map ':' entries (MAP-E4) and '=>' match arms (D-100).",
            DiagnosticCodes.ExpectedToken,
            DiagnosticCodes.DeprecatedFatArrow),
        ["projects/pino-compiler/modules/Parser.pino"] = Allow(
            "Uses legacy map ':' entries (MAP-E4), '=>' match arms (D-100) and '@pub_testing' (D-163).",
            DiagnosticCodes.ExpectedToken,
            DiagnosticCodes.ExpectedIdentifier,
            DiagnosticCodes.DeprecatedFatArrow,
            DiagnosticCodes.DeprecatedVisibilitySyntax),
        ["projects/pino-compiler/test/match_expr_test.pino"] = Allow(
            "Uses legacy '=>' match arms instead of '->' (D-100).",
            DiagnosticCodes.DeprecatedFatArrow),
        ["projects/pino-compiler/test/reference_equality_test.pino"] = Allow(
            "Uses legacy map ':' entries instead of '->' (MAP-E4) and removed '==='/'!==' operators (D-178).",
            DiagnosticCodes.ExpectedIdentifier,
            DiagnosticCodes.RemovedIdentityOperator),
        ["projects/pino-compiler/test/str_conversion_test.pino"] = Allow(
            "Uses legacy map ':' entries instead of '->' (MAP-E4).",
            DiagnosticCodes.ExpectedToken,
            DiagnosticCodes.ExpectedIdentifier),
        ["projects/pino-compiler/test/structural_equality_test.pino"] = Allow(
            "Uses legacy map ':' entries instead of '->' (MAP-E4).",
            DiagnosticCodes.ExpectedIdentifier),
        ["projects/pino-compiler/test/union_equality_test.pino"] = Allow(
            "Uses deprecated '@generic[...]' instead of '@@Generic[...]' (D-072).",
            DiagnosticCodes.DeprecatedGenericSyntax),
        ["projects/pino-compiler/test/union_payload_test.pino"] = Allow(
            "Uses legacy map ':' entries (MAP-E4) and deprecated '@generic[...]' (D-072).",
            DiagnosticCodes.ExpectedIdentifier,
            DiagnosticCodes.DeprecatedGenericSyntax),
        ["projects/pino-compiler/test/vector_generic_mapping_test.pino"] = Allow(
            "Uses deprecated '@generic[...]' instead of '@@Generic[...]' (D-072).",
            DiagnosticCodes.DeprecatedGenericSyntax),
        ["projects/pino-compiler/test/vector_instance_method_test.pino"] = Allow(
            "Uses legacy '=>' lambda arrows instead of '->' (D-100).",
            DiagnosticCodes.DeprecatedFatArrow),
      };

  private static AllowedDiagnostics Allow(string reason, params string[] codes) =>
      new(new HashSet<string>(codes, StringComparer.Ordinal), reason);

  [Theory]
  [MemberData(nameof(LexerCorpusTests.CorpusFiles), MemberType = typeof(LexerCorpusTests))]
  public void EveryCorpusFileParsesWithInvariantsAndExpectedDiagnostics(string relativePath) {
    string fullPath = Path.Combine(s_repositoryRoot, relativePath);
    string text = File.ReadAllText(fullPath);
    var source = new SourceText(text, relativePath);

    var sw = Stopwatch.StartNew();
    ParseResult result = Parser.Parse(source);
    sw.Stop();

    ParserTestHelpers.AssertFileInvariants(result, expectNoDiagnostics: false);
    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"{relativePath} took {sw.Elapsed.TotalMilliseconds:F1} ms.");

    if (HasSiblingErrFile(relativePath, fullPath)) {
      return;
    }

    if (Allowlist.TryGetValue(relativePath, out AllowedDiagnostics? allowed)) {
      AssertAllowlistEntryMatches(relativePath, allowed, result);
      return;
    }

    Assert.True(
        result.Diagnostics.All(static d => d.Severity == DiagnosticSeverity.Hint),
        $"{relativePath} produced unexpected diagnostics: {FormatDiagnostics(result)}");
  }

  [Fact]
  public void AllowlistContainsNoStaleEntries() {
    foreach ((string relativePath, AllowedDiagnostics allowed) in Allowlist) {
      string fullPath = Path.Combine(s_repositoryRoot, relativePath);
      if (!File.Exists(fullPath)) {
        continue;
      }

      string text = File.ReadAllText(fullPath);
      ParseResult result = Parser.Parse(new SourceText(text, relativePath));
      AssertAllowlistEntryMatches(relativePath, allowed, result);
    }
  }

  [Fact]
  public void StaleAllowlistEntry_FailsCheck() {
    ParseResult clean = Parser.Parse(new SourceText("fn ok() { val x = 1 }", "clean.pino"));
    AllowedDiagnostics staleEntry = Allow("synthetic stale entry", DiagnosticCodes.DeprecatedFatArrow);
    Assert.ThrowsAny<Exception>(() => AssertAllowlistEntryMatches("clean.pino", staleEntry, clean));
  }

  private static void AssertAllowlistEntryMatches(string relativePath, AllowedDiagnostics allowed, ParseResult result) {
    Diagnostic[] reported = [.. result.Diagnostics.Where(static d => d.Severity != DiagnosticSeverity.Hint)];
    Assert.True(
        reported.Length > 0,
        $"Stale allowlist entry '{relativePath}' ({allowed.Reason}): file now parses with zero diagnostics.");
    string[] unexpected = [.. reported
        .Select(static d => d.Code)
        .Where(code => !allowed.Codes.Contains(code))
        .Distinct(StringComparer.Ordinal)];
    Assert.True(
        unexpected.Length == 0,
        $"{relativePath} produced unexpected diagnostics [{string.Join(", ", unexpected)}]; allowed: [{string.Join(", ", allowed.Codes)}]. Details: {FormatDiagnostics(result)}");
  }

  [Fact]
  public void CorpusDirectorySummary() {
    var byDir = new SortedDictionary<string, (int Files, int WithDiagnostics)>(StringComparer.Ordinal);
    foreach (string relativePath in ParserTestHelpers.GetCorpusFiles()) {
      string dir = GetTopLevelBucket(relativePath);
      string fullPath = Path.Combine(s_repositoryRoot, relativePath);
      ParseResult result = Parser.Parse(new SourceText(File.ReadAllText(fullPath), relativePath));
      byDir.TryGetValue(dir, out (int Files, int WithDiagnostics) counts);
      byDir[dir] = (counts.Files + 1, counts.WithDiagnostics + (result.Diagnostics.IsEmpty ? 0 : 1));
    }

    foreach ((string dir, (int files, int withDiag)) in byDir) {
      output.WriteLine($"{dir}: total={files}, withDiagnostics={withDiag}");
    }
  }

  private static bool HasSiblingErrFile(string relativePath, string fullPath) =>
      relativePath.StartsWith("conformance/", StringComparison.Ordinal)
      && File.Exists(Path.ChangeExtension(fullPath, ".err"));

  private static string GetTopLevelBucket(string relativePath) {
    int slash = relativePath.IndexOf('/');
    return slash < 0 ? "(root)" : relativePath[..slash];
  }

  private static string FormatDiagnostics(ParseResult result) =>
      string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code}@{d.Span.Start}+{d.Span.Length}: {d.Message}"));
}
