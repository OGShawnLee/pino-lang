using System.Diagnostics;

using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

using Xunit.Abstractions;

namespace Pino.Compiler.Tests.Parsing;

public sealed class ParserSmokeTests(ITestOutputHelper output) {
  private static readonly string s_repositoryRoot = FindRepositoryRoot();

  public static TheoryData<string> SmokeFiles {
    get {
      var data = new TheoryData<string>();
      string selfHostParser = "projects/pino-compiler/modules/parser.pino";
      if (File.Exists(Path.Combine(s_repositoryRoot, selfHostParser))) {
        data.Add(selfHostParser);
      }

      string conformanceDir = Path.Combine(s_repositoryRoot, "conformance");
      if (Directory.Exists(conformanceDir)) {
        foreach (string file in Directory.EnumerateFiles(conformanceDir, "*.pino", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(s_repositoryRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)) {
          data.Add(file);
        }
      }

      return data;
    }
  }

  [Theory]
  [MemberData(nameof(SmokeFiles))]
  public void RealCodeSmoke_ParsesWithoutExceptionMaintainsTokenAccountingUnderOneSecond(string relativePath) {
    string text = File.ReadAllText(Path.Combine(s_repositoryRoot, relativePath));
    var source = new SourceText(text, relativePath);

    var sw = Stopwatch.StartNew();
    ParseResult result = Parser.Parse(source);
    sw.Stop();

    ParserTestHelpers.AssertFileInvariants(result, expectNoDiagnostics: false);
    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"{relativePath} took {sw.Elapsed.TotalMilliseconds:F1} ms.");
  }

  [Fact]
  public void ConformanceSmoke_RecordsDiagnosticStatistics() {
    string conformanceDir = Path.Combine(s_repositoryRoot, "conformance");
    string[] files = [.. Directory.EnumerateFiles(conformanceDir, "*.pino", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(s_repositoryRoot, path).Replace('\\', '/'))
        .Order(StringComparer.Ordinal)];

    Assert.NotEmpty(files);

    int zeroDiagCount = 0;
    int someDiagCount = 0;
    var codeCounts = new Dictionary<string, (int Count, string ExampleFile)>(StringComparer.Ordinal);

    foreach (string file in files) {
      string text = File.ReadAllText(Path.Combine(s_repositoryRoot, file));
      ParseResult result = Parser.Parse(new SourceText(text, file));
      ParserTestHelpers.AssertFileInvariants(result, expectNoDiagnostics: false);

      if (result.Diagnostics.IsEmpty) {
        zeroDiagCount++;
      } else {
        someDiagCount++;
        foreach (var diag in result.Diagnostics) {
          if (codeCounts.TryGetValue(diag.Code, out var existing)) {
            codeCounts[diag.Code] = (existing.Count + 1, existing.ExampleFile);
          } else {
            codeCounts[diag.Code] = (1, file);
          }
        }
      }
    }

    output.WriteLine($"Conformance files: total={files.Length}, zeroDiagnostics={zeroDiagCount}, withDiagnostics={someDiagCount}");
    foreach (var entry in codeCounts.OrderByDescending(static kv => kv.Value.Count).ThenBy(static kv => kv.Key, StringComparer.Ordinal).Take(10)) {
      output.WriteLine($"  {entry.Key}: {entry.Value.Count} (e.g. {entry.Value.ExampleFile})");
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
}
