using Pino.Compiler.Checking;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerMainPinoTests {
  private const string DesignSection33MainPino = """
    union Constant {
      Bool(b bool)
      Float(f float)
      Int(n int)
      String(str string)
    }

    @@Generic[T]
    struct Person {
      pub name string
      pub data T
    }

    val n int = 12
    val f float = 12.5
    val p Person[string] = Person {
      name: "Shawn"
      data: "China"
    }
    var nullish Option[string] = Option::None

    fn call_lambda(fun fn) {
      fun()
    }

    fn call_lambda_2(fun fn (int)) {
      fun(1)
    }

    fn call_lambda_3(fun fn (int, int) -> int) {
      return fun(1, 2)
    }

    call_lambda_3(fn (n, m) -> n + m)

    var int_map = map[int, string]

    int_map = map[int, string] {
      1 -> "Shawn"
      2 -> "Lee"
    }

    var int_set = set[int]

    int_set = set[int] { 1, 2, 3 }
    """;

  [Fact]
  public void MainPino_FromEnvironmentOrRepoRoot_ChecksWithZeroErrorsAndZeroWarnings() {
    string? envPath = Environment.GetEnvironmentVariable("PINO_MAIN_PINO");
    string targetPath = !string.IsNullOrWhiteSpace(envPath)
        ? envPath
        : Path.Combine(ParserTestHelpers.FindRepositoryRoot(), "main.pino");

    Assert.True(File.Exists(targetPath), $"Expected main.pino at '{targetPath}'.");

    string text = File.ReadAllText(targetPath);
    ParseResult parsed = Parser.Parse(new SourceText(text, targetPath));
    Assert.Empty(parsed.Diagnostics);

    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
    Assert.True(
        compilation.Diagnostics.IsEmpty,
        $"Expected 0 errors and 0 warnings on '{targetPath}', got: {string.Join("; ", compilation.Diagnostics.Select(static d => $"{d.Severity}[{d.Code}]: {d.Message}"))}");
    CheckerGenericsTests.AssertNoTypeVariablesEscape(compilation);

    // When testing the owner's §3.3 main.pino (e.g. via PINO_MAIN_PINO or in the main checkout),
    // assert the exact §3.3 semantic model types and GenericInstantiations.
    Scope? rootScope = compilation.RootModel.GetScope(parsed.Root);
    if (rootScope is not null
        && rootScope.TryLookupLocal("Person", out _)
        && rootScope.TryLookupLocal("nullish", out _)) {
      AssertDesignSection33Invariants(compilation);
    }
  }

  [Fact]
  public void DesignSection33MainPino_MatchesWorkedTraceTypesAndInstantiations() {
    Compilation compilation = CheckerTestSupport.Compile(DesignSection33MainPino, "main.pino");
    Assert.Empty(compilation.Diagnostics);
    CheckerGenericsTests.AssertNoTypeVariablesEscape(compilation);
    AssertDesignSection33Invariants(compilation);
  }

  private static void AssertDesignSection33Invariants(Compilation compilation) {
    Assert.False(compilation.RootModel.IsProgramMode);

    TypeInterner types = compilation.Types;
    Assert.Same(types.Int, CheckerTestSupport.Local(compilation, "n").Type);
    Assert.Same(types.Float, CheckerTestSupport.Local(compilation, "f").Type);
    Assert.Equal("Person[string]", CheckerTestSupport.Local(compilation, "p").Type.DisplayName);
    Assert.Equal("Option[string]", CheckerTestSupport.Local(compilation, "nullish").Type.DisplayName);
    Assert.Same(types.GetMap(types.Int, types.String), CheckerTestSupport.Local(compilation, "int_map").Type);
    Assert.Same(types.GetSet(types.Int), CheckerTestSupport.Local(compilation, "int_set").Type);

    Scope rootScope = compilation.RootModel.GetScope(compilation.RootTree.Root)!;
    var callLambda = Assert.IsType<FunctionSymbol>(rootScope.DeclaredSymbols["call_lambda"]);
    var callLambda2 = Assert.IsType<FunctionSymbol>(rootScope.DeclaredSymbols["call_lambda_2"]);
    var callLambda3 = Assert.IsType<FunctionSymbol>(rootScope.DeclaredSymbols["call_lambda_3"]);
    Assert.Same(types.Void, callLambda.ReturnType);
    Assert.Same(types.Void, callLambda2.ReturnType);
    Assert.Same(types.Int, callLambda3.ReturnType);

    LambdaExpressionSyntax lambda = Assert.Single(CheckerTestSupport.All<LambdaExpressionSyntax>(compilation));
    Assert.Same(
        types.GetFunction([types.Int, types.Int], types.Int),
        compilation.RootModel.GetType(lambda));
    Assert.Empty(compilation.RootModel.GetCaptures(lambda)!.Entries);

    // Exact concrete generic instantiations: [Person[string], Option[string]].
    Assert.Equal(2, compilation.GenericInstantiations.Length);
    Assert.Contains(
        compilation.GenericInstantiations,
        i => i.Definition.Name == "Person"
            && i.TypeArguments.Length == 1
            && ReferenceEquals(i.TypeArguments[0], types.String));
    Assert.Contains(
        compilation.GenericInstantiations,
        i => i.Definition.Name == "Option"
            && i.TypeArguments.Length == 1
            && ReferenceEquals(i.TypeArguments[0], types.String));
  }
}
