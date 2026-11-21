using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerExpressionErrorToleranceTests {
  [Fact]
  public void ErrorTypeAbsorption_A4_OneUndeclaredNameProducesExactlyOneError() {
    Compilation compilation = Compile("""
      val bad = missing_name
      val arithmetic = bad + 1
      val reversed = 1 - bad
      val negated = -bad
      val inverted = not bad
      val logic = bad and true
      val equality = bad == 1
      val comparison = bad < 2
      val membership = bad in [1, 2]
      val reverse_membership = 1 in bad
      val indexed = bad[0]
      val index_by = [1, 2][bad]
      val element = [bad, 1, 2.5]
      val literal_set = set[int] { bad, 2 }
      val literal_map = map[string, int] { "a" -> bad }
      val conditional = if bad then 1 else 2
      val branches = if true then bad else "x"
      val interpolated = "value $bad"
      val widened float = bad
      val str_plus = "a" + bad
      var target = 0
      target = bad
      target += bad
      for 3 {
        val inside = bad * 2
      }
      for x in bad {
        val y = x + 1
      }
      for bad {
      }
      if bad {
        val z = bad
      }
      assert bad
      """);

    // Only the first reference to the undeclared name is an error; everything downstream is ErrorType.
    Diagnostic error = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.UndeclaredIdentifier, error.Code);
    Assert.Contains("missing_name", error.Message);

    foreach (string name in new[] {
      "bad", "arithmetic", "reversed", "negated", "inverted", "indexed", "branches",
    }) {
      Assert.True(Local(compilation, name).Type.IsError, name);
    }

    // Boolean-valued operators stay `bool` even when an operand is erroneous.
    foreach (string name in new[] { "logic", "equality", "comparison", "membership", "reverse_membership" }) {
      Assert.Same(compilation.Types.Bool, Local(compilation, name).Type);
    }

    // An erroneous index still yields the element type of the (valid) vector being indexed.
    Assert.Same(compilation.Types.Int, Local(compilation, "index_by").Type);
    Assert.Same(compilation.Types.String, Local(compilation, "interpolated").Type);
    Assert.Same(compilation.Types.Float, Local(compilation, "widened").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "target").Type);
  }

  [Fact]
  public void ErrorTypeInConditionalsAndAssignments_DoesNotCascadeIntoOtherBindings() {
    Compilation compilation = Compile("""
      val bad = missing_name
      val ok = 1
      val clean = ok + 2
      var f = 1.5
      f = bad
      f = ok
      val mixed = if bad then ok else 2.5
      val unrelated = [ok, 2]
      """);

    Assert.Single(CheckerErrors(compilation));
    Assert.Same(compilation.Types.Int, Local(compilation, "clean").Type);
    Assert.Same(
        compilation.Types.GetVector(compilation.Types.Int),
        Local(compilation, "unrelated").Type);
    // The int-to-float widening of `f = ok` is still recorded even though `f = bad` was erroneous.
    var assignment = All<AssignmentExpressionSyntax>(compilation)[1];
    AssertIntToFloat(compilation, assignment.Value);
  }

  [Theory]
  [InlineData("val x =")]
  [InlineData("val x =\nval y = x + 1")]
  [InlineData("val flag = true\nif flag {")]
  [InlineData("val v = [1, 2")]
  [InlineData("val v = [1, 2\nval w = v")]
  [InlineData("val t = 5\nval u = t +")]
  [InlineData("val s = \"abc\"\nval c = s[")]
  [InlineData("val m = map[string, int] {")]
  [InlineData("val xs = [1, 2]\nfor x in xs {")]
  [InlineData("val xs = [1, 2]\nfor x in")]
  [InlineData("val n = 3\nfor n {")]
  [InlineData("var count = 0\ncount =")]
  [InlineData("val a = 1\nif a ==")]
  [InlineData("val x int =")]
  [InlineData("val = 5")]
  [InlineData("val big = if true then")]
  [InlineData("fn foo(")]
  [InlineData("fn foo(a int) {\n  val b = a +\n}")]
  [InlineData("val obj = 1\nobj:")]
  [InlineData("fn f(a int) {}\nf(1,")]
  [InlineData("struct Point { x int }\nval p = Point { x: 1,")]
  public void HalfTypedShapes_A2_ProduceNoCheckerErrors(string source) {
    Compilation compilation = CompileTolerant(source);
    Assert.Empty(CheckerErrors(compilation));
  }

  [Fact]
  public void HalfTypedShapes_A2_KeepTargetTypesAndBindNamesForCompletion() {
    // `val x =`: the name is bound (to ErrorType) so later uses do not cascade.
    Compilation missingInitializer = CompileTolerant("val x =\nval y = x + 1");
    Assert.Empty(CheckerErrors(missingInitializer));
    Assert.True(Local(missingInitializer, "x").Type.IsError);
    Assert.True(Local(missingInitializer, "y").Type.IsError);

    // `if x {`: the condition is still typed.
    Compilation missingBlock = CompileTolerant("val flag = true\nif flag {");
    IfStatementSyntax ifStatement = All<IfStatementSyntax>(missingBlock).Single();
    Assert.Same(missingBlock.Types.Bool, missingBlock.RootModel.GetType(ifStatement.Condition));

    // `[1, 2`: the elements are still typed and joined.
    Compilation missingBracket = CompileTolerant("val v = [1, 2");
    Assert.Empty(CheckerErrors(missingBracket));
    Assert.Same(
        missingBracket.Types.GetVector(missingBracket.Types.Int),
        Local(missingBracket, "v").Type);

    // `obj:`: the target of the half-typed member access is typed for completion.
    Compilation missingMember = CompileTolerant("val obj = 1\nobj:");
    MemberAccessExpressionSyntax member = All<MemberAccessExpressionSyntax>(missingMember).Single();
    Assert.Same(missingMember.Types.Int, missingMember.RootModel.GetType(member.Target));
    Assert.True(missingMember.RootModel.GetType(member)!.IsError);

    // `val x int =`: the declared type is kept even though the initializer is missing.
    Compilation annotated = CompileTolerant("val x int =\nval y = x + 1");
    Assert.Empty(CheckerErrors(annotated));
    Assert.Same(annotated.Types.Int, Local(annotated, "x").Type);
    Assert.Same(annotated.Types.Int, Local(annotated, "y").Type);
  }

  [Fact]
  public void MissingAndErrorNodes_InsideBlocksAndLoops_AreSkippedSilently() {
    Compilation compilation = CompileTolerant("""
      fn demo(xs []int) {
        var total = 0
        for x in xs {
          total +=
        }
        if total > {
          val z = 1
        }
        val w = [total, ]
        total = total *
      }
      """);

    Assert.Empty(CheckerErrors(compilation));
  }
}
