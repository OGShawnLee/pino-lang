using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Checking;

public sealed class TypeSystemTests {
  [Fact]
  public void PrimitiveSingletons_HaveExpectedHelpersAndDisplayNames() {
    Assert.True(PrimitiveType.Int.IsInteger);
    Assert.False(PrimitiveType.Int.IsFloat);
    Assert.True(PrimitiveType.Int.IsNumeric);
    Assert.Equal("int", PrimitiveType.Int.DisplayName);

    Assert.False(PrimitiveType.Float.IsInteger);
    Assert.True(PrimitiveType.Float.IsFloat);
    Assert.True(PrimitiveType.Float.IsNumeric);
    Assert.Equal("float", PrimitiveType.Float.DisplayName);

    Assert.False(PrimitiveType.Bool.IsNumeric);
    Assert.Equal("bool", PrimitiveType.Bool.DisplayName);
    Assert.Equal("rune", PrimitiveType.Rune.DisplayName);
    Assert.Equal("string", PrimitiveType.String.DisplayName);
    Assert.Equal("regex", PrimitiveType.Regex.DisplayName);

    Assert.Same(PrimitiveType.Int, PrimitiveType.FromKind(PrimitiveKind.Int));
    Assert.Same(PrimitiveType.Regex, PrimitiveType.FromKind(PrimitiveKind.Regex));
  }

  [Fact]
  public void SpecialSingletons_AreDistinctAndPropagateContainsError() {
    ErrorType err = ErrorType.Instance;
    NeverType never = NeverType.Instance;
    VoidType @void = VoidType.Instance;

    Assert.True(err.IsError);
    Assert.False(err.IsNever);
    Assert.False(err.IsVoid);
    Assert.True(err.ContainsError);

    Assert.True(never.IsNever);
    Assert.False(never.IsError);
    Assert.False(never.IsVoid);
    Assert.False(never.ContainsError);

    Assert.True(@void.IsVoid);
    Assert.False(@void.IsError);
    Assert.False(@void.IsNever);
    Assert.False(@void.ContainsError);

    Assert.NotSame(err, never);
    Assert.NotSame(err, @void);
    Assert.NotSame(never, @void);

    var types = new TypeInterner();
    Assert.False(types.GetVector(types.Int).ContainsError);
    Assert.True(types.GetVector(err).ContainsError);
    Assert.True(types.GetMap(types.String, err).ContainsError);
    Assert.True(types.GetSet(err).ContainsError);
    Assert.True(types.GetTuple([new TupleField("a", err)]).ContainsError);
    Assert.True(types.GetFunction([new FunctionParameter(types.Int, false)], err).ContainsError);
    Assert.True(types.GetFunction([new FunctionParameter(err, false)], types.Bool).ContainsError);

    var structSym = new StructSymbol("Box", SymbolVisibility.Public, [], 1, default, null);
    var unionSym = new UnionSymbol("Opt", SymbolVisibility.Public, [], false, 2, default, null);
    var ifaceSym = new InterfaceSymbol("Shape", SymbolVisibility.Public, [], 3, default, null);

    Assert.True(types.GetStruct(structSym, [err]).ContainsError);
    Assert.True(types.GetUnion(unionSym, [err]).ContainsError);
    Assert.True(types.GetInterface(ifaceSym, [err]).ContainsError);
  }

  [Fact]
  public void TypeInterner_InternsCompositeAndNominalTypesByStructure() {
    var types = new TypeInterner();

    Assert.Same(types.GetVector(types.Int), types.GetVector(types.Int));
    Assert.NotSame(types.GetVector(types.Int), types.GetVector(types.String));

    Assert.Same(types.GetMap(types.String, types.Int), types.GetMap(types.String, types.Int));
    Assert.NotSame(types.GetMap(types.String, types.Int), types.GetMap(types.Int, types.String));

    Assert.Same(types.GetSet(types.Rune), types.GetSet(types.Rune));
    Assert.Same(
        types.GetFunction([new FunctionParameter(types.Int, false), new FunctionParameter(types.String, false)], types.Bool),
        types.GetFunction([new FunctionParameter(types.Int, false), new FunctionParameter(types.String, false)], types.Bool));
    Assert.NotSame(
        types.GetFunction([new FunctionParameter(types.Int, false)], types.Void),
        types.GetFunction([new FunctionParameter(types.Int, false)], types.Bool));
    FunctionType mutFn = types.GetFunction([new FunctionParameter(types.Int, true), new FunctionParameter(types.String, false)], types.Void);
    FunctionType immutFn = types.GetFunction([new FunctionParameter(types.Int, false), new FunctionParameter(types.String, false)], types.Void);
    Assert.Same(
        mutFn,
        types.GetFunction([new FunctionParameter(types.Int, true), new FunctionParameter(types.String, false)], types.Void));
    Assert.NotSame(mutFn, immutFn);
    Assert.Equal("fn (mut int, string) -> void", mutFn.DisplayName);
    Assert.Equal("fn (int, string) -> void", immutFn.DisplayName);

    var structA = new StructSymbol("Point", SymbolVisibility.Public, [], 1, default, "a.pino");
    var structB = new StructSymbol("Point", SymbolVisibility.Public, [], 2, default, "b.pino");
    Assert.Same(types.GetStruct(structA), types.GetStruct(structA));
    Assert.NotSame(types.GetStruct(structA), types.GetStruct(structB));

    var enumA = new EnumSymbol("Direction", SymbolVisibility.Public, 3, default, "a.pino");
    Assert.Same(types.GetEnum(enumA), types.GetEnum(enumA));

    var unionA = new UnionSymbol("Result", SymbolVisibility.Public, [], false, 4, default, "a.pino");
    Assert.Same(
        types.GetUnion(unionA, [types.Int, types.String]),
        types.GetUnion(unionA, [types.Int, types.String]));

    var ifaceA = new InterfaceSymbol("Repo", SymbolVisibility.Public, [], 5, default, "a.pino");
    Assert.Same(
        types.GetInterface(ifaceA, [types.Int]),
        types.GetInterface(ifaceA, [types.Int]));

    var tp = new TypeParameterSymbol("T", 0, 6, default, "a.pino");
    Assert.Same(types.GetTypeParameter(tp), types.GetTypeParameter(tp));
  }

  [Fact]
  public void TupleType_CanonicalizesFieldOrderForOrderIndependentEquality() {
    var types = new TypeInterner();

    TupleType t1 = types.GetTuple([
      new TupleField("x", types.Int),
      new TupleField("y", types.String),
    ]);
    TupleType t2 = types.GetTuple([
      new TupleField("y", types.String),
      new TupleField("x", types.Int),
    ]);

    Assert.Same(t1, t2);
    Assert.Equal("@(x int, y string)", t1.DisplayName);
    Assert.Equal(["x", "y"], t1.Fields.Select(static f => f.Label));
  }

  [Fact]
  public void Scope_SupportsParentLookupDuplicateDetectionShadowingAndHoisting() {
    var root = new Scope(ScopeKind.Module);
    var fn1 = new FunctionSymbol(
        "foo",
        SymbolVisibility.Public,
        [],
        [],
        VoidType.Instance,
        ReturnInferenceState.Explicit,
        1,
        new SourceSpan(0, 3),
        "a.pino");
    var fn2 = new FunctionSymbol(
        "foo",
        SymbolVisibility.Public,
        [],
        [],
        PrimitiveType.Int,
        ReturnInferenceState.Explicit,
        2,
        new SourceSpan(10, 3),
        "a.pino");

    Assert.True(root.TryDeclareHoisted(fn1, out Symbol? existing1));
    Assert.Null(existing1);
    Assert.False(root.TryDeclareHoisted(fn2, out Symbol? existing2));
    Assert.Same(fn1, existing2);
    Assert.Single(root.HoistedDeclarations);

    var inner = new Scope(ScopeKind.Block, root);
    var local = new LocalSymbol("foo", PrimitiveType.Int, false, LocalKind.Variable, 3, default, "a.pino");
    Assert.True(inner.TryDeclare(local, out _));

    Assert.True(inner.TryLookup("foo", out Symbol? foundInner));
    Assert.Same(local, foundInner);
    Assert.True(root.TryLookup("foo", out Symbol? foundRoot));
    Assert.Same(fn1, foundRoot);
    Assert.False(inner.TryLookupLocal("bar", out _));
  }

  [Fact]
  public void TypeReachabilityGraph_ComputesPointerFreeAndCycleCapableBaseline() {
    var types = new TypeInterner();

    var enumSym = new EnumSymbol("Color", SymbolVisibility.Public, 1, default, "a.pino");
    EnumType colorEnum = types.GetEnum(enumSym);

    var pointSym = new StructSymbol("Point", SymbolVisibility.Public, [], 2, default, "a.pino");
    pointSym.Fields = [
      new FieldSymbol("x", types.Int, SymbolVisibility.Public, 0, pointSym, 3, default, "a.pino"),
      new FieldSymbol("y", types.Float, SymbolVisibility.Public, 1, pointSym, 4, default, "a.pino"),
      new FieldSymbol("c", colorEnum, SymbolVisibility.Public, 2, pointSym, 5, default, "a.pino"),
    ];
    pointSym.AllFields = pointSym.Fields;
    StructType pointType = types.GetStruct(pointSym);

    var namedSym = new StructSymbol("Named", SymbolVisibility.Public, [], 6, default, "a.pino");
    namedSym.Fields = [
      new FieldSymbol("name", types.String, SymbolVisibility.Public, 0, namedSym, 7, default, "a.pino"),
    ];
    namedSym.AllFields = namedSym.Fields;
    StructType namedType = types.GetStruct(namedSym);

    var bagSym = new StructSymbol("Bag", SymbolVisibility.Public, [], 8, default, "a.pino");
    bagSym.Fields = [
      new FieldSymbol("items", types.GetVector(types.Int), SymbolVisibility.Public, 0, bagSym, 9, default, "a.pino"),
    ];
    bagSym.AllFields = bagSym.Fields;
    StructType bagType = types.GetStruct(bagSym);

    var outerSym = new StructSymbol("Outer", SymbolVisibility.Public, [], 10, default, "a.pino");
    outerSym.Fields = [
      new FieldSymbol("pt", pointType, SymbolVisibility.Public, 0, outerSym, 11, default, "a.pino"),
    ];
    outerSym.AllFields = outerSym.Fields;
    StructType outerType = types.GetStruct(outerSym);

    var nodeSym = new StructSymbol("Node", SymbolVisibility.Public, [], 12, default, "a.pino");
    StructType nodeType = types.GetStruct(nodeSym);
    VectorType nodeVec = types.GetVector(nodeType);
    nodeSym.Fields = [
      new FieldSymbol("links", nodeVec, SymbolVisibility.Public, 0, nodeSym, 13, default, "a.pino"),
    ];
    nodeSym.AllFields = nodeSym.Fields;

    var immutableCycleA = new StructSymbol("ImmA", SymbolVisibility.Public, [], 14, default, "a.pino");
    var immutableCycleB = new StructSymbol("ImmB", SymbolVisibility.Public, [], 15, default, "a.pino");
    StructType immAType = types.GetStruct(immutableCycleA);
    StructType immBType = types.GetStruct(immutableCycleB);

    var builder = new TypeReachabilityGraph.Builder();
    builder.AddEdge(pointType, types.Int, isMutableEdge: true);
    builder.AddEdge(pointType, types.Float, isMutableEdge: true);
    builder.AddEdge(pointType, colorEnum, isMutableEdge: true);
    builder.AddEdge(namedType, types.String, isMutableEdge: true);
    builder.AddEdge(bagType, types.GetVector(types.Int), isMutableEdge: true);
    builder.AddEdge(outerType, pointType, isMutableEdge: true);
    builder.AddEdge(nodeType, nodeVec, isMutableEdge: true);
    builder.AddEdge(nodeVec, nodeType, isMutableEdge: true);
    builder.AddEdge(immAType, immBType, isMutableEdge: false);
    builder.AddEdge(immBType, immAType, isMutableEdge: false);
    TypeReachabilityGraph graph = builder.Build();

    // Pointer-free checks
    Assert.True(graph.IsPointerFree(types.Bool));
    Assert.True(graph.IsPointerFree(types.Int));
    Assert.True(graph.IsPointerFree(types.Float));
    Assert.True(graph.IsPointerFree(types.Rune));
    Assert.True(graph.IsPointerFree(colorEnum));
    Assert.True(graph.IsPointerFree(types.Void));
    Assert.True(graph.IsPointerFree(types.Never));
    Assert.True(graph.IsPointerFree(pointType));

    Assert.False(graph.IsPointerFree(types.String));
    Assert.False(graph.IsPointerFree(types.Regex));
    Assert.False(graph.IsPointerFree(types.GetVector(types.Int)));
    Assert.False(graph.IsPointerFree(types.GetMap(types.String, types.Int)));
    Assert.False(graph.IsPointerFree(types.GetSet(types.Int)));
    Assert.False(graph.IsPointerFree(types.GetFunction([], types.Void)));
    Assert.False(graph.IsPointerFree(types.Error));
    Assert.False(graph.IsPointerFree(namedType));
    Assert.False(graph.IsPointerFree(bagType));
    Assert.False(graph.IsPointerFree(outerType));

    // Cycle-capable checks (builder level: edges with isMutableEdge: true form a mutable cycle; false edges do not)
    Assert.True(graph.IsCycleCapable(nodeType));
    Assert.True(graph.IsCycleCapable(nodeVec));
    Assert.False(graph.IsCycleCapable(pointType));
    Assert.False(graph.IsCycleCapable(outerType));
    Assert.False(graph.IsCycleCapable(immAType));
    Assert.False(graph.IsCycleCapable(immBType));
  }
}
