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

  [Fact]
  public void TestGenericResultDisambiguation() {
    var code = @"
      var res = """"
      fn run_test {
        fn divide(a int, b int) Result[int, string] {
          if b == 0 {
            return Result::Failure(""Cannot divide by zero"")
          }
          return Result::Success(a / b)
        }

        match divide(10, 2) {
          when Result::Success(v) { res = ""Success:"" + str(v) }
          when Result::Failure(e) { res = ""Failure:"" + e }
        }
      }
      run_test()
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal("Success:5", env.Get("res"));
  }

  [Fact]
  public void TestMultiGenericUnionExpectedType() {
    var code = @"
      @generic[A, B, C, D]
      union Tuple4 {
        First(A)
        Second(B)
        Third(C)
        Fourth(D)
      }

      var res1 = """"
      var res2 = """"
      var res3 = """"
      var res4 = """"

      fn run_test {
        fn get_val(kind int) Tuple4[int, string, bool, float] {
          if kind == 1 {
            return Tuple4::First(42)
          }
          if kind == 2 {
            return Tuple4::Second(""hello"")
          }
          if kind == 3 {
            return Tuple4::Third(true)
          }
          return Tuple4::Fourth(3.14)
        }

        match get_val(1) {
          when Tuple4::First(v) { res1 = ""First:"" + str(v) }
          else { res1 = ""other"" }
        }
        match get_val(2) {
          when Tuple4::Second(v) { res2 = ""Second:"" + v }
          else { res2 = ""other"" }
        }
        match get_val(3) {
          when Tuple4::Third(v) { res3 = ""Third:"" + str(v) }
          else { res3 = ""other"" }
        }
        match get_val(4) {
          when Tuple4::Fourth(v) { res4 = ""Fourth:"" + str(v) }
          else { res4 = ""other"" }
        }
      }
      run_test()
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal("First:42", env.Get("res1"));
    Assert.Equal("Second:hello", env.Get("res2"));
    Assert.Equal("Third:True", env.Get("res3"));
    Assert.Equal("Fourth:3.14", env.Get("res4"));
  }

  [Fact]
  public void TestGenericUnionAssignmentAndArgDisambiguation() {
    var code = @"
      var res1 = """"
      var res2 = """"
      
      fn run_test {
        fn process(r Result[int, string]) string {
          match r {
            when Result::Success(v) { return ""Val:"" + str(v) }
            when Result::Failure(e) { return ""Err:"" + e }
          }
        }

        res1 = process(Result::Success(100))
        res2 = process(Result::Failure(""error_msg""))
      }
      run_test()
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal("Val:100", env.Get("res1"));
    Assert.Equal("Err:error_msg", env.Get("res2"));
  }

  [Fact]
  public void TestGenericRecursiveUnion() {
    var code = @"
      @generic[T]
      union List {
        Cons(T, List[T])
        Nil
      }
      var res = 0
      fn run_test {
        val l = List::Cons(42, List::Nil)
        match l {
          when List::Cons(v, next) { res = v }
          else { res = 0 }
        }
      }
      run_test()
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(42L, env.Get("res"));
  }

  [Fact]
  public void TestUnionIsExpressionExecution() {
    var code = @"
      @generic[T]
      union RemoteData {
        Loading
        Success(T)
        Failure(string)
      }

      var res_simple = false
      var res_not = false
      var res_implicit = false
      var res_bind = 0

      fn run_test {
        val state = RemoteData::Success(42)

        res_simple = state is RemoteData[int]::Success
        res_not = state is not RemoteData[int]::Loading
        res_implicit = state is RemoteData::Success

        if state is RemoteData::Success(value) {
          res_bind = value
        }
      }
      run_test()
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.True((bool)env.Get("res_simple"));
    Assert.True((bool)env.Get("res_not"));
    Assert.True((bool)env.Get("res_implicit"));
    Assert.Equal(42L, env.Get("res_bind"));
  }

  [Fact]
  public void TestGenericUnionIsExpressionExecution() {
    var code = @"
      @generic[Value]
      fn if_present(option Option[Value], default Value) Value {
        if option is Option::Some(value) {
          return value
        }
        return default
      }

      val int_opt = Option::Some(12)
      var res_val = if_present(int_opt, 0)
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(12L, env.Get("res_val"));
  }

  [Fact]
  public void TestUnionIsExpressionWithSubsequentCondition() {
    var code = @"
      fn check_name(name Option[string]) bool {
        if name is Option::Some(n) and n == ""Shawn"" {
          return true
        }
        return false
      }

      val opt1 = Option::Some(""Shawn"")
      val opt2 = Option::Some(""NotShawn"")
      val opt3 = Option::None

      val res1 = check_name(opt1)
      val res2 = check_name(opt2)
      val res3 = check_name(opt3)
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.True((bool)env.Get("res1"));
    Assert.False((bool)env.Get("res2"));
    Assert.False((bool)env.Get("res3"));
  }
}

