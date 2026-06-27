using Xunit;
using System;
using Pino;

namespace pino_csharp.tests.Execution;

public class ScopingExecutionTests {
  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestScopeLeakageFromCallerToCalleeExecution(ExecutionEngine engine) {
    var code = @"
      var result = """"
      fn foo() string {
        val local_main = ""not an int""
        return local_main
      }
      fn run_test {
        val local_main = 12
        result = foo()
      }
      run_test()
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal("not an int", env.Get("result"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestScopeShadowingInLocalBlocksExecution(ExecutionEngine engine) {
    var code = @"
      var x_outer = 0
      var x_inner = """"
      fn run_test {
        val x = 10
        if true {
          val x = ""hello""
          x_inner = x
        }
        x_outer = x
      }
      run_test()
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal("hello", env.Get("x_inner"));
    Assert.Equal(10L, env.Get("x_outer"));
  }

  [Fact]
  public void TestLambdaCaptureScopingExecution() {
    var code = @"
      var val_x = 0
      fn run_test {
        val x = 5
        var f = fn() => x
        if true {
          val y = 10
          f = fn() => x + y
        }
        val_x = f()
      }
      run_test()
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(15L, env.Get("val_x"));
  }

  [Fact]
  public void TestDeeplyNestedCallerScopesExecution() {
    var code = @"
      val global_val = 123
      struct S {
        fn get_global() int {
          return global_val
        }
      }
      var res = 0
      fn run_test {
        val s = S {}
        if true {
          for 1 {
            res = s:get_global()
          }
        }
      }
      run_test()
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(123L, env.Get("res"));
  }
}
