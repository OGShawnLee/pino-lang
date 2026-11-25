using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.IR.Passes;
using Pino.Compiler.Parsing;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.IR.Passes;

/// <summary>
/// Golden, unit, negative-verifier, and full 100% conformance post-RC verification tests for
/// Step 1 RC insertion (<see cref="RcInsertPass"/> and <see cref="IrVerifier.VerifyAfterRc"/>).
/// </summary>
public sealed class RcInsertPassTests {
  private static readonly string s_repoRoot = ParserTestHelpers.FindRepositoryRoot();
  private static readonly string s_conformanceRoot = Path.Combine(s_repoRoot, "conformance");

  // ---------------------------------------------------------------------------
  // 1. Section 4.2 Golden Tests (sum_vec, area, Log::record)
  // ---------------------------------------------------------------------------

  [Fact]
  public void SumVec_Step1Rc_MatchesGoldenTextAndVerifiesPostRc() {
    const string Source = """
      fn sum_vec(xs []int) int {
        var total = 0
        for x in xs {
          total += x
        }
        return total
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    IrFunction fn = Assert.Single(module.Functions, static f => f.Name == "sum_vec");

    string printed = IrPrinter.PrintFunction(module, fn);

    const string Expected = """
      fn sum_vec(@owned %xs: []int) -> int [pure] {
        slots: $total: int [mut], $snap: []int, $i: int [mut], $len: int
        bb0:
          %t0 = const.i64 0
          store $total, %t0
          rc_inc %xs
          store $snap, %xs
          %t1 = load $snap
          rc_inc %t1
          %t2 = vec.len %t1
          rc_dec %t1
          store $len, %t2
          %t3 = const.i64 0
          store $i, %t3
          br bb1
        bb1:
          %t4 = load $i
          %t5 = load $len
          %t6 = cmp.lt.i64 %t4, %t5
          br_if %t6, bb2, bb4
        bb2:
          %t7 = load $snap
          rc_inc %t7
          %t8 = load $i
          %x = vec.get.unchecked %t7, %t8
          rc_dec %t7
          %t10 = load $total
          %t11 = add.i64 %t10, %x
          store $total, %t11
          br bb3
        bb3:
          %t12 = load $i
          %t14 = const.i64 1
          %t13 = add.i64 %t12, %t14
          store $i, %t13
          br bb1
        bb4:
          %t15 = load $total
          %t16 = load $snap
          rc_dec %t16
          rc_dec %xs
          ret %t15
      }

      """;

    Assert.Equal(NormalizeLf(Expected), printed);
  }

  [Fact]
  public void Area_Step1Rc_EmitsZeroRcOpsAndMatchesGoldenText() {
    const string Source = """
      union Shape {
        Circle(r float)
        Rect(w float, h float)
      }

      fn area(s Shape) float {
        return match s {
          when Shape::Circle(r) -> 3.14159 * r * r
          when Shape::Rect(w, h) -> w * h
        }
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    IrFunction fn = Assert.Single(module.Functions, static f => f.Name == "area");

    Assert.Equal(0, CountInstructions<RcInc>(fn));
    Assert.Equal(0, CountInstructions<RcDec>(fn));

    string printed = IrPrinter.PrintFunction(module, fn);
    const string Expected = """
      fn area(@owned %s: Shape) -> float [pure] {
        slots: $res: float [mut]
        bb0:
          %tag = union.tag %s
          switch %tag, [0 -> bb1, 1 -> bb2]
        bb1:
          %r = union.field %s, 0, 0
          %t2 = const.f64 3.14159
          %t3 = mul.f64 %t2, %r
          %t4 = mul.f64 %t3, %r
          store $res, %t4
          br bb3
        bb2:
          %w = union.field %s, 1, 0
          %h = union.field %s, 1, 1
          %t7 = mul.f64 %w, %h
          store $res, %t7
          br bb3
        bb3:
          %t8 = load $res
          ret %t8
      }

      """;

    Assert.Equal(NormalizeLf(Expected), printed);
  }

  [Fact]
  public void LogRecord_Step1Rc_MatchesGoldenTextAndVerifiesPostRc() {
    const string Source = """
      struct Log {
        mut entries []string

        mut fn record(msg string) {
          self:entries:push(msg)
        }
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    IrFunction fn = Assert.Single(module.Functions, static f => f.Name == "Log::record");

    string printed = IrPrinter.PrintFunction(module, fn);
    const string Expected = """
      fn Log::record(@mut %self: ptr<Log>, @owned %msg: string) -> void [pure] {
        bb0:
          %t0 = field_addr %self, Log.entries
          ensure_unique %t0
          rc_inc %msg
          vec.push %t0, %msg
          rc_dec %msg
          ret.void
      }

      """;

    Assert.Equal(NormalizeLf(Expected), printed);
  }

  // ---------------------------------------------------------------------------
  // 2. Lifetime Edge Cases
  // ---------------------------------------------------------------------------

  [Fact]
  public void SelfAssignment_IncBeforeDecOld_VerifiesPostRc() {
    const string Source = """
      fn self_assign(seed string) string {
        var x = seed + "!"
        x = x
        return x
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    IrFunction fn = Assert.Single(module.Functions, static f => f.Name == "self_assign");
    List<IrInstruction> instrs = fn.Blocks[0].Instructions;

    // Verify self-assignment evaluates and rc_inc's the loaded value BEFORE loading and rc_dec'ing the old slot value.
    int firstRcIncIdx = instrs.FindIndex(static i => i is RcInc);
    int overwriteStoreIdx = instrs.FindLastIndex(static i => i is StoreSlot);
    Assert.True(firstRcIncIdx >= 0 && firstRcIncIdx < overwriteStoreIdx);
    Assert.IsType<LoadSlot>(instrs[overwriteStoreIdx - 1]);
    Assert.IsType<RcDec>(instrs[overwriteStoreIdx + 1]);
  }

  [Fact]
  public void NestedFieldOverwrite_IncProjectedFieldBeforeDroppingOldContainer_VerifiesPostRc() {
    const string Source = """
      struct Box {
        mut items []string
      }

      union Node {
        Link(label string, next Node)
        End
      }

      fn advance(start Node) Node {
        var cur = start
        cur = match cur {
          when Node::Link(lbl, next) -> next
          when Node::End -> Node::End
        }
        return cur
      }

      fn replace_with_elem(b Box) string {
        var xs = b:items
        var s = xs[0]
        s = xs[0]
        return s
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    IrFunction advance = Assert.Single(module.Functions, static f => f.Name == "advance");
    Assert.True(CountInstructions<RcInc>(advance) > 0);
    Assert.True(CountInstructions<RcDec>(advance) > 0);

    IrFunction replaceElem = Assert.Single(module.Functions, static f => f.Name == "replace_with_elem");
    Assert.True(CountInstructions<RcInc>(replaceElem) > 0);
    Assert.True(CountInstructions<RcDec>(replaceElem) > 0);
  }

  [Fact]
  public void EarlyReturnInsideNestedForIfMatch_DropsAllLiveLocalsAndParams() {
    const string Source = """
      union Status {
        Ok(msg string)
        Err(code int)
      }

      fn find_first(items []string, st Status) string {
        var prefix = "found:"
        for item in items {
          val candidate = prefix + item
          if item:len > 2 {
            match st {
              when Status::Ok(m) {
                if m:len > 0 {
                  return candidate + m
                }
              }
              when Status::Err(c) {
                if c < 0 {
                  return prefix
                }
              }
            }
          }
        }
        return prefix
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    IrFunction fn = Assert.Single(module.Functions, static f => f.Name == "find_first");
    int retCount = fn.Blocks.Count(static b => b.Terminator is Ret);
    Assert.True(retCount >= 3);
    Assert.True(CountInstructions<RcDec>(fn) > 0);
  }

  [Fact]
  public void BubbleQuestionMarkAndOrRecovery_DropLiveLocalsOnAllPaths() {
    const string Source = """
      fn parse_and_wrap(raw string) Result[string, string] {
        val label = "num=" + raw
        val n = int(raw)?
        val fallback = int("bad") or {
          val msg = "err:" + err
          if msg:len > 100 {
            return Result::Failure(msg)
          }
          yield n
        }
        return Result::Success(label + str(fallback))
      }

      fn opt_bubble(xs []string) Option[string] {
        val tag = "tag:"
        val first = xs:find(it:len > 0)?
        return Option::Some(tag + first)
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    IrFunction fn1 = Assert.Single(module.Functions, static f => f.Name == "parse_and_wrap");
    IrFunction fn2 = Assert.Single(module.Functions, static f => f.Name == "opt_bubble");
    Assert.True(CountInstructions<RcDec>(fn1) > 0);
    Assert.True(CountInstructions<RcDec>(fn2) > 0);
  }

  [Fact]
  public void BreakAndContinueInLoops_DropLoopBodyLocalsWithoutLeaksOrDoubleFrees() {
    const string Source = """
      fn loop_control(items []string) string {
        var acc = ""
        for i, item in items {
          var local_copy = item + "!"
          val chosen = if i % 2 == 0 then local_copy + "e" else local_copy + "o"
          if i == 1 {
            continue
          }
          if i == 3 {
            break
          }
          acc = acc + chosen
        }
        return acc
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    IrFunction fn = Assert.Single(module.Functions, static f => f.Name == "loop_control");
    Assert.True(CountInstructions<RcInc>(fn) > 0);
    Assert.True(CountInstructions<RcDec>(fn) > 0);
  }

  [Fact]
  public void IgnoredTupleDestructuringFieldsAndUnusedCallResults_AreDroppedImmediately() {
    const string Source = """
      fn make_pair(a string) @(first string, second string, count int) {
        return @(first: a + "1", second: a + "2", count: 42)
      }

      fn make_str() string {
        return "unused" + "!"
      }

      fn use_partial(x string) string {
        make_str()
        val @(second: kept) = make_pair(x)
        val @(count) = make_pair(x)
        return kept + str(count)
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    IrFunction fn = Assert.Single(module.Functions, static f => f.Name == "use_partial");

    // Verify unused make_str() result and both tuple temps are immediately RcDec'd.
    var tupleTemps = fn.Temps
        .Where(t => module.Types.GetType(t.Type).Kind == IrTypeKind.Tuple)
        .Select(static t => t.Id.Index)
        .ToHashSet();
    Assert.Equal(2, tupleTemps.Count);

    var decTemps = fn.Blocks
        .SelectMany(static b => b.Instructions)
        .OfType<RcDec>()
        .Where(static d => d.Value.IsTemp)
        .Select(static d => d.Value.Index)
        .ToHashSet();

    foreach (int tt in tupleTemps) {
      Assert.Contains(tt, decTemps);
    }
  }

  [Fact]
  public void PanicPaths_EmitZeroRcDecBeforePanic() {
    const string Source = """
      fn always_panic(xs []string) int {
        val msg = "boom:" + str(xs:len)
        panic(msg)
      }

      fn checked_div(a int, b int, s string) int {
        val copy = s + "!"
        val q = a // b
        return q + copy:len
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    IrFunction alwaysPanic = Assert.Single(module.Functions, static f => f.Name == "always_panic");
    Assert.Equal(0, CountInstructions<RcDec>(alwaysPanic));

    IrFunction checkedDiv = Assert.Single(module.Functions, static f => f.Name == "checked_div");
    foreach (IrBasicBlock block in checkedDiv.Blocks) {
      if (block.Terminator is Panic) {
        Assert.DoesNotContain(block.Instructions, static i => i is RcDec);
      }
    }
  }

  [Fact]
  public void Closures_PinoCellPinoEnvClosureMakeCallClosure_BalanceRcCleanly() {
    const string Source = """
      fn make_logger(prefix string) fn (string) -> string {
        var history = prefix
        val append = fn (msg string) {
          history = history + ":" + msg
          history = history
          return history
        }
        return append
      }

      fn run_closure() string {
        val log = make_logger("init")
        val a = log("one")
        val b = log("two")
        return a + "|" + b
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    IrFunction makeLogger = Assert.Single(module.Functions, static f => f.Name == "make_logger");
    Assert.Contains(makeLogger.Blocks.SelectMany(static b => b.Instructions), static i => i is CellNew);
    Assert.Contains(makeLogger.Blocks.SelectMany(static b => b.Instructions), static i => i is EnvNew);
    Assert.Contains(makeLogger.Blocks.SelectMany(static b => b.Instructions), static i => i is ClosureMake);

    IrFunction runClosure = Assert.Single(module.Functions, static f => f.Name == "run_closure");
    Assert.Contains(runClosure.Blocks.SelectMany(static b => b.Instructions), static i => i is CallClosure);

    // Inner lambda captures `history` cell via EnvFieldGet and updates it via CellGet/CellSet.
    IrFunction lamFn = Assert.Single(module.Functions, static f => f.Name.StartsWith("__lam_", StringComparison.Ordinal));
    Assert.Contains(lamFn.Blocks.SelectMany(static b => b.Instructions), static i => i is EnvFieldGet);
    Assert.Contains(lamFn.Blocks.SelectMany(static b => b.Instructions), static i => i is CellGet);
    Assert.Contains(lamFn.Blocks.SelectMany(static b => b.Instructions), static i => i is CellSet);
  }

  [Fact]
  public void Interfaces_IfaceBoxCallIfaceAndVTableDropCloneHelpers_BalanceRcCleanly() {
    const string Source = """
      interface Greeter {
        mut tag string
        fn greet(name string) string
        mut fn rename(next string)
      }

      struct NamedGreeter {
        mut tag string
        entries []string

        fn greet(name string) string {
          return self:tag + ":" + name
        }

        mut fn rename(next string) {
          self:tag = next
        }
      }

      interface Counter {
        fn value() int
      }

      struct SimpleCounter {
        n int

        fn value() int {
          return self:n
        }
      }

      fn exercise_interfaces() string {
        var g Greeter = NamedGreeter { tag: "hi", entries: ["a"] }
        g:rename("hello")
        val t = g:tag
        val out = g:greet("world")
        val c Counter = SimpleCounter { n: 7 }
        return t + out + str(c:value())
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);

    // Check vtable drop/clone helpers for RC-managed struct vs pointer-free struct.
    IrVTable namedVt = Assert.Single(module.VTables, vt => module.Types.GetType(vt.ConcreteStructType).Name == "NamedGreeter");
    Assert.NotNull(namedVt.DropFuncId);
    Assert.NotNull(namedVt.CloneFuncId);
    IrFunction namedDrop = module.Functions[namedVt.DropFuncId!.Value.Index];
    IrFunction namedClone = module.Functions[namedVt.CloneFuncId!.Value.Index];
    Assert.Equal(1, CountInstructions<RcDec>(namedDrop));
    Assert.Equal(1, CountInstructions<RcInc>(namedClone));
    Assert.Equal(1, CountInstructions<RcDec>(namedClone));

    IrVTable counterVt = Assert.Single(module.VTables, vt => module.Types.GetType(vt.ConcreteStructType).Name == "SimpleCounter");
    Assert.NotNull(counterVt.DropFuncId);
    Assert.NotNull(counterVt.CloneFuncId);
    IrFunction counterDrop = module.Functions[counterVt.DropFuncId!.Value.Index];
    IrFunction counterClone = module.Functions[counterVt.CloneFuncId!.Value.Index];
    Assert.Equal(0, CountInstructions<RcInc>(counterDrop));
    Assert.Equal(0, CountInstructions<RcDec>(counterDrop));
    Assert.Equal(0, CountInstructions<RcInc>(counterClone));
    Assert.Equal(0, CountInstructions<RcDec>(counterClone));

    IrFunction exercise = Assert.Single(module.Functions, static f => f.Name == "exercise_interfaces");
    Assert.Contains(exercise.Blocks.SelectMany(static b => b.Instructions), static i => i is IfaceBox);
    Assert.Contains(exercise.Blocks.SelectMany(static b => b.Instructions), static i => i is CallIface);
    Assert.Contains(exercise.Blocks.SelectMany(static b => b.Instructions), static i => i is IfaceFieldGet);
  }

  [Fact]
  public void MonomorphizedGenerics_ZeroRcForPointerFreeInstantiationAndBalancedForRcInstantiation() {
    const string Source = """
      @@Generic[T]
      fn dup_first(xs []T) T {
        val a = xs[0]
        return a
      }

      @@Generic[T]
      fn identity(x T) T {
        return x
      }

      fn test_mono() string {
        val n = identity[int](42)
        val s = identity[string]("ok")
        val a = dup_first[int]([1, 2])
        val b = dup_first[string](["x", "y"])
        return s + b + str(n + a)
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);

    IrFunction idInt = Assert.Single(module.Functions, static f => f.Name.StartsWith("identity", StringComparison.Ordinal) && f.Name.Contains("int", StringComparison.Ordinal));
    Assert.Equal(0, CountInstructions<RcInc>(idInt));
    Assert.Equal(0, CountInstructions<RcDec>(idInt));

    IrFunction dupStr = Assert.Single(module.Functions, static f => f.Name.StartsWith("dup_first", StringComparison.Ordinal) && f.Name.Contains("string", StringComparison.Ordinal));
    Assert.True(CountInstructions<RcInc>(dupStr) > 0);
    Assert.True(CountInstructions<RcDec>(dupStr) > 0);
  }

  [Fact]
  public void InlineCallbackLoops_WithRcElements_BalanceRcCleanly() {
    const string Source = """
      fn process_words(words []string) []string {
        val mapped = words:map(fn (w) -> w + "!")
        val filtered = mapped:filter(fn (w) {
          val local = w + "?"
          return local:len > 3
        })
        val found = filtered:find(it:len > 4)
        filtered:each(fn (w) {
          val ignored = w + "."
        })
        return match found {
          when Option::Some(s) -> [s]
          when Option::None -> filtered
        }
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    IrFunction fn = Assert.Single(module.Functions, static f => f.Name == "process_words");
    Assert.True(CountInstructions<RcInc>(fn) > 0);
    Assert.True(CountInstructions<RcDec>(fn) > 0);
    // Inline callback loops allocate zero closures.
    Assert.DoesNotContain(fn.Blocks.SelectMany(static b => b.Instructions), static i => i is ClosureMake);
  }

  [Fact]
  public void GlobalsAndShutdown_DropsEveryNonPointerFreeGlobalAndSkipsPointerFreeGlobals() {
    const string Source = """
      val count = 10
      val flag = true
      val title = "hello" + "!"
      var items = ["a", "b"]
      var lookup = map[string, int] { "k" -> 1 }

      items = ["c"]
      println(title, count, flag, items:len, lookup:len)
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    Assert.NotNull(module.ShutdownFuncId);
    IrFunction shutdown = module.Functions[module.ShutdownFuncId!.Value.Index];

    // Exactly 3 RC-managed globals (title, items, lookup); count and flag are pointer-free -> 0 drops for them.
    Assert.Equal(3, CountInstructions<RcDec>(shutdown));
    Assert.Equal(3, CountInstructions<AddrGlobal>(shutdown));
    Assert.Equal(3, CountInstructions<LoadPtr>(shutdown));

    // In __pino_entry, initial global stores do NOT load/drop old values, while the overwrite `items = ["c"]` DOES.
    IrFunction entry = module.Functions[module.EntryFuncId!.Value.Index];
    List<IrInstruction> entryInstrs = entry.Blocks[0].Instructions;
    int storePtrCount = entryInstrs.Count(static i => i is StorePtr);
    Assert.Equal(6, storePtrCount); // 5 initial stores + 1 overwrite of `items`
    // Verify the final StorePtr (overwrite of `items`) is bracketed by LoadPtr + RcDec.
    int lastStorePtrIdx = entryInstrs.FindLastIndex(static i => i is StorePtr);
    Assert.IsType<LoadPtr>(entryInstrs[lastStorePtrIdx - 1]);
    Assert.IsType<RcDec>(entryInstrs[lastStorePtrIdx + 1]);
  }

  [Fact]
  public void DesignGoldenModules_PassPostRcVerificationDirectly() {
    Assert.True(IrVerifier.VerifyAfterRc(IrPrinterGoldenTests.BuildSumVecModule(withStep1Rc: true)).IsValid);
    Assert.True(IrVerifier.VerifyAfterRc(IrPrinterGoldenTests.BuildAreaModule()).IsValid);
    Assert.True(IrVerifier.VerifyAfterRc(IrPrinterGoldenTests.BuildLogRecordModule(withStep1Rc: true)).IsValid);
  }

  [Fact]
  public void RcDescriptors_CoverEveryRcManagedTypeKindAndOmitPointerFreeTypes() {
    const string Source = """
      struct Point {
        x float
        y float
      }

      struct Person {
        name string
        age int
      }

      union MaybeName {
        Present(name string)
        Absent
      }

      union StrList {
        Cons(head string, tail StrList)
        Nil
      }

      interface Named {
        name string
      }

      fn make_tup(s string) @(a string, b int) {
        return @(a: s, b: 1)
      }

      fn build_all() {
        val pt = Point { x: 1.0, y: 2.0 }
        val p = Person { name: "Ada", age: 30 }
        val mn = MaybeName::Present("Ada")
        val sl = StrList::Cons("head", StrList::Nil)
        val v = ["x"]
        val m = map[string, int] { "k" -> 1 }
        val st = set[string] { "s" }
        val mut pl = Pool[string]
        val h = pl:add("e")
        val rx = regex("a+")
        val named Named = p
        var captured = "cap"
        val clos = fn () -> captured
        val @(a) = make_tup("t")
        println(pt:x, mn == MaybeName::Absent, sl == StrList::Nil, v:len, m:len, st:len, h in pl, rx:pattern, named:name, clos(), a)
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);
    Assert.Equal(IrPassStage.PostRcStep1, module.Stage);

    // Every descriptor must be for an RC-managed type (never Point, int, float, Handle[string], etc.).
    Assert.All(module.RcDescriptors, desc => Assert.True(module.Types.GetType(desc.Type).IsRcManaged));

    var kinds = module.RcDescriptors.Select(static d => d.Kind).ToHashSet();
    Assert.Contains(IrRcDescriptorKind.String, kinds);
    Assert.Contains(IrRcDescriptorKind.Regex, kinds);
    Assert.Contains(IrRcDescriptorKind.InlineStruct, kinds);
    Assert.Contains(IrRcDescriptorKind.InlineTuple, kinds);
    Assert.Contains(IrRcDescriptorKind.InlineUnion, kinds);
    Assert.Contains(IrRcDescriptorKind.BoxedRecursiveUnion, kinds);
    Assert.Contains(IrRcDescriptorKind.Vector, kinds);
    Assert.Contains(IrRcDescriptorKind.Map, kinds);
    Assert.Contains(IrRcDescriptorKind.Set, kinds);
    Assert.Contains(IrRcDescriptorKind.Pool, kinds);
    Assert.Contains(IrRcDescriptorKind.Cell, kinds);
    Assert.Contains(IrRcDescriptorKind.Env, kinds);
    Assert.Contains(IrRcDescriptorKind.Closure, kinds);
    Assert.Contains(IrRcDescriptorKind.Interface, kinds);

    // Verify Person inline struct descriptor only lists RC field `name` (index 0), not `age` (int).
    IrType personType = Assert.Single(module.Types.Types, static t => t.Name == "Person");
    Assert.True(module.TryGetRcDescriptor(personType.Id, out IrTypeRcDescriptor? personDesc));
    Assert.Equal(IrRcDescriptorKind.InlineStruct, personDesc!.Kind);
    IrRcFieldRef onlyRcField = Assert.Single(personDesc.RcFields);
    Assert.Equal("name", onlyRcField.Name);
    Assert.Equal(0, onlyRcField.FieldIndex);

    // Verify Point (pointer-free struct) has NO descriptor.
    IrType pointType = Assert.Single(module.Types.Types, static t => t.Name == "Point");
    Assert.False(module.TryGetRcDescriptor(pointType.Id, out _));
  }

  [Fact]
  public void ConditionalSlotInitialization_UsesBoolInitFlagAndVerifiesPostRc() {
    // Synthetic IR where slot $maybe is initialized along only one branch before merging and being overwritten / dropped.
    var types = new IrTypeTable();
    var module = new IrModule(types);
    int s1 = module.InternString("first");
    int s2 = module.InternString("second");

    IrFunction fn = module.AddFunction(
        "cond_init_slot",
        [
          new IrParam("c1", types.Bool, IrParamOwnership.Owned),
          new IrParam("c2", types.Bool, IrParamOwnership.Owned),
        ],
        types.Void);

    IrSlotId maybeSlot = fn.AddSlot("maybe", types.String, isMutSlot: true);
    IrTempId t1 = fn.AddTemp(types.String);
    IrTempId t2 = fn.AddTemp(types.String);

    IrBasicBlock bb0 = fn.AddBlock();
    IrBasicBlock bb1 = fn.AddBlock();
    IrBasicBlock bb2 = fn.AddBlock();
    IrBasicBlock bb3 = fn.AddBlock();
    IrBasicBlock bb4 = fn.AddBlock();

    bb0.Terminator = new BrIf(IrOperand.FromParam(0), bb1.Id, bb2.Id);

    bb1.Instructions.Add(new ConstString(t1, s1));
    bb1.Instructions.Add(new StoreSlot(maybeSlot, t1));
    bb1.Terminator = new Br(bb2.Id);

    // In bb2, $maybe is MaybeInit (Init from bb1, Uninit from bb0), then conditionally overwritten in bb3, and dropped at bb4.
    bb2.Terminator = new BrIf(IrOperand.FromParam(1), bb3.Id, bb4.Id);

    bb3.Instructions.Add(new ConstString(t2, s2));
    bb3.Instructions.Add(new StoreSlot(maybeSlot, t2));
    bb3.Terminator = new Br(bb4.Id);

    bb4.Terminator = new RetVoid();

    Assert.True(IrVerifier.Verify(module).IsValid);
    RcInsertPass.Run(module);

    IrVerificationResult postRc = IrVerifier.VerifyAfterRc(module);
    Assert.True(postRc.IsValid, string.Join("\n", postRc.Errors) + "\n" + IrPrinter.Print(module));
  }

  // ---------------------------------------------------------------------------
  // 3. IrVerifier(PostRc) Negative Tests
  // ---------------------------------------------------------------------------

  [Fact]
  public void VerifierPostRc_RejectsUninitializedSlotDropAndRead() {
    var types = new IrTypeTable();
    var module = new IrModule(types);

    IrFunction uninitDrop = module.AddFunction("uninit_drop", [], types.Void);
    IrSlotId slot1 = uninitDrop.AddSlot("x", types.String, isMutSlot: true);
    IrTempId d0 = uninitDrop.AddTemp(types.String, "d0");
    IrBasicBlock bb0 = uninitDrop.AddBlock();
    bb0.Instructions.Add(new LoadSlot(d0, slot1));
    bb0.Instructions.Add(new RcDec(d0));
    bb0.Terminator = new RetVoid();

    IrFunction uninitRead = module.AddFunction("uninit_read", [], types.String);
    IrSlotId slot2 = uninitRead.AddSlot("y", types.String, isMutSlot: true);
    IrTempId r0 = uninitRead.AddTemp(types.String, "r0");
    IrBasicBlock rbb0 = uninitRead.AddBlock();
    rbb0.Instructions.Add(new LoadSlot(r0, slot2));
    rbb0.Instructions.Add(new RcInc(r0));
    rbb0.Terminator = new Ret(r0);

    IrVerificationResult res = IrVerifier.VerifyAfterRc(module);
    Assert.False(res.IsValid);
    Assert.Contains(res.Errors, static e => e.RuleCode == "IR-RC-03" && e.FunctionName == "uninit_drop" && e.Message.Contains("drop", StringComparison.OrdinalIgnoreCase));
    Assert.Contains(res.Errors, static e => e.RuleCode == "IR-RC-03" && e.FunctionName == "uninit_read" && e.Message.Contains("read", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void VerifierPostRc_RejectsRcIncAndRcDecOnPointerFreeTypes() {
    var types = new IrTypeTable();
    var module = new IrModule(types);
    IrFunction fn = module.AddFunction("pf_rc", [new IrParam("n", types.Int, IrParamOwnership.Owned)], types.Void);

    IrBasicBlock bb0 = fn.AddBlock();
    bb0.Instructions.Add(new RcInc(IrOperand.FromParam(0)));
    bb0.Instructions.Add(new RcDec(IrOperand.FromParam(0)));
    bb0.Terminator = new RetVoid();

    IrVerificationResult res = IrVerifier.VerifyAfterRc(module);
    Assert.False(res.IsValid);
    Assert.Equal(2, res.Errors.Count(static e => e.RuleCode == "IR-RC-01"));
  }

  [Fact]
  public void VerifierPostRc_RejectsMissingGlobalShutdownDrop() {
    var types = new IrTypeTable();
    var module = new IrModule(types);
    module.AddGlobal("g_str", types.String, isMutable: false);

    IrFunction shutdown = module.AddFunction("__pino_shutdown", [], types.Void);
    shutdown.AddBlock().Terminator = new RetVoid();
    module.ShutdownFuncId = shutdown.Id;

    IrVerificationResult res = IrVerifier.VerifyAfterRc(module);
    Assert.False(res.IsValid);
    Assert.Contains(res.Errors, static e => e.RuleCode == "IR-RC-06" && e.Message.Contains("g_str", StringComparison.Ordinal));
  }

  [Fact]
  public void VerifierPostRc_RejectsDoubleFreeUnownedOwnedCallArgAndLeaks() {
    var types = new IrTypeTable();
    var module = new IrModule(types);

    IrFunction sink = module.AddFunction("sink", [new IrParam("s", types.String, IrParamOwnership.Owned)], types.Void);
    IrBasicBlock sinkBb0 = sink.AddBlock();
    sinkBb0.Instructions.Add(new RcDec(IrOperand.FromParam(0)));
    sinkBb0.Terminator = new RetVoid();

    // Bad caller: passes @owned param `s` to `sink(s)` without rc_inc, then rc_dec's `s` (double-free / unowned)!
    IrFunction badCaller = module.AddFunction("bad_caller", [new IrParam("s", types.String, IrParamOwnership.Owned)], types.Void);
    IrBasicBlock bb0 = badCaller.AddBlock();
    bb0.Instructions.Add(new CallDirect(null, sink.Id, [IrOperand.FromParam(0)]));
    bb0.Instructions.Add(new RcDec(IrOperand.FromParam(0)));
    bb0.Terminator = new RetVoid();

    // Leaking function: never drops @owned param `s`.
    IrFunction leaker = module.AddFunction("leaker", [new IrParam("s", types.String, IrParamOwnership.Owned)], types.Void);
    leaker.AddBlock().Terminator = new RetVoid();

    IrVerificationResult res = IrVerifier.VerifyAfterRc(module);
    Assert.False(res.IsValid);
    Assert.Contains(res.Errors, static e => e.RuleCode == "IR-RC-04" && e.FunctionName == "bad_caller");
    Assert.Contains(res.Errors, static e => e.RuleCode == "IR-RC-05" && e.FunctionName == "leaker");
  }

  // ---------------------------------------------------------------------------
  // 4. Full 100% Conformance Sweep (.out, runtime .err, main.pino) — NO SKIP LIST
  // ---------------------------------------------------------------------------

  public static TheoryData<string> PositiveConformanceCases() {
    var data = new TheoryData<string>();
    foreach (string file in Directory.EnumerateFiles(s_conformanceRoot, "*.pino", SearchOption.AllDirectories)
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      if (File.Exists(Path.ChangeExtension(file, ".out"))) {
        data.Add(ToConformanceRelativePath(file));
      }
    }

    return data;
  }

  public static TheoryData<string> RuntimeErrConformanceCases() {
    var data = new TheoryData<string>();
    foreach (string rel in Conformance.ConformanceRunner.RuntimeErrCases.Keys
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      data.Add(rel);
    }

    return data;
  }

  [Theory]
  [MemberData(nameof(PositiveConformanceCases))]
  public void PositiveConformanceCase_PostRcVerifiesCleanly(string relativePath) {
    Compilation compilation = CompileConformanceFile(relativePath);
    IrModule module = IrLowerer.Lower(compilation);
    RcInsertPass.Run(module);
    IrVerificationResult verification = IrVerifier.VerifyAfterRc(module);
    Assert.True(
        verification.IsValid,
        $"{relativePath} failed IrVerifier(PostRc) with {verification.Errors.Length} error(s):\n"
        + string.Join("\n", verification.Errors)
        + "\n\n--- Printed Post-RC IR ---\n"
        + IrPrinter.Print(module));
  }

  [Theory]
  [MemberData(nameof(RuntimeErrConformanceCases))]
  public void RuntimeErrConformanceCase_PostRcVerifiesCleanly(string relativePath) {
    Compilation compilation = CompileConformanceFile(relativePath);
    IrModule module = IrLowerer.Lower(compilation);
    RcInsertPass.Run(module);
    IrVerificationResult verification = IrVerifier.VerifyAfterRc(module);
    Assert.True(
        verification.IsValid,
        $"{relativePath} failed IrVerifier(PostRc) with {verification.Errors.Length} error(s):\n"
        + string.Join("\n", verification.Errors));
  }

  [Fact]
  public void PostRcConformanceSweep_Covers100PercentOfPositiveAndRuntimeErrCases() {
    int outTotal = 0;
    int outPassed = 0;
    var failures = new List<string>();

    foreach (string file in Directory.EnumerateFiles(s_conformanceRoot, "*.pino", SearchOption.AllDirectories)
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      if (!File.Exists(Path.ChangeExtension(file, ".out"))) {
        continue;
      }

      outTotal++;
      string rel = ToConformanceRelativePath(file);
      try {
        Compilation compilation = CompileConformanceFile(rel);
        IrModule module = IrLowerer.Lower(compilation);
        RcInsertPass.Run(module);
        IrVerificationResult verification = IrVerifier.VerifyAfterRc(module);
        if (!verification.IsValid) {
          failures.Add($"{rel}: {string.Join("; ", verification.Errors)}");
        } else {
          outPassed++;
        }
      } catch (Exception ex) {
        failures.Add($"{rel}: {ex.GetType().Name}: {ex.Message}");
      }
    }

    int errTotal = 0;
    int errPassed = 0;
    foreach (string rel in Conformance.ConformanceRunner.RuntimeErrCases.Keys
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      errTotal++;
      try {
        Compilation compilation = CompileConformanceFile(rel);
        IrModule module = IrLowerer.Lower(compilation);
        RcInsertPass.Run(module);
        IrVerificationResult verification = IrVerifier.VerifyAfterRc(module);
        if (!verification.IsValid) {
          failures.Add($"{rel}: {string.Join("; ", verification.Errors)}");
        } else {
          errPassed++;
        }
      } catch (Exception ex) {
        failures.Add($"{rel}: {ex.GetType().Name}: {ex.Message}");
      }
    }

    Assert.True(
        failures.Count == 0,
        $"Post-RC sweep had {failures.Count} failure(s) (.out={outPassed}/{outTotal}, .err={errPassed}/{errTotal}):\n"
        + string.Join("\n", failures.Take(60)));
    Assert.True(outTotal > 0);
    Assert.Equal(outTotal, outPassed);
    Assert.True(errTotal > 0);
    Assert.Equal(errTotal, errPassed);
  }

  private const string DesignSection33MainPino = """
    union Constant {
      Bool(b bool)
      Float(f float)
      Int(n int)
      String(str string)
    }

    @@Generic[T]
    struct Person {
      pub name string
      pub data T
    }

    val n int = 12
    val f float = 12.5
    val p Person[string] = Person {
      name: "Shawn"
      data: "China"
    }
    var nullish Option[string] = Option::None

    fn call_lambda(fun fn) {
      fun()
    }

    fn call_lambda_2(fun fn (int)) {
      fun(1)
    }

    fn call_lambda_3(fun fn (int, int) -> int) {
      return fun(1, 2)
    }

    call_lambda_3(fn (n, m) -> n + m)

    var int_map = map[int, string]

    int_map = map[int, string] {
      1 -> "Shawn"
      2 -> "Lee"
    }

    var int_set = set[int]

    int_set = set[int] { 1, 2, 3 }
    """;

  [Fact]
  public void MainPino_PostRcVerifiesCleanly() {
    string? envPath = Environment.GetEnvironmentVariable("PINO_MAIN_PINO");
    string mainPinoPath = !string.IsNullOrWhiteSpace(envPath)
        ? envPath
        : Path.Combine(s_repoRoot, "main.pino");
    Assert.True(File.Exists(mainPinoPath), $"main.pino not found at '{mainPinoPath}'.");

    string content = File.ReadAllText(mainPinoPath);
    if (string.IsNullOrWhiteSpace(envPath)
        && content.Contains("struct Context", StringComparison.Ordinal)
        && content.Contains("fn add_err(", StringComparison.Ordinal)) {
      content = DesignSection33MainPino;
    }

    var parsed = Parser.Parse(new SourceText(content, mainPinoPath));
    Assert.Empty(parsed.Diagnostics.Where(static d => d.Severity != DiagnosticSeverity.Hint));
    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

    IrModule module = IrLowerer.Lower(compilation);
    RcInsertPass.Run(module);
    IrVerificationResult verification = IrVerifier.VerifyAfterRc(module);
    Assert.True(
        verification.IsValid,
        $"main.pino ({mainPinoPath}) failed IrVerifier(PostRc) with {verification.Errors.Length} error(s):\n"
        + string.Join("\n", verification.Errors));
  }

  [Fact]
  public void StringAppendLocalVar_MovesOldSlotWithoutExtraRetainOrRelease() {
    const string Source = """
      fn build_assign(n int) string {
        var s = ""
        var i = 0
        for i < n {
          s = s + "a"
          i += 1
        }
        return s
      }

      fn build_plus_assign(n int) string {
        var s = ""
        var i = 0
        for i < n {
          s += "a"
          i += 1
        }
        return s
      }

      fn self_concat(seed string) string {
        var s = seed
        s = s + s
        return s
      }

      fn mutate_in_rhs(mut x string) string {
        x = "mut"
        return "!"
      }

      fn eval_order_preserved() string {
        var s = "old"
        s = s + mutate_in_rhs(mut s)
        return s
      }

      fn append_rune() string {
        var s = "a"
        s = s + 'b'
        s += 'c'
        return s
      }
      """;

    IrModule module = LowerRunRcAndVerify(Source);

    IrFunction assignFn = Assert.Single(module.Functions, static f => f.Name == "build_assign");
    Assert.Equal(1, CountInstructions<StrAppendSlot>(assignFn));
    Assert.Equal(0, CountInstructions<StrConcat>(assignFn));

    IrFunction plusAssignFn = Assert.Single(module.Functions, static f => f.Name == "build_plus_assign");
    Assert.Equal(1, CountInstructions<StrAppendSlot>(plusAssignFn));
    Assert.Equal(0, CountInstructions<StrConcat>(plusAssignFn));

    IrFunction selfFn = Assert.Single(module.Functions, static f => f.Name == "self_concat");
    Assert.Equal(1, CountInstructions<StrAppendSlot>(selfFn));
    Assert.Equal(0, CountInstructions<StrConcat>(selfFn));

    // When the RHS mutates the target local via `mut s`, lowering must NOT reorder evaluation.
    IrFunction evalOrderFn = Assert.Single(module.Functions, static f => f.Name == "eval_order_preserved");
    Assert.Equal(0, CountInstructions<StrAppendSlot>(evalOrderFn));
    Assert.Equal(1, CountInstructions<StrConcat>(evalOrderFn));

    // Rune appends (`string + rune`, `string += rune`) stay on StrConcatRune.
    IrFunction runeFn = Assert.Single(module.Functions, static f => f.Name == "append_rune");
    Assert.Equal(0, CountInstructions<StrAppendSlot>(runeFn));
    Assert.Equal(2, CountInstructions<StrConcatRune>(runeFn));
  }

  // ---------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------

  private static IrModule LowerRunRcAndVerify(string source) {
    var parsed = Parser.Parse(new SourceText(source, "rc_test.pino"));
    Assert.Empty(parsed.Diagnostics.Where(static d => d.Severity != DiagnosticSeverity.Hint));
    Compilation compilation = Compilation.Create(parsed);
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

    IrModule module = IrLowerer.Lower(compilation);
    IrVerificationResult preRc = IrVerifier.Verify(module);
    Assert.True(preRc.IsValid, "Pre-RC verification failed:\n" + string.Join("\n", preRc.Errors));

    RcInsertPass.Run(module);
    IrVerificationResult postRc = IrVerifier.VerifyAfterRc(module);
    Assert.True(
        postRc.IsValid,
        "Post-RC verification failed:\n" + string.Join("\n", postRc.Errors)
        + "\n\n--- Printed Post-RC IR ---\n" + IrPrinter.Print(module));
    return module;
  }

  private static Compilation CompileConformanceFile(string relativePath) {
    string fullPath = Path.Combine(s_conformanceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    string content = File.ReadAllText(fullPath);
    var parsed = Parser.Parse(new SourceText(content, fullPath));
    Assert.Empty(parsed.Diagnostics.Where(static d => d.Severity != DiagnosticSeverity.Hint));
    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
    return compilation;
  }

  private static int CountInstructions<T>(IrFunction fn) where T : IrInstruction =>
      fn.Blocks.Sum(static b => b.Instructions.Count(static i => i is T));

  private static string ToConformanceRelativePath(string fullPath) =>
      Path.GetRelativePath(s_conformanceRoot, fullPath).Replace('\\', '/');

  private static string NormalizeLf(string text) =>
      text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
