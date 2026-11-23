using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.Parsing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.IR.Lowering;

/// <summary>
/// Unit and golden tests covering ticket <c>061</c> AST-to-PIR lowering:
/// closures &amp; <c>D-056</c> <c>PinoCell</c> captures, <c>STRUCT-14</c> bound methods &amp;
/// <c>D-055</c> indexed calls, interface vtables &amp; CoW <c>EnsureUnique</c>, generic
/// monomorphization (including transitive instantiations and <c>@@Generic[T is Bound]</c>),
/// and inline IR loop lowering for all 19 callback intrinsics.
/// </summary>
public sealed class IrLoweringClosuresInterfacesGenericsTests {
  // ---------------------------------------------------------------------------
  // Closures & D-056 PinoCell captures (FN-8..25, D-056, design §5.5)
  // ---------------------------------------------------------------------------

  [Fact]
  public void D056_MakeCounter_BoxesVarInCell_CopiesValIntoEnv_AndNonCapturingUsesNullEnv() {
    const string Source = """
      fn make_counter(start int) fn () -> int {
        var count = start
        val step = 1
        return fn () {
          count += step
          return count
        }
      }

      fn double_it(x int) int {
        return x * 2
      }

      struct Math {
        static fn triple(x int) int {
          return x * 3
        }
      }

      fn test_closures() int {
        val c = make_counter(10)
        val non_cap = fn (x int) -> x + 1
        val fn_val = double_it
        val static_val = Math::triple
        return c() + non_cap(2) + fn_val(3) + static_val(4)
      }
      """;

    IrModule module = LowerAndVerify(Source);

    // 1. make_counter: `var count` is boxed in PinoCell (CellNew), `val step` is copied by value into PinoEnv.
    IrFunction makeCounter = GetFunction(module, "make_counter");
    IrInstruction[] makeCounterInsts = AllInstructions(makeCounter);

    CellNew cellNew = Assert.Single(makeCounterInsts.OfType<CellNew>());
    IrTypeId cellTypeId = makeCounter.Temps[cellNew.Dest.Index].Type;
    var cellType = Assert.IsType<IrCellType>(module.Types.GetType(cellTypeId));
    Assert.Equal(module.Types.Int, cellType.ElementType);
    Assert.False(cellType.IsCycleCapable);

    EnvNew envNew = Assert.Single(makeCounterInsts.OfType<EnvNew>());
    IrTypeId envTypeId = makeCounter.Temps[envNew.Dest.Index].Type;
    var envType = Assert.IsType<IrEnvType>(module.Types.GetType(envTypeId));
    Assert.Equal(2, envType.Fields.Length);
    Assert.Contains(envType.Fields, f => f.Name == "count" && f.Type == cellTypeId);
    Assert.Contains(envType.Fields, f => f.Name == "step" && f.Type == module.Types.Int);
    Assert.False(envType.IsCycleCapable);

    ClosureMake counterClos = Assert.Single(makeCounterInsts.OfType<ClosureMake>());
    Assert.NotNull(counterClos.Env);

    // Inner lambda reads/writes `count` via CellGet/CellSet and reads `step` via EnvFieldGet.
    IrFunction lambdaFn = module.Functions[counterClos.Function.Index];
    IrInstruction[] lambdaInsts = AllInstructions(lambdaFn);
    Assert.Contains(lambdaInsts, static i => i is EnvFieldGet);
    Assert.Contains(lambdaInsts, static i => i is CellGet);
    Assert.Contains(lambdaInsts, static i => i is CellSet);

    // 2. Non-capturing lambda, top-level fn value, and static method value all emit ClosureMake(..., null) with 0 EnvNew.
    IrFunction testClosures = GetFunction(module, "test_closures");
    IrInstruction[] testInsts = AllInstructions(testClosures);
    Assert.Empty(testInsts.OfType<EnvNew>());

    ClosureMake[] nullEnvClosures = [.. testInsts.OfType<ClosureMake>()];
    Assert.Equal(3, nullEnvClosures.Length);
    Assert.All(nullEnvClosures, static cm => Assert.Null(cm.Env));

    // All 4 indirect calls lower to CallClosure.
    Assert.Equal(4, testInsts.OfType<CallClosure>().Count());
  }

  [Fact]
  public void NestedClosures_ShareOuterBoxedCell_AndTagCycleCapableCells() {
    const string Source = """
      fn make_pair() fn () -> int {
        var acc = 0
        var cb fn () -> int = fn () -> 0
        cb = fn () {
          val inner = fn () {
            acc += 1
            return acc
          }
          return inner()
        }
        return cb
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction makePair = GetFunction(module, "make_pair");
    CellNew[] cellNews = [.. AllInstructions(makePair).OfType<CellNew>()];
    Assert.NotEmpty(cellNews);

    // `acc` is int (not cycle-capable); verify outer allocates CellNew for `acc` and nested closures use CellGet/CellSet.
    Assert.Contains(
        cellNews,
        cn => module.Types.GetType(makePair.Temps[cn.Dest.Index].Type) is IrCellType ct
            && ct.ElementType == module.Types.Int
            && !ct.IsCycleCapable);

    // A closure capturing a `var` of closure type must mark the cell and env as cycle-capable.
    const string CycleSource = """
      fn self_ref() int {
        var f fn () -> int = fn () -> 0
        f = fn () {
          val g = f
          return 1
        }
        return f()
      }
      """;

    IrModule cycleMod = LowerAndVerify(CycleSource);
    IrFunction selfRef = GetFunction(cycleMod, "self_ref");
    CellNew cycleCell = Assert.Single(AllInstructions(selfRef).OfType<CellNew>());
    var cycleCellType = Assert.IsType<IrCellType>(cycleMod.Types.GetType(selfRef.Temps[cycleCell.Dest.Index].Type));
    Assert.True(cycleCellType.IsCycleCapable);

    EnvNew cycleEnv = Assert.Single(AllInstructions(selfRef).OfType<EnvNew>());
    var cycleEnvType = Assert.IsType<IrEnvType>(cycleMod.Types.GetType(selfRef.Temps[cycleEnv.Dest.Index].Type));
    Assert.True(cycleEnvType.IsCycleCapable);
  }

  // ---------------------------------------------------------------------------
  // STRUCT-14 bound methods & D-055 indexed calls
  // ---------------------------------------------------------------------------

  [Fact]
  public void Struct14_BoundMethod_CapturesSelfValueSnapshot_AndD055_IndexedCallLowersToCallClosure() {
    const string Source = """
      struct Adder {
        base int

        fn add(x int) int {
          return base + x
        }
      }

      struct Box {
        op fn (int) -> int
      }

      fn run_calls(i int) int {
        var a = Adder { base: 10 }
        val bound = a:add
        a = Adder { base: 99 }
        val fns = [bound, fn (n int) -> n + 1]
        val b = Box { op: bound }
        return fns[i](9) + b:op(4)
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction runCalls = GetFunction(module, "run_calls");
    IrInstruction[] insts = AllInstructions(runCalls);

    // STRUCT-14: bound method captures a 1-field PinoEnv holding a value snapshot of `Adder` (not a ptr/cell).
    EnvNew boundEnv = Assert.Single(insts.OfType<EnvNew>());
    var envType = Assert.IsType<IrEnvType>(module.Types.GetType(runCalls.Temps[boundEnv.Dest.Index].Type));
    IrEnvField selfField = Assert.Single(envType.Fields);
    Assert.Equal("self", selfField.Name);
    var structType = Assert.IsType<IrStructType>(module.Types.GetType(selfField.Type));
    Assert.Equal("Adder", structType.Name);

    // ClosureMake binds the synthesized trampoline with the snapshot env.
    ClosureMake boundClos = Assert.Single(insts.OfType<ClosureMake>(), cm => cm.Env is not null);
    IrFunction trampoline = module.Functions[boundClos.Function.Index];
    Assert.Contains(AllInstructions(trampoline), static i => i is EnvFieldGet);
    Assert.Contains(
        AllInstructions(trampoline).OfType<CallDirect>(),
        cd => module.Functions[cd.Callee.Index].Name == "Adder::add");

    // D-055 `fns[i](9)` and callable field `b:op(4)` both lower to CallClosure.
    Assert.Contains(insts, static i => i is VecGet);
    Assert.Equal(2, insts.OfType<CallClosure>().Count());
  }

  // ---------------------------------------------------------------------------
  // Interfaces: VTable synthesis, IfaceBox, CallIface, EnsureUnique (IFACE-1..6)
  // ---------------------------------------------------------------------------

  [Fact]
  public void Interface_SynthesizesVTableWithLifecycleAndMembers_AndEmitsEnsureUniqueOnMutCallsAndPropWrites() {
    const string Source = """
      interface Counter {
        mut value int
        fn read() int
        mut fn bump(delta int)
      }

      struct Tally {
        mut value int

        fn read() int {
          return value
        }

        mut fn bump(delta int) {
          value += delta
        }
      }

      fn drive(t Tally) int {
        var c Counter = t
        val kind = type(c)
        c:value = c:value + 1
        c:bump(5)
        val n = c:read()
        return n + kind:len
      }
      """;

    IrModule module = LowerAndVerify(Source);

    // 1. VTable for (Tally, Counter) has drop, clone, eq, to_str, type_name, methods, and properties.
    IrVTable vtable = Assert.Single(module.VTables);
    Assert.Equal("Tally", vtable.TypeName);
    Assert.Equal("Counter", module.Types.GetType(vtable.InterfaceType).Name);
    Assert.Equal("Tally", module.Types.GetType(vtable.ConcreteStructType).Name);
    Assert.Equal(2, vtable.MethodFuncIds.Length);
    Assert.Single(vtable.PropertyFieldIndices);
    Assert.Equal(0, vtable.PropertyFieldIndices[0]);
    Assert.Single(vtable.PropertyGetterFuncIds);
    Assert.Single(vtable.PropertySetterFuncIds);
    Assert.NotNull(vtable.PropertySetterFuncIds[0]);

    // Clone, drop, eq, to_str functions all exist and are valid.
    Assert.NotNull(vtable.DropFuncId);
    Assert.NotNull(vtable.CloneFuncId);
    Assert.NotNull(vtable.EqFuncId);
    Assert.NotNull(vtable.ToStrFuncId);

    // 2. Check drive() IR instructions: IfaceBox, IfaceTypeName, IfaceFieldGet, EnsureUnique + IfaceFieldAddr, EnsureUnique + CallIface(mut), CallIface(non-mut).
    IrFunction drive = GetFunction(module, "drive");
    IrInstruction[] insts = AllInstructions(drive);

    Assert.Single(insts.OfType<IfaceBox>());
    Assert.Contains(insts.OfType<CallRuntime>(), static cr => cr.Op == IrRuntimeOp.IfaceTypeName);
    Assert.Contains(insts, static i => i is IfaceFieldGet);

    int fieldAddrIdx = Array.FindIndex(insts, static i => i is IfaceFieldAddr);
    Assert.True(fieldAddrIdx > 0, "Expected IfaceFieldAddr for interface property write.");
    Assert.Contains(insts[..fieldAddrIdx], static i => i is EnsureUnique);

    int mutCallIdx = Array.FindIndex(insts, static i => i is CallIface { IsMutating: true });
    Assert.True(mutCallIdx > 0, "Expected mutating CallIface for c:bump(5).");
    Assert.Contains(insts[fieldAddrIdx..mutCallIdx], static i => i is EnsureUnique);

    Assert.Contains(insts, static i => i is CallIface { IsMutating: false });
  }

  // ---------------------------------------------------------------------------
  // Generics monomorphization: transitive instantiations & @@Generic[T is Bound]
  // ---------------------------------------------------------------------------

  [Fact]
  public void Generics_MonomorphizesTransitiveInstantiations_AndBoundedDirectDispatchWithZeroCallIface() {
    const string Source = """
      interface Named {
        name string
        fn label() string
      }

      struct Dog {
        name string

        fn label() string {
          return "dog:" + name
        }
      }

      @@Generic[T]
      fn wrap_vec(x T) Option[[]T] {
        return Option::Some([x])
      }

      @@Generic[T]
      fn outer_wrap(x T) Option[[]T] {
        val mut local_copy T = x
        return wrap_vec(local_copy)
      }

      @@Generic[T is Named]
      fn describe(item T) string {
        return item:name + "=" + item:label()
      }

      fn main() {
        val wrapped = outer_wrap(42)
        val d = Dog { name: "Rex" }
        val desc = describe(d)
        println(desc)
        match wrapped {
          when Option::Some(xs) { println(xs:len) }
          when Option::None { println(0) }
        }
      }
      """;

    IrModule module = LowerAndVerify(Source);

    // 1. Transitive monomorphization: outer_wrap[int] -> wrap_vec[int] with `val mut local_copy T` silent on int.
    IrFunction outerInt = Assert.Single(module.Functions, static f => f.Name == "outer_wrap__int");
    IrFunction wrapInt = Assert.Single(module.Functions, static f => f.Name == "wrap_vec__int");
    Assert.Contains(AllInstructions(outerInt).OfType<CallDirect>(), cd => cd.Callee == wrapInt.Id);

    // 2. Bounded generic `describe[Dog]`: direct FieldGet and CallDirect to `Dog::label` with ZERO CallIface / IfaceBox / VTables.
    IrFunction describeDog = Assert.Single(module.Functions, static f => f.Name == "describe__Dog");
    IrInstruction[] descInsts = AllInstructions(describeDog);
    Assert.Contains(descInsts, static i => i is FieldGet);
    Assert.Contains(
        descInsts.OfType<CallDirect>(),
        cd => module.Functions[cd.Callee.Index].Name == "Dog::label");
    Assert.Empty(descInsts.OfType<CallIface>());
    Assert.Empty(descInsts.OfType<IfaceBox>());
    Assert.Empty(module.VTables);
  }

  // ---------------------------------------------------------------------------
  // Inline callback loops for all 19 callback intrinsics (design §5.9)
  // ---------------------------------------------------------------------------

  [Fact]
  public void CallbackIntrinsics_InlineImplicitItAndLambdasWithZeroClosures_AndUseCallClosureForFnVars() {
    const string Source = """
      fn inline_filter_and_map(words []string, nums []int) int {
        val short = words:filter(it:len <= 4)
        val doubled = nums:map(fn (x) -> x * 2)
        val filtered = nums:filter(fn (x) {
          if x > 2 {
            return true
          }
          return false
        })
        [3, 4]:each(println)
        return short:len + doubled:len + filtered:len
      }

      fn dynamic_map(nums []int, f fn (int) -> int) []int {
        return nums:map(f)
      }

      fn all_nineteen_callbacks(nums []int, s set[int]) int {
        var total = 0
        nums:each(fn (x) { total += x })
        nums:each(fn (x, i) { total += x + i })
        val m1 = nums:map(it + 1)
        val m2 = nums:map(fn (x, i) -> x + i)
        val f1 = nums:filter(it > 0)
        val f2 = nums:filter(fn (x, i) -> x > i)
        val fd1 = nums:find(it > 5)
        val fd2 = nums:find(fn (x, i) -> x == i)
        val fi1 = nums:find_index(it > 5)
        val fi2 = nums:find_index(fn (x, i) -> x == i)
        val a1 = nums:any(it > 0)
        val a2 = nums:any(fn (x, i) -> x > i)
        val al1 = nums:all(it > 0)
        val al2 = nums:all(fn (x, i) -> x >= i)
        val c1 = nums:count(it > 0)
        val c2 = nums:count(fn (x, i) -> x > i)
        val sa = s:any(it > 0)
        val sal = s:all(it > 0)
        s:each(fn (x) { total += x })
        if a1 and a2 and al1 and al2 and sa and sal {
          total += 1
        }
        return total + m1:len + m2:len + f1:len + f2:len + c1 + c2
      }
      """;

    IrModule module = LowerAndVerify(Source);

    // 1. Inline filter (implicit `it`), inline map (arrow lambda), block lambda with `return`, and `[3, 4]:each(println)`
    //    must emit ZERO EnvNew, ZERO ClosureMake, and ZERO CallClosure instructions!
    IrFunction inlineFn = GetFunction(module, "inline_filter_and_map");
    IrInstruction[] inlineInsts = AllInstructions(inlineFn);
    Assert.Empty(inlineInsts.OfType<EnvNew>());
    Assert.Empty(inlineInsts.OfType<ClosureMake>());
    Assert.Empty(inlineInsts.OfType<CallClosure>());
    Assert.Contains(inlineInsts, static i => i is VecLen);
    Assert.Contains(inlineInsts, static i => i is VecPush);
    Assert.Contains(inlineInsts.OfType<CallRuntime>(), static cr => cr.Op == IrRuntimeOp.PrintlnN);

    // 2. Function variable `nums:map(f)` lowers to an inline loop calling CallClosure inside the loop body.
    IrFunction dynFn = GetFunction(module, "dynamic_map");
    IrInstruction[] dynInsts = AllInstructions(dynFn);
    Assert.Single(dynInsts.OfType<CallClosure>());
    Assert.Contains(dynInsts, static i => i is VecLen);
    Assert.Contains(dynInsts, static i => i is VecPush);

    // 3. All 19 callback intrinsics with inline lambdas / implicit `it` emit ZERO EnvNew, ClosureMake, CallClosure, or CellNew.
    IrFunction all19 = GetFunction(module, "all_nineteen_callbacks");
    IrInstruction[] all19Insts = AllInstructions(all19);
    Assert.Empty(all19Insts.OfType<EnvNew>());
    Assert.Empty(all19Insts.OfType<ClosureMake>());
    Assert.Empty(all19Insts.OfType<CallClosure>());
    Assert.Empty(all19Insts.OfType<CellNew>());
  }

  // ---------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------

  private static IrModule LowerAndVerify(string source) {
    var parsed = Parser.Parse(new SourceText(source, "test061.pino"));
    Assert.Empty(parsed.Diagnostics.Where(static d => d.Severity != DiagnosticSeverity.Hint));
    Compilation compilation = Compilation.Create(parsed);
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

    IrModule module = IrLowerer.Lower(compilation);
    IrVerificationResult verification = IrVerifier.Verify(module);
    Assert.True(
        verification.IsValid,
        $"IrVerifier reported {verification.Errors.Length} error(s):\n"
        + string.Join("\n", verification.Errors)
        + "\n\n--- Printed IR ---\n"
        + IrPrinter.Print(module));
    return module;
  }

  private static IrFunction GetFunction(IrModule module, string name) =>
      Assert.Single(module.Functions, f => f.Name == name);

  private static IrInstruction[] AllInstructions(IrFunction fn) =>
      [.. fn.Blocks.SelectMany(static b => b.Instructions)];
}
