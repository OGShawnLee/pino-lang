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
    Assert.False(graph.IsPointerFree(types.GetFunction([types.Int], types.Int)));
    Assert.False(graph.IsPointerFree(types.GetUnion(compilation.Prelude.OptionUnion, [types.String])));
  }

  [Fact]
  public void ReachabilityGraph_DetectsCycleCapableTypesAcrossMutableEdgesAndConservativeUnions() {
    const string Code = """
      struct Point {
        x int
        y float
      }

      struct Node {
        next Option[Node]
      }

      struct Tree {
        children []Tree
      }

      struct MapNode {
        edges map[string, MapNode]
      }

      struct SetNode {
        peers set[SetNode]
      }

      union NonRecUnion {
        SomeInt(int)
        Empty
      }

      union RecUnion {
        Leaf(int)
        Branch(RecUnion, RecUnion)
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
    UnionType nonRecUnion = types.GetUnion((UnionSymbol) modScope.DeclaredSymbols["NonRecUnion"]);
    UnionType recUnion = types.GetUnion((UnionSymbol) modScope.DeclaredSymbols["RecUnion"]);

    Assert.False(graph.IsCycleCapable(pointType));
    Assert.False(graph.IsCycleCapable(nonRecUnion));

    Assert.True(graph.IsCycleCapable(nodeType));
    Assert.True(graph.IsCycleCapable(optNodeType));
    Assert.True(graph.IsCycleCapable(treeType));
    Assert.True(graph.IsCycleCapable(vecTreeType));
    Assert.True(graph.IsCycleCapable(mapNodeType));
    Assert.True(graph.IsCycleCapable(setNodeType));

    // Conservative union payload edge (spec/features/unions.md has no payload immutability rule).
    Assert.True(graph.IsCycleCapable(recUnion));
  }
}
