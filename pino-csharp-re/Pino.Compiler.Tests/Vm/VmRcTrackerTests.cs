using System.Collections.Immutable;

using Pino.Compiler.IR;
using Pino.Vm;

namespace Pino.Compiler.Tests.Vm;

/// <summary>
/// Hand-built bad IR proving that <see cref="VmRcTracker"/> deterministically fails on use-after-free, double-free,
/// negative counts and leaks, and classifies the D-056 closure cycle as a <c>CYCLE-LEAK</c> (design §4.3).
/// </summary>
public sealed class VmRcTrackerTests {
  private static (IrModule Module, IrFunction Entry, IrBasicBlock Block, IrTempId Vec) NewModuleWithVector() {
    var types = new IrTypeTable();
    var module = new IrModule(types);
    IrFunction entry = module.AddFunction("__pino_entry", [], types.Void);
    module.EntryFuncId = entry.Id;
    IrBasicBlock block = entry.AddBlock();
    IrTypeId vecType = types.GetVector(types.Int);
    IrTempId vec = entry.AddTemp(vecType);
    block.Instructions.Add(new VecNew(vec, ImmutableArray<IrOperand>.Empty, AllocRegion.Heap));
    return (module, entry, block, vec);
  }

  private static VmRun Execute(IrModule module) => VmTestHarness.Execute(module, verifyRc: true, string.Empty, source: null);

  [Fact]
  public void UseAfterFree_OnRcInc_FailsImmediately() {
    (IrModule module, _, IrBasicBlock block, IrTempId vec) = NewModuleWithVector();
    block.Instructions.Add(new RcDec(vec));
    block.Instructions.Add(new RcInc(vec));
    block.Terminator = new RetVoid();

    VmRun run = Execute(module);

    Assert.Equal(PinoVmStopReason.RcViolation, run.Result.Reason);
    Assert.Equal(VmRcViolationKind.UseAfterFree, run.Result.RcErrorKind);
    Assert.Contains("use-after-free", run.Result.RcError, StringComparison.Ordinal);
    Assert.Contains("rc_inc", run.Result.RcError, StringComparison.Ordinal);
  }

  [Fact]
  public void UseAfterFree_OnObjectAccess_FailsImmediately() {
    (IrModule module, IrFunction entry, IrBasicBlock block, IrTempId vec) = NewModuleWithVector();
    block.Instructions.Add(new RcDec(vec));
    block.Instructions.Add(new VecLen(entry.AddTemp(module.Types.Int), vec));
    block.Terminator = new RetVoid();

    VmRun run = Execute(module);

    Assert.Equal(VmRcViolationKind.UseAfterFree, run.Result.RcErrorKind);
    Assert.Contains("freed", run.Result.RcError, StringComparison.Ordinal);
  }

  [Fact]
  public void DoubleFree_OnSecondRcDec_FailsImmediately() {
    (IrModule module, _, IrBasicBlock block, IrTempId vec) = NewModuleWithVector();
    block.Instructions.Add(new RcDec(vec));
    block.Instructions.Add(new RcDec(vec));
    block.Terminator = new RetVoid();

    VmRun run = Execute(module);

    Assert.Equal(VmRcViolationKind.DoubleFree, run.Result.RcErrorKind);
    Assert.Contains("double-free", run.Result.RcError, StringComparison.Ordinal);
  }

  [Fact]
  public void NegativeCount_IsReportedByTheTracker() {
    var tracker = new VmRcTracker(types: null, verifyRc: true);
    var vec = new PinoVmVec();
    tracker.Register(vec, IrTypeId.Invalid, default, "test");
    vec.ShadowRc = -5;

    var dec = Assert.Throws<PinoVmRcException>(() => tracker.Dec(vec));
    var inc = Assert.Throws<PinoVmRcException>(() => tracker.Inc(vec));

    Assert.Equal(VmRcViolationKind.NegativeCount, dec.Kind);
    Assert.Equal(VmRcViolationKind.NegativeCount, inc.Kind);
  }

  [Fact]
  public void ImmortalAndArenaObjects_IgnoreIncAndDec() {
    var tracker = new VmRcTracker(types: null, verifyRc: true);
    PinoVmVec immortal = tracker.MakeImmortal(new PinoVmVec(), IrTypeId.Invalid);
    PinoVmVec arena = tracker.Register(new PinoVmVec(), IrTypeId.Invalid, default, "test", AllocRegionKind.Arena);

    tracker.Inc(immortal);
    Assert.False(tracker.Dec(immortal));
    tracker.Inc(arena);
    Assert.False(tracker.Dec(arena));

    Assert.Equal(PinoVmHeapObject.ImmortalRc, immortal.ShadowRc);
    Assert.Equal(0, arena.ShadowRc);
    Assert.True(tracker.ReportLeaks().IsClean);
    Assert.Empty(tracker.ReportLeaks().CycleLeaks);
  }

  [Fact]
  public void LeakedObjectAtExit_IsReportedWithAllocationDetails() {
    (IrModule module, _, IrBasicBlock block, _) = NewModuleWithVector();
    block.Terminator = new RetVoid();

    VmRun run = Execute(module);

    Assert.Null(run.Result.RcError);
    VmRcLeakReport report = Assert.IsType<VmRcLeakReport>(run.Result.LeakReport);
    Assert.False(report.IsClean);
    VmRcLeak leak = Assert.Single(report.Leaks);
    Assert.Equal("vector", leak.KindName);
    Assert.Equal("[]int", leak.TypeName);
    Assert.Equal(1, leak.ShadowRc);
    Assert.Equal("__pino_entry", leak.AllocFunction);
    Assert.Empty(report.CycleLeaks);
    Assert.Contains("LEAK: vector []int", report.Format(), StringComparison.Ordinal);
  }

  [Fact]
  public void BalancedProgram_HasNoLeaksAndNoErrors() {
    (IrModule module, _, IrBasicBlock block, IrTempId vec) = NewModuleWithVector();
    block.Instructions.Add(new RcDec(vec));
    block.Terminator = new RetVoid();

    VmRun run = Execute(module);

    Assert.Equal(0, run.Result.ExitCode);
    Assert.True(run.Result.LeakReport!.IsClean);
    Assert.False(run.Result.HasRcErrors);
    Assert.Equal(0, run.Vm.RcTracker.LiveObjectCount);
  }

  [Fact]
  public void RcChecking_CanBeSwitchedOff_WhileKeepingCounts() {
    (IrModule module, _, IrBasicBlock block, IrTempId vec) = NewModuleWithVector();
    block.Instructions.Add(new RcDec(vec));
    block.Instructions.Add(new RcDec(vec));
    block.Terminator = new RetVoid();

    VmRun run = VmTestHarness.Execute(module, verifyRc: false, string.Empty, source: null);

    Assert.Equal(0, run.Result.ExitCode);
    Assert.Null(run.Result.RcError);
    Assert.Null(run.Result.LeakReport);
  }

  [Fact]
  public void SelfRecursiveLambda_IsClassifiedAsCycleLeak_NotAsALeak() {
    const string Source = """
        fn run() {
          var fact = fn (n int) -> n
          fact = fn (n int) -> if n <= 1 then 1 else n * fact(n - 1)
          println(fact(5))
        }

        run()
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal("120", VmTestHarness.Normalize(run.Stdout));
    Assert.Equal(0, run.Result.ExitCode);
    VmRcLeakReport report = Assert.IsType<VmRcLeakReport>(run.Result.LeakReport);
    Assert.True(report.IsClean, report.Format());
    Assert.NotEmpty(report.CycleLeaks);
    Assert.All(report.CycleLeaks, static l => Assert.True(l.IsCycleLeak));
    Assert.Contains("CYCLE-LEAK", report.Format(), StringComparison.Ordinal);
    Assert.Contains(report.CycleLeaks, static l => l.KindName == "cell");
    Assert.Contains(report.CycleLeaks, static l => l.KindName == "env");
  }

  [Fact]
  public void NonCapturingClosure_AndCellWithoutCycle_AreFreed() {
    const string Source = """
        var count = 0
        val bump = fn () {
          count += 1
        }
        bump()
        bump()
        println(count)
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal("2", VmTestHarness.Normalize(run.Stdout));
    Assert.True(run.Result.LeakReport!.IsClean, run.Result.LeakReport.Format());
    Assert.Empty(run.Result.LeakReport.CycleLeaks);
  }

  [Fact]
  public void CopyOnWrite_CloneHoldsItsOwnReferences_AndNothingLeaks() {
    const string Source = """
        val a = ["x", "y"]
        var b = a
        b:push("z")
        println(a)
        println(b)
        """;

    VmRun run = VmTestHarness.Run(Source);

    Assert.Equal("[\"x\", \"y\"]\n[\"x\", \"y\", \"z\"]", VmTestHarness.Normalize(run.Stdout));
    Assert.True(run.Result.LeakReport!.IsClean, run.Result.LeakReport.Format());
  }
}
