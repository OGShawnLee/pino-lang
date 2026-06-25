using Xunit;
using System;
using Pino;

namespace pino_csharp.tests.Execution;

public partial class ControlFlowTests {
  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestVMLocalsAndGlobals(ExecutionEngine engine) {
    var code = @"
      val a = 10
      var b = 5
      {
        val a = 20
        b = a + b
      }
      val res = b
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(25L, env.Get("res"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestVMControlFlow(ExecutionEngine engine) {
    var code = @"
      var x = 0
      if true {
        x = 10
      } else {
        x = 20
      }

      var y = 0
      if false {
        y = 10
      } else {
        y = 20
      }
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(10L, env.Get("x"));
    Assert.Equal(20L, env.Get("y"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestVMFunctionsAndRecursion(ExecutionEngine engine) {
    var code = @"
      fn add(a int, b int) int {
        return a + b
      }
      val resAdd = add(5, 7)

      fn fib(n int) int {
        if n <= 1 {
          return n
        }
        return fib(n - 1) + fib(n - 2)
      }
      val resFib = fib(6)
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(12L, env.Get("resAdd"));
    Assert.Equal(8L, env.Get("resFib"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestRangeLoopSingleVar(ExecutionEngine engine) {
    // Note: We capture output of println in this test by using standard evaluator redirect.
    // However, since VM doesn't print to Console via native Println (wait, PrintlnFunction calls Console.WriteLine),
    // we can redirect Console.Out during VM execution too!
    var code = @"
      var sum = 0
      for i in 3 {
        sum = sum + i
      }
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(3L, env.Get("sum")); // 0 + 1 + 2 = 3
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestRangeLoopDoubleVar(ExecutionEngine engine) {
    var code = @"
      var sumIdx = 0
      var sumVal = 0
      for idx, v in 3 {
        sumIdx = sumIdx + idx
        sumVal = sumVal + v
      }
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(3L, env.Get("sumIdx"));
    Assert.Equal(3L, env.Get("sumVal"));
  }

  [Fact]
  public void TestMatchStatementExecution() {
    // 1. Simple match on integers
    var code1 = @"
      var x = 2
      var res = """"
      match x {
        when 1 { res = ""one"" }
        when 2 { res = ""two"" }
        else { res = ""other"" }
      }
    ";
    var env1 = PinoTestRunner.Execute(code1, ExecutionEngine.TreeWalk);
    Assert.Equal("two", env1.Get("res"));

    // 2. Match with multiple when-values
    var code2 = @"
      var score = 8
      var grade = """"
      match score {
        when 9, 10 { grade = ""A"" }
        when 7, 8 { grade = ""B"" }
        else { grade = ""C"" }
      }
    ";
    var env2 = PinoTestRunner.Execute(code2, ExecutionEngine.TreeWalk);
    Assert.Equal("B", env2.Get("grade"));

    // 3. Match with Else fallback
    var code3 = @"
      var score = 5
      var grade = """"
      match score {
        when 9, 10 { grade = ""A"" }
        when 7, 8 { grade = ""B"" }
        else { grade = ""F"" }
      }
    ";
    var env3 = PinoTestRunner.Execute(code3, ExecutionEngine.TreeWalk);
    Assert.Equal("F", env3.Get("grade"));

    // 4. Match on Enums
    var code4 = @"
      enum State { Active, Closed }
      var current = State::Closed
      var message = """"
      match current {
        when State::Active { message = ""Online"" }
        when State::Closed { message = ""Offline"" }
      }
    ";
    var env4 = PinoTestRunner.Execute(code4, ExecutionEngine.TreeWalk);
    Assert.Equal("Offline", env4.Get("message"));
  }
}
