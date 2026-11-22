using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerStructAndInterfaceTests {
  [Fact]
  public void MethodScopes_D073AndD075_LocalShadowsField_SelfReachesField_BareSiblingCalls() {
    Compilation compilation = Compile("""
      struct Box {
        x int
        mut hp int

        mut fn move(x int) int {
          val local = self:x + x
          hp -= 5
          return local + area()
        }

        fn area() int {
          return x * 2
        }
      }

      var b = Box { x: 10, hp: 100 }
      val r = b:move(3)
      val f = b:area
      val via_bound = f()
      """);

    Assert.Empty(CheckerErrors(compilation));
    Assert.Same(compilation.Types.Int, Local(compilation, "r").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "via_bound").Type);
    Assert.Same(
        compilation.Types.GetFunction([], compilation.Types.Int),
        Local(compilation, "f").Type);

    // D-073: inside `move(x int)`, `x` resolves to ParameterSymbol while `self:x` resolves to FieldSymbol.
    List<IdentifierExpressionSyntax> idents = All<IdentifierExpressionSyntax>(compilation);
    IdentifierExpressionSyntax shadowedParam = idents.First(i =>
        TextOf(compilation, i.IdentifierToken) == "x"
        && compilation.RootModel.GetReferencedSymbol(i) is ParameterSymbol);
    Assert.IsType<ParameterSymbol>(compilation.RootModel.GetReferencedSymbol(shadowedParam));

    MemberAccessExpressionSyntax selfX = All<MemberAccessExpressionSyntax>(compilation)
        .First(m => TextOf(compilation, m.MemberToken) == "x");
    Assert.IsType<FieldSymbol>(compilation.RootModel.GetReferencedSymbol(selfX));

    // Bare field read (`x` in `area`) and bare field write (`hp -= 5`) resolve to FieldSymbol.
    Assert.Contains(
        idents,
        i => TextOf(compilation, i.IdentifierToken) == "x"
            && compilation.RootModel.GetReferencedSymbol(i) is FieldSymbol);
    Assert.Contains(
        idents,
        i => TextOf(compilation, i.IdentifierToken) == "hp"
            && compilation.RootModel.GetReferencedSymbol(i) is FieldSymbol);

    // D-075: bare sibling call `area()` records InstanceMethod with HasImplicitSelf = true.
    CallExpressionSyntax bareSiblingCall = All<CallExpressionSyntax>(compilation)
        .Single(c => c.Callee is IdentifierExpressionSyntax id && TextOf(compilation, id.IdentifierToken) == "area");
    CallResolution? siblingRes = compilation.RootModel.GetCallResolution(bareSiblingCall);
    Assert.NotNull(siblingRes);
    Assert.Equal(CallKind.InstanceMethod, siblingRes!.Kind);
    Assert.True(siblingRes.HasImplicitSelf);
    Assert.IsType<MethodSymbol>(siblingRes.TargetSymbol);

    // Explicit receiver call `b:move(3)` records InstanceMethod with HasImplicitSelf = false.
    CallExpressionSyntax receiverCall = All<CallExpressionSyntax>(compilation)
        .Single(c => c.Callee is MemberAccessExpressionSyntax m && TextOf(compilation, m.MemberToken) == "move");
    CallResolution? receiverRes = compilation.RootModel.GetCallResolution(receiverCall);
    Assert.NotNull(receiverRes);
    Assert.Equal(CallKind.InstanceMethod, receiverRes!.Kind);
    Assert.False(receiverRes.HasImplicitSelf);
  }

  [Fact]
  public void StructEmbeddingAndLiterals_D078D104OQ29_RequiresAllFieldsSupportsShorthandAndOverride() {
    Compilation compilation = Compile("""
      struct Shape {
        x int
        y int

        fn label() string {
          return "shape"
        }
      }

      struct Circle {
        Shape
        radius int

        fn label() string {
          return "circle"
        }

        fn scale() int {
          return x + radius
        }
      }

      val x = 10
      val c = Circle { x, y: 20, radius: 5 }
      val rx = c:x
      val lbl = c:label()
      val sc = c:scale()
      """);

    Assert.Empty(CheckerErrors(compilation));
    Assert.Same(compilation.Types.Int, Local(compilation, "rx").Type);
    Assert.Same(compilation.Types.String, Local(compilation, "lbl").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "sc").Type);

    // D-104: shorthand `Circle { x, ... }` records both the FieldSymbol and the LocalSymbol.
    StructFieldInitSyntax shorthandInit = All<StructFieldInitSyntax>(compilation)
        .Single(f => TextOf(compilation, f.NameToken) == "x");
    Assert.IsType<FieldSymbol>(compilation.RootModel.GetReferencedSymbol(shorthandInit));
    Assert.IsType<LocalSymbol>(compilation.RootModel.GetShorthandValueSymbol(shorthandInit));

    // OQ-29 / STRUCT-E12: omitting an embedded field emits PN3207 "Missing property" with RelatedLocation.
    Compilation missingEmbedded = Compile("""
      struct Shape {
        x int
        y int
      }
      struct Circle {
        Shape
        radius int
      }
      val c = Circle { x: 10, radius: 5 }
      """);

    Diagnostic missingDiag = Assert.Single(CheckerErrors(missingEmbedded));
    Assert.Equal(DiagnosticCodes.MissingStructField, missingDiag.Code);
    Assert.Contains("Missing property", missingDiag.Message);
    Assert.Contains("'y'", missingDiag.Message);
    Assert.NotEmpty(missingDiag.Related);

    // Unknown and duplicate fields in struct literals are rejected.
    Compilation unknownAndDup = Compile("""
      struct Point { x int }
      val p = Point { x: 1, x: 2, z: 3 }
      """);
    Diagnostic[] dupErrs = CheckerErrors(unknownAndDup);
    Assert.Equal(2, dupErrs.Length);
    Assert.Contains(dupErrs, static d => d.Code == DiagnosticCodes.DuplicateDeclaration);
    Assert.Contains(dupErrs, static d => d.Code == DiagnosticCodes.UnknownMember);
  }

  [Fact]
  public void StructuralInterfaces_D083OQ30_IgnoresParamNamesRecordsConversionAndChecksSignatures() {
    Compilation ok = Compile("""
      interface Greeter {
        name string
        fn greet(person string) string
      }

      struct Polite {
        name string

        fn greet(who string) string {
          return "hi $who"
        }
      }

      fn welcome(g Greeter) string {
        return g:name + ": " + g:greet("Ana")
      }

      val p = Polite { name: "Bob" }
      val msg = welcome(p)
      val g_val Greeter = p
      var g_var Greeter = Polite { name: "Sue" }
      g_var = p
      val same = g_val == p
      """);

    Assert.Empty(CheckerErrors(ok));
    Assert.Same(compilationType(ok, "string"), Local(ok, "msg").Type);
    Assert.Same(ok.Types.Bool, Local(ok, "same").Type);

    // StructToInterface conversion is recorded on call argument, val initializer, var initializer, and assignment.
    CallExpressionSyntax welcomeCall = All<CallExpressionSyntax>(ok)
        .Single(c => c.Callee is IdentifierExpressionSyntax id && TextOf(ok, id.IdentifierToken) == "welcome");
    ConversionInfo? argConv = ok.RootModel.GetConversion(welcomeCall.Arguments[0].Expression);
    Assert.NotNull(argConv);
    Assert.Equal(ConversionKind.StructToInterface, argConv!.Kind);
    Assert.IsType<InterfaceType>(argConv.TargetInterface);

    ConversionInfo? initConv = ok.RootModel.GetConversion(Declaration(ok, "g_val").Initializer);
    Assert.NotNull(initConv);
    Assert.Equal(ConversionKind.StructToInterface, initConv!.Kind);

    AssignmentExpressionSyntax assign = All<AssignmentExpressionSyntax>(ok).Single();
    ConversionInfo? assignConv = ok.RootModel.GetConversion(assign.Value);
    Assert.NotNull(assignConv);
    Assert.Equal(ConversionKind.StructToInterface, assignConv!.Kind);

    static TypeSymbol compilationType(Compilation c, string kind) =>
        kind == "string" ? c.Types.String : c.Types.Error;
  }

  [Fact]
  public void CrossModuleVisibility_D163D062D064OQ31_EnforcesPrivateFieldsAndFunctions() {
    var source = new InMemoryModuleSource();
    source.AddFile("proj/modules/Ledger.pino", """
      module Ledger

      pub struct Entry {
        pub label string
        amount int
      }

      pub interface HasAmount {
        amount int
      }

      pub fn make_entry(label string, amount int) Entry {
        return Entry { label: label, amount: amount }
      }

      fn secret_fee() int {
        return 3
      }

      @@Testable fn audit_fee() int {
        return 9
      }
      """);

    // 1. Cross-module read, write, literal on private field, private function call, and @@Testable from non-test.
    const string BadApp = """
      import Ledger

      val e1 = Ledger::make_entry("rent", 900)
      val lbl = e1:label
      e1:label = "food"
      val amt = e1:amount
      e1:amount = 100
      val e2 = Ledger::Entry { label: "x", amount: 1 }
      val e3 = Ledger::Entry { label: "x" }
      val f1 = Ledger::secret_fee()
      val f2 = Ledger::audit_fee()

      fn take_amount(h Ledger::HasAmount) {}
      take_amount(e1)
      """;

    Compilation bad = Compilation.Create(Parser.Parse(new SourceText(BadApp, "proj/app.pino")), source);
    Diagnostic[] errors = CheckerErrors(bad);

    // Private field read, write, literal with field, literal omitting private field: all PN3009 "is private" with RelatedLocation.
    Diagnostic[] privFieldErrors = errors.Where(static d => d.Code == DiagnosticCodes.PrivateFieldAccess).ToArray();
    Assert.Equal(4, privFieldErrors.Length);
    Assert.All(privFieldErrors, static d => {
      Assert.Contains("is private", d.Message);
      Assert.NotEmpty(d.Related);
      Assert.Equal("proj/modules/Ledger.pino", d.Related[0].FilePath);
    });

    // Private fn + @@Testable fn from non-test: PN3007.
    Diagnostic[] privFnErrors = errors.Where(static d => d.Code == DiagnosticCodes.SymbolNotPublic).ToArray();
    Assert.Equal(2, privFnErrors.Length);
    Assert.All(privFnErrors, static d => Assert.Contains("is private", d.Message));

    // Cross-module private field cannot satisfy interface property.
    Assert.Contains(errors, static d => d.Code == DiagnosticCodes.InterfaceNotSatisfied && d.Message.Contains("expected type 'HasAmount'"));

    // 2. Test file can call @@Testable function and valid pub members with 0 errors.
    const string GoodTest = """
      import Ledger
      val e = Ledger::make_entry("ok", 10)
      val x = e:label
      val fee = Ledger::audit_fee()
      """;
    Compilation good = Compilation.Create(Parser.Parse(new SourceText(GoodTest, "proj/app_test.pino")), source);
    Assert.Empty(CheckerErrors(good));
  }

  [Fact]
  public void CallableFieldsStaticMethodsAndIndexing_CheckCallsAndErrors() {
    Compilation ok = Compile("""
      struct Counter {
        base int
        inc fn (int) -> int
        items []int

        static fn create(v int) Counter {
          return Counter {
            base: v,
            inc: fn (x int) -> x + 1,
            items: [1, 2, 3]
          }
        }
      }

      val factory = Counter::create
      val c = factory(5)
      val next = c:inc(10)
      val second = c:items[1]
      """);

    Assert.Empty(CheckerErrors(ok));
    Assert.Same(ok.Types.Int, Local(ok, "next").Type);
    Assert.Same(ok.Types.Int, Local(ok, "second").Type);

    CallExpressionSyntax incCall = All<CallExpressionSyntax>(ok)
        .Single(c => c.Callee is MemberAccessExpressionSyntax m && TextOf(ok, m.MemberToken) == "inc");
    CallResolution? incRes = ok.RootModel.GetCallResolution(incCall);
    Assert.NotNull(incRes);
    Assert.Equal(CallKind.CallableField, incRes!.Kind);
    Assert.IsType<FieldSymbol>(incRes.TargetSymbol);
  }

  [Fact]
  public void StructAndInterfaceConformanceErrCases_MatchRequiredErrTexts() {
    string repoRoot = ParserTestHelpers.FindRepositoryRoot();
    string conformanceRoot = Path.Combine(repoRoot, "conformance");

    string[] cases = [
      "structs/err-missing-field.pino",
      "structs/err-missing-method.pino",
      "structs/err-this-in-static.pino",
      "structs/err-field-in-static.pino",
      "structs/err-static-via-instance.pino",
      "structs/err-instance-via-static.pino",
      "structs/err-fn-field-arg-type.pino",
      "structs/err-fn-field-arg-count.pino",
      "structs/err-call-non-function-field.pino",
      "structs/err-field-type-mismatch.pino",
      "structs/err-literal-omits-field.pino",
      "structs/err-literal-omits-embedded-field.pino",
      "interfaces/err-missing-method.pino",
      "interfaces/err-missing-property.pino",
      "interfaces/err-property-type-mismatch.pino",
      "interfaces/err-return-type-mismatch.pino",
      "interfaces/err-signature-mismatch.pino",
      "interfaces/err-unknown-field-on-interface.pino",
      "interfaces/err-unknown-method-on-interface.pino",
      "modules/err-private-field.pino",
      "modules/err-private-field-literal.pino",
      "modules/err-private-function-call.pino",
      "modules/err-private-struct.pino",
      "modules/err-missing-struct.pino",
    ];

    foreach (string relPath in cases) {
      string fullPath = Path.Combine(conformanceRoot, relPath);
      string expectedText = File.ReadAllText(Path.ChangeExtension(fullPath, ".err")).Trim();
      ParseResult parsed = Parser.Parse(new SourceText(File.ReadAllText(fullPath), fullPath));
      Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);

      Assert.Contains(
          compilation.Diagnostics,
          d => d.Severity == DiagnosticSeverity.Error
              && d.Message.Contains(expectedText, StringComparison.Ordinal));
    }

    // Positive struct & interface cases check with 0 errors.
    string[] positives = [
      "structs/embedding-fields.pino",
      "structs/embedding-methods.pino",
      "structs/embedding-override.pino",
      "interfaces/exact-signature-match.pino",
      "interfaces/parameter-names-ignored.pino",
    ];
    foreach (string relPath in positives) {
      string fullPath = Path.Combine(conformanceRoot, relPath);
      Compilation compilation = Compilation.Create(
          Parser.Parse(new SourceText(File.ReadAllText(fullPath), fullPath)),
          FileSystemModuleSource.Instance);
      Assert.Empty(CheckerErrors(compilation));
    }
  }

  [Fact]
  public void RecursiveStructValue_DirectSelfField_ReportsPN3114WithReadableText() {
    Compilation compilation = Compile("struct Bad { x Bad }\n");

    Diagnostic error = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.RecursiveStructValue, error.Code);
    Assert.Equal("Struct 'Bad' cannot contain itself directly because it would have infinite size.", error.Message);
    Assert.Equal(
        "wrap the field in 'Option[Bad]', store it in '[]Bad' or a recursive union, or use 'Handle[Bad]' with a 'Pool[Bad]'",
        error.Help);
    RelatedLocation related = Assert.Single(error.Related);
    Assert.Equal("field 'x' refers back to 'Bad' here", related.Message);
  }

  [Fact]
  public void RecursiveStructValue_MutualTwoStructs_ReportsOncePerStruct() {
    Compilation compilation = Compile("struct A { b B }\nstruct B { a A }\n");

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(2, errors.Length);
    Assert.All(errors, static e => Assert.Equal(DiagnosticCodes.RecursiveStructValue, e.Code));
    Assert.Contains(errors, static e => e.Message == "Struct 'A' cannot contain itself, even through 'B', because it would have infinite size.");
    Assert.Contains(errors, static e => e.Message == "Struct 'B' cannot contain itself, even through 'A', because it would have infinite size.");
  }

  [Fact]
  public void RecursiveStructValue_MutualThreeStructs_ReportsOncePerStruct() {
    Compilation compilation = Compile("struct A { b B }\nstruct B { c C }\nstruct C { a A }\n");

    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(3, errors.Length);
    Assert.All(errors, static e => Assert.Equal(DiagnosticCodes.RecursiveStructValue, e.Code));
    Assert.Contains(errors, static e => e.Message == "Struct 'A' cannot contain itself, even through 'B', because it would have infinite size.");
  }

  [Fact]
  public void RecursiveStructValue_GenericStructContainingItself_IsRejected() {
    Compilation compilation = Compile("@@Generic[T]\nstruct Box { x Box[T] }\n");

    Diagnostic error = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.RecursiveStructValue, error.Code);
    Assert.Equal("Struct 'Box' cannot contain itself directly because it would have infinite size.", error.Message);
  }

  [Fact]
  public void RecursiveStructValue_EmbeddedStructThroughField_IsRejected() {
    Compilation compilation = Compile("struct Base { owner Child }\nstruct Child { Base }\n");

    Assert.NotEmpty(CheckerErrors(compilation));
    Assert.All(CheckerErrors(compilation), static e => Assert.Equal(DiagnosticCodes.RecursiveStructValue, e.Code));
  }

  [Fact]
  public void RecursiveStructValue_BrokenThroughIndirection_IsValid() {
    Compilation compilation = Compile("""
      struct OptNode { next Option[OptNode] }
      struct VecNode { kids []VecNode }
      struct MapNode { kids map[string, MapNode] }
      struct SetNode { seen set[SetNode] }
      struct PoolNode { pool Pool[PoolNode] }
      struct Item { parent Handle[Item] }
      struct FnNode { visit fn (FnNode) -> int }
      union List {
        Cons(int, List)
        Nil
      }
      struct Holder { list List }
      """);

    Assert.Empty(CheckerErrors(compilation));
  }
}
