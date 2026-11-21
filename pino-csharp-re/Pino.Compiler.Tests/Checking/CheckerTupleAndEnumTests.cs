using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerTupleAndEnumTests {
  [Fact]
  public void Tuples_TUP1ToTUP10_OrderIndependentShorthandDestructuringAndFunctionReturns() {
    Compilation compilation = Compile("""
      fn make_pair(a int, b string) @(first int, second string) {
        return @(second: b, first: a)
      }

      fn shorthand(x int, y int) @(x int, y int) {
        return @(y, x)
      }

      fn get_factory() fn () -> @(x int, y int) {
        fn generate() @(y int, x int) {
          return @(x: 1, y: 2)
        }
        return generate
      }

      fn widened() @(a float, b float) {
        return @(b: 2, a: 1.5)
      }

      val @(first, second) = make_pair(42, "ok")
      var @(second: renamed_s) = make_pair(1, "v")
      renamed_s = "updated"
      val @(y: sy, x: sx) = shorthand(3, 4)
      val gen = get_factory()
      val @(x: gx) = gen()
      val @(a: wa, b: wb) = widened()
      """);

    Assert.Empty(CheckerErrors(compilation));
    Assert.Same(compilation.Types.Int, Destructured(compilation, "first").Type);
    Assert.Same(compilation.Types.String, Destructured(compilation, "second").Type);
    Assert.Same(compilation.Types.String, Destructured(compilation, "renamed_s").Type);
    Assert.True(Destructured(compilation, "renamed_s").IsMutable);
    Assert.Same(compilation.Types.Int, Destructured(compilation, "sx").Type);
    Assert.Same(compilation.Types.Int, Destructured(compilation, "sy").Type);
    Assert.Same(compilation.Types.Int, Destructured(compilation, "gx").Type);
    Assert.Same(compilation.Types.Float, Destructured(compilation, "wa").Type);
    Assert.Same(compilation.Types.Float, Destructured(compilation, "wb").Type);
  }

  private static LocalSymbol Destructured(Compilation compilation, string name) {
    var field = All<Pino.Compiler.Syntax.DestructureFieldSyntax>(compilation)
        .Single(f => TextOf(compilation, f.VariableToken ?? f.LabelToken) == name);
    return Assert.IsType<LocalSymbol>(compilation.RootModel.GetDeclaredSymbol(field));
  }

  [Fact]
  public void Tuples_TUPE1ToTUPE7_AllErrorRulesMatchConformanceTexts() {
    // TUP-E1: tuple literal outside return
    Compilation e1 = Compile("val t = @(x: 1, y: 2)");
    Diagnostic d1 = Assert.Single(CheckerErrors(e1));
    Assert.Equal(DiagnosticCodes.TupleLiteralOutsideReturn, d1.Code);
    Assert.Contains("Tuple literals can only be used as a return value", d1.Message);

    // TUP-E2: tuple literal as call argument
    Compilation e2 = Compile("""
      fn take(t int) {}
      take(@(x: 1, y: 2))
      """);
    Diagnostic d2 = Assert.Single(CheckerErrors(e2));
    Assert.Equal(DiagnosticCodes.TupleLiteralOutsideReturn, d2.Code);
    Assert.Contains("Tuple literals can only be used as a return value", d2.Message);

    // TUP-E3: returned tuple field type mismatch
    Compilation e3 = Compile("""
      fn get_coords() @(x int, y int) {
        return @(x: 10, y: "not-an-int")
      }
      """);
    Diagnostic d3 = Assert.Single(CheckerErrors(e3));
    Assert.Equal(DiagnosticCodes.ReturnTypeMismatch, d3.Code);
    Assert.Contains("declared return type", d3.Message);

    // TUP-E4: duplicate label in tuple literal
    Compilation e4 = Compile("""
      fn get_coords() @(x int, y int) {
        return @(x: 10, x: 20)
      }
      """);
    Diagnostic d4 = Assert.Single(CheckerErrors(e4));
    Assert.Equal(DiagnosticCodes.DuplicateTupleLabel, d4.Code);
    Assert.Contains("Duplicate label 'x' in tuple literal.", d4.Message);

    // TUP-E5: destructure unknown field
    Compilation e5 = Compile("""
      fn get_coords() @(x int, y int) {
        return @(x: 10, y: 20)
      }
      val @(z) = get_coords()
      """);
    Diagnostic d5 = Assert.Single(CheckerErrors(e5));
    Assert.Equal(DiagnosticCodes.UnknownTupleField, d5.Code);
    Assert.Contains("Field 'z' does not exist in tuple type", d5.Message);

    // TUP-E6: destructure duplicate variable name
    Compilation e6 = Compile("""
      fn get_coords() @(x int, y int) {
        return @(x: 10, y: 20)
      }
      val @(x: a, y: a) = get_coords()
      """);
    Diagnostic d6 = Assert.Single(CheckerErrors(e6));
    Assert.Equal(DiagnosticCodes.DuplicateDestructureVariable, d6.Code);
    Assert.Contains("Duplicate variable name 'a' in destructuring.", d6.Message);

    // TUP-E7: function returning function with mismatched tuple labels
    Compilation e7 = Compile("""
      fn get_factory_invalid() fn () -> @(x int, y int) {
        fn generate() @(a int, b int) {
          return @(a: 1, b: 2)
        }
        return generate
      }
      """);
    Diagnostic d7 = Assert.Single(CheckerErrors(e7));
    Assert.Equal(DiagnosticCodes.ReturnTypeMismatch, d7.Code);
    Assert.Contains("declared return type", d7.Message);
  }

  [Fact]
  public void Enums_ENUM1ToENUM8AndD077_MemberAccessEqualityAndUnknownMember() {
    Compilation ok = Compile("""
      enum Direction { North South East West }
      enum LogLevel { Info, Warning, Error }

      struct Nav {
        heading Direction
      }

      fn is_north(d Direction) bool {
        return d == Direction::North
      }

      val d1 = Direction::North
      val d2 = Direction::South
      val eq = d1 == d2
      val neq = d1 != LogLevel::Info
      val nav = Nav { heading: d1 }
      val txt = str(d1)
      val kind = type(d1)
      val check = is_north(nav:heading)
      """);

    Diagnostic crossEnum = Assert.Single(CheckerErrors(ok));
    Assert.Equal(DiagnosticCodes.InvalidOperatorOperands, crossEnum.Code);
    Assert.Same(ok.Types.Bool, Local(ok, "eq").Type);
    Assert.Same(ok.Types.String, Local(ok, "txt").Type);
    Assert.Same(ok.Types.String, Local(ok, "kind").Type);
    Assert.Same(ok.Types.Bool, Local(ok, "check").Type);

    // D-077 / ENUM-E2: unknown enum member emits PN3308 "has no member".
    string repoRoot = ParserTestHelpers.FindRepositoryRoot();
    string errEnumPath = Path.Combine(repoRoot, "conformance", "enums", "err-unknown-member.pino");
    string expectedText = File.ReadAllText(Path.ChangeExtension(errEnumPath, ".err")).Trim();
    Compilation unknownMember = Compilation.Create(
        Parser.Parse(new SourceText(File.ReadAllText(errEnumPath), errEnumPath)),
        FileSystemModuleSource.Instance);

    Diagnostic err = Assert.Single(CheckerErrors(unknownMember));
    Assert.Equal(DiagnosticCodes.UnknownEnumMember, err.Code);
    Assert.Contains(expectedText, err.Message);
  }
}
