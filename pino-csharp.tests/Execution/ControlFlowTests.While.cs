using Xunit;
using System;
using Pino;

namespace pino_csharp.tests.Execution;

public partial class ControlFlowTests {
  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestWhileLoopSimpleCondition(ExecutionEngine engine) {
    var code = @"
      var i = 1
      var sum = 0
      for i <= 10 {
        sum = sum + i
        i = i + 1
      }
      val res = sum
      val final_i = i
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(55L, env.Get("res"));
    Assert.Equal(11L, env.Get("final_i"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestWhileLoopBooleanVariable(ExecutionEngine engine) {
    var code = @"
      var active = true
      var count = 0
      for active {
        count = count + 1
        if count == 5 {
          active = false
        }
      }
      val res = count
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(5L, env.Get("res"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestWhileLoopNested(ExecutionEngine engine) {
    var code = @"
      var x = 1
      var total = 0
      for x <= 3 {
        var y = 1
        for y <= 3 {
          total = total + x * y
          y = y + 1
        }
        x = x + 1
      }
      val res = total
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(36L, env.Get("res"));
  }
}
