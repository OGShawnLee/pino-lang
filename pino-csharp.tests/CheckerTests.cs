using Xunit;
using System;
using Pino;

namespace pino_csharp.tests;

public class CheckerTests {
  private void CheckCode(string source) {
    var program = Parser.ParseProgramString(source);
    var checker = new Checker();
    checker.Check(program);
  }

  [Fact]
  public void TestTypeCheckerValidInterface() {
    var input = @"
      interface Greeter {
        fn greet(name string)
      }
      
      struct User {
        fn greet(name string) {
          println(""Hello, "" + name)
        }
      }
      
      fn run_greet(g Greeter) {
        g:greet(""Shawn"")
      }
      
      val u = User {}
      run_greet(u)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestTypeCheckerInvalidInterfaceThrows() {
    var input = @"
      interface Greeter {
        fn greet(name string)
      }
      
      struct User {
        fn other() {}
      }
      
      fn run_greet(g Greeter) {}
      
      val u = User {}
      run_greet(u)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestTypeCheckerVectorMapInferenceError() {
    var input = @"
      val list = []int { len: 3, init: it * 3 }
      val list_str = list:map(""$it is a string"")

      fn print_list(list []int) {}

      print_list(list_str)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
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
  public void TestStructEmbeddingChecking() {
    var input = @"
      struct Shape {
        x int
        y int
      }

      struct Circle {
        Shape
        radius int
      }

      val c = Circle {
        x: 10,
        y: 20,
        radius: 5
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestStaticMethodTypeCheckingErrors() {
    // 1. Trying to access 'this' from a static method should fail type check
    var inputInvalidThis = @"
      struct BadStruct {
        x int
        static fn bad() int {
          return this:x
        }
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(inputInvalidThis));

    // 2. Trying to access instance field directly from a static method should fail
    var inputInvalidField = @"
      struct BadStruct2 {
        x int
        static fn bad() int {
          return x
        }
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(inputInvalidField));

    // 3. Trying to call static method via instance member access ':' should fail
    var inputInvalidInstanceCall = @"
      struct Helper {
        static fn helper_fn() int { return 1 }
      }
      val h = Helper {}
      val res = h:helper_fn()
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(inputInvalidInstanceCall));

    // 4. Trying to call instance method via static member access '::' should fail
    var inputInvalidStaticCall = @"
      struct Helper2 {
        fn helper_fn() int { return 1 }
      }
      val res = Helper2::helper_fn()
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(inputInvalidStaticCall));
  }

  [Fact]
  public void TestMethodCallOnMethodTypedField() {
    var input = @"
      struct Incrementer {
        increment fn(int) int
      }

      val increment = fn(n int) => n + 1
      val i = Incrementer {
       increment: increment
      }
      i:increment(12)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestTypeCheckerMethodTypedFieldIncompatibleAssignmentThrows() {
    var input = @"
      struct Incrementer {
        increment fn(int) int
      }

      val increment = fn(n string) => n + 1
      val i = Incrementer {
       increment: increment
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestTypeCheckerMethodTypedFieldArgTypeMismatchThrows() {
    var input = @"
      struct Incrementer {
        increment fn(int) int
      }

      val increment = fn(n int) => n + 1
      val i = Incrementer {
       increment: increment
      }

      i:increment(""not an int"" )
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestTypeCheckerMethodTypedFieldArgCountMismatchThrows() {
    var input = @"
      struct Incrementer {
        increment fn(int) int
      }

      val increment = fn(n int) => n + 1
      val i = Incrementer {
       increment: increment
      }
      i:increment(12, 34)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestTypeCheckerNonFunctionFieldCallThrows() {
    var input = @"
      struct Calculator {
        count int
      }
      val c = Calculator { count: 0 }
      c:count(10)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestMethodCallOnMethodTypedStaticFieldThrows() {
    var input = @"
      struct Calculator {
        static fn increment(n int) int {
          return n + 1
        }
      }

      struct Incrementer {
        increment fn(int) int
      }

      val i = Incrementer {
       increment: Calculator::increment
      }

      i:increment(12)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestTypeCheckerLambdaAsStructFieldAssignmentPasses() {
    var input = @"
      struct Incrementer {
        increment fn(int) int
      }

      val i = Incrementer {
        increment: fn (n int) => n + 1
      }
    ";
    CheckCode(input);
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
  public void TestMapTypeCheckerSemanticValidation() {
    // 1. Valid types pass
    var validCode = @"
      val m = map[string, int] { ""a"": 1, ""b"": 2 }
    ";
    CheckCode(validCode);

    // 2. Invalid key type throws
    var invalidKeyCode = @"
      val m = map[string, int] { 123: 1 }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(invalidKeyCode));

    // 3. Invalid value type throws
    var invalidValCode = @"
      val m = map[string, int] { ""a"": ""hello"" }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(invalidValCode));
  }

  [Fact]
  public void TestEnumDefinitionAndTypeChecking() {
    // 1. Valid enum declaration
    var code = @"
      enum Status { Active, Pending, Closed }
      val current = Status::Active
    ";
    CheckCode(code);

    // 2. Type Checker throws when passing incompatible type to function parameter
    var badCode1 = @"
      enum Status { Active, Pending, Closed }
      fn check_status(s Status) {}
      check_status(42)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(badCode1));

    // 3. Type Checker throws when assigning a different enum type to a struct field
    var badCode2 = @"
      enum Status { Active, Closed }
      enum Mode { Read, Write }
      struct Config {
        status Status
      }
      val c = Config { status: Mode::Read }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(badCode2));
  }

  [Fact]
  public void TestRuneTypeChecker() {
    // 1. Valid declarations, arithmetic, and parameter passing
    var code1 = @"
      val a = 'a'
      val next = a + 1
      val dist = 'b' - a
      val prev = 'b' - 1

      struct Foo {
        r rune
      }
      val f = Foo { r: 'x' }

      fn takes_rune(v rune) {}
      takes_rune('y')
    ";
    CheckCode(code1);

    // 2. Passing int to rune parameter throws
    var badCode1 = @"
      fn takes_rune(v rune) {}
      takes_rune(97)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(badCode1));

    // 3. Passing rune to int parameter throws
    var badCode2 = @"
      fn takes_int(v int) {}
      takes_int('a')
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(badCode2));

    // 4. Initializing struct rune field with int throws
    var badCode3 = @"
      struct Foo {
        r rune
      }
      val f = Foo { r: 97 }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(badCode3));
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
  public void TestStructGenericsExplicitInstantiation() {
    var input = @"
      struct Pair[Key Value] {
        key Key
        value Value
      }
      val p = Pair[string, int] { key: ""Hello"", value: 42 }
      fn expect_string(s string) {}
      fn expect_int(i int) {}
      expect_string(p:key)
      expect_int(p:value)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestStructGenericsImplicitInstantiation() {
    var input = @"
      struct Pair[Key Value] {
        key Key
        value Value
      }
      val p = Pair { key: ""Hello"", value: 42 }
      fn expect_string(s string) {}
      fn expect_int(i int) {}
      expect_string(p:key)
      expect_int(p:value)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestStructGenericsIncompatibleInitializationTypes() {
    var input = @"
      struct Pair[Key Value] {
        key Key
        value Value
      }
      val p = Pair[string, int] { key: ""Hello"", value: ""World"" }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestStructGenericsIncompatibleTypesThrows() {
    var input = @"
      struct Pair[Key Value] {
        key Key
        value Value
      }
      val p = Pair[string, int] { key: ""Hello"", value: 42 }
      fn expect_string(s string) {}
      expect_string(p:value)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestStructGenericsInconsistentInferenceThrows() {
    var input = @"
      struct Quad[T] {
        a T
        b T
      }
      val q = Quad { a: ""Hello"", b: 42 }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }
}
