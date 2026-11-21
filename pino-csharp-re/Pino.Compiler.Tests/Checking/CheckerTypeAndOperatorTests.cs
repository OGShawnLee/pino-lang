using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerTypeAndOperatorTests {
  [Fact]
  public void Annotations_D012AndOQ9_DeclaredTypeWinsAndIntWidensToFloat() {
    Compilation compilation = Compile("""
      val n int = 12
      val widened float = 1
      val bad int = "x"
      val narrowed int = 1.5
      val after = bad + 1
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(2, errors.Length);
    Assert.All(errors, static d => Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code));
    Assert.Equal("Cannot assign type 'string' to variable 'bad' of type 'int'.", errors[0].Message);
    Assert.Contains("Cannot assign type 'float' to variable 'narrowed' of type 'int'", errors[1].Message);

    // D-012 / VAR-10: the declared type wins on mismatch, so `bad` does not cascade into `after`.
    Assert.Same(compilation.Types.Int, Local(compilation, "bad").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "narrowed").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "after").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "n").Type);
    Assert.False(Local(compilation, "n").IsMutable);

    // OQ-9 Option 1: `val x float = 1` widens `int` to `float` and records the conversion.
    Assert.Same(compilation.Types.Float, Local(compilation, "widened").Type);
    AssertIntToFloat(compilation, Declaration(compilation, "widened").Initializer);
    AssertNoConversion(compilation, Declaration(compilation, "n").Initializer);
  }

  [Fact]
  public void ValInitializedWithOptionNone_D160_RejectedForValAllowedForVar() {
    Compilation compilation = Compile("""
      val x Option[int] = Option::None
      val y = Option::None
      var z Option[int] = Option::None
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(2, errors.Length);
    Assert.All(errors, static d => {
      Assert.Equal(DiagnosticCodes.ValInitializedWithOptionNone, d.Code);
      Assert.Contains("Option::None", d.Message);
      Assert.Contains("var", d.Help);
    });

    // The annotated binding keeps its declared type; the unannotated one is ErrorType.
    Assert.IsType<UnionType>(Local(compilation, "x").Type);
    Assert.Same(compilation.Prelude.OptionUnion, ((UnionType) Local(compilation, "x").Type).Definition);
    Assert.True(Local(compilation, "y").Type.IsError);
    Assert.False(Local(compilation, "z").Type.IsError);
    Assert.True(Local(compilation, "z").IsMutable);
  }

  [Fact]
  public void Assignment_D017AndAD002_RecordsAssignedBindingsAndRejectsVal() {
    Compilation compilation = Compile("""
      var counter = 0
      counter = counter + 1
      var total = 10
      total //= 2
      var added = 1
      added += 2
      var untouched = 5
      val fixed = 3
      var chain_a = 0
      var chain_b = 0
      chain_a = chain_b = 4
      fn bump(p int, q int) {
        p = p + 1
        val inner = q
      }
      """);

    Assert.Empty(CheckerErrors(compilation));
    SemanticModel model = compilation.RootModel;

    Assert.True(model.IsAssignedAfterInit(Local(compilation, "counter")));
    Assert.True(model.IsAssignedAfterInit(Local(compilation, "total")));
    Assert.True(model.IsAssignedAfterInit(Local(compilation, "added")));
    Assert.True(model.IsAssignedAfterInit(Local(compilation, "chain_a")));
    Assert.True(model.IsAssignedAfterInit(Local(compilation, "chain_b")));
    Assert.False(model.IsAssignedAfterInit(Local(compilation, "untouched")));
    Assert.False(model.IsAssignedAfterInit(Local(compilation, "fixed")));
    Assert.False(model.IsAssignedAfterInit(Local(compilation, "inner")));

    FunctionDeclarationSyntax bump = All<FunctionDeclarationSyntax>(compilation).Single();
    var p = Assert.IsType<ParameterSymbol>(model.GetDeclaredSymbol(bump.Parameters[0]));
    var q = Assert.IsType<ParameterSymbol>(model.GetDeclaredSymbol(bump.Parameters[1]));
    Assert.True(model.IsAssignedAfterInit(p));
    Assert.False(model.IsAssignedAfterInit(q));
    Assert.Equal(6, compilation.RootModel.AssignedBindings.Count);
  }

  [Fact]
  public void Assignment_D017_ValTargetsAreRejectedWithRelatedLocation() {
    Compilation compilation = Compile("""
      val total = 5
      total = 6
      total += 1
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(2, errors.Length);
    Assert.All(errors, d => {
      Assert.Equal(DiagnosticCodes.CannotAssignToImmutable, d.Code);
      Assert.Contains("Cannot reassign constant variable", d.Message);
      Assert.Contains("val", d.Help);
      RelatedLocation related = Assert.Single(d.Related);
      Assert.Equal(Declaration(compilation, "total").NameToken.Span, related.Span);
    });
  }

  [Fact]
  public void NumericOperators_D019_WidenIntToFloatAndDivisionIsAlwaysFloat() {
    Compilation compilation = Compile("""
      val a = 1 + 2.5
      val b = 5 / 2
      val c = 1 == 1.0
      val d = 2 * 3
      val e = 1.5 < 2
      var i = 1
      i = 2.5
      """);

    Diagnostic error = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.TypeMismatch, error.Code);
    Assert.Equal("Cannot assign type 'float' to target of type 'int'.", error.Message);

    List<BinaryExpressionSyntax> binaries = All<BinaryExpressionSyntax>(compilation);
    BinaryExpressionSyntax plus = binaries[0];
    BinaryExpressionSyntax slash = binaries[1];
    BinaryExpressionSyntax equals = binaries[2];
    BinaryExpressionSyntax times = binaries[3];
    BinaryExpressionSyntax less = binaries[4];

    // `1 + 2.5` is a float and only the `int` operand records IntToFloat.
    Assert.Same(compilation.Types.Float, compilation.RootModel.GetType(plus));
    AssertIntToFloat(compilation, plus.Left);
    AssertNoConversion(compilation, plus.Right);

    // `5 / 2` is always a float and both `int` operands widen.
    Assert.Same(compilation.Types.Float, compilation.RootModel.GetType(slash));
    AssertIntToFloat(compilation, slash.Left);
    AssertIntToFloat(compilation, slash.Right);

    // `1 == 1.0` widens the int side and is a bool.
    Assert.Same(compilation.Types.Bool, compilation.RootModel.GetType(equals));
    AssertIntToFloat(compilation, equals.Left);
    AssertNoConversion(compilation, equals.Right);

    // `2 * 3` stays an int with no conversions.
    Assert.Same(compilation.Types.Int, compilation.RootModel.GetType(times));
    AssertNoConversion(compilation, times.Left);

    // Comparison of float and int widens the int operand.
    Assert.Same(compilation.Types.Bool, compilation.RootModel.GetType(less));
    AssertIntToFloat(compilation, less.Right);

    // VAR-E5: `i = 2.5` never narrows, and `i` stays an int.
    Assert.Same(compilation.Types.Int, Local(compilation, "i").Type);
  }

  [Fact]
  public void FloorDivisionAndModulo_D164AndOQ5_AreFlooredOnIntAndFloat() {
    Compilation compilation = Compile("""
      val a = -7 // 2
      val b = -7 % 2
      val c = 7.0 // 2
      val d = 7.5 % 2
      val e = 7 % 2.5
      var x = 7
      x //= 2
      var y = 7.0
      y //= 2
      y %= 2
      y -= 1
      x //= 2.0
      x %= 2.5
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(2, errors.Length);
    Assert.All(errors, static d => {
      Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code);
      Assert.Equal("Cannot assign type 'float' to target of type 'int'.", d.Message);
    });

    Assert.Same(compilation.Types.Int, Local(compilation, "a").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "b").Type);
    Assert.Same(compilation.Types.Float, Local(compilation, "c").Type);
    Assert.Same(compilation.Types.Float, Local(compilation, "d").Type);
    Assert.Same(compilation.Types.Float, Local(compilation, "e").Type);

    // `7.0 // 2` widens the int divisor; `7 % 2.5` widens the int dividend.
    var floatFloor = (BinaryExpressionSyntax) Declaration(compilation, "c").Initializer;
    AssertNoConversion(compilation, floatFloor.Left);
    AssertIntToFloat(compilation, floatFloor.Right);
    var mixedModulo = (BinaryExpressionSyntax) Declaration(compilation, "e").Initializer;
    AssertIntToFloat(compilation, mixedModulo.Left);

    // `y //= 2` on a float target widens the value operand.
    List<AssignmentExpressionSyntax> assignments = All<AssignmentExpressionSyntax>(compilation);
    AssertIntToFloat(compilation, assignments[1].Value);
    Assert.Same(compilation.Types.Float, Local(compilation, "y").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "x").Type);
  }

  [Fact]
  public void RuneAndStringOperators_D011D025_RejectRuneConcatenationAndStringWithNonString() {
    Compilation compilation = Compile("""
      val shifted = 'A' + 1
      val prev = 'z' - 2
      val dist = 'c' - 'a'
      val joined = "a" + "b"
      val bad_runes = 'a' + 'b'
      val bad_right = "n=" + 1
      val bad_left = 1 + "n"
      val ordered = "a" < "b"
      val rune_order = 'a' <= 'b'
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(3, errors.Length);
    Assert.All(errors, static d => Assert.Equal(DiagnosticCodes.InvalidOperatorOperands, d.Code));

    // RUNE-E2: suggests "$a$b" or str(a) + str(b).
    Assert.Contains("\"$a$b\"", errors[0].Message);
    Assert.Contains("str(a) + str(b)", errors[0].Message);
    Assert.Contains("\"$a$b\"", errors[0].Help);

    // STR-E5: the conformance text is "string", in either order.
    Assert.Contains("string", errors[1].Message);
    Assert.Contains("string", errors[2].Message);

    Assert.Same(compilation.Types.Rune, Local(compilation, "shifted").Type);
    Assert.Same(compilation.Types.Rune, Local(compilation, "prev").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "dist").Type);
    Assert.Same(compilation.Types.String, Local(compilation, "joined").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "ordered").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "rune_order").Type);
  }

  [Fact]
  public void RunePlusString_D025AndStr12_SynthesizesStringAndRejectsInvalidCombinations() {
    Compilation compilation = Compile("""
      val ch = 'x'
      val s = "yz"
      val lit = 'a' + "bc"
      val vars = ch + s
      val chain = 'x' + "y" + 'z'
      val bad_runes = 'a' + 'b'
      val bad_int_str = 1 + "a"
      var r = 'a'
      r += "bc"
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(3, errors.Length);
    Assert.Equal(DiagnosticCodes.InvalidOperatorOperands, errors[0].Code);
    Assert.Contains("\"$a$b\"", errors[0].Message);
    Assert.Equal(DiagnosticCodes.InvalidOperatorOperands, errors[1].Code);
    Assert.Contains("string", errors[1].Message);
    Assert.Equal(DiagnosticCodes.TypeMismatch, errors[2].Code);

    Assert.Same(compilation.Types.String, Local(compilation, "lit").Type);
    Assert.Same(compilation.Types.String, Local(compilation, "vars").Type);
    Assert.Same(compilation.Types.String, Local(compilation, "chain").Type);
  }

  [Fact]
  public void StringIndexingAndInterpolation_StrE1AndDecisionD023() {
    Compilation compilation = Compile("""
      var s = "abc"
      val ch = s[1]
      s[0] = 'x'
      val name = "pino"
      val sum = 1
      val text = "hi $name, total $(sum + 2) ok"
      """);

    Diagnostic error = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.InvalidAssignmentTarget, error.Code);
    Assert.Contains("Cannot assign to index", error.Message);

    Assert.Same(compilation.Types.Rune, Local(compilation, "ch").Type);
    Assert.Same(compilation.Types.String, Local(compilation, "text").Type);

    var interpolated = (InterpolatedStringExpressionSyntax) Declaration(compilation, "text").Initializer;
    StringInterpolationPartSyntax[] parts = [.. interpolated.Parts.OfType<StringInterpolationPartSyntax>()];
    Assert.Equal(2, parts.Length);
    ConversionInfo first = Assert.IsType<ConversionInfo>(ConversionOf(compilation, parts[0]));
    Assert.Equal(ConversionKind.StringInterpolationFormat, first.Kind);
    Assert.Same(compilation.Types.String, first.TargetType);
    ConversionInfo second = Assert.IsType<ConversionInfo>(ConversionOf(compilation, parts[1]));
    Assert.Equal(ConversionKind.StringInterpolationFormat, second.Kind);
    Assert.Same(compilation.Types.Int, second.TargetType);
  }

  [Fact]
  public void LogicalEqualityComparisonAndMembership_CheckOperandTypes() {
    Compilation compilation = Compile("""
      val ok = true and not false or 1 < 2
      val same = "a" == "a"
      val ident = 1 === 1
      val nope = 1 !== 2
      val in_vec = 2 in [1, 2, 3]
      val not_in_map = "k" not in map[string, int] { "a" -> 1 }
      val in_set = 1 in set[int] { 1 }
      val in_str = "ab" in "abc"
      val in_float_vec = 1 in [1.5, 2.5]
      val bad_and = true and 1
      val bad_not = not 1
      val bad_neg = -"a"
      val bad_eq = 1 == "a"
      val bad_cmp = true < false
      val bad_in = 1 in 5
      val bad_elem = "x" in [1, 2]
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(7, errors.Length);
    Assert.All(errors, static d => Assert.Equal(DiagnosticCodes.InvalidOperatorOperands, d.Code));

    foreach (string name in new[] { "ok", "same", "ident", "nope", "in_vec", "not_in_map", "in_set", "in_str", "in_float_vec" }) {
      Assert.Same(compilation.Types.Bool, Local(compilation, name).Type);
    }

    // `1 in [1.5, 2.5]` widens the probe to the element type.
    var inFloat = (BinaryExpressionSyntax) Declaration(compilation, "in_float_vec").Initializer;
    AssertIntToFloat(compilation, inFloat.Left);
  }

  [Fact]
  public void CollectionLiterals_D168AndOQ10_WidenIntElementsAndValuesToFloat() {
    Compilation compilation = Compile("""
      val v = [1, 2.5]
      val s = set[float] { 1, 2.5 }
      val m = map[string, float] { "a" -> 1, "b" -> 2.5 }
      val typed []float = [1, 2]
      val ints = [1, 2, 3]
      val empty = []int
      val grid = [[1, 2], [3]]
      """);

    Assert.Empty(CheckerErrors(compilation));

    Assert.Same(compilation.Types.GetVector(compilation.Types.Float), Local(compilation, "v").Type);
    var vector = (VectorLiteralExpressionSyntax) Declaration(compilation, "v").Initializer;
    AssertIntToFloat(compilation, vector.Elements[0]);
    AssertNoConversion(compilation, vector.Elements[1]);

    Assert.Same(compilation.Types.GetSet(compilation.Types.Float), Local(compilation, "s").Type);
    var set = (SetLiteralExpressionSyntax) Declaration(compilation, "s").Initializer;
    AssertIntToFloat(compilation, set.Elements[0]);
    AssertNoConversion(compilation, set.Elements[1]);

    Assert.Same(
        compilation.Types.GetMap(compilation.Types.String, compilation.Types.Float),
        Local(compilation, "m").Type);
    var map = (MapLiteralExpressionSyntax) Declaration(compilation, "m").Initializer;
    AssertIntToFloat(compilation, map.Entries[0].Value);
    AssertNoConversion(compilation, map.Entries[1].Value);

    // An expected `[]float` pushes the element type into every element.
    var typedVector = (VectorLiteralExpressionSyntax) Declaration(compilation, "typed").Initializer;
    AssertIntToFloat(compilation, typedVector.Elements[0]);
    AssertIntToFloat(compilation, typedVector.Elements[1]);

    Assert.Same(compilation.Types.GetVector(compilation.Types.Int), Local(compilation, "ints").Type);
    Assert.Same(compilation.Types.GetVector(compilation.Types.Int), Local(compilation, "empty").Type);
    Assert.Same(
        compilation.Types.GetVector(compilation.Types.GetVector(compilation.Types.Int)),
        Local(compilation, "grid").Type);
  }

  [Fact]
  public void CollectionLiterals_MapKeyValueAndElementMismatches_AreTypeErrors() {
    Compilation compilation = Compile("""
      val bad_key = map[string, int] { 123 -> 1 }
      val bad_value = map[string, int] { "a" -> "hello" }
      val bad_set = set[int] { 1, "two" }
      val bad_vec = [1, "two"]
      val bad_typed []int = [1, 2.5]
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(5, errors.Length);
    Assert.All(errors, static d => {
      Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code);
      Assert.Contains("Cannot assign type", d.Message);
    });
    Assert.Contains("map key of type 'string'", errors[0].Message);
    Assert.Contains("map value of type 'int'", errors[1].Message);
    Assert.Contains("set element of type 'int'", errors[2].Message);
  }

  [Fact]
  public void Indexing_VectorMapAndString_TypeTheElementAndRejectOtherTargets() {
    Compilation compilation = Compile("""
      val xs = [10, 20]
      val m = map[string, float] { "a" -> 1.5 }
      var scores = map[string, int]
      scores["x"] = 1
      scores["x"] += 2
      var grid = [1, 2, 3]
      grid[0] = 9
      val first = xs[0]
      val value = m["a"]
      val rune_at = "hey"[1]
      val bad_index = xs["zero"]
      val bad_key = m[1]
      val bad_target = 5[0]
      """);

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(3, errors.Length);
    Assert.Contains(errors, static d => d.Code == DiagnosticCodes.InvalidIndexOperation && d.Message.Contains("'int'"));
    Assert.Contains(errors, static d => d.Code == DiagnosticCodes.TypeMismatch && d.Message.Contains("map key"));
    Assert.Contains(errors, static d => d.Code == DiagnosticCodes.InvalidIndexOperation && d.Message.Contains("Cannot index"));

    Assert.Same(compilation.Types.Int, Local(compilation, "first").Type);
    Assert.Same(compilation.Types.Float, Local(compilation, "value").Type);
    Assert.Same(compilation.Types.Rune, Local(compilation, "rune_at").Type);
  }

  [Fact]
  public void Scoping_D018_InitializerReadsOuterBindingAndSameScopeRedeclarationIsRejected() {
    Compilation compilation = Compile("""
      val x = 1
      {
        val x = x + 1
        val y = x
      }
      val dup = 1
      val dup = "again"
      """);

    Diagnostic error = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.DuplicateDeclaration, error.Code);
    Assert.Contains("already declared", error.Message);
    Assert.Single(error.Related);

    var inner = (VariableDeclarationSyntax) All<BlockStatementSyntax>(compilation).Single().Items[0];
    var innerX = Assert.IsType<LocalSymbol>(compilation.RootModel.GetDeclaredSymbol(inner));
    Assert.Same(compilation.Types.Int, innerX.Type);
    var read = Assert.IsType<IdentifierExpressionSyntax>(((BinaryExpressionSyntax) inner.Initializer).Left);
    VariableDeclarationSyntax outer = compilation.RootTree.Root.Items.OfType<VariableDeclarationSyntax>().First();
    Symbol? outerX = compilation.RootModel.GetDeclaredSymbol(outer);
    Assert.Same(outerX, compilation.RootModel.GetReferencedSymbol(read));
    Assert.NotSame(innerX, compilation.RootModel.GetReferencedSymbol(read));
  }

  [Fact]
  public void StrictTextualOrder_OQ16Option2_FunctionNeverSeesLaterTopLevelBinding() {
    // Script Mode: a function declared above a top-level `val` does not see it.
    Compilation script = Compile("""
      fn get_limit() int {
        return LIMIT
      }
      val LIMIT = 10
      println(get_limit())
      """);
    Assert.False(script.RootModel.IsProgramMode);
    Diagnostic scriptError = Assert.Single(CheckerErrors(script));
    Assert.Equal(DiagnosticCodes.UndeclaredIdentifier, scriptError.Code);
    Assert.Contains("is not declared", scriptError.Message);
    Assert.Contains("LIMIT", scriptError.Message);

    // Program Mode: same rule, even though `main` runs after the `val` is initialized.
    Compilation program = Compile("""
      fn get_limit() int {
        return LIMIT
      }
      val LIMIT = 10
      fn main() {
        println(get_limit())
      }
      """);
    Assert.True(program.RootModel.IsProgramMode);
    Diagnostic programError = Assert.Single(CheckerErrors(program));
    Assert.Equal(DiagnosticCodes.UndeclaredIdentifier, programError.Code);
    Assert.Contains("is not declared", programError.Message);

    // `var` follows the same textual order (ticket 041, VAR-E8).
    Compilation variable = Compile("""
      fn get_count() int {
        return count
      }
      var count = 10
      """);
    Assert.Contains("is not declared", Assert.Single(CheckerErrors(variable)).Message);

    // A function declared below the binding sees it, in both modes.
    Compilation below = Compile("""
      val LIMIT = 10
      fn get_limit() int {
        return LIMIT
      }
      fn main() {
        println(get_limit())
      }
      """);
    Assert.Empty(CheckerErrors(below));

    // Nested blocks use the same strict order for locals declared after a nested function.
    Compilation nested = Compile("""
      fn outer() int {
        fn inner() int {
          return later
        }
        val later = 1
        return inner()
      }
      """);
    Assert.Contains("is not declared", Assert.Single(CheckerErrors(nested)).Message);
  }

  [Fact]
  public void ConformanceErrCases_PrimaryMessageContainsTheErrText() {
    string repoRoot = ParserTestHelpers.FindRepositoryRoot();
    string conformanceRoot = Path.Combine(repoRoot, "conformance");

    // Every case in this ticket's scope whose error is a checker error with a normative .err text
    // (OQ-42 legacy `TYPE CHECK ERROR` cases and calls/returns/constants belong to tickets 037-040).
    string[] cases = [
      "variables/err-annotation-mismatch",
      "variables/err-float-to-int",
      "variables/err-function-reads-later-val",
      "variables/err-function-reads-later-var",
      "variables/err-redeclare-same-scope",
      "variables/err-val-option-none",
      "variables/val-compound-assign-rejected",
      "variables/val-reassign-rejected",
      "variables/var-type-mismatch-rejected",
      "types/err-float-to-int-annotation",
      "types/err-void-variable",
      "types/err-null-removed",
      "strings/err-concat-non-string",
      "strings/immutable-index-assign-rejected",
      "conditionals/err-branch-type-mismatch",
      "conditionals/err-non-bool-condition",
      "entry-point/err-main-reads-later-val",
    ];

    foreach (string relative in cases) {
      string pinoPath = Path.Combine(conformanceRoot, relative + ".pino");
      string expected = File.ReadAllText(Path.ChangeExtension(pinoPath, ".err")).Trim();
      ParseResult parsed = Parser.Parse(new SourceText(File.ReadAllText(pinoPath), pinoPath));
      Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);

      Assert.True(
          CheckerErrors(compilation).Any(d => d.Message.Contains(expected, StringComparison.Ordinal)),
          $"{relative}: no checker error contains '{expected}'.");
    }

    // Legacy-mangled OQ-42 texts (`TYPE CHECK ERROR`) are handled in ticket 040; the checker still
    // rejects both cases.
    foreach (string relative in new[] { "maps/err-key-type", "maps/err-value-type" }) {
      string pinoPath = Path.Combine(conformanceRoot, relative + ".pino");
      Compilation compilation = Compilation.Create(
          Parser.Parse(new SourceText(File.ReadAllText(pinoPath), pinoPath)),
          FileSystemModuleSource.Instance);
      Assert.Contains(CheckerErrors(compilation), static d => d.Code == DiagnosticCodes.TypeMismatch);
    }
  }
}
