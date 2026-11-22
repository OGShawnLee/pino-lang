using Pino.Compiler.Checking;
using Pino.Compiler.IR;

namespace Pino.Compiler.Tests.IR;

public sealed class IrVerifierTests {
  [Fact]
  public void ValidModules_ReferenceFunctionsPoolsDisjointMutCallsAndResetReuse_PassVerification() {
    // 1. All three Section 1 reference functions (pre-RC and Step 1 RC).
    AssertValid(IrPrinterGoldenTests.BuildSumVecModule(withStep1Rc: false));
    AssertValid(IrPrinterGoldenTests.BuildSumVecModule(withStep1Rc: true));
    AssertValid(IrPrinterGoldenTests.BuildAreaModule());
    AssertValid(IrPrinterGoldenTests.BuildLogRecordModule(withStep1Rc: false));
    AssertValid(IrPrinterGoldenTests.BuildLogRecordModule(withStep1Rc: true));

    // 2. Pool[T] + D-184 PtrEq + @mut CallDirect + Reset/Reuse on recursive union.
    var types = new IrTypeTable();
    IrTypeId enemyId = types.DeclareStruct("Enemy");
    types.DefineStructFields(enemyId, [new IrStructField("hp", types.Int, IsMutable: true)]);
    IrTypeId poolEnemy = types.GetPool(enemyId);
    IrTypeId handleEnemy = types.GetHandle(enemyId);
    IrTypeId ptrEnemy = types.GetPtr(enemyId);
    IrTypeId ptrInt = types.GetPtr(types.Int);
    IrTypeId ptrPool = types.GetPtr(poolEnemy);

    IrTypeId listId = types.DeclareUnion("List");
    types.DefineUnionVariants(listId, [
      new IrUnionVariant("Cons", 0, [
        new IrUnionVariantField(null, types.Int),
        new IrUnionVariantField(null, listId),
      ]),
      new IrUnionVariant("Nil", 1, []),
    ]);
    IrTypeId ptrList = types.GetPtr(listId);

    var module = new IrModule(types);
    int msgId = module.InternString("Cannot pass the same element");

    IrFunction duelFn = module.AddFunction(
        "duel",
        [
          new IrParam("a", ptrEnemy, IrParamOwnership.Mut),
          new IrParam("hp_b", ptrInt, IrParamOwnership.Mut),
        ],
        types.Void);
    duelFn.AddBlock().Terminator = new RetVoid();

    IrFunction callerFn = module.AddFunction(
        "test_pool_and_reuse",
        [
          new IrParam("h1", handleEnemy, IrParamOwnership.Owned),
          new IrParam("h2", handleEnemy, IrParamOwnership.Owned),
          new IrParam("xs", listId, IrParamOwnership.Owned),
        ],
        listId);

    IrSlotId sPool = callerFn.AddSlot("armory", poolEnemy, isMutSlot: true);
    IrTempId tPool = callerFn.AddTemp(poolEnemy);
    IrTempId pPool = callerFn.AddTemp(ptrPool);
    IrTempId slotA = callerFn.AddTemp(ptrEnemy);
    IrTempId slotB = callerFn.AddTemp(ptrEnemy);
    IrTempId hpB = callerFn.AddTemp(ptrInt);
    IrTempId alias = callerFn.AddTemp(types.Bool);
    IrTempId panicMsg = callerFn.AddTemp(types.String);
    IrTempId tok = callerFn.AddTemp(ptrList);
    IrTempId head = callerFn.AddTemp(types.Int);
    IrTempId tail = callerFn.AddTemp(listId);
    IrTempId rebuilt = callerFn.AddTemp(listId);

    IrBasicBlock bb0 = callerFn.AddBlock();
    IrBasicBlock bbPanic = callerFn.AddBlock();
    IrBasicBlock bbCall = callerFn.AddBlock();

    bb0.Instructions.Add(new PoolNew(tPool, AllocRegion.Heap));
    bb0.Instructions.Add(new StoreSlot(sPool, tPool));
    bb0.Instructions.Add(new AddrLocal(pPool, sPool));
    bb0.Instructions.Add(new EnsureUnique(pPool));
    bb0.Instructions.Add(new PoolElemAddr(slotA, pPool, IrOperand.FromParam(0), "armory"));
    bb0.Instructions.Add(new PoolElemAddr(slotB, pPool, IrOperand.FromParam(1), "armory"));
    bb0.Instructions.Add(new FieldAddr(hpB, slotB, 0));
    bb0.Instructions.Add(new PtrEq(alias, slotA, slotB));
    bb0.Terminator = new BrIf(alias, bbPanic.Id, bbCall.Id);

    bbPanic.Instructions.Add(new ConstString(panicMsg, msgId));
    bbPanic.Terminator = new Panic(panicMsg);

    bbCall.Instructions.Add(new CallDirect(null, duelFn.Id, [slotA, hpB]));
    bbCall.Instructions.Add(new UnionField(head, IrOperand.FromParam(2), 0, 0));
    bbCall.Instructions.Add(new UnionField(tail, IrOperand.FromParam(2), 0, 1));
    bbCall.Instructions.Add(new Reset(tok, IrOperand.FromParam(2)));
    bbCall.Instructions.Add(new Reuse(rebuilt, tok, 0, [head, tail], AllocRegion.Heap));
    bbCall.Terminator = new Ret(rebuilt);

    AssertValid(module);
  }

  [Fact]
  public void MissingTerminator_ReportsCfgErrorWithoutThrowing() {
    var types = new IrTypeTable();
    var module = new IrModule(types);
    IrFunction fn = module.AddFunction("no_term", [], types.Void);
    fn.AddBlock(); // Terminator intentionally null

    IrVerificationResult res = IrVerifier.Verify(module);
    Assert.False(res.IsValid);
    Assert.Contains(res.Errors, e => e.RuleCode == "IR-CFG-02" && e.Message.Contains("missing a terminator", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void InvalidTargetBlock_ReportsCfgErrorWithoutThrowing() {
    var types = new IrTypeTable();
    var module = new IrModule(types);
    IrFunction fn = module.AddFunction("bad_branch", [], types.Void);
    IrBasicBlock bb0 = fn.AddBlock();
    bb0.Terminator = new Br(new IrBlockId(99));

    IrVerificationResult res = IrVerifier.Verify(module);
    Assert.False(res.IsValid);
    Assert.Contains(res.Errors, e => e.RuleCode == "IR-CFG-03" && e.Message.Contains("bb99", StringComparison.Ordinal));
  }

  [Fact]
  public void MultiplyDefinedTemp_ReportsSsaError() {
    var types = new IrTypeTable();
    var module = new IrModule(types);
    IrFunction fn = module.AddFunction("multi_def", [], types.Int);
    IrTempId t0 = fn.AddTemp(types.Int, "t0");
    IrBasicBlock bb0 = fn.AddBlock();
    bb0.Instructions.Add(new ConstInt(t0, 1));
    bb0.Instructions.Add(new ConstInt(t0, 2));
    bb0.Terminator = new Ret(t0);

    IrVerificationResult res = IrVerifier.Verify(module);
    Assert.False(res.IsValid);
    Assert.Contains(res.Errors, e => e.RuleCode == "IR-SSA-02" && e.Message.Contains("multiply defined", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void UseNotDominatedByDef_ReportsDominanceError() {
    var types = new IrTypeTable();
    var module = new IrModule(types);
    IrFunction fn = module.AddFunction("bad_dom", [new IrParam("cond", types.Bool, IrParamOwnership.Owned)], types.Int);
    IrTempId t0 = fn.AddTemp(types.Int, "t0");

    IrBasicBlock bb0 = fn.AddBlock();
    IrBasicBlock bb1 = fn.AddBlock();
    IrBasicBlock bb2 = fn.AddBlock();

    bb0.Terminator = new BrIf(IrOperand.FromParam(0), bb1.Id, bb2.Id);

    // %t0 is defined only in bb1, so bb1 does NOT dominate bb2!
    bb1.Instructions.Add(new ConstInt(t0, 42));
    bb1.Terminator = new Br(bb2.Id);

    bb2.Terminator = new Ret(t0);

    IrVerificationResult res = IrVerifier.Verify(module);
    Assert.False(res.IsValid);
    Assert.Contains(res.Errors, e => e.RuleCode == "IR-SSA-04" && e.Message.Contains("not dominated", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void OperandTypeMismatch_ReportsTypeError() {
    var types = new IrTypeTable();
    var module = new IrModule(types);
    IrFunction fn = module.AddFunction("bad_types", [], types.Int);
    IrTempId i = fn.AddTemp(types.Int, "i");
    IrTempId f = fn.AddTemp(types.Float, "f");
    IrTempId sum = fn.AddTemp(types.Int, "sum");

    IrBasicBlock bb0 = fn.AddBlock();
    bb0.Instructions.Add(new ConstInt(i, 1));
    bb0.Instructions.Add(new ConstFloat(f, 2.0));
    bb0.Instructions.Add(new Add(sum, i, f));
    bb0.Terminator = new Ret(sum);

    IrVerificationResult res = IrVerifier.Verify(module);
    Assert.False(res.IsValid);
    Assert.Contains(res.Errors, e => e.RuleCode == "IR-TYPE-03" && e.Message.Contains("mismatch", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void RcIncAndRcDecOnPointerFreeType_ReportsRcError() {
    var types = new IrTypeTable();
    IrTypeId pointId = types.DeclareStruct("Point");
    types.DefineStructFields(pointId, [
      new IrStructField("x", types.Float, false),
      new IrStructField("y", types.Float, false),
    ]);

    var module = new IrModule(types);
    IrFunction fn = module.AddFunction(
        "bad_rc",
        [
          new IrParam("n", types.Int, IrParamOwnership.Owned),
          new IrParam("pt", pointId, IrParamOwnership.Owned),
        ],
        types.Void);

    IrBasicBlock bb0 = fn.AddBlock();
    bb0.Instructions.Add(new RcInc(IrOperand.FromParam(0)));
    bb0.Instructions.Add(new RcDec(IrOperand.FromParam(1)));
    bb0.Terminator = new RetVoid();

    IrVerificationResult res = IrVerifier.Verify(module);
    Assert.False(res.IsValid);
    Assert.Equal(2, res.Errors.Count(e => e.RuleCode == "IR-RC-01"));
  }

  [Fact]
  public void NonPtrPassedToMutParameter_ReportsMutError() {
    var types = new IrTypeTable();
    IrTypeId ptrInt = types.GetPtr(types.Int);
    IrTypeId ptrFloat = types.GetPtr(types.Float);
    var module = new IrModule(types);

    IrFunction callee = module.AddFunction(
        "inc_mut",
        [new IrParam("x", ptrInt, IrParamOwnership.Mut)],
        types.Void);
    callee.AddBlock().Terminator = new RetVoid();

    IrFunction caller = module.AddFunction(
        "bad_mut_call",
        [
          new IrParam("v", types.Int, IrParamOwnership.Owned),
          new IrParam("pf", ptrFloat, IrParamOwnership.Mut),
        ],
        types.Void);

    IrBasicBlock bb0 = caller.AddBlock();
    bb0.Instructions.Add(new CallDirect(null, callee.Id, [IrOperand.FromParam(0)]));
    bb0.Instructions.Add(new CallDirect(null, callee.Id, [IrOperand.FromParam(1)]));
    bb0.Terminator = new RetVoid();

    IrVerificationResult res = IrVerifier.Verify(module);
    Assert.False(res.IsValid);
    Assert.Equal(2, res.Errors.Count(e => e.RuleCode == "IR-MUT-01"));
  }

  [Fact]
  public void InvalidAllocRegionReference_ReportsRegionError() {
    var types = new IrTypeTable();
    IrTypeId vecInt = types.GetVector(types.Int);
    var module = new IrModule(types);

    IrFunction fn = module.AddFunction("bad_region", [], types.Void);
    IrTempId v0 = fn.AddTemp(vecInt, "v0");
    IrTempId v1 = fn.AddTemp(vecInt, "v1");

    IrBasicBlock bb0 = fn.AddBlock();
    // Arena 0 used before ArenaEnter(0)
    bb0.Instructions.Add(new VecNew(v0, [], AllocRegion.Arena(0)));
    bb0.Instructions.Add(new ArenaEnter(1));
    bb0.Instructions.Add(new VecNew(v1, [], AllocRegion.Arena(1)));
    // Missing ArenaExit(1) before RetVoid
    bb0.Terminator = new RetVoid();

    IrVerificationResult res = IrVerifier.Verify(module);
    Assert.False(res.IsValid);
    Assert.Equal(2, res.Errors.Count(e => e.RuleCode == "IR-REG-01"));
  }

  private static void AssertValid(IrModule module) {
    IrVerificationResult result = IrVerifier.Verify(module);
    Assert.True(result.IsValid, string.Join("\n", result.Errors));
  }
}
