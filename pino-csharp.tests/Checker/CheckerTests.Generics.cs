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
      struct Book {
        name string
      }
      val lib = Library[Book] {
        catalog: map[string, Book] {}
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestStructDecoratorSyntaxAndBoundsInvalid() {
    var input = @"
      interface DocumentShape {
        name string
      }
      @generic[Doc is DocumentShape]
      struct Library {
        catalog map[string, Doc]
      }
      struct User {
        age int
      }
      val lib = Library[User] {
        catalog: map[string, User] {}
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestStructDecoratorSyntaxAndBoundsPrimitiveInvalid() {
    var input = @"
      interface DocumentShape {
        name string
      }
      @generic[Doc is DocumentShape]
      struct Library {
        catalog map[string, Doc]
      }
      val lib = Library[int] {
        catalog: map[string, int] {}
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestFunctionGenericsBounds() {
    var input = @"
      interface DocumentShape {
        name string
      }
      @generic[Doc is DocumentShape]
      fn print_doc(d Doc) string {
        return d:name
      }
      struct Book {
        name string
      }
      val b = Book { name: ""The Great Gatsby"" }
      val name = print_doc[Book](b)
      fn expect_string(s string) {}
      expect_string(name)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestFunctionGenericsBoundsInvalid() {
    var input = @"
      interface DocumentShape {
        name string
      }
      @generic[Doc is DocumentShape]
      fn print_doc(d Doc) string {
        return d:name
      }
      struct User {
        age int
      }
      val u = User { age: 25 }
      print_doc[User](u)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestMethodGenericsBounds() {
    var input = @"
      interface DocumentShape {
        name string
      }
      struct Printer {
        @generic[Doc is DocumentShape]
        fn print_doc(d Doc) string {
          return d:name
        }
      }
      struct Book {
        name string
      }
      val p = Printer {}
      val b = Book { name: ""Don Quixote"" }
      val name = p:print_doc[Book](b)
      fn expect_string(s string) {}
      expect_string(name)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestMethodGenericsBoundsInvalid() {
    var input = @"
      interface DocumentShape {
        name string
      }
      struct Printer {
        @generic[Doc is DocumentShape]
        fn print_doc(d Doc) string {
          return d:name
        }
      }
      struct User {
        age int
      }
      val p = Printer {}
      val u = User { age: 30 }
      p:print_doc[User](u)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestGenericInterfaces() {
    var input = @"
      @generic[T]
      interface Repository {
        fn find_by_id(id string) T
        fn save(entity T)
      }
      struct Person {
        id string
        name string
      }
      struct SQLPersonRepository {
        fn find_by_id(id string) Person {
          return Person { id: id, name: ""John"" }
        }
        fn save(entity Person) {}
      }
      fn check_repo(repo Repository[Person]) {
        val p = repo:find_by_id(""123"")
        fn expect_person(per Person) {}
        expect_person(p)
      }
      val r = SQLPersonRepository {}
      check_repo(r)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestGenericInterfacesInvalid() {
    var input = @"
      @generic[T]
      interface Repository {
        fn find_by_id(id string) T
        fn save(entity T)
      }
      struct Person {
        id string
        name string
      }
      struct SQLPersonRepository {
        fn find_by_id(id string) string {
          return ""Not a Person""
        }
        fn save(entity Person) {}
      }
      fn check_repo(repo Repository[Person]) {}
      val r = SQLPersonRepository {}
      check_repo(r)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestGenericConstraintUsingGenericInterface() {
    var input = @"
      @generic[T]
      interface Repository {
        fn find_by_id(id string) T
        fn save(entity T)
      }
      struct Person {
        id string
        name string
      }
      struct SQLPersonRepository {
        fn find_by_id(id string) Person {
          return Person { id: id, name: ""John"" }
        }
        fn save(entity Person) {}
      }
      @generic[Repo is Repository[Person]]
      fn process(r Repo) Person {
        return r:find_by_id(""123"")
      }
      val repo = SQLPersonRepository {}
      val p = process[SQLPersonRepository](repo)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestGenericConstraintUsingGenericInterfaceInvalid() {
    var input = @"
      @generic[T]
      interface Repository {
        fn find_by_id(id string) T
        fn save(entity T)
      }
      struct Person {
        id string
        name string
      }
      struct IncompatibleRepository {
        fn find_by_id(id string) string {
          return ""Not a Person""
        }
      }
      @generic[Repo is Repository[Person]]
      fn process(r Repo) Person {
        return r:find_by_id(""123"")
      }
      val repo = IncompatibleRepository {}
      process[IncompatibleRepository](repo)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }
}
