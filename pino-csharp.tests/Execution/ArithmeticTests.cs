using Xunit;
using System;
using Pino;

namespace pino_csharp.tests.Execution;

public class ArithmeticTests {
  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestBasicIntegerArithmetic(ExecutionEngine engine) {
    var code = @"
      val x = 2 + 3 * 4
      val y = (2 + 3) * 4
      val z = 10 / 2 - 1
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(14L, env.Get("x"));
    Assert.Equal(20L, env.Get("y"));
    Assert.Equal(4L, env.Get("z"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestRuneArithmeticShift(ExecutionEngine engine) {
    var code = @"
      val a = 'a'
      val b = 'b'
      val next = a + 1
      val prev = b - 1
      val dist = b - a
      val concat = a + b
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(new PinoRune(97), env.Get("a"));
    Assert.Equal(new PinoRune(98), env.Get("b"));
    Assert.Equal(new PinoRune(98), env.Get("next"));
    Assert.Equal(new PinoRune(97), env.Get("prev"));
    Assert.Equal(1L, env.Get("dist"));
    Assert.Equal("ab", env.Get("concat"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestRuneComparisons(ExecutionEngine engine) {
    var code = @"
      val a = 'a'
      val b = 'b'
      val is_eq = a == 'a'
      val is_lt = a < b
      val is_gt = b > a
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(true, env.Get("is_eq"));
    Assert.Equal(true, env.Get("is_lt"));
    Assert.Equal(true, env.Get("is_gt"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestRuneConversionsAndUtilities(ExecutionEngine engine) {
    var code = @"
      val a = 'a'
      val r_type = type(a)
      val r_str = str(a)
      val from_int = rune(97)
      val from_str = rune(""z"")
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal("rune", env.Get("r_type"));
    Assert.Equal("a", env.Get("r_str"));
    Assert.Equal(new PinoRune(97), env.Get("from_int"));
    Assert.Equal(new PinoRune(122), env.Get("from_str"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestBooleanLogic(ExecutionEngine engine) {
    var code = @"
      val t = true
      val f = false
      val and_tt = t and t
      val and_tf = t and f
      val or_tf = t or f
      val or_ff = f or f
      val eq_tf = t == f
      val neq_tf = t != f
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(true, env.Get("and_tt"));
    Assert.Equal(false, env.Get("and_tf"));
    Assert.Equal(true, env.Get("or_tf"));
    Assert.Equal(false, env.Get("or_ff"));
    Assert.Equal(false, env.Get("eq_tf"));
    Assert.Equal(true, env.Get("neq_tf"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestUnaryOperators(ExecutionEngine engine) {
    var code = @"
      val neg_int = -42
      val neg_float = -3.14
      val not_true = not true
      val not_false = not false
      val prec_mul = -2 * 3
      val prec_add = -2 + 3
      val group_neg = -(2 + 3)
      val group_not = not (true and false)
      val not_and_prec = not true and false
    ";
    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(-42L, env.Get("neg_int"));
    Assert.Equal(-3.14, env.Get("neg_float"));
    Assert.Equal(false, env.Get("not_true"));
    Assert.Equal(true, env.Get("not_false"));
    Assert.Equal(-6L, env.Get("prec_mul"));
    Assert.Equal(1L, env.Get("prec_add"));
    Assert.Equal(-5L, env.Get("group_neg"));
    Assert.Equal(true, env.Get("group_not"));
    Assert.Equal(false, env.Get("not_and_prec"));
  }

  [Theory]
  [InlineData(ExecutionEngine.TreeWalk)]
  [InlineData(ExecutionEngine.VM)]
  public void TestBooleanShortCircuit(ExecutionEngine engine) {
    var code = @"
      var called = false

      fn should_not_be_called() {
        called = true
        return true
      }

      val res_and = false and should_not_be_called()
      val res_or = true or should_not_be_called()
    ";

    var env = PinoTestRunner.Execute(code, engine);
    Assert.Equal(false, env.Get("res_and"));
    Assert.Equal(true, env.Get("res_or"));
    Assert.Equal(false, env.Get("called"));
  }

  [Fact]
  public void TestUnionEquality() {
    var code = @"
      union Foo {
        A
        B
      }

      val is_a_equals_a = Foo::A == Foo::A
      val is_a_not_equals_b = Foo::A != Foo::B
      val is_a_equals_b = Foo::A == Foo::B
      val is_a_not_equals_a = Foo::A != Foo::A
    ";

    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(true, env.Get("is_a_equals_a"));
    Assert.Equal(true, env.Get("is_a_not_equals_b"));
    Assert.Equal(false, env.Get("is_a_equals_b"));
    Assert.Equal(false, env.Get("is_a_not_equals_a"));
  }

  [Fact]
  public void TestUnionEqualityResult() {
    var code = @"
      fn divide(a int, b int) Result[int, string] {
        if b == 0 {
          return Result::Failure(""Cannot divide by zero"")
        }

        return Result::Success(a / b)
      }

      val is_a_equals_a = divide(10, 2) == Result::Success(5)
      val is_a_not_equals_b = divide(10, 0) != Result::Success(5)
    ";

    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(true, env.Get("is_a_equals_a"));
    Assert.Equal(true, env.Get("is_a_not_equals_b"));
  }

  [Fact]
  public void TestUnionEqualityWithValues() {
    var code = @"
      union Data {
        Int(int)
        String(string)
      }

      val is_same_variant_same_data = Data::Int(1) == Data::Int(1)
      val is_same_variant_different_data = Data::Int(1) == Data::Int(2)
      val is_different_variant = Data::Int(1) == Data::String(""not-an-int"")
    ";

    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(true, env.Get("is_same_variant_same_data"));
    Assert.Equal(false, env.Get("is_same_variant_different_data"));
    Assert.Equal(false, env.Get("is_different_variant"));
  }

  [Fact]
  public void TestEnumEquality() {
    var code = @"
      enum Zap {
        A
        B
      }

      val is_a_equals_a = Zap::A == Zap::A
      val is_a_not_equals_b = Zap::A != Zap::B
      val is_a_equals_b = Zap::A == Zap::B
      val is_a_not_equals_a = Zap::A != Zap::A
    ";

    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(true, env.Get("is_a_equals_a"));
    Assert.Equal(true, env.Get("is_a_not_equals_b"));
    Assert.Equal(false, env.Get("is_a_equals_b"));
    Assert.Equal(false, env.Get("is_a_not_equals_a"));
  }
}

