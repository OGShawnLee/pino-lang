using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

/// <summary>
/// Ticket 051: <c>val mut</c> bindings (<c>VAR-12</c>, <c>VAR-13</c>, <c>VAR-E12</c>, <c>VAR-E13</c>,
/// <c>MAIN-E3</c>, <c>MOD-E9</c>, <c>FN-21</c>) and inferred module-state purity facts
/// (<c>ReadsModuleState</c> / <c>WritesModuleState</c>, <c>D-183</c>).
/// </summary>
public sealed class CheckerValMutAndPurityTests {
  // ---------------------------------------------------------------------------------------------
  // `val mut` mutation, non-reassignment (VAR-E12), and mutable-interior check (VAR-E13)
  // ---------------------------------------------------------------------------------------------

  [Fact]
  public void ValMut_AllowsDeepMutationAndMutArgs_RespectsReadOnlyFields() {
    Compilation good = Compile("""
      struct Base {
        mut hp int
        id int
      }

      struct Hero {
        Base
        mut tags []string
        mut fn heal(amt int) {
          hp += amt
        }
      }

      interface Stepper {
        mut fn step() void
      }

      struct Counter {
        mut n int
        mut fn step() void {
          n += 1
        }
      }

      fn append(mut xs []int, item int) {
        xs:push(item)
      }

      @@Generic[T]
      fn wrap_generic(initial T) T {
        val mut box T = initial
        return box
      }

      val mut v = [1, 2]
      v:push(3)
      v[0] = 10
      val popped = v:pop()
      append(mut v, 4)

      val mut m map[string, int] = map[string, int] { "a" -> 1 }
      m["b"] = 2
      m["a"] += 5
      val removed = m:remove("b")

      val mut s = set[int] { 1 }
      s:add(2)
      s:remove(1)

      val mut h = Hero { hp: 10, id: 1, tags: ["a"] }
      h:hp += 5
      h:tags:push("b")
      h:heal(2)

      val mut st Stepper = Counter { n: 0 }
      st:step()

      val g_res = wrap_generic(42)
      """);

    Assert.Empty(CheckerErrors(good));

    LocalSymbol vLocal = Local(good, "v");
    Assert.True(vLocal.IsMutable);
    Assert.False(vLocal.IsReassignable);
    Assert.True(good.RootModel.IsAssignedAfterInit(vLocal));

    // Read-only struct field on `val mut` is still rejected with PN3221 (STRUCT-E15).
    Compilation badField = Compile("""
      struct Point {
        x int
        mut y int
      }
      val mut p = Point { x: 1, y: 2 }
      p:x = 9
      """);
    Diagnostic fieldErr = Assert.Single(CheckerErrors(badField));
    Assert.Equal(DiagnosticCodes.CannotMutateReadOnlyField, fieldErr.Code);
  }

  [Fact]
  public void ValMut_ReassignmentDirectAndCompound_EmitsVAR_E12() {
    Compilation direct = Compile("""
      val mut v = [1, 2]
      v = [3, 4]
      """);

    Diagnostic diag = Assert.Single(CheckerErrors(direct));
    Assert.Equal(DiagnosticCodes.CannotReassignValMut, diag.Code);
    Assert.Equal("Cannot reassign 'val mut' variable 'v'.", diag.Message);
    Assert.Equal("declare it with 'var' to allow reassignment", diag.Help);
    RelatedLocation rel = Assert.Single(diag.Related);
    Assert.Equal("'v' is declared with 'val mut' here", rel.Message);

    // Compound reassignment on the binding itself also emits VAR-E12.
    Compilation compound = Compile("""
      val mut n = 1
      n += 2
      """);

    Diagnostic[] compoundErrors = CheckerErrors(compound);
    Assert.Contains(compoundErrors, d =>
        d.Code == DiagnosticCodes.ValMutRequiresMutableInterior);
    Diagnostic compoundReassign = Assert.Single(compoundErrors, d => d.Code == DiagnosticCodes.CannotReassignValMut);
    Assert.Equal("Cannot reassign 'val mut' variable 'n'.", compoundReassign.Message);
    Assert.Equal("declare it with 'var' to allow reassignment", compoundReassign.Help);
  }

  [Fact]
  public void ValMut_TypesWithoutMutableInterior_EmitVAR_E13() {
    Compilation bad = Compile("""
      enum Color { Red, Blue }
      union Choice {
        A(int)
        B(string)
      }
      struct ReadOnlyBase { x int }
      struct ReadOnlyStruct {
        ReadOnlyBase
        items []int
      }
      interface ReadOnlyIface {
        name string
        fn describe() string
      }
      struct Named {
        name string
        fn describe() string { return name }
      }
      fn inc(x int) int { return x + 1 }
      fn pair() @(x int, y int) { return @(x: 1, y: 2) }

      val mut a = 0
      val mut b = 1.5
      val mut c = true
      val mut d = 'a'
      val mut e = "hi"
      val mut f = Color::Red
      val mut g = Choice::A(1)
      val mut h Option[int] = Option::Some(1)
      val mut i Option[int] = Option::None
      val mut j Result[int, string] = Result::Success(1)
      val mut k fn (int) -> int = inc
      val mut l = ReadOnlyStruct { x: 1, items: [1] }
      val mut m ReadOnlyIface = Named { name: "x" }
      val mut t = pair()
      """);

    Diagnostic[] errors = CheckerErrors(bad);
    Assert.Equal(14, errors.Length);
    Assert.All(errors, d => {
      Assert.Equal(DiagnosticCodes.ValMutRequiresMutableInterior, d.Code);
      Assert.Contains("has nothing to mutate", d.Message, StringComparison.Ordinal);
      Assert.Equal("use 'val' if it never changes, or 'var' to reassign it", d.Help);
    });

    // A4 error tolerance: an error-typed initializer must stay silent for VAR-E13.
    Compilation broken = Compile("""
      val mut x = undeclared_value
      """);
    Diagnostic[] brokenErrors = CheckerErrors(broken);
    Diagnostic onlyErr = Assert.Single(brokenErrors);
    Assert.Equal(DiagnosticCodes.UndeclaredIdentifier, onlyErr.Code);
  }

  [Fact]
  public void ValMut_PlacementVisibilityMutationPlacesAndCaptures_MatchVar() {
    // Program Mode root file rejects top-level `val mut` (MAIN-E3).
    Compilation progRoot = Compile("""
      val mut errors = []string
      fn main() {
        println(errors:len)
      }
      """);
    Diagnostic mainErr = Assert.Single(CheckerErrors(progRoot));
    Assert.Equal(DiagnosticCodes.InvalidTopLevelInProgramMode, mainErr.Code);
    Assert.Equal("Top-level variable 'errors' must be declared with 'val' in program mode.", mainErr.Message);

    // `pub val mut` and `@@Testable val mut` rejected (MOD-E9).
    Compilation visErrs = Compile("""
      pub val mut a = []int
      @@Testable val mut b = []int
      """);
    Diagnostic[] modErrors = CheckerErrors(visErrs);
    Assert.Equal(2, modErrors.Length);
    Assert.Equal(DiagnosticCodes.SymbolNotPublic, modErrors[0].Code);
    Assert.Contains("Top-level 'val mut' cannot be marked 'pub'", modErrors[0].Message, StringComparison.Ordinal);
    Assert.Contains("pub val", modErrors[0].Message, StringComparison.Ordinal);
    Assert.Contains("pub fn", modErrors[0].Message, StringComparison.Ordinal);
    Assert.Equal(DiagnosticCodes.SymbolNotPublic, modErrors[1].Code);
    Assert.Contains("Top-level 'val mut' cannot be marked '@@Testable'", modErrors[1].Message, StringComparison.Ordinal);

    // MutationPlaces (TopLevelVar & CapturedVar) + D-056 capture + ReachabilityBuilder.
    Compilation places = Compile("""
      val mut global_items = []int

      fn push_global(x int) {
        global_items:push(x)
      }

      fn make_pusher() fn (int) -> void {
        val mut local_items = []int
        return fn (x int) {
          local_items:push(x)
        }
      }
      """);
    Assert.Empty(CheckerErrors(places));

    CallExpressionSyntax globalPush = All<CallExpressionSyntax>(places)[0];
    MutationPlaceInfo? globalPlace = places.RootModel.GetMutationPlace(globalPush);
    Assert.NotNull(globalPlace);
    Assert.Equal(MutationKind.MutatingCall, globalPlace!.Kind);
    Assert.Equal(MutationRootKind.TopLevelVar, globalPlace.RootKind);

    CallExpressionSyntax capturedPush = All<CallExpressionSyntax>(places)[1];
    MutationPlaceInfo? capturedPlace = places.RootModel.GetMutationPlace(capturedPush);
    Assert.NotNull(capturedPlace);
    Assert.Equal(MutationKind.MutatingCall, capturedPlace!.Kind);
    Assert.Equal(MutationRootKind.CapturedVar, capturedPlace.RootKind);

    LambdaExpressionSyntax lambda = All<LambdaExpressionSyntax>(places).Single();
    CaptureList? captures = places.RootModel.GetCaptures(lambda);
    Assert.NotNull(captures);
    CaptureEntry entry = Assert.Single(captures!.Entries);
    Assert.Equal("local_items", entry.Symbol.Name);
    Assert.True(entry.IsMutable);
    Assert.True(entry.IsMutatedByClosure);
  }

  // ---------------------------------------------------------------------------------------------
  // Module-state purity facts (ReadsModuleState & WritesModuleState, D-183)
  // ---------------------------------------------------------------------------------------------

  [Fact]
  public void ModuleStateEffects_DirectReadsWritesAndPureFunctions_AreInferredWithoutDiagnostics() {
    Compilation comp = Compile("""
      val frozen_const = 42
      var counter = 0
      val mut errors = []string

      fn pure_math(a int, b int) int {
        val mut local_buf = [a, b]
        local_buf:push(frozen_const)
        return local_buf[0] + local_buf[1] + local_buf[2]
      }

      fn read_var() int {
        return counter
      }

      fn read_val_mut() int {
        return errors:len
      }

      fn write_var_only(n int) {
        counter = n
      }

      fn write_val_mut_only(msg string) {
        errors:push(msg)
      }

      fn write_val_mut_index_only(msg string) {
        errors[0] = msg
      }

      fn read_and_write_var() {
        counter += 1
      }

      fn read_and_write_pop() Option[string] {
        return errors:pop()
      }

      fn helper(mut xs []string) {
        xs:push("x")
      }

      fn mut_arg_on_global() {
        helper(mut errors)
      }
      """);

    Assert.Empty(comp.Diagnostics);

    SemanticModel model = comp.RootModel;
    Assert.Equal(ModuleStateEffects.Pure, model.GetModuleStateEffects(GetFunction(comp, "pure_math")));
    Assert.Equal(ModuleStateEffects.Pure, model.GetModuleStateEffects(GetFunction(comp, "helper")));
    Assert.Equal(ModuleStateEffects.ReadOnly, model.GetModuleStateEffects(GetFunction(comp, "read_var")));
    Assert.Equal(ModuleStateEffects.ReadOnly, model.GetModuleStateEffects(GetFunction(comp, "read_val_mut")));
    Assert.Equal(ModuleStateEffects.WriteOnly, model.GetModuleStateEffects(GetFunction(comp, "write_var_only")));
    Assert.Equal(ModuleStateEffects.WriteOnly, model.GetModuleStateEffects(GetFunction(comp, "write_val_mut_only")));
    Assert.Equal(ModuleStateEffects.WriteOnly, model.GetModuleStateEffects(GetFunction(comp, "write_val_mut_index_only")));
    Assert.Equal(ModuleStateEffects.ReadWrite, model.GetModuleStateEffects(GetFunction(comp, "read_and_write_var")));
    Assert.Equal(ModuleStateEffects.ReadWrite, model.GetModuleStateEffects(GetFunction(comp, "read_and_write_pop")));
    Assert.Equal(ModuleStateEffects.ReadWrite, model.GetModuleStateEffects(GetFunction(comp, "mut_arg_on_global")));
  }

  [Fact]
  public void ModuleStateEffects_TransitiveAndCrossModuleAndMethods_PropagateCorrectly() {
    var modules = new InMemoryModuleSource(new Dictionary<string, string>(StringComparer.Ordinal) {
      ["modules/State.pino"] = """
        module State

        val mut log = []string

        pub fn add(msg string) {
          log:push(msg)
        }

        pub fn count() int {
          return log:len
        }

        pub fn pure_helper(x int) int {
          return x * 2
        }

        pub struct BaseLogger {
          pub tag string
          pub fn emit(msg string) {
            add(msg)
          }
        }

        pub struct AppLogger {
          BaseLogger
        }
        """,
    });

    ParseResult rootTree = Parser.Parse(new SourceText("""
      import State

      fn wrap_add(s string) {
        State::add(s)
      }

      fn wrap_both(s string) int {
        wrap_add(s)
        return State::count()
      }

      fn wrap_pure(x int) int {
        return State::pure_helper(x)
      }

      fn use_embedded_method(l State::AppLogger) {
        l:emit("hi")
      }
      """, "main.pino"));
    Assert.Empty(rootTree.Diagnostics);

    Compilation comp = Compilation.Create(rootTree, modules);
    Assert.Empty(comp.Diagnostics);

    SemanticModel model = comp.RootModel;
    Assert.Equal(ModuleStateEffects.WriteOnly, model.GetModuleStateEffects(GetFunction(comp, "wrap_add")));
    Assert.Equal(ModuleStateEffects.ReadWrite, model.GetModuleStateEffects(GetFunction(comp, "wrap_both")));
    Assert.Equal(ModuleStateEffects.Pure, model.GetModuleStateEffects(GetFunction(comp, "wrap_pure")));
    Assert.Equal(ModuleStateEffects.WriteOnly, model.GetModuleStateEffects(GetFunction(comp, "use_embedded_method")));

    ModuleSymbol stateMod = comp.Modules.Values.Single(static m => m.Name == "State");
    StructSymbol appLogger = Assert.IsType<StructSymbol>(stateMod.Declarations.Single(static d => d.Name == "AppLogger"));
    MethodSymbol promotedEmit = appLogger.Methods.Single(static m => m.Name == "emit");
    Assert.Equal(ModuleStateEffects.WriteOnly, model.GetModuleStateEffects(promotedEmit));
  }

  [Fact]
  public void ModuleStateEffects_MutualRecursionSccFixedPoint_PropagatesAcrossCycle() {
    Compilation comp = Compile("""
      var state = 0

      fn rec_a(n int) int {
        if n <= 0 { return 0 }
        return rec_b(n - 1)
      }

      fn rec_b(n int) int {
        if n <= 0 { return state }
        return rec_a(n - 1)
      }

      fn rw_a(n int) {
        if n <= 0 { return }
        val x = state
        rw_b(n - 1)
      }

      fn rw_b(n int) {
        if n <= 0 {
          state = 9
          return
        }
        rw_a(n - 1)
      }

      fn pure_even(n int) bool {
        if n == 0 { return true }
        return pure_odd(n - 1)
      }

      fn pure_odd(n int) bool {
        if n == 0 { return false }
        return pure_even(n - 1)
      }

      fn caller_of_rec_a(n int) int {
        return rec_a(n)
      }
      """);

    Assert.Empty(comp.Diagnostics);
    SemanticModel model = comp.RootModel;

    Assert.Equal(ModuleStateEffects.ReadOnly, model.GetModuleStateEffects(GetFunction(comp, "rec_a")));
    Assert.Equal(ModuleStateEffects.ReadOnly, model.GetModuleStateEffects(GetFunction(comp, "rec_b")));
    Assert.Equal(ModuleStateEffects.ReadOnly, model.GetModuleStateEffects(GetFunction(comp, "caller_of_rec_a")));

    Assert.Equal(ModuleStateEffects.ReadWrite, model.GetModuleStateEffects(GetFunction(comp, "rw_a")));
    Assert.Equal(ModuleStateEffects.ReadWrite, model.GetModuleStateEffects(GetFunction(comp, "rw_b")));

    Assert.Equal(ModuleStateEffects.Pure, model.GetModuleStateEffects(GetFunction(comp, "pure_even")));
    Assert.Equal(ModuleStateEffects.Pure, model.GetModuleStateEffects(GetFunction(comp, "pure_odd")));
  }

  [Fact]
  public void ModuleStateEffects_FunctionValuesUnknownAndClosureDeclarerVsCaller() {
    Compilation comp = Compile("""
      var counter = 0
      val mut log = []int

      struct Holder {
        cb fn () -> void
      }

      interface Runner {
        fn run() void
      }

      fn make_closure() fn () -> void {
        return fn () {
          counter += 1
        }
      }

      fn call_param(f fn () -> void) {
        f()
      }

      fn call_field(h Holder) {
        h:cb()
      }

      fn call_lambda_local() {
        val f = fn () -> 1
        val r = f()
      }

      fn call_interface(r Runner) {
        r:run()
      }

      fn pure_vec_filter(xs []int) []int {
        return xs:filter(fn (x) -> x > 0)
      }

      fn pure_vec_map_it(xs []int) []int {
        return xs:map(it + 1)
      }

      fn mutating_vec_each(xs []int) {
        xs:each(fn (x) {
          log:push(x)
        })
      }

      fn unknown_vec_each(xs []int, cb fn (int) -> void) {
        xs:each(cb)
      }
      """);

    Assert.Empty(comp.Diagnostics);
    SemanticModel model = comp.RootModel;

    // Declaring a closure that mutates module state does NOT mark the declarer.
    Assert.Equal(ModuleStateEffects.Pure, model.GetModuleStateEffects(GetFunction(comp, "make_closure")));

    // Calling through any function value or interface marks both Reads and Writes true.
    Assert.Equal(ModuleStateEffects.ReadWrite, model.GetModuleStateEffects(GetFunction(comp, "call_param")));
    Assert.Equal(ModuleStateEffects.ReadWrite, model.GetModuleStateEffects(GetFunction(comp, "call_field")));
    Assert.Equal(ModuleStateEffects.ReadWrite, model.GetModuleStateEffects(GetFunction(comp, "call_lambda_local")));
    Assert.Equal(ModuleStateEffects.ReadWrite, model.GetModuleStateEffects(GetFunction(comp, "call_interface")));
    Assert.Equal(ModuleStateEffects.ReadWrite, model.GetModuleStateEffects(GetFunction(comp, "unknown_vec_each")));

    // Synchronous stdlib callbacks analyze inline lambdas / implicit `it` precisely.
    Assert.Equal(ModuleStateEffects.Pure, model.GetModuleStateEffects(GetFunction(comp, "pure_vec_filter")));
    Assert.Equal(ModuleStateEffects.Pure, model.GetModuleStateEffects(GetFunction(comp, "pure_vec_map_it")));
    Assert.Equal(ModuleStateEffects.WriteOnly, model.GetModuleStateEffects(GetFunction(comp, "mutating_vec_each")));
  }

  private static FunctionSymbol GetFunction(Compilation comp, string name) {
    FunctionDeclarationSyntax decl = All<FunctionDeclarationSyntax>(comp)
        .Single(fn => TextOf(comp, fn.NameToken) == name);
    return Assert.IsType<FunctionSymbol>(comp.RootModel.GetDeclaredSymbol(decl));
  }
}
