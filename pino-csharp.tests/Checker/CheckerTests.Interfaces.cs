using Xunit;
using System;
using Pino;

namespace pino_csharp.tests;

public partial class CheckerTests {
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
  public void TestAccessOnMissingFieldThrows() {
    var input = @"
      interface Person {
        name string
      }

      fn print_person(p Person) {
        println(p:age)
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestAccessOnMissingMethodThrows() {
    var input = @"
      interface Person {
        name string
      }

      fn print_person(p Person) {
        p:print()
      }
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestMapInterfaceCompatibilityPasses() {
    var input = @"
      interface Greeter {
        fn greet(name string)
      }
      
      struct User {
        fn greet(name string) {
          println(""Hello, "" + name)
        }
      }
      
      struct Registry {
        clients map[string, Greeter]
      }
      
      val u = User {}
      val reg = Registry {
        clients: map[string, User] {
          ""client1"": u
        }
      }
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestInterfacePropertiesCompatibilityPasses() {
    var input = @"
      interface HasDetails {
        name string
        age int
        fn summary() string
      }

      struct Person {
        name string
        age int
        fn summary() string {
          return ""$name $age""
        }
      }

      fn print_details(h HasDetails) {
        println(h:name)
      }

      val p = Person { name: ""Shawn"", age: 25 }
      print_details(p)
    ";
    CheckCode(input);
  }

  [Fact]
  public void TestInterfacePropertiesMissingFieldThrows() {
    var input = @"
      interface HasDetails {
        name string
        age int
      }

      struct Person {
        name string
      }

      fn print_details(h HasDetails) {}
      val p = Person { name: ""Shawn"" }
      print_details(p)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestInterfacePropertiesTypeMismatchThrows() {
    var input = @"
      interface HasDetails {
        name string
        age int
      }

      struct Person {
        name string
        age string
      }

      fn print_details(h HasDetails) {}
      val p = Person { name: ""Shawn"", age: ""twenty"" }
      print_details(p)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }

  [Fact]
  public void TestGenericStructMethodInferenceWithInterfaceCompatibilityThrows() {
    var input = @"
      interface DocumentShape {
        name string
        page_count int
        fn read()
      }

      struct Document {
        name string
        page_count int
      }

      struct Library[Doc] {
        catalog map[string, Doc]

        fn get_book_by_key(key string) {
          return catalog[key]
        }

        static fn reading(doc DocumentShape) {
          doc:read()
        }
      }

      val library = Library {
        catalog: map[string, Document] {
          ""001"": Document { name: ""Pino Programming"", page_count: 300 }
        }
      }
      val element = library:get_book_by_key(""001"")
      Library::reading(element)
    ";
    Assert.ThrowsAny<Exception>(() => CheckCode(input));
  }
}
