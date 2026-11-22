using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.Parsing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.IR.Lowering;

/// <summary>
/// Unit tests covering core AST-to-PIR lowering requirements from ticket <c>060</c>
/// (<c>spec.md</c> and <c>056-re-ir-design/design.md</c> §5.1–§5.10).
/// </summary>
public sealed class IrLoweringCoreTests {
  // ---------------------------------------------------------------------------
  // Division / Modulo panic guards (TYPE-E4, TYPE-E6, D-186) & compound assign (VAR-4)
  // ---------------------------------------------------------------------------

  [Fact]
  public void FloorDivAndMod_EmitZeroAndIntMinOverflowPanicGuards() {
    const string Source = """
      fn div_ops(a int, b int) int {
        val q = a // b
        val r = a % b
        return q + r
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction fn = GetFunction(module, "div_ops");
    string[] panics = AllPanicMessages(module, fn);

    Assert.Contains(IrLowerer.PanicDivByZero, panics);
    Assert.Contains(IrLowerer.PanicIntMinDivOverflow, panics);
    Assert.Equal("Cannot divide by zero.", IrLowerer.PanicDivByZero);
    Assert.Equal(
        "Integer division overflowed: the smallest 64-bit integer cannot be divided by -1.",
        IrLowerer.PanicIntMinDivOverflow);

    string printed = IrPrinter.PrintFunction(module, fn);
    Assert.Contains("floordiv.i64", printed);
    Assert.Contains("floormod.i64", printed);
    Assert.Contains("-9223372036854775808", printed);
  }

  [Fact]
  public void FloatDivision_UsesDivF64WithZeroGuard() {
    const string Source = """
      fn ratio(a int, b int) float {
        return a / b
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction fn = GetFunction(module, "ratio");
    string printed = IrPrinter.PrintFunction(module, fn);

    Assert.Contains("cast.i64_to_f64", printed);
    Assert.Contains("div.f64", printed);
    Assert.Contains(IrLowerer.PanicDivByZero, AllPanicMessages(module, fn));
  }

  [Fact]
  public void CompoundAssignment_EvaluatesPathIndexExactlyOnce() {
    const string Source = """
      var calls = 0
      fn next_idx() int {
        calls += 1
        return 0
      }

      fn bump(mut xs []int) {
        xs[next_idx()] += 10
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction bump = GetFunction(module, "bump");

    int callCount = bump.Blocks
        .SelectMany(static b => b.Instructions)
        .OfType<CallDirect>()
        .Count(c => module.Functions[c.Callee.Index].Name == "next_idx");
    Assert.Equal(1, callCount);

    string printed = IrPrinter.PrintFunction(module, bump);
    Assert.Contains("ensure_unique", printed);
    Assert.Contains("vec.elem_addr", printed);
    Assert.Contains("load_ptr", printed);
    Assert.Contains("store_ptr", printed);
  }

  // ---------------------------------------------------------------------------
  // Strings, runes, interpolation, short-circuit, structural equality
  // ---------------------------------------------------------------------------

  [Fact]
  public void StringInterpolationAndRuneConcat_LowerCleanly() {
    const string Source = """
      fn greet(name string, n int, ch rune) string {
        val a = "hi $name #$n"
        val b = a + ch
        val c = ch + b
        return c
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction fn = GetFunction(module, "greet");
    string printed = IrPrinter.PrintFunction(module, fn);

    Assert.Contains("str.concat_n", printed);
    Assert.Contains("pino_to_str", printed);
    Assert.Contains("str.concat_rune", printed);
    Assert.Contains("rune.concat_str", printed);
  }

  [Fact]
  public void StructuralEquality_SynthesizesEqHelpers() {
    const string Source = """
      struct Point {
        x int
        y int
      }

      fn same(a Point, b Point, xs []Point, ys []Point) bool {
        return a == b and xs != ys
      }
      """;

    IrModule module = LowerAndVerify(Source);
    Assert.Contains(module.Functions, static f => f.Name.StartsWith("__pino_eq_", StringComparison.Ordinal));
  }

  // ---------------------------------------------------------------------------
  // All 9 LoopKinds (SideTables.cs:43-53)
  // ---------------------------------------------------------------------------

  [Fact]
  public void AllNineLoopKinds_LowerAndVerifyCleanly() {
    const string Source = """
      fn all_loops(
        xs []int,
        s string,
        m map[string, int],
        st set[int],
        p Pool[int]
      ) int {
        var acc = 0

        # 1. Infinite
        for {
          acc += 1
          break
        }

        # 2. ConditionLoop
        for acc < 2 {
          acc += 1
        }

        # 3. CountLoop
        for 3 {
          acc += it
        }

        # 4. ForInRange (1-var and 2-var)
        for v in 4 {
          acc += v
        }
        for i, v in 3 {
          acc += i + v
        }

        # 5. ForInVector (1-var and 2-var)
        for x in xs {
          acc += x
        }
        for i, x in xs {
          acc += i + x
        }

        # 6. ForInString (UTF-8 runes with continue advancing bb_latch)
        for i, ch in s {
          if ch == 'a' {
            continue
          }
          acc += i + (ch - 'a')
        }

        # 7. ForInMap (1-var and 2-var)
        for v in m {
          acc += v
        }
        for k, v in m {
          acc += k:len + v
        }

        # 8. ForInSet
        for elem in st {
          acc += elem
        }

        # 9. ForInPool (1-var and 2-var)
        for val_item in p {
          acc += val_item
        }
        for h, val_item in p {
          if h in p {
            acc += val_item
          }
        }

        return acc
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction fn = GetFunction(module, "all_loops");
    IrInstruction[] allInstrs = [.. fn.Blocks.SelectMany(static b => b.Instructions)];

    Assert.Contains(allInstrs, static i => i is VecGet { Unchecked: true });
    Assert.Contains(allInstrs, static i => i is StrByteLen);
    Assert.Contains(allInstrs, static i => i is StrDecodeRune);
    Assert.Contains(allInstrs, static i => i is StrNextBytePos);
    Assert.Contains(allInstrs, static i => i is MapSlotLen);
    Assert.Contains(allInstrs, static i => i is MapSlotIsLive);
    Assert.Contains(allInstrs, static i => i is SetSlotLen);
    Assert.Contains(allInstrs, static i => i is SetSlotIsLive);
    Assert.Contains(allInstrs, static i => i is PoolSlotLen);
    Assert.Contains(allInstrs, static i => i is PoolSlotIsLive);
    Assert.Contains(allInstrs, static i => i is PoolSlotHandle);
  }

  // ---------------------------------------------------------------------------
  // match (positional + @label bindings), is + and, ? / or, Result[void, E]
  // ---------------------------------------------------------------------------

  [Fact]
  public void MatchPositionalAndLabeledBindings_AndIsAndChains_LowerCleanly() {
    const string Source = """
      union Event {
        Click(x int, y int)
        Key(code int)
        Quit
      }

      fn handle(e Event) int {
        if e is Event::Click(cx, cy) and cx > 0 and cy > 0 {
          return cx + cy
        }

        return match e {
          when Event::Click(@y: yv, @x: xv) -> xv - yv
          when Event::Key(c) -> c
          when Event::Quit -> 0
        }
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction fn = GetFunction(module, "handle");
    string printed = IrPrinter.PrintFunction(module, fn);

    Assert.Contains("union.tag", printed);
    Assert.Contains("union.field", printed);
    Assert.Contains("switch", printed);
  }

  [Fact]
  public void BubbleAndOrRecovery_AndResultVoid_LowerCleanly() {
    const string Source = """
      fn check_positive(n int) Result[void, string] {
        if n <= 0 {
          return Result::Failure("non-positive")
        }
        return Result::Success
      }

      fn caller(n int, opt Option[int]) Result[int, string] {
        check_positive(n)?
        val a = opt or {
          yield err:len
        }
        check_positive(a) or {
          return Result::Failure(err)
        }
        return Result::Success(a)
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction checkPos = GetFunction(module, "check_positive");
    IrFunction caller = GetFunction(module, "caller");

    string checkPrinted = IrPrinter.PrintFunction(module, checkPos);
    Assert.Contains("union.new", checkPrinted);

    string callerPrinted = IrPrinter.PrintFunction(module, caller);
    Assert.Contains("\"None\"", callerPrinted);
  }

  // ---------------------------------------------------------------------------
  // Tuples, destructuring, embedded struct promotion
  // ---------------------------------------------------------------------------

  [Fact]
  public void TuplesAndDestructuring_UseCanonicalFieldOrder() {
    const string Source = """
      fn swap_pair(a int, b string) @(second string, first int) {
        return @(first: a, second: b)
      }

      fn use_pair() string {
        val @(second, first) = swap_pair(7, "ok")
        return "$second:$first"
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction swap = GetFunction(module, "swap_pair");
    string printed = IrPrinter.PrintFunction(module, swap);
    Assert.Contains("tuple.new", printed);
  }

  [Fact]
  public void EmbeddedStructPromotion_LowersFieldsAndMutMethods() {
    const string Source = """
      struct Pos {
        mut x int
        mut y int

        fn sum() int {
          return self:x + self:y
        }

        mut fn shift(dx int, dy int) {
          self:x += dx
          self:y += dy
        }
      }

      struct Entity {
        Pos
        name string
      }

      fn move_entity(mut e Entity) int {
        e:shift(2, 3)
        e:x += 1
        return e:sum() + e:y
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction fn = GetFunction(module, "move_entity");
    string printed = IrPrinter.PrintFunction(module, fn);

    Assert.Contains("field_addr %e, Entity.Pos", printed);
    Assert.Contains("call Pos::shift", printed);
    Assert.Contains("call Pos::sum", printed);
  }

  // ---------------------------------------------------------------------------
  // []T { len, init, cap } clamping + [WARNING] to stderr
  // ---------------------------------------------------------------------------

  [Fact]
  public void VectorSizedInit_LowersWithClampingWarningAndVecWithCap() {
    const string Source = """
      fn make_buf(n int, c int) []int {
        return []int { len: n, init: 42, cap: c }
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction fn = GetFunction(module, "make_buf");
    string printed = IrPrinter.PrintFunction(module, fn);

    Assert.Contains("vec.with_cap", printed);
    Assert.Contains("pino_eprintln", printed);
    Assert.Contains("[WARNING]", printed);
    Assert.Contains("vec.push", printed);
  }

  // ---------------------------------------------------------------------------
  // D-184 runtime exclusivity checks (single-step and multi-step) & FN-31 order
  // ---------------------------------------------------------------------------

  [Fact]
  public void D184_SingleStepEnemiesAliasing_EmitsPtrEqWithExactFn34Panic() {
    const string Source = """
      struct Enemy {
        mut hp int
      }

      fn duel(mut a Enemy, mut hp int) {
        a:hp -= 1
        hp -= 1
      }

      fn run(mut enemies []Enemy, i int, j int) {
        duel(mut enemies[i], mut enemies[j]:hp)
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction run = GetFunction(module, "run");
    string printed = IrPrinter.PrintFunction(module, run);

    Assert.Contains("ptr.eq", printed);
    Assert.Contains(
        "Cannot pass the same element of 'enemies' as two 'mut' arguments to 'duel'.",
        AllPanicMessages(module, run));
  }

  [Fact]
  public void D184_MultiStepGridAliasing_ComparesEveryUnknownStepWithPtrEq() {
    const string Source = """
      fn swap_cells(mut a int, mut b int) {
        val tmp = a
        a = b
        b = tmp
      }

      fn run(mut grid [][]int, i int, j int, k int, l int) {
        swap_cells(mut grid[i][j], mut grid[k][l])
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction run = GetFunction(module, "run");

    int ptrEqCount = run.Blocks
        .SelectMany(static b => b.Instructions)
        .OfType<PtrEq>()
        .Count();
    Assert.Equal(2, ptrEqCount);
    Assert.Contains(
        "Cannot pass the same element of 'grid' as two 'mut' arguments to 'swap_cells'.",
        AllPanicMessages(module, run));
  }

  // ---------------------------------------------------------------------------
  // D-185 Pool[T] stale-handle panic message
  // ---------------------------------------------------------------------------

  [Fact]
  public void D185_PoolIndexing_CarriesFirstIdentifierForExactPoolE1Panic() {
    const string Source = """
      struct World {
        mut entities Pool[int]
      }

      fn bump_entity(mut world World, h Handle[int]) int {
        val v = world:entities[h]
        world:entities[h] = v + 1
        return world:entities[h]
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction fn = GetFunction(module, "bump_entity");

    PoolGetOrPanic[] gets = fn.Blocks.SelectMany(static b => b.Instructions).OfType<PoolGetOrPanic>().ToArray();
    PoolElemAddr[] addrs = fn.Blocks.SelectMany(static b => b.Instructions).OfType<PoolElemAddr>().ToArray();

    Assert.NotEmpty(gets);
    Assert.NotEmpty(addrs);
    Assert.All(gets, static g => Assert.Equal("world", g.PoolName));
    Assert.All(addrs, static a => Assert.Equal("world", a.PoolName));
    Assert.Equal(
        "This handle no longer points to a value in 'world'; it was removed.",
        IrLowerer.FormatPoolStaleHandlePanic("world"));
  }

  // ---------------------------------------------------------------------------
  // Modules topological init order (D-165), Program vs Script mode, D-183 effects
  // ---------------------------------------------------------------------------

  [Fact]
  public void Modules_TopologicalInitOrderAndMainResultHandler_D165() {
    var source = new InMemoryModuleSource();
    source.AddFile("root/modules/Base.pino", """
      module Base
      pub val BASE_VAL = 10
      """);
    source.AddFile("root/modules/Mid.pino", """
      module Mid
      import Base
      pub val MID_VAL = Base::BASE_VAL + 5
      """);

    const string RootSource = """
      import Mid
      import Base

      val TOTAL = Mid::MID_VAL + Base::BASE_VAL

      fn main() Result[void, string] {
        if TOTAL <= 0 {
          return Result::Failure("bad")
        }
        return Result::Success
      }
      """;

    var parsed = Parser.Parse(new SourceText(RootSource, "root/app.pino"));
    Assert.Empty(parsed.Diagnostics);
    Compilation compilation = Compilation.Create(parsed, source);
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

    IrModule module = IrLowerer.Lower(compilation);
    IrVerificationResult verification = IrVerifier.Verify(module);
    Assert.True(verification.IsValid, string.Join("\n", verification.Errors));
    Assert.True(module.IsProgramMode);

    // D-165: __pino_entry calls __pino_init_Base before __pino_init_Mid before main.
    Assert.NotNull(module.EntryFuncId);
    IrFunction entry = module.Functions[module.EntryFuncId!.Value.Index];
    string[] callOrder = entry.Blocks
        .SelectMany(static b => b.Instructions)
        .OfType<CallDirect>()
        .Select(c => module.Functions[c.Callee.Index].Name)
        .ToArray();

    int baseInit = Array.FindIndex(callOrder, static n => n.Contains("init_Base", StringComparison.OrdinalIgnoreCase));
    int midInit = Array.FindIndex(callOrder, static n => n.Contains("init_Mid", StringComparison.OrdinalIgnoreCase));
    int mainCall = Array.FindIndex(callOrder, static n => n == "main");

    Assert.True(baseInit >= 0, $"Expected base init in [{string.Join(", ", callOrder)}]");
    Assert.True(midInit > baseInit, $"Expected mid init after base init in [{string.Join(", ", callOrder)}]");
    Assert.True(mainCall > midInit, $"Expected main call after mid init in [{string.Join(", ", callOrder)}]");

    // ERR-18..20: Program-mode Result main failure prints to stderr and exits 1.
    string entryPrinted = IrPrinter.PrintFunction(module, entry);
    Assert.Contains("pino_eprintln", entryPrinted);
    Assert.Contains("pino_exit", entryPrinted);
  }

  [Fact]
  public void ModuleStateEffects_D183_PropagatesOntoEveryLoweredFunction() {
    const string Source = """
      var counter = 0

      fn pure_add(a int, b int) int {
        return a + b
      }

      fn read_counter() int {
        return counter
      }

      fn inc_counter() {
        counter += 1
      }

      inc_counter()
      val _ = read_counter() + pure_add(1, 2)
      """;

    IrModule module = LowerAndVerify(Source);
    Assert.False(module.IsProgramMode);

    Assert.Equal(ModuleStateEffects.Pure, GetFunction(module, "pure_add").ModuleStateEffects);
    ModuleStateEffects readEffects = GetFunction(module, "read_counter").ModuleStateEffects;
    Assert.True(readEffects.ReadsModuleState && !readEffects.WritesModuleState);
    Assert.Equal(ModuleStateEffects.ReadWrite, GetFunction(module, "inc_counter").ModuleStateEffects);
  }

  [Fact]
  public void Fn31_ReadBeforeMutArgumentEvaluationOrder() {
    const string Source = """
      fn push_first(mut xs []int) {
        xs:push(xs[0])
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction fn = GetFunction(module, "push_first");
    List<IrInstruction> instrs = fn.Blocks[0].Instructions;

    int vecGetIdx = instrs.FindIndex(static i => i is VecGet);
    int ensureUniqueIdx = instrs.FindIndex(static i => i is EnsureUnique);
    int vecPushIdx = instrs.FindIndex(static i => i is VecPush);

    Assert.True(vecGetIdx >= 0 && ensureUniqueIdx > vecGetIdx && vecPushIdx > ensureUniqueIdx);
  }

  [Fact]
  public void VecJoinIntrinsic_LowersToInlineIrLoop() {
    const string Source = """
      fn csv(parts []string) string {
        return parts:join(",")
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction fn = GetFunction(module, "csv");
    string printed = IrPrinter.PrintFunction(module, fn);

    Assert.Contains("vec.len", printed);
    Assert.Contains("vec.get.unchecked", printed);
    Assert.Contains("str.concat", printed);
  }

  // ---------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------

  private static IrModule LowerAndVerify(string source) {
    var parsed = Parser.Parse(new SourceText(source, "test.pino"));
    Assert.Empty(parsed.Diagnostics);
    Compilation compilation = Compilation.Create(parsed);
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

    IrModule module = IrLowerer.Lower(compilation);
    IrVerificationResult verification = IrVerifier.Verify(module);
    Assert.True(
        verification.IsValid,
        string.Join("\n", verification.Errors) + "\n\n" + IrPrinter.Print(module));
    return module;
  }

  private static IrFunction GetFunction(IrModule module, string name) =>
      Assert.Single(module.Functions, f => f.Name == name);

  private static string[] AllPanicMessages(IrModule module, IrFunction fn) {
    var msgs = new List<string>();
    var constStrings = new Dictionary<IrTempId, string>();
    foreach (IrBasicBlock block in fn.Blocks) {
      foreach (IrInstruction instr in block.Instructions) {
        if (instr is ConstString cs) {
          constStrings[cs.Dest] = module.StringPool[cs.StringPoolId];
        }
      }

      if (block.Terminator is Panic panic && panic.Message.Kind == IrOperandKind.Temp
          && constStrings.TryGetValue(panic.Message.TempId, out string? text)) {
        msgs.Add(text);
      }
    }

    return [.. msgs];
  }
}
