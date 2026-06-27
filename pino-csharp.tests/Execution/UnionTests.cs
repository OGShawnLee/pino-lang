using Xunit;
using System;
using Pino;

namespace pino_csharp.tests.Execution;

public class UnionTests {
  [Fact]
  public void TestBasicUnionMatchExecution() {
    var code = @"
      union Entity {
        Person(string)
        Animal(string, string)
        Ghost
      }
      var res = """"
      fn run_test {
        val hero = Entity::Animal(""Togo"", ""Boxer"")
        match hero {
          when Entity::Person(name) { res = ""Person:"" + name }
          when Entity::Animal(name, species) { res = ""Animal:"" + name + "":"" + species }
          when Entity::Ghost { res = ""Ghost"" }
        }
      }
      run_test()
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal("Animal:Togo:Boxer", env.Get("res"));
  }

  [Fact]
  public void TestUnionMatchElseFallbackExecution() {
    var code = @"
      union Entity {
        Person(string)
        Ghost
      }
      var res = """"
      fn run_test {
        val hero = Entity::Ghost
        match hero {
          when Entity::Person(name) { res = ""Person:"" + name }
          else { res = ""ElseFallback"" }
        }
      }
      run_test()
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal("ElseFallback", env.Get("res"));
  }

  [Fact]
  public void TestGenericUnionMatchExecution() {
    var code = @"
      @generic[T]
      union Option {
        Some(T)
        None
      }
      var res1 = """"
      var res2 = """"
      fn run_test {
        val o1 = Option::Some(42)
        val o2 = Option::None
        match o1 {
          when Option::Some(v) { res1 = ""Some:"" + str(v) }
          else { res1 = ""none"" }
        }
        match o2 {
          when Option::Some(v) { res2 = ""Some:"" + str(v) }
          when Option::None { res2 = ""None"" }
        }
      }
      run_test()
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal("Some:42", env.Get("res1"));
    Assert.Equal("None", env.Get("res2"));
  }
}
