using Xunit;
using System;
using Pino;

namespace pino_csharp.tests;

public partial class CheckerTests {
  private void CheckCode(string source) {
    var program = Parser.ParseProgramString(source);
    var checker = new Checker();
    checker.Check(program);
  }

  [Fact]
  public void TestTypeCheckerFunctionSignatureScopingAndCompatibility() {
    var input = @"
      val list = []int { len: 3, init: it * 3 }
      
      fn print_list(list []int, on_each fn (int)) {
        list:each(on_each)
      }
      
      print_list(list, it * 2)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestTypeCheckerDeclaredReturnType() {
    var input = @"
      struct Product {
        price int

        fn get_double_price() int {
          return price * 2
        }
      }

      fn another_get_double(n int) int {
        return n * 2
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestTypeCheckerDeclaredReturnTypeInvalid() {
    var input = @"
      fn get_name() string {
        return 42
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestTypeCheckerRecursiveFunctionExplicitTypePasses() {
    var input = @"
      fn fib(n int) int {
        if n <= 1 { return n }
        return fib(n - 1) + fib(n - 2)
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestTypeCheckerRecursiveFunctionNoTypeThrows() {
    var input = @"
      fn fib(n int) {
        if n <= 1 { return n }
        return fib(n - 1) + fib(n - 2)
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestTypeCheckerMutuallyRecursiveExplicitTypePasses() {
    var input = @"
      fn is_even(n int) bool {
        if n == 0 { return true }
        return is_odd(n - 1)
      }
      fn is_odd(n int) bool {
        if n == 0 { return false }
        return is_even(n - 1)
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestProgramModeTopLevelRestrictions() {
    // 1. Valid program: main is defined, no side effects, only val globals and declarations
    var valid = @"
      val global_val = 100
      fn main() {
        println(""Hello"")
      }
    ";
    CheckCode(valid);

    // 2. Invalid: var global declared when main is defined
    var badGlobalVar = @"
      var global_var = 100
      fn main() {}
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(badGlobalVar));

    // 3. Invalid: loose top-level function call when main is defined
    var badTopLevelCall = @"
      println(""Oops"")
      fn main() {}
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(badTopLevelCall));

    // 4. Invalid: loose top-level loop when main is defined
    var badTopLevelLoop = @"
      for 5 {}
      fn main() {}
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(badTopLevelLoop));
  }

  [Fact]
  public void TestModuleMainFunctionRestriction() {
    var source = @"
      fn main() {
        println(""I am a module"")
      }
    ";
    var program = Parser.ParseProgramString(source);
    var checker = new Checker { IsModule = true };
    Assert.ThrowsAny<Exception>(() => checker.Check(program));
  }
}
