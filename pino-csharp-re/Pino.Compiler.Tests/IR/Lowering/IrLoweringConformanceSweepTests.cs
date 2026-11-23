using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.Parsing;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.IR.Lowering;

/// <summary>
/// Sweeps 100% of positive (<c>.out</c>) conformance cases, all runtime-error (<c>.err</c>)
/// cases, and <c>main.pino</c>, verifying that <see cref="IrLowerer.Lower"/> succeeds
/// and <see cref="IrVerifier.Verify"/> reports 0 errors on every case with no skip list.
/// </summary>
public sealed class IrLoweringConformanceSweepTests {
  private static readonly string s_repoRoot = ParserTestHelpers.FindRepositoryRoot();
  private static readonly string s_conformanceRoot = Path.Combine(s_repoRoot, "conformance");

  public static TheoryData<string> PositiveConformanceCases() {
    var data = new TheoryData<string>();
    foreach (string file in Directory.EnumerateFiles(s_conformanceRoot, "*.pino", SearchOption.AllDirectories)
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      if (File.Exists(Path.ChangeExtension(file, ".out"))) {
        data.Add(ToConformanceRelativePath(file));
      }
    }
    return data;
  }

  public static TheoryData<string> RuntimeErrConformanceCases() {
    var data = new TheoryData<string>();
    foreach (string rel in Checking.CheckerConformanceTests.RuntimeErrCases.Keys
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      data.Add(rel);
    }
    return data;
  }

  [Theory]
  [MemberData(nameof(PositiveConformanceCases))]
  public void PositiveConformanceCase_LowersAndVerifiesCleanly(string relativePath) {
    Compilation compilation = CompileConformanceFile(relativePath);
    IrModule module = IrLowerer.Lower(compilation);
    IrVerificationResult verification = IrVerifier.Verify(module);
    Assert.True(
        verification.IsValid,
        $"{relativePath} failed IrVerifier with {verification.Errors.Length} error(s):\n"
        + string.Join("\n", verification.Errors)
        + "\n\n--- Printed IR ---\n"
        + IrPrinter.Print(module));
  }

  [Theory]
  [MemberData(nameof(RuntimeErrConformanceCases))]
  public void RuntimeErrConformanceCase_LowersAndVerifiesCleanly(string relativePath) {
    Compilation compilation = CompileConformanceFile(relativePath);
    IrModule module = IrLowerer.Lower(compilation);
    IrVerificationResult verification = IrVerifier.Verify(module);
    Assert.True(
        verification.IsValid,
        $"{relativePath} failed IrVerifier with {verification.Errors.Length} error(s):\n"
        + string.Join("\n", verification.Errors));
  }

  [Fact]
  public void ConformanceSweep_Covers100PercentOfPositiveCases() {
    int total = 0;
    int lowered = 0;
    var failures = new List<string>();

    foreach (string file in Directory.EnumerateFiles(s_conformanceRoot, "*.pino", SearchOption.AllDirectories)
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      if (!File.Exists(Path.ChangeExtension(file, ".out"))) {
        continue;
      }

      total++;
      string rel = ToConformanceRelativePath(file);
      Compilation compilation = CompileConformanceFile(rel);

      try {
        IrModule module = IrLowerer.Lower(compilation);
        IrVerificationResult verification = IrVerifier.Verify(module);
        if (!verification.IsValid) {
          failures.Add($"{rel}: IrVerifier errors: {string.Join("; ", verification.Errors)}");
        } else {
          lowered++;
        }
      } catch (Exception ex) {
        failures.Add($"{rel}: {ex.GetType().Name}: {ex.Message}");
      }
    }

    Assert.True(
        failures.Count == 0,
        $"Conformance sweep had {failures.Count} failure(s) (total={total}, lowered={lowered}):\n"
        + string.Join("\n", failures.Take(60)));
    Assert.True(total > 0, "Expected at least one positive conformance case.");
    Assert.Equal(total, lowered);
  }

  private const string DesignSection33MainPino = """
    union Constant {
      Bool(b bool)
      Float(f float)
      Int(n int)
      String(str string)
    }

    @@Generic[T]
    struct Person {
      pub name string
      pub data T
    }

    val n int = 12
    val f float = 12.5
    val p Person[string] = Person {
      name: "Shawn"
      data: "China"
    }
    var nullish Option[string] = Option::None

    fn call_lambda(fun fn) {
      fun()
    }

    fn call_lambda_2(fun fn (int)) {
      fun(1)
    }

    fn call_lambda_3(fun fn (int, int) -> int) {
      return fun(1, 2)
    }

    call_lambda_3(fn (n, m) -> n + m)

    var int_map = map[int, string]

    int_map = map[int, string] {
      1 -> "Shawn"
      2 -> "Lee"
    }

    var int_set = set[int]

    int_set = set[int] { 1, 2, 3 }
    """;

  [Fact]
  public void MainPino_LowersAndVerifiesCleanly() {
    string? envPath = Environment.GetEnvironmentVariable("PINO_MAIN_PINO");
    string mainPinoPath = !string.IsNullOrWhiteSpace(envPath)
        ? envPath
        : Path.Combine(s_repoRoot, "main.pino");
    Assert.True(File.Exists(mainPinoPath), $"main.pino not found at '{mainPinoPath}'.");

    string content = File.ReadAllText(mainPinoPath);

    // Match CheckerMainPinoTests: fresh worktrees hold the legacy pre-AD-003 committed `main.pino`
    // (which has 2 intentional PN3222 checker errors), while PINO_MAIN_PINO or the main checkout
    // holds the owner's §3.3 main.pino. Lower & verify §3.3 main.pino when falling back on the
    // legacy snapshot, and always lower & verify the exact target file when PINO_MAIN_PINO is set.
    if (string.IsNullOrWhiteSpace(envPath)
        && content.Contains("struct Context", StringComparison.Ordinal)
        && content.Contains("fn add_err(", StringComparison.Ordinal)) {
      content = DesignSection33MainPino;
    }

    var parsed = Parser.Parse(new SourceText(content, mainPinoPath));
    Assert.Empty(parsed.Diagnostics.Where(static d => d.Severity != DiagnosticSeverity.Hint));
    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

    IrModule module = IrLowerer.Lower(compilation);
    IrVerificationResult verification = IrVerifier.Verify(module);
    Assert.True(
        verification.IsValid,
        $"main.pino ({mainPinoPath}) failed IrVerifier with {verification.Errors.Length} error(s):\n"
        + string.Join("\n", verification.Errors));
  }

  private static Compilation CompileConformanceFile(string relativePath) {
    string fullPath = Path.Combine(s_conformanceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    string content = File.ReadAllText(fullPath);
    var parsed = Parser.Parse(new SourceText(content, fullPath));
    Assert.Empty(parsed.Diagnostics.Where(static d => d.Severity != DiagnosticSeverity.Hint));
    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
    return compilation;
  }

  private static string ToConformanceRelativePath(string fullPath) =>
      Path.GetRelativePath(s_conformanceRoot, fullPath).Replace('\\', '/');
}
