using Pino.Vm;

namespace Pino.Compiler.Tests.Vm;

/// <summary>Sandboxed browser/WASM playground runner (<see cref="PlaygroundRunner"/>) tests.</summary>
public sealed class PlaygroundRunnerTests {
  [Fact]
  public void Run_ValidAd003ProgramReturnsStdout() {
    const string Code = """
        struct Counter {
          mut n int

          mut fn bump(delta int) {
            self:n += delta
          }
        }

        var c = Counter { n: 10 }
        c:bump(5)
        var p = Pool[Counter]
        val h = p:add(c)
        println(p[h])
        """;

    PlaygroundResult result = PlaygroundRunner.Run(Code);

    Assert.True(result.Succeeded);
    Assert.Equal(0, result.ExitCode);
    Assert.Equal("Counter { n: 15 }\n", result.StdOut);
    Assert.Equal(string.Empty, result.StdErr);
    Assert.Equal("Counter { n: 15 }\n", PlaygroundRunner.Evaluate(Code));
  }

  [Fact]
  public void Run_InfiniteLoopStopsCleanlyWithStepLimitMessage() {
    PlaygroundResult result = PlaygroundRunner.Run("for { }\n", maxSteps: 1000);

    Assert.False(result.Succeeded);
    Assert.Equal(1, result.ExitCode);
    Assert.Equal(PlaygroundRunner.StepLimitMessage + "\n", result.StdErr);
    Assert.Equal(PlaygroundRunner.StepLimitMessage + "\n", PlaygroundRunner.Evaluate("for { }\n", maxSteps: 1000));
  }

  [Fact]
  public void Run_ReadFileAndWriteFileReturnPermissionDenied() {
    const string Code = """
        println(read_file("x"))
        println(write_file("x", "hello"))
        println(file_exists("x"))
        """;

    PlaygroundResult result = PlaygroundRunner.Run(Code);

    Assert.True(result.Succeeded);
    Assert.Equal(0, result.ExitCode);
    string expectedFailure =
        $"Result::Failure(IOError::PermissionDenied(\"{PlaygroundRunner.FileAccessDeniedMessage}\"))";
    Assert.Equal($"{expectedFailure}\n{expectedFailure}\nfalse\n", result.StdOut);
    Assert.Equal(string.Empty, result.StdErr);
  }

  [Fact]
  public void Run_CompileErrorsReturnPlainRenderedDiagnosticsWithoutAnsi() {
    const string Code = "val x int = \"oops\"\n";

    PlaygroundResult result = PlaygroundRunner.Run(Code);

    Assert.False(result.Succeeded);
    Assert.Equal(1, result.ExitCode);
    Assert.Equal(string.Empty, result.StdOut);
    Assert.Contains("error[PN3102]", result.StdErr, StringComparison.Ordinal);
    Assert.DoesNotContain("\u001b[", result.StdErr, StringComparison.Ordinal);
    Assert.Equal(result.StdErr, PlaygroundRunner.Evaluate(Code));
  }

  [Fact]
  public void Run_PanicFormatsMessageAndReturnsExitCode101() {
    const string Code = "println(\"before\")\npanic(\"playground crash\")\n";

    PlaygroundResult result = PlaygroundRunner.Run(Code);

    Assert.False(result.Succeeded);
    Assert.Equal(101, result.ExitCode);
    Assert.Equal("before\n", result.StdOut);
    Assert.Contains("thread 'main' panicked at 'playground crash'", result.StdErr, StringComparison.Ordinal);
    Assert.Equal(result.StdErr, PlaygroundRunner.Evaluate(Code));
  }

  [Fact]
  public void Run_ProgramWithOnlyHintStillRuns() {
    const string Code = "val x = 7;\nprintln(x)\n";

    PlaygroundResult result = PlaygroundRunner.Run(Code);

    Assert.True(result.Succeeded);
    Assert.Equal(0, result.ExitCode);
    Assert.Equal("7\n", result.StdOut);
    Assert.Contains("hint[PN1018]", result.StdErr, StringComparison.Ordinal);
    Assert.Equal("7\n", PlaygroundRunner.Evaluate(Code));
  }
}
