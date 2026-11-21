using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerControlFlowBasicTests {
  [Fact]
  public void IfExpression_D033_JoinsIntAndFloatAndRejectsIncompatibleBranches() {
    Compilation compilation = Compile("""
      val joined = if true then 1 else 2.5
      val swapped = if true then 2.5 else 1
      val same = if true then "a" else "b"
      val pushed float = if true then 1 else 2
      val bad = if true then 1 else "x"
      val after = bad
      """);

    Diagnostic error = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.BranchTypeMismatch, error.Code);
    Assert.Contains("incompatible types", error.Message);
    Assert.Contains("'int' and 'string'", error.Message);

    // IF-7: `if true then 1 else 2.5` is a float and the int branch widens.
    Assert.Same(compilation.Types.Float, Local(compilation, "joined").Type);
    var joined = (IfExpressionSyntax) Declaration(compilation, "joined").Initializer;
    AssertIntToFloat(compilation, joined.ThenBranch);
    AssertNoConversion(compilation, joined.ElseBranch);

    var swapped = (IfExpressionSyntax) Declaration(compilation, "swapped").Initializer;
    Assert.Same(compilation.Types.Float, Local(compilation, "swapped").Type);
    AssertNoConversion(compilation, swapped.ThenBranch);
    AssertIntToFloat(compilation, swapped.ElseBranch);

    Assert.Same(compilation.Types.String, Local(compilation, "same").Type);

    // An expected `float` is pushed into both branches (D-033 + D-172).
    var pushed = (IfExpressionSyntax) Declaration(compilation, "pushed").Initializer;
    AssertIntToFloat(compilation, pushed.ThenBranch);
    AssertIntToFloat(compilation, pushed.ElseBranch);

    // IF-E5: the failed join is ErrorType, so `after` does not cascade.
    Assert.True(Local(compilation, "bad").Type.IsError);
    Assert.True(Local(compilation, "after").Type.IsError);
  }

  [Fact]
  public void IfConditions_D032_MustBeBoolInStatementsAndExpressions() {
    Compilation compilation = Compile("""
      val flag = true
      if flag {
        val inside = 1
      } else if 1 {
        val elsewhere = 2
      } else {
        val last = 3
      }
      if 2.5 {
      }
      val r = if "x" then 1 else 2
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(3, errors.Length);
    Assert.All(errors, static d => {
      Assert.Equal(DiagnosticCodes.InvalidConditionType, d.Code);
      Assert.Contains("bool", d.Message);
    });
    Assert.Contains("'int'", errors[0].Message);
    Assert.Contains("'float'", errors[1].Message);
    Assert.Contains("'string'", errors[2].Message);

    // Locals declared in `if` blocks are block-scoped (VAR-5).
    Assert.False(compilation.RootModel.GetScope(compilation.RootTree.Root)!.TryLookupLocal("inside", out _));
  }

  [Fact]
  public void ForLoops_CountAndConditionClassification_D002AndD003() {
    Compilation compilation = Compile("""
      for 3 {
        val index = it
      }
      var go = true
      for go {
        go = false
      }
      for {
        break
      }
      for 1.5 {
      }
      for "x" {
      }
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(2, errors.Length);
    Assert.All(errors, static d => {
      Assert.Equal(DiagnosticCodes.InvalidConditionType, d.Code);
      Assert.Contains("bool", d.Message);
    });

    List<ForStatementSyntax> loops = All<ForStatementSyntax>(compilation);
    SemanticModel model = compilation.RootModel;

    // LOOP-3 / OQ-17 / D-002: `for N` declares an immutable implicit `it: int` in the body scope.
    LoopInfo count = Assert.IsType<LoopInfo>(model.GetLoopInfo(loops[0]));
    Assert.Equal(LoopKind.CountLoop, count.Kind);
    LocalSymbol it = Assert.IsType<LocalSymbol>(count.ImplicitItSymbol);
    Assert.Equal("it", it.Name);
    Assert.Equal(LocalKind.ImplicitIt, it.Kind);
    Assert.False(it.IsMutable);
    Assert.Same(compilation.Types.Int, it.Type);
    Assert.Same(it, model.GetScope(loops[0])!.DeclaredSymbols["it"]);
    Assert.Same(compilation.Types.Int, Local(compilation, "index").Type);

    // LOOP-13 / OQ-25 / D-003: a bool header is a condition loop with no implicit `it`.
    LoopInfo condition = Assert.IsType<LoopInfo>(model.GetLoopInfo(loops[1]));
    Assert.Equal(LoopKind.ConditionLoop, condition.Kind);
    Assert.Null(condition.ImplicitItSymbol);

    Assert.Equal(LoopKind.Infinite, model.GetLoopInfo(loops[2])!.Kind);
    Assert.Null(model.GetLoopInfo(loops[3]));
    Assert.Null(model.GetLoopInfo(loops[4]));
  }

  [Fact]
  public void ForLoops_ImplicitItIsAnImmutableBinding() {
    Compilation compilation = Compile("""
      for 3 {
        it = 5
      }
      """);

    Diagnostic error = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.CannotAssignToImmutable, error.Code);
    Assert.Contains("Cannot reassign constant variable 'it'", error.Message);
  }

  [Fact]
  public void ForInLoops_BindElementTypesForRangeVectorStringMapAndSet() {
    Compilation compilation = Compile("""
      val xs = [1, 2, 3]
      val m = map[string, float] { "a" -> 1.5 }
      val s = set[string] { "a" }
      for i in 3 { }
      for i, v in 3 { }
      for x in xs { }
      for i, x in xs { }
      for ch in "abc" { }
      for i, ch in "abc" { }
      for v in m { }
      for k, v in m { }
      for e in s { }
      for x in 2.5 { }
      for x in true { }
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(2, errors.Length);
    Assert.All(errors, static d => {
      Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code);
      Assert.Contains("Cannot iterate over", d.Message);
    });

    List<ForStatementSyntax> loops = All<ForStatementSyntax>(compilation);
    SemanticModel model = compilation.RootModel;
    TypeInterner types = compilation.Types;

    void AssertLoop(int index, LoopKind kind, TypeSymbol first, TypeSymbol? second = null) {
      LoopInfo info = Assert.IsType<LoopInfo>(model.GetLoopInfo(loops[index]));
      Assert.Equal(kind, info.Kind);
      Assert.Same(first, info.FirstBinding!.Type);
      Assert.Equal(LocalKind.LoopVariable, info.FirstBinding.Kind);
      if (second is null) {
        Assert.Null(info.SecondBinding);
      } else {
        Assert.Same(second, info.SecondBinding!.Type);
      }
    }

    // LOOP-4 / LOOP-8: both bindings over an int are the counter.
    AssertLoop(0, LoopKind.ForInRange, types.Int);
    AssertLoop(1, LoopKind.ForInRange, types.Int, types.Int);
    // LOOP-5 / LOOP-7: element, or (index, element).
    AssertLoop(2, LoopKind.ForInVector, types.Int);
    AssertLoop(3, LoopKind.ForInVector, types.Int, types.Int);
    // LOOP-6 / LOOP-7: rune, or (index, rune).
    AssertLoop(4, LoopKind.ForInString, types.Rune);
    AssertLoop(5, LoopKind.ForInString, types.Int, types.Rune);
    // LOOP-9 / D-120: a single variable binds the value; LOOP-10 / D-004: (key, value).
    AssertLoop(6, LoopKind.ForInMap, types.Float);
    AssertLoop(7, LoopKind.ForInMap, types.String, types.Float);
    // LOOP-18: sets bind the element.
    AssertLoop(8, LoopKind.ForInSet, types.String);
  }

  [Fact]
  public void VectorInitializer_ImplicitItIsInScopeOnlyInsideInit() {
    Compilation compilation = Compile("""
      val squares = []int { len: 4, init: it * it }
      val halves = []float { len: 2, init: it }
      val capped = []int { cap: 8 }
      val both = []int { len: 2, init: it + 1, cap: 10 }
      val empty = []string
      val bad_init = []int { len: 3, init: "x" }
      val bad_len = []int { len: "n", init: 0 }
      fn make_item(i int) string { return "item" }
      val by_reference = []string { len: 2, init: make_item }
      val bad_reference = []int { len: 2, init: make_item }
      val outside = it
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(3, errors.Length);
    Assert.All(errors, static d => Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code));
    Assert.Contains("vector element of type 'int'", errors[0].Message);
    Assert.Contains("vector length of type 'int'", errors[1].Message);
    // A function reference with the exact `fn (int) -> T` signature is accepted as `init`.
    Assert.Contains("'fn (int) -> string' to vector element of type 'int'", errors[2].Message);
    Assert.Same(compilation.Types.GetVector(compilation.Types.String), Local(compilation, "by_reference").Type);

    TypeInterner types = compilation.Types;
    Assert.Same(types.GetVector(types.Int), Local(compilation, "squares").Type);
    Assert.Same(types.GetVector(types.Float), Local(compilation, "halves").Type);
    Assert.Same(types.GetVector(types.Int), Local(compilation, "capped").Type);
    Assert.Same(types.GetVector(types.Int), Local(compilation, "both").Type);
    Assert.Same(types.GetVector(types.String), Local(compilation, "empty").Type);

    // VEC-8 + D-172: `it` is an int widened to the declared element type.
    var halves = (VectorInitializerExpressionSyntax) Declaration(compilation, "halves").Initializer;
    VectorPropertySyntax init = halves.Properties.Single(p => TextOf(compilation, p.NameToken) == "init");
    AssertIntToFloat(compilation, init.Value);

    Scope initScope = Assert.IsType<Scope>(compilation.RootModel.GetScope(init));
    LocalSymbol it = Assert.IsType<LocalSymbol>(initScope.DeclaredSymbols["it"]);
    Assert.Equal(LocalKind.ImplicitIt, it.Kind);
    Assert.False(it.IsMutable);
    Assert.Same(types.Int, it.Type);
  }

  [Fact]
  public void StandaloneBlocks_D161_ShadowOuterNamesWithoutChangingThem() {
    Compilation compilation = Compile("""
      val a = 1
      {
        {
          val a = 2.5
          val c = a
        }
        val a = "inner"
        val b = a
      }
      val after = a + 1
      if true {
        val a = 'r'
      }
      """);

    Assert.Empty(CheckerErrors(compilation));
    Assert.Same(compilation.Types.String, Local(compilation, "b").Type);
    Assert.Same(compilation.Types.Float, Local(compilation, "c").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "after").Type);
    Assert.False(compilation.RootModel.GetScope(compilation.RootTree.Root)!.TryLookupLocal("b", out _));

    // A same-scope redeclaration inside a block is still rejected (D-018).
    Compilation duplicate = Compile("""
      {
        val a = 1
        val a = 2
      }
      """);
    Assert.Contains("already declared", Assert.Single(CheckerErrors(duplicate)).Message);
  }

  [Fact]
  public void AssertAndTest_CheckConditionAndTestBodyScopes() {
    Compilation compilation = Compile("""
      assert 1 == 1
      assert 1
      assert "text"
      test "adds" {
        val x = 1 + 1
        assert x == 2
        val y = x
      }
      test "interpolated $y" {
      }
      val outside = x
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(4, errors.Length);
    Assert.Equal(DiagnosticCodes.InvalidConditionType, errors[0].Code);
    Assert.Contains("'assert' condition", errors[0].Message);
    Assert.Contains("bool", errors[0].Message);
    Assert.Contains("'int'", errors[0].Message);
    Assert.Equal(DiagnosticCodes.InvalidConditionType, errors[1].Code);
    Assert.Contains("'string'", errors[1].Message);

    // Test names are interpolated strings (checked like any string); `y` is not in scope there.
    Assert.Equal(DiagnosticCodes.UndeclaredIdentifier, errors[2].Code);

    // Test bodies are checked, and their locals do not leak to the module scope.
    Assert.Equal(DiagnosticCodes.UndeclaredIdentifier, errors[3].Code);
    Assert.Same(compilation.Types.Int, Local(compilation, "y").Type);
  }

  [Fact]
  public void ConditionalAndLoopConformanceCases_ProduceTheExpectedErrors() {
    Compilation branch = Compile("""
      val r = if true then 1 else "x"
      println(r)
      """);
    Assert.Contains(CheckerErrors(branch), static d => d.Code == DiagnosticCodes.BranchTypeMismatch
        && d.Message.Contains("incompatible types", StringComparison.Ordinal));

    Compilation condition = Compile("""
      if 1 {
        println("truthy")
      }
      """);
    Assert.Contains(CheckerErrors(condition), static d => d.Code == DiagnosticCodes.InvalidConditionType
        && d.Message.Contains("bool", StringComparison.Ordinal));

    // Function bodies are checked in textual order with parameters in scope.
    Compilation body = Compile("""
      fn sum(xs []int) int {
        var total = 0
        for x in xs {
          if x > 1 {
            total += x
          }
        }
        for i, x in xs {
          total += i * x
        }
        return total
      }
      """);
    Assert.Empty(CheckerErrors(body));
  }
}
