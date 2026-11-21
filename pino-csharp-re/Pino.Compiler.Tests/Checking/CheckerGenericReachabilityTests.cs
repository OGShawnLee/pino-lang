using Pino.Compiler.Checking;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerGenericReachabilityTests {
  [Fact]
  public void GenericInstantiations_PointerFreeAndCycleCapableReflectConcreteArguments() {
    const string Code = """
      @@Generic[T]
      struct Box {
        item T
      }

      struct Node {
        next Option[Node]
      }

      @@Generic[T]
      struct GenericNode {
        value T
        next Option[GenericNode[T]]
      }

      fn save_code() Result[void, int] {
        return Result::Success
      }

      fn save_msg() Result[void, string] {
        return Result::Success
      }

      val bInt = Box { item: 42 }
      val bStr = Box { item: "hello" }
      val n = Node { next: Option::None }
      val gn = GenericNode { value: 1, next: Option::None }
      """;

    Compilation compilation = CheckerTestSupport.Compile(Code);
    Assert.Empty(compilation.Diagnostics);
    CheckerGenericsTests.AssertNoTypeVariablesEscape(compilation);

    TypeReachabilityGraph graph = compilation.Reachability;
    TypeInterner types = compilation.Types;

    TypeSymbol boxInt = CheckerTestSupport.Local(compilation, "bInt").Type;
    TypeSymbol boxStr = CheckerTestSupport.Local(compilation, "bStr").Type;
    TypeSymbol nodeType = CheckerTestSupport.Local(compilation, "n").Type;
    TypeSymbol optNodeType = types.GetUnion(compilation.Prelude.OptionUnion, [nodeType]);
    TypeSymbol genericNodeInt = CheckerTestSupport.Local(compilation, "gn").Type;
    TypeSymbol optGenericNodeInt = types.GetUnion(compilation.Prelude.OptionUnion, [genericNodeInt]);
    UnionType resVoidInt = types.GetUnion(compilation.Prelude.ResultUnion, [types.Void, types.Int]);
    UnionType resVoidStr = types.GetUnion(compilation.Prelude.ResultUnion, [types.Void, types.String]);

    // Concrete generic Box[int] is pointer-free; Box[string] is not.
    Assert.True(graph.IsPointerFree(boxInt));
    Assert.False(graph.IsPointerFree(boxStr));
    Assert.False(graph.IsCycleCapable(boxInt));
    Assert.False(graph.IsCycleCapable(boxStr));

    // Result[void, int] is pointer-free; Result[void, string] is not.
    Assert.True(graph.IsPointerFree(resVoidInt));
    Assert.False(graph.IsPointerFree(resVoidStr));

    // AD-003 / D-177: recursive data structs and Option[Node] form acyclic value trees (`IsCycleCapable == false`).
    Assert.False(graph.IsCycleCapable(nodeType));
    Assert.False(graph.IsCycleCapable(optNodeType));
    Assert.False(graph.IsCycleCapable(genericNodeInt));
    Assert.False(graph.IsCycleCapable(optGenericNodeInt));
    Assert.False(graph.IsPointerFree(nodeType));
    Assert.False(graph.IsPointerFree(genericNodeInt));
  }

  [Fact]
  public void ClosureEnvironments_MutableVarCapturesFormCyclesWhileImmutableScalarCapturesDoNot() {
    // 1. Closure capturing only an immutable scalar `val`: immutable edge, NOT cycle-capable.
    const string ImmutableScalarCode = """
      fn make_adder(k int) fn (int) -> int {
        val offset = k + 1
        return fn (x int) -> x + offset
      }
      """;
    Compilation scalarComp = CheckerTestSupport.Compile(ImmutableScalarCode);
    Assert.Empty(scalarComp.Diagnostics);
    FunctionType intToInt = scalarComp.Types.GetFunction([new FunctionParameter(scalarComp.Types.Int, false)], scalarComp.Types.Int);
    Assert.False(scalarComp.Reachability.IsCycleCapable(intToInt));
    Assert.Contains(
        scalarComp.Reachability.GetEdges(intToInt),
        e => ReferenceEquals(e.Target, scalarComp.Types.Int) && !e.IsMutableEdge);

    // 2. Recursive closure capturing a mutable `var` holding the closure itself (ticket 032 §2c, D-056/FN-21).
    const string RecursiveVarClosureCode = """
      fn main() {
        var count_down = fn (n int) -> n
        count_down = fn (n int) {
          if n == 0 {
            return 0
          }
          return count_down(n - 1)
        }
        println(count_down(3))
      }
      """;
    Compilation recComp = CheckerTestSupport.Compile(RecursiveVarClosureCode);
    Assert.Empty(recComp.Diagnostics);
    FunctionType recFnType = recComp.Types.GetFunction([new FunctionParameter(recComp.Types.Int, false)], recComp.Types.Int);
    Assert.Contains(
        recComp.Reachability.GetEdges(recFnType),
        e => ReferenceEquals(e.Target, recFnType) && e.IsMutableEdge);
    Assert.True(recComp.Reachability.IsCycleCapable(recFnType));

    // 3. Closure stored in a struct field that captures a local `var` of that struct (D-056 + AD-003).
    const string StructClosureCycleCode = """
      struct Button {
        label string
        mut on_click fn () -> string
      }

      fn main() {
        var b = Button { label: "ok", on_click: fn () -> "" }
        b:on_click = fn () -> b:label
        println(b:on_click())
      }
      """;
    Compilation btnComp = CheckerTestSupport.Compile(StructClosureCycleCode);
    Assert.Empty(btnComp.Diagnostics);
    TypeSymbol buttonType = CheckerTestSupport.Local(btnComp, "b").Type;
    FunctionType onClickType = btnComp.Types.GetFunction([], btnComp.Types.String);
    Assert.Contains(
        btnComp.Reachability.GetEdges(onClickType),
        e => ReferenceEquals(e.Target, buttonType) && e.IsMutableEdge);
    Assert.True(btnComp.Reachability.IsCycleCapable(buttonType));
    Assert.True(btnComp.Reachability.IsCycleCapable(onClickType));

    // 4. Closure stored in a `val` struct that captures an immutable `val` of that struct: NOT cycle-capable.
    const string ImmutableStructClosureCode = """
      struct ReadOnlyBtn {
        label string
        on_click fn () -> string
      }

      fn main() {
        val snap = ReadOnlyBtn { label: "ok", on_click: fn () -> "" }
        val btn = ReadOnlyBtn { label: "ok", on_click: fn () -> snap:label }
        println(btn:on_click())
      }
      """;
    Compilation immBtnComp = CheckerTestSupport.Compile(ImmutableStructClosureCode);
    Assert.Empty(immBtnComp.Diagnostics);
    TypeSymbol roBtnType = CheckerTestSupport.Local(immBtnComp, "btn").Type;
    FunctionType roClickType = immBtnComp.Types.GetFunction([], immBtnComp.Types.String);
    Assert.False(immBtnComp.Reachability.IsCycleCapable(roBtnType));
    Assert.False(immBtnComp.Reachability.IsCycleCapable(roClickType));
  }
}
