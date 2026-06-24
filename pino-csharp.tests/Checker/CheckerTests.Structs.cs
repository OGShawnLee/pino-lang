using Xunit;
using System;
using Pino;

namespace pino_csharp.tests;

public partial class CheckerTests {
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
}
