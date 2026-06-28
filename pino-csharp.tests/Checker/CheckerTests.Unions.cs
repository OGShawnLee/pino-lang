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

  [Fact]
  public void TestUnionMatchExhaustiveSuccess() {
    var input = @"
      union Entity {
        Person(string)
        Ghost
      }
      fn check(hero Entity) {
        match hero {
          when Entity::Person(name) {}
          when Entity::Ghost {}
        }
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestUnionMatchMissingVariantThrows() {
    var input = @"
      union Entity {
        Person(string)
        Ghost
      }
      fn check(hero Entity) {
        match hero {
          when Entity::Person(name) {}
        }
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestUnionMatchWithElseSuccess() {
    var input = @"
      union Entity {
        Person(string)
        Ghost
      }
      fn check(hero Entity) {
        match hero {
          when Entity::Person(name) {}
          else {}
        }
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestUnionGenericAsParameter() {
    var input = @"
      @generic[T]
      union Container {
        Value(T)
      }
      fn use_container(c Container[int]) {}
      use_container(Container::Value(42))
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestUnionGenericAsParameterThrows() {
    var input = @"
      @generic[T]
      union Container {
        Value(T)
      }
      fn use_container(c Container[int]) {}
      use_container(Container::Value(""not an int""))
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestNativePreludeOptionAndResult() {
    var input = @"
      fn check_option(opt Option[int]) bool {
        match opt {
          when Option::Some(value) { return true }
          when Option::None { return false }
        }
      }

      fn check_result(res Result[string, int]) string {
        match res {
          when Result::Success(value) { return value }
          when Result::Failure(err) { return ""Error"" }
        }
      }

      val opt_val = Option::Some(42)

      val has_val = check_option(opt_val)
      val message = check_result(Result::Success(""OK""))
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestNativeResult() {
    var input = @"
      fn divide(a int, b int) Result[int, string] {
        if b == 0 {
          return Result::Failure(""Cannot divide by zero"")
        }
        return Result::Success(a / b)
      }

      val result = divide(10, 2)

      match result {
        when Result::Success(value) { 
          println(""Result: $value"") 
        }
        when Result::Failure(err) { 
          println(""Error: $err"") 
        }
      }
    ";
    CheckCode(input);
  }
}
