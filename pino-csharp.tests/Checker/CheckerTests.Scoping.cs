using Xunit;
using System;
using Pino;

namespace pino_csharp.tests;

public partial class CheckerTests {
  [Fact]
  public void TestScopeLeakageFromCallerToCallee() {
    var input = @"
      fn foo() string {
        val local_main = ""not an int""
        return local_main
      }

      fn main {
        val local_main = 12
        val result = foo()
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestScopeLeakageNestedCalls() {
    var input = @"
      fn foo() bool {
        val local_var = true
        return local_var
      }

      fn bar() string {
        val local_var = ""hello""
        val res = foo()
        return local_var
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestScopeLeakageStructMethods() {
    var input = @"
      struct Helper {
        static fn check() string {
          val x = ""valid string""
          return x
        }
      }

      fn main {
        val x = 42
        val s = Helper::check()
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestScopeLeakageGlobalPreservation() {
    var input = @"
      val GLOBAL_LIMIT = 100

      fn check_limit() int {
        return GLOBAL_LIMIT
      }

      fn main {
        val result = check_limit()
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestScopeShadowingInLocalBlocks() {
    var input = @"
      fn shadow_test() int {
        val x = 10
        if true {
          val x = ""hello""
        }
        return x
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestDeeplyNestedCallerScopesExecution() {
    var input = @"
      val global_val = 123
      struct S {
        fn get_global() int {
          return global_val
        }
      }

      fn main {
        val s = S {}
        var res = 0
        if true {
          for 1 {
            res = s:get_global()
          }
        }
      }
    ";
    PinoTestRunner.Execute(input, ExecutionEngine.TreeWalk);
  }

  [Fact]
  public void TestLambdaCaptureScoping() {
    var input = @"
      fn lambda_capture() fn() int {
        val x = 5
        if true {
          val y = 10
          return fn() => x + y
        }
        return fn() => x
      }
    ";
    CheckCode(input);
  }
}
