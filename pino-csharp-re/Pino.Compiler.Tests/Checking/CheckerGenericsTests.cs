using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Parsing;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerGenericsTests {
  internal static void AssertNoTypeVariablesEscape(Compilation compilation) {
    foreach (SemanticModel model in new[] { compilation.RootModel }) {
      foreach (SyntaxNode node in CheckerTestSupport.Descendants<SyntaxNode>(model.Tree.Root)) {
        if (model.GetType(node) is { } type) {
          Assert.False(
              ContainsTypeVariable(type),
              $"TypeVariableType escaped into SemanticModel.GetType({node.GetType().Name}#{node.Id}): {type.DisplayName}");
        }

        if (model.GetConversion(node) is { } conv) {
          Assert.False(ContainsTypeVariable(conv.TargetType));
          if (conv.TargetInterface is { } ti) {
            Assert.False(ContainsTypeVariable(ti));
          }
          if (conv.ImplicitLambdaType is { } ilt) {
            Assert.False(ContainsTypeVariable(ilt));
          }
        }

        if (node is CallExpressionSyntax call
            && model.GetCallResolution(call) is { ConstructedUnionType: { } ut }) {
          Assert.False(ContainsTypeVariable(ut));
        }
      }
    }

    foreach (GenericInstantiation inst in compilation.GenericInstantiations) {
      foreach (TypeSymbol arg in inst.TypeArguments) {
        Assert.False(ContainsTypeVariable(arg), $"TypeVariableType escaped into GenericInstantiations: {arg.DisplayName}");
        Assert.True(Unifier.IsConcreteType(arg), $"Non-concrete type in GenericInstantiations: {arg.DisplayName}");
      }
    }
  }

  private static bool ContainsTypeVariable(TypeSymbol type) => Unifier.ContainsTypeVariable(type);

  [Fact]
  public void GenericStructs_ExplicitInferredAndContextualInstantiation() {
    const string Code = """
      @@Generic[Key, Value]
      struct Pair {
        key Key
        value Value
      }

      @@Generic[T]
      struct Person {
        pub name string
        pub data T
      }

      val p1 = Pair[string, int] { key: "Hello", value: 42 }
      val p2 = Pair { key: "World", value: 99 }
      val p3 Person[string] = Person { name: "Shawn", data: "China" }
      val k1 = p1:key
      val v1 = p1:value
      val k2 = p2:key
      val v2 = p2:value
      val d3 = p3:data
      """;

    Compilation compilation = CheckerTestSupport.Compile(Code);
    Assert.Empty(compilation.Diagnostics);
    AssertNoTypeVariablesEscape(compilation);

    Assert.Same(compilation.Types.String, CheckerTestSupport.Local(compilation, "k1").Type);
    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "v1").Type);
    Assert.Same(compilation.Types.String, CheckerTestSupport.Local(compilation, "k2").Type);
    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "v2").Type);
    Assert.Same(compilation.Types.String, CheckerTestSupport.Local(compilation, "d3").Type);

    // Deduplicated GenericInstantiations: Pair[string, int] appears once even though instantiated twice.
    var pairInsts = compilation.GenericInstantiations
        .Where(static i => i.Definition.Name == "Pair")
        .ToArray();
    Assert.Single(pairInsts);
    Assert.Equal(2, pairInsts[0].TypeArguments.Length);
    Assert.Same(compilation.Types.String, pairInsts[0].TypeArguments[0]);
    Assert.Same(compilation.Types.Int, pairInsts[0].TypeArguments[1]);

    var personInsts = compilation.GenericInstantiations
        .Where(static i => i.Definition.Name == "Person")
        .ToArray();
    Assert.Single(personInsts);
    Assert.Same(compilation.Types.String, personInsts[0].TypeArguments[0]);
  }

  [Fact]
  public void GenericFunctionsMethodsAndHigherOrderLambdas_ResolveAndRecordInstantiations() {
    const string Code = """
      @@Generic[T]
      fn identity(v T) T {
        return v
      }

      @@Generic[T, U]
      fn map_all(list []T, transform fn (T) -> U) []U {
        var out = []U
        for x in list {
          out:push(transform(x))
        }
        return out
      }

      struct Printer {
        @@Generic[T]
        fn show(v T) string {
          return "<" + str(v) + ">"
        }
      }

      struct Reader {
        @@Generic[T]
        static fn read(v T) T {
          return v
        }
      }

      val a = identity[int](42)
      val b = identity("hi")
      val nums = [1, 2, 3]
      val mapped1 = map_all(nums, fn (x) -> "$x")
      val mapped2 = map_all(nums, it * 2)
      val std1 = [1, 2, 3]:map(fn (x) -> "$x")
      val std2 = [1, 2]:map(fn (x, i) -> x + i)
      val std3 = [1, 2, 3]:map[string](fn (x) -> "$x")
      val stdIt = [1, 2, 3]:map(it * 10)
      val p = Printer {}
      val s1 = p:show[int](5)
      val s2 = p:show("x")
      val r1 = Reader::read[int](42)
      val r2 = Reader::read("text")
      """;

    Compilation compilation = CheckerTestSupport.Compile(Code);
    Assert.Empty(compilation.Diagnostics);
    AssertNoTypeVariablesEscape(compilation);

    TypeInterner types = compilation.Types;
    Assert.Same(types.Int, CheckerTestSupport.Local(compilation, "a").Type);
    Assert.Same(types.String, CheckerTestSupport.Local(compilation, "b").Type);
    Assert.Same(types.GetVector(types.String), CheckerTestSupport.Local(compilation, "mapped1").Type);
    Assert.Same(types.GetVector(types.Int), CheckerTestSupport.Local(compilation, "mapped2").Type);
    Assert.Same(types.GetVector(types.String), CheckerTestSupport.Local(compilation, "std1").Type);
    Assert.Same(types.GetVector(types.Int), CheckerTestSupport.Local(compilation, "std2").Type);
    Assert.Same(types.GetVector(types.String), CheckerTestSupport.Local(compilation, "std3").Type);
    Assert.Same(types.GetVector(types.Int), CheckerTestSupport.Local(compilation, "stdIt").Type);
    Assert.Same(types.String, CheckerTestSupport.Local(compilation, "s1").Type);
    Assert.Same(types.String, CheckerTestSupport.Local(compilation, "s2").Type);
    Assert.Same(types.Int, CheckerTestSupport.Local(compilation, "r1").Type);
    Assert.Same(types.String, CheckerTestSupport.Local(compilation, "r2").Type);

    Assert.Contains(compilation.GenericInstantiations, i => i.Definition.Name == "identity" && ReferenceEquals(i.TypeArguments[0], types.Int));
    Assert.Contains(compilation.GenericInstantiations, i => i.Definition.Name == "identity" && ReferenceEquals(i.TypeArguments[0], types.String));
    Assert.Contains(compilation.GenericInstantiations, i => i.Definition.Name == "map_all"
        && ReferenceEquals(i.TypeArguments[0], types.Int)
        && ReferenceEquals(i.TypeArguments[1], types.String));
    Assert.Contains(compilation.GenericInstantiations, i => i.Definition.Name == "show" && ReferenceEquals(i.TypeArguments[0], types.Int));
    Assert.Contains(compilation.GenericInstantiations, i => i.Definition.Name == "read" && ReferenceEquals(i.TypeArguments[0], types.String));
  }

  [Fact]
  public void GenericBoundsAndGenericInterfaces_EnforceGEN8ThroughGEN11AndErrors() {
    const string ValidCode = """
      interface Shape {
        fn area() int
      }

      struct Square {
        side int
        fn area() int {
          return self:side * self:side
        }
      }

      @@Generic[T is Shape]
      fn get_area(s T) int {
        return s:area()
      }

      @@Generic[T]
      interface Repository {
        fn find_by_id(id string) T
        fn save(entity T)
      }

      struct Person {
        id string
        name string
      }

      struct SQLPersonRepository {
        fn find_by_id(id string) Person {
          return Person { id: id, name: "John" }
        }
        fn save(entity Person) {}
      }

      @@Generic[Repo is Repository[Person]]
      fn process(r Repo) Person {
        return r:find_by_id("123")
      }

      val a1 = get_area[Square](Square { side: 5 })
      val a2 = get_area(Square { side: 3 })
      val p1 = process[SQLPersonRepository](SQLPersonRepository {})
      val p2 = process(SQLPersonRepository {})
      """;

    Compilation valid = CheckerTestSupport.Compile(ValidCode);
    Assert.Empty(valid.Diagnostics);
    AssertNoTypeVariablesEscape(valid);
    Assert.Same(valid.Types.Int, CheckerTestSupport.Local(valid, "a1").Type);
    Assert.Same(valid.Types.Int, CheckerTestSupport.Local(valid, "a2").Type);
    Assert.Equal("Person", CheckerTestSupport.Local(valid, "p1").Type.DisplayName);
    Assert.Equal("Person", CheckerTestSupport.Local(valid, "p2").Type.DisplayName);

    // GEN-E1: inconsistent inference with RelatedLocation.
    const string InconsistentCode = """
      @@Generic[T]
      struct Quad {
        a T
        b T
      }
      val q = Quad { a: "Hello", b: 42 }
      """;
    Compilation inconsistent = CheckerTestSupport.Compile(InconsistentCode);
    Diagnostic e1 = Assert.Single(CheckerTestSupport.CheckerErrors(inconsistent));
    Assert.Equal(DiagnosticCodes.InconsistentGenericInference, e1.Code);
    Assert.Contains("Inconsistent types for generic parameter 'T': 'string' vs 'int'", e1.Message);
    Assert.NotEmpty(e1.Related);
    AssertNoTypeVariablesEscape(inconsistent);

    // GEN-E5: bound violated on inferred generic function call.
    const string BoundFailCode = """
      interface DocumentShape {
        name string
      }
      @@Generic[Doc is DocumentShape]
      fn print_doc(d Doc) string {
        return d:name
      }
      struct User {
        age int
      }
      val r = print_doc(User { age: 25 })
      """;
    Compilation boundFail = CheckerTestSupport.Compile(BoundFailCode);
    Diagnostic e5 = Assert.Single(CheckerTestSupport.CheckerErrors(boundFail));
    Assert.Equal(DiagnosticCodes.GenericConstraintNotSatisfied, e5.Code);
    Assert.Contains("does not satisfy generic constraint", e5.Message);
    AssertNoTypeVariablesEscape(boundFail);

    // PN3110: unconstrained generic parameter when nothing fixes T.
    const string UnconstrainedCode = """
      @@Generic[T]
      fn empty_opt() Option[T] {
        return Option::None
      }
      val x = empty_opt()
      """;
    Compilation unconstrained = CheckerTestSupport.Compile(UnconstrainedCode);
    Diagnostic eUnconstrained = Assert.Single(CheckerTestSupport.CheckerErrors(unconstrained));
    Assert.Equal(DiagnosticCodes.CannotInferGenericArguments, eUnconstrained.Code);
    AssertNoTypeVariablesEscape(unconstrained);
  }

  [Fact]
  public void GenericUnions_InferenceContextualAndRecursive() {
    const string Code = """
      @@Generic[T]
      union List {
        Cons(T, List[T])
        Nil
      }

      @@Generic[A, B, C, D]
      union Tuple4 {
        First(A)
        Second(B)
        Third(C)
        Fourth(D)
      }

      fn process(r Result[int, string]) Result[int, string] {
        return r
      }

      fn get_val(kind int) Tuple4[int, string, bool, float] {
        if kind == 1 {
          return Tuple4::First(42)
        }
        if kind == 2 {
          return Tuple4::Second("hello")
        }
        if kind == 3 {
          return Tuple4::Third(true)
        }
        return Tuple4::Fourth(3.5)
      }

      @@Generic[Value]
      fn if_present(option Option[Value], default Value) Value {
        return default
      }

      val someInt = Option::Some(42)
      var noneStr Option[string] = Option::None
      val rOk = process(Result::Success(100))
      val rErr = process(Result::Failure("boom"))
      val list = List::Cons(1, List::Cons(2, List::Nil))
      val chosen = if_present(Option::None, 7)
      """;

    Compilation compilation = CheckerTestSupport.Compile(Code);
    Assert.Empty(compilation.Diagnostics);
    AssertNoTypeVariablesEscape(compilation);

    Assert.Equal("Option[int]", CheckerTestSupport.Local(compilation, "someInt").Type.DisplayName);
    Assert.Equal("Option[string]", CheckerTestSupport.Local(compilation, "noneStr").Type.DisplayName);
    Assert.Equal("Result[int, string]", CheckerTestSupport.Local(compilation, "rOk").Type.DisplayName);
    Assert.Equal("Result[int, string]", CheckerTestSupport.Local(compilation, "rErr").Type.DisplayName);
    Assert.Equal("List[int]", CheckerTestSupport.Local(compilation, "list").Type.DisplayName);
    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "chosen").Type);
  }

  [Fact]
  public void Decision055_IndexedElementCallsDisambiguateFromGenericTypeArguments() {
    const string Code = """
      struct Dispatch {
        handlers []fn (int) -> int
      }

      val fns []fn (int) -> int = [fn (x) -> x + 1, fn (x) -> x * 2]
      val i = 1
      val a = fns[0](9)
      val b = fns[i](9)

      val table = map[string, fn (int) -> int] {
        "inc" -> fn (x) -> x + 1
      }
      val key = "inc"
      val c = table[key](10)

      val d = Dispatch { handlers: fns }
      val e = d:handlers[i](5)
      """;

    Compilation compilation = CheckerTestSupport.Compile(Code);
    Assert.Empty(compilation.Diagnostics);
    AssertNoTypeVariablesEscape(compilation);

    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "a").Type);
    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "b").Type);
    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "c").Type);
    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "e").Type);

    var bCall = (CallExpressionSyntax) CheckerTestSupport.Declaration(compilation, "b").Initializer;
    CallResolution? bRes = compilation.RootModel.GetCallResolution(bCall);
    Assert.NotNull(bRes);
    Assert.Equal(CallKind.IndexedElementCall, bRes!.Kind);
    Assert.Equal("i", bRes.IndexSymbol?.Name);

    var cCall = (CallExpressionSyntax) CheckerTestSupport.Declaration(compilation, "c").Initializer;
    CallResolution? cRes = compilation.RootModel.GetCallResolution(cCall);
    Assert.NotNull(cRes);
    Assert.Equal(CallKind.IndexedElementCall, cRes!.Kind);
    Assert.Equal("key", cRes.IndexSymbol?.Name);

    var eCall = (CallExpressionSyntax) CheckerTestSupport.Declaration(compilation, "e").Initializer;
    CallResolution? eRes = compilation.RootModel.GetCallResolution(eCall);
    Assert.NotNull(eRes);
    Assert.Equal(CallKind.IndexedElementCall, eRes!.Kind);
    Assert.Equal("i", eRes.IndexSymbol?.Name);
  }

  [Fact]
  public void ConformanceGenericsAndGenericUnionCases_MatchSpecAndErrTexts() {
    string repoRoot = ParserTestHelpers.FindRepositoryRoot();

    string[] positiveCases = [
      "conformance/generics/bound-function.pino",
      "conformance/generics/bound-generic-interface.pino",
      "conformance/generics/bound-method.pino",
      "conformance/generics/bound-struct.pino",
      "conformance/generics/bounded-generic-shape.pino",
      "conformance/generics/constrained-member-access.pino",
      "conformance/generics/function-explicit.pino",
      "conformance/generics/function-higher-order.pino",
      "conformance/generics/function-implicit.pino",
      "conformance/generics/generic-interface.pino",
      "conformance/generics/method-instance.pino",
      "conformance/generics/method-static.pino",
      "conformance/generics/struct-decorator.pino",
      "conformance/generics/struct-explicit-instantiation.pino",
      "conformance/generics/struct-implicit-instantiation.pino",
      "conformance/unions/generic-context-inference.pino",
      "conformance/unions/generic-decorator.pino",
      "conformance/unions/generic-inferred-type.pino",
      "conformance/unions/generic-inline.pino",
      "conformance/unions/generic-multi-param.pino",
      "conformance/unions/generic-recursive.pino",
      "conformance/unions/is-in-generic-fn.pino",
    ];

    foreach (string relPath in positiveCases) {
      string code = File.ReadAllText(Path.Combine(repoRoot, relPath));
      Compilation compilation = CheckerTestSupport.Compile(code, relPath);
      Assert.True(
          compilation.Diagnostics.IsEmpty,
          $"{relPath} had diagnostics: {string.Join("; ", compilation.Diagnostics.Select(static d => $"{d.Code}: {d.Message}"))}");
      AssertNoTypeVariablesEscape(compilation);
    }

    // Exact conformance .err text cases (non-OQ-42).
    string[] exactErrCases = [
      "conformance/generics/err-bound-function",
      "conformance/generics/err-bound-generic-interface",
      "conformance/generics/err-bound-method",
      "conformance/generics/err-bound-primitive",
      "conformance/generics/err-bound-struct",
      "conformance/generics/err-generic-interface-signature",
      "conformance/generics/err-inconsistent-inference",
      "conformance/generics/err-param-name-conflict",
      "conformance/generics/err-static-instance-generic",
      "conformance/generics/err-substituted-type-misuse",
      "conformance/generics/err-unconstrained-member-access",
      "conformance/unions/err-constructor-arg-type",
    ];

    foreach (string basePath in exactErrCases) {
      string code = File.ReadAllText(Path.Combine(repoRoot, basePath + ".pino"));
      string expectedText = File.ReadAllText(Path.Combine(repoRoot, basePath + ".err")).Trim();
      Compilation compilation = CheckerTestSupport.Compile(code, basePath + ".pino");
      Diagnostic[] errors = CheckerTestSupport.CheckerErrors(compilation);
      Assert.NotEmpty(errors);
      Assert.True(
          errors.Any(d => d.Message.Contains(expectedText, StringComparison.Ordinal)),
          $"{basePath}.pino expected '{expectedText}', got: {string.Join(" | ", errors.Select(static d => d.Message))}");
      AssertNoTypeVariablesEscape(compilation);
    }

    // OQ-42 cases whose legacy .err uses mangled names (`Pair_string_int`, `Option_string`, `Container_string`):
    // verify modern Pino phrasing matches the spec rule requirements (GEN-E2, UNION-E3, UNION-E4).
    (string Path, string RequiredPhrase)[] modernCases = [
      ("conformance/generics/err-incompatible-init.pino", "Cannot assign type 'string' to field 'value' of type 'int'"),
      ("conformance/unions/err-generic-mismatch.pino", "expected type 'Maybe[int]', but got 'Maybe[string]'"),
      ("conformance/unions/err-generic-arg-mismatch.pino", "expected type 'Container[int]', but got 'Container[string]'"),
    ];

    foreach ((string relPath, string requiredPhrase) in modernCases) {
      string code = File.ReadAllText(Path.Combine(repoRoot, relPath));
      Compilation compilation = CheckerTestSupport.Compile(code, relPath);
      Diagnostic[] errors = CheckerTestSupport.CheckerErrors(compilation);
      Assert.NotEmpty(errors);
      Assert.True(
          errors.Any(d => d.Message.Contains(requiredPhrase, StringComparison.Ordinal)),
          $"{relPath} expected '{requiredPhrase}', got: {string.Join(" | ", errors.Select(static d => d.Message))}");
      AssertNoTypeVariablesEscape(compilation);
    }
  }
}
