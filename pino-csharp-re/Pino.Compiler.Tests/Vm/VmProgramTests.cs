using Pino.Compiler.IR;
using Pino.Vm;

namespace Pino.Compiler.Tests.Vm;

/// <summary>Whole-program behavior on the VM: value semantics, D-184, D-185, panics, print forms, and the host hooks.</summary>
public sealed class VmProgramTests {
  private static string Out(VmRun run) => VmTestHarness.Normalize(run.Stdout);

  private static void AssertCleanRun(VmRun run) {
    Assert.Equal(0, run.Result.ExitCode);
    Assert.Null(run.Result.RcError);
    Assert.True(run.Result.LeakReport!.IsClean, run.Result.LeakReport.Format());
  }

  // ---------------------------------------------------------------------------
  // CoW value semantics (D-177)
  // ---------------------------------------------------------------------------

  [Fact]
  public void CopyOnWrite_VectorAssignmentIsAnIndependentCopy() {
    const string Source = """
        val a = [1, 2]
        var b = a
        b:push(3)
        b[0] = 99
        println(a)
        println(b)
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal("[1, 2]\n[99, 2, 3]", Out(run));
    AssertCleanRun(run);
  }

  [Fact]
  public void CopyOnWrite_MapSetAndPoolCopiesAreIndependent() {
    const string Source = """
        val m = map[string, int] { "a" -> 1 }
        var m2 = m
        m2["b"] = 2
        println(m)
        println(m2)

        val s = set[int] { 1, 2 }
        var s2 = s
        s2:add(3)
        println(s)
        println(s2)

        var p = Pool[string]
        val h = p:add("one")
        var p2 = p
        val h2 = p2:add("two")
        println(p)
        println(p2)
        println(h2 in p)
        println(h2 in p2)
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal(
        "map[string, int] { \"a\" -> 1 }\nmap[string, int] { \"a\" -> 1, \"b\" -> 2 }\n"
        + "set[int] { 1, 2 }\nset[int] { 1, 2, 3 }\n"
        + "Pool[string] { Handle(0, 0) -> \"one\" }\nPool[string] { Handle(0, 0) -> \"one\", Handle(1, 0) -> \"two\" }\n"
        + "false\ntrue",
        Out(run));
    AssertCleanRun(run);
  }

  [Fact]
  public void CopyOnWrite_NestedStructAndVectorFieldsDetachOnlyWhenMutated() {
    const string Source = """
        struct Bag {
          mut items []string
          mut n int
        }

        val a = Bag { items: ["x"], n: 1 }
        var b = a
        b:items:push("y")
        b:n = 5
        println(a)
        println(b)
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal("Bag { items: [\"x\"], n: 1 }\nBag { items: [\"x\", \"y\"], n: 5 }", Out(run));
    AssertCleanRun(run);
  }

  // ---------------------------------------------------------------------------
  // D-184 and D-185
  // ---------------------------------------------------------------------------

  [Fact]
  public void D184_AliasedMutElementsPanicWithTheExactMessage() {
    const string Source = """
        fn duel(mut a int, mut b int) {
          a += 1
          b += 1
        }

        var enemies = [10, 20]
        val i = 0
        val j = 0
        duel(mut enemies[i], mut enemies[j])
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal(101, run.Result.ExitCode);
    Assert.Equal(
        "Cannot pass the same element of 'enemies' as two 'mut' arguments to 'duel'.",
        run.Result.PanicMessage);
    Assert.Contains("thread 'main' panicked at 'Cannot pass the same element of 'enemies'", run.Stderr, StringComparison.Ordinal);
  }

  [Fact]
  public void D184_DistinctElementsAreUpdatedInPlace() {
    const string Source = """
        fn duel(mut a int, mut b int) {
          a += 1
          b += 1
        }

        var enemies = [10, 20]
        var i = 0
        var j = 1
        duel(mut enemies[i], mut enemies[j])
        println(enemies)
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal("[11, 21]", Out(run));
    AssertCleanRun(run);
  }

  [Fact]
  public void D185_StaleHandleReadPanicsWithTheExactMessage() {
    const string Source = """
        var armory = Pool[int]
        val h = armory:add(10)
        val removed = armory:remove(h)
        println(armory[h])
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal(101, run.Result.ExitCode);
    Assert.Equal("This handle no longer points to a value in 'armory'; it was removed.", run.Result.PanicMessage);
  }

  [Fact]
  public void D185_PoolPrintFormsAndTypeNames() {
    const string Source = """
        var p = Pool[string]
        val a = p:add("Ann")
        val b = p:add("Bob")
        println(p)
        println(a)
        println(type(p))
        println(type(a))
        p:remove(a)
        println(p)
        println(b in p)
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal(
        "Pool[string] { Handle(0, 0) -> \"Ann\", Handle(1, 0) -> \"Bob\" }\nHandle(0, 0)\nPool\nHandle\nPool[string] { Handle(1, 0) -> \"Bob\" }\ntrue",
        Out(run));
    AssertCleanRun(run);
  }

  // ---------------------------------------------------------------------------
  // Panics (ERR-3..5, ERR-21, ERR-24, D-186)
  // ---------------------------------------------------------------------------

  [Fact]
  public void IntMinFloorDivByMinusOne_PanicsWithTheOverflowMessage() {
    const string Source = """
        var x = -9223372036854775807 - 1
        var d = -1
        println("before")
        println(x // d)
        println("after")
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal(101, run.Result.ExitCode);
    Assert.Equal("before", Out(run));
    Assert.Equal(
        "Integer division overflowed: the smallest 64-bit integer cannot be divided by -1.",
        run.Result.PanicMessage);
  }

  [Fact]
  public void IntMinModuloMinusOne_PanicsWithTheOverflowMessage() {
    const string Source = """
        var x = -9223372036854775807 - 1
        var d = -1
        println(x % d)
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal(101, run.Result.ExitCode);
    Assert.Equal(IrLowerer.PanicIntMinDivOverflow, run.Result.PanicMessage);
  }

  [Fact]
  public void DivisionByZero_UsesThePlainEnglishMessage() {
    const string Source = """
        var z = 0
        println(10 // z)
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal(101, run.Result.ExitCode);
    Assert.Equal("Cannot divide by zero.", run.Result.PanicMessage);
  }

  [Fact]
  public void FloorDivAndModuloRoundTowardNegativeInfinity() {
    const string Source = """
        var a = -7
        var b = 2
        println(a // b)
        println(a % b)
        println(7 // -2)
        println(7 % -2)
        println(-7.5 // 2.0)
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal("-4\n1\n-4\n-1\n-4.0", Out(run));
  }

  [Fact]
  public void VectorIndexOutOfRange_UsesThePlainEnglishMessage() {
    const string Source = """
        val xs = [1, 2, 3]
        var i = 5
        println(xs[i])
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal("Index 5 is out of range for a vector of length 3.", run.Result.PanicMessage);
  }

  [Fact]
  public void StringIndexAndSubstringOutOfRange_UseThePlainEnglishMessages() {
    VmRun index = VmTestHarness.Run("val s = \"héllo\"\nvar i = 9\nprintln(s[i])\n");
    VmRun substring = VmTestHarness.Run("val s = \"héllo\"\nvar n = 4\nprintln(s:substring(3, n))\n");

    Assert.Equal("Index 9 is out of range for a string of length 5.", index.Result.PanicMessage);
    Assert.Equal(
        "Substring starting at 3 with length 4 is out of range for a string of length 5.",
        substring.Result.PanicMessage);
  }

  [Fact]
  public void MissingMapKey_UsesThePlainEnglishMessage() {
    VmRun run = VmTestHarness.Run("val m = map[string, int] { \"a\" -> 1 }\nvar k = \"zz\"\nprintln(m[k])\n");

    Assert.Equal("Key \"zz\" was not found in map.", run.Result.PanicMessage);
  }

  [Fact]
  public void InvalidRegexPattern_PanicsWithThePatternAndDetail() {
    VmRun run = VmTestHarness.Run("val r = regex(\"[\")\nprintln(r:pattern)\n");

    Assert.Equal(101, run.Result.ExitCode);
    Assert.StartsWith("Invalid pattern '[': ", run.Result.PanicMessage, StringComparison.Ordinal);
    Assert.EndsWith(".", run.Result.PanicMessage, StringComparison.Ordinal);
  }

  [Fact]
  public void Panic_PrintsLocationAndNumberedBacktraceToStderr() {
    const string Source = "fn first() {\n  second()\n}\n\nfn second() {\n  panic(\"crash message\")\n}\n\nfirst()\n";

    VmRun run = VmTestHarness.Run(Source, path: "panic_case.pino");

    Assert.Equal(101, run.Result.ExitCode);
    Assert.StartsWith(
        "thread 'main' panicked at 'crash message'\n  at panic_case.pino:6:3\n",
        run.Stderr,
        StringComparison.Ordinal);
    Assert.Contains("  0: second at panic_case.pino:6:3\n", run.Stderr, StringComparison.Ordinal);
    Assert.Contains("  1: first at panic_case.pino:2:3\n", run.Stderr, StringComparison.Ordinal);
    Assert.Contains("  2: __pino_entry at panic_case.pino:9:1\n", run.Stderr, StringComparison.Ordinal);
    Assert.Equal(string.Empty, run.Stdout);
  }

  [Fact]
  public void MainFailure_WritesErrorLineToStderrAndExitsOne() {
    VmRun result = VmTestHarness.Run("fn main() Result[int, string] {\n  println(\"start\")\n  return Result::Failure(\"boom\")\n}\n");
    VmRun none = VmTestHarness.Run("fn main() Option[int] {\n  return Option::None\n}\n");
    VmRun ok = VmTestHarness.Run("fn main() Result[int, string] {\n  return Result::Success(3)\n}\n");

    Assert.Equal(1, result.Result.ExitCode);
    Assert.Equal("start", Out(result));
    Assert.Equal("Error: boom\n", result.Stderr);
    Assert.Equal(1, none.Result.ExitCode);
    Assert.Equal("Error: Option::None\n", none.Stderr);
    Assert.Equal(0, ok.Result.ExitCode);
    Assert.Equal(string.Empty, ok.Stderr);
  }

  // ---------------------------------------------------------------------------
  // Print forms (D-022, D-186)
  // ---------------------------------------------------------------------------

  [Fact]
  public void PrintForms_CoverScalarsCollectionsUnionsAndStructs() {
    const string Source = """
        union Shape {
          Circle(r float)
          Dot
        }
        enum Dir { North, South }
        struct User { name string }

        println(true, false, 3, 2.0, 'x')
        println(["a", "b"], [1.5, 2.0], []int)
        println(map[string, int] { "k" -> 1 }, map[int, int])
        println(set[string] { "b", "a" }, set[int])
        println(Shape::Circle(1.5), Shape::Dot, Dir::South)
        var none Option[int] = Option::None
        println(Option::Some(2), none, Option::Some("s"))
        println(User { name: "Test" })
        println(type(1), type(1.5), type("s"), type([1]), type(true))
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal(
        "true false 3 2.0 x\n"
        + "[\"a\", \"b\"] [1.5, 2.0] []\n"
        + "map[string, int] { \"k\" -> 1 } map[int, int] { }\n"
        + "set[string] { \"b\", \"a\" } set[int] { }\n"
        + "Shape::Circle(1.5) Shape::Dot Dir::South\n"
        + "Option::Some(2) Option::None Option::Some(\"s\")\n"
        + "User { name: \"Test\" }\n"
        + "int float string vector bool",
        Out(run));
    AssertCleanRun(run);
  }

  // ---------------------------------------------------------------------------
  // Strings
  // ---------------------------------------------------------------------------

  [Fact]
  public void Strings_UseCodePointIndexingAndZeroCopySlices() {
    const string Source = """
        val s = "a🌲bc"
        println(s:len)
        println(s[1])
        println(s:substring(1, 2))
        println(s:index_of("b"))
        println(s:last_index_of("c"))
        println("  hi  ":trim() + "|")
        println("a,b,c":split(","))
        println("hello":replace("l", "L"))
        println("Hello":upper(), "Hello":lower())
        println('x' + "y", "y" + 'x')
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal("4\n🌲\n🌲b\n2\n3\nhi|\n[\"a\", \"b\", \"c\"]\nheLLo\nHELLO hello\nxy yx", Out(run));
    AssertCleanRun(run);
  }

  // ---------------------------------------------------------------------------
  // Host hooks, step budget and cancellation
  // ---------------------------------------------------------------------------

  [Fact]
  public void HostHooks_ProvideInputFilesClockRandomSleepAndClear() {
    const string Source = """
        val line = readline("name? ")
        println(line)
        println(file_exists("a.txt"))
        println(write_file("a.txt", "hello"))
        println(file_exists("a.txt"))
        println(read_file("a.txt"))
        println(read_file("missing.txt"))
        println(time())
        println(rand(0))
        sleep(5)
        clear()
        """;
    var fs = new InMemoryVmFileSystem();

    VmRun run = VmTestHarness.Run(Source, input: "Pino\n", fs: fs);

    Assert.Equal(0, run.Result.ExitCode);
    Assert.Equal(
        "name? Result::Success(\"Pino\")\nfalse\nResult::Success(\"a.txt\")\ntrue\nResult::Success(\"hello\")\n"
        + "Result::Failure(IOError::NotFound(\"missing.txt\"))\n1700000000000.0\n0",
        Out(run));
    Assert.Equal("hello", fs.Files["a.txt"]);
    Assert.Equal(1, run.Host.SleepCalls);
    Assert.Equal(1, run.Host.ClearCalls);
  }

  [Fact]
  public void Readline_AtEndOfInputYieldsFailure() {
    VmRun run = VmTestHarness.Run("println(readline(\"\"))\n", input: string.Empty);

    Assert.StartsWith("Result::Failure(", Out(run), StringComparison.Ordinal);
  }

  [Fact]
  public void ParsingBuiltins_ReturnResults() {
    const string Source = """
        println(int("42"), int("x"), float("2.5"), float("y"))
        println(int(3.9), float(7), rune(65), rune("Z"))
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal(
        "Result::Success(42) Result::Failure(\"Cannot convert 'x' to an int.\") Result::Success(2.5) Result::Failure(\"Cannot convert 'y' to a float.\")\n3 7.0 A Z",
        Out(run));
  }

  [Fact]
  public void StepBudget_StopsAnInfiniteLoop() {
    IrModule module = VmTestHarness.BuildPostRc("for {\n}\n");

    VmRun run = VmTestHarness.Execute(
        module,
        verifyRc: true,
        string.Empty,
        source: null,
        options: new PinoVmOptions { VerifyRc = true, MaxSteps = 1000 });

    Assert.Equal(PinoVmStopReason.StepLimit, run.Result.Reason);
    Assert.Equal(1, run.Result.ExitCode);
    Assert.Contains("step limit", run.Stderr, StringComparison.Ordinal);
    Assert.True(run.Result.Steps > 1000);
  }

  [Fact]
  public void CancellationToken_IsCheckedOnBackwardBranches() {
    IrModule module = VmTestHarness.BuildPostRc("for {\n}\n");
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var host = new BufferedVmHost();
    var vm = new PinoVm(module, host, new PinoVmOptions { VerifyRc = false, CancellationToken = cts.Token });

    Assert.Throws<OperationCanceledException>(() => vm.Run());
  }

  [Fact]
  public void ClosuresAndInterfaces_RunWithoutLeaks() {
    const string Source = """
        interface Named {
          name string
          fn greet() string
        }

        struct Dog {
          name string
          fn greet() string {
            return "woof " + self:name
          }
        }

        fn make_adder(n int) fn (int) -> int {
          return fn (x int) -> x + n
        }

        val add3 = make_adder(3)
        println(add3(4))
        val n Named = Dog { name: "rex" }
        println(n:greet(), n:name)
        println(type(n))
        println([1, 2, 3]:map(it * 2))
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal("7\nwoof rex rex\nDog\n[2, 4, 6]", Out(run));
    AssertCleanRun(run);
  }

  [Fact]
  public void GlobalsAreInitializedOnceAndDroppedAtShutdown() {
    const string Source = """
        var log = ["start"]
        fn note(msg string) {
          log:push(msg)
        }
        note("a")
        note("b")
        println(log)
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal("[\"start\", \"a\", \"b\"]", Out(run));
    AssertCleanRun(run);
    Assert.Equal(0, run.Vm.RcTracker.LiveObjectCount);
  }
}
