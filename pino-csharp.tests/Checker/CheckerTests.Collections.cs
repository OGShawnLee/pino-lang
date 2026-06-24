using Xunit;
using System;
using Pino;

namespace pino_csharp.tests;

public partial class CheckerTests {
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
}
