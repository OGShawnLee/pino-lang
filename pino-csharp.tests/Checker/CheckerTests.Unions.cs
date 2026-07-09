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

  [Fact]
  public void TestUnionOrOperatorSuccess() {
    var input = @"
      val res = Result::Success(10)
      val value = res or {
        return 20
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestUnionOrOperatorFailure() {
    var input = @"
      val res = Result::Failure(10)
      val value = res or {
        yield 20
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestUnionOrOperatorFailureErrCapturing() {
    var input = @"
      fn no_good() Result[string, string] {
        return Result::Failure(""Something went wrong"")
      }

      val res = no_good() or {
        println(err)
        yield ""fallback""
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestUnionBubbleResultOperator() {
    var input = @"
      fn divide(a int, b int) Result[int, string] {
        if b == 0 {
          return Result::Failure(""Cannot divide by zero"")
        }
        return Result::Success(a / b)
      }

      fn main() Result[int, string] {
        val result = divide(10, 0)?
        return Result::Success(result)
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestUnionBubbleOptionOperator() {
    var input = @"
      fn get_val(flag bool) Option[int] {
        if flag {
          return Option::Some(10)
        }
        return Option::None
      }

      fn process() Option[int] {
        val x = get_val(true)?
        return Option::Some(x + 5)
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestUnionBubbleMismatchingErrorTypes() {
    var input = @"
      fn get_str_err() Result[int, string] {
        return Result::Failure(""error"")
      }

      # Function expects error of type int, but get_str_err returns error of type string.
      fn process() Result[int, int] {
        val x = get_str_err()?
        return Result::Success(x)
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestUnionBubbleMismatchingErrorTypesComplex() {
    var input = @"
      fn divide(a int, b int) Result[int, string] {
        if b == 0 {
          return Result::Failure(""Division by zero"")
        }

        return Result::Success(a / b)
      }

      fn another_result() Result[string, int] {
        return Result::Failure(12)
      }

      fn use_divide(a int, b int) Result[int, string] {
        val res = divide(a, b)?
        another_result()?
        return Result::Success(res)
      }

      fn main() Result[int, string] {
        val n = use_divide(10, 0)?
        println(n)
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestUnionBubbleOptionResultMismatch() {
    var input = @"
      fn get_res() Result[int, string] {
        return Result::Success(10)
      }

      # Function expects Option[int], but get_res is a Result.
      fn process() Option[int] {
        val x = get_res()?
        return Option::Some(x)
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestUnionYieldMismatchingType() {
    var input = @"
      fn get_res() Result[int, string] {
        return Result::Success(10)
      }

      fn process() int {
        # Recovery block yields string, but expected success type of get_res is int.
        val val = get_res() or {
          yield ""not an int""
        }
        return val
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestUnionIsExpressionChecking() {
    var input = @"
      @generic[T]
      union RemoteData {
        Loading
        Success(T)
        Failure(string)
      }

      val state = RemoteData::Success(42)

      # 1. Simple check
      val a = state is RemoteData::Loading
      # 2. Negated check
      val b = state is not RemoteData::Loading
      # 3. Explicit generic check
      val c = state is RemoteData[int]::Success

      # 4. Pattern bindings scoping
      if state is RemoteData[int]::Success(number) {
        val y = number + 10
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestUnionIsExpressionErrors() {
    // 1. Non-existent variant
    var input1 = @"
      val state = Option::Some(10)
      val check = state is Option::Success
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input1));

    // 2. Left-hand side is not a union or enum type
    var input2 = @"
      val x = 12
      val check = x is Option::Some
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input2));
  }
}
