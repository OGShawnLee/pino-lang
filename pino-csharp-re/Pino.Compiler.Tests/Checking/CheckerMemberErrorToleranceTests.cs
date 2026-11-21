using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerMemberErrorToleranceTests {
  [Theory]
  [InlineData("struct Point { x int }\nval p = Point { x: 1 }\np:")]
  [InlineData("val s = \"abc\"\ns:")]
  [InlineData("val xs = [1, 2]\nxs:")]
  [InlineData("enum Direction { North }\nDirection::")]
  [InlineData("struct Helper { static fn make() int { return 1 } }\nHelper::")]
  [InlineData("struct Point { x int, y int }\nval p = Point { x: 1,")]
  [InlineData("struct Point { x int, y int }\nval p = Point { x: ")]
  [InlineData("fn foo(a int, b int) {}\nfoo(1,")]
  [InlineData("fn foo(")]
  [InlineData("struct S { fn m(a int")]
  public void RemainingHalfTypedShapes_A2_ProduceZeroCheckerErrors(string source) {
    Compilation compilation = CompileTolerant(source);
    Assert.Empty(CheckerErrors(compilation));
  }

  [Fact]
  public void IncompleteMemberAndStaticAccess_PreserveTargetTypeAndSymbolForLspCompletion() {
    // `obj:`: Target keeps its StructType even while MemberToken is missing.
    Compilation memberComp = CompileTolerant("""
      struct Point { x int }
      val p = Point { x: 1 }
      p:
      """);
    Assert.Empty(CheckerErrors(memberComp));
    MemberAccessExpressionSyntax member = All<MemberAccessExpressionSyntax>(memberComp).Single();
    TypeSymbol? targetType = memberComp.RootModel.GetType(member.Target);
    Assert.IsType<StructType>(targetType);
    Assert.Equal("Point", ((StructType) targetType!).Definition.Name);
    Assert.True(memberComp.RootModel.GetType(member)!.IsError);

    // `Mod::`: Target keeps its ModuleSymbol in _referencedSymbols even while MemberToken is missing.
    var modSource = new InMemoryModuleSource();
    modSource.AddFile("proj/modules/Math.pino", """
      module Math
      pub fn add(a int, b int) int { return a + b }
      """);

    Compilation modComp = Compilation.Create(
        Parser.Parse(new SourceText("import Math\nMath::", "proj/app.pino")),
        modSource);
    Assert.Empty(CheckerErrors(modComp));
    StaticAccessExpressionSyntax modAccess = All<StaticAccessExpressionSyntax>(modComp).Single();
    Assert.IsType<ModuleSymbol>(modComp.RootModel.GetReferencedSymbol(modAccess.Target));
    Assert.True(modComp.RootModel.GetType(modAccess)!.IsError);

    // `Enum::`: Target keeps its EnumSymbol in _referencedSymbols even while MemberToken is missing.
    Compilation enumComp = CompileTolerant("enum Color { Red }\nColor::");
    Assert.Empty(CheckerErrors(enumComp));
    StaticAccessExpressionSyntax enumAccess = All<StaticAccessExpressionSyntax>(enumComp).Single();
    Assert.IsType<EnumSymbol>(enumComp.RootModel.GetReferencedSymbol(enumAccess.Target));
    Assert.True(enumComp.RootModel.GetType(enumAccess)!.IsError);
  }

  [Fact]
  public void ErrorTypeAbsorption_A4_InCallsMemberAccessAndStructLiterals_SuppressesCascades() {
    Compilation compilation = Compile("""
      struct Box {
        x int
        fn get() int { return x }
      }

      fn take(b Box) int { return b:get() }

      val bad = missing_var
      val b1 = Box { x: bad }
      val r1 = b1:get()
      val r2 = take(bad)
      val r3 = bad:anything(1, 2)
      val r4 = bad:prop
      """);

    Diagnostic error = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.UndeclaredIdentifier, error.Code);
    Assert.Same(compilation.Types.Int, Local(compilation, "r1").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "r2").Type);
    Assert.True(Local(compilation, "r3").Type.IsError);
    Assert.True(Local(compilation, "r4").Type.IsError);
  }
}
