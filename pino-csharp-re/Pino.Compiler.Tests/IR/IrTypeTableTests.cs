using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.IR;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.IR;

public sealed class IrTypeTableTests {
  [Fact]
  public void InternFromCompilation_ComputesTransitivePointerFreeRecursiveUnionsAndPassByConstPtr() {
    const string Code = """
      enum Color {
        Red
        Green
      }

      struct Point {
        x float
        y float
      }

      struct Rect {
        a Point
        b Point
      }

      struct Entity {
        hp int
        target Handle[Entity]
      }

      struct Log {
        mut entries []string
      }

      union Shape {
        Circle(r float)
        RectShape(w float, h float)
      }

      union List {
        Cons(int, List)
        Nil
      }

      struct Node {
        value int
        next Option[Node]
      }

      fn use_all(
        c Color,
        p Point,
        r Rect,
        e Entity,
        l Log,
        s Shape,
        xs List,
        n Node,
        optPt Option[Point],
        pool Pool[Point],
        m map[string, int],
        st set[int],
        cb fn (int) -> bool
      ) {}
      """;

    ParseResult parsed = Parser.Parse(new SourceText(Code, "ir_types.pino"));
    Assert.Empty(parsed.Diagnostics);

    Compilation compilation = Compilation.Create(parsed);
    Assert.Empty(compilation.Diagnostics);

    var table = new IrTypeTable(compilation.Reachability);
    TypeInterner cTypes = compilation.Types;
    Scope modScope = compilation.RootModel.GetScope(parsed.Root)!;

    IrTypeId colorId = table.Intern(cTypes.GetEnum((EnumSymbol) modScope.DeclaredSymbols["Color"]));
    IrTypeId pointId = table.Intern(cTypes.GetStruct((StructSymbol) modScope.DeclaredSymbols["Point"]));
    IrTypeId rectId = table.Intern(cTypes.GetStruct((StructSymbol) modScope.DeclaredSymbols["Rect"]));
    IrTypeId entityId = table.Intern(cTypes.GetStruct((StructSymbol) modScope.DeclaredSymbols["Entity"]));
    IrTypeId logId = table.Intern(cTypes.GetStruct((StructSymbol) modScope.DeclaredSymbols["Log"]));
    IrTypeId shapeId = table.Intern(cTypes.GetUnion((UnionSymbol) modScope.DeclaredSymbols["Shape"]));
    IrTypeId listId = table.Intern(cTypes.GetUnion((UnionSymbol) modScope.DeclaredSymbols["List"]));
    IrTypeId nodeId = table.Intern(cTypes.GetStruct((StructSymbol) modScope.DeclaredSymbols["Node"]));
    IrTypeId optPointId = table.Intern(cTypes.GetUnion(compilation.Prelude.OptionUnion, [cTypes.GetStruct((StructSymbol) modScope.DeclaredSymbols["Point"])]));
    IrTypeId optNodeId = table.Intern(cTypes.GetUnion(compilation.Prelude.OptionUnion, [cTypes.GetStruct((StructSymbol) modScope.DeclaredSymbols["Node"])]));
    IrTypeId poolPointId = table.Intern(cTypes.GetPool(cTypes.GetStruct((StructSymbol) modScope.DeclaredSymbols["Point"])));
    IrTypeId mapId = table.Intern(cTypes.GetMap(cTypes.String, cTypes.Int));
    IrTypeId setId = table.Intern(cTypes.GetSet(cTypes.Int));
    IrTypeId closureId = table.Intern(cTypes.GetFunction([new FunctionParameter(cTypes.Int, false)], cTypes.Bool));

    IrType point = table.GetType(pointId);
    Assert.True(point.IsPointerFree);
    Assert.Equal(16, point.SizeBytes);
    Assert.False(point.PassByConstPtr);

    IrType rect = table.GetType(rectId);
    Assert.True(rect.IsPointerFree);
    Assert.Equal(32, rect.SizeBytes);
    Assert.True(rect.PassByConstPtr);

    IrType entity = table.GetType(entityId);
    Assert.True(entity.IsPointerFree);
    Assert.Equal(16, entity.SizeBytes);
    Assert.False(entity.PassByConstPtr);

    IrType log = table.GetType(logId);
    Assert.False(log.IsPointerFree);
    Assert.Equal(8, log.SizeBytes);
    Assert.False(log.PassByConstPtr);

    IrType shape = table.GetType(shapeId);
    Assert.True(shape.IsPointerFree);
    Assert.False(shape.IsRecursiveUnion);
    Assert.Equal(24, shape.SizeBytes); // 4-byte tag + 4-byte pad + 16-byte (w, h) payload

    IrType optPoint = table.GetType(optPointId);
    Assert.True(optPoint.IsPointerFree);
    Assert.False(optPoint.IsRecursiveUnion);
    Assert.Equal(24, optPoint.SizeBytes);

    IrType list = table.GetType(listId);
    Assert.False(list.IsPointerFree);
    Assert.True(list.IsRecursiveUnion);
    Assert.Equal(8, list.SizeBytes);

    IrType optNode = table.GetType(optNodeId);
    Assert.False(optNode.IsPointerFree);
    Assert.True(optNode.IsRecursiveUnion);
    Assert.Equal(8, optNode.SizeBytes);

    IrType node = table.GetType(nodeId);
    Assert.False(node.IsPointerFree);
    Assert.Equal(16, node.SizeBytes); // 8-byte int + 8-byte boxed Option[Node]*

    Assert.True(table.GetType(colorId).IsPointerFree);
    Assert.False(table.GetType(poolPointId).IsPointerFree);
    Assert.False(table.GetType(mapId).IsPointerFree);
    Assert.False(table.GetType(setId).IsPointerFree);
    Assert.False(table.GetType(closureId).IsPointerFree);
  }

  [Fact]
  public void ManualConstruction_CoversAll22TypeKindsAndRejectsErrorOrUnresolvedTypes() {
    var table = new IrTypeTable();

    IrTypeId enumId = table.GetEnum("Dir", ["North", "South"]);
    IrTypeId handleId = table.GetHandle(table.Int);
    IrTypeId vecId = table.GetVector(table.Int);
    IrTypeId mapId = table.GetMap(table.String, table.Int);
    IrTypeId setId = table.GetSet(table.Rune);
    IrTypeId poolId = table.GetPool(table.Float);
    IrTypeId tupleId = table.GetTuple([new IrTupleField("b", table.Bool), new IrTupleField("a", table.Int)]);
    IrTypeId ptrId = table.GetPtr(table.Int);
    IrTypeId cellId = table.GetCell(table.Int, isCycleCapable: true);
    IrTypeId envId = table.GetEnv("Env_0", [new IrEnvField("n", cellId)], isCycleCapable: true);
    IrTypeId closureId = table.GetClosure([new IrClosureParam(ptrId, IrParamOwnership.Mut)], table.Void);
    IrTypeId structId = table.DeclareStruct("Pair");
    table.DefineStructFields(structId, [
      new IrStructField("x", table.Int, false),
      new IrStructField("y", table.Int, false),
    ]);
    IrTypeId unionId = table.DeclareUnion("Expr");
    table.DefineUnionVariants(unionId, [
      new IrUnionVariant("Lit", 0, [new IrUnionVariantField(null, table.Int)]),
      new IrUnionVariant("Neg", 1, [new IrUnionVariantField(null, unionId)]),
    ]);
    IrTypeId ifaceId = table.DeclareInterface("Named");
    table.DefineInterfaceMembers(
        ifaceId,
        [new IrInterfaceProperty("name", table.String, false)],
        [new IrInterfaceMethod("get_name", false, [], table.String)]);

    // Verify canonical tuple sorting (`a` before `b`) and pointer-free status.
    var tupleType = (IrTupleType) table.GetType(tupleId);
    Assert.Equal("a", tupleType.Fields[0].Label);
    Assert.Equal("b", tupleType.Fields[1].Label);
    Assert.True(tupleType.IsPointerFree);
    Assert.Equal(16, tupleType.SizeBytes);

    // Verify all 22 IrTypeKinds are represented across the table.
    var kinds = table.Types.Select(static t => t.Kind).ToHashSet();
    Assert.Equal(Enum.GetValues<IrTypeKind>().Length, kinds.Count);

    // Verify recursive union and cycle-capable flags.
    Assert.True(table.GetType(unionId).IsRecursiveUnion);
    Assert.False(table.GetType(unionId).IsPointerFree);
    Assert.True(table.GetType(cellId).IsCycleCapable);
    Assert.True(table.GetType(envId).IsCycleCapable);

    // Verify unresolved / error types are rejected.
    Assert.Throws<ArgumentException>(() => table.Intern(ErrorType.Instance));
    Assert.Throws<ArgumentException>(() => table.Intern(new TypeVariableType(0, default)));
    var dummySym = new EnumSymbol("E", SymbolVisibility.Public, 1, default, "a.pino");
    Assert.Throws<ArgumentException>(() => table.Intern(new PendingReturnType(dummySym)));
    var tpSym = new TypeParameterSymbol("T", 0, 2, default, "a.pino");
    Assert.Throws<ArgumentException>(() => table.Intern(tpSym.Type));
  }

  [Fact]
  public void InternFromCompilation_PropagatesCycleCapableOnMutableClosureCaptures() {
    const string Code = """
      fn make_rec() fn (int) -> int {
        var f = fn (n int) -> n
        f = fn (n int) {
          if n <= 1 {
            return 1
          }
          return n * f(n - 1)
        }
        return f
      }
      """;

    Compilation compilation = Compilation.Create(Parser.Parse(new SourceText(Code, "cycle.pino")));
    Assert.Empty(compilation.Diagnostics);

    var table = new IrTypeTable(compilation.Reachability);
    FunctionType fnType = compilation.Types.GetFunction(
        [new FunctionParameter(compilation.Types.Int, false)],
        compilation.Types.Int);
    IrTypeId closureId = table.Intern(fnType);

    Assert.True(table.GetType(closureId).IsCycleCapable);
    Assert.False(table.GetType(closureId).IsPointerFree);
  }
}
