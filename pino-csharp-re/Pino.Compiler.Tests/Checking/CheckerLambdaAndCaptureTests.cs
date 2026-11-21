using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerLambdaAndCaptureTests {
  [Fact]
  public void ContextualLambdas_D167_AllFiveTypedContextsInferParameterTypes() {
    Compilation compilation = Compile("""
      # 1. Typed val/var (D-012) and reassignment
      val inc fn (int) -> int = fn (x) -> x + 1
      var bin fn (int, int) -> int = fn (a, b) -> a + b
      bin = fn (a, b) -> a - b

      # 2. Return in function and method with known return type
      fn make_adder() fn (int) -> int {
        return fn (x) -> x + 10
      }
      struct Factory {
        fn make_mul() fn (int) -> int {
          return fn (x) { return x * 2 }
        }
      }

      # 3. Struct literal field
      struct Box {
        op fn (int) -> int
      }
      val boxed = Box { op: fn (x) -> x * 3 }

      # 4. Vector, set, and map element/value
      val fs []fn (int) -> int = [fn (x) -> x + 1, fn (x) -> x * 2]
      val s = set[fn (int) -> int] { fn (x) -> x - 1 }
      val m = map[string, fn (int) -> int] { "inc" -> fn (x) -> x + 1 }

      # 5. Call arguments (user functions + 1-/2-arg stdlib callbacks on []T and set[T])
      fn call_lambda_3(fun fn (int, int) -> int) {
        return fun(1, 2)
      }
      val r_call = call_lambda_3(fn (n, m) -> n + m)

      val xs = [1, 2, 3, 4]
      xs:each(fn (x) { println(x) })
      xs:each(fn (x, i) { println(x + i) })
      val filtered1 = xs:filter(fn (x) -> x > 1)
      val filtered2 = xs:filter(fn (x, i) -> i > 0)
      val found1 = xs:find(fn (x) -> x == 2)
      val found2 = xs:find(fn (x, i) -> i == 1)
      val fidx1 = xs:find_index(fn (x) -> x == 2)
      val fidx2 = xs:find_index(fn (x, i) -> i == 1)
      val any1 = xs:any(fn (x) -> x > 2)
      val any2 = xs:any(fn (x, i) -> i > 0)
      val all1 = xs:all(fn (x) -> x > 0)
      val all2 = xs:all(fn (x, i) -> i >= 0)
      val cnt1 = xs:count(fn (x) -> x > 1)
      val cnt2 = xs:count(fn (x, i) -> i > 0)
      val mapped1 = xs:map(fn (x) -> "$x")
      val mapped2 = xs:map(fn (x, i) -> x + i)

      val st = set[int] { 1, 2, 3 }
      val st_any = st:any(fn (x) -> x > 1)
      val st_all = st:all(fn (x) -> x > 0)
      st:each(fn (x) { println(x) })

      val added = make_adder()(5)
      val box_res = boxed:op(4)
      val vec_res = fs[0](5)
      """);

    Assert.Empty(CheckerErrors(compilation));
    Assert.Same(compilation.Types.Int, Local(compilation, "r_call").Type);
    Assert.Same(compilation.Types.GetVector(compilation.Types.Int), Local(compilation, "filtered1").Type);
    Assert.Same(compilation.Types.GetVector(compilation.Types.Int), Local(compilation, "filtered2").Type);
    Assert.Same(
        compilation.Types.GetUnion(compilation.Prelude.OptionUnion, [compilation.Types.Int]),
        Local(compilation, "found1").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "fidx1").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "fidx2").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "any1").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "all2").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "cnt1").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "cnt2").Type);
    Assert.Same(compilation.Types.GetVector(compilation.Types.String), Local(compilation, "mapped1").Type);
    Assert.Same(compilation.Types.GetVector(compilation.Types.Int), Local(compilation, "mapped2").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "st_any").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "st_all").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "added").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "box_res").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "vec_res").Type);

    // Every untyped lambda parameter was inferred to a concrete type (no ErrorType).
    foreach (LambdaExpressionSyntax lambda in All<LambdaExpressionSyntax>(compilation)) {
      foreach (ParameterSyntax param in lambda.Parameters) {
        ParameterSymbol paramSym = Assert.IsType<ParameterSymbol>(compilation.RootModel.GetDeclaredSymbol(param));
        Assert.Same(compilation.Types.Int, paramSym.Type);
      }
    }

    // Verify 1-arg vs 2-arg stdlib IntrinsicId selection on filter and map.
    CallExpressionSyntax filter1Call = (CallExpressionSyntax) Declaration(compilation, "filtered1").Initializer;
    CallExpressionSyntax filter2Call = (CallExpressionSyntax) Declaration(compilation, "filtered2").Initializer;
    CallExpressionSyntax map1Call = (CallExpressionSyntax) Declaration(compilation, "mapped1").Initializer;
    CallExpressionSyntax map2Call = (CallExpressionSyntax) Declaration(compilation, "mapped2").Initializer;
    Assert.Equal(IntrinsicId.VecFilter1, compilation.RootModel.GetCallResolution(filter1Call)!.Intrinsic);
    Assert.Equal(IntrinsicId.VecFilter2, compilation.RootModel.GetCallResolution(filter2Call)!.Intrinsic);
    Assert.Equal(IntrinsicId.VecMap1, compilation.RootModel.GetCallResolution(map1Call)!.Intrinsic);
    Assert.Equal(IntrinsicId.VecMap2, compilation.RootModel.GetCallResolution(map2Call)!.Intrinsic);
  }

  [Fact]
  public void UntypedLambdaWithNoContext_OQ14AndFNE11_EmitsPN3109WithoutCascades() {
    Compilation compilation = Compile("""
      val f = fn (x) -> x + 1
      val r = f(1)
      """);

    Diagnostic err = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.CannotInferLambdaParameter, err.Code);
    Assert.Contains("Cannot infer parameter type", err.Message);

    LambdaExpressionSyntax lambda = Assert.Single(All<LambdaExpressionSyntax>(compilation));
    ParameterSymbol param = Assert.IsType<ParameterSymbol>(compilation.RootModel.GetDeclaredSymbol(lambda.Parameters[0]));
    Assert.Same(compilation.Types.Error, param.Type);
    Assert.Same(compilation.Types.Error, Local(compilation, "r").Type);
  }

  [Fact]
  public void LambdaReturns_D053_ArrowAndBlockReturnInferenceWideningAndErrors() {
    Compilation ok = Compile("""
      val arrow = fn (x int) -> x * 2
      val block_int = fn (x int) {
        if x > 0 {
          return x
        }
        return 0
      }
      val block_void = fn (x int) {
        println(x)
      }
      val block_mixed = fn (b bool) {
        if b {
          return 1
        }
        return 2.5
      }
      """);

    Assert.Empty(CheckerErrors(ok));
    Assert.Same(ok.Types.GetFunction([ok.Types.Int], ok.Types.Int), Local(ok, "arrow").Type);
    Assert.Same(ok.Types.GetFunction([ok.Types.Int], ok.Types.Int), Local(ok, "block_int").Type);
    Assert.Same(ok.Types.GetFunction([ok.Types.Int], ok.Types.Void), Local(ok, "block_void").Type);
    Assert.Same(ok.Types.GetFunction([ok.Types.Bool], ok.Types.Float), Local(ok, "block_mixed").Type);

    var mixedLambda = (LambdaExpressionSyntax) Declaration(ok, "block_mixed").Initializer;
    List<ReturnStatementSyntax> mixedReturns = [.. Descendants<ReturnStatementSyntax>(mixedLambda)];
    AssertIntToFloat(ok, mixedReturns[0].Expression!);
    AssertNoConversion(ok, mixedReturns[1].Expression!);

    // Conflicting returns & bare return + value return inside block lambdas.
    Compilation errs = Compile("""
      val clash = fn (b bool) {
        if b { return 1 }
        return "nope"
      }
      val bare = fn (b bool) {
        if b { return }
        return 42
      }
      """);

    Diagnostic[] diags = CheckerErrors(errs);
    Assert.Equal(2, diags.Length);
    Assert.Equal(DiagnosticCodes.ConflictingReturnTypes, diags[0].Code);
    Assert.Contains("conflicting return types", diags[0].Message);
    Assert.Equal(DiagnosticCodes.BareReturnInNonVoidFunction, diags[1].Code);
    Assert.Contains("must return a value", diags[1].Message);
  }

  [Fact]
  public void ImplicitIt_FN16AndFN17AndD002AndPN3210_BindsAndRespectsLexicalScope() {
    Compilation ok = Compile("""
      fn apply(f fn (int) -> int, x int) int {
        return f(x)
      }

      fn double_it(it int) int {
        return it * 2
      }

      fn apply_twice(it int) int {
        return double_it(it)
      }

      val xs = [1, 2, 3, 4]
      val filtered = xs:filter(it > 1)
      val doubled = xs:map(it * 2)
      val counted = xs:count(it % 2 == 0)
      val applied = apply(it + 1, 5)
      val names = ["John", "Alice", "Bob"]
      val short_names = names:filter(it:len <= 4)

      # D-002 OQ-17: `for N` binds `it: int`, so `it` stays the loop variable in calls.
      for 3 {
        println("$it")
        println(it * 2)
      }

      val from_declared = apply_twice(3)
      """);

    Assert.Empty(CheckerErrors(ok));
    Assert.Same(ok.Types.GetVector(ok.Types.Int), Local(ok, "filtered").Type);
    Assert.Same(ok.Types.GetVector(ok.Types.Int), Local(ok, "doubled").Type);
    Assert.Same(ok.Types.Int, Local(ok, "counted").Type);
    Assert.Same(ok.Types.Int, Local(ok, "applied").Type);
    Assert.Same(ok.Types.GetVector(ok.Types.String), Local(ok, "short_names").Type);
    Assert.Same(ok.Types.Int, Local(ok, "from_declared").Type);

    // Check ConversionKind.ImplicitItLambda on `xs:filter(it > 1)` and `apply(it + 1, 5)`.
    var filterCall = (CallExpressionSyntax) Declaration(ok, "filtered").Initializer;
    ExpressionSyntax filterArg = filterCall.Arguments[0].Expression;
    ConversionInfo? filterConv = ok.RootModel.GetConversion(filterArg);
    Assert.NotNull(filterConv);
    Assert.Equal(ConversionKind.ImplicitItLambda, filterConv!.Kind);
    Assert.NotNull(filterConv.ImplicitItSymbol);
    Assert.Equal("it", filterConv.ImplicitItSymbol!.Name);
    Assert.Equal(LocalKind.ImplicitIt, filterConv.ImplicitItSymbol.Kind);
    Assert.False(filterConv.ImplicitItSymbol.IsMutable);
    Assert.Same(ok.Types.Int, filterConv.ImplicitItSymbol.Type);
    Assert.Same(ok.Types.GetFunction([ok.Types.Int], ok.Types.Bool), filterConv.ImplicitLambdaType);
    Assert.Equal(IntrinsicId.VecFilter1, ok.RootModel.GetCallResolution(filterCall)!.Intrinsic);

    var applyCall = (CallExpressionSyntax) Declaration(ok, "applied").Initializer;
    ExpressionSyntax applyArg = applyCall.Arguments[0].Expression;
    ConversionInfo? applyConv = ok.RootModel.GetConversion(applyArg);
    Assert.NotNull(applyConv);
    Assert.Equal(ConversionKind.ImplicitItLambda, applyConv!.Kind);
    Assert.Same(ok.Types.GetFunction([ok.Types.Int], ok.Types.Int), applyConv.ImplicitLambdaType);

    // FN-17: `double_it(it)` inside `apply_twice(it int)` is NOT wrapped as ImplicitItLambda.
    CallExpressionSyntax doubleItCall = All<CallExpressionSyntax>(ok)
        .Single(c => c.Callee is IdentifierExpressionSyntax id && TextOf(ok, id.IdentifierToken) == "double_it");
    Assert.Null(ok.RootModel.GetConversion(doubleItCall.Arguments[0].Expression));

    // D-002 OQ-17: `println("$it")` and `println(it * 2)` inside `for 3` are NOT wrapped.
    foreach (CallExpressionSyntax printlnCall in All<CallExpressionSyntax>(ok)
        .Where(c => c.Callee is IdentifierExpressionSyntax id && TextOf(ok, id.IdentifierToken) == "println")) {
      Assert.Null(ok.RootModel.GetConversion(printlnCall.Arguments[0].Expression));
    }

    // Multi-param and zero-param callable rejection with PN3210 (InvalidImplicitIt).
    Compilation invalidIt = Compile("""
      fn pair_apply(f fn (int, int) -> int) int {
        return f(1, 2)
      }
      fn thunk(f fn () -> int) int {
        return f()
      }
      val a = pair_apply(it + 1)
      val b = thunk(it + 1)
      """);

    Diagnostic[] itErrs = CheckerErrors(invalidIt);
    Assert.Equal(2, itErrs.Length);
    Assert.All(itErrs, static d => Assert.Equal(DiagnosticCodes.InvalidImplicitIt, d.Code));
  }

  [Fact]
  public void ClosureCaptures_AD002AndD056_RecordsLambdasNestedFunctionsAndImplicitIt() {
    Compilation compilation = Compile("""
      fn make_counter() fn () -> int {
        var count = 0
        return fn () {
          count = count + 1
          return count
        }
      }

      fn make_compound_counter() fn () -> int {
        var total = 0
        return fn () {
          total += 2
          return total
        }
      }

      fn read_only_captures(factor int) fn (int) -> int {
        val offset = 10
        var untouched = 5
        return fn (x) -> x * factor + offset + untouched
      }

      fn nested_function_captures(base int) int {
        var acc = 0
        val step = 3
        fn bump(n int) {
          acc += n + step + base
          return acc
        }
        return bump(4)
      }

      fn transitive_capture() fn () -> int {
        var shared = 0
        return fn () {
          val inner = fn () {
            shared = shared + 1
            return shared
          }
          return inner()
        }
      }

      val xs = [1, 2, 3, 4]
      val limit = 2
      val no_cap_filter = xs:filter(it > 1)
      val cap_filter = xs:filter(it > limit)
      """);

    Assert.Empty(CheckerErrors(compilation));
    SemanticModel model = compilation.RootModel;

    // 1. make_counter: captures `count` with IsMutable = true, IsMutatedByClosure = true.
    FunctionDeclarationSyntax makeCounterFn = All<FunctionDeclarationSyntax>(compilation)
        .Single(f => TextOf(compilation, f.NameToken) == "make_counter");
    Assert.Null(model.GetCaptures(makeCounterFn));
    LambdaExpressionSyntax counterLambda = Assert.Single(Descendants<LambdaExpressionSyntax>(makeCounterFn));
    CaptureList? counterCaptures = model.GetCaptures(counterLambda);
    Assert.NotNull(counterCaptures);
    CaptureEntry countCap = Assert.Single(counterCaptures!.Entries);
    Assert.Same(Local(compilation, "count"), countCap.Symbol);
    Assert.True(countCap.IsMutable);
    Assert.True(countCap.IsMutatedByClosure);

    // 2. make_compound_counter: compound assignment `+=` sets IsMutatedByClosure = true.
    FunctionDeclarationSyntax compoundFn = All<FunctionDeclarationSyntax>(compilation)
        .Single(f => TextOf(compilation, f.NameToken) == "make_compound_counter");
    LambdaExpressionSyntax compoundLambda = Assert.Single(Descendants<LambdaExpressionSyntax>(compoundFn));
    CaptureEntry totalCap = Assert.Single(model.GetCaptures(compoundLambda)!.Entries);
    Assert.Same(Local(compilation, "total"), totalCap.Symbol);
    Assert.True(totalCap.IsMutable);
    Assert.True(totalCap.IsMutatedByClosure);

    // 3. read_only_captures: deterministic order [factor (param), offset (val), untouched (var)].
    FunctionDeclarationSyntax roFn = All<FunctionDeclarationSyntax>(compilation)
        .Single(f => TextOf(compilation, f.NameToken) == "read_only_captures");
    LambdaExpressionSyntax roLambda = Assert.Single(Descendants<LambdaExpressionSyntax>(roFn));
    CaptureList roCaptures = model.GetCaptures(roLambda)!;
    Assert.Equal(3, roCaptures.Entries.Length);

    Assert.Equal("factor", roCaptures.Entries[0].Symbol.Name);
    Assert.IsType<ParameterSymbol>(roCaptures.Entries[0].Symbol);
    Assert.False(roCaptures.Entries[0].IsMutable);
    Assert.False(roCaptures.Entries[0].IsMutatedByClosure);

    Assert.Same(Local(compilation, "offset"), roCaptures.Entries[1].Symbol);
    Assert.False(roCaptures.Entries[1].IsMutable);
    Assert.False(roCaptures.Entries[1].IsMutatedByClosure);

    Assert.Same(Local(compilation, "untouched"), roCaptures.Entries[2].Symbol);
    Assert.True(roCaptures.Entries[2].IsMutable);
    Assert.False(roCaptures.Entries[2].IsMutatedByClosure);

    // 4. nested_function_captures: nested `fn bump` captures [acc (mutated var), step (val), base (param)].
    FunctionDeclarationSyntax bumpFn = All<FunctionDeclarationSyntax>(compilation)
        .Single(f => TextOf(compilation, f.NameToken) == "bump");
    CaptureList? bumpCaptures = model.GetCaptures(bumpFn);
    Assert.NotNull(bumpCaptures);
    Assert.Equal(3, bumpCaptures!.Entries.Length);
    Assert.Same(Local(compilation, "acc"), bumpCaptures.Entries[0].Symbol);
    Assert.True(bumpCaptures.Entries[0].IsMutable);
    Assert.True(bumpCaptures.Entries[0].IsMutatedByClosure);
    Assert.Same(Local(compilation, "step"), bumpCaptures.Entries[1].Symbol);
    Assert.False(bumpCaptures.Entries[1].IsMutable);
    Assert.False(bumpCaptures.Entries[1].IsMutatedByClosure);
    Assert.Equal("base", bumpCaptures.Entries[2].Symbol.Name);
    Assert.False(bumpCaptures.Entries[2].IsMutable);
    Assert.False(bumpCaptures.Entries[2].IsMutatedByClosure);

    // 5. transitive_capture: both outer lambda and inner lambda capture `shared`.
    FunctionDeclarationSyntax transitiveFn = All<FunctionDeclarationSyntax>(compilation)
        .Single(f => TextOf(compilation, f.NameToken) == "transitive_capture");
    List<LambdaExpressionSyntax> transitiveLambdas = [.. Descendants<LambdaExpressionSyntax>(transitiveFn)];
    Assert.Equal(2, transitiveLambdas.Count);
    foreach (LambdaExpressionSyntax l in transitiveLambdas) {
      CaptureEntry cap = Assert.Single(model.GetCaptures(l)!.Entries);
      Assert.Same(Local(compilation, "shared"), cap.Symbol);
      Assert.True(cap.IsMutable);
      Assert.True(cap.IsMutatedByClosure);
    }

    // 6. `xs:filter(it > 1)` has empty capture list and ConversionKind.ImplicitItLambda.
    var noCapCall = (CallExpressionSyntax) Declaration(compilation, "no_cap_filter").Initializer;
    ExpressionSyntax noCapArg = noCapCall.Arguments[0].Expression;
    Assert.Equal(ConversionKind.ImplicitItLambda, model.GetConversion(noCapArg)!.Kind);
    CaptureList? noCapList = model.GetCaptures(noCapArg);
    Assert.NotNull(noCapList);
    Assert.True(noCapList!.IsEmpty);

    // 7. `xs:filter(it > limit)` captures `limit` (read-only val) and records ImplicitItLambda.
    var capCall = (CallExpressionSyntax) Declaration(compilation, "cap_filter").Initializer;
    ExpressionSyntax capArg = capCall.Arguments[0].Expression;
    Assert.Equal(ConversionKind.ImplicitItLambda, model.GetConversion(capArg)!.Kind);
    CaptureEntry limitCap = Assert.Single(model.GetCaptures(capArg)!.Entries);
    Assert.Same(Local(compilation, "limit"), limitCap.Symbol);
    Assert.False(limitCap.IsMutable);
    Assert.False(limitCap.IsMutatedByClosure);
  }

  [Fact]
  public void ConformanceLambdaAndImplicitItCases_PassPositivesAndMatchErrTexts() {
    string repoRoot = ParserTestHelpers.FindRepositoryRoot();
    string conformanceRoot = Path.Combine(repoRoot, "conformance");

    string[] positiveCases = [
      "functions/lambda-infer-typed-val.pino",
      "functions/lambda-infer-return.pino",
      "functions/lambda-infer-struct-field.pino",
      "functions/lambda-infer-collection-element.pino",
      "functions/lambda-infer-call-argument.pino",
      "functions/closure-mutable-capture.pino",
      "functions/closure-capture.pino",
      "functions/implicit-it-argument.pino",
      "functions/declared-it-parameter.pino",
      "functions/untyped-lambda-commas.pino",
      "loops/times-it-in-call-argument.pino",
      "loops/times-it-call-argument.pino",
      "stdlib-vector/count-it.pino",
    ];

    foreach (string relPath in positiveCases) {
      string fullPath = Path.Combine(conformanceRoot, relPath);
      ParseResult parsed = Parser.Parse(new SourceText(File.ReadAllText(fullPath), fullPath));
      Assert.Empty(parsed.Diagnostics);
      Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
      Assert.Empty(CheckerErrors(compilation));
    }

    string errPath = Path.Combine(conformanceRoot, "functions/err-untyped-lambda-no-context.pino");
    string expectedErr = File.ReadAllText(Path.ChangeExtension(errPath, ".err")).Trim();
    ParseResult errParsed = Parser.Parse(new SourceText(File.ReadAllText(errPath), errPath));
    Compilation errCompilation = Compilation.Create(errParsed, FileSystemModuleSource.Instance);
    Assert.Contains(
        errCompilation.Diagnostics,
        d => d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains(expectedErr, StringComparison.Ordinal));
  }
}
