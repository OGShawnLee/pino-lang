using System.Globalization;
using System.Text;

using Pino.Compiler.IR;
using Pino.Compiler.Text;

namespace Pino.Vm;

/// <summary>Settings for one <see cref="PinoVm"/> run.</summary>
public sealed class PinoVmOptions {
  /// <summary>
  /// Fail immediately on use-after-free, double-free and negative counts, and report leaks after
  /// <c>__pino_shutdown()</c> (design §4.3). Shadow counts are maintained either way because copy-on-write needs them.
  /// </summary>
  public bool VerifyRc { get; init; } = true;

  /// <summary>Maximum number of backward branches before the run is stopped; <c>0</c> means no limit.</summary>
  public long MaxSteps { get; init; }

  /// <summary>Checked on every backward branch (the hook for fuzzing and the future WASM playground).</summary>
  public CancellationToken CancellationToken { get; init; }

  /// <summary>The root source file, used to print panic locations.</summary>
  public SourceText? RootSource { get; init; }

  /// <summary>Optional lookup of the source file of an imported module's function, for panic locations.</summary>
  public Func<IrFunction, SourceText?>? SourceResolver { get; init; }
}

public enum PinoVmStopReason {
  Completed,
  Exited,
  Panicked,
  RcViolation,
  StepLimit,
}

/// <summary>The outcome of <see cref="PinoVm.Run"/>.</summary>
public sealed class PinoVmResult {
  /// <summary><c>0</c> on success, <c>1</c> when <c>main</c> failed, <c>101</c> after a panic (<c>ERR-5</c>).</summary>
  public int ExitCode { get; init; }

  public PinoVmStopReason Reason { get; init; }

  public string? PanicMessage { get; init; }

  public bool PanicIsAssert { get; init; }

  public string? PanicFile { get; init; }

  public int PanicLine { get; init; }

  /// <summary>The first shadow-RC violation (use-after-free, double-free, negative count), if one stopped the run.</summary>
  public string? RcError { get; init; }

  public VmRcViolationKind? RcErrorKind { get; init; }

  /// <summary>The exit-time leak report; <see langword="null"/> when RC checking is off or the run did not finish cleanly.</summary>
  public VmRcLeakReport? LeakReport { get; init; }

  public long Steps { get; init; }

  public bool HasRcErrors => RcError is not null || (LeakReport is { IsClean: false });
}

/// <summary>A runtime panic (<c>ERR-3</c>); caught by <see cref="PinoVm.Run"/> and printed to <see cref="IVmHost.Err"/>.</summary>
public sealed class PinoVmPanic(string message, bool isAssert = false) : Exception(message) {
  public bool IsAssert { get; } = isAssert;
}

internal sealed class PinoVmExit(int code) : Exception("exit") {
  public int Code { get; } = code;
}

public sealed class PinoVmStepLimitException(long steps)
    : Exception($"The program was stopped after {steps} loop iterations because it reached the step limit.");

/// <summary>An impossible VM state (a bug in the IR or the VM, never a Pino program error).</summary>
public sealed class PinoVmInternalException(string message) : Exception(message);

internal enum OpKind : byte {
  ConstInt, ConstFloat, ConstBool, ConstRune, ConstString, ConstEnum, Copy, Move,
  LoadSlot, StoreSlot, AddrLocal, AddrGlobal, FieldGet, FieldAddr, LoadPtr, StorePtr, PtrEq,
  Add, Sub, Mul, DivF64, FloorDiv, FloorMod, Neg, Not, Cmp,
  CastI64ToF64, CastF64ToI64, CastI64ToRune, CastRuneToI64, StructEq,
  StructNew, TupleNew, TupleField, UnionNew, UnionTag, UnionField,
  IfaceBox, IfaceFieldGet, IfaceFieldAddr,
  CellNew, CellGet, CellSet, CellAddr, EnvNew, EnvFieldGet, ClosureMake,
  VecNew, VecWithCap, VecLen, VecCap, VecGet, VecElemAddr, VecPush, VecPop, VecSwap, VecContains,
  MapNew, MapLen, MapGet, MapElemAddr, MapSet, MapRemove, MapContains, MapKeys, MapValues,
  MapSlotLen, MapSlotIsLive, MapSlotKey, MapSlotValue,
  SetNew, SetLen, SetAdd, SetRemove, SetContains, SetSlotLen, SetSlotIsLive, SetSlotElem,
  PoolNew, PoolLen, PoolAdd, PoolRemove, PoolGet, PoolGetOrPanic, PoolElemAddr, PoolHandles, PoolClear,
  PoolContains, PoolSlotLen, PoolSlotIsLive, PoolSlotHandle, PoolSlotValue,
  StrLen, StrGetRune, StrByteLen, StrDecodeRune, StrNextBytePos, StrEq, StrCmp,
  StrConcat, StrAppendSlot, StrAppendPtr, StrConcatN, StrConcatRune, RuneConcatStr,
  CallDirect, CallClosure, CallIface, CallRuntime,
  RcInc, RcDec, EnsureUnique, Reset, Reuse, ArenaEnter, ArenaExit,
  Br, BrIf, Switch, Ret, RetVoid, Panic, Unreachable,
}

internal sealed class VmOp {
  public OpKind Kind;
  public IrInstruction? Instr;
  public IrTerminator? Term;
  public int Target0 = -1;
  public int Target1 = -1;
  public int[]? CaseValues;
  public int[]? CaseTargets;
  public int DefaultTarget = -1;
}

/// <summary>A function flattened into one contiguous op array with integer branch targets (design §6.1).</summary>
internal sealed class CompiledFunc {
  public CompiledFunc(IrFunction fn, VmOp[] ops, SourceSpan[] spans) {
    Fn = fn;
    Ops = ops;
    Spans = spans;
    ParamCount = fn.Parameters.Length;
    SlotBase = ParamCount + fn.Temps.Count;
    FrameSize = SlotBase + fn.Slots.Count;
  }

  public IrFunction Fn { get; }

  public VmOp[] Ops { get; }

  /// <summary>Parallel to <see cref="Ops"/>; only read when printing a panic location.</summary>
  public SourceSpan[] Spans { get; }

  public int ParamCount { get; }

  public int SlotBase { get; }

  public int FrameSize { get; }

  public IrTypeId OperandType(IrOperand op) =>
      op.IsParam ? Fn.Parameters[op.Index].Type : Fn.Temps[op.Index].Type;

  public int RegOf(IrOperand op) => op.IsParam ? op.Index : ParamCount + op.Index;
}

internal struct Frame {
  public CompiledFunc Func;
  public PinoValue[] Regs;
  public int Pc;

  /// <summary>Register of the caller that receives the return value, or <c>-1</c>.</summary>
  public int RetReg;
}

/// <summary>
/// The flat-array register interpreter for the Pino IR (<c>IQ-8</c>, design §6): every function is flattened into one
/// <see cref="VmOp"/> array, registers are dense frame slots, values are the unboxed <see cref="PinoValue"/>, and
/// <c>ptr&lt;T&gt;</c> is a <see cref="PlaceRef"/>. All I/O goes through <see cref="IVmHost"/>.
/// </summary>
public sealed partial class PinoVm {
  private static readonly PinoValue[] s_noFields = [];

  private readonly IrModule _module;
  private readonly IrTypeTable _types;
  private readonly IVmHost _host;
  private readonly PinoVmOptions _options;
  private readonly VmRcTracker _rc;
  private CompiledFunc?[] _funcs;
  private PinoValue[] _globals;
  private PinoVmString?[] _stringLiterals;
  private SourceText? _activeRootSource;
  private Frame[] _frames = new Frame[32];
  private PinoValue[]?[] _frameRegs = new PinoValue[]?[32];
  private int _frameCount;
  private readonly Stack<PinoVmHeapObject> _dropStack = new();
  private bool _dropping;
  private long _steps;

  public PinoVm(IrModule module, IVmHost host, PinoVmOptions? options = null) {
    ArgumentNullException.ThrowIfNull(module);
    ArgumentNullException.ThrowIfNull(host);
    _module = module;
    _types = module.Types;
    _host = host;
    _options = options ?? new PinoVmOptions();
    _rc = new VmRcTracker(_types, _options.VerifyRc);
    _funcs = new CompiledFunc?[module.Functions.Count];
    _globals = new PinoValue[module.Globals.Count];
    _stringLiterals = new PinoVmString?[module.StringPool.Count];
  }

  public VmRcTracker RcTracker => _rc;

  /// <summary>The module's global variable slots (read by tests after a run).</summary>
  public IReadOnlyList<PinoValue> Globals => _globals;

  /// <summary>
  /// Runs the module's initializers and entry function, then (when <see cref="PinoVmOptions.VerifyRc"/> is on and the run
  /// ended cleanly) checks that no non-immortal object is still alive after <c>__pino_shutdown()</c>.
  /// </summary>
  public PinoVmResult Run() {
    try {
      // The lowered `__pino_entry` already calls every `__pino_init_<Mod>()` (design §5.8); only run an initializer here
      // when the entry function does not, so a global is never initialized twice.
      HashSet<IrFuncId> calledByEntry = CollectDirectCallees(_module.EntryFuncId);
      foreach (IrFuncId init in _module.InitFuncIds) {
        if (!calledByEntry.Contains(init)) {
          CallFunction(init, []);
        }
      }

      if (_module.EntryFuncId is { } entry) {
        CallFunction(entry, []);
      }

      return Finish(0, PinoVmStopReason.Completed);
    } catch (PinoVmExit exit) {
      ClearFrames();
      return Finish(exit.Code, PinoVmStopReason.Exited);
    } catch (PinoVmPanic panic) {
      (string? file, int line) panicLoc = _frameCount > 0 ? LocationFileLine(_frameCount - 1) : (null, 0);
      _host.Err.Write(FormatPanicReport(panic.Message));
      _host.Err.Flush();
      ClearFrames();
      return new PinoVmResult {
        ExitCode = 101,
        Reason = PinoVmStopReason.Panicked,
        PanicMessage = panic.Message,
        PanicIsAssert = panic.IsAssert,
        PanicFile = panicLoc.file,
        PanicLine = panicLoc.line,
        Steps = _steps,
      };
    } catch (PinoVmRcException rcError) {
      ClearFrames();
      return new PinoVmResult {
        ExitCode = 1,
        Reason = PinoVmStopReason.RcViolation,
        RcError = rcError.Message,
        RcErrorKind = rcError.Kind,
        Steps = _steps,
      };
    } catch (PinoVmStepLimitException limit) {
      ClearFrames();
      _host.Err.Write(limit.Message + "\n");
      _host.Err.Flush();
      return new PinoVmResult { ExitCode = 1, Reason = PinoVmStopReason.StepLimit, Steps = _steps };
    } catch {
      ClearFrames();
      throw;
    }
  }

  private HashSet<IrFuncId> CollectDirectCallees(IrFuncId? function) {
    var callees = new HashSet<IrFuncId>();
    if (function is not { } id || (uint) id.Index >= (uint) _module.Functions.Count) {
      return callees;
    }

    foreach (IrBasicBlock block in _module.Functions[id.Index].Blocks) {
      foreach (IrInstruction instruction in block.Instructions) {
        if (instruction is CallDirect call) {
          callees.Add(call.Callee);
        }
      }
    }

    return callees;
  }

  private PinoVmResult Finish(int exitCode, PinoVmStopReason reason) {
    _host.Out.Flush();
    VmRcLeakReport? report = _options.VerifyRc ? _rc.ReportLeaks() : null;
    return new PinoVmResult { ExitCode = exitCode, Reason = reason, LeakReport = report, Steps = _steps };
  }

  /// <summary>
  /// Grows the VM's function, global, and string-literal tables to match an incrementally extended
  /// <see cref="IrModule"/> while preserving all live global values and compiled functions.
  /// </summary>
  public void SyncWithModule() {
    if (_module.Functions.Count > _funcs.Length) {
      Array.Resize(ref _funcs, _module.Functions.Count);
    }

    if (_module.Globals.Count > _globals.Length) {
      Array.Resize(ref _globals, _module.Globals.Count);
    }

    if (_module.StringPool.Count > _stringLiterals.Length) {
      Array.Resize(ref _stringLiterals, _module.StringPool.Count);
    }

    if (_module.ShutdownFuncId is { } sid && (uint) sid.Index < (uint) _funcs.Length) {
      _funcs[sid.Index] = null;
    }
  }

  /// <summary>
  /// Runs a single function on the existing VM heap and global table without running shutdown or
  /// leak checks (used by incremental REPL sessions).
  /// </summary>
  public PinoVmResult RunFunction(IrFuncId funcId, SourceText? source = null) {
    SyncWithModule();
    SourceText? previousSource = _activeRootSource;
    _activeRootSource = source;
    _steps = 0;

    try {
      CallFunction(funcId, []);
      _host.Out.Flush();
      return new PinoVmResult { ExitCode = 0, Reason = PinoVmStopReason.Completed, Steps = _steps };
    } catch (PinoVmExit exit) {
      ClearFrames();
      _host.Out.Flush();
      return new PinoVmResult { ExitCode = exit.Code, Reason = PinoVmStopReason.Exited, Steps = _steps };
    } catch (PinoVmPanic panic) {
      (string? file, int line) panicLoc = _frameCount > 0 ? LocationFileLine(_frameCount - 1) : (null, 0);
      string report = FormatPanicReport(panic.Message);
      ClearFrames();
      _host.Err.Write(report);
      _host.Err.Flush();
      return new PinoVmResult {
        ExitCode = 101,
        Reason = PinoVmStopReason.Panicked,
        PanicMessage = panic.Message,
        PanicIsAssert = panic.IsAssert,
        PanicFile = panicLoc.file,
        PanicLine = panicLoc.line,
        Steps = _steps,
      };
    } catch (PinoVmRcException rcError) {
      ClearFrames();
      return new PinoVmResult {
        ExitCode = 1,
        Reason = PinoVmStopReason.RcViolation,
        RcError = rcError.Message,
        RcErrorKind = rcError.Kind,
        Steps = _steps,
      };
    } catch (PinoVmStepLimitException limit) {
      ClearFrames();
      _host.Err.Write(limit.Message + "\n");
      _host.Err.Flush();
      return new PinoVmResult { ExitCode = 1, Reason = PinoVmStopReason.StepLimit, Steps = _steps };
    } finally {
      ClearFrames();
      _activeRootSource = previousSource;
    }
  }

  /// <summary>
  /// Runs module setup (<see cref="IrModule.TestSetupFuncId"/>), then one test body, then
  /// <see cref="IrModule.ShutdownFuncId"/>, and returns the per-test outcome and leak report.
  /// Call on a fresh <see cref="PinoVm"/> instance per test for full isolation (<c>TEST-4</c>).
  /// </summary>
  public PinoVmResult RunTest(IrFuncId testFuncId) {
    try {
      if (_module.TestSetupFuncId is { } setupId) {
        CallFunction(setupId, []);
      } else {
        foreach (IrFuncId init in _module.InitFuncIds) {
          CallFunction(init, []);
        }
      }

      CallFunction(testFuncId, []);

      if (_module.ShutdownFuncId is { } shutdownId && (uint) shutdownId.Index < (uint) _module.Functions.Count) {
        CallFunction(shutdownId, []);
      }

      Array.Clear(_globals);
      return Finish(0, PinoVmStopReason.Completed);
    } catch (PinoVmExit exit) {
      ClearFrames();
      if (exit.Code == 0 && _module.ShutdownFuncId is { } shutdownId && (uint) shutdownId.Index < (uint) _module.Functions.Count) {
        CallFunction(shutdownId, []);
        Array.Clear(_globals);
      }

      return Finish(exit.Code, PinoVmStopReason.Exited);
    } catch (PinoVmPanic panic) {
      (string? file, int line) panicLoc = _frameCount > 0 ? LocationFileLine(_frameCount - 1) : (null, 0);
      ClearFrames();
      _host.Out.Flush();
      return new PinoVmResult {
        ExitCode = 101,
        Reason = PinoVmStopReason.Panicked,
        PanicMessage = panic.Message,
        PanicIsAssert = panic.IsAssert,
        PanicFile = panicLoc.file,
        PanicLine = panicLoc.line,
        Steps = _steps,
      };
    } catch (PinoVmRcException rcError) {
      ClearFrames();
      return new PinoVmResult {
        ExitCode = 1,
        Reason = PinoVmStopReason.RcViolation,
        RcError = rcError.Message,
        RcErrorKind = rcError.Kind,
        Steps = _steps,
      };
    } catch (PinoVmStepLimitException limit) {
      ClearFrames();
      _host.Err.Write(limit.Message + "\n");
      _host.Err.Flush();
      return new PinoVmResult { ExitCode = 1, Reason = PinoVmStopReason.StepLimit, Steps = _steps };
    } catch {
      ClearFrames();
      throw;
    }
  }

  /// <summary>
  /// Runs <c>__pino_shutdown()</c> to release all live RC-managed globals and returns the leak report.
  /// </summary>
  public PinoVmResult Shutdown() {
    SyncWithModule();
    try {
      if (_module.ShutdownFuncId is { } shutdownId && (uint) shutdownId.Index < (uint) _module.Functions.Count) {
        CallFunction(shutdownId, []);
      }

      Array.Clear(_globals);
      return Finish(0, PinoVmStopReason.Completed);
    } catch (PinoVmRcException rcError) {
      ClearFrames();
      return new PinoVmResult {
        ExitCode = 1,
        Reason = PinoVmStopReason.RcViolation,
        RcError = rcError.Message,
        RcErrorKind = rcError.Kind,
        Steps = _steps,
      };
    }
  }

  internal void DropGlobalsFrom(int startIndex) {
    for (int i = startIndex; i < _globals.Length && i < _module.Globals.Count; i++) {
      PinoValue val = _globals[i];
      if (val.Raw != 0 || val.Obj is not null) {
        DecValue(val, _module.Globals[i].Type);
        _globals[i] = default;
      }
    }
  }

  // ---------------------------------------------------------------------------
  // Loading: flatten blocks into one op array per function
  // ---------------------------------------------------------------------------

  private CompiledFunc Func(IrFuncId id) {
    CompiledFunc? compiled = _funcs[id.Index];
    if (compiled is not null) {
      return compiled;
    }

    IrFunction fn = _module.Functions[id.Index];
    var ops = new List<VmOp>();
    var spans = new List<SourceSpan>();
    var blockPc = new Dictionary<int, int>();
    foreach (IrBasicBlock block in fn.Blocks) {
      blockPc[block.Id.Index] = ops.Count;
      foreach (IrInstruction instr in block.Instructions) {
        ops.Add(new VmOp { Kind = KindOf(instr), Instr = instr });
        spans.Add(instr.Span);
      }

      IrTerminator term = block.Terminator
          ?? throw new PinoVmInternalException($"Block {block.Id} of '{fn.Name}' has no terminator.");
      var termOp = new VmOp { Kind = KindOf(term), Term = term };
      switch (term) {
        case Br br:
          termOp.Target0 = br.Target.Index;
          break;
        case BrIf brIf:
          termOp.Target0 = brIf.TrueTarget.Index;
          termOp.Target1 = brIf.FalseTarget.Index;
          break;
        case Switch sw:
          termOp.CaseValues = [.. sw.Cases.Select(static c => c.CaseValue)];
          termOp.CaseTargets = [.. sw.Cases.Select(static c => c.Target.Index)];
          termOp.DefaultTarget = sw.DefaultTarget?.Index ?? -1;
          break;
      }

      ops.Add(termOp);
      spans.Add(term.Span);
    }

    foreach (VmOp op in ops) {
      if (op.Term is null) {
        continue;
      }

      if (op.Target0 >= 0) {
        op.Target0 = blockPc[op.Target0];
      }

      if (op.Target1 >= 0) {
        op.Target1 = blockPc[op.Target1];
      }

      if (op.CaseTargets is { } targets) {
        for (int i = 0; i < targets.Length; i++) {
          targets[i] = blockPc[targets[i]];
        }
      }

      if (op.DefaultTarget >= 0) {
        op.DefaultTarget = blockPc[op.DefaultTarget];
      }
    }

    compiled = new CompiledFunc(fn, [.. ops], [.. spans]);
    _funcs[id.Index] = compiled;
    return compiled;
  }

  private static OpKind KindOf(IrInstruction instr) =>
      instr switch {
        ConstInt => OpKind.ConstInt,
        ConstFloat => OpKind.ConstFloat,
        ConstBool => OpKind.ConstBool,
        ConstRune => OpKind.ConstRune,
        ConstString => OpKind.ConstString,
        ConstEnum => OpKind.ConstEnum,
        Copy => OpKind.Copy,
        Move => OpKind.Move,
        LoadSlot => OpKind.LoadSlot,
        StoreSlot => OpKind.StoreSlot,
        AddrLocal => OpKind.AddrLocal,
        AddrGlobal => OpKind.AddrGlobal,
        FieldGet => OpKind.FieldGet,
        FieldAddr => OpKind.FieldAddr,
        LoadPtr => OpKind.LoadPtr,
        StorePtr => OpKind.StorePtr,
        PtrEq => OpKind.PtrEq,
        Add => OpKind.Add,
        Sub => OpKind.Sub,
        Mul => OpKind.Mul,
        DivF64 => OpKind.DivF64,
        FloorDiv => OpKind.FloorDiv,
        FloorMod => OpKind.FloorMod,
        Neg => OpKind.Neg,
        Not => OpKind.Not,
        Cmp => OpKind.Cmp,
        CastI64ToF64 => OpKind.CastI64ToF64,
        CastF64ToI64 => OpKind.CastF64ToI64,
        CastI64ToRune => OpKind.CastI64ToRune,
        CastRuneToI64 => OpKind.CastRuneToI64,
        StructEq => OpKind.StructEq,
        StructNew => OpKind.StructNew,
        TupleNew => OpKind.TupleNew,
        TupleField => OpKind.TupleField,
        UnionNew => OpKind.UnionNew,
        UnionTag => OpKind.UnionTag,
        UnionField => OpKind.UnionField,
        IfaceBox => OpKind.IfaceBox,
        IfaceFieldGet => OpKind.IfaceFieldGet,
        IfaceFieldAddr => OpKind.IfaceFieldAddr,
        CellNew => OpKind.CellNew,
        CellGet => OpKind.CellGet,
        CellSet => OpKind.CellSet,
        CellAddr => OpKind.CellAddr,
        EnvNew => OpKind.EnvNew,
        EnvFieldGet => OpKind.EnvFieldGet,
        ClosureMake => OpKind.ClosureMake,
        VecNew => OpKind.VecNew,
        VecWithCap => OpKind.VecWithCap,
        VecLen => OpKind.VecLen,
        VecCap => OpKind.VecCap,
        VecGet => OpKind.VecGet,
        VecElemAddr => OpKind.VecElemAddr,
        VecPush => OpKind.VecPush,
        VecPop => OpKind.VecPop,
        VecSwap => OpKind.VecSwap,
        VecContains => OpKind.VecContains,
        MapNew => OpKind.MapNew,
        MapLen => OpKind.MapLen,
        MapGet => OpKind.MapGet,
        MapElemAddr => OpKind.MapElemAddr,
        MapSet => OpKind.MapSet,
        MapRemove => OpKind.MapRemove,
        MapContains => OpKind.MapContains,
        MapKeys => OpKind.MapKeys,
        MapValues => OpKind.MapValues,
        MapSlotLen => OpKind.MapSlotLen,
        MapSlotIsLive => OpKind.MapSlotIsLive,
        MapSlotKey => OpKind.MapSlotKey,
        MapSlotValue => OpKind.MapSlotValue,
        SetNew => OpKind.SetNew,
        SetLen => OpKind.SetLen,
        SetAdd => OpKind.SetAdd,
        SetRemove => OpKind.SetRemove,
        SetContains => OpKind.SetContains,
        SetSlotLen => OpKind.SetSlotLen,
        SetSlotIsLive => OpKind.SetSlotIsLive,
        SetSlotElem => OpKind.SetSlotElem,
        PoolNew => OpKind.PoolNew,
        PoolLen => OpKind.PoolLen,
        PoolAdd => OpKind.PoolAdd,
        PoolRemove => OpKind.PoolRemove,
        PoolGet => OpKind.PoolGet,
        PoolGetOrPanic => OpKind.PoolGetOrPanic,
        PoolElemAddr => OpKind.PoolElemAddr,
        PoolHandles => OpKind.PoolHandles,
        PoolClear => OpKind.PoolClear,
        PoolContains => OpKind.PoolContains,
        PoolSlotLen => OpKind.PoolSlotLen,
        PoolSlotIsLive => OpKind.PoolSlotIsLive,
        PoolSlotHandle => OpKind.PoolSlotHandle,
        PoolSlotValue => OpKind.PoolSlotValue,
        StrLen => OpKind.StrLen,
        StrGetRune => OpKind.StrGetRune,
        StrByteLen => OpKind.StrByteLen,
        StrDecodeRune => OpKind.StrDecodeRune,
        StrNextBytePos => OpKind.StrNextBytePos,
        StrEq => OpKind.StrEq,
        StrCmp => OpKind.StrCmp,
        StrConcat => OpKind.StrConcat,
        StrAppendSlot => OpKind.StrAppendSlot,
        StrAppendPtr => OpKind.StrAppendPtr,
        StrConcatN => OpKind.StrConcatN,
        StrConcatRune => OpKind.StrConcatRune,
        RuneConcatStr => OpKind.RuneConcatStr,
        CallDirect => OpKind.CallDirect,
        CallClosure => OpKind.CallClosure,
        CallIface => OpKind.CallIface,
        CallRuntime => OpKind.CallRuntime,
        RcInc => OpKind.RcInc,
        RcDec => OpKind.RcDec,
        EnsureUnique => OpKind.EnsureUnique,
        Reset => OpKind.Reset,
        Reuse => OpKind.Reuse,
        ArenaEnter => OpKind.ArenaEnter,
        ArenaExit => OpKind.ArenaExit,
        _ => throw new PinoVmInternalException($"The VM has no implementation for IR instruction '{instr.GetType().Name}'."),
      };

  private static OpKind KindOf(IrTerminator term) =>
      term switch {
        Br => OpKind.Br,
        BrIf => OpKind.BrIf,
        Switch => OpKind.Switch,
        Ret => OpKind.Ret,
        RetVoid => OpKind.RetVoid,
        Panic => OpKind.Panic,
        Unreachable => OpKind.Unreachable,
        _ => throw new PinoVmInternalException($"The VM has no implementation for IR terminator '{term.GetType().Name}'."),
      };

  // ---------------------------------------------------------------------------
  // Calls
  // ---------------------------------------------------------------------------

  /// <summary>Runs <paramref name="callee"/> to completion (re-entrant) and returns its result.</summary>
  private PinoValue CallFunction(IrFuncId callee, ReadOnlySpan<PinoValue> args) {
    CompiledFunc func = Func(callee);
    int depth = _frameCount;
    PinoValue[] regs = RentRegs(depth, func.FrameSize);
    args.CopyTo(regs);
    _frames[depth] = new Frame { Func = func, Regs = regs, Pc = 0, RetReg = -1 };
    _frameCount = depth + 1;
    return RunLoop(depth);
  }

  private PinoValue RunLoop(int baseDepth) {
    int top = _frameCount - 1;
    CompiledFunc func = _frames[top].Func;
    PinoValue[] regs = _frames[top].Regs;
    VmOp[] ops = func.Ops;
    int pc = _frames[top].Pc;

    while (true) {
      VmOp op = ops[pc];
      _frames[top].Pc = pc;
      switch (op.Kind) {
        // ----- constants & moves
        case OpKind.ConstInt: {
            var i = (ConstInt) op.Instr!;
            regs[Dest(func, i.Dest)] = PinoValue.FromInt(i.Value);
            break;
          }

        case OpKind.ConstFloat: {
            var i = (ConstFloat) op.Instr!;
            regs[Dest(func, i.Dest)] = PinoValue.FromFloat(i.Value);
            break;
          }

        case OpKind.ConstBool: {
            var i = (ConstBool) op.Instr!;
            regs[Dest(func, i.Dest)] = PinoValue.FromBool(i.Value);
            break;
          }

        case OpKind.ConstRune: {
            var i = (ConstRune) op.Instr!;
            regs[Dest(func, i.Dest)] = PinoValue.FromRune(i.Value);
            break;
          }

        case OpKind.ConstString: {
            var i = (ConstString) op.Instr!;
            regs[Dest(func, i.Dest)] = new PinoValue(0, StringLiteral(i.StringPoolId));
            break;
          }

        case OpKind.ConstEnum: {
            var i = (ConstEnum) op.Instr!;
            regs[Dest(func, i.Dest)] = PinoValue.FromEnum(i.Ordinal);
            break;
          }

        case OpKind.Copy: {
            var i = (Copy) op.Instr!;
            regs[Dest(func, i.Dest)] = Val(func, regs, i.Source);
            break;
          }

        case OpKind.Move: {
            var i = (Move) op.Instr!;
            regs[Dest(func, i.Dest)] = Val(func, regs, i.Source);
            break;
          }

        // ----- locals & places
        case OpKind.LoadSlot: {
            var i = (LoadSlot) op.Instr!;
            regs[Dest(func, i.Dest)] = regs[func.SlotBase + i.Slot.Index];
            break;
          }

        case OpKind.StoreSlot: {
            var i = (StoreSlot) op.Instr!;
            regs[func.SlotBase + i.Slot.Index] = Val(func, regs, i.Value);
            break;
          }

        case OpKind.AddrLocal: {
            var i = (AddrLocal) op.Instr!;
            regs[Dest(func, i.Dest)] = new PlaceRef(PlaceKind.LocalSlot, regs, func.SlotBase + i.Slot.Index).ToValue();
            break;
          }

        case OpKind.AddrGlobal: {
            var i = (AddrGlobal) op.Instr!;
            regs[Dest(func, i.Dest)] = new PlaceRef(PlaceKind.GlobalSlot, _globals, i.Global.Index).ToValue();
            break;
          }

        case OpKind.FieldGet: {
            var i = (FieldGet) op.Instr!;
            regs[Dest(func, i.Dest)] = Fields(Val(func, regs, i.Target))[i.FieldIndex];
            break;
          }

        case OpKind.FieldAddr: {
            var i = (FieldAddr) op.Instr!;
            PlaceRef parent = PlaceRef.FromValue(Val(func, regs, i.TargetPtr));
            regs[Dest(func, i.Dest)] = new PlaceRef(PlaceKind.StructField, parent, i.FieldIndex).ToValue();
            break;
          }

        case OpKind.LoadPtr: {
            var i = (LoadPtr) op.Instr!;
            regs[Dest(func, i.Dest)] = LoadPlace(PlaceRef.FromValue(Val(func, regs, i.Ptr)));
            break;
          }

        case OpKind.StorePtr: {
            var i = (StorePtr) op.Instr!;
            StorePlace(PlaceRef.FromValue(Val(func, regs, i.Ptr)), Val(func, regs, i.Value));
            break;
          }

        case OpKind.PtrEq: {
            var i = (PtrEq) op.Instr!;
            PlaceRef a = PlaceRef.FromValue(Val(func, regs, i.LeftPtr));
            PlaceRef b = PlaceRef.FromValue(Val(func, regs, i.RightPtr));
            regs[Dest(func, i.Dest)] = PinoValue.FromBool(a.Equals(b));
            break;
          }

        // ----- arithmetic
        case OpKind.Add: {
            var i = (Add) op.Instr!;
            regs[Dest(func, i.Dest)] = Arith(func, regs, i.Dest, i.Left, i.Right, '+');
            break;
          }

        case OpKind.Sub: {
            var i = (Sub) op.Instr!;
            regs[Dest(func, i.Dest)] = Arith(func, regs, i.Dest, i.Left, i.Right, '-');
            break;
          }

        case OpKind.Mul: {
            var i = (Mul) op.Instr!;
            regs[Dest(func, i.Dest)] = Arith(func, regs, i.Dest, i.Left, i.Right, '*');
            break;
          }

        case OpKind.DivF64: {
            var i = (DivF64) op.Instr!;
            double a = Val(func, regs, i.Left).AsFloat;
            double b = Val(func, regs, i.Right).AsFloat;
            regs[Dest(func, i.Dest)] = PinoValue.FromFloat(a / b);
            break;
          }

        case OpKind.FloorDiv: {
            var i = (FloorDiv) op.Instr!;
            PinoValue l = Val(func, regs, i.Left);
            PinoValue r = Val(func, regs, i.Right);
            if (IsFloat(func, i.Dest)) {
              regs[Dest(func, i.Dest)] = PinoValue.FromFloat(Math.Floor(l.AsFloat / r.AsFloat));
            } else {
              regs[Dest(func, i.Dest)] = PinoValue.FromInt(IntFloorDiv(l.Raw, r.Raw));
            }

            break;
          }

        case OpKind.FloorMod: {
            var i = (FloorMod) op.Instr!;
            PinoValue l = Val(func, regs, i.Left);
            PinoValue r = Val(func, regs, i.Right);
            if (IsFloat(func, i.Dest)) {
              double a = l.AsFloat;
              double b = r.AsFloat;
              regs[Dest(func, i.Dest)] = PinoValue.FromFloat(a - (Math.Floor(a / b) * b));
            } else {
              regs[Dest(func, i.Dest)] = PinoValue.FromInt(IntFloorMod(l.Raw, r.Raw));
            }

            break;
          }

        case OpKind.Neg: {
            var i = (Neg) op.Instr!;
            PinoValue v = Val(func, regs, i.Operand);
            regs[Dest(func, i.Dest)] = IsFloat(func, i.Dest)
                ? PinoValue.FromFloat(-v.AsFloat)
                : PinoValue.FromInt(unchecked(-v.Raw));
            break;
          }

        case OpKind.Not: {
            var i = (Not) op.Instr!;
            regs[Dest(func, i.Dest)] = PinoValue.FromBool(!Val(func, regs, i.Operand).AsBool);
            break;
          }

        case OpKind.Cmp: {
            var i = (Cmp) op.Instr!;
            PinoValue l = Val(func, regs, i.Left);
            PinoValue r = Val(func, regs, i.Right);
            bool result;
            if (_types.GetType(func.OperandType(i.Left)).Kind == IrTypeKind.Float) {
              double a = l.AsFloat;
              double b = r.AsFloat;
              result = i.Op switch {
                IrCmpOp.Eq => a == b,
                IrCmpOp.Ne => a != b,
                IrCmpOp.Lt => a < b,
                IrCmpOp.Le => a <= b,
                IrCmpOp.Gt => a > b,
                _ => a >= b,
              };
            } else {
              long a = l.Raw;
              long b = r.Raw;
              result = i.Op switch {
                IrCmpOp.Eq => a == b,
                IrCmpOp.Ne => a != b,
                IrCmpOp.Lt => a < b,
                IrCmpOp.Le => a <= b,
                IrCmpOp.Gt => a > b,
                _ => a >= b,
              };
            }

            regs[Dest(func, i.Dest)] = PinoValue.FromBool(result);
            break;
          }

        case OpKind.CastI64ToF64: {
            var i = (CastI64ToF64) op.Instr!;
            regs[Dest(func, i.Dest)] = PinoValue.FromFloat(Val(func, regs, i.Source).Raw);
            break;
          }

        case OpKind.CastF64ToI64: {
            var i = (CastF64ToI64) op.Instr!;
            regs[Dest(func, i.Dest)] = PinoValue.FromInt((long) Val(func, regs, i.Source).AsFloat);
            break;
          }

        case OpKind.CastI64ToRune: {
            var i = (CastI64ToRune) op.Instr!;
            regs[Dest(func, i.Dest)] = PinoValue.FromRune((int) Val(func, regs, i.Source).Raw);
            break;
          }

        case OpKind.CastRuneToI64: {
            var i = (CastRuneToI64) op.Instr!;
            regs[Dest(func, i.Dest)] = PinoValue.FromInt(Val(func, regs, i.Source).Raw);
            break;
          }

        case OpKind.StructEq: {
            var i = (StructEq) op.Instr!;
            regs[Dest(func, i.Dest)] = PinoValue.FromBool(
                PinoValueEquality.StructurallyEqual(Val(func, regs, i.Left), Val(func, regs, i.Right)));
            break;
          }

        // ----- aggregates
        case OpKind.StructNew: {
            var i = (StructNew) op.Instr!;
            regs[Dest(func, i.Dest)] = PinoValue.FromObject(Gather(func, regs, i.Fields));
            break;
          }

        case OpKind.TupleNew: {
            var i = (TupleNew) op.Instr!;
            regs[Dest(func, i.Dest)] = PinoValue.FromObject(Gather(func, regs, i.Fields));
            break;
          }

        case OpKind.TupleField: {
            var i = (TupleField) op.Instr!;
            regs[Dest(func, i.Dest)] = Fields(Val(func, regs, i.Tuple))[i.FieldIndex];
            break;
          }

        case OpKind.UnionNew: {
            var i = (UnionNew) op.Instr!;
            regs[Dest(func, i.Dest)] = NewUnionValue(func, regs, i);
            break;
          }

        case OpKind.UnionTag: {
            var i = (UnionTag) op.Instr!;
            PinoValue u = Val(func, regs, i.Union);
            regs[Dest(func, i.Dest)] = PinoValue.FromInt(u.Obj is PinoVmUnion boxed ? UseObject(boxed, "union.tag").Tag : u.Raw);
            break;
          }

        case OpKind.UnionField: {
            var i = (UnionField) op.Instr!;
            PinoValue u = Val(func, regs, i.Union);
            regs[Dest(func, i.Dest)] = u.Obj is PinoVmUnion boxed
                ? UseObject(boxed, "union.field").Fields[i.FieldIndex]
                : Fields(u)[i.FieldIndex];
            break;
          }

        case OpKind.IfaceBox: {
            var i = (IfaceBox) op.Instr!;
            var box = new PinoVmIfaceBox(i.VTable.Index, Val(func, regs, i.Value));
            Alloc(box, func.Fn.Temps[i.Dest.Index].Type, i.Region);
            regs[Dest(func, i.Dest)] = new PinoValue(i.VTable.Index, box);
            break;
          }

        case OpKind.IfaceFieldGet: {
            var i = (IfaceFieldGet) op.Instr!;
            PinoValue iface = Val(func, regs, i.Iface);
            regs[Dest(func, i.Dest)] = ReadIfaceProperty(UseObject((PinoVmIfaceBox) iface.Obj!, "interface field read"), i.PropertyIndex, func.Fn.Temps[i.Dest.Index].Type);
            break;
          }

        case OpKind.IfaceFieldAddr: {
            var i = (IfaceFieldAddr) op.Instr!;
            PinoValue iface = LoadPlace(PlaceRef.FromValue(Val(func, regs, i.IfacePtr)));
            var box = UseObject((PinoVmIfaceBox) iface.Obj!, "interface field address");
            regs[Dest(func, i.Dest)] = new PlaceRef(PlaceKind.IfaceField, box, i.PropertyIndex).ToValue();
            break;
          }

        // ----- closures & cells
        case OpKind.CellNew: {
            var i = (CellNew) op.Instr!;
            var cell = new PinoVmCell(Val(func, regs, i.InitialValue));
            Alloc(cell, func.Fn.Temps[i.Dest.Index].Type, i.Region);
            regs[Dest(func, i.Dest)] = PinoValue.FromObject(cell);
            break;
          }

        case OpKind.CellGet: {
            var i = (CellGet) op.Instr!;
            regs[Dest(func, i.Dest)] = UseObject((PinoVmCell) Val(func, regs, i.Cell).Obj!, "cell.get").Value;
            break;
          }

        case OpKind.CellSet: {
            var i = (CellSet) op.Instr!;
            UseObject((PinoVmCell) Val(func, regs, i.Cell).Obj!, "cell.set").Value = Val(func, regs, i.Value);
            break;
          }

        case OpKind.CellAddr: {
            var i = (CellAddr) op.Instr!;
            var cell = UseObject((PinoVmCell) Val(func, regs, i.Cell).Obj!, "cell.addr");
            regs[Dest(func, i.Dest)] = new PlaceRef(PlaceKind.Cell, cell, 0).ToValue();
            break;
          }

        case OpKind.EnvNew: {
            var i = (EnvNew) op.Instr!;
            var env = new PinoVmEnv(Gather(func, regs, i.Captures));
            Alloc(env, func.Fn.Temps[i.Dest.Index].Type, i.Region);
            regs[Dest(func, i.Dest)] = PinoValue.FromObject(env);
            break;
          }

        case OpKind.EnvFieldGet: {
            var i = (EnvFieldGet) op.Instr!;
            regs[Dest(func, i.Dest)] = UseObject((PinoVmEnv) Val(func, regs, i.Env).Obj!, "env.field").Fields[i.FieldIndex];
            break;
          }

        case OpKind.ClosureMake: {
            var i = (ClosureMake) op.Instr!;
            object? env = i.Env is { } envOp ? Val(func, regs, envOp).Obj : null;
            regs[Dest(func, i.Dest)] = new PinoValue(i.Function.Index + 1L, env);
            break;
          }

        // ----- calls
        case OpKind.CallDirect: {
            var i = (CallDirect) op.Instr!;
            _frames[top].Pc = pc + 1;
            CompiledFunc callee = Func(i.Callee);
            int calleeDepth = _frameCount;
            PinoValue[] calleeRegs = RentRegs(calleeDepth, callee.FrameSize);
            for (int a = 0; a < i.Arguments.Length; a++) {
              calleeRegs[a] = Val(func, regs, i.Arguments[a]);
            }

            _frames[calleeDepth] = new Frame {
              Func = callee,
              Regs = calleeRegs,
              Pc = 0,
              RetReg = i.Dest is { } d ? Dest(func, d) : -1,
            };
            _frameCount = calleeDepth + 1;
            top = calleeDepth;
            func = callee;
            regs = calleeRegs;
            ops = func.Ops;
            pc = 0;
            continue;
          }

        case OpKind.CallClosure: {
            var i = (CallClosure) op.Instr!;
            PinoValue closure = Val(func, regs, i.Closure);
            if (closure.Raw == 0) {
              throw new PinoVmInternalException("A closure value with no function was called.");
            }

            PinoVmEnv? env = closure.Obj as PinoVmEnv;
            if (env is not null) {
              UseObject(env, "closure call");
              _rc.Inc(env);
            }

            _frames[top].Pc = pc + 1;
            CompiledFunc callee = Func(new IrFuncId((int) closure.Raw - 1));
            int calleeDepth = _frameCount;
            PinoValue[] calleeRegs = RentRegs(calleeDepth, callee.FrameSize);
            int slot = 0;
            if (env is not null) {
              calleeRegs[slot++] = PinoValue.FromObject(env);
            }

            for (int a = 0; a < i.Arguments.Length; a++) {
              calleeRegs[slot++] = Val(func, regs, i.Arguments[a]);
            }

            _frames[calleeDepth] = new Frame {
              Func = callee,
              Regs = calleeRegs,
              Pc = 0,
              RetReg = i.Dest is { } d ? Dest(func, d) : -1,
            };
            _frameCount = calleeDepth + 1;
            top = calleeDepth;
            func = callee;
            regs = calleeRegs;
            ops = func.Ops;
            pc = 0;
            continue;
          }

        case OpKind.CallIface: {
            var i = (CallIface) op.Instr!;
            PinoValue receiver = Val(func, regs, i.Receiver);
            PinoVmIfaceBox box;
            PinoValue self;
            if (i.IsMutating) {
              PinoValue ifaceValue = LoadPlace(PlaceRef.FromValue(receiver));
              box = UseObject((PinoVmIfaceBox) ifaceValue.Obj!, "interface method call");
              self = new PlaceRef(PlaceKind.IfaceField, box, -1).ToValue();
            } else {
              box = UseObject((PinoVmIfaceBox) receiver.Obj!, "interface method call");
              self = box.Value;
              IncValue(self, _module.VTables[box.VTableId].ConcreteStructType);
            }

            IrVTable vtable = _module.VTables[box.VTableId];
            _frames[top].Pc = pc + 1;
            CompiledFunc callee = Func(vtable.MethodFuncIds[i.MethodIndex]);
            int calleeDepth = _frameCount;
            PinoValue[] calleeRegs = RentRegs(calleeDepth, callee.FrameSize);
            calleeRegs[0] = self;
            for (int a = 0; a < i.Arguments.Length; a++) {
              calleeRegs[a + 1] = Val(func, regs, i.Arguments[a]);
            }

            _frames[calleeDepth] = new Frame {
              Func = callee,
              Regs = calleeRegs,
              Pc = 0,
              RetReg = i.Dest is { } d ? Dest(func, d) : -1,
            };
            _frameCount = calleeDepth + 1;
            top = calleeDepth;
            func = callee;
            regs = calleeRegs;
            ops = func.Ops;
            pc = 0;
            continue;
          }

        case OpKind.CallRuntime: {
            var i = (CallRuntime) op.Instr!;
            PinoValue result = ExecRuntime(func, regs, i);
            if (i.Dest is { } d) {
              regs[Dest(func, d)] = result;
            }

            break;
          }

        // ----- ownership
        case OpKind.RcInc: {
            var i = (RcInc) op.Instr!;
            IncValue(Val(func, regs, i.Value), func.OperandType(i.Value));
            break;
          }

        case OpKind.RcDec: {
            var i = (RcDec) op.Instr!;
            DecValue(Val(func, regs, i.Value), func.OperandType(i.Value));
            break;
          }

        case OpKind.EnsureUnique: {
            var i = (EnsureUnique) op.Instr!;
            EnsureUniquePlace(PlaceRef.FromValue(Val(func, regs, i.PlacePtr)));
            break;
          }

        case OpKind.Reset:
        case OpKind.Reuse:
          throw new PinoVmInternalException("The VM does not support the reset and reuse instructions because Step 1 never emits them.");

        case OpKind.ArenaEnter:
        case OpKind.ArenaExit:
          break;

        // ----- terminators
        case OpKind.Br: {
            int target = op.Target0;
            if (target <= pc) {
              BackwardBranch();
            }

            pc = target;
            continue;
          }

        case OpKind.BrIf: {
            var t = (BrIf) op.Term!;
            int target = Val(func, regs, t.Condition).AsBool ? op.Target0 : op.Target1;
            if (target <= pc) {
              BackwardBranch();
            }

            pc = target;
            continue;
          }

        case OpKind.Switch: {
            var t = (Switch) op.Term!;
            long key = Val(func, regs, t.Scrutinee).Raw;
            int target = op.DefaultTarget;
            int[] values = op.CaseValues!;
            for (int c = 0; c < values.Length; c++) {
              if (values[c] == key) {
                target = op.CaseTargets![c];
                break;
              }
            }

            if (target < 0) {
              throw new PinoVmInternalException($"A switch in '{func.Fn.Name}' had no case for {key} and no default.");
            }

            if (target <= pc) {
              BackwardBranch();
            }

            pc = target;
            continue;
          }

        case OpKind.Ret:
        case OpKind.RetVoid: {
            PinoValue result = op.Kind == OpKind.Ret ? Val(func, regs, ((Ret) op.Term!).Value) : default;
            int retReg = _frames[top].RetReg;
            Array.Clear(regs, 0, func.FrameSize);
            _frames[top] = default;
            _frameCount = top;
            if (_frameCount == baseDepth) {
              return result;
            }

            top = _frameCount - 1;
            ref Frame caller = ref _frames[top];
            func = caller.Func;
            regs = caller.Regs;
            ops = func.Ops;
            pc = caller.Pc;
            if (retReg >= 0) {
              regs[retReg] = result;
            }

            continue;
          }

        case OpKind.Panic: {
            var t = (Panic) op.Term!;
            throw new PinoVmPanic(StringOf(Val(func, regs, t.Message)), t.IsAssert);
          }

        case OpKind.Unreachable:
          throw new PinoVmInternalException($"Execution reached code in '{func.Fn.Name}' that the compiler proved cannot run.");

        default:
          if (!ExecCollectionOp(func, regs, op)) {
            throw new PinoVmInternalException($"The VM has no implementation for '{op.Kind}'.");
          }

          break;
      }

      pc++;
    }
  }

  private PinoValue[] RentRegs(int depth, int frameSize) {
    if (depth >= _frames.Length) {
      int newLength = _frames.Length * 2;
      Array.Resize(ref _frames, newLength);
      Array.Resize(ref _frameRegs, newLength);
    }

    PinoValue[]? regs = _frameRegs[depth];
    if (regs is null || regs.Length < frameSize) {
      regs = new PinoValue[Math.Max(frameSize, 32)];
      _frameRegs[depth] = regs;
    }

    return regs;
  }

  private void ClearFrames() {
    for (int i = 0; i < _frameCount; i++) {
      Array.Clear(_frames[i].Regs, 0, _frames[i].Func.FrameSize);
      _frames[i] = default;
    }

    _frameCount = 0;
  }

  private void BackwardBranch() {
    _steps++;
    if (_options.MaxSteps > 0 && _steps > _options.MaxSteps) {
      throw new PinoVmStepLimitException(_steps);
    }

    if ((_steps & 0x3FF) == 0) {
      _options.CancellationToken.ThrowIfCancellationRequested();
    }
  }

  private int Dest(CompiledFunc func, IrTempId temp) => func.ParamCount + temp.Index;

  private static PinoValue Val(CompiledFunc func, PinoValue[] regs, IrOperand op) =>
      regs[op.Kind == IrOperandKind.Param ? op.Index : func.ParamCount + op.Index];

  private PinoValue[] Gather(CompiledFunc func, PinoValue[] regs, System.Collections.Immutable.ImmutableArray<IrOperand> operands) {
    if (operands.Length == 0) {
      return s_noFields;
    }

    var values = new PinoValue[operands.Length];
    for (int i = 0; i < values.Length; i++) {
      values[i] = Val(func, regs, operands[i]);
    }

    return values;
  }

  private static PinoValue[] Fields(in PinoValue aggregate) =>
      aggregate.Obj as PinoValue[] ?? throw new PinoVmInternalException("A field was read from a value that is not a struct or tuple.");

  private PinoVmString StringLiteral(int poolId) {
    PinoVmString? literal = _stringLiterals[poolId];
    if (literal is null) {
      literal = PinoVmString.FromString(_module.StringPool[poolId]);
      _rc.MakeImmortal(literal, _types.String);
      _stringLiterals[poolId] = literal;
    }

    return literal;
  }

  private bool IsFloat(CompiledFunc func, IrTempId temp) =>
      _types.GetType(func.Fn.Temps[temp.Index].Type).Kind == IrTypeKind.Float;

  private PinoValue Arith(CompiledFunc func, PinoValue[] regs, IrTempId dest, IrOperand left, IrOperand right, char op) {
    PinoValue l = Val(func, regs, left);
    PinoValue r = Val(func, regs, right);
    IrTypeKind kind = _types.GetType(func.Fn.Temps[dest.Index].Type).Kind;
    if (kind == IrTypeKind.Float) {
      double a = l.AsFloat;
      double b = r.AsFloat;
      return PinoValue.FromFloat(op switch { '+' => a + b, '-' => a - b, _ => a * b });
    }

    long x = l.Raw;
    long y = r.Raw;
    long result = unchecked(op switch { '+' => x + y, '-' => x - y, _ => x * y });
    return kind == IrTypeKind.Rune ? PinoValue.FromRune((int) result) : PinoValue.FromInt(result);
  }

  private static long IntFloorDiv(long a, long b) {
    if (b == 0) {
      throw new PinoVmPanic(IrLowerer.PanicDivByZero);
    }

    if (a == long.MinValue && b == -1) {
      throw new PinoVmPanic(IrLowerer.PanicIntMinDivOverflow);
    }

    long q = a / b;
    long r = a % b;
    if (r != 0 && (r ^ b) < 0) {
      q--;
    }

    return q;
  }

  private static long IntFloorMod(long a, long b) {
    if (b == 0) {
      throw new PinoVmPanic(IrLowerer.PanicDivByZero);
    }

    if (a == long.MinValue && b == -1) {
      throw new PinoVmPanic(IrLowerer.PanicIntMinDivOverflow);
    }

    long r = a % b;
    if (r != 0 && (r ^ b) < 0) {
      r += b;
    }

    return r;
  }

  // ---------------------------------------------------------------------------
  // Allocation & access checks
  // ---------------------------------------------------------------------------

  private T Alloc<T>(T obj, IrTypeId typeId, AllocRegion region) where T : PinoVmHeapObject {
    if (_frameCount > 0) {
      ref Frame top = ref _frames[_frameCount - 1];
      SourceSpan span = top.Func.Spans[Math.Min(top.Pc, top.Func.Spans.Length - 1)];
      return _rc.Register(obj, typeId, span, top.Func.Fn.Name, region.Kind);
    }

    return _rc.Register(obj, typeId, default, function: null, region.Kind);
  }

  private T Alloc<T>(T obj, IrTypeId typeId) where T : PinoVmHeapObject => Alloc(obj, typeId, AllocRegion.Heap);

  private T UseObject<T>(T obj, string operation) where T : PinoVmHeapObject {
    _rc.CheckAccess(obj, operation);
    return obj;
  }

  private string StringOf(in PinoValue value) =>
      UseObject((PinoVmString) value.Obj!, "string access").ToString();

  private PinoVmString NewString(string text, IrTypeId? type = null) =>
      Alloc(PinoVmString.FromString(text), type ?? _types.String);

  // ---------------------------------------------------------------------------
  // Places
  // ---------------------------------------------------------------------------

  private PinoValue LoadPlace(PlaceRef place) {
    switch (place.Kind) {
      case PlaceKind.LocalSlot:
      case PlaceKind.GlobalSlot:
        return ((PinoValue[]) place.Container)[place.Index];

      case PlaceKind.Cell:
        return UseObject((PinoVmCell) place.Container, "load through pointer").Value;

      case PlaceKind.StructField: {
          PinoValue parent = LoadPlace((PlaceRef) place.Container);
          return Fields(parent)[place.Index];
        }

      case PlaceKind.VecElem: {
          var vec = UseObject((PinoVmVec) place.Container, "load through pointer");
          return vec.Items[place.Index];
        }

      case PlaceKind.MapValue: {
          var map = UseObject((PinoVmMap) place.Container, "load through pointer");
          return map.EntryAt(place.Index).Value;
        }

      case PlaceKind.PoolSlot: {
          var pool = UseObject((PinoVmPool) place.Container, "load through pointer");
          return pool.SlotAt(place.Index).Value;
        }

      case PlaceKind.IfaceField: {
          var box = UseObject((PinoVmIfaceBox) place.Container, "load through pointer");
          return place.Index < 0 ? box.Value : ReadIfaceProperty(box, place.Index, IrTypeId.Invalid);
        }

      default:
        throw new PinoVmInternalException($"Unknown place kind '{place.Kind}'.");
    }
  }

  private void StorePlace(PlaceRef place, PinoValue value) {
    switch (place.Kind) {
      case PlaceKind.LocalSlot:
      case PlaceKind.GlobalSlot:
        ((PinoValue[]) place.Container)[place.Index] = value;
        break;

      case PlaceKind.Cell:
        UseObject((PinoVmCell) place.Container, "store through pointer").Value = value;
        break;

      case PlaceKind.StructField: {
          var parentPlace = (PlaceRef) place.Container;
          PinoValue parent = LoadPlace(parentPlace);
          var copy = (PinoValue[]) Fields(parent).Clone();
          copy[place.Index] = value;
          StorePlace(parentPlace, new PinoValue(parent.Raw, copy));
          break;
        }

      case PlaceKind.VecElem:
        UseObject((PinoVmVec) place.Container, "store through pointer").Items[place.Index] = value;
        break;

      case PlaceKind.MapValue:
        UseObject((PinoVmMap) place.Container, "store through pointer").EntryAt(place.Index).Value = value;
        break;

      case PlaceKind.PoolSlot:
        UseObject((PinoVmPool) place.Container, "store through pointer").SlotAt(place.Index).Value = value;
        break;

      case PlaceKind.IfaceField: {
          var box = UseObject((PinoVmIfaceBox) place.Container, "store through pointer");
          if (place.Index < 0) {
            box.Value = value;
          } else {
            WriteIfaceProperty(box, place.Index, value);
          }

          break;
        }

      default:
        throw new PinoVmInternalException($"Unknown place kind '{place.Kind}'.");
    }
  }

  private PinoValue ReadIfaceProperty(PinoVmIfaceBox box, int propertyIndex, IrTypeId propertyType) {
    IrVTable vtable = _module.VTables[box.VTableId];
    int fieldIndex = propertyIndex < vtable.PropertyFieldIndices.Length ? vtable.PropertyFieldIndices[propertyIndex] : -1;
    if (fieldIndex >= 0) {
      return Fields(box.Value)[fieldIndex];
    }

    // Promoted (embedded) field: run the generated getter on an owned copy of the struct.
    IncValue(box.Value, vtable.ConcreteStructType);
    PinoValue result = CallFunction(vtable.PropertyGetterFuncIds[propertyIndex], [box.Value]);
    if (propertyType.IsValid) {
      DecValue(result, propertyType);
    } else if (_module.Types.GetType(vtable.InterfaceType) is IrInterfaceType iface) {
      DecValue(result, iface.Properties[propertyIndex].Type);
    }

    return result;
  }

  private void WriteIfaceProperty(PinoVmIfaceBox box, int propertyIndex, PinoValue value) {
    IrVTable vtable = _module.VTables[box.VTableId];
    int fieldIndex = propertyIndex < vtable.PropertyFieldIndices.Length ? vtable.PropertyFieldIndices[propertyIndex] : -1;
    if (fieldIndex >= 0) {
      var copy = (PinoValue[]) Fields(box.Value).Clone();
      copy[fieldIndex] = value;
      box.Value = new PinoValue(box.Value.Raw, copy);
      return;
    }

    IrFuncId setter = vtable.PropertySetterFuncIds[propertyIndex]
        ?? throw new PinoVmInternalException("A read-only interface property was written.");
    CallFunction(setter, [new PlaceRef(PlaceKind.IfaceField, box, -1).ToValue(), value]);
  }

  // ---------------------------------------------------------------------------
  // Unions
  // ---------------------------------------------------------------------------

  private PinoValue NewUnionValue(CompiledFunc func, PinoValue[] regs, UnionNew instr) {
    IrTypeId destType = func.Fn.Temps[instr.Dest.Index].Type;
    var union = (IrUnionType) _types.GetType(destType);
    int tag = union.Variants[instr.VariantIndex].TagOrdinal;
    PinoValue[] fields = Gather(func, regs, instr.Fields);
    if (union.IsRecursiveUnion) {
      var node = new PinoVmUnion(tag, fields);
      Alloc(node, destType, instr.Region);
      return PinoValue.FromObject(node);
    }

    return new PinoValue(tag, fields.Length == 0 ? null : fields);
  }

  /// <summary>Builds an inline <c>Option</c>/<c>Result</c>/<c>IOError</c> value of <paramref name="unionType"/> by variant name.</summary>
  private PinoValue MakeUnion(IrTypeId unionType, string variantName, params PinoValue[] fields) {
    var union = (IrUnionType) _types.GetType(unionType);
    foreach (IrUnionVariant variant in union.Variants) {
      if (string.Equals(variant.Name, variantName, StringComparison.Ordinal)) {
        return new PinoValue(variant.TagOrdinal, fields.Length == 0 ? null : fields);
      }
    }

    throw new PinoVmInternalException($"Union '{union.Name}' has no variant '{variantName}'.");
  }

  private PinoValue MakeSome(IrTypeId optionType, PinoValue payload) => MakeUnion(optionType, "Some", payload);

  private PinoValue MakeNone(IrTypeId optionType) => MakeUnion(optionType, "None");

  // ---------------------------------------------------------------------------
  // Reference counting
  // ---------------------------------------------------------------------------

  private void IncValue(PinoValue value, IrTypeId typeId) {
    IrType type = _types.GetType(typeId);
    if (!type.IsRcManaged) {
      return;
    }

    switch (type) {
      case IrStructType st:
        if (value.Obj is PinoValue[] sf) {
          for (int i = 0; i < st.Fields.Length; i++) {
            IncValue(sf[i], st.Fields[i].Type);
          }
        }

        break;

      case IrTupleType tt:
        if (value.Obj is PinoValue[] tf) {
          for (int i = 0; i < tt.Fields.Length; i++) {
            IncValue(tf[i], tt.Fields[i].Type);
          }
        }

        break;

      case IrUnionType ut when !ut.IsRecursiveUnion:
        if (value.Obj is PinoValue[] uf) {
          IrUnionVariant variant = VariantByTag(ut, (int) value.Raw);
          for (int i = 0; i < variant.Fields.Length; i++) {
            IncValue(uf[i], variant.Fields[i].Type);
          }
        }

        break;

      default:
        if (value.Obj is PinoVmHeapObject heap) {
          _rc.Inc(heap);
        }

        break;
    }
  }

  private void DecValue(PinoValue value, IrTypeId typeId) {
    IrType type = _types.GetType(typeId);
    if (!type.IsRcManaged) {
      return;
    }

    switch (type) {
      case IrStructType st:
        if (value.Obj is PinoValue[] sf) {
          for (int i = 0; i < st.Fields.Length; i++) {
            DecValue(sf[i], st.Fields[i].Type);
          }
        }

        break;

      case IrTupleType tt:
        if (value.Obj is PinoValue[] tf) {
          for (int i = 0; i < tt.Fields.Length; i++) {
            DecValue(tf[i], tt.Fields[i].Type);
          }
        }

        break;

      case IrUnionType ut when !ut.IsRecursiveUnion:
        if (value.Obj is PinoValue[] uf) {
          IrUnionVariant variant = VariantByTag(ut, (int) value.Raw);
          for (int i = 0; i < variant.Fields.Length; i++) {
            DecValue(uf[i], variant.Fields[i].Type);
          }
        }

        break;

      default:
        if (value.Obj is PinoVmHeapObject heap) {
          DecObject(heap);
        }

        break;
    }
  }

  private static IrUnionVariant VariantByTag(IrUnionType union, int tag) {
    foreach (IrUnionVariant variant in union.Variants) {
      if (variant.TagOrdinal == tag) {
        return variant;
      }
    }

    throw new PinoVmInternalException($"Union '{union.Name}' has no variant with tag {tag}.");
  }

  private void DecObject(PinoVmHeapObject obj) {
    if (!_rc.Dec(obj)) {
      return;
    }

    _dropStack.Push(obj);
    if (_dropping) {
      return;
    }

    _dropping = true;
    try {
      while (_dropStack.Count > 0) {
        DropChildren(_dropStack.Pop());
      }
    } finally {
      _dropping = false;
    }
  }

  /// <summary>Drops the children of an object whose shadow count just reached <c>0</c> (the per-type <c>drop_T</c>).</summary>
  private void DropChildren(PinoVmHeapObject obj) {
    switch (obj) {
      case PinoVmRegex rx:
        DecObject(rx.Pattern);
        break;

      case PinoVmVec vec: {
          IrTypeId element = ((IrVectorType) _types.GetType(vec.TypeId)).ElementType;
          if (_types.GetType(element).IsRcManaged) {
            for (int i = 0; i < vec.Count; i++) {
              DecValue(vec.Items[i], element);
            }
          }

          break;
        }

      case PinoVmMap map: {
          var mt = (IrMapType) _types.GetType(map.TypeId);
          bool dropKey = _types.GetType(mt.KeyType).IsRcManaged;
          bool dropValue = _types.GetType(mt.ValueType).IsRcManaged;
          for (int i = 0; i < map.SlotLength; i++) {
            if (!map.SlotIsLive(i)) {
              continue;
            }

            if (dropKey) {
              DecValue(map.EntryAt(i).Key, mt.KeyType);
            }

            if (dropValue) {
              DecValue(map.EntryAt(i).Value, mt.ValueType);
            }
          }

          break;
        }

      case PinoVmSet set: {
          IrTypeId element = ((IrSetType) _types.GetType(set.TypeId)).ElementType;
          if (_types.GetType(element).IsRcManaged) {
            for (int i = 0; i < set.SlotLength; i++) {
              if (set.SlotIsLive(i)) {
                DecValue(set.EntryAt(i).Key, element);
              }
            }
          }

          break;
        }

      case PinoVmPool pool: {
          IrTypeId element = ((IrPoolType) _types.GetType(pool.TypeId)).ElementType;
          if (_types.GetType(element).IsRcManaged) {
            for (int i = 0; i < pool.SlotLength; i++) {
              if (pool.SlotAt(i).State == PoolSlotState.Live) {
                DecValue(pool.SlotAt(i).Value, element);
              }
            }
          }

          break;
        }

      case PinoVmCell cell:
        DecValue(cell.Value, ((IrCellType) _types.GetType(cell.TypeId)).ElementType);
        break;

      case PinoVmEnv env: {
          var et = (IrEnvType) _types.GetType(env.TypeId);
          for (int i = 0; i < env.Fields.Length && i < et.Fields.Length; i++) {
            DecValue(env.Fields[i], et.Fields[i].Type);
          }

          break;
        }

      case PinoVmUnion node: {
          var ut = (IrUnionType) _types.GetType(node.TypeId);
          IrUnionVariant variant = VariantByTag(ut, node.Tag);
          for (int i = 0; i < variant.Fields.Length; i++) {
            DecValue(node.Fields[i], variant.Fields[i].Type);
          }

          break;
        }

      case PinoVmIfaceBox box:
        DecValue(box.Value, _module.VTables[box.VTableId].ConcreteStructType);
        break;
    }
  }

  /// <summary>
  /// <c>ensure_unique %place</c>: when the container stored at the place is shared (<c>rc != 1</c>), stores a fresh unique
  /// copy that holds its own references to the elements and releases the place's old reference.
  /// </summary>
  private void EnsureUniquePlace(PlaceRef place) {
    PinoValue current = LoadPlace(place);
    if (current.Obj is not PinoVmHeapObject heap || heap.IsArena || heap.ShadowRc == 1) {
      return;
    }

    UseObject(heap, "ensure_unique");
    PinoVmHeapObject copy;
    switch (heap) {
      case PinoVmVec vec: {
          PinoVmVec clone = vec.ShallowClone();
          IrTypeId element = ((IrVectorType) _types.GetType(vec.TypeId)).ElementType;
          for (int i = 0; i < clone.Count; i++) {
            IncValue(clone.Items[i], element);
          }

          copy = clone;
          break;
        }

      case PinoVmMap map: {
          PinoVmMap clone = map.ShallowClone();
          var mt = (IrMapType) _types.GetType(map.TypeId);
          for (int i = 0; i < clone.SlotLength; i++) {
            if (clone.SlotIsLive(i)) {
              IncValue(clone.EntryAt(i).Key, mt.KeyType);
              IncValue(clone.EntryAt(i).Value, mt.ValueType);
            }
          }

          copy = clone;
          break;
        }

      case PinoVmSet set: {
          PinoVmSet clone = set.ShallowClone();
          IrTypeId element = ((IrSetType) _types.GetType(set.TypeId)).ElementType;
          for (int i = 0; i < clone.SlotLength; i++) {
            if (clone.SlotIsLive(i)) {
              IncValue(clone.EntryAt(i).Key, element);
            }
          }

          copy = clone;
          break;
        }

      case PinoVmPool pool: {
          PinoVmPool clone = pool.ShallowClone();
          IrTypeId element = ((IrPoolType) _types.GetType(pool.TypeId)).ElementType;
          for (int i = 0; i < clone.SlotLength; i++) {
            if (clone.SlotAt(i).State == PoolSlotState.Live) {
              IncValue(clone.SlotAt(i).Value, element);
            }
          }

          copy = clone;
          break;
        }

      case PinoVmIfaceBox box: {
          IrVTable vtable = _module.VTables[box.VTableId];
          IncValue(box.Value, vtable.ConcreteStructType);
          copy = new PinoVmIfaceBox(box.VTableId, box.Value);
          break;
        }

      default:
        return;
    }

    Alloc(copy, heap.TypeId);
    PinoValue replacement = heap is PinoVmIfaceBox ifaceBox
        ? new PinoValue(ifaceBox.VTableId, copy)
        : PinoValue.FromObject(copy);
    StorePlace(place, replacement);
    DecObject(heap);
  }

  // ---------------------------------------------------------------------------
  // Panic reporting (ERR-4, ERR-21)
  // ---------------------------------------------------------------------------

  private string FormatPanicReport(string message) {
    var sb = new StringBuilder();
    sb.Append("thread 'main' panicked at '").Append(message).Append("'\n");
    if (_frameCount > 0) {
      sb.Append("  at ").Append(Location(_frameCount - 1)).Append('\n');
      sb.Append("stack backtrace:\n");
      int n = 0;
      for (int i = _frameCount - 1; i >= 0; i--) {
        sb.Append(CultureInfo.InvariantCulture, $"  {n++}: {_frames[i].Func.Fn.Name} at {Location(i)}\n");
      }
    }

    return sb.ToString();
  }

  private (string? File, int Line) LocationFileLine(int frameIndex) {
    ref Frame frame = ref _frames[frameIndex];
    SourceText? source = _options.SourceResolver?.Invoke(frame.Func.Fn)
        ?? frame.Func.Fn.Source
        ?? _activeRootSource
        ?? _options.RootSource;
    int pc = Math.Min(frame.Pc - (frameIndex == _frameCount - 1 ? 0 : 1), frame.Func.Spans.Length - 1);
    SourceSpan span = pc >= 0 ? frame.Func.Spans[pc] : default;
    if (source is null) {
      return (null, 0);
    }

    int offset = Math.Clamp(span.Start, 0, source.Length);
    LinePosition position = source.GetLineColumn(offset);
    string file = (source.FilePath ?? "<input>").Replace('\\', '/');
    return (file, position.Line);
  }

  private string Location(int frameIndex) {
    ref Frame frame = ref _frames[frameIndex];
    SourceText? source = _options.SourceResolver?.Invoke(frame.Func.Fn)
        ?? frame.Func.Fn.Source
        ?? _activeRootSource
        ?? _options.RootSource;
    int pc = Math.Min(frame.Pc - (frameIndex == _frameCount - 1 ? 0 : 1), frame.Func.Spans.Length - 1);
    SourceSpan span = pc >= 0 ? frame.Func.Spans[pc] : default;
    if (source is null) {
      return "<unknown>";
    }

    int offset = Math.Clamp(span.Start, 0, source.Length);
    LinePosition position = source.GetLineColumn(offset);
    string file = (source.FilePath ?? "<input>").Replace('\\', '/');
    return string.Create(
        CultureInfo.InvariantCulture,
        $"{file}:{position.Line}:{position.Column}");
  }
}
