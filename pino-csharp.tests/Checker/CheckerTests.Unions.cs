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
}
