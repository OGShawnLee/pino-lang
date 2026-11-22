using Pino.Compiler.Checking;
using Pino.Compiler.IR;

namespace Pino.Compiler.Tests.IR;

public sealed class IrPrinterGoldenTests {
  [Fact]
  public void SumVec_PreRc_MatchesGoldenTextAndVerifies() {
    IrModule module = BuildSumVecModule(withStep1Rc: false);
    AssertValid(module);

    string actual = IrPrinter.Print(module);
    const string Expected = """
      fn sum_vec(@owned %xs: []int) -> int [pure] {
        slots: $total: int [mut], $snap: []int, $i: int [mut], $len: int
        bb0:
          %c0 = const.i64 0
          store $total, %c0
          store $snap, %xs
          %t0 = load $snap
          %t1 = vec.len %t0
          store $len, %t1
          store $i, %c0
          br bb1
        bb1:
          %t2 = load $i
          %t3 = load $len
          %t4 = cmp.lt.i64 %t2, %t3
          br_if %t4, bb2, bb3
        bb2:
          %t5 = load $snap
          %t6 = load $i
          %x = vec.get.unchecked %t5, %t6
          %t7 = load $total
          %t8 = add.i64 %t7, %x
          store $total, %t8
          %c1 = const.i64 1
          %t9 = add.i64 %t6, %c1
          store $i, %t9
          br bb1
        bb3:
          %t10 = load $total
          ret %t10
      }

      """;

    Assert.Equal(NormalizeLf(Expected), actual);
  }

  [Fact]
  public void SumVec_Step1Rc_MatchesGoldenTextAndVerifies() {
    IrModule module = BuildSumVecModule(withStep1Rc: true);
    AssertValid(module);

    string actual = IrPrinter.Print(module);
    const string Expected = """
      fn sum_vec(@owned %xs: []int) -> int [pure] {
        slots: $total: int [mut], $snap: []int, $i: int [mut], $len: int
        bb0:
          %c0 = const.i64 0
          store $total, %c0
          rc_inc %xs
          store $snap, %xs
          %t0 = load $snap
          rc_inc %t0
          %t1 = vec.len %t0
          rc_dec %t0
          store $len, %t1
          store $i, %c0
          br bb1
        bb1:
          %t2 = load $i
          %t3 = load $len
          %t4 = cmp.lt.i64 %t2, %t3
          br_if %t4, bb2, bb3
        bb2:
          %t5 = load $snap
          rc_inc %t5
          %t6 = load $i
          %x = vec.get.unchecked %t5, %t6
          rc_dec %t5
          %t7 = load $total
          %t8 = add.i64 %t7, %x
          store $total, %t8
          %c1 = const.i64 1
          %t9 = add.i64 %t6, %c1
          store $i, %t9
          br bb1
        bb3:
          %t10 = load $total
          %d0 = load $snap
          rc_dec %d0
          rc_dec %xs
          ret %t10
      }

      """;

    Assert.Equal(NormalizeLf(Expected), actual);
  }

  [Fact]
  public void Area_PreRcAndStep1Rc_MatchGoldenTextAndVerify() {
    // Shape is pointer-free, so Step 1 RC emits zero rc_inc/rc_dec operations (identical output).
    IrModule module = BuildAreaModule();
    AssertValid(module);

    string actual = IrPrinter.Print(module);
    const string Expected = """
      fn area(@owned %s: Shape) -> float [pure] {
        slots: $res: float [mut]
        bb0:
          %tag = union.tag %s
          switch %tag, [0 -> bb1, 1 -> bb2]
        bb1:
          %r = union.field %s, 0, 0
          %pi = const.f64 3.14159
          %t0 = mul.f64 %pi, %r
          %t1 = mul.f64 %t0, %r
          store $res, %t1
          br bb3
        bb2:
          %w = union.field %s, 1, 0
          %h = union.field %s, 1, 1
          %t2 = mul.f64 %w, %h
          store $res, %t2
          br bb3
        bb3:
          %t3 = load $res
          ret %t3
      }

      """;

    Assert.Equal(NormalizeLf(Expected), actual);
  }

  [Fact]
  public void LogRecord_PreRc_MatchesGoldenTextAndVerifies() {
    IrModule module = BuildLogRecordModule(withStep1Rc: false);
    AssertValid(module);

    string actual = IrPrinter.Print(module);
    const string Expected = """
      fn Log::record(@mut %self: ptr<Log>, @owned %msg: string) -> void [pure] {
        bb0:
          %p0 = field_addr %self, Log.entries
          ensure_unique %p0
          vec.push %p0, %msg
          ret.void
      }

      """;

    Assert.Equal(NormalizeLf(Expected), actual);
  }

  [Fact]
  public void LogRecord_Step1Rc_MatchesGoldenTextAndVerifies() {
    IrModule module = BuildLogRecordModule(withStep1Rc: true);
    AssertValid(module);

    string actual = IrPrinter.Print(module);
    const string Expected = """
      fn Log::record(@mut %self: ptr<Log>, @owned %msg: string) -> void [pure] {
        bb0:
          %p0 = field_addr %self, Log.entries
          ensure_unique %p0
          rc_inc %msg
          vec.push %p0, %msg
          rc_dec %msg
          ret.void
      }

      """;

    Assert.Equal(NormalizeLf(Expected), actual);
  }

  [Fact]
  public void Print_ShowsModuleStateEffectsAndAllocRegions() {
    var types = new IrTypeTable();
    IrTypeId vecInt = types.GetVector(types.Int);
    var module = new IrModule(types);
    module.AddGlobal("counter", types.Int, isMutable: true);

    IrFunction fn = module.AddFunction(
        "build_in_arena",
        [new IrParam("x", types.Int, IrParamOwnership.Borrowed)],
        types.Int,
        ModuleStateEffects.ReadWrite);
    IrTempId vecTemp = fn.AddTemp(vecInt, "v");
    IrTempId lenTemp = fn.AddTemp(types.Int, "len");

    IrBasicBlock bb0 = fn.AddBlock();
    bb0.Instructions.Add(new ArenaEnter(0));
    bb0.Instructions.Add(new VecNew(vecTemp, [IrOperand.FromParam(0)], AllocRegion.Arena(0)));
    bb0.Instructions.Add(new VecLen(lenTemp, vecTemp));
    bb0.Instructions.Add(new ArenaExit(0));
    bb0.Terminator = new Ret(lenTemp);

    AssertValid(module);

    string actual = IrPrinter.Print(module);
    const string Expected = """
      global mut $counter: int

      fn build_in_arena(@borrowed %x: int) -> int [reads_module_state, writes_module_state] {
        bb0:
          arena_enter 0
          %v = vec.new []int(%x) [region=arena(0)]
          %len = vec.len %v
          arena_exit 0
          ret %len
      }

      """;

    Assert.Equal(NormalizeLf(Expected), actual);
  }

  internal static IrModule BuildSumVecModule(bool withStep1Rc) {
    var types = new IrTypeTable();
    IrTypeId vecInt = types.GetVector(types.Int);
    var module = new IrModule(types);

    IrFunction fn = module.AddFunction(
        "sum_vec",
        [new IrParam("xs", vecInt, IrParamOwnership.Owned)],
        types.Int,
        ModuleStateEffects.Pure);

    IrSlotId sTotal = fn.AddSlot("total", types.Int, isMutSlot: true);
    IrSlotId sSnap = fn.AddSlot("snap", vecInt);
    IrSlotId sI = fn.AddSlot("i", types.Int, isMutSlot: true);
    IrSlotId sLen = fn.AddSlot("len", types.Int);

    IrTempId c0 = fn.AddTemp(types.Int, "c0");
    IrTempId t0 = fn.AddTemp(vecInt, "t0");
    IrTempId t1 = fn.AddTemp(types.Int, "t1");
    IrTempId t2 = fn.AddTemp(types.Int, "t2");
    IrTempId t3 = fn.AddTemp(types.Int, "t3");
    IrTempId t4 = fn.AddTemp(types.Bool, "t4");
    IrTempId t5 = fn.AddTemp(vecInt, "t5");
    IrTempId t6 = fn.AddTemp(types.Int, "t6");
    IrTempId x = fn.AddTemp(types.Int, "x");
    IrTempId t7 = fn.AddTemp(types.Int, "t7");
    IrTempId t8 = fn.AddTemp(types.Int, "t8");
    IrTempId c1 = fn.AddTemp(types.Int, "c1");
    IrTempId t9 = fn.AddTemp(types.Int, "t9");
    IrTempId t10 = fn.AddTemp(types.Int, "t10");
    IrTempId d0 = withStep1Rc ? fn.AddTemp(vecInt, "d0") : IrTempId.Invalid;

    IrBasicBlock bb0 = fn.AddBlock();
    IrBasicBlock bb1 = fn.AddBlock();
    IrBasicBlock bb2 = fn.AddBlock();
    IrBasicBlock bb3 = fn.AddBlock();

    IrOperand xs = IrOperand.FromParam(0);

    bb0.Instructions.Add(new ConstInt(c0, 0));
    bb0.Instructions.Add(new StoreSlot(sTotal, c0));
    if (withStep1Rc) {
      bb0.Instructions.Add(new RcInc(xs));
    }
    bb0.Instructions.Add(new StoreSlot(sSnap, xs));
    bb0.Instructions.Add(new LoadSlot(t0, sSnap));
    if (withStep1Rc) {
      bb0.Instructions.Add(new RcInc(t0));
    }
    bb0.Instructions.Add(new VecLen(t1, t0));
    if (withStep1Rc) {
      bb0.Instructions.Add(new RcDec(t0));
    }
    bb0.Instructions.Add(new StoreSlot(sLen, t1));
    bb0.Instructions.Add(new StoreSlot(sI, c0));
    bb0.Terminator = new Br(bb1.Id);

    bb1.Instructions.Add(new LoadSlot(t2, sI));
    bb1.Instructions.Add(new LoadSlot(t3, sLen));
    bb1.Instructions.Add(new Cmp(t4, IrCmpOp.Lt, t2, t3));
    bb1.Terminator = new BrIf(t4, bb2.Id, bb3.Id);

    bb2.Instructions.Add(new LoadSlot(t5, sSnap));
    if (withStep1Rc) {
      bb2.Instructions.Add(new RcInc(t5));
    }
    bb2.Instructions.Add(new LoadSlot(t6, sI));
    bb2.Instructions.Add(new VecGet(x, t5, t6, Unchecked: true));
    if (withStep1Rc) {
      bb2.Instructions.Add(new RcDec(t5));
    }
    bb2.Instructions.Add(new LoadSlot(t7, sTotal));
    bb2.Instructions.Add(new Add(t8, t7, x));
    bb2.Instructions.Add(new StoreSlot(sTotal, t8));
    bb2.Instructions.Add(new ConstInt(c1, 1));
    bb2.Instructions.Add(new Add(t9, t6, c1));
    bb2.Instructions.Add(new StoreSlot(sI, t9));
    bb2.Terminator = new Br(bb1.Id);

    bb3.Instructions.Add(new LoadSlot(t10, sTotal));
    if (withStep1Rc) {
      bb3.Instructions.Add(new LoadSlot(d0, sSnap));
      bb3.Instructions.Add(new RcDec(d0));
      bb3.Instructions.Add(new RcDec(xs));
    }
    bb3.Terminator = new Ret(t10);

    return module;
  }

  internal static IrModule BuildAreaModule() {
    var types = new IrTypeTable();
    IrTypeId shapeId = types.DeclareUnion("Shape");
    types.DefineUnionVariants(shapeId, [
      new IrUnionVariant("Circle", 0, [new IrUnionVariantField("r", types.Float)]),
      new IrUnionVariant("Rect", 1, [
        new IrUnionVariantField("w", types.Float),
        new IrUnionVariantField("h", types.Float),
      ]),
    ]);

    var module = new IrModule(types);
    IrFunction fn = module.AddFunction(
        "area",
        [new IrParam("s", shapeId, IrParamOwnership.Owned)],
        types.Float,
        ModuleStateEffects.Pure);

    IrSlotId sRes = fn.AddSlot("res", types.Float, isMutSlot: true);
    IrTempId tag = fn.AddTemp(types.Int, "tag");
    IrTempId r = fn.AddTemp(types.Float, "r");
    IrTempId pi = fn.AddTemp(types.Float, "pi");
    IrTempId t0 = fn.AddTemp(types.Float, "t0");
    IrTempId t1 = fn.AddTemp(types.Float, "t1");
    IrTempId w = fn.AddTemp(types.Float, "w");
    IrTempId h = fn.AddTemp(types.Float, "h");
    IrTempId t2 = fn.AddTemp(types.Float, "t2");
    IrTempId t3 = fn.AddTemp(types.Float, "t3");

    IrBasicBlock bb0 = fn.AddBlock();
    IrBasicBlock bb1 = fn.AddBlock();
    IrBasicBlock bb2 = fn.AddBlock();
    IrBasicBlock bb3 = fn.AddBlock();

    IrOperand s = IrOperand.FromParam(0);

    bb0.Instructions.Add(new UnionTag(tag, s));
    bb0.Terminator = new Switch(tag, [(0, bb1.Id), (1, bb2.Id)], null);

    bb1.Instructions.Add(new UnionField(r, s, 0, 0));
    bb1.Instructions.Add(new ConstFloat(pi, 3.14159));
    bb1.Instructions.Add(new Mul(t0, pi, r));
    bb1.Instructions.Add(new Mul(t1, t0, r));
    bb1.Instructions.Add(new StoreSlot(sRes, t1));
    bb1.Terminator = new Br(bb3.Id);

    bb2.Instructions.Add(new UnionField(w, s, 1, 0));
    bb2.Instructions.Add(new UnionField(h, s, 1, 1));
    bb2.Instructions.Add(new Mul(t2, w, h));
    bb2.Instructions.Add(new StoreSlot(sRes, t2));
    bb2.Terminator = new Br(bb3.Id);

    bb3.Instructions.Add(new LoadSlot(t3, sRes));
    bb3.Terminator = new Ret(t3);

    return module;
  }

  internal static IrModule BuildLogRecordModule(bool withStep1Rc) {
    var types = new IrTypeTable();
    IrTypeId vecStr = types.GetVector(types.String);
    IrTypeId logId = types.DeclareStruct("Log");
    types.DefineStructFields(logId, [new IrStructField("entries", vecStr, IsMutable: true)]);
    IrTypeId ptrLog = types.GetPtr(logId);
    IrTypeId ptrVecStr = types.GetPtr(vecStr);

    var module = new IrModule(types);
    IrFunction fn = module.AddFunction(
        "Log::record",
        [
          new IrParam("self", ptrLog, IrParamOwnership.Mut),
          new IrParam("msg", types.String, IrParamOwnership.Owned),
        ],
        types.Void,
        ModuleStateEffects.Pure);

    IrTempId p0 = fn.AddTemp(ptrVecStr, "p0");
    IrBasicBlock bb0 = fn.AddBlock();

    IrOperand self = IrOperand.FromParam(0);
    IrOperand msg = IrOperand.FromParam(1);

    bb0.Instructions.Add(new FieldAddr(p0, self, 0));
    bb0.Instructions.Add(new EnsureUnique(p0));
    if (withStep1Rc) {
      bb0.Instructions.Add(new RcInc(msg));
    }
    bb0.Instructions.Add(new VecPush(p0, msg));
    if (withStep1Rc) {
      bb0.Instructions.Add(new RcDec(msg));
    }
    bb0.Terminator = new RetVoid();

    return module;
  }

  private static void AssertValid(IrModule module) {
    IrVerificationResult result = IrVerifier.Verify(module);
    Assert.True(result.IsValid, string.Join("\n", result.Errors));
  }

  private static string NormalizeLf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
