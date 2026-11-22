using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerMutabilityTests {
  [Fact]
  public void DeepValImmutability_VAR_E10_RejectsNestedWritesAndMutatingCallsAtArbitraryDepth() {
    Compilation bad = Compile("""
      struct Player {
        mut hp int
        mut fn heal(amount int) {
          hp += amount
        }
      }

      struct Game {
        mut player Player
        mut items []int
        mut scores map[string, int]
        mut tags set[int]
        mut grid [][]int
      }

      val g = Game {
        player: Player { hp: 100 },
        items: [1, 2, 3],
        scores: map[string, int] { "a" -> 10 },
        tags: set[int] { 1, 2 },
        grid: [[10, 20]]
      }

      g:player:hp -= 1
      g:player:heal(5)
      g:items:push(1)
      g:items:pop()
      g:items[0] = 9
      g:scores["a"] += 1
      g:scores:remove("a")
      g:tags:add(3)
      g:tags:remove(1)
      g:grid[0][0] = 99
      """);

    Diagnostic[] errors = CheckerErrors(bad);
    Assert.Equal(10, errors.Length);
    Assert.All(errors, d => {
      Assert.Equal(DiagnosticCodes.CannotMutateImmutableValue, d.Code);
      Assert.Contains("Cannot mutate immutable variable 'g' of type 'Game'.", d.Message);
      Assert.Equal("declare 'g' with 'val mut' to change its contents, or with 'var' to also reassign it", d.Help);
      Assert.Single(d.Related);
    });

    // Same paths succeed on `var g` when every traversed struct field is `mut`.
    Compilation good = Compile("""
      struct Player {
        mut hp int
        mut fn heal(amount int) {
          hp += amount
        }
      }

      struct Game {
        mut player Player
        mut items []int
        mut scores map[string, int]
        mut tags set[int]
        mut grid [][]int
      }

      var g = Game {
        player: Player { hp: 100 },
        items: [1, 2, 3],
        scores: map[string, int] { "a" -> 10 },
        tags: set[int] { 1, 2 },
        grid: [[10, 20]]
      }

      g:player:hp -= 1
      g:player:heal(5)
      g:items:push(1)
      val last = g:items:pop()
      g:items[0] = 9
      g:scores["a"] += 1
      val removed = g:scores:remove("a")
      g:tags:add(3)
      g:tags:remove(1)
      g:grid[0][0] = 99
      """);

    Assert.Empty(CheckerErrors(good));
  }

  [Fact]
  public void ReadOnlyFieldsEmbeddedMutAndCrossModulePubMut_STRUCT_E15AndD163_EnforceFieldMutability() {
    Compilation bad = Compile("""
      struct Inner {
        ro_val int
        mut rw_val int
      }

      struct Bag {
        items []int
        inner Inner
        mut rw_inner Inner
      }

      struct Base {
        ro_base int
        mut rw_base int
      }

      struct Derived {
        Base
        ro_own int
        mut rw_own int
      }

      var b = Bag {
        items: [1, 2],
        inner: Inner { ro_val: 1, rw_val: 2 },
        rw_inner: Inner { ro_val: 3, rw_val: 4 }
      }
      b:items = [3]
      b:items:push(4)
      b:items[0] = 5
      b:inner:rw_val = 10
      b:rw_inner:ro_val = 20

      var d = Derived { ro_base: 1, rw_base: 2, ro_own: 3, rw_own: 4 }
      d:ro_base = 10
      d:ro_own = 20
      d:rw_base = 30
      d:rw_own = 40
      """);

    Diagnostic[] errors = CheckerErrors(bad);
    Assert.Equal(7, errors.Length);
    Assert.All(errors, static d => {
      Assert.Equal(DiagnosticCodes.CannotMutateReadOnlyField, d.Code);
      Assert.Single(d.Related);
      Assert.Contains("add 'mut' to field", d.Help);
    });
    Assert.Contains("field 'items' of struct 'Bag'", errors[0].Message);
    Assert.Contains("field 'items' of struct 'Bag'", errors[1].Message);
    Assert.Contains("field 'items' of struct 'Bag'", errors[2].Message);
    Assert.Contains("field 'inner' of struct 'Bag'", errors[3].Message);
    Assert.Contains("field 'ro_val' of struct 'Inner'", errors[4].Message);
    Assert.Contains("field 'ro_base' of struct 'Derived'", errors[5].Message);
    Assert.Contains("in struct 'Base'", errors[5].Help);
    Assert.Contains("field 'ro_own' of struct 'Derived'", errors[6].Message);

    // Cross-module `pub` vs `pub mut`, `pub val` deep immutability, private module `var`, and `@@Testable var`.
    var source = new InMemoryModuleSource();
    source.AddFile("proj/modules/Store.pino", """
      module Store

      pub struct Config {
        pub name string
        pub mut retries int
      }

      pub val DEFAULTS = [1, 2, 3]
      var private_counter = 0

      pub fn bump() int {
        private_counter += 1
        return private_counter
      }
      """);

    Compilation cross = Compilation.Create(
        Parser.Parse(new SourceText("""
          import Store

          var cfg = Store::Config { name: "api", retries: 3 }
          cfg:retries = 5
          cfg:name = "bad"
          Store::DEFAULTS:push(4)
          val n = Store::bump()
          """, "proj/app.pino")),
        source);

    Diagnostic[] crossErrors = CheckerErrors(cross);
    Assert.Equal(2, crossErrors.Length);
    Assert.Equal(DiagnosticCodes.CannotMutateReadOnlyField, crossErrors[0].Code);
    Assert.Contains("field 'name' of struct 'Config'", crossErrors[0].Message);
    Assert.Equal("proj/modules/Store.pino", crossErrors[0].Related[0].FilePath);
    Assert.Equal(DiagnosticCodes.CannotMutateImmutableValue, crossErrors[1].Code);
    Assert.Contains("variable 'DEFAULTS'", crossErrors[1].Message);

    // MOD-E9: @@Testable var is rejected with clear guidance.
    Compilation testableVar = Compile("@@Testable var secret = 0\n");
    Diagnostic testableErr = Assert.Single(CheckerErrors(testableVar));
    Assert.Equal(DiagnosticCodes.SymbolNotPublic, testableErr.Code);
    Assert.Contains("use '@@Testable val' or a '@@Testable fn'", testableErr.Message);
  }

  [Fact]
  public void MutatingMethodsSelfRulesAndBoundMutFn_STRUCT_E16AndE17_EnforceAllCases() {
    Compilation bad = Compile("""
      struct Counter {
        mut count int
        mut items []int
        ro int

        fn bad_self_field() {
          self:count += 1
        }

        fn bad_bare_field() {
          count += 1
        }

        fn bad_self_collection() {
          self:items:push(1)
        }

        fn bad_bare_collection() {
          items:push(1)
        }

        fn bad_bare_sibling() {
          inc()
        }

        fn bad_self_sibling() {
          self:inc()
        }

        fn bad_reassign_self_plain() {
          self = Counter { count: 0, items: []int, ro: 0 }
        }

        mut fn bad_reassign_self_mut() {
          self = Counter { count: 0, items: []int, ro: 0 }
        }

        mut fn bad_ro_field() {
          ro = 1
        }

        mut fn inc() {
          count += 1
        }

        fn read() int {
          return count
        }

        mut fn bad_bind_bare() {
          val f = inc
        }
      }

      val ro_counter = Counter { count: 0, items: []int, ro: 1 }
      ro_counter:inc()

      var rw_counter = Counter { count: 0, items: []int, ro: 1 }
      val ok_bound = rw_counter:read
      val ok_val = ok_bound()
      val bad_bound = rw_counter:inc

      var nums = [1, 2]
      val bad_stdlib_bound = nums:push
      """);

    Diagnostic[] errors = CheckerErrors(bad);
    Assert.Equal(13, errors.Length);

    // 6 PN3222 errors inside non-mutating methods.
    Diagnostic[] pn3222 = errors.Where(static d => d.Code == DiagnosticCodes.CannotMutateSelfInNonMutMethod).ToArray();
    Assert.Equal(6, pn3222.Length);
    Assert.All(pn3222, static d => {
      Assert.Contains("Cannot mutate 'self' inside non-mutating method 'Counter:", d.Message);
      Assert.Contains("mark the method as 'mut fn", d.Help);
      Assert.Single(d.Related);
    });

    // 2 PN3201 errors for `self = ...` (both plain fn and mut fn).
    Diagnostic[] pn3201 = errors.Where(static d => d.Code == DiagnosticCodes.CannotAssignToImmutable).ToArray();
    Assert.Equal(2, pn3201.Length);
    Assert.All(pn3201, static d => Assert.Equal("Cannot reassign 'self'.", d.Message));

    // 1 PN3221 error for bare read-only field `ro = 1` inside `mut fn`.
    Diagnostic pn3221 = Assert.Single(errors, static d => d.Code == DiagnosticCodes.CannotMutateReadOnlyField);
    Assert.Contains("field 'ro' of struct 'Counter'", pn3221.Message);

    // 1 PN3220 error for `ro_counter:inc()` on a `val`.
    Diagnostic pn3220 = Assert.Single(errors, static d => d.Code == DiagnosticCodes.CannotMutateImmutableValue);
    Assert.Contains("variable 'ro_counter'", pn3220.Message);

    // 3? Wait: bad_bind_bare + bad_bound + bad_stdlib_bound = 3 -> total would be 13! Let's check exact count.
    Diagnostic[] pn3227 = errors.Where(static d => d.Code == DiagnosticCodes.CannotBindMutatingMethod).ToArray();
    Assert.Equal(3, pn3227.Length);
    Assert.Contains(pn3227, static d => d.Message.Contains("Counter:inc") && d.Help!.Contains("fn () { inc() }"));
    Assert.Contains(pn3227, static d => d.Message.Contains("Counter:inc") && d.Help!.Contains("fn () { rw_counter:inc() }"));
    Assert.Contains(pn3227, static d => d.Message.Contains("[]int:push"));
  }

  [Fact]
  public void InterfacesAndBoundedGenerics_IFACE_E7AndGEN9_EnforceMutPropsAndMutMethods() {
    Compilation good = Compile("""
      interface Stepper {
        ro_id int
        mut count int
        fn value() int
        mut fn step() void
      }

      struct Bot {
        ro_id int
        mut count int
        fn value() int { return count }
        mut fn step() { count += 1 }
      }

      fn advance_iface(mut s Stepper) int {
        s:count += 1
        s:step()
        return s:value()
      }

      @@Generic[T is Stepper]
      fn advance_generic(mut s T) int {
        s:count += 1
        s:step()
        return s:value()
      }

      @@Generic[T is Stepper]
      fn read_generic(s T) int {
        return s:ro_id + s:value()
      }

      var iface_val Stepper = Bot { ro_id: 1, count: 0 }
      val a = advance_iface(mut iface_val)
      var bot = Bot { ro_id: 2, count: 10 }
      val b = advance_generic(mut bot)
      val c = read_generic(bot)
      """);

    Assert.Empty(CheckerErrors(good));

    Compilation bad = Compile("""
      interface Stepper {
        ro_id int
        ro_items []int
        mut count int
        fn value() int
        mut fn step() void
      }

      fn bad_iface_ro_prop(mut s Stepper) {
        s:ro_id = 5
        s:ro_items:push(1)
      }

      fn bad_iface_ro_param(s Stepper) {
        s:count = 1
        s:step()
      }

      fn bad_iface_bind(mut s Stepper) {
        val f = s:step
      }

      @@Generic[T is Stepper]
      fn bad_generic_ro_param(s T) {
        s:count = 1
        s:step()
      }

      @@Generic[T is Stepper]
      fn bad_generic_ro_prop(mut s T) {
        s:ro_id = 1
      }
      """);

    Diagnostic[] errors = CheckerErrors(bad);
    Assert.Equal(8, errors.Length);
    Assert.Equal(3, errors.Count(static d => d.Code == DiagnosticCodes.CannotMutateReadOnlyField));
    Assert.Equal(4, errors.Count(static d => d.Code == DiagnosticCodes.CannotMutateImmutableValue));
    Assert.Single(errors, static d => d.Code == DiagnosticCodes.CannotBindMutatingMethod);
  }

  [Fact]
  public void ParametersCallSiteMutAndIndirectCalls_FN_E14AndE15_EnforceAllRules() {
    Compilation good = Compile("""
      struct Point {
        mut x int
        mut y int
      }

      fn bump(mut p Point, dx int) {
        p:x += dx
      }

      @@Generic[T]
      fn swap(mut a T, mut b T) {
        val tmp = a
        a = b
        b = tmp
      }

      var a = Point { x: 1, y: 2 }
      var b = Point { x: 3, y: 4 }
      bump(mut a, 10)
      swap(mut a, mut b)
      swap[Point](mut a, mut b)

      val cb fn (mut Point, int) -> void = fn (p, d) { p:x += d }
      cb(mut a, 5)
      """);

    Assert.Empty(CheckerErrors(good));

    Compilation bad = Compile("""
      struct Point {
        mut x int
        y int
      }

      fn bad_param_reassign(n int) {
        n = 2
      }

      fn bad_param_interior(p Point, xs []int) {
        p:x = 5
        xs:push(1)
      }

      fn need_mut(mut p Point) {
        p:x += 1
      }

      fn read_only(p Point) int {
        return p:x
      }

      fn need_mut_float(mut f float) {
        f += 1.0
      }

      @@Generic[T]
      fn gen_mut(mut v T) {}

      var p = Point { x: 1, y: 2 }
      val ro = Point { x: 1, y: 2 }
      var n = 1
      need_mut(p)
      read_only(mut p)
      need_mut(mut ro)
      need_mut_float(mut n)
      gen_mut(p)
      println(mut p)
      val opt = Option::Some(mut n)
      """);

    Diagnostic[] errors = CheckerErrors(bad);
    Assert.Equal(10, errors.Length);
    Assert.Contains(errors, static d => d.Code == DiagnosticCodes.CannotAssignToImmutable && d.Message == "Cannot reassign immutable parameter 'n'.");
    Assert.Equal(2, errors.Count(static d => d.Code == DiagnosticCodes.CannotMutateImmutableValue && d.Message.Contains("immutable parameter")));
    Assert.Equal(2, errors.Count(static d => d.Code == DiagnosticCodes.MissingMutAtCallSite));
    Assert.Equal(3, errors.Count(static d => d.Code == DiagnosticCodes.UnexpectedMutAtCallSite));
    Assert.Contains(errors, static d => d.Code == DiagnosticCodes.CannotMutateImmutableValue && d.Message.Contains("immutable variable 'ro'"));
    // Invariance: `int` variable cannot widen to `mut float`.
    Assert.Contains(errors, static d => d.Code == DiagnosticCodes.TypeMismatch && d.Message == "A 'mut' argument must have exactly the parameter's type 'float', but 'n' is 'int'.");
  }

  [Fact]
  public void PN3226Messages_FN_E16_NameElementOrValueAndTarget() {
    Compilation bad = Compile("""
      struct P {
        mut x int
      }

      fn f(mut a int, mut b int) {}
      fn g(mut a P, mut b int) {}

      var v = [1, 2]
      var p = P { x: 1 }
      f(mut v[0], mut v[0])
      g(mut p, mut p:x)
      f(mut p:x, mut p:x)
      val h = fn (mut a int, mut b int) {}
      h(mut p:x, mut p:x)
      """);

    Diagnostic[] errors = CheckerErrors(bad);
    Assert.Equal(
        [
          "Cannot pass the same element of 'v' as two 'mut' arguments to 'f'.",
          "Cannot use 'p' twice as 'mut' in one call to 'g'.",
          "Cannot use 'p:x' twice as 'mut' in one call to 'f'.",
          "Cannot use 'p:x' twice as 'mut' in one call to this function.",
        ],
        errors.Select(static d => d.Message).ToArray());
    Assert.Equal("use two different elements, or make one argument a plain read", errors[0].Help);
    Assert.All(errors.Skip(1), static d => Assert.Equal("pass it once as 'mut', or pass a copy for the other argument", d.Help));
  }

  [Fact]
  public void MutArgumentExactType_FN_37_ReportsPN3102WithPlainMessage() {
    Compilation bad = Compile("""
      fn grow(mut x float) {}
      var n = 1
      grow(mut n)
      """);

    Diagnostic d = Assert.Single(CheckerErrors(bad));
    Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code);
    Assert.Equal("A 'mut' argument must have exactly the parameter's type 'float', but 'n' is 'int'.", d.Message);
    Assert.Equal("declare 'n' with type 'float', for example 'var n float = ...'", d.Help);
  }

  [Fact]
  public void Exclusivity_PN3226AndFN31_RejectsMutMutSameRootAndAllowsMutPlusRead() {
    // FN-31 (OQ-043-1 router resolution): mut + read on the same root IS ALLOWED.
    Compilation allowed = Compile("""
      struct Point {
        mut x int
        mut fn add(delta int) {
          x += delta
        }
      }

      fn add_into(mut dst int, src int) {
        dst += src
      }

      fn add_into_rev(src int, mut dst int) {
        dst += src
      }

      var a = 10
      add_into(mut a, a)
      add_into_rev(a, mut a)

      var v = [1, 2, 3]
      v:push(v[0])

      var p = Point { x: 5 }
      p:add(p:x)
      """);

    Assert.Empty(CheckerErrors(allowed));

    // PN3226 / FN-E16 (refined by D-184): proven-overlapping mut + mut on the same root (including mutating receiver) IS REJECTED.
    Compilation rejected = Compile("""
      struct Pair {
        mut x int
        mut y int

        mut fn merge(mut other Pair) {
          x += other:x
        }

        mut fn self_overlap() {
          merge(mut self)
          self:merge(mut self)
          swap_ints(mut x, mut self:x)
        }
      }

      fn swap_ints(mut a int, mut b int) {
        val t = a
        a = b
        b = t
      }

      @@Generic[T]
      fn swap_gen(mut a T, mut b T) {
        val t = a
        a = b
        b = t
      }

      fn take_both(mut p Pair, mut x int) {
        p:x += x
      }

      var a = 1
      swap_ints(mut a, mut a)
      swap_gen(mut a, mut a)

      var p = Pair { x: 1, y: 2 }
      take_both(mut p, mut p:x)
      p:merge(mut p)

      var v = [1, 2, 3]
      val i = 0
      swap_ints(mut v[0], mut v[0])
      swap_ints(mut v[i], mut v[i])
      """);

    Diagnostic[] errors = CheckerErrors(rejected);
    Assert.Equal(9, errors.Length);
    Assert.All(errors, static d => {
      Assert.Equal(DiagnosticCodes.OverlappingMutArguments, d.Code);
      Assert.True(
          d.Message.StartsWith("Cannot pass the same element of '", StringComparison.Ordinal)
          || d.Message.StartsWith("Cannot use '", StringComparison.Ordinal),
          d.Message);
      Assert.Contains("'mut'", d.Message);
      Assert.Single(d.Related);
      Assert.Contains("already mutably borrowed here", d.Related[0].Message);
    });
  }

  [Fact]
  public void ClosureCaptures_D056AndFN_E17_TracksInteriorVarMutationAndRejectsMutParamAndMutSelf() {
    // 1. Captured `var` mutated internally sets IsMutatedByClosure = true AND AssignedBindings.
    Compilation ok = Compile("""
      struct Bag {
        mut limit int
        mut items []int

        mut fn filter_above() []int {
          val f = fn () { return self:limit }
          val g = fn () { return limit }
          val _unused = f() + g()
          return self:items:filter(it > self:limit)
        }
      }

      fn make_collector() fn (int) -> void {
        var acc = []int
        return fn (x int) {
          acc:push(x)
        }
      }

      fn own_mut_param_lambda() {
        val inc fn (mut int) -> void = fn (mut n int) {
          n += 1
        }
        var x = 1
        inc(mut x)
      }
      """);

    Assert.Empty(CheckerErrors(ok));
    SemanticModel okModel = ok.RootModel;
    FunctionDeclarationSyntax collectorFn = All<FunctionDeclarationSyntax>(ok)
        .Single(f => TextOf(ok, f.NameToken) == "make_collector");
    LambdaExpressionSyntax collectorLambda = Assert.Single(Descendants<LambdaExpressionSyntax>(collectorFn));
    LocalSymbol accSym = Local(ok, "acc");
    CaptureEntry accCap = Assert.Single(okModel.GetCaptures(collectorLambda)!.Entries);
    Assert.Same(accSym, accCap.Symbol);
    Assert.True(accCap.IsMutable);
    Assert.True(accCap.IsMutatedByClosure);
    Assert.True(okModel.AssignedBindings.IsAssignedAfterInit(accSym));

    // Read-only `self` capture inside `mut fn` is a value snapshot (`IsMutable == false`, `IsMutatedByClosure == false`).
    MethodDeclarationSyntax filterAbove = All<MethodDeclarationSyntax>(ok)
        .Single(m => TextOf(ok, m.NameToken) == "filter_above");
    foreach (LambdaExpressionSyntax selfReadLambda in Descendants<LambdaExpressionSyntax>(filterAbove)) {
      CaptureEntry selfCap = Assert.Single(okModel.GetCaptures(selfReadLambda)!.Entries);
      Assert.Equal("self", selfCap.Symbol.Name);
      Assert.False(selfCap.IsMutable);
      Assert.False(selfCap.IsMutatedByClosure);
    }

    // 2. Capturing `mut` parameter (read or write, lambda / nested fn / implicit `it`) or mutably capturing `mut fn` `self` emits PN3228.
    Compilation bad = Compile("""
      struct Counter {
        mut n int
        mut fn inc() { n += 1 }

        mut fn bad_self_write() {
          val f1 = fn () { self:n += 1 }
          val f2 = fn () { n += 1 }
          val f3 = fn () { inc() }
          val f4 = fn () { self:inc() }
        }
      }

      fn bad_mut_param_read(mut x int) fn () -> int {
        return fn () -> x
      }

      fn bad_mut_param_write(mut x int) fn () -> void {
        return fn () {
          x += 1
        }
      }

      fn bad_mut_param_nested(mut x int) {
        fn inner() {
          x += 1
        }
        inner()
      }

      fn bad_mut_param_implicit_it(mut limit int, xs []int) []int {
        return xs:filter(it > limit)
      }
      """);

    Diagnostic[] errors = CheckerErrors(bad);
    Assert.Equal(8, errors.Length);
    Assert.All(errors, static d => {
      Assert.Equal(DiagnosticCodes.CannotCaptureMutParameter, d.Code);
      Assert.Single(d.Related);
      Assert.Contains("copy the needed value into a local 'val'", d.Help);
    });
    Assert.Equal(4, errors.Count(static d => d.Message.Contains("mutably capture 'self'")));
    Assert.Equal(4, errors.Count(static d => d.Message.Contains("capture 'mut' parameter")));
  }

  [Fact]
  public void LoopVariablesUnionPayloadsTupleDestructuringRvaluesAndPushVoid_EnforceAD003() {
    Compilation bad = Compile("""
      struct Point {
        mut x int
        mut fn inc() { x += 1 }
      }

      union Box {
        Hold(Point)
        List([]int)
      }

      fn make_vec() []int { return [1, 2] }
      fn make_point() Point { return Point { x: 1 } }
      fn bump(mut p Point) { p:x += 1 }
      fn bump_int(mut n int) { n += 1 }
      fn make_pair() @(a int, b []int) { return @(a: 1, b: [1]) }

      val pts = [Point { x: 1 }]
      for p in pts {
        p = Point { x: 2 }
        p:x = 3
        p:inc()
      }

      for i, p in pts {
        i += 1
      }

      val bx = Box::Hold(Point { x: 1 })
      match bx {
        when Box::Hold(p) {
          p:x = 2
        }
        when Box::List(xs) {
          xs:push(1)
        }
      }
      if bx is Box::Hold(p) {
        p:inc()
      }

      val @(a: va, b: vb) = make_pair()
      va = 2
      vb:push(2)

      [1, 2]:push(3)
      Point { x: 1 }:inc()
      make_vec()[0] = 9
      bump(mut make_point())
      bump(mut Point { x: 1 })
      bump_int(mut 1)

      var nums = [1]
      nums:push(2):push(3)
      val r = nums:push(4)
      """);

    Diagnostic[] errors = CheckerErrors(bad);

    // Loop variables: `p = ...` (PN3201), `p:x = 3` (PN3220), `p:inc()` (PN3220), `i += 1` (PN3201).
    Assert.Contains(errors, static d => d.Code == DiagnosticCodes.CannotAssignToImmutable && d.Message.Contains("'p'"));
    Assert.Contains(errors, static d => d.Code == DiagnosticCodes.CannotAssignToImmutable && d.Message.Contains("'i'"));
    Assert.Equal(2, errors.Count(static d => d.Code == DiagnosticCodes.CannotMutateImmutableValue && d.Message.Contains("immutable loop variable 'p'")));

    // Union pattern bindings (when + if is): 3 PN3220 errors.
    Assert.Equal(3, errors.Count(static d => d.Code == DiagnosticCodes.CannotMutateImmutableValue && d.Message.Contains("immutable pattern binding")));

    // Tuple destructuring `val @(va, vb)`: PN3201 + PN3220.
    Assert.Contains(errors, static d => d.Code == DiagnosticCodes.CannotAssignToImmutable && d.Message.Contains("'va'"));
    Assert.Contains(errors, static d => d.Code == DiagnosticCodes.CannotMutateImmutableValue && d.Message.Contains("'vb'"));

    // Temporary rvalue mutations: 6 PN3225 errors.
    Assert.Equal(6, errors.Count(static d => d.Code == DiagnosticCodes.InvalidMutArgument));

    // Chained `nums:push(2):push(3)` fails with PN3208 on `void`, and `val r = nums:push(4)` fails with PN3101.
    Assert.Contains(errors, static d => d.Code == DiagnosticCodes.UnknownMember && d.Message == "Type 'void' does not have method 'push'.");
    Assert.Contains(errors, static d => d.Code == DiagnosticCodes.InvalidVoidTypePosition && d.Message.Contains("Cannot initialize 'r' with a 'void' value"));

    Assert.Equal(17, errors.Length);

    // `var @(a, b) = make_pair()` binds mutable variables.
    Compilation goodDestructure = Compile("""
      fn make_pair() @(a int, b []int) { return @(a: 1, b: [1]) }
      var @(a, b) = make_pair()
      a += 1
      b:push(2)
      """);
    Assert.Empty(CheckerErrors(goodDestructure));
  }

  [Fact]
  public void MutationPlacesSideTable_RecordsAllFiveMutationRootKindsAndInOutFacts() {
    Compilation compilation = Compile("""
      struct Counter {
        mut count int
        mut fn bump_self() {
          count += 1
          self:count = 2
          inc_self()
        }
        mut fn inc_self() {
          count += 1
        }
      }

      var top_var = [1]

      fn helper(mut p Counter) {
        p:count = 10
        p:inc_self()
      }

      fn driver() {
        var local_v = [1, 2]
        local_v[0] = 9
        local_v:push(3)

        var local_c = Counter { count: 0 }
        helper(mut local_c)

        top_var:push(2)

        var captured = [10]
        val f = fn () {
          captured:push(20)
        }
        f()

        var scalar = 1
        scalar = 2
        scalar += 3
      }
      """);

    Assert.Empty(CheckerErrors(compilation));
    SemanticModel model = compilation.RootModel;

    var places = new List<MutationPlaceInfo>();
    foreach (SyntaxNode node in All<SyntaxNode>(compilation)) {
      if (model.GetMutationPlace(node) is { } info) {
        places.Add(info);
      }
    }

    // All 5 MutationRootKind values are populated!
    Assert.Contains(places, static p => p.RootKind == MutationRootKind.LocalVar && p.Kind == MutationKind.IndexWrite && p.RequiresCoWCheck && !p.PassByInOut);
    Assert.Contains(places, static p => p.RootKind == MutationRootKind.LocalVar && p.Kind == MutationKind.MutatingCall && p.RequiresCoWCheck && p.PassByInOut);
    Assert.Contains(places, static p => p.RootKind == MutationRootKind.LocalVar && p.Kind == MutationKind.MutArgument && p.RequiresCoWCheck && p.PassByInOut);
    Assert.Contains(places, static p => p.RootKind == MutationRootKind.LocalVar && p.Kind == MutationKind.DirectAssign && !p.RequiresCoWCheck && !p.PassByInOut);
    Assert.Contains(places, static p => p.RootKind == MutationRootKind.LocalVar && p.Kind == MutationKind.CompoundAssign && !p.RequiresCoWCheck && !p.PassByInOut);
    Assert.Contains(places, static p => p.RootKind == MutationRootKind.MutParameter && p.Kind == MutationKind.FieldWrite && p.RequiresCoWCheck && !p.PassByInOut);
    Assert.Contains(places, static p => p.RootKind == MutationRootKind.MutParameter && p.Kind == MutationKind.MutatingCall && p.RequiresCoWCheck && p.PassByInOut);
    Assert.Contains(places, static p => p.RootKind == MutationRootKind.MutSelf && p.Kind == MutationKind.FieldWrite && p.RequiresCoWCheck && !p.PassByInOut);
    Assert.Contains(places, static p => p.RootKind == MutationRootKind.MutSelf && p.Kind == MutationKind.MutatingCall && p.RequiresCoWCheck && p.PassByInOut);
    Assert.Contains(places, static p => p.RootKind == MutationRootKind.TopLevelVar && p.Kind == MutationKind.MutatingCall && p.RequiresCoWCheck && p.PassByInOut);
    Assert.Contains(places, static p => p.RootKind == MutationRootKind.CapturedVar && p.Kind == MutationKind.MutatingCall && p.RequiresCoWCheck && p.PassByInOut);

    // Every single MutationKind (6) and MutationRootKind (5) enum value was observed.
    Assert.Equal(6, places.Select(static p => p.Kind).Distinct().Count());
    Assert.Equal(5, places.Select(static p => p.RootKind).Distinct().Count());
  }

  [Fact]
  public void ErrorTolerance_A4_HalfTypedAndErrorExpressionsProduceZeroMutabilityCascades() {
    Compilation errorTypes = Compile("""
      struct Point { x int }
      fn take_mut(mut n int) {}

      val broken_val = missing_fn()
      broken_val:y = 1
      broken_val:push(1)
      broken_val[0] = 2

      take_mut(mut unknown_var)

      fn never_paths() {
        val nv = panic("boom")
        nv:y = 1
        panic("boom"):push(1)
        take_mut(mut panic("boom"))
      }

      val p = Point { x: 1 }
      p:nonexistent = 2
      """);

    Diagnostic[] errors = CheckerErrors(errorTypes);
    Assert.DoesNotContain(errors, static d =>
        d.Code is DiagnosticCodes.CannotMutateImmutableValue
            or DiagnosticCodes.CannotMutateReadOnlyField
            or DiagnosticCodes.CannotMutateSelfInNonMutMethod
            or DiagnosticCodes.MissingMutAtCallSite
            or DiagnosticCodes.UnexpectedMutAtCallSite
            or DiagnosticCodes.InvalidMutArgument
            or DiagnosticCodes.OverlappingMutArguments
            or DiagnosticCodes.CannotBindMutatingMethod
            or DiagnosticCodes.CannotCaptureMutParameter);

    // Half-typed syntax with `mut` produces zero checker cascades.
    string[] halfTyped = [
      "fn take(mut x int) {}\ntake(mut ",
      "fn take(mut x int) {}\ntake(mut )",
      "struct S { mut x int }\nvar s = S { x: 1 }\ns:",
    ];
    foreach (string snippet in halfTyped) {
      Compilation comp = CompileTolerant(snippet);
      Assert.Empty(CheckerErrors(comp));
    }
  }

  [Fact]
  public void DisjointMutSlots_D184_ProvenDisjointProvenOverlapAndRuntimeChecks() {
    // 1. Proven disjoint: 0 errors, 0 run-time checks.
    Compilation disjoint = Compile("""
      struct Point {
        mut x int
        mut y int
      }

      struct Enemy {
        mut hp int
        mut mp int

        mut fn hit(mut other Enemy) {
          other:hp -= 1
        }
      }

      struct Holder {
        mut x int
        mut y int
        mut items []int

        mut fn swap_fields() {
          f(mut x, mut y)
          f(mut x, mut self:y)
          f(mut items[0], mut self:items[1])
        }
      }

      fn f(mut a int, mut b int) {}
      fn f3(mut a int, mut b int, mut c int) {}

      fn check_disjoint(i int, j int) {
        var p = Point { x: 1, y: 2 }
        f(mut p:x, mut p:y)

        var v = [1, 2, 3, 4]
        f(mut v[0], mut v[1])
        f(mut v[0], mut v[-1])
        f(mut v[(0)], mut v[(1)])
        f(mut v[i], mut v[i + 1])
        f(mut v[i - 1], mut v[i + 1])
        f3(mut v[0], mut v[1], mut v[2])

        var pts = [Point { x: 1, y: 2 }]
        f(mut pts[0]:x, mut pts[0]:y)

        var enemies = [Enemy { hp: 10, mp: 5 }, Enemy { hp: 10, mp: 5 }]
        f(mut enemies[i]:hp, mut enemies[j]:mp)
        enemies[0]:hit(mut enemies[1])

        var m = map[string, int] { "a" -> 1  "b" -> 2 }
        f(mut m["a"], mut m["b"])

        if i != j {
          f(mut v[i], mut v[j])
          enemies[i]:hit(mut enemies[j])
        }

        for k in 3 {
          f(mut v[k], mut v[k + 1])
        }

        for 3 {
          f(mut v[it], mut v[it + 1])
        }
      }
      """);

    Assert.Empty(CheckerErrors(disjoint));
    SemanticModel disjointModel = disjoint.RootModel;
    foreach (CallExpressionSyntax call in All<CallExpressionSyntax>(disjoint)) {
      Assert.Empty(disjointModel.GetRuntimeDisjointnessChecks(call));
    }

    // 2. Proven overlap: every call emits PN3226.
    Compilation overlap = Compile("""
      struct Point {
        mut x int
        mut y int

        mut fn merge(mut other Point) {
          x += other:x
        }

        mut fn bump_int(mut n int) {
          x += n
        }

        mut fn bad_self() {
          f(mut x, mut self:x)
          merge(mut self)
          bump_int(mut x)
        }
      }

      struct Enemy {
        mut hp int
        mut fn hit(mut other Enemy) { other:hp -= 1 }
      }

      fn f(mut a int, mut b int) {}
      fn f3(mut a int, mut b int, mut c int) {}
      fn take_pt(mut p Point, mut x int) {}
      fn take_vec(mut v []int, mut x int) {}

      fn check_overlap(i int, k string) {
        var a = 1
        f(mut a, mut a)

        var p = Point { x: 1, y: 2 }
        take_pt(mut p, mut p:x)
        p:merge(mut p)

        var v = [1, 2, 3]
        take_vec(mut v, mut v[i])
        f(mut v[0], mut v[0])
        f(mut v[-1], mut v[-1])
        f(mut v[i], mut v[i])
        f(mut v[i + 1], mut v[i + 1])
        f(mut v[i], mut v[i + 0])
        f3(mut v[0], mut v[1], mut v[0])

        var es = [Enemy { hp: 10 }]
        es[i]:hit(mut es[i])

        var m = map[string, int] { "a" -> 1 }
        f(mut m["a"], mut m["a"])
        f(mut m[k], mut m[k])
      }
      """);

    Diagnostic[] overlapErrors = CheckerErrors(overlap);
    Assert.Equal(16, overlapErrors.Length);
    Assert.All(overlapErrors, static d => Assert.Equal(DiagnosticCodes.OverlappingMutArguments, d.Code));

    // 3. Needs run-time check: 0 errors, exact checks recorded in SemanticModel.
    Compilation runtimeChecked = Compile("""
      struct Enemy {
        mut hp int
        mut fn hit(mut other Enemy) { other:hp -= 1 }
      }

      struct IndexBox {
        idx int
        mut items []int

        mut fn check_field_idx() {
          f(mut items[idx], mut items[idx])
        }
      }

      fn idx_fn() int { return 0 }
      fn f(mut a int, mut b int) {}
      fn f3(mut a int, mut b int, mut c int) {}
      fn take_enemy_and_hp(mut e Enemy, mut hp int) {}

      fn check_runtime(i int, j int, k int, l int, mut mut_idx int) {
        var v = [1, 2, 3]
        f(mut v[i], mut v[j])
        f(mut v[idx_fn()], mut v[0])
        f3(mut v[i], mut v[j], mut v[k])

        var es = [Enemy { hp: 5 }, Enemy { hp: 6 }]
        es[i]:hit(mut es[j])
        take_enemy_and_hp(mut es[i], mut es[j]:hp)

        var grid = [[1, 2], [3, 4]]
        f(mut grid[i][j], mut grid[k][l])

        # Constants are NOT folded (FN-33).
        val c0 = 0
        val c1 = 1
        f(mut v[c0], mut v[c1])
        f(mut v[c0], mut v[0])

        # Mutable names (`var`, `mut` param) never count as immutable `n`.
        var vi = 0
        f(mut v[vi], mut v[vi])
        f(mut v[vi], mut v[vi + 1])
        f(mut v[mut_idx], mut v[mut_idx])

        # Non-int/string literals (`bool`) are unknown.
        var bm = map[bool, int] { true -> 1  false -> 2 }
        f(mut bm[true], mut bm[true])
      }
      """);

    Assert.Empty(CheckerErrors(runtimeChecked));
    SemanticModel rtModel = runtimeChecked.RootModel;
    CallExpressionSyntax[] rtCalls = All<CallExpressionSyntax>(runtimeChecked)
        .Where(c => !rtModel.GetRuntimeDisjointnessChecks(c).IsEmpty)
        .ToArray();

    // Expect 13 calls with runtime checks:
    // 1. items[idx] vs items[idx] (field as index)
    // 2. f(mut v[i], mut v[j])
    // 3. f(mut v[idx_fn()], mut v[0])
    // 4. f3(mut v[i], mut v[j], mut v[k]) -> 3 pairs
    // 5. es[i]:hit(mut es[j]) -> Receiver vs Arg 0
    // 6. take_enemy_and_hp(mut es[i], mut es[j]:hp) -> 1 step pair ([i] vs [j])
    // 7. f(mut grid[i][j], mut grid[k][l]) -> 2 step pairs
    // 8. f(mut v[c0], mut v[c1])
    // 9. f(mut v[c0], mut v[0])
    // 10. f(mut v[vi], mut v[vi])
    // 11. f(mut v[vi], mut v[vi + 1])
    // 12. f(mut v[mut_idx], mut v[mut_idx])
    // 13. f(mut bm[true], mut bm[true])
    Assert.Equal(13, rtCalls.Length);

    CallExpressionSyntax CallMatching(string snippet) =>
        rtCalls.Single(c => runtimeChecked.RootTree.Source.AsSpan(c.Span).ToString() == snippet);

    // f(mut v[i], mut v[j]): 1 check, 1 unknown step pair.
    RuntimeDisjointnessCheck vjCheck = Assert.Single(rtModel.GetRuntimeDisjointnessChecks(CallMatching("f(mut v[i], mut v[j])")));
    Assert.Equal("v", vjCheck.RootSymbol.Name);
    Assert.False(vjCheck.FirstAccess.IsReceiver);
    Assert.Equal(0, vjCheck.FirstAccess.ArgumentIndex);
    Assert.Equal(1, vjCheck.SecondAccess.ArgumentIndex);
    RuntimeDisjointnessStepPair vjPair = Assert.Single(vjCheck.UnknownSteps);
    Assert.Equal("v[i]", runtimeChecked.RootTree.Source.AsSpan(vjPair.First.Span).ToString());
    Assert.Equal("v[j]", runtimeChecked.RootTree.Source.AsSpan(vjPair.Second.Span).ToString());

    // f3(mut v[i], mut v[j], mut v[k]): 3 pairwise checks.
    var f3Checks = rtModel.GetRuntimeDisjointnessChecks(CallMatching("f3(mut v[i], mut v[j], mut v[k])"));
    Assert.Equal(3, f3Checks.Length);
    Assert.Contains(f3Checks, static c => c.FirstAccess.ArgumentIndex == 0 && c.SecondAccess.ArgumentIndex == 1);
    Assert.Contains(f3Checks, static c => c.FirstAccess.ArgumentIndex == 0 && c.SecondAccess.ArgumentIndex == 2);
    Assert.Contains(f3Checks, static c => c.FirstAccess.ArgumentIndex == 1 && c.SecondAccess.ArgumentIndex == 2);

    // es[i]:hit(mut es[j]): Receiver vs Arg 0.
    RuntimeDisjointnessCheck hitCheck = Assert.Single(rtModel.GetRuntimeDisjointnessChecks(CallMatching("es[i]:hit(mut es[j])")));
    Assert.True(hitCheck.FirstAccess.IsReceiver);
    Assert.Equal(0, hitCheck.SecondAccess.ArgumentIndex);
    RuntimeDisjointnessStepPair hitPair = Assert.Single(hitCheck.UnknownSteps);
    Assert.Equal("es[i]", runtimeChecked.RootTree.Source.AsSpan(hitPair.First.Span).ToString());
    Assert.Equal("es[j]", runtimeChecked.RootTree.Source.AsSpan(hitPair.Second.Span).ToString());

    // take_enemy_and_hp(mut es[i], mut es[j]:hp): compares es[i] vs es[j] at prefix depth 0.
    RuntimeDisjointnessCheck enemyHpCheck = Assert.Single(rtModel.GetRuntimeDisjointnessChecks(CallMatching("take_enemy_and_hp(mut es[i], mut es[j]:hp)")));
    RuntimeDisjointnessStepPair enemyHpPair = Assert.Single(enemyHpCheck.UnknownSteps);
    Assert.Equal("es[i]", runtimeChecked.RootTree.Source.AsSpan(enemyHpPair.First.Span).ToString());
    Assert.Equal("es[j]", runtimeChecked.RootTree.Source.AsSpan(enemyHpPair.Second.Span).ToString());

    // f(mut grid[i][j], mut grid[k][l]): 1 check with 2 unknown step pairs.
    RuntimeDisjointnessCheck gridCheck = Assert.Single(rtModel.GetRuntimeDisjointnessChecks(CallMatching("f(mut grid[i][j], mut grid[k][l])")));
    Assert.Equal(2, gridCheck.UnknownSteps.Length);
    Assert.Equal("grid[i]", runtimeChecked.RootTree.Source.AsSpan(gridCheck.UnknownSteps[0].First.Span).ToString());
    Assert.Equal("grid[k]", runtimeChecked.RootTree.Source.AsSpan(gridCheck.UnknownSteps[0].Second.Span).ToString());
    Assert.Equal("grid[i][j]", runtimeChecked.RootTree.Source.AsSpan(gridCheck.UnknownSteps[1].First.Span).ToString());
    Assert.Equal("grid[k][l]", runtimeChecked.RootTree.Source.AsSpan(gridCheck.UnknownSteps[1].Second.Span).ToString());
  }

  [Fact]
  public void DisjointMutSlots_D184_BranchFactScopingAndShadowing() {
    Compilation comp = Compile("""
      fn f(mut a int, mut b int) int { return a + b }

      fn branch_tests(i int, j int, flag bool) {
        var v = [1, 2, 3]

        # Proven disjoint: nested inside then-branch (nested if, loop, block), reversed, parenthesized.
        if i != j {
          f(mut v[i], mut v[j])
          if flag {
            for 2 {
              {
                f(mut v[i], mut v[j])
              }
            }
          }
        } else {
          # Else branch: NOT proven disjoint -> 1 runtime check.
          f(mut v[i], mut v[j])
        }

        # After if: NOT proven disjoint -> 1 runtime check.
        f(mut v[i], mut v[j])

        # Reversed and parenthesized condition: proven disjoint.
        if (j != i) {
          f(mut v[i], mut v[j])
        }

        # If-expression: then-branch proven disjoint, else-branch needs runtime check.
        val res = if i != j then f(mut v[i], mut v[j]) else f(mut v[i], mut v[j])
        val res_typed int = if i != j then f(mut v[i], mut v[j]) else f(mut v[i], mut v[j])

        # Joined with `and` or `or`: does NOT count -> 1 runtime check each.
        if i != j and flag {
          f(mut v[i], mut v[j])
        }
        if i != j or flag {
          f(mut v[i], mut v[j])
        }

        # `var` bindings do NOT count -> 1 runtime check.
        var a = 0
        var b = 1
        if a != b {
          f(mut v[a], mut v[b])
        }

        # Lambda body inside then-branch does NOT inherit branch fact -> 1 runtime check.
        if i != j {
          val cb = fn () {
            f(mut v[i], mut v[j])
          }
          cb()
        }

        # Shadowing inner `i` creates a new symbol -> 1 runtime check.
        if i != j {
          val i = j
          f(mut v[i], mut v[j])
        }
      }
      """);

    Assert.Empty(CheckerErrors(comp));
    SemanticModel model = comp.RootModel;
    CallExpressionSyntax[] fCalls = All<CallExpressionSyntax>(comp)
        .Where(c => c.Callee is IdentifierExpressionSyntax id && TextOf(comp, id.IdentifierToken) == "f")
        .ToArray();

    // Order of `f(...)` calls in source:
    // 0: then-branch direct             -> 0 checks
    // 1: then-branch nested if/for/{}   -> 0 checks
    // 2: else-branch                    -> 1 check
    // 3: after if                       -> 1 check
    // 4: (j != i) then-branch           -> 0 checks
    // 5: if-expr (synth) then-branch    -> 0 checks
    // 6: if-expr (synth) else-branch    -> 1 check
    // 7: if-expr (check) then-branch    -> 0 checks
    // 8: if-expr (check) else-branch    -> 1 check
    // 9: `and` condition                -> 1 check
    // 10: `or` condition                -> 1 check
    // 11: `var a != b`                  -> 1 check
    // 12: lambda body inside then       -> 1 check
    // 13: shadowed `val i = j`          -> 1 check
    Assert.Equal(14, fCalls.Length);
    Assert.Empty(model.GetRuntimeDisjointnessChecks(fCalls[0]));
    Assert.Empty(model.GetRuntimeDisjointnessChecks(fCalls[1]));
    Assert.Single(model.GetRuntimeDisjointnessChecks(fCalls[2]));
    Assert.Single(model.GetRuntimeDisjointnessChecks(fCalls[3]));
    Assert.Empty(model.GetRuntimeDisjointnessChecks(fCalls[4]));
    Assert.Empty(model.GetRuntimeDisjointnessChecks(fCalls[5]));
    Assert.Single(model.GetRuntimeDisjointnessChecks(fCalls[6]));
    Assert.Empty(model.GetRuntimeDisjointnessChecks(fCalls[7]));
    Assert.Single(model.GetRuntimeDisjointnessChecks(fCalls[8]));
    Assert.Single(model.GetRuntimeDisjointnessChecks(fCalls[9]));
    Assert.Single(model.GetRuntimeDisjointnessChecks(fCalls[10]));
    Assert.Single(model.GetRuntimeDisjointnessChecks(fCalls[11]));
    Assert.Single(model.GetRuntimeDisjointnessChecks(fCalls[12]));
    Assert.Single(model.GetRuntimeDisjointnessChecks(fCalls[13]));
  }

  [Fact]
  public void VectorSwap_VECLIB26ToE2_ChecksMutabilityVoidReturnAndEffects() {
    Compilation ok = Compile("""
      val mut mod_vec = [1, 2, 3]

      fn swap_module_vec() {
        mod_vec:swap(0, 2)
      }

      fn swap_local() {
        var v = [10, 20, 30]
        v:swap(0, 2)
        v:swap(1, 1)

        val mut vm = [4, 5]
        vm:swap(0, 1)
      }
      """);

    Assert.Empty(CheckerErrors(ok));
    SemanticModel okModel = ok.RootModel;
    CallExpressionSyntax[] swapCalls = All<CallExpressionSyntax>(ok)
        .Where(c => okModel.GetCallResolution(c) is { Kind: CallKind.Intrinsic, Intrinsic: IntrinsicId.VecSwap })
        .ToArray();
    Assert.Equal(4, swapCalls.Length);
    foreach (CallExpressionSyntax swapCall in swapCalls) {
      Assert.True(okModel.GetType(swapCall)!.IsVoid);
      MutationPlaceInfo? place = okModel.GetMutationPlace(swapCall);
      Assert.NotNull(place);
      Assert.Equal(MutationKind.MutatingCall, place.Kind);
      Assert.True(place.PassByInOut);
    }

    FunctionSymbol swapModFn = All<FunctionDeclarationSyntax>(ok)
        .Select(f => (FunctionSymbol) okModel.GetDeclaredSymbol(f)!)
        .Single(f => f.Name == "swap_module_vec");
    ModuleStateEffects effects = okModel.GetModuleStateEffects(swapModFn);
    Assert.True(effects.ReadsModuleState);
    Assert.True(effects.WritesModuleState);

    Compilation bad = Compile("""
      val imm = [1, 2]
      imm:swap(0, 1)

      [1, 2]:swap(0, 1)

      var v = [1, 2]
      val r = v:swap(0, 1)
      v:swap("a", 0)
      v:swap(0)
      """);

    Diagnostic[] badErrors = CheckerErrors(bad);
    Assert.Contains(badErrors, static d => d.Code == DiagnosticCodes.CannotMutateImmutableValue && d.Message.Contains("'imm'"));
    Assert.Contains(badErrors, static d => d.Code == DiagnosticCodes.InvalidMutArgument);
    Assert.Contains(badErrors, static d => d.Code == DiagnosticCodes.InvalidVoidTypePosition && d.Message.Contains("'r'"));
    Assert.Contains(badErrors, static d => d.Code == DiagnosticCodes.TypeMismatch);
    Assert.Contains(badErrors, static d => d.Code == DiagnosticCodes.WrongArgumentCount);
    Assert.Equal(5, badErrors.Length);
  }
}
