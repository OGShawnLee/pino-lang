using System.Text.RegularExpressions;

namespace pino_csharp.tests.Conformance;

public class SpecLintTests {
  private static readonly Regex RuleLine = new(@"^- ([A-Z]+-E?\d+): ", RegexOptions.Compiled);
  private static readonly Regex CoversLine = new(@"^#\s*covers:\s*(.*)$", RegexOptions.Compiled);
  private static readonly string[] FeatureHeadings = { "## Syntax", "## Rules", "## Errors", "## Examples", "## Open questions" };

  private sealed record Rule(string Id, string File, string Line);

  private static string SpecDir => Path.Combine(ConformanceRunner.RepoRoot, "spec");

  private static string Rel(string fullPath) =>
    Path.GetRelativePath(ConformanceRunner.RepoRoot, fullPath).Replace('\\', '/');

  private static IEnumerable<string> SpecRuleFiles() {
    foreach (var dir in new[] { "features", "stdlib" }) {
      var path = Path.Combine(SpecDir, dir);
      if (!Directory.Exists(path)) continue;
      foreach (var file in Directory.EnumerateFiles(path, "*.md").OrderBy(f => f, StringComparer.Ordinal)) {
        yield return file;
      }
    }
  }

  /// <summary>Rule lines are only read inside the "## Rules" and "## Errors" sections.</summary>
  private static List<Rule> ReadRules(string file) {
    var rules = new List<Rule>();
    bool inRuleSection = false;
    foreach (var line in File.ReadAllLines(file)) {
      if (line.StartsWith("## ")) {
        var heading = line.Trim();
        inRuleSection = heading == "## Rules" || heading == "## Errors";
        continue;
      }
      if (!inRuleSection) continue;
      var m = RuleLine.Match(line);
      if (m.Success) rules.Add(new Rule(m.Groups[1].Value, Rel(file), line));
    }
    return rules;
  }

  private static List<Rule> AllRules() => SpecRuleFiles().SelectMany(ReadRules).ToList();

  /// <summary>Case path -> IDs named by its "# covers:" first line (null when the header is missing).</summary>
  private static Dictionary<string, List<string>?> ReadCovers() {
    var result = new Dictionary<string, List<string>?>();
    foreach (var casePath in ConformanceRunner.DiscoverCases()) {
      var firstLine = File.ReadLines(ConformanceRunner.CaseFullPath(casePath)).FirstOrDefault() ?? "";
      var m = CoversLine.Match(firstLine.Trim());
      result[casePath] = m.Success
        ? m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
        : null;
    }
    return result;
  }

  /// <summary>Spec files whose SPEC.md row says "migrated": (relative path under spec/, is stdlib).</summary>
  private static List<(string Path, bool IsStdlib)> MigratedFiles() {
    var specMd = Path.Combine(SpecDir, "SPEC.md");
    var result = new List<(string, bool)>();
    foreach (var raw in File.ReadAllLines(specMd)) {
      var line = raw.Trim();
      if (!line.StartsWith("|")) continue;
      var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToList();
      if (cells.Count < 4 || cells[^1] != "migrated") continue;
      var fileCell = cells.FirstOrDefault(c => Regex.IsMatch(c, @"^`(features|stdlib)/[^`]+\.md`$"));
      if (fileCell == null) continue;
      var path = fileCell.Trim('`');
      result.Add((path, path.StartsWith("stdlib/")));
    }
    return result;
  }

  [Fact]
  public void MigratedFilesExistAndHaveRequiredHeadings() {
    var migrated = MigratedFiles();
    Assert.True(migrated.Count > 0, "spec/SPEC.md lists no migrated feature or stdlib file.");
    var problems = new List<string>();
    foreach (var (path, isStdlib) in migrated) {
      var full = Path.Combine(SpecDir, path);
      if (!File.Exists(full)) {
        problems.Add($"spec/{path}: listed as migrated in spec/SPEC.md but the file does not exist.");
        continue;
      }
      var headings = File.ReadAllLines(full).Select(l => l.Trim()).ToHashSet();
      var required = isStdlib ? new[] { "## Rules" } : FeatureHeadings;
      foreach (var heading in required) {
        if (!headings.Contains(heading)) problems.Add($"spec/{path}: missing heading '{heading}'.");
      }
    }
    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }

  [Fact]
  public void RuleIdsAreUnique() {
    var problems = AllRules()
      .GroupBy(r => r.Id)
      .Where(g => g.Count() > 1)
      .Select(g => $"Rule ID {g.Key} is defined more than once: {string.Join(", ", g.Select(r => r.File))}.")
      .ToList();
    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }

  [Fact]
  public void EveryRuleIsCoveredByACase() {
    var covered = ReadCovers().Values.Where(v => v != null).SelectMany(v => v!).ToHashSet();
    var problems = new List<string>();
    foreach (var rule in AllRules()) {
      if (rule.Line.Contains("[no-case:")) continue;
      if (!covered.Contains(rule.Id)) {
        problems.Add($"{rule.File}: rule {rule.Id} is not named in any '# covers:' line under conformance/ (add a case or a [no-case: <reason>] marker).");
      }
    }
    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }

  [Fact]
  public void EveryCoveredIdExistsInTheSpec() {
    var known = AllRules().Select(r => r.Id).ToHashSet();
    var problems = new List<string>();
    foreach (var (casePath, ids) in ReadCovers()) {
      var file = $"conformance/{casePath}";
      if (ids == null) {
        problems.Add($"{file}: first line must be '# covers: <RULE-ID>, ...'.");
        continue;
      }
      foreach (var id in ids) {
        if (!known.Contains(id)) problems.Add($"{file}: '# covers:' names {id}, which does not exist in spec/features or spec/stdlib.");
      }
    }
    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }

  [Fact]
  public void EveryCaseHasExactlyOneExpectationFile() {
    var problems = new List<string>();
    foreach (var casePath in ConformanceRunner.DiscoverCases()) {
      int count = ConformanceRunner.ExpectationFileCount(casePath);
      if (count != 1) {
        problems.Add($"conformance/{casePath}: expected exactly one .out or .err sibling file, found {count}.");
      }
    }
    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }

  [Fact]
  public void EveryDecisionHasAValidStatus() {
    var dir = Path.Combine(SpecDir, "decisions");
    var problems = new List<string>();
    foreach (var file in Directory.EnumerateFiles(dir, "D-*.md").OrderBy(f => f, StringComparer.Ordinal)) {
      var statusLine = File.ReadAllLines(file).FirstOrDefault(l => l.StartsWith("Status:"));
      if (statusLine == null) {
        problems.Add($"{Rel(file)}: missing 'Status:' line.");
        continue;
      }
      var value = statusLine.Substring("Status:".Length).Trim();
      if (value != "open" && value != "accepted" && value != "rejected") {
        problems.Add($"{Rel(file)}: Status '{value}' must be open, accepted or rejected.");
      }
    }
    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }
}
