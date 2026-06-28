using Xunit;
using System;
using Pino;

namespace pino_csharp.tests.Execution;

public class MatchExpressionTests {
  [Fact]
  public void TestMatchExpressionArrowSyntax() {
    var code = @"
      enum Planet {
        Mercury
        Venus
        Earth
        Mars
      }

      var res1 = 0.0
      var res2 = 0.0

      fn test_match(p Planet) float {
        return match p {
          when Planet::Mercury => 3.7
          when Planet::Venus => 8.87
          when Planet::Earth => 9.81
          when Planet::Mars => 3.71
        }
      }

      fn run_test {
        res1 = test_match(Planet::Earth)
        res2 = test_match(Planet::Mars)
      }
      run_test()
    ";

    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(9.81, env.Get("res1"));
    Assert.Equal(3.71, env.Get("res2"));
  }

  [Fact]
  public void TestMatchExpressionBlockYieldSyntax() {
    var code = @"
      enum Planet {
        Mercury
        Venus
        Earth
        Mars
      }

      var res1 = 0.0
      var res2 = 0.0

      fn test_match_block(p Planet) float {
        return match p {
          when Planet::Earth {
            val factor = 1.0
            yield 9.81 * factor
          }
          when Planet::Mars {
            val factor = 2.0
            yield 3.71 * factor
          }
          else {
            yield 0.0
          }
        }
      }

      fn run_test {
        res1 = test_match_block(Planet::Earth)
        res2 = test_match_block(Planet::Mars)
      }
      run_test()
    ";

    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(9.81, env.Get("res1"));
    Assert.Equal(7.42, env.Get("res2"));
  }

  [Fact]
  public void TestMatchExpressionCheckerTypeMismatch() {
    var code = @"
      enum Planet {
        Mercury
        Venus
      }

      fn test_mismatch(p Planet) float {
        return match p {
          when Planet::Mercury => 3.7
          when Planet::Venus => ""not a float""
        }
      }
    ";

    Assert.ThrowsAny<Exception>(() => {
      PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    });
  }

  [Fact]
  public void TestMatchExpressionCheckerExhaustiveness() {
    var code = @"
      enum Planet {
        Mercury
        Venus
        Earth
      }

      fn test_incomplete(p Planet) float {
        return match p {
          when Planet::Mercury => 3.7
          when Planet::Venus => 8.87
        }
      }
    ";

    Assert.ThrowsAny<Exception>(() => {
      PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    });
  }

  [Fact]
  public void TestMatchStatementStillWorks() {
    var code = @"
      enum Planet {
        Mercury
        Venus
      }

      var matched = false

      fn test_stmt(p Planet) {
        match p {
          when Planet::Mercury {
            matched = true
          }
          when Planet::Venus {
            matched = false
          }
        }
      }

      test_stmt(Planet::Mercury)
    ";

    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.True((bool)env.Get("matched")!);
  }
}
