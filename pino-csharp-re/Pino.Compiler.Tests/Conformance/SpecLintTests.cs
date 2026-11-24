using System.Text.RegularExpressions;

namespace Pino.Compiler.Tests.Conformance;

public sealed class SpecLintTests {
  private static readonly Regex s_ruleLine = new(@"^- ([A-Z]+-E?\d+): ", RegexOptions.Compiled);
  private static readonly Regex s_coversLine = new(@"^#\s*covers:\s*(.*)$", RegexOptions.Compiled);
  private static readonly string[] s_featureHeadings = ["## Syntax", "## Rules", "## Errors", "## Examples", "## Open questions"];

  private sealed record Rule(string Id, string File, string Line);

  private static string SpecDir => Path.Combine(ConformanceRunner.RepoRoot, "spec");

  private static string Rel(string fullPath) =>
      Path.GetRelativePath(ConformanceRunner.RepoRoot, fullPath).Replace('\\', '/');

  private static IEnumerable<string> SpecRuleFiles() {
    foreach (string dir in new[] { "features", "stdlib" }) {
      string path = Path.Combine(SpecDir, dir);
      if (!Directory.Exists(path)) {
        continue;
      }

      foreach (string file in Directory.EnumerateFiles(path, "*.md").OrderBy(static f => f, StringComparer.Ordinal)) {
        yield return file;
      }
    }
  }

  /// <summary>Rule lines are only read inside the "## Rules" and "## Errors" sections.</summary>
  private static List<Rule> ReadRules(string file) {
    var rules = new List<Rule>();
    bool inRuleSection = false;
    foreach (string line in File.ReadAllLines(file)) {
      if (line.StartsWith("## ", StringComparison.Ordinal)) {
        string heading = line.Trim();
        inRuleSection = heading == "## Rules" || heading == "## Errors";
        continue;
      }

      if (!inRuleSection) {
        continue;
      }

      Match m = s_ruleLine.Match(line);
      if (m.Success) {
        rules.Add(new Rule(m.Groups[1].Value, Rel(file), line));
      }
    }

    return rules;
  }

  private static List<Rule> AllRules() => [.. SpecRuleFiles().SelectMany(ReadRules)];

  /// <summary>Case path -> IDs named by its "# covers:" first line (null when the header is missing).</summary>
  private static Dictionary<string, List<string>?> ReadCovers() {
    var result = new Dictionary<string, List<string>?>();
    foreach (string casePath in ConformanceRunner.DiscoverCases()) {
      string firstLine = File.ReadLines(ConformanceRunner.CaseFullPath(casePath)).FirstOrDefault() ?? string.Empty;
      Match m = s_coversLine.Match(firstLine.Trim());
      result[casePath] = m.Success
          ? [.. m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
          : null;
    }

    return result;
  }

  /// <summary>Spec files whose SPEC.md row says "migrated": (relative path under spec/, is stdlib).</summary>
  private static List<(string Path, bool IsStdlib)> MigratedFiles() {
    string specMd = Path.Combine(SpecDir, "SPEC.md");
    var result = new List<(string, bool)>();
    foreach (string raw in File.ReadAllLines(specMd)) {
      string line = raw.Trim();
      if (!line.StartsWith('|')) {
        continue;
      }

      List<string> cells = [.. line.Trim('|').Split('|').Select(static c => c.Trim())];
      if (cells.Count < 4 || cells[^1] != "migrated") {
        continue;
      }

      string? fileCell = cells.FirstOrDefault(static c => Regex.IsMatch(c, @"^`(features|stdlib)/[^`]+\.md`$"));
      if (fileCell is null) {
        continue;
      }

      string path = fileCell.Trim('`');
      result.Add((path, path.StartsWith("stdlib/", StringComparison.Ordinal)));
    }

    return result;
  }

  [Fact]
  public void MigratedFilesExistAndHaveRequiredHeadings() {
    List<(string Path, bool IsStdlib)> migrated = MigratedFiles();
    Assert.True(migrated.Count > 0, "spec/SPEC.md lists no migrated feature or stdlib file.");
    var problems = new List<string>();
    foreach ((string path, bool isStdlib) in migrated) {
      string full = Path.Combine(SpecDir, path);
      if (!File.Exists(full)) {
        problems.Add($"spec/{path}: listed as migrated in spec/SPEC.md but the file does not exist.");
        continue;
      }

      HashSet<string> headings = [.. File.ReadAllLines(full).Select(static l => l.Trim())];
      string[] required = isStdlib ? ["## Rules"] : s_featureHeadings;
      foreach (string heading in required) {
        if (!headings.Contains(heading)) {
          problems.Add($"spec/{path}: missing heading '{heading}'.");
        }
      }
    }

    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }

  [Fact]
  public void RuleIdsAreUnique() {
    List<string> problems = [.. AllRules()
        .GroupBy(static r => r.Id)
        .Where(static g => g.Count() > 1)
        .Select(static g => $"Rule ID {g.Key} is defined more than once: {string.Join(", ", g.Select(static r => r.File))}.")];
    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }

  [Fact]
  public void EveryRuleIsCoveredByACase() {
    HashSet<string> covered = [.. ReadCovers().Values.Where(static v => v is not null).SelectMany(static v => v!)];
    var problems = new List<string>();
    foreach (Rule rule in AllRules()) {
      if (rule.Line.Contains("[no-case:", StringComparison.Ordinal)) {
        continue;
      }

      if (!covered.Contains(rule.Id)) {
        problems.Add(
            $"{rule.File}: rule {rule.Id} is not named in any '# covers:' line under conformance/ (add a case or a [no-case: <reason>] marker).");
      }
    }

    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }

  [Fact]
  public void EveryCoveredIdExistsInTheSpec() {
    HashSet<string> known = [.. AllRules().Select(static r => r.Id)];
    var problems = new List<string>();
    foreach ((string casePath, List<string>? ids) in ReadCovers()) {
      string file = $"conformance/{casePath}";
      if (ids is null) {
        problems.Add($"{file}: first line must be '# covers: <RULE-ID>, ...'.");
        continue;
      }

      foreach (string id in ids) {
        if (!known.Contains(id)) {
          problems.Add($"{file}: '# covers:' names {id}, which does not exist in spec/features or spec/stdlib.");
        }
      }
    }

    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }

  [Fact]
  public void EveryCaseHasExactlyOneExpectationFile() {
    var problems = new List<string>();
    foreach (string casePath in ConformanceRunner.DiscoverCases()) {
      int count = ConformanceRunner.ExpectationFileCount(casePath);
      if (count != 1) {
        problems.Add($"conformance/{casePath}: expected exactly one .out or .err sibling file, found {count}.");
      }
    }

    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }

  [Fact]
  public void EveryDecisionHasAValidStatus() {
    string dir = Path.Combine(SpecDir, "decisions");
    var problems = new List<string>();
    foreach (string file in Directory.EnumerateFiles(dir, "D-*.md").OrderBy(static f => f, StringComparer.Ordinal)) {
      string? statusLine = File.ReadAllLines(file).FirstOrDefault(static l => l.StartsWith("Status:", StringComparison.Ordinal));
      if (statusLine is null) {
        problems.Add($"{Rel(file)}: missing 'Status:' line.");
        continue;
      }

      string value = statusLine["Status:".Length..].Trim();
      if (value != "open" && value != "accepted" && value != "rejected") {
        problems.Add($"{Rel(file)}: Status '{value}' must be open, accepted or rejected.");
      }
    }

    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }
}
