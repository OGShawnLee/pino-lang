using Xunit;
using System;
using Pino;

namespace pino_csharp.tests;

public partial class CheckerTests {
  [Fact]
  public void TestUnionConstructorTypes() {
    var input = @"
      union Entity {
        Person(string)
      }
      fn check {
        val constructor = Entity::Person
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestUnionConstructorArgumentTypeMismatch() {
    var input = @"
      union Entity {
        Person(string)
      }
      fn check {
        val x = Entity::Person(12)
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestGenericUnionMonomorphizationSuccess() {
    var input = @"
      @generic[T]
      union Option {
        Some(T)
        None
      }

      val x = Option::Some(42)
      val y = Option::Some(""hello"")

      fn expect_int(opt Option[int]) {}
      fn expect_str(opt Option[string]) {}

      expect_int(x)
      expect_str(y)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestGenericUnionMonomorphizationMismatchThrows() {
    var input = @"
      @generic[T]
      union Option {
        Some(T)
        None
      }

      val x = Option::Some(""hello"")
      
      fn expect_int(opt Option[int]) {}
      expect_int(x) 
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }
}
