using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerPatternAndFlowTests {
  private static Compilation CompileSource(string code) => Compile(code);

  private static Compilation CompileWithModules(string mainCode, IReadOnlyDictionary<string, string> modules) {
    var source = new InMemoryModuleSource();
    foreach ((string name, string content) in modules) {
      source.AddFile($"modules/{name}.pino", content);
    }

    ParseResult parsed = Parser.Parse(new SourceText(mainCode, "main.pino"));
    Assert.Empty(parsed.Diagnostics);
    return Compilation.Create(parsed, source);
  }

  private static void AssertNoErrors(Compilation compilation) =>
      Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
  [Fact]
  public void UnionPatterns_PositionalLabeledNestedAndErrors_CheckedPerD169() {
    const string ValidCode = """
      union Shape {
        Circle(radius float)
        Rect(width float, height float)
        Point
      }

      union Outer {
        Wrap(Option[Shape])
        Empty
      }

      fn area(o Outer) float {
        return match o {
          when Outer::Wrap(Option::Some(Shape::Circle(r))) { yield 3.14 * r * r }
          when Outer::Wrap(Option::Some(Shape::Rect(@height: h, @width: w))) { yield w * h }
          when Outer::Wrap(Option::Some(Shape::Point)) { yield 0.0 }
          when Outer::Wrap(Option::None) { yield 0.0 }
          when Outer::Empty { yield 0.0 }
        }
      }
      """;

    Compilation valid = CompileSource(ValidCode);
    AssertNoErrors(valid);

    const string BadLabels = """
      union NamedShape {
        Circle(radius float)
      }

      union PosShape {
        Rect(float, float)
        Point
      }

      fn bad(ns NamedShape, ps PosShape) void {
        match ns {
          when NamedShape::Circle(@wrong: r) { println(r) }
          else {}
        }
        match ns {
          when NamedShape::Circle(@radius: a, @radius: b) { println(a) }
          else {}
        }
        match ps {
          when PosShape::Rect(@w: x, @h: y) { println(x) }
          when PosShape::Rect(a, b, c) { println(a) }
          when PosShape::Point(x) { println(x) }
          else {}
        }
      }
      """;

    Compilation invalid = CompileSource(BadLabels);
    Diagnostic[] codes = invalid.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
    Assert.Contains(codes, static d => d.Code == DiagnosticCodes.UnknownPatternFieldLabel);
    Assert.Contains(codes, static d => d.Code == DiagnosticCodes.DuplicatePatternFieldLabel);
    Assert.Contains(codes, static d => d.Code == DiagnosticCodes.LabelPatternOnPositionalVariant);
    Assert.Equal(2, codes.Count(static d => d.Code == DiagnosticCodes.TooManyPatternBindings));
    Assert.DoesNotContain(codes, static d => d.Code == DiagnosticCodes.UndeclaredIdentifier);
  }

  [Fact]
  public void BareIdentifierInWhen_D034_ReportsPN3314WithElseHelp() {
    const string Code = """
      fn f(x int) int {
        return match x {
          when 1 { yield 10 }
          when other { yield 20 }
        }
      }
      """;

    Compilation compilation = CompileSource(Code);
    Diagnostic diag = Assert.Single(compilation.Diagnostics, static d => d.Code == DiagnosticCodes.BareIdentifierPatternNotAllowed);
    Assert.Contains("else", diag.Message, StringComparison.Ordinal);
    Assert.Contains("else", diag.Help, StringComparison.Ordinal);
  }

  [Fact]
  public void IsAndIsNot_FlowBindings_AndChaining_ScopedOnlyToThenBranch() {
    const string ValidCode = """
      fn sum_both(a Option[int], b Option[int]) int {
        var total = 0
        if a is Option::Some(x) and b is Option::Some(y) and x < y {
          total = x + y
        }
        val picked = if a is Option::Some(v) then v * 2 else 0
        var cur = a
        for cur is Option::Some(item) {
          total += item
          cur = Option::None
        }
        return total + picked
      }
      """;

    Compilation valid = CompileSource(ValidCode);
    AssertNoErrors(valid);

    SemanticModel model = valid.RootModel;
    MatchAndAssertIfBindings(model);

    const string OutOfScopeCode = """
      fn leak(a Option[int], flag bool) int {
        var sum = 0
        if a is not Option::Some(x) {
          sum += x
        }
        if a is Option::Some(y) or flag {
          sum += y
        }
        if not (a is Option::Some(z)) {
          sum += z
        } else {
          sum += z
        }
        return sum + x
      }
      """;

    Compilation leaks = CompileSource(OutOfScopeCode);
    Diagnostic[] undeclared = leaks.Diagnostics.Where(static d => d.Code == DiagnosticCodes.UndeclaredIdentifier).ToArray();
    Assert.Equal(5, undeclared.Length);
  }

  private static void MatchAndAssertIfBindings(SemanticModel model) {
    var fn = Assert.IsType<FunctionDeclarationSyntax>(model.Tree.Root.Items[0]);
    var ifStmt = Assert.IsType<IfStatementSyntax>(fn.Body.Items[1]);
    var patterns = Descendants<VariantPatternSyntax>(ifStmt.Condition).ToArray();
    Assert.Equal(2, patterns.Length);
    Assert.All(patterns, p => Assert.Single(model.GetPatternBindings(p)!.Bindings));
  }

  [Fact]
  public void MatchExhaustiveness_UnreachableArms_AndExpressionYieldJoining() {
    const string ValidMatch = """
      enum Color { Red, Green, Blue }

      fn code(c Color) float {
        return match c {
          when Color::Red { yield 1 }
          when Color::Green { yield 2.5 }
          when Color::Blue {
            val tmp = 3
            yield tmp
          }
        }
      }
      """;

    Compilation valid = CompileSource(ValidMatch);
    AssertNoErrors(valid);
    Assert.Empty(valid.Diagnostics);

    const string DiagnosticsMatch = """
      enum Color { Red, Green, Blue }

      fn non_exhaustive_enum(c Color) int {
        return match c {
          when Color::Red, Color::Red { yield 1 }
        }
      }

      fn unreachable_after_else(x int) int {
        return match x {
          when 1 { yield 1 }
          else { yield 0 }
          when 2 { yield 2 }
        }
      }

      fn incompatible_yields(b bool) int {
        return match b {
          when true { yield 1 }
          else { yield "nope" }
        }
      }

      fn supress_on_error() int {
        return match missing_val {
          when 1 { yield 1 }
        }
      }
      """;

    Compilation diags = CompileSource(DiagnosticsMatch);
    Assert.Contains(diags.Diagnostics, static d => d.Code == DiagnosticCodes.NonExhaustiveMatch);
    Assert.DoesNotContain(
        diags.Diagnostics,
        static d => d.Code == DiagnosticCodes.NonExhaustiveMatch && d.Span.Start > DiagnosticsMatch.IndexOf("supress_on_error", StringComparison.Ordinal));
    Diagnostic[] unreachable = diags.Diagnostics.Where(static d => d.Code == DiagnosticCodes.UnreachableMatchArm).ToArray();
    Assert.Equal(2, unreachable.Length);
    Assert.All(unreachable, static d => Assert.Equal(DiagnosticTags.Unnecessary, d.Tags));
    Assert.Contains(diags.Diagnostics, static d => d.Code == DiagnosticCodes.BranchTypeMismatch);
  }

  [Fact]
  public void DefiniteReturn_InfiniteLoopAndMissingPath_AndLoopControlScoping() {
    const string ValidControl = """
      fn infinite_no_break() int {
        for {
          println("running")
        }
      }

      fn all_match_paths(o Option[int]) int {
        match o {
          when Option::Some(v) { return v }
          when Option::None { return 0 }
        }
      }
      """;

    AssertNoErrors(CompileSource(ValidControl));

    const string InvalidControl = """
      fn infinite_with_break() int {
        for {
          break
        }
      }

      fn nested_break_error() void {
        for {
          val f = fn() {
            break
          }
          fn inner() {
            continue
          }
          f()
          inner()
          break
        }
      }

      fn lambda_missing_return() void {
        val f fn(bool) -> int = fn(flag) {
          if flag {
            return 1
          }
        }
        println(f(true))
      }

      yield 1
      """;

    Compilation bad = CompileSource(InvalidControl);
    Assert.Equal(2, bad.Diagnostics.Count(static d => d.Code == DiagnosticCodes.MissingReturnPath));
    Assert.Equal(2, bad.Diagnostics.Count(static d => d.Code == DiagnosticCodes.BreakOrContinueOutsideLoop));
    Assert.Single(bad.Diagnostics, static d => d.Code == DiagnosticCodes.YieldOutsideValidContext);
  }

  [Fact]
  public void ConstantEvaluation_D166_ErrorsAndWarnings() {
    const string Code = """
      val zero = 10 - 5 * 2
      val neg = -3
      val small = 2
      val large = 5
      var runtime_zero = 0

      val bad_div = 10 / zero
      val bad_floor = 10 // 0
      val bad_mod = 10 % zero
      val ok_runtime = 10 / runtime_zero

      val v1 = []int { len: neg }
      val v2 = []int { cap: neg }
      val v3 = []int { len: large, cap: small }

      for 1 > 2 {
        println("dead")
      }
      """;

    Compilation compilation = CompileSource(Code);
    Assert.Equal(3, compilation.Diagnostics.Count(static d => d.Code == DiagnosticCodes.ConstantDivisionByZero));
    Assert.Single(compilation.Diagnostics, static d => d.Code == DiagnosticCodes.NegativeVectorLength);
    Assert.Single(compilation.Diagnostics, static d => d.Code == DiagnosticCodes.NegativeVectorCapacity);
    Assert.Single(compilation.Diagnostics, static d => d.Code == DiagnosticCodes.VectorCapacityBelowLength);
    Diagnostic warn = Assert.Single(compilation.Diagnostics, static d => d.Code == DiagnosticCodes.ConstantFalseLoop);
    Assert.Equal(DiagnosticSeverity.Warning, warn.Severity);
    Assert.Equal(DiagnosticTags.Unnecessary, warn.Tags);
    Assert.Equal(6, compilation.Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Error));
  }

  [Fact]
  public void Ticket042Features_StringPlusRune_CompoundDivideInt_AndPubVal() {
    const string StringAndAssignCode = """
      val ch = '!'
      var msg = "hi" + ch
      msg += '?'
      val rune_left = 'a' + "b"
      val bad_order = 1 + "b"

      var n = 10
      n /= 2
      n //= 2
      var f = 10.0
      f /= 2
      """;

    Compilation strAssign = CompileSource(StringAndAssignCode);
    Assert.Same(strAssign.Types.String, Local(strAssign, "rune_left").Type);
    Assert.Contains(strAssign.Diagnostics, static d => d.Code == DiagnosticCodes.InvalidOperatorOperands && d.Message.Contains("string", StringComparison.Ordinal));
    Diagnostic varE9 = Assert.Single(strAssign.Diagnostics, static d => d.Code == DiagnosticCodes.TypeMismatch);
    Assert.Contains("//=", varE9.Message, StringComparison.Ordinal);
    Assert.Contains("//=", varE9.Help, StringComparison.Ordinal);
    Assert.Equal(2, strAssign.Diagnostics.Length);

    var modules = new Dictionary<string, string>(StringComparer.Ordinal) {
      ["Limits"] = """
        pub val max_items = 100
        pub val zero_const = 0
        val secret = 42
        """,
      ["BadVar"] = """
        pub var counter = 0
        """,
    };

    Compilation modOk = CompileWithModules(
        """
        import Limits
        from Limits import max_items
        val a = Limits::max_items + max_items
        val div_zero = 10 / Limits::zero_const
        """,
        modules);
    Assert.Single(modOk.Diagnostics, static d => d.Code == DiagnosticCodes.ConstantDivisionByZero);

    Compilation modPriv = CompileWithModules(
        """
        import Limits
        val a = Limits::secret
        """,
        modules);
    Assert.Single(modPriv.Diagnostics, static d => d.Code == DiagnosticCodes.SymbolNotPublic);

    Compilation modBadVar = CompileWithModules("import BadVar\n", modules);
    Diagnostic badVarDiag = Assert.Single(modBadVar.Diagnostics, static d => d.Code == DiagnosticCodes.SymbolNotPublic);
    Assert.Contains("pub val", badVarDiag.Message, StringComparison.Ordinal);
    Assert.Contains("pub val", badVarDiag.Help, StringComparison.Ordinal);
  }
}
