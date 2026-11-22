using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerPoolTests {
  [Fact]
  public void PoolMembersAndOperators_AllRowsCheckCleanWithExpectedTypesAndEffects() {
    const string code = """
struct Weapon {
  pub name string
  pub mut durability int
}

fn duel(mut a Weapon, mut b Weapon) {
  a:durability -= 1
  b:durability -= 1
}

fn touch(mut p Pool[Weapon], flag bool) {
  if flag {
    p:clear()
  }
}

fn main() {
  var armory = Pool[Weapon]
  var lists = Pool[[]int]
  var fns = Pool[fn (int) -> int]
  val h = armory:add(Weapon { name: "Sword", durability: 3 })
  val g = armory:add(Weapon { name: "Axe", durability: 5 })
  val fn_h = fns:add(fn (x int) -> x + 1)
  val res = fns[fn_h](10)
  armory[h] = Weapon { name: "Longsword", durability: 4 }
  armory[h]:durability -= 1
  var nums = Pool[int]
  val nh = nums:add(10)
  nums[nh] += 5
  val in_flag = h in armory
  val not_in_flag = g not in armory
  val found = armory:get(h)
  val hs = armory:handles()
  val n1 = armory:len
  val n2 = armory:length
  val same_h = h == g
  val diff_h = h != g
  val s_h = str(h)
  var by_handle = map[Handle[Weapon], int] { h -> 1 }
  var handle_set = set[Handle[Weapon]] { h, g }
  val copy = armory
  val same_pool = copy == armory
  val diff_pool = copy != armory
  touch(mut armory, h in armory)
  val removed = armory:remove(g)
  armory:clear()
  println(res, in_flag, not_in_flag, found, hs, n1, n2, same_h, diff_h, s_h, by_handle, handle_set, same_pool, diff_pool, removed, lists:len)
}
""";

    Compilation compilation = Compile(code);
    Assert.Empty(CheckerErrors(compilation));

    StructSymbol weaponSym = All<StructDeclarationSyntax>(compilation)
        .Select(s => Assert.IsType<StructSymbol>(compilation.RootModel.GetDeclaredSymbol(s)))
        .Single(s => s.Name == "Weapon");
    StructType weaponType = compilation.Types.GetStruct(weaponSym);
    PoolType weaponPool = compilation.Types.GetPool(weaponType);
    HandleType weaponHandle = compilation.Types.GetHandle(weaponType);

    Assert.Same(weaponPool, Local(compilation, "armory").Type);
    Assert.Same(weaponHandle, Local(compilation, "h").Type);
    Assert.Same(weaponHandle, Local(compilation, "g").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "res").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "in_flag").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "not_in_flag").Type);
    Assert.Same(compilation.Types.GetUnion(compilation.Prelude.OptionUnion, [weaponType]), Local(compilation, "found").Type);
    Assert.Same(compilation.Types.GetUnion(compilation.Prelude.OptionUnion, [weaponType]), Local(compilation, "removed").Type);
    Assert.Same(compilation.Types.GetVector(weaponHandle), Local(compilation, "hs").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "n1").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "n2").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "same_h").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "diff_h").Type);
    Assert.Same(compilation.Types.String, Local(compilation, "s_h").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "same_pool").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "diff_pool").Type);
    Assert.Same(compilation.Types.GetMap(weaponHandle, compilation.Types.Int), Local(compilation, "by_handle").Type);
    Assert.Same(compilation.Types.GetSet(weaponHandle), Local(compilation, "handle_set").Type);
  }

  [Fact]
  public void PoolIteration_LOOP19AndLOOP20_BindDeeplyImmutableValueAndHandleValue() {
    const string code = """
struct Weapon {
  pub name string
  pub mut durability int
}

fn walk() {
  var armory = Pool[Weapon]
  val h = armory:add(Weapon { name: "Sword", durability: 3 })
  for w in armory {
    println(w:name)
  }
  for k, w in armory {
    println("$k $(w:name)")
  }
  for h_iter in armory:handles() {
    armory[h_iter]:durability -= 1
  }
  println(h)
}
""";

    Compilation compilation = Compile(code);
    Assert.Empty(CheckerErrors(compilation));

    List<ForStatementSyntax> loops = All<ForStatementSyntax>(compilation);
    Assert.Equal(3, loops.Count);

    StructSymbol weaponSym = All<StructDeclarationSyntax>(compilation)
        .Select(s => Assert.IsType<StructSymbol>(compilation.RootModel.GetDeclaredSymbol(s)))
        .Single();
    StructType weaponType = compilation.Types.GetStruct(weaponSym);
    HandleType weaponHandle = compilation.Types.GetHandle(weaponType);

    LoopInfo? singleLoop = compilation.RootModel.GetLoopInfo(loops[0]);
    Assert.NotNull(singleLoop);
    Assert.Equal(LoopKind.ForInPool, singleLoop!.Kind);
    Assert.Same(weaponType, singleLoop.FirstBinding!.Type);
    Assert.False(singleLoop.FirstBinding.IsMutable);
    Assert.Null(singleLoop.SecondBinding);

    LoopInfo? pairLoop = compilation.RootModel.GetLoopInfo(loops[1]);
    Assert.NotNull(pairLoop);
    Assert.Equal(LoopKind.ForInPool, pairLoop!.Kind);
    Assert.Same(weaponHandle, pairLoop.FirstBinding!.Type);
    Assert.False(pairLoop.FirstBinding.IsMutable);
    Assert.Same(weaponType, pairLoop.SecondBinding!.Type);
    Assert.False(pairLoop.SecondBinding.IsMutable);
  }

  [Fact]
  public void PoolLoopVariables_CannotBeMutated_D182() {
    const string code = """
struct Weapon {
  pub mut durability int
}

fn bad() {
  var armory = Pool[Weapon]
  for w in armory {
    w:durability -= 1
  }
}
""";

    Compilation compilation = Compile(code);
    Diagnostic err = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.CannotMutateImmutableValue, err.Code);
  }

  [Fact]
  public void PoolErrors_WrongHandleTypeNonHandleIndexConstructedHandleAndValMutation() {
    const string code = """
struct Weapon {
  pub name string
  pub mut durability int
}

fn bad() {
  var armory = Pool[Weapon]
  var nums = Pool[int]
  val nh = nums:add(42)
  val bad_int_idx = armory[0]
  val bad_handle_idx = armory[nh]
  val bad_in_val = Weapon { name: "X", durability: 1 } in armory
  val bad_ctor = Handle[Weapon]
  val frozen = Pool[Weapon]
  val h = frozen:add(Weapon { name: "Sword", durability: 3 })
  val a = armory:add(Weapon { name: "Axe", durability: 5 })
  val frozen2 = armory
  frozen2[a]:durability -= 1
  println(bad_int_idx, bad_handle_idx, bad_in_val, bad_ctor, h)
}
""";

    Compilation compilation = Compile(code);
    Diagnostic[] errors = CheckerErrors(compilation);

    Assert.Contains(errors, d => d.Code == DiagnosticCodes.InvalidIndexOperation
        && d.Message == "Pool index must have type 'Handle[Weapon]', but got 'int'.");
    Assert.Contains(errors, d => d.Code == DiagnosticCodes.InvalidIndexOperation
        && d.Message == "Pool index must have type 'Handle[Weapon]', but got 'Handle[int]'.");
    Assert.Contains(errors, d => d.Code == DiagnosticCodes.InvalidOperatorOperands
        && d.Message.Contains("Handle[Weapon]", StringComparison.Ordinal));
    Assert.Contains(errors, d => d.Code == DiagnosticCodes.CannotConstructHandleDirectly
        && d.Message == "A 'Handle[Weapon]' can't be created directly."
        && d.Help == "get one from 'add' on a 'Pool[Weapon]'");
    Assert.Equal(2, errors.Count(d => d.Code == DiagnosticCodes.CannotMutateImmutableValue));
  }

  [Fact]
  public void PoolAndHandle_ReservedNamesBareNamesWrongArityAndVoidElement() {
    const string code = """
struct Pool { x int }
enum Handle { A }
union Pool2 { A }
@@Generic[Pool]
fn bad_gen_pool(x Pool) Pool { return x }
@@Generic[Handle]
fn bad_gen_handle(x Handle) Handle { return x }

fn bad_types(a Pool, b Handle, c Pool[int, string], d Handle[int, string], e Pool[void], f Handle[void]) {
  val x = Pool
  val y = Handle
  val pool = 1
  val handle = 2
  println(x, y, pool, handle)
}
""";

    Compilation compilation = Compile(code);
    Diagnostic[] errors = CheckerErrors(compilation);

    Assert.Contains(errors, d => d.Code == DiagnosticCodes.DuplicateDeclaration
        && d.Message == "'Pool' is a built-in type and can't be declared again."
        && d.Help == "choose another name");
    Assert.Contains(errors, d => d.Code == DiagnosticCodes.DuplicateDeclaration
        && d.Message == "'Handle' is a built-in type and can't be declared again."
        && d.Help == "choose another name");
    Assert.Contains(errors, d => d.Code == DiagnosticCodes.GenericParameterNameConflict
        && d.Message.Contains("'Pool'", StringComparison.Ordinal));
    Assert.Contains(errors, d => d.Code == DiagnosticCodes.GenericParameterNameConflict
        && d.Message.Contains("'Handle'", StringComparison.Ordinal));
    Assert.Contains(errors, d => d.Code == DiagnosticCodes.CannotInferGenericArguments
        && d.Message.Contains("'Pool'", StringComparison.Ordinal));
    Assert.Contains(errors, d => d.Code == DiagnosticCodes.CannotInferGenericArguments
        && d.Message.Contains("'Handle'", StringComparison.Ordinal));
    Assert.Contains(errors, d => d.Code == DiagnosticCodes.TypeMismatch
        && d.Message == "Generic type 'Pool' expects 1 type argument(s), but got 2.");
    Assert.Contains(errors, d => d.Code == DiagnosticCodes.TypeMismatch
        && d.Message == "Generic type 'Handle' expects 1 type argument(s), but got 2.");
    Assert.Equal(2, errors.Count(d => d.Code == DiagnosticCodes.InvalidVoidTypePosition));
    Assert.Contains(errors, d => d.Code == DiagnosticCodes.UndeclaredIdentifier
        && d.Message == "Identifier 'Pool' is not declared.");
    Assert.Contains(errors, d => d.Code == DiagnosticCodes.UndeclaredIdentifier
        && d.Message == "Identifier 'Handle' is not declared.");
  }

  [Fact]
  public void PoolAndHandle_ReservedUnionAndInterfaceNames_EmitPN3002() {
    const string code = """
union Pool { A }
interface Handle { fn id() int }
""";

    Compilation compilation = Compile(code);
    Diagnostic[] errors = CheckerErrors(compilation);
    Assert.Equal(2, errors.Length);
    Assert.Equal(DiagnosticCodes.DuplicateDeclaration, errors[0].Code);
    Assert.Equal("'Pool' is a built-in type and can't be declared again.", errors[0].Message);
    Assert.Equal("choose another name", errors[0].Help);
    Assert.Equal(DiagnosticCodes.DuplicateDeclaration, errors[1].Code);
    Assert.Equal("'Handle' is a built-in type and can't be declared again.", errors[1].Message);
    Assert.Equal("choose another name", errors[1].Help);
  }

  [Fact]
  public void ValMut_VARE13_AllowsPoolAndRejectsHandle() {
    const string code = """
struct Weapon { pub name string }

fn check_val_mut() {
  val mut armory = Pool[Weapon]
  val mut h = armory:add(Weapon { name: "Sword" })
  println(h)
}
""";

    Compilation compilation = Compile(code);
    Diagnostic err = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.ValMutRequiresMutableInterior, err.Code);
    Assert.Contains("Handle[Weapon]", err.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void DisjointSlots_D184_RecordsRuntimeCheckProvesSameSymbolOverlapAndHonorsBranchFact() {
    const string code = """
struct Weapon {
  pub name string
  pub mut durability int
}

fn duel(mut a Weapon, mut b Weapon) {
  a:durability -= 1
  b:durability -= 1
}

fn run() {
  var armory = Pool[Weapon]
  val h = armory:add(Weapon { name: "Sword", durability: 3 })
  val g = armory:add(Weapon { name: "Axe", durability: 5 })
  duel(mut armory[h], mut armory[g])
  if h != g {
    duel(mut armory[h], mut armory[g])
  }
  duel(mut armory[h], mut armory[h])
}
""";

    Compilation compilation = Compile(code);
    Diagnostic err = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.OverlappingMutArguments, err.Code);

    List<CallExpressionSyntax> duelCalls = All<CallExpressionSyntax>(compilation)
        .Where(c => c.Callee is IdentifierExpressionSyntax id && TextOf(compilation, id.IdentifierToken) == "duel")
        .ToList();
    Assert.Equal(3, duelCalls.Count);

    // 1. duel(mut armory[h], mut armory[g]) outside branch fact -> 1 runtime check
    ImmutableArray<RuntimeDisjointnessCheck> checks0 = compilation.RootModel.GetRuntimeDisjointnessChecks(duelCalls[0]);
    RuntimeDisjointnessCheck check = Assert.Single(checks0);
    Assert.Equal("armory", check.RootSymbol.Name);
    Assert.Equal(MutAccessTarget.Argument(0), check.FirstAccess);
    Assert.Equal(MutAccessTarget.Argument(1), check.SecondAccess);
    Assert.Single(check.UnknownSteps);

    // 2. duel(mut armory[h], mut armory[g]) inside `if h != g` -> proven disjoint, 0 runtime checks
    Assert.Empty(compilation.RootModel.GetRuntimeDisjointnessChecks(duelCalls[1]));

    // 3. duel(mut armory[h], mut armory[h]) -> PN3226, 0 runtime checks
    Assert.Empty(compilation.RootModel.GetRuntimeDisjointnessChecks(duelCalls[2]));
  }

  [Fact]
  public void GenericsReachabilityMemoryFactsAndModuleStateEffects_SupportPoolAndHandle() {
    const string code = """
struct Node {
  pub id int
  pub next Option[Handle[Node]]
}

struct Link {
  pub target Handle[Node]
}

@@Generic[T]
fn fetch(p Pool[T], h Handle[T]) T {
  return p[h]
}

val mut world = Pool[Node]

fn spawn(id int) Handle[Node] {
  return world:add(Node { id: id, next: Option::None })
}

fn despawn(h Handle[Node]) Option[Node] {
  return world:remove(h)
}

fn wipe() {
  world:clear()
}

fn inspect(h Handle[Node]) Option[Node] {
  return world:get(h)
}

fn run() {
  val h = spawn(1)
  val n = fetch(world, h)
  println(n:id)
}
""";

    Compilation compilation = Compile(code);
    Assert.Empty(CheckerErrors(compilation));

    StructSymbol nodeSym = All<StructDeclarationSyntax>(compilation)
        .Select(s => Assert.IsType<StructSymbol>(compilation.RootModel.GetDeclaredSymbol(s)))
        .Single(s => s.Name == "Node");
    StructSymbol linkSym = All<StructDeclarationSyntax>(compilation)
        .Select(s => Assert.IsType<StructSymbol>(compilation.RootModel.GetDeclaredSymbol(s)))
        .Single(s => s.Name == "Link");
    StructType nodeType = compilation.Types.GetStruct(nodeSym);
    StructType linkType = compilation.Types.GetStruct(linkSym);
    PoolType nodePool = compilation.Types.GetPool(nodeType);
    HandleType nodeHandle = compilation.Types.GetHandle(nodeType);

    // MemoryFacts: Handle[T] and structs holding only handles/scalars are pointer-free; Pool[T] is not.
    Assert.True(compilation.Reachability.IsPointerFree(nodeHandle));
    Assert.True(compilation.Reachability.IsPointerFree(linkType));
    Assert.False(compilation.Reachability.IsPointerFree(nodePool));

    // Reachability: Pool[Node] has a non-mutable edge to Node and is acyclic.
    ImmutableArray<TypeReachabilityEdge> poolEdges = compilation.Reachability.GetEdges(nodePool);
    Assert.Contains(poolEdges, e => ReferenceEquals(e.Target, nodeType) && !e.IsMutableEdge);
    Assert.False(compilation.Reachability.IsCycleCapable(nodePool));
    Assert.False(compilation.Reachability.IsCycleCapable(nodeType));

    // Generic instantiation recorded for fetch[Node]
    Assert.Contains(
        compilation.GenericInstantiations,
        g => g.Definition is FunctionSymbol { Name: "fetch" }
            && g.TypeArguments.Length == 1
            && ReferenceEquals(g.TypeArguments[0], nodeType));

    // ModuleStateEffects for pool intrinsics
    FunctionSymbol spawnFn = All<FunctionDeclarationSyntax>(compilation)
        .Select(f => Assert.IsType<FunctionSymbol>(compilation.RootModel.GetDeclaredSymbol(f)))
        .Single(f => f.Name == "spawn");
    FunctionSymbol despawnFn = All<FunctionDeclarationSyntax>(compilation)
        .Select(f => Assert.IsType<FunctionSymbol>(compilation.RootModel.GetDeclaredSymbol(f)))
        .Single(f => f.Name == "despawn");
    FunctionSymbol wipeFn = All<FunctionDeclarationSyntax>(compilation)
        .Select(f => Assert.IsType<FunctionSymbol>(compilation.RootModel.GetDeclaredSymbol(f)))
        .Single(f => f.Name == "wipe");
    FunctionSymbol inspectFn = All<FunctionDeclarationSyntax>(compilation)
        .Select(f => Assert.IsType<FunctionSymbol>(compilation.RootModel.GetDeclaredSymbol(f)))
        .Single(f => f.Name == "inspect");

    Assert.Equal(ModuleStateEffects.ReadWrite, compilation.GetModuleStateEffects(spawnFn));
    Assert.Equal(ModuleStateEffects.ReadWrite, compilation.GetModuleStateEffects(despawnFn));
    Assert.Equal(ModuleStateEffects.WriteOnly, compilation.GetModuleStateEffects(wipeFn));
    Assert.Equal(ModuleStateEffects.ReadOnly, compilation.GetModuleStateEffects(inspectFn));
  }

  [Fact]
  public void ErrorTolerance_HalfTypedPoolAndHandle_DoNotThrowOrLeakInternalTypes() {
    string[] snippets = [
      "var p = Pool[",
      "var p = Pool[Weapon",
      "var h = Handle[",
      "var h = Handle[NoSuchType]",
      "var p = Pool[NoSuchType]\nval x = p[0]",
      "var p = Pool[]",
    ];

    foreach (string snippet in snippets) {
      Compilation compilation = CompileTolerant(snippet);
      Assert.NotNull(compilation.RootModel);
    }
  }
}
