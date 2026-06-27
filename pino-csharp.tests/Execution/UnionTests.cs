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
}
