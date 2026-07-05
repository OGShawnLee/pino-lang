using Xunit;
using System;
using Pino;

namespace pino_csharp.tests.Execution;

public class TupleTests {
  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestTupleBasicReturnAndDestructure(ExecutionEngine engine) {
    var code = @"
      fn divide(a int, b int) @(quotient int, remainder int) {
        return @(quotient: a / b, remainder: a % b)
      }
      val @(quotient: q, remainder: r) = divide(10, 3)
      val @(quotient) = divide(10, 3)
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(3L, env.Get("q"));
    Assert.Equal(1L, env.Get("r"));
    Assert.Equal(3L, env.Get("quotient"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestTupleDestructureRenaming(ExecutionEngine engine) {
    var code = @"
      fn divide(a int, b int) @(quotient int, remainder int) {
        return @(quotient: a / b, remainder: a % b)
      }
      val @(quotient: q_val, remainder: r_val) = divide(10, 3)
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(3L, env.Get("q_val"));
    Assert.Equal(1L, env.Get("r_val"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestTupleDestructureIgnoringFields(ExecutionEngine engine) {
    var code = @"
      fn divide(a int, b int) @(quotient int, remainder int) {
        return @(quotient: a / b, remainder: a % b)
      }
      val @(quotient: q) = divide(10, 3)
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(3L, env.Get("q"));
    Assert.ThrowsAny<Exception>(() => env.Get("remainder"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestTupleOrderIndependentExecution(ExecutionEngine engine) {
    var code = @"
      fn get_coords() @(x int, y int) {
        return @(y: 20, x: 10)
      }
      val @(y: b, x: a) = get_coords()
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(10L, env.Get("a"));
    Assert.Equal(20L, env.Get("b"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestGenericTupleOrderIndependent(ExecutionEngine engine) {
    var code = @"
      @generic[T]
      fn return_tuple(v T) @(value T, label string) {
        return @(label: ""generic"", value: v)
      }
      val @(value: v1, label: l1) = return_tuple[int](42)
      val @(label: l2, value: v2) = return_tuple[string](""hello"")
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(42L, env.Get("v1"));
    Assert.Equal("generic", env.Get("l1"));
    Assert.Equal("hello", env.Get("v2"));
    Assert.Equal("generic", env.Get("l2"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestTupleShorthandLiteralExecution(ExecutionEngine engine) {
    var code = @"
      fn divide(a int, b int) @(quotient int, remainder int) {
        val quotient = a / b
        val remainder = a % b
        return @(quotient, remainder)
      }
      val @(remainder: r, quotient: q) = divide(10, 3)
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(3L, env.Get("q"));
    Assert.Equal(1L, env.Get("r"));
  }
}
