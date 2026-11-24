using System.Text.RegularExpressions;

using Pino.Cli;

namespace Pino.Compiler.Tests.Cli;

/// <summary>
/// End-to-end tests for <c>pino test</c> on both VM and C engines (ticket 080, D-188, TEST-1..14).
/// </summary>
public sealed class CliTestCommandTests : IDisposable {
  private static readonly Regex s_duration = new(@"\(\d+\.\d{2} s\)", RegexOptions.Compiled);

  private readonly string _root = Path.Combine(Path.GetTempPath(), "pino-test-cmd-" + Guid.NewGuid().ToString("N"));

  public CliTestCommandTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try {
      Directory.Delete(_root, recursive: true);
    } catch (IOException) {
    } catch (UnauthorizedAccessException) {
    }
  }

  [Theory]
  [InlineData("--vm")]
  [InlineData("--c")]
  public void InitDemo_RunsTestSuiteAndIgnoresTestsDuringRun(string engineFlag) {
    CliResult init = Run(["init", "demo"]);
    Assert.Equal(0, init.ExitCode);

    string demoDir = Path.Combine(_root, "demo");
    CliResult testRun = Run(["test", engineFlag], workingDirectory: demoDir);
    Assert.Equal(0, testRun.ExitCode);
    Assert.Equal(string.Empty, testRun.Stderr);
    Assert.Equal(
        "test/main_test.pino\n  [PASS] should add numbers correctly\n\n1 passed (<time>)\n",
        NormalizeTime(testRun.Stdout));

    CliResult progRun = Run(["run", "main.pino"], workingDirectory: demoDir);
    Assert.Equal(0, progRun.ExitCode);
    Assert.Equal("Welcome to Pino! Result: 42\n", progRun.Stdout);
  }

  [Theory]
  [InlineData("--vm")]
  [InlineData("--c")]
  public void Discovery_FindsAllTestFilesInOrdinalOrder_AndSkipsIgnoredDirectories(string engineFlag) {
    WriteFile("alpha_test.pino", """
        test "alpha one" {
          assert(1 + 1 == 2)
        }
        """);
    WriteFile(Path.Combine("test", "nested", "beta.pino"), """
        test "beta one" {
          assert(true)
        }
        """);
    WriteFile("inline.pino", """
        fn double(n int) int {
          return n * 2
        }

        test "inline double" {
          assert(double(4) == 8)
        }
        """);
    WriteFile("plain.pino", """
        val x = 1
        println(x)
        """);
    // Skipped directories must never be discovered.
    WriteFile(Path.Combine("bin", "bad_test.pino"), "test \"ignored bin\" { panic(\"no\") }\n");
    WriteFile(Path.Combine("obj", "bad_test.pino"), "test \"ignored obj\" { panic(\"no\") }\n");
    WriteFile(Path.Combine("tooling", "bad_test.pino"), "test \"ignored tooling\" { panic(\"no\") }\n");
    WriteFile(Path.Combine(".git", "bad_test.pino"), "test \"ignored git\" { panic(\"no\") }\n");

    CliResult res = Run(["test", engineFlag]);
    Assert.Equal(0, res.ExitCode);
    Assert.Equal(string.Empty, res.Stderr);
    Assert.Equal(
        "alpha_test.pino\n" +
        "  [PASS] alpha one\n\n" +
        "inline.pino\n" +
        "  [PASS] inline double\n\n" +
        "test/nested/beta.pino\n" +
        "  [PASS] beta one\n\n" +
        "3 passed (<time>)\n",
        NormalizeTime(res.Stdout));
  }

  [Theory]
  [InlineData("--vm")]
  [InlineData("--c")]
  public void TestableImportsAndModulePanicsWorkOnBothEngines(string engineFlag) {
    WriteFile(Path.Combine("modules", "secret.pino"), """
        module secret

        @@Testable fn hidden_add(a int, b int) int {
          return a + b
        }

        pub fn explode() {
          val n = 1
          panic("from secret module")
        }
        """);
    WriteFile(Path.Combine("test", "secret_test.pino"), """
        from secret import hidden_add, explode

        test "uses @@Testable helper" {
          assert(hidden_add(2, 3) == 5)
        }

        test "panics in imported module" {
          explode()
        }
        """);

    CliResult res = Run(["test", engineFlag]);
    Assert.Equal(1, res.ExitCode);
    Assert.Equal(
        "test/secret_test.pino\n" +
        "  [PASS] uses @@Testable helper\n" +
        "  [FAIL] panics in imported module  (modules/secret.pino:9)\n" +
        "         panicked: from secret module\n\n" +
        "1 passed, 1 failed (<time>)\n",
        NormalizeTime(res.Stdout));
  }

  [Fact]
  public void CompileErrorInOneFileStillRunsOtherFilesAndExitsOne() {
    WriteFile("a_broken_test.pino", """
        test "broken" {
          val n int = "nope"
        }
        """);
    WriteFile("b_good_test.pino", """
        test "good" {
          assert(2 + 2 == 4)
        }
        """);

    CliResult vmRes = Run(["test", "--vm"]);
    CliResult cRes = Run(["test", "--c"]);

    Assert.Equal(1, vmRes.ExitCode);
    Assert.Equal(1, cRes.ExitCode);
    Assert.Equal(NormalizeTime(vmRes.Stdout), NormalizeTime(cRes.Stdout));
    Assert.Contains("a_broken_test.pino\nerror[PN3102]", vmRes.Stdout, StringComparison.Ordinal);
    Assert.Contains("b_good_test.pino\n  [PASS] good\n", vmRes.Stdout, StringComparison.Ordinal);
    Assert.Contains("1 passed, 1 failed (", vmRes.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void FilterMatchingNothingPrintsNoTestsFoundAndExitsZero() {
    WriteFile("math_test.pino", """
        test "adds numbers" {
          assert(1 + 1 == 2)
        }
        """);

    CliResult res = Run(["test", "--filter", "nonexistent"]);
    Assert.Equal(0, res.ExitCode);
    Assert.Equal("No tests found.\n", res.Stdout);
  }

  [Fact]
  public void ColorAlwaysEmitsAnsiAndNeverDoesNot() {
    WriteFile("color_test.pino", """
        test "passes" {
          assert(true)
        }

        test "fails" {
          val x = 1
          assert(x == 2)
        }
        """);

    CliResult colored = Run(["test", "--color", "always"]);
    CliResult plain = Run(["test", "--color", "never"]);

    Assert.Equal(1, colored.ExitCode);
    Assert.Equal(1, plain.ExitCode);
    Assert.Contains("\x1b[1;32m[PASS]\x1b[0m", colored.Stdout, StringComparison.Ordinal);
    Assert.Contains("\x1b[1;31m[FAIL]\x1b[0m", colored.Stdout, StringComparison.Ordinal);
    Assert.DoesNotContain("\x1b[", plain.Stdout, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("--vm", "--c")]
  [InlineData("--bogus")]
  [InlineData("--filter")]
  [InlineData("does_not_exist.pino")]
  [InlineData("a.pino", "b.pino")]
  public void BadUsageExitsTwo(params string[] extraArgs) {
    string[] args = ["test", .. extraArgs];
    CliResult res = Run(args);

    Assert.Equal(2, res.ExitCode);
    Assert.Equal(string.Empty, res.Stdout);
    Assert.StartsWith("pino: ", res.Stderr, StringComparison.Ordinal);
  }

  private static string NormalizeTime(string text) =>
      s_duration.Replace(text.Replace("\r\n", "\n", StringComparison.Ordinal), "(<time>)");

  private string WriteFile(string relativePath, string content) {
    string path = Path.Combine(_root, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
    return path;
  }

  private CliResult Run(string[] args, string? workingDirectory = null) {
    var stdout = new StringWriter { NewLine = "\n" };
    var stderr = new StringWriter { NewLine = "\n" };
    var env = new CliEnvironment(
        _ => null,
        IsOutputRedirected: true,
        SupportsAnsi: false,
        Notes: new Dictionary<string, string>(),
        WorkingDirectory: workingDirectory ?? _root);
    int exit = CliApp.Run(args, TextReader.Null, stdout, stderr, env);
    return new CliResult(exit, stdout.ToString(), stderr.ToString());
  }

  private sealed record CliResult(int ExitCode, string Stdout, string Stderr);
}
