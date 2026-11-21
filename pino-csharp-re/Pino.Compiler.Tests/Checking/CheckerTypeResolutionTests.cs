using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerTypeResolutionTests {
  private static Compilation CompileSource(string code, string? filePath = "test.pino") {
    ParseResult parsed = Parser.Parse(new SourceText(code, filePath));
    Assert.Empty(parsed.Diagnostics);
    return Compilation.Create(parsed);
  }

  [Fact]
  public void VoidPositions_AllowedInReturnsAndResultSuccess_RejectedElsewhere() {
    const string AllowedCode = """
      fn log(msg string) void {}
      fn apply(cb fn (int) -> void, cb2 fn (int)) {}
      fn save() Result[void, string] {
        return Result::Success
      }
      """;

    Compilation allowed = CompileSource(AllowedCode);
    Assert.Empty(allowed.Diagnostics);

    var saveFn = Assert.IsType<FunctionDeclarationSyntax>(allowed.RootTree.Root.Items[2]);
    var saveSym = Assert.IsType<FunctionSymbol>(allowed.RootModel.GetDeclaredSymbol(saveFn));
    var resultType = Assert.IsType<UnionType>(saveSym.ReturnType);
    Assert.Same(allowed.Prelude.ResultUnion, resultType.Definition);
    Assert.Same(allowed.Types.Void, resultType.TypeArguments[0]);
    Assert.Same(allowed.Types.String, resultType.TypeArguments[1]);

    const string DisallowedCode = """
      fn bad_param(x void) {}
      struct BadField {
        f void
        vs []void
        ms map[string, void]
        ss set[void]
        opt Option[void]
        res Result[int, void]
      }
      interface BadIface {
        p void
      }
      union BadUnion {
        Pay(void)
      }
      fn bad_tuple() @(a void) {}
      """;

    Compilation disallowed = CompileSource(DisallowedCode);
    Diagnostic[] voidErrors = disallowed.Diagnostics
        .Where(static d => d.Code == DiagnosticCodes.InvalidVoidTypePosition)
        .ToArray();
    Assert.Equal(10, voidErrors.Length);
    Assert.All(voidErrors, static d => Assert.Contains("void", d.Message));
  }

  [Fact]
  public void FunctionAndTupleTypes_ResolveAndStoreSideTables() {
    const string Code = """
      fn make(a fn, b fn (int), c fn -> int, d fn (int, string) -> bool) @(x int, y string) {}
      """;

    Compilation compilation = CompileSource(Code);
    SemanticModel model = compilation.RootModel;
    Assert.Empty(model.Diagnostics);

    var fnDecl = Assert.IsType<FunctionDeclarationSyntax>(compilation.RootTree.Root.Items[0]);
    var fnSym = Assert.IsType<FunctionSymbol>(model.GetDeclaredSymbol(fnDecl));

    Assert.Same(compilation.Types.GetFunction([], compilation.Types.Void), fnSym.Parameters[0].Type);
    Assert.Same(compilation.Types.GetFunction([compilation.Types.Int], compilation.Types.Void), fnSym.Parameters[1].Type);
    Assert.Same(compilation.Types.GetFunction([], compilation.Types.Int), fnSym.Parameters[2].Type);
    Assert.Same(
        compilation.Types.GetFunction([compilation.Types.Int, compilation.Types.String], compilation.Types.Bool),
        fnSym.Parameters[3].Type);

    TupleType expectedRet = compilation.Types.GetTuple([
      new TupleField("y", compilation.Types.String),
      new TupleField("x", compilation.Types.Int),
    ]);
    Assert.Same(expectedRet, fnSym.ReturnType);
    Assert.Same(expectedRet, model.GetType(fnDecl.ReturnType!));
  }

  [Fact]
  public void GenericParameters_EnforceGenE4GenE5GenE7GenE9AndConstraints() {
    const string Code = """
      struct Document { name string }

      @@Generic[Document]
      struct BadGeneric {
        d Document
      }

      @@Generic[T is int]
      struct BadConstraint {
        x T
      }

      @@Generic[Doc]
      struct StaticLeak {
        static fn read(d Doc) {}
      }

      interface DocumentShape {
        title string
        fn summary() string
      }

      @@Generic[T]
      interface Repository {
        fn find(id int) T
      }

      struct User { name string }

      struct ValidDoc {
        title string
        fn summary() string { return title }
      }

      struct ValidRepo {
        fn find(id int) User { return User { name: "" } }
      }

      struct BadRepo {
        fn find(id string) User { return User { name: "" } }
      }

      @@Generic[Doc is DocumentShape]
      struct Holder {
        doc Doc
      }

      @@Generic[Repo is Repository[User]]
      struct RepoHolder {
        repo Repo
      }

      fn use_bad(a Holder[User], b Holder[int], c RepoHolder[BadRepo]) {}
      fn use_good(a Holder[ValidDoc], b RepoHolder[ValidRepo]) {}
      """;

    Compilation compilation = CompileSource(Code);
    Assert.Contains(
        compilation.Diagnostics,
        static d => d.Code == DiagnosticCodes.GenericParameterNameConflict
            && d.Message.Contains("conflicts with an existing defined type name"));
    Assert.Contains(
        compilation.Diagnostics,
        static d => d.Code == DiagnosticCodes.StaticMethodUsesInstanceGeneric
            && d.Message.Contains("cannot use instance-bound generic parameter"));

    Diagnostic[] constraintErrors = compilation.Diagnostics
        .Where(static d => d.Code == DiagnosticCodes.GenericConstraintNotSatisfied)
        .ToArray();
    // BadConstraint (T is int), Holder[User], Holder[int], RepoHolder[BadRepo]
    Assert.Equal(4, constraintErrors.Length);
    Assert.Equal(3, constraintErrors.Count(static d => d.Message.Contains("does not satisfy generic constraint")));
  }

  [Fact]
  public void StructEmbedding_PromotesFieldsAndMethods_OverridesAndDetectsCollisionsAndCycles() {
    const string GoodCode = """
      struct Base {
        pub id int
        secret string

        fn describe() string { return "base" }
        fn base_only() int { return id }
        static fn create() int { return 0 }
      }

      struct Derived {
        Base
        extra float

        fn describe() string { return "derived" }
        static fn helper() int { return 1 }
      }
      """;

    Compilation good = CompileSource(GoodCode);
    Assert.Empty(good.Diagnostics);

    Scope? modScope = good.RootModel.GetScope(good.RootTree.Root);
    Assert.True(modScope!.TryLookup("Derived", out Symbol? derivedFound));
    var derived = Assert.IsType<StructSymbol>(derivedFound);

    Assert.Equal(["extra"], derived.Fields.Select(static f => f.Name));
    Assert.Equal(["extra", "id", "secret"], derived.AllFields.Select(static f => f.Name));
    Assert.False(derived.AllFields[0].IsPromotedFromEmbedded);
    Assert.True(derived.AllFields[1].IsPromotedFromEmbedded);
    Assert.Single(derived.AllFields[1].EmbeddedChain);

    // Outer method `describe` overrides Base::describe; static `create` is NOT promoted.
    Assert.Equal(["describe", "helper", "base_only"], derived.Methods.Select(static m => m.Name));
    Assert.False(derived.Methods[0].IsPromotedFromEmbedded);
    Assert.True(derived.Methods[2].IsPromotedFromEmbedded);

    // MemberScope contains self, all fields, and instance methods only.
    Assert.NotNull(derived.MemberScope);
    Assert.True(derived.MemberScope!.TryLookupLocal("self", out _));
    Assert.True(derived.MemberScope.TryLookupLocal("id", out _));
    Assert.True(derived.MemberScope.TryLookupLocal("base_only", out _));
    Assert.False(derived.MemberScope.TryLookupLocal("helper", out _));

    // Instance method scope chains through MemberScope; static method scope does not.
    var derivedDecl = Assert.IsType<StructDeclarationSyntax>(good.RootTree.Root.Items[1]);
    var instanceMethod = Assert.IsType<MethodDeclarationSyntax>(derivedDecl.Members[2]);
    var staticMethod = Assert.IsType<MethodDeclarationSyntax>(derivedDecl.Members[3]);
    Assert.Same(derived.MemberScope, good.RootModel.GetScope(instanceMethod)!.Parent);
    Assert.NotSame(derived.MemberScope, good.RootModel.GetScope(staticMethod)!.Parent);

    const string CollisionAndCycleCode = """
      struct Shape {
        x int
      }
      struct Circle {
        Shape
        x int
      }
      struct A { B }
      struct B { A }
      """;

    Compilation bad = CompileSource(CollisionAndCycleCode);
    Assert.Contains(
        bad.Diagnostics,
        static d => d.Code == DiagnosticCodes.DuplicateDeclaration && d.Message.Contains("collides with"));
    Assert.Contains(
        bad.Diagnostics,
        static d => d.Code == DiagnosticCodes.TypeMismatch && d.Message.Contains("Cyclic struct embedding"));
  }

  [Fact]
  public void MissingAndErrorNodes_ProduceZeroCheckerCascades() {
    const string BrokenCode = """
      fn broken(a , b fn (int) -> ) {
        val x =
        if x {
      }
      struct Incomplete {
        f []
      }
      """;

    ParseResult parsed = Parser.Parse(new SourceText(BrokenCode, "broken.pino"));
    Assert.NotEmpty(parsed.Diagnostics);

    Compilation compilation = Compilation.Create(parsed);
    // Checker must not emit PN3003/PN3101/etc. for parser-inserted missing/error nodes.
    Assert.DoesNotContain(
        compilation.Diagnostics,
        static d => d.Code.StartsWith("PN3", StringComparison.Ordinal));
  }
}
