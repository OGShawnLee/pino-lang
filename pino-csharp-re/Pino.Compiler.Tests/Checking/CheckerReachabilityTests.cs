using Pino.Compiler.Checking;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerReachabilityTests {
  [Fact]
  public void ReachabilityGraph_ComputesPointerFreeForPrimitivesStructsEnumsAndCollections() {
    const string Code = """
      enum Color {
        Red
        Green
      }

      struct Point {
        x int
        y float
        c Color
      }

      struct Named {
        s string
      }

      struct Wrapper {
        pt Point
      }

      union ScalarUnion {
        IntVal(int)
        Empty
      }

      fn refs(
        nums []int,
        lookup map[string, int],
        uniq set[int],
        cb fn (int) -> int,
        opt Option[int],
        optStr Option[string]
      ) {}
      """;

    ParseResult parsed = Parser.Parse(new SourceText(Code, "reach.pino"));
    Assert.Empty(parsed.Diagnostics);

    Compilation compilation = Compilation.Create(parsed);
    Assert.Empty(compilation.Diagnostics);

    TypeReachabilityGraph graph = compilation.Reachability;
    TypeInterner types = compilation.Types;
    Scope modScope = compilation.RootModel.GetScope(parsed.Root)!;

    Assert.True(modScope.TryLookup("Color", out Symbol? colorSym));
    Assert.True(modScope.TryLookup("Point", out Symbol? pointSym));
    Assert.True(modScope.TryLookup("Named", out Symbol? namedSym));
    Assert.True(modScope.TryLookup("Wrapper", out Symbol? wrapperSym));
    Assert.True(modScope.TryLookup("ScalarUnion", out Symbol? scalarUnionSym));

    EnumType colorType = types.GetEnum((EnumSymbol) colorSym!);
    StructType pointType = types.GetStruct((StructSymbol) pointSym!);
    StructType namedType = types.GetStruct((StructSymbol) namedSym!);
    StructType wrapperType = types.GetStruct((StructSymbol) wrapperSym!);
    UnionType scalarUnionType = types.GetUnion((UnionSymbol) scalarUnionSym!);

    Assert.True(graph.IsPointerFree(types.Int));
    Assert.True(graph.IsPointerFree(types.Bool));
    Assert.True(graph.IsPointerFree(types.Float));
    Assert.True(graph.IsPointerFree(types.Rune));
    Assert.True(graph.IsPointerFree(colorType));
    Assert.True(graph.IsPointerFree(pointType));
    Assert.True(graph.IsPointerFree(scalarUnionType));
    Assert.True(graph.IsPointerFree(types.GetUnion(compilation.Prelude.OptionUnion, [types.Int])));

    Assert.False(graph.IsPointerFree(types.String));
    Assert.False(graph.IsPointerFree(types.Regex));
    Assert.False(graph.IsPointerFree(namedType));
    Assert.False(graph.IsPointerFree(wrapperType));
    Assert.False(graph.IsPointerFree(types.GetVector(types.Int)));
    Assert.False(graph.IsPointerFree(types.GetMap(types.String, types.Int)));
    Assert.False(graph.IsPointerFree(types.GetSet(types.Int)));
    Assert.False(graph.IsPointerFree(types.GetFunction([new FunctionParameter(types.Int, false)], types.Int)));
    Assert.False(graph.IsPointerFree(types.GetUnion(compilation.Prelude.OptionUnion, [types.String])));
  }

  [Fact]
  public void ReachabilityGraph_AD003_PureDataStructsCollectionsUnionsAndInterfacesAreAcyclic() {
    const string Code = """
      struct Point {
        x int
        y float
      }

      struct Node {
        mut next Option[Node]
        mut children []Node
      }

      struct Tree {
        mut children []Tree
      }

      struct MapNode {
        mut edges map[string, MapNode]
      }

      struct SetNode {
        mut peers set[SetNode]
      }

      struct AST {
        mut decls []Decl
      }

      struct Decl {
        mut body AST
      }

      union NonRecUnion {
        SomeInt(int)
        Empty
      }

      union Expr {
        Lit(int)
        Add(Expr, Expr)
      }

      interface NodeLike {
        mut next Option[NodeLike]
        mut fn step(mut n NodeLike) NodeLike
      }
      """;

    ParseResult parsed = Parser.Parse(new SourceText(Code, "cycles.pino"));
    Assert.Empty(parsed.Diagnostics);

    Compilation compilation = Compilation.Create(parsed);
    Assert.Empty(compilation.Diagnostics);

    TypeReachabilityGraph graph = compilation.Reachability;
    TypeInterner types = compilation.Types;
    Scope modScope = compilation.RootModel.GetScope(parsed.Root)!;

    StructType pointType = types.GetStruct((StructSymbol) modScope.DeclaredSymbols["Point"]);
    StructType nodeType = types.GetStruct((StructSymbol) modScope.DeclaredSymbols["Node"]);
    UnionType optNodeType = types.GetUnion(compilation.Prelude.OptionUnion, [nodeType]);
    StructType treeType = types.GetStruct((StructSymbol) modScope.DeclaredSymbols["Tree"]);
    VectorType vecTreeType = types.GetVector(treeType);
    StructType mapNodeType = types.GetStruct((StructSymbol) modScope.DeclaredSymbols["MapNode"]);
    StructType setNodeType = types.GetStruct((StructSymbol) modScope.DeclaredSymbols["SetNode"]);
    StructType astType = types.GetStruct((StructSymbol) modScope.DeclaredSymbols["AST"]);
    StructType declType = types.GetStruct((StructSymbol) modScope.DeclaredSymbols["Decl"]);
    VectorType vecDeclType = types.GetVector(declType);
    UnionType nonRecUnion = types.GetUnion((UnionSymbol) modScope.DeclaredSymbols["NonRecUnion"]);
    UnionType exprUnion = types.GetUnion((UnionSymbol) modScope.DeclaredSymbols["Expr"]);
    InterfaceType nodeLikeIface = types.GetInterface((InterfaceSymbol) modScope.DeclaredSymbols["NodeLike"]);

    // Under AD-003 / D-177 value semantics with CoW, all pure data edges are immutable (`isMutableEdge: false`),
    // so recursive structs, collections, mutual AST cycles, unions, and interfaces are NOT cycle-capable.
    Assert.False(graph.IsCycleCapable(pointType));
    Assert.False(graph.IsCycleCapable(nonRecUnion));
    Assert.False(graph.IsCycleCapable(nodeType));
    Assert.False(graph.IsCycleCapable(optNodeType));
    Assert.False(graph.IsCycleCapable(treeType));
    Assert.False(graph.IsCycleCapable(vecTreeType));
    Assert.False(graph.IsCycleCapable(mapNodeType));
    Assert.False(graph.IsCycleCapable(setNodeType));
    Assert.False(graph.IsCycleCapable(astType));
    Assert.False(graph.IsCycleCapable(declType));
    Assert.False(graph.IsCycleCapable(vecDeclType));
    Assert.False(graph.IsCycleCapable(exprUnion));
    Assert.False(graph.IsCycleCapable(nodeLikeIface));
  }
}
