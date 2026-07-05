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

  [Fact]
  public void TestNamespacedEntityVisibility() {
    // 1. Set up private struct Reader and private interface IReader in module Lexer
    var lexerSource = @"
      module Lexer
      struct Reader {
        fn current() Result[string, string] {
          return Result::Success(""Value"")
        }
      }
      interface IReader {
        fn current() Result[string, string]
      }
      pub struct BadReader {}
    ";
    var lexerProgram = Parser.ParseProgramString(lexerSource);
    var lexerChecker = new Checker { IsModule = true };
    lexerChecker.Check(lexerProgram);

    // 2. Main program referencing private Lexer::Reader {}
    var sourceStruct = @"
      import Lexer
      fn main {
        val r = Lexer::Reader {}
      }
    ";
    var programStruct = Parser.ParseProgramString(sourceStruct);
    var checkerStruct = new Checker();
    checkerStruct._moduleCheckers["Lexer"] = lexerChecker;

    var exStruct = Assert.ThrowsAny<Exception>(() => checkerStruct.Check(programStruct));
    Assert.Contains("Struct 'Lexer::Reader' is not public", exStruct.Message);

    // 3. Main program referencing private Lexer::IReader as parameter type (must call it to check compatibility)
    var sourceInterface = @"
      import Lexer
      fn print(reader Lexer::IReader) {}
      fn main {
        print(Lexer::BadReader {})
      }
    ";
    var programInterface = Parser.ParseProgramString(sourceInterface);
    var checkerInterface = new Checker();
    checkerInterface._moduleCheckers["Lexer"] = lexerChecker;

    var exInterface = Assert.ThrowsAny<Exception>(() => checkerInterface.Check(programInterface));
    Assert.Contains("Interface 'Lexer::IReader' is not public", exInterface.Message);
  }

  [Fact]
  public void TestNamespacedEntityNotDefined() {
    var lexerSource = @"
      module Lexer
      pub struct BadReader {}
    ";
    var lexerProgram = Parser.ParseProgramString(lexerSource);
    var lexerChecker = new Checker { IsModule = true };
    lexerChecker.Check(lexerProgram);

    var source = @"
      import Lexer
      fn main {
        val r = Lexer::MissingStruct {}
      }
    ";
    var program = Parser.ParseProgramString(source);
    var checker = new Checker();
    checker._moduleCheckers["Lexer"] = lexerChecker;

    var ex = Assert.ThrowsAny<Exception>(() => checker.Check(program));
    Assert.Contains("Struct 'MissingStruct' is not defined in module 'Lexer'", ex.Message);
  }

  [Fact]
  public void TestTypeCheckerTupleValid() {
    var source = @"
      fn get_coords() (x int, y int) {
        return (x: 10, y: 20)
      }
      val (x, y) = get_coords()
    ";
    CheckCode(source);
  }

  [Fact]
  public void TestTypeCheckerTupleInvalidReturn() {
    var source = @"
      fn get_coords() (x int, y int) {
        return (x: 10, y: ""not-an-int"")
      }
    ";
    var ex = Assert.ThrowsAny<Exception>(() => CheckCode(source));
    Assert.Contains("declared return type", ex.Message);
  }

  [Fact]
  public void TestTypeCheckerTupleIllegalContext() {
    var source = @"
      val t = (x: 1, y: 2)
    ";
    var ex = Assert.ThrowsAny<Exception>(() => CheckCode(source));
    Assert.Contains("Tuple literals can only be used as a return value", ex.Message);
  }

  [Fact]
  public void TestTypeCheckerTupleDuplicateLabelsReturn() {
    // Duplicate labels in function declaration (checked at parser or checker, we raise in parser or checker. Our code does not allow parsing duplicate labels or check in checker. Let's test duplicate labels in tuple literal or declaration).
    var source = @"
      fn get_coords() (x int, y int) {
        return (x: 10, x: 20)
      }
    ";
    var ex = Assert.ThrowsAny<Exception>(() => CheckCode(source));
    Assert.Contains("Duplicate label 'x' in tuple literal.", ex.Message);
  }

  [Fact]
  public void TestTypeCheckerTupleDestructuringMismatch() {
    var source = @"
      fn get_coords() (x int, y int) {
        return (x: 10, y: 20)
      }
      val (z) = get_coords()
    ";
    var ex = Assert.ThrowsAny<Exception>(() => CheckCode(source));
    Assert.Contains("Field 'z' does not exist in tuple type", ex.Message);
  }

  [Fact]
  public void TestTypeCheckerTupleDestructuringDuplicateVariables() {
    var source = @"
      fn get_coords() (x int, y int) {
        return (x: 10, y: 20)
      }
      val (x: a, y: a) = get_coords()
    ";
    var ex = Assert.ThrowsAny<Exception>(() => CheckCode(source));
    Assert.Contains("Duplicate variable name 'a' in destructuring.", ex.Message);
  }

  [Fact]
  public void TestTypeCheckerTupleReturningFunction() {
    var source = @"
      fn get_factory() fn() (x int, y int) {
        fn generate() (x int, y int) {
          return (x: 1, y: 2)
        }
        return generate
      }
      val factory = get_factory()
      val (x, y) = factory()
    ";
    CheckCode(source);
  }

  [Fact]
  public void TestTypeCheckerTupleReturningFunctionInvalid() {
    var source = @"
      fn get_factory_invalid() fn() (x int, y int) {
        fn generate() (a int, b int) {
          return (a: 1, b: 2)
        }
        return generate
      }
    ";
    var ex = Assert.ThrowsAny<Exception>(() => CheckCode(source));
    Assert.Contains("declared return type 'fn() (x:int,y:int)', but returned 'fn() (a:int,b:int)'", ex.Message);
  }

  [Fact]
  public void TestTypeCheckerTupleOrderIndependent() {
    var source = @"
      fn get_coords() (x int, y int) {
        return (y: 20, x: 10)
      }
      fn get_factory() fn() (x int, y int) {
        fn generate() (y int, x int) {
          return (y: 2, x: 1)
        }
        return generate
      }
      val factory = get_factory()
      val (y: b, x: a) = factory()
    ";
    CheckCode(source);
  }
}
