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

  [Fact]
  public void TestFunctionGenericsExplicitCall() {
    var input = @"
      @generic[T, U]
      fn map(list []T, transform fn(T) U) []U {
        return []U
      }
      val numbers = [1, 2, 3]
      val strings = map[int, string](numbers, fn(n int) => ""test"")
      fn expect_strings(s []string) {}
      expect_strings(strings)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestFunctionGenericsImplicitCall() {
    var input = @"
      @generic[T, U]
      fn map(list []T, transform fn(T) U) []U {
        return []U
      }
      val numbers = [1, 2, 3]
      val strings = map(numbers, fn(n int) => ""test"")
      fn expect_strings(s []string) {}
      expect_strings(strings)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestStructMethodGenerics() {
    var input = @"
      struct Reader {
        @generic[T]
        static fn read(v T) T {
          return v
        }
      }
      val res = Reader::read[int](42)
      fn expect_int(n int) {}
      expect_int(res)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestStructDecoratorSyntaxAndBounds() {
    var input = @"
      interface DocumentShape {
        name string
      }
      @generic[Doc is DocumentShape]
      struct Library {
        catalog map[string, Doc]
      }
    ";
    CheckCode(input);
  }
}
