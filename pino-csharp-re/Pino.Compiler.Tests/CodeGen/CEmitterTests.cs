using System.Globalization;
using System.Text;

using Pino.Cli;
using Pino.Compiler.Checking;
using Pino.Compiler.CodeGen.C;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.IR.Passes;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.CodeGen;

public sealed class CEmitterTests : IDisposable {
  private readonly string _work = Path.Combine(Path.GetTempPath(), "pino-re-cemitter-" + Guid.NewGuid().ToString("N"));
  private readonly string _repoRoot = CCompilerDriver.FindRepositoryRoot();

  public CEmitterTests() {
    Directory.CreateDirectory(_work);
  }

  public void Dispose() {
    try {
      Directory.Delete(_work, recursive: true);
    } catch (IOException) {
    } catch (UnauthorizedAccessException) {
    }
  }

  [Fact]
  public void EmittedC_HasNoBomNoGcHeaderAndHandlesCKeywords() {
    const string Source = """
      struct Record {
        char rune
        default int
        register string
        auto bool
      }

      fn switch_val(unsigned int, signed int) int {
        val volatile = unsigned + signed
        val goto = volatile * 2
        return goto
      }

      val char = 'a'
      val default = 1
      val register = "ok"
      val r = Record { char: char, default: default, register: register, auto: true }
      println(r:char, r:default, r:register, r:auto, switch_val(3, 4))
      """;

    (string cSource, CExecResult exec) = CompileAndRunTcc(Source, "keywords.pino", debugRc: true);

    byte[] bytes = CEmitter.Utf8NoBom.GetBytes(cSource);
    bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    Assert.False(hasBom);
    Assert.DoesNotContain("#include <gc.h>", cSource, StringComparison.Ordinal);
    Assert.DoesNotContain("#include <stdatomic.h>", cSource, StringComparison.Ordinal);
    Assert.Contains("#include \"pino_rt.h\"", cSource, StringComparison.Ordinal);

    Assert.Equal(0, exec.ExitCode);
    Assert.Equal("a 1 ok true 14", Normalize(exec.Stdout));
    Assert.Equal(string.Empty, exec.Stderr.Trim());
  }

  [Fact]
  public void FloatFormatting_MatchesDotNetOracleAndD022() {
    const string Source = """
      val zero = 0.0
      val neg_zero = -zero
      val f1e20 = float("1e20") or { yield 0.0 }
      val f1e16 = float("1e16") or { yield 0.0 }
      val f1e15 = float("1e15") or { yield 0.0 }
      val f1e14 = float("1e14") or { yield 0.0 }
      val fbig = float("123456789012345680.0") or { yield 0.0 }
      val fsmall = float("1.5e-7") or { yield 0.0 }
      println(str(f1e20))
      println(str(f1e16))
      println(str(f1e15))
      println(str(f1e14))
      println(str(fbig))
      println(str(0.0001))
      println(str(0.00001))
      println(str(fsmall))
      println(str(0.1 + 0.2))
      println(str(neg_zero))
      println(str(3.0))
      println(str(2.5))
      """;

    (_, CExecResult exec) = CompileAndRunTcc(Source, "floats.pino", debugRc: true);
    Assert.Equal(0, exec.ExitCode);

    double[] values = [
      1e20,
      1e16,
      1e15,
      1e14,
      123456789012345680.0,
      0.0001,
      0.00001,
      1.5e-7,
      0.1 + 0.2,
      -0.0,
      3.0,
      2.5,
    ];
    string[] expected = [.. values.Select(FormatOracleFloat)];

    Assert.Equal(string.Join("\n", expected), Normalize(exec.Stdout));
    Assert.Equal(string.Empty, exec.Stderr.Trim());
  }

  [Fact]
  public void CowValueSemantics_DetachesVectorsMapsSetsPoolsAndInterfacesWithZeroLeaks() {
    const string Source = """
      interface Counter {
        mut count int
        mut fn bump(n int)
        fn get() int
      }

      struct BoxCounter {
        mut count int
        label string

        mut fn bump(n int) {
          self:count += n
        }

        fn get() int {
          return self:count
        }
      }

      var v1 = ["a", "b"]
      var v2 = v1
      v2:push("c")
      println(v1)
      println(v2)

      var m1 = map[string, int] { "k" -> 1 }
      var m2 = m1
      m2["k"] = 99
      m2["z"] = 2
      println(m1)
      println(m2)

      var s1 = set[string] { "x" }
      var s2 = s1
      s2:add("y")
      println(s1)
      println(s2)

      var p1 = Pool[string]
      val h = p1:add("hero")
      var p2 = p1
      val _ = p2:remove(h)
      println(p1[h])
      println(p2:len)

      var c1 Counter = BoxCounter { count: 10, label: "tag" }
      var c2 Counter = c1
      c2:bump(5)
      println(c1:get(), c2:get())
      """;

    (_, CExecResult exec) = CompileAndRunTcc(Source, "cow.pino", debugRc: true);
    Assert.Equal(0, exec.ExitCode);
    string expected = """
      ["a", "b"]
      ["a", "b", "c"]
      map[string, int] { "k" -> 1 }
      map[string, int] { "k" -> 99, "z" -> 2 }
      set[string] { "x" }
      set[string] { "x", "y" }
      hero
      0
      10 15
      """;
    Assert.Equal(Normalize(expected), Normalize(exec.Stdout));
    Assert.Equal(string.Empty, exec.Stderr.Trim());
  }

  [Fact]
  public void RuntimePanics_ReportExactLineColBacktraceAndSpecMessages() {
    const string Source = """
      fn inner() {
        panic("boom here")
      }

      fn outer() {
        inner()
      }

      outer()
      """;

    (string cSource, CExecResult exec) = CompileAndRunTcc(Source, "panic_loc.pino", debugRc: true);
    Assert.Contains("#line 2 \"", cSource, StringComparison.Ordinal);
    Assert.Equal(101, exec.ExitCode);
    Assert.Contains("thread 'main' panicked at 'boom here'", exec.Stderr, StringComparison.Ordinal);
    Assert.Contains("panic_loc.pino:2:", exec.Stderr, StringComparison.Ordinal);
    Assert.Contains("0: inner", exec.Stderr, StringComparison.Ordinal);
    Assert.Contains("1: outer", exec.Stderr, StringComparison.Ordinal);
  }

  [Fact]
  public void D184_D185_AndIntMinOverflow_PanicWithExactMessages() {
    const string D184Source = """
      fn swap_ints(mut a int, mut b int) {
        val t = a
        a = b
        b = t
      }

      var xs = [1, 2, 3]
      val i = 1
      val j = 1
      swap_ints(mut xs[i], mut xs[j])
      """;
    (_, CExecResult r184) = CompileAndRunTcc(D184Source, "d184.pino", debugRc: true);
    Assert.Equal(101, r184.ExitCode);
    Assert.Contains("Cannot pass the same element of 'xs' as two 'mut' arguments to 'swap_ints'.", r184.Stderr, StringComparison.Ordinal);

    const string D185Source = """
      var items = Pool[int]
      val h = items:add(42)
      val _ = items:remove(h)
      println(items[h])
      """;
    (_, CExecResult r185) = CompileAndRunTcc(D185Source, "d185.pino", debugRc: true);
    Assert.Equal(101, r185.ExitCode);
    Assert.Contains("This handle no longer points to a value in 'items'; it was removed.", r185.Stderr, StringComparison.Ordinal);

    const string IntMinSource = """
      var a = -9223372036854775808
      var b = -1
      println(a // b)
      """;
    (_, CExecResult rMin) = CompileAndRunTcc(IntMinSource, "intmin.pino", debugRc: true);
    Assert.Equal(101, rMin.ExitCode);
    Assert.Contains("Integer division overflowed: the smallest 64-bit integer cannot be divided by -1.", rMin.Stderr, StringComparison.Ordinal);
  }

  [Fact]
  public void CliRunAndBuild_HandleCExecutionReleaseEmitCHintsAndDefaultVm() {
    string semiFile = Path.Combine(_work, "semi.pino");
    File.WriteAllText(semiFile, "val a = 40;\nval b = 2;\nprintln(a + b);\n", CEmitter.Utf8NoBom);

    // 1. Default `run` (no engine flag) executes on the VM, prints the PN1018 hints, and exits 0.
    (int vmCode, string vmOut, string vmErr) = RunCli(["run", "--color", "never", semiFile]);
    Assert.Equal(0, vmCode);
    Assert.Equal("42", Normalize(vmOut));
    Assert.Contains("hint[PN1018]", vmErr, StringComparison.Ordinal);

    // 2. `run --c --debug-rc` still executes a file with only PN1018 hints, prints the hints, and exits 0.
    (int runCode, string runOut, string runErr) = RunCli(["run", "--c", "--debug-rc", "--color", "never", semiFile]);
    Assert.Equal(0, runCode);
    Assert.Equal("42", Normalize(runOut));
    Assert.Contains("hint[PN1018]", runErr, StringComparison.Ordinal);

    // 3. `build --emit-c` writes BOM-less C and a working executable.
    string outExe = Path.Combine(_work, OperatingSystem.IsWindows() ? "semi_out.exe" : "semi_out");
    string emittedC = Path.Combine(_work, "semi_out.c");
    (int buildCode, _, string buildErr) = RunCli(["build", "--debug-rc", "--emit-c", emittedC, "-o", outExe, "--color", "never", semiFile]);
    Assert.Equal(0, buildCode);
    Assert.True(File.Exists(outExe));
    Assert.True(File.Exists(emittedC));
    Assert.Contains("hint[PN1018]", buildErr, StringComparison.Ordinal);

    byte[] emittedBytes = File.ReadAllBytes(emittedC);
    Assert.False(emittedBytes.Length >= 3 && emittedBytes[0] == 0xEF && emittedBytes[1] == 0xBB && emittedBytes[2] == 0xBF);

    CExecResult directRun = CCompilerDriver.RunExecutable(outExe, _work, failOnLeak: true, closeStdin: true);
    Assert.Equal(0, directRun.ExitCode);
    Assert.Equal("42", Normalize(directRun.Stdout));
  }

  [Fact]
  public void GccReleaseSmoke_MultiFeatureProgramBuildsAndMatchesTccOutput() {
    if (CCompilerDriver.FindReleaseCompiler() is null) {
      return; // gcc/clang optional when absent on PATH.
    }

    const string Source = """
      @@Generic[T]
      union List {
        Cons(head T, tail List[T])
        Nil
      }

      fn sum_list(xs List[int]) int {
        return match xs {
          when List::Nil -> 0
          when List::Cons(h, t) -> h + sum_list(t)
        }
      }

      interface Greeter {
        name string
        fn greet() string
      }

      struct LargePerson {
        name string
        bio string
        role string
        score int

        fn greet() string {
          return "$name ($role): $score"
        }
      }

      fn make_counter(start int) fn () -> int {
        var n = start
        return fn () {
          n += 1
          return n
        }
      }

      @@Generic[T]
      fn identity(x T) T {
        return x
      }

      fn main() {
        val xs = List::Cons(10, List::Cons(20, List::Cons(12, List::Nil)))
        println(sum_list(xs))

        val g Greeter = LargePerson { name: "Ada", bio: "Pioneer", role: "Lead", score: 100 }
        println(g:greet())
        println(type(g))

        val c = make_counter(5)
        println(c(), c(), c())

        var pool = Pool[string]
        val h1 = pool:add("sword")
        val h2 = pool:add("shield")
        val _ = pool:remove(h1)
        val h3 = pool:add("wand")
        println(pool)
        println(pool[h2], pool[h3])

        val mapped = [1, 2, 3, 4]:filter(it % 2 == 0):map(it * 10)
        println(identity(mapped))
      }
      """;

    (string cSource, CExecResult tccExec) = CompileAndRunTcc(Source, "smoke.pino", debugRc: true);
    Assert.Equal(0, tccExec.ExitCode);

    string gccExe = Path.Combine(_work, OperatingSystem.IsWindows() ? "smoke_gcc.exe" : "smoke_gcc");
    CBuildResult gccBuild = CCompilerDriver.Build(new CBuildOptions(
        CSource: cSource,
        OutputExecutablePath: gccExe,
        Release: true,
        DebugRc: true,
        RepoRoot: _repoRoot));
    Assert.True(gccBuild.Succeeded, $"GCC release build failed:\n{gccBuild.Stderr}\n{gccBuild.Stdout}");

    CExecResult gccExec = CCompilerDriver.RunExecutable(gccExe, _work, failOnLeak: true, closeStdin: true);
    Assert.Equal(0, gccExec.ExitCode);
    Assert.Equal(Normalize(tccExec.Stdout), Normalize(gccExec.Stdout));
    Assert.Equal(string.Empty, gccExec.Stderr.Trim());
  }

  private (string CSource, CExecResult Exec) CompileAndRunTcc(string source, string fileName, bool debugRc) {
    string pinoPath = Path.Combine(_work, fileName);
    File.WriteAllText(pinoPath, source, CEmitter.Utf8NoBom);

    ParseResult parsed = Parser.Parse(new SourceText(source, pinoPath));
    Assert.Empty(parsed.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

    IrModule ir = IrLowerer.Lower(compilation);
    RcInsertPass.Run(ir);
    string cSource = CEmitter.Emit(ir, compilation);

    string exePath = Path.Combine(_work, Path.GetFileNameWithoutExtension(fileName) + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
    CBuildResult build = CCompilerDriver.Build(new CBuildOptions(
        CSource: cSource,
        OutputExecutablePath: exePath,
        Release: false,
        DebugRc: debugRc,
        RepoRoot: _repoRoot));
    Assert.True(build.Succeeded, $"TCC build failed for {fileName}:\n{build.Stderr}\n{build.Stdout}");

    CExecResult exec = CCompilerDriver.RunExecutable(exePath, _work, failOnLeak: debugRc, closeStdin: true);
    return (cSource, exec);
  }

  private static (int ExitCode, string Stdout, string Stderr) RunCli(string[] args) {
    var stdout = new StringWriter();
    var stderr = new StringWriter();
    var env = new CliEnvironment(_ => null, IsOutputRedirected: true, SupportsAnsi: false);
    int code = CliApp.Run(args, stdout, stderr, env);
    return (code, stdout.ToString(), stderr.ToString());
  }

  private static string FormatOracleFloat(double value) {
    if (double.IsNaN(value)) {
      return "NaN";
    }

    if (double.IsPositiveInfinity(value)) {
      return "Infinity";
    }

    if (double.IsNegativeInfinity(value)) {
      return "-Infinity";
    }

    string text = value.ToString(CultureInfo.InvariantCulture);
    if (!text.Contains('.', StringComparison.Ordinal)
        && !text.Contains('E', StringComparison.Ordinal)
        && !text.Contains('e', StringComparison.Ordinal)) {
      text += ".0";
    }

    return text;
  }

  private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
