using Pino.Cli;
using Pino.Compiler.Diagnostics;
using Pino.Vm;

namespace Pino.Compiler.Tests.Vm;

/// <summary>Interactive REPL session (<see cref="VmSession"/>) and <c>pino-re repl</c> tests.</summary>
public sealed class VmSessionReplTests {
  private static readonly DiagnosticRenderOptions s_plainOptions = new() {
    Color = false,
    Voice = DiagnosticVoice.Plain,
  };

  [Fact]
  public void Session_PersistsVarMutationAndAutoPrintsExpression() {
    var host = new BufferedVmHost();
    var session = new VmSession(host, s_plainOptions);

    VmSessionResult r1 = session.Submit("var x = 10");
    VmSessionResult r2 = session.Submit("x += 5");
    VmSessionResult r3 = session.Submit("x");

    Assert.True(r1.Succeeded);
    Assert.True(r2.Succeeded);
    Assert.True(r3.Succeeded);
    Assert.True(r3.PrintedValue);
    Assert.Equal("15\n", host.StdOut);
    Assert.Equal(string.Empty, host.StdErr);
  }

  [Fact]
  public void Session_SupportsValMutMutationAcrossChunks() {
    var host = new BufferedVmHost();
    var session = new VmSession(host, s_plainOptions);

    Assert.True(session.Submit("val mut xs = [1, 2]").Succeeded);
    Assert.True(session.Submit("xs:push(3)").Succeeded);
    VmSessionResult len = session.Submit("xs:len");
    VmSessionResult list = session.Submit("xs");

    Assert.True(len.Succeeded);
    Assert.True(list.Succeeded);
    Assert.Equal("3\n[1, 2, 3]\n", host.StdOut);
    Assert.Equal(string.Empty, host.StdErr);
  }

  [Fact]
  public void Session_SupportsStructAndPoolAcrossLines() {
    var host = new BufferedVmHost();
    var session = new VmSession(host, s_plainOptions);

    Assert.True(session.Submit("struct Weapon {\n  name string\n  dmg int\n}").Succeeded);
    Assert.True(session.Submit("var armory = Pool[Weapon]").Succeeded);
    Assert.True(session.Submit("val h = armory:add(Weapon { name: \"Sword\", dmg: 12 })").Succeeded);
    VmSessionResult lookup = session.Submit("armory[h]");

    Assert.True(lookup.Succeeded);
    Assert.True(lookup.PrintedValue);
    Assert.Equal("Weapon { name: \"Sword\", dmg: 12 }\n", host.StdOut);
    Assert.Equal(string.Empty, host.StdErr);
  }

  [Fact]
  public void Session_InvalidSyntaxAndTypesPrintDiagnosticsAndPreserveState() {
    var host = new BufferedVmHost();
    var session = new VmSession(host, s_plainOptions);

    Assert.True(session.Submit("var x = 10").Succeeded);
    Assert.True(session.Submit("x += 5").Succeeded);

    VmSessionResult syntaxErr = session.Submit("val = ???");
    Assert.False(syntaxErr.Succeeded);
    Assert.Equal(1, syntaxErr.ExitCode);
    Assert.NotNull(syntaxErr.RenderedDiagnostics);
    Assert.Contains("error[", host.StdErr, StringComparison.Ordinal);

    VmSessionResult typeErr = session.Submit("val bad int = \"nope\"");
    Assert.False(typeErr.Succeeded);
    Assert.Equal(1, typeErr.ExitCode);
    Assert.Contains("error[PN3102]", host.StdErr, StringComparison.Ordinal);

    VmSessionResult after = session.Submit("x");
    Assert.True(after.Succeeded);
    Assert.Equal("15\n", host.StdOut);
  }

  [Fact]
  public void Session_PanicPrintsReportWithoutKillingSession() {
    var host = new BufferedVmHost();
    var session = new VmSession(host, s_plainOptions);

    Assert.True(session.Submit("var x = 10").Succeeded);
    Assert.True(session.Submit("x += 5").Succeeded);

    VmSessionResult panicked = session.Submit("panic(\"repl boom\")");
    Assert.False(panicked.Succeeded);
    Assert.Equal(101, panicked.ExitCode);
    Assert.Equal("repl boom", panicked.PanicMessage);
    Assert.Contains("thread 'main' panicked at 'repl boom'", host.StdErr, StringComparison.Ordinal);

    VmSessionResult after = session.Submit("x");
    Assert.True(after.Succeeded);
    Assert.Equal("15\n", host.StdOut);
  }

  [Fact]
  public void Session_ResetClearsAccumulatedState() {
    var host = new BufferedVmHost();
    var session = new VmSession(host, s_plainOptions);

    Assert.True(session.Submit("var x = 15").Succeeded);
    Assert.True(session.Submit("x").Succeeded);
    Assert.Equal("15\n", host.StdOut);

    session.Reset();
    Assert.Equal(0, session.ChunkCount);

    VmSessionResult missing = session.Submit("x");
    Assert.False(missing.Succeeded);
    Assert.Contains("error[", host.StdErr, StringComparison.Ordinal);
  }

  [Fact]
  public void Session_LineWithOnlyHintStillRuns() {
    var host = new BufferedVmHost();
    var session = new VmSession(host, s_plainOptions);

    VmSessionResult r = session.Submit("val a = 42;");

    Assert.True(r.Succeeded);
    Assert.Contains("hint[PN1018]", host.StdErr, StringComparison.Ordinal);
    Assert.True(session.Submit("a").Succeeded);
    Assert.Equal("42\n", host.StdOut);
  }

  [Fact]
  public void Session_DoesNotReplayPriorPrintlnOutput() {
    var host = new BufferedVmHost();
    using var session = new VmSession(host, s_plainOptions);

    Assert.True(session.Submit("println(\"once\")").Succeeded);
    Assert.True(session.Submit("val a = 1").Succeeded);
    Assert.True(session.Submit("val b = 2").Succeeded);
    Assert.True(session.Submit("a + b").Succeeded);
    Assert.True(session.Submit("val c = a + b").Succeeded);
    Assert.True(session.Submit("c * 2").Succeeded);

    Assert.Equal("once\n3\n6\n", host.StdOut);
  }

  [Fact]
  public void Session_ValTimeDoesNotReEvaluateOnLaterReads() {
    var host = new TickingClockHost();
    using var session = new VmSession(host, s_plainOptions);

    Assert.True(session.Submit("val t = time()").Succeeded);
    Assert.True(session.Submit("t").Succeeded);
    Assert.True(session.Submit("t").Succeeded);

    Assert.Equal(1, host.ClockCalls);
    Assert.Equal("1700000000001.0\n1700000000001.0\n", host.StdOut);
  }

  [Fact]
  public void Session_ReadlineInEarlierChunkIsNotReadAgain() {
    var host = new BufferedVmHost(input: "first-line\n");
    using var session = new VmSession(host, s_plainOptions);

    Assert.True(session.Submit("val line = readline(\"\")").Succeeded);
    Assert.True(session.Submit("line").Succeeded);
    Assert.True(session.Submit("line").Succeeded);

    Assert.Equal("Result::Success(\"first-line\")\nResult::Success(\"first-line\")\n", host.StdOut);
    Assert.Equal(string.Empty, host.StdErr);
  }

  [Fact]
  public void Session_CounterLoopRunsOnceAndLaterReadsDoNotRerunIt() {
    var host = new BufferedVmHost();
    using var session = new VmSession(host, s_plainOptions);

    Assert.True(session.Submit("var n = 0").Succeeded);
    VmSessionResult loopResult = session.Submit("for 1000000 {\n  n += 1\n}");
    VmSessionResult read1 = session.Submit("n");
    VmSessionResult read2 = session.Submit("n");

    Assert.True(loopResult.Succeeded);
    Assert.True(loopResult.Steps >= 1_000_000);
    Assert.True(read1.Succeeded);
    Assert.Equal(0, read1.Steps);
    Assert.True(read2.Succeeded);
    Assert.Equal(0, read2.Steps);
    Assert.Equal("1000000\n1000000\n", host.StdOut);
  }

  [Fact]
  public void Session_VerifyRcReportsZeroLeaksAfterShutdownAndCliQuit() {
    var host = new BufferedVmHost();
    var session = new VmSession(host, s_plainOptions, verifyRc: true);

    Assert.True(session.Submit("struct Box { name string }").Succeeded);
    Assert.True(session.Submit("var p = Pool[Box]").Succeeded);
    Assert.True(session.Submit("val h = p:add(Box { name: \"gold\" })").Succeeded);
    Assert.True(session.Submit("val mut xs = [\"a\", \"b\"]").Succeeded);
    Assert.True(session.Submit("xs:push(\"c\")").Succeeded);
    Assert.True(session.Submit("p[h]").Succeeded);

    PinoVmResult shutdown = session.Shutdown();
    Assert.Equal(0, shutdown.ExitCode);
    Assert.Null(shutdown.RcError);
    Assert.NotNull(shutdown.LeakReport);
    Assert.True(shutdown.LeakReport!.IsClean, shutdown.LeakReport.Format());

    var stdin = new StringReader("val mut v = [\"hello\", \"world\"]\nv:push(\"!\")\nv\n:quit\n");
    var stdout = new StringWriter { NewLine = "\n" };
    var stderr = new StringWriter { NewLine = "\n" };
    var env = new CliEnvironment(_ => null, IsOutputRedirected: true, SupportsAnsi: false, new Dictionary<string, string>());

    int exit = CliApp.Run(["repl", "--verify-rc", "--color", "never"], stdin, stdout, stderr, env);
    Assert.Equal(0, exit);
    Assert.Equal("[\"hello\", \"world\", \"!\"]\n", stdout.ToString());
    Assert.Equal(string.Empty, stderr.ToString());
  }

  private sealed class TickingClockHost : IVmHost {
    private readonly System.Text.StringBuilder _out = new();
    private readonly System.Text.StringBuilder _err = new();
    private double _clock = 1_700_000_000_000.0;

    public TickingClockHost() {
      Out = new StringWriter(_out) { NewLine = "\n" };
      Err = new StringWriter(_err) { NewLine = "\n" };
    }

    public int ClockCalls { get; private set; }

    public TextWriter Out { get; }

    public TextWriter Err { get; }

    public TextReader In => TextReader.Null;

    public IVmFileSystem Fs => NullVmFileSystem.Instance;

    public Random Random { get; } = new(12345);

    public string StdOut => _out.ToString();

    public string StdErr => _err.ToString();

    public double ClockMs() {
      ClockCalls++;
      _clock += 1.0;
      return _clock;
    }

    public void Sleep(int milliseconds) { }

    public void Clear() { }
  }

  [Fact]
  public void NeedsMoreInput_DetectsUnclosedDelimitersIgnoringStringsAndComments() {
    Assert.True(VmSession.NeedsMoreInput("fn add(a int, b int) int {"));
    Assert.True(VmSession.NeedsMoreInput("val xs = [1, 2"));
    Assert.True(VmSession.NeedsMoreInput("println(\"hi\""));
    Assert.False(VmSession.NeedsMoreInput("fn add(a int, b int) int {\n  return a + b\n}"));
    Assert.False(VmSession.NeedsMoreInput("val s = \"{\" // unclosed in string and comment {"));
    Assert.False(VmSession.NeedsMoreInput("val r = '{'"));
  }

  [Fact]
  public void CliRepl_SupportsMultiLineInputAndMetaCommands() {
    const string Input = """
        :help
        var x = 10
        x += 5
        fn add(a int, b int) int {
          return a + b
        }
        add(x, 7)
        :reset
        x
        :quit
        """;

    var stdin = new StringReader(Input);
    var stdout = new StringWriter { NewLine = "\n" };
    var stderr = new StringWriter { NewLine = "\n" };
    var env = new CliEnvironment(_ => null, IsOutputRedirected: true, SupportsAnsi: false, new Dictionary<string, string>());

    int exit = CliApp.Run(["repl", "--color", "never", "--voice", "plain"], stdin, stdout, stderr, env);

    Assert.Equal(0, exit);
    string outText = stdout.ToString();
    Assert.Contains(":help", outText, StringComparison.Ordinal);
    Assert.Contains(":reset", outText, StringComparison.Ordinal);
    Assert.Contains(":quit", outText, StringComparison.Ordinal);
    Assert.Contains("22\n", outText, StringComparison.Ordinal);
    Assert.Contains("Session reset.\n", outText, StringComparison.Ordinal);
    Assert.Contains("error[", stderr.ToString(), StringComparison.Ordinal);
  }

  [Fact]
  public void CliRepl_StripsLeadingUtf8BomOnPipedStdin() {
    var stdin = new StringReader("\uFEFF:help\n:quit\n");
    var stdout = new StringWriter { NewLine = "\n" };
    var stderr = new StringWriter { NewLine = "\n" };
    var env = new CliEnvironment(_ => null, IsOutputRedirected: true, SupportsAnsi: false, new Dictionary<string, string>());

    int exit = CliApp.Run(["repl", "--color", "never"], stdin, stdout, stderr, env);

    Assert.Equal(0, exit);
    Assert.Contains("REPL commands:", stdout.ToString(), StringComparison.Ordinal);
    Assert.Equal(string.Empty, stderr.ToString());
  }
}
