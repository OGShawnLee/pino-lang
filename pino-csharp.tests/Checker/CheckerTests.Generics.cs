using Xunit;
using System;
using Pino;

namespace pino_csharp.tests;

public partial class CheckerTests {
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

  [Fact]
  public void TestGenericMapValueTypeMismatchThrows() {
    var input = @"
      struct Document {
        name string
        page_count int
      }

      struct Library {
        catalog map[string, Document]
      }

      val library_invalid = Library {
        catalog: map[string, string] {
          ""001"": ""What""
        }
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestGenericParameterNameConflictThrows() {
    var input = @"
      struct Document {
        name string
      }

      struct Library[Document] {
        catalog map[string, Document]
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }
}
