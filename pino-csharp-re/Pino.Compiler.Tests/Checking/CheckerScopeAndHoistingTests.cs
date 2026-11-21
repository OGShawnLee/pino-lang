using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerScopeAndHoistingTests {
  private static Compilation CompileSource(string code, string? filePath = "test.pino") {
    ParseResult parsed = Parser.Parse(new SourceText(code, filePath));
    Assert.Empty(parsed.Diagnostics);
    return Compilation.Create(parsed);
  }

  [Fact]
  public void ModuleAndNestedBlocks_HoistDeclarationsButNotValOrVar() {
    const string Code = """
      val top = helper()

      fn helper() int {
        val inner = local_fn()
        fn local_fn() int {
          val x = 1
          return x
        }
        return inner
      }

      struct Box {
        item Item
      }

      enum Color {
        Red
        Blue
      }

      union Item {
        Num(int)
        Empty
      }

      interface HasColor {
        color Color
      }
      """;

    Compilation compilation = CompileSource(Code);
    SemanticModel model = compilation.RootModel;
    Assert.Empty(model.Diagnostics);

    Scope? moduleScope = model.GetScope(compilation.RootTree.Root);
    Assert.NotNull(moduleScope);
    Assert.Equal(["helper", "Box", "Color", "Item", "HasColor"], moduleScope!.HoistedDeclarations.Select(static s => s.Name));
    Assert.False(moduleScope.TryLookupLocal("top", out _));

    var helperDecl = Assert.IsType<FunctionDeclarationSyntax>(compilation.RootTree.Root.Items[1]);
    Symbol? helperSym = model.GetDeclaredSymbol(helperDecl);
    Assert.IsType<FunctionSymbol>(helperSym);

    Scope? helperBodyScope = model.GetScope(helperDecl.Body);
    Assert.NotNull(helperBodyScope);
    Assert.Equal(["local_fn"], helperBodyScope!.HoistedDeclarations.Select(static s => s.Name));
    Assert.False(helperBodyScope.TryLookupLocal("inner", out _));
  }

  [Fact]
  public void NestedControlFlowAndStandaloneBlocks_GetOwnScopesAndHoistLocally() {
    const string Code = """
      fn demo(flag bool, xs []int) {
        {
          val r = block_fn()
          fn block_fn() int { return 1 }
        }
        if flag {
          fn then_fn() int { return 2 }
        } else {
          fn else_fn() int { return 3 }
        }
        for x in xs {
          fn loop_fn() int { return 4 }
        }
        val f = fn () {
          fn lambda_fn() int { return 5 }
        }
        match 1 {
          when 1 {
            fn arm_fn() int { return 6 }
          }
          else {
            fn else_arm_fn() int { return 7 }
          }
        }
      }

      test "hoist in test" {
        fn test_fn() int { return 8 }
      }
      """;

    Compilation compilation = CompileSource(Code);
    SemanticModel model = compilation.RootModel;
    Assert.Empty(model.Diagnostics);

    var fnDecl = Assert.IsType<FunctionDeclarationSyntax>(compilation.RootTree.Root.Items[0]);
    Scope? bodyScope = model.GetScope(fnDecl.Body);
    Assert.NotNull(bodyScope);

    // Standalone block scope (D-161) does not leak into fn body scope.
    var standaloneBlock = Assert.IsType<BlockStatementSyntax>(fnDecl.Body.Items[0]);
    Scope? standaloneScope = model.GetScope(standaloneBlock);
    Assert.NotNull(standaloneScope);
    Assert.Same(bodyScope, standaloneScope!.Parent);
    Assert.True(standaloneScope.TryLookupLocal("block_fn", out _));
    Assert.False(bodyScope!.TryLookupLocal("block_fn", out _));

    var ifStmt = Assert.IsType<IfStatementSyntax>(fnDecl.Body.Items[1]);
    Assert.NotNull(model.GetScope(ifStmt));
    Assert.True(model.GetScope(ifStmt.ThenBranch)!.TryLookupLocal("then_fn", out _));
    Assert.True(model.GetScope(Assert.IsType<BlockStatementSyntax>(ifStmt.ElseBranch))!.TryLookupLocal("else_fn", out _));

    var forStmt = Assert.IsType<ForStatementSyntax>(fnDecl.Body.Items[2]);
    Assert.NotNull(model.GetScope(forStmt));
    Assert.True(model.GetScope(forStmt.Body)!.TryLookupLocal("loop_fn", out _));

    var valLambda = Assert.IsType<VariableDeclarationSyntax>(fnDecl.Body.Items[3]);
    var lambda = Assert.IsType<LambdaExpressionSyntax>(valLambda.Initializer);
    Assert.NotNull(model.GetScope(lambda));
    Assert.True(model.GetScope(lambda.BodyBlock!)!.TryLookupLocal("lambda_fn", out _));

    var matchStmt = Assert.IsType<MatchStatementSyntax>(fnDecl.Body.Items[4]);
    var whenArm = Assert.IsType<WhenArmSyntax>(matchStmt.Arms[0]);
    var elseArm = Assert.IsType<ElseArmSyntax>(matchStmt.Arms[1]);
    Assert.NotNull(model.GetScope(whenArm));
    Assert.NotNull(model.GetScope(elseArm));
    Assert.True(model.GetScope(whenArm.BlockBody!)!.TryLookupLocal("arm_fn", out _));
    Assert.True(model.GetScope(elseArm.BlockBody!)!.TryLookupLocal("else_arm_fn", out _));

    var testDecl = Assert.IsType<TestDeclarationSyntax>(compilation.RootTree.Root.Items[1]);
    Assert.NotNull(model.GetScope(testDecl));
    Assert.True(model.GetScope(testDecl.Body)!.TryLookupLocal("test_fn", out _));
  }

  [Fact]
  public void DuplicateDeclarationsInSameScope_EmitPN3002WithRelatedLocationAndKeepFirst() {
    const string Code = """
      fn add(a int, b int) int { return a + b }
      fn add(a int, b int, c int) int { return a + b + c }

      struct Point {
        x int
        x string
      }

      enum Dir {
        North
        North
      }

      union Choice {
        First
        First
      }

      fn dup_params(a int, a string) {}
      """;

    Compilation compilation = CompileSource(Code);
    Diagnostic[] dups = compilation.Diagnostics
        .Where(static d => d.Code == DiagnosticCodes.DuplicateDeclaration)
        .ToArray();

    Assert.Equal(5, dups.Length);
    foreach (Diagnostic diag in dups) {
      Assert.Contains("already declared", diag.Message);
      Assert.Single(diag.Related);
    }

    // First function symbol stays registered in module scope (A4 non-cascading).
    Scope? moduleScope = compilation.RootModel.GetScope(compilation.RootTree.Root);
    Assert.True(moduleScope!.TryLookup("add", out Symbol? sym));
    var firstAdd = Assert.IsType<FunctionSymbol>(sym);
    Assert.Equal(2, firstAdd.Parameters.Length);
  }

  [Fact]
  public void CapitalizationRule_EnforcesD014OnNominalTypesMembersAndVariantsOnly() {
    const string BadCode = """
      struct lower_struct { x int }
      interface lower_iface { fn f() }
      enum lower_enum { lower_member }
      union lower_union { lower_variant }
      """;

    Compilation bad = CompileSource(BadCode);
    Diagnostic[] casingErrors = bad.Diagnostics
        .Where(static d => d.Code == DiagnosticCodes.InvalidTypeNameCasing)
        .ToArray();
    Assert.Equal(6, casingErrors.Length);
    Assert.All(casingErrors, static d => Assert.Contains("capitalized", d.Message));

    // D-014 / LEX-21 / D-005 (LEX-22): free forms and C keywords are valid.
    const string GoodCode = """
      val GLOBAL_LIMIT = 100
      val snake_case_1 = 1
      struct HTTPServer2 {
        register_id int
      }
      fn my_helper(char int, double float, register bool) {}
      """;

    Compilation good = CompileSource(GoodCode);
    Assert.Empty(good.Diagnostics);
  }

  [Fact]
  public void ProgramMode_ValidatesTopLevelItemsAndMainParameters() {
    const string ValidProgram = """
      val MAX = 10
      struct Item { x int }
      fn helper() int { return MAX }
      fn main() {
        val y = helper()
      }
      """;

    Compilation valid = CompileSource(ValidProgram);
    Assert.True(valid.RootModel.IsProgramMode);
    Assert.Empty(valid.Diagnostics);

    const string ScriptCode = """
      var count = 0
      count = count + 1
      """;

    Compilation script = CompileSource(ScriptCode);
    Assert.False(script.RootModel.IsProgramMode);
    Assert.Empty(script.Diagnostics);

    // MAIN-E1, MAIN-E2, MAIN-E3
    const string InvalidProgram = """
      var counter = 0
      counter = 1
      if true {}
      for 3 {}
      fn main() {}
      main()
      """;

    Compilation invalid = CompileSource(InvalidProgram);
    Assert.True(invalid.RootModel.IsProgramMode);
    Diagnostic[] topLevelErrors = invalid.Diagnostics
        .Where(static d => d.Code == DiagnosticCodes.InvalidTopLevelInProgramMode)
        .ToArray();
    Assert.Equal(5, topLevelErrors.Length);
    Assert.Contains(topLevelErrors, static d => d.Message.Contains("must be declared with 'val'"));
    Assert.Equal(4, topLevelErrors.Count(static d => d.Message.Contains("not allowed at the top level when a 'main' function is defined")));

    // MAIN-E4 / D-036 interim rule
    const string MainWithParams = """
      fn main(args []string) {}
      """;

    Compilation mainParams = CompileSource(MainWithParams);
    Diagnostic mainParamDiag = Assert.Single(mainParams.Diagnostics);
    Assert.Equal(DiagnosticCodes.MainFunctionHasParameters, mainParamDiag.Code);
    Assert.Contains("cannot take parameters", mainParamDiag.Message);
  }

  [Fact]
  public void ParameterDefaultValues_AreRejectedWithPN3113() {
    const string Code = """
      fn scale(x int, factor int = 2) int {
        return x * factor
      }
      fn bump(x = 1) int {
        return x + 1
      }
      """;

    Compilation compilation = CompileSource(Code);
    Diagnostic[] defaults = compilation.Diagnostics
        .Where(static d => d.Code == DiagnosticCodes.DefaultParameterNotSupported)
        .ToArray();
    Assert.Equal(2, defaults.Length);
    Assert.All(defaults, static d => Assert.Contains("default value", d.Message));
  }
}
