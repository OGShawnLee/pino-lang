using Xunit;
using System;
using Pino;

namespace pino_csharp.tests.Execution;

public class ArithmeticTests {
  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestVMArithmetic(ExecutionEngine engine) {
    var code = @"
      val x = 2 + 3 * 4
      val y = (2 + 3) * 4
      val z = 10 / 2 - 1
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(14L, env.Get("x"));
    Assert.Equal(20L, env.Get("y"));
    Assert.Equal(4L, env.Get("z"));
  }
}
