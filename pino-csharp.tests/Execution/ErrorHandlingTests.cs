using Xunit;
using System;
using Pino;

namespace pino_csharp.tests.Execution;

public class ErrorHandlingTests {
  [Fact]
  public void TestPanicHaltAndBacktrace() {
    var code = @"
      fn first {
        second()
      }
      fn second {
        panic(""crash message"")
      }
      first()
    ";

    var ex = Assert.Throws<PinoPanicException>(() => {
      PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    });

    Assert.Equal("crash message", ex.PanicMessage);
    Assert.Contains("second", ex.CallStack);
    Assert.Contains("first", ex.CallStack);
  }

  [Fact]
  public void TestBubbleOperatorSuccessAndFailure() {
    var code = @"
      var res_ok = """"
      var res_fail = """"

      fn check_ok() Result[string, string] {
        val val1 = Result::Success(""success_val"")?
        res_ok = val1
        return Result::Success(""done"")
      }

      fn check_fail() Result[string, string] {
        val val2 = Result::Failure(""error_val"")?
        res_fail = val2
        return Result::Success(""done"")
      }

      fn run_test {
        check_ok()
        check_fail()
      }
      run_test()
    ";

    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal("success_val", env.Get("res_ok"));
    Assert.Equal("", env.Get("res_fail")); // check_fail should have returned early on bubble
  }

  [Fact]
  public void TestRecoveryOrBlockWithYield() {
    var code = @"
      var res_ok = """"
      var res_fail = """"
      var captured_err = """"

      fn check_ok() string {
        val val1 = Result::Success(""success_val"") or {
          yield ""fallback_val""
        }
        return val1
      }

      fn check_fail() string {
        val val2 = Result::Failure(""error_val"") or {
          captured_err = err
          yield ""fallback_val""
        }
        return val2
      }

      fn run_test {
        res_ok = check_ok()
        res_fail = check_fail()
      }
      run_test()
    ";

    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal("success_val", env.Get("res_ok"));
    Assert.Equal("fallback_val", env.Get("res_fail"));
    Assert.Equal("error_val", env.Get("captured_err"));
  }

  [Fact]
  public void TestCheckerEnforcesSafetyRules() {
    // 1. Using '?' outside a function
    var code1 = @"
      val val = Result::Success(100)?
    ";
    Assert.ThrowsAny<Exception>(() => {
      PinoTestRunner.Execute(code1, ExecutionEngine.TreeWalk);
    });

    // 2. Using '?' in a function with incompatible return type
    var code2 = @"
      fn test_func() int {
        val val = Result::Success(100)?
        return val
      }
      test_func()
    ";
    Assert.ThrowsAny<Exception>(() => {
      PinoTestRunner.Execute(code2, ExecutionEngine.TreeWalk);
    });

    // 3. Using 'yield' outside of recovery block
    var code3 = @"
      fn test_func() int {
        yield 100
        return 100
      }
      test_func()
    ";
    Assert.ThrowsAny<Exception>(() => {
      PinoTestRunner.Execute(code3, ExecutionEngine.TreeWalk);
    });
  }
}
