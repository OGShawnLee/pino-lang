using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerModuleAndVisibilityTests {
  [Fact]
  public void ModuleResolution_InMemory_PrefersCasePreservingThenLowercaseAndWalksUp() {
    var source = new InMemoryModuleSource();
    source.AddFile("root/modules/Geometry.pino", """
      module Geometry
      pub struct Point {
        pub x int
        pub y int
      }
      """);
    source.AddFile("root/modules/geometry.pino", """
      module geometry
      pub struct LowerPoint {
        pub z int
      }
      """);
    source.AddFile("root/modules/vault.pino", """
      module Vault
      pub fn deposit() int { return 1 }
      """);

    // Importing from nested directory walks up to root/modules/ and prefers Geometry.pino over geometry.pino.
    const string MainCode = """
      import Geometry
      import Vault
      from Geometry import Point

      fn use_both(p Geometry::Point, p2 Point) int {
        return Vault::deposit()
      }
      """;

    ParseResult parsed = Parser.Parse(new SourceText(MainCode, "root/sub/app.pino"));
    Assert.Empty(parsed.Diagnostics);

    Compilation compilation = Compilation.Create(parsed, source);
    Assert.Empty(compilation.Diagnostics);

    Assert.True(compilation.Modules.TryGetValue("root/modules/Geometry.pino", out ModuleSymbol? geoMod));
    Assert.True(geoMod!.TryGetExport("Point", out _));
    Assert.False(geoMod.TryGetExport("LowerPoint", out _));
    Assert.False(compilation.Modules.ContainsKey("root/modules/geometry.pino"));

    // Fallback to lowercased vault.pino when Vault.pino does not exist.
    Assert.True(compilation.Modules.ContainsKey("root/modules/vault.pino"));
  }

  [Fact]
  public void ModuleResolution_FileSystem_WalksParentsAndClassifiesTestFiles() {
    string tempRoot = Path.Combine(Path.GetTempPath(), $"pino-mod-test-{Guid.NewGuid():N}");
    try {
      string modulesDir = Path.Combine(tempRoot, "modules");
      string nestedDir = Path.Combine(tempRoot, "src", "nested");
      Directory.CreateDirectory(modulesDir);
      Directory.CreateDirectory(nestedDir);

      File.WriteAllText(
          Path.Combine(modulesDir, "math.pino"),
          """
          module Math
          pub struct Vec2 {
            pub x float
            pub y float
          }
          @@Testable fn debug_helper() int { return 42 }
          """);

      string testFilePath = Path.Combine(nestedDir, "math_test.pino");
      const string TestCode = """
        import Math
        from Math import debug_helper
        fn check(v Math::Vec2) {}
        """;

      ParseResult parsed = Parser.Parse(new SourceText(TestCode, testFilePath));
      Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
      Assert.Empty(compilation.Diagnostics);

      Assert.True(FileSystemModuleSource.Instance.IsTestFile("foo_test.pino"));
      Assert.True(FileSystemModuleSource.Instance.IsTestFile(Path.Combine("proj", "test", "helper.pino")));
      Assert.False(FileSystemModuleSource.Instance.IsTestFile(Path.Combine("proj", "src", "app.pino")));
    } finally {
      if (Directory.Exists(tempRoot)) {
        Directory.Delete(tempRoot, recursive: true);
      }
    }
  }

  [Fact]
  public void VisibilityRules_EnforcePubPrivateAndTestable() {
    var source = new InMemoryModuleSource();
    source.AddFile("proj/modules/Geometry.pino", """
      module Geometry
      pub struct Point {
        pub x int
        y int
      }
      struct Secret {
        value int
      }
      interface Hidden {
        fn area() int
      }
      fn hidden_fn() int { return 1 }
      @@Testable fn audit_fn() int { return 2 }
      """);

    const string NonTestCode = """
      import Geometry
      from Geometry import hidden_fn, audit_fn, missing_sym, Point

      fn bad(s Geometry::Secret, h Geometry::Hidden, m Geometry::Missing) {}
      """;

    ParseResult nonTestParsed = Parser.Parse(new SourceText(NonTestCode, "proj/main.pino"));
    Compilation nonTest = Compilation.Create(nonTestParsed, source);

    Assert.Contains(
        nonTest.Diagnostics,
        static d => d.Code == DiagnosticCodes.SymbolNotPublic && d.Message.Contains("Struct 'Geometry::Secret' is not public"));
    Assert.Contains(
        nonTest.Diagnostics,
        static d => d.Code == DiagnosticCodes.SymbolNotPublic && d.Message.Contains("Interface 'Geometry::Hidden' is not public"));
    Assert.Contains(
        nonTest.Diagnostics,
        static d => d.Code == DiagnosticCodes.ModuleMemberNotFound && d.Message.Contains("'Missing' is not defined in module 'Geometry'"));
    Assert.Contains(
        nonTest.Diagnostics,
        static d => d.Code == DiagnosticCodes.SymbolNotPublic && d.Message.Contains("does not export"));
    Assert.Contains(
        nonTest.Diagnostics,
        static d => d.Code == DiagnosticCodes.ModuleMemberNotFound && d.Message.Contains("does not export"));

    // Struct field visibility defaults to Private unless marked `pub`.
    ModuleSymbol geo = nonTest.Modules["proj/modules/Geometry.pino"];
    Assert.True(geo.TryGetExport("Point", out Symbol? pointSym));
    var pointStruct = Assert.IsType<StructSymbol>(pointSym);
    Assert.Equal(SymbolVisibility.Public, pointStruct.Fields[0].Visibility);
    Assert.Equal(SymbolVisibility.Private, pointStruct.Fields[1].Visibility);

    // D-064: @@Testable is allowed in both *_test.pino and test/*.pino files.
    const string TestCode = """
      from Geometry import audit_fn
      """;

    Compilation testBySuffix = Compilation.Create(
        Parser.Parse(new SourceText(TestCode, "proj/geom_test.pino")),
        source);
    Assert.Empty(testBySuffix.Diagnostics);

    Compilation testByDir = Compilation.Create(
        Parser.Parse(new SourceText(TestCode, "proj/test/geom.pino")),
        source);
    Assert.Empty(testByDir.Diagnostics);
  }

  [Fact]
  public void CircularImportsAndModuleMain_AreRejectedAndCachedPerCanonicalPath() {
    var source = new InMemoryModuleSource();
    source.AddFile("proj/modules/A.pino", """
      module A
      import B
      pub fn fa() int { return 1 }
      """);
    source.AddFile("proj/modules/B.pino", """
      module B
      import A
      pub fn fb() int { return 2 }
      fn main() {}
      """);

    ParseResult rootParsed = Parser.Parse(new SourceText("import A", "proj/app.pino"));
    Compilation compilation = Compilation.Create(rootParsed, source);

    Assert.Contains(compilation.Diagnostics, static d => d.Code == DiagnosticCodes.ModuleImportCycle);
    Assert.Contains(compilation.Diagnostics, static d => d.Code == DiagnosticCodes.ModuleDefinesMain);
    Assert.Equal(3, compilation.Modules.Count);

    // Missing module emits PN3004.
    Compilation missing = Compilation.Create(
        Parser.Parse(new SourceText("import NoSuchModule", "proj/missing.pino")),
        source);
    Diagnostic missingDiag = Assert.Single(missing.Diagnostics);
    Assert.Equal(DiagnosticCodes.ModuleNotFound, missingDiag.Code);
  }

  [Fact]
  public void ConformancePhase1And2Cases_MatchExpectedErrTextsAndPassPositiveCases() {
    string repoRoot = ParserTestHelpers.FindRepositoryRoot();
    string conformanceRoot = Path.Combine(repoRoot, "conformance");

    string[] phase12ErrCases = [
      "entry-point/err-explicit-main-call.pino",
      "entry-point/err-main-with-parameters.pino",
      "entry-point/err-top-level-assignment.pino",
      "entry-point/err-top-level-call.pino",
      "entry-point/err-top-level-if.pino",
      "entry-point/err-top-level-loop.pino",
      "entry-point/err-top-level-var.pino",
      "functions/err-default-parameter-typed.pino",
      "functions/err-default-parameter-typeless.pino",
      "functions/err-function-overload.pino",
      "generics/err-param-name-conflict.pino",
      "generics/err-static-instance-generic.pino",
      "lexical/err-lowercase-enum-member.pino",
      "lexical/err-lowercase-enum-name.pino",
      "lexical/err-lowercase-interface-name.pino",
      "lexical/err-lowercase-struct-name.pino",
      "lexical/err-lowercase-union-name.pino",
      "lexical/err-lowercase-union-variant.pino",
      "modules/err-from-import-private-function.pino",
      "modules/err-private-interface.pino",
      "modules/err-testable-import-from-non-test.pino",
      "structs/err-embedded-field-collision.pino",
      "types/err-void-field.pino",
      "types/err-void-parameter.pino",
    ];

    foreach (string relPath in phase12ErrCases) {
      string fullPath = Path.Combine(conformanceRoot, relPath);
      string errPath = Path.ChangeExtension(fullPath, ".err");
      string expectedText = File.ReadAllText(errPath).Trim();
      ParseResult parsed = Parser.Parse(new SourceText(File.ReadAllText(fullPath), fullPath));
      Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);

      Assert.Contains(
          compilation.Diagnostics,
          d => d.Severity == DiagnosticSeverity.Error
              && d.Message.Contains(expectedText, StringComparison.Ordinal));
    }

    // Legacy OQ-42 case: emits PN3008 (MOD-E4) for imported module defining main.
    string modMainPath = Path.Combine(conformanceRoot, "modules/err-module-main.pino");
    Compilation modMain = Compilation.Create(
        Parser.Parse(new SourceText(File.ReadAllText(modMainPath), modMainPath)),
        FileSystemModuleSource.Instance);
    Assert.Contains(modMain.Diagnostics, static d => d.Code == DiagnosticCodes.ModuleDefinesMain);

    // Positive module conformance cases pass Phases 1-2 with 0 diagnostics.
    string[] positiveModuleCases = [
      "modules/import-capitalized-file.pino",
      "modules/testable-import_test.pino",
    ];

    foreach (string relPath in positiveModuleCases) {
      string fullPath = Path.Combine(conformanceRoot, relPath);
      Compilation compilation = Compilation.Create(
          Parser.Parse(new SourceText(File.ReadAllText(fullPath), fullPath)),
          FileSystemModuleSource.Instance);
      Assert.Empty(compilation.Diagnostics);
    }
  }
}
