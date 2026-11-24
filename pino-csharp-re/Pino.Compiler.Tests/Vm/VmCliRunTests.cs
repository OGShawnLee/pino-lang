using Pino.Cli;

namespace Pino.Compiler.Tests.Vm;

/// <summary><c>pino-re run [--vm] [--verify-rc] &lt;file&gt;</c>: exit codes 0, 1 and 101 (ERR-5).</summary>
public sealed class VmCliRunTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "pino-re-run-" + Guid.NewGuid().ToString("N"));

  public VmCliRunTests() {
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
  public void Run_ExecutesAProgramOnTheVmAndExitsZero() {
    string file = WriteFile("hello.pino", "println(\"hello\", 1 + 2)\n");

    (int exit, string stdout, string stderr) = Run(["run", file]);

    Assert.Equal(0, exit);
    Assert.Equal("hello 3\n", stdout);
    Assert.Equal(string.Empty, stderr);
  }

  [Fact]
  public void Run_WithVmAndVerifyRcFlags_AcceptsThemInAnyOrder() {
    string file = WriteFile("flags.pino", "val xs = [1, 2]\nprintln(xs)\n");

    (int exit, string stdout, string stderr) = Run(["run", "--vm", "--verify-rc", file]);
    (int exit2, string stdout2, _) = Run(["run", file, "--verify-rc", "--vm"]);

    Assert.Equal(0, exit);
    Assert.Equal("[1, 2]\n", stdout);
    Assert.Equal(string.Empty, stderr);
    Assert.Equal(0, exit2);
    Assert.Equal("[1, 2]\n", stdout2);
  }

  [Fact]
  public void Run_ExitsOneOnACompileError_WithoutRunningAnything() {
    string file = WriteFile("bad.pino", "println(\"never\")\nval x int = \"oops\"\n");

    (int exit, string stdout, string stderr) = Run(["run", file, "--color", "never"]);

    Assert.Equal(1, exit);
    Assert.Equal(string.Empty, stdout);
    Assert.Contains("error[PN3102]", stderr, StringComparison.Ordinal);
  }

  [Fact]
  public void Run_ExitsOneWhenMainFails() {
    string file = WriteFile("fail.pino", "fn main() Result[int, string] {\n  println(\"start\")\n  return Result::Failure(\"boom\")\n}\n");

    (int exit, string stdout, string stderr) = Run(["run", file]);

    Assert.Equal(1, exit);
    Assert.Equal("start\n", stdout);
    Assert.Equal("Error: boom\n", stderr);
  }

  [Fact]
  public void Run_ExitsOneHundredOneOnPanic() {
    string file = WriteFile("panic.pino", "println(\"before\")\npanic(\"crash message\")\n");

    (int exit, string stdout, string stderr) = Run(["run", file]);

    Assert.Equal(101, exit);
    Assert.Equal("before\n", stdout);
    Assert.StartsWith("thread 'main' panicked at 'crash message'\n  at ", stderr, StringComparison.Ordinal);
  }

  [Fact]
  public void Run_StillRunsAProgramWhoseOnlyDiagnosticIsAHint_AndPrintsTheHint() {
    string file = WriteFile("semi.pino", "val x = 1;\nprintln(x)\n");

    (int exit, string stdout, string stderr) = Run(["run", file, "--color", "never"]);

    Assert.Equal(0, exit);
    Assert.Equal("1\n", stdout);
    Assert.Contains("hint[PN1018]", stderr, StringComparison.Ordinal);
  }

  [Fact]
  public void Run_VmAndCEngines_ProduceTheSameStdoutAndExitCode() {
    const string Source = """
        struct Point {
          x int
          y int
        }

        fn sum(xs []int) int {
          var total = 0
          for x in xs {
            total += x
          }
          return total
        }

        val p = Point { x: 3, y: 4 }
        println("sum", sum([1, 2, 3, 4]), p, 7 // 2, 7.0 / 2)
        println(["a", "b"], map[string, int] { "k" -> 1 })
        """;
    string file = WriteFile("both.pino", Source);

    (int vmExit, string vmOut, string vmErr) = Run(["run", "--vm", file]);
    (int cExit, string cOutRaw, string cErr) = Run(["run", "--c", file]);
    string cOut = cOutRaw.Replace("\r\n", "\n", StringComparison.Ordinal); // the native Windows console writes CRLF
    (int defaultExit, string defaultOut, _) = Run(["run", file]);

    Assert.Equal(0, vmExit);
    Assert.Equal(string.Empty, vmErr);
    Assert.Equal(string.Empty, cErr);
    Assert.Equal("sum 10 Point { x: 3, y: 4 } 3 3.5\n[\"a\", \"b\"] map[string, int] { \"k\" -> 1 }\n", vmOut);
    Assert.Equal(vmExit, cExit);
    Assert.Equal(vmOut, cOut);
    Assert.Equal(vmExit, defaultExit);
    Assert.Equal(vmOut, defaultOut);
  }

  [Fact]
  public void Run_VmAndCEngines_AgreeOnAPanicExitCode() {
    string file = WriteFile("panic_both.pino", "println(\"before\")\npanic(\"crash message\")\n");

    (int vmExit, string vmOut, string vmErr) = Run(["run", "--vm", file]);
    (int cExit, string cOutRaw, string cErr) = Run(["run", "--c", file]);
    string cOut = cOutRaw.Replace("\r\n", "\n", StringComparison.Ordinal);

    Assert.Equal(101, vmExit);
    Assert.Equal(vmExit, cExit);
    Assert.Equal(vmOut, cOut);
    Assert.Contains("crash message", vmErr, StringComparison.Ordinal);
    Assert.Contains("crash message", cErr, StringComparison.Ordinal);
  }

  [Fact]
  public void Run_WithoutAFileIsABadUsage() {
    (int exit, _, string stderr) = Run(["run"]);

    Assert.Equal(2, exit);
    Assert.Contains("missing path (usage: pino-re run", stderr, StringComparison.Ordinal);
  }

  [Fact]
  public void Run_VerifyRcReportsNothingForACleanProgram() {
    string file = WriteFile("clean.pino", "var m = map[string, []int]\nm[\"a\"] = [1]\nprintln(m)\n");

    (int exit, string stdout, string stderr) = Run(["run", "--verify-rc", file]);

    Assert.Equal(0, exit);
    Assert.Equal("map[string, []int] { \"a\" -> [1] }\n", stdout);
    Assert.Equal(string.Empty, stderr);
  }

  private string WriteFile(string name, string content) {
    string path = Path.Combine(_root, name);
    File.WriteAllText(path, content);
    return path;
  }

  private static (int ExitCode, string Stdout, string Stderr) Run(string[] args) {
    var stdout = new StringWriter { NewLine = "\n" };
    var stderr = new StringWriter { NewLine = "\n" };
    var environment = new CliEnvironment(_ => null, IsOutputRedirected: true, SupportsAnsi: false, new Dictionary<string, string>());
    int exit = CliApp.Run(args, stdout, stderr, environment);
    return (exit, stdout.ToString(), stderr.ToString());
  }
}
