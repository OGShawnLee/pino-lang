using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerCallAndStdlibTests {
  [Fact]
  public void CallsAndExplicitReturns_D057AndOQ9AndD055_CheckArityTypesWideningAndIndexedCalls() {
    Compilation ok = Compile("""
      fn add_f(a float, b float) float {
        return a + b
      }

      fn inc(x int) int {
        return x + 1
      }

      fn dec(x int) int {
        return x - 1
      }

      val sum = add_f(1, 2.5)
      val fns = [inc, dec]
      val idx = 1
      val called_lit = fns[0](9)
      val called_var = fns[idx](9)
      """);

    Assert.Empty(CheckerErrors(ok));
    Assert.Same(ok.Types.Float, Local(ok, "sum").Type);
    Assert.Same(ok.Types.Int, Local(ok, "called_lit").Type);
    Assert.Same(ok.Types.Int, Local(ok, "called_var").Type);

    // OQ-9: int argument widens to float parameter.
    CallExpressionSyntax addCall = All<CallExpressionSyntax>(ok)
        .Single(c => c.Callee is IdentifierExpressionSyntax id && TextOf(ok, id.IdentifierToken) == "add_f");
    AssertIntToFloat(ok, addCall.Arguments[0].Expression);
    AssertNoConversion(ok, addCall.Arguments[1].Expression);
    Assert.Equal(CallKind.DirectFunction, ok.RootModel.GetCallResolution(addCall)!.Kind);

    // D-055: fns[idx](9) resolves as IndexedElementCall with IndexSymbol = idx.
    CallExpressionSyntax idxCall = All<CallExpressionSyntax>(ok)
        .Single(c => c.TypeArguments is not null);
    CallResolution? idxRes = ok.RootModel.GetCallResolution(idxCall);
    Assert.NotNull(idxRes);
    Assert.Equal(CallKind.IndexedElementCall, idxRes!.Kind);
    Assert.Same(Local(ok, "fns"), idxRes.TargetSymbol);
    Assert.Same(Local(ok, "idx"), idxRes.IndexSymbol);

    // D-057 / FN-E9 / FN-E10 / TYPE-E3 / FN-E7
    Compilation errs = Compile("""
      fn two(a int, b int) int { return a + b }
      val bad_arity = two(1)
      val bad_arg = two(1, 2.5)
      fn bad_ret() int { return 1.5 }
      fn bare_ret() int { return }
      fn void_ret() { }
      fn bad_void_ret() void { return 1 }
      """);

    Diagnostic[] diags = CheckerErrors(errs);
    Assert.Equal(5, diags.Length);
    Assert.Contains(diags, static d => d.Code == DiagnosticCodes.WrongArgumentCount && d.Message.Contains("expected 2 arguments"));
    Assert.Contains(diags, static d => d.Code == DiagnosticCodes.TypeMismatch && d.Message.Contains("float"));
    Assert.Contains(diags, static d => d.Code == DiagnosticCodes.ReturnTypeMismatch && d.Message.Contains("float") && d.Message.Contains("declared return type"));
    Assert.Contains(diags, static d => d.Code == DiagnosticCodes.BareReturnInNonVoidFunction && d.Message.Contains("must return a value"));
    Assert.Contains(diags, static d => d.Code == DiagnosticCodes.ReturnTypeMismatch && d.Message.Contains("declared return type"));
  }

  [Fact]
  public void NonGenericUnionConstructors_UNION2AndUNIONE1_RecordCallResolutionAndRejectMismatch() {
    Compilation compilation = Compile("""
      union Entity {
        Person(string)
        Empty
      }

      val p = Entity::Person("Ada")
      val e = Entity::Empty
      val bad = Entity::Person(12)
      """);

    Diagnostic err = Assert.Single(CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.VariantArgumentMismatch, err.Code);
    Assert.Contains("for variant constructor", err.Message);

    CallExpressionSyntax personCall = All<CallExpressionSyntax>(compilation)[0];
    CallResolution? res = compilation.RootModel.GetCallResolution(personCall);
    Assert.NotNull(res);
    Assert.Equal(CallKind.UnionVariantConstructor, res!.Kind);
    Assert.IsType<UnionVariantSymbol>(res.TargetSymbol);
    Assert.NotNull(res.ConstructedUnionType);
    Assert.Same(res.ConstructedUnionType, Local(compilation, "p").Type);
    Assert.Same(res.ConstructedUnionType, Local(compilation, "e").Type);
  }

  [Fact]
  public void StdlibGlobals_AllCatalogEntriesDispatchExactIntrinsicId() {
    Compilation compilation = Compile("""
      println("hello", 42)
      val rl1 = readline()
      val rl2 = readline("> ")
      val i1 = int(3.5)
      val i2 = int("42")
      val f1 = float(7)
      val f2 = float("7.5")
      val r1 = rune(65)
      val r2 = rune("A")
      val s1 = str(123)
      val t1 = type(123)
      val rnd1 = rand()
      val rnd2 = rand(10)
      val tm = time()
      sleep(5)
      clear()
      val rx = regex("[0-9]+")
      val rf = read_file("a.txt")
      val wf = write_file("a.txt", "hi")
      val fe = file_exists("a.txt")

      fn diverge() int {
        return panic("boom")
      }
      """);

    Assert.Empty(CheckerErrors(compilation));

    IntrinsicId[] expectedIds = [
      IntrinsicId.Println,
      IntrinsicId.ReadlineNoPrompt,
      IntrinsicId.ReadlineWithPrompt,
      IntrinsicId.IntFromNumber,
      IntrinsicId.IntFromString,
      IntrinsicId.FloatFromNumber,
      IntrinsicId.FloatFromString,
      IntrinsicId.RuneFromInt,
      IntrinsicId.RuneFromString,
      IntrinsicId.Str,
      IntrinsicId.Type,
      IntrinsicId.RandFloat,
      IntrinsicId.RandInt,
      IntrinsicId.Time,
      IntrinsicId.Sleep,
      IntrinsicId.Clear,
      IntrinsicId.RegexCompile,
      IntrinsicId.ReadFile,
      IntrinsicId.WriteFile,
      IntrinsicId.FileExists,
      IntrinsicId.Panic,
    ];

    List<CallExpressionSyntax> calls = All<CallExpressionSyntax>(compilation);
    Assert.Equal(expectedIds.Length, calls.Count);
    for (int i = 0; i < expectedIds.Length; i++) {
      CallResolution? res = compilation.RootModel.GetCallResolution(calls[i]);
      Assert.NotNull(res);
      Assert.Equal(CallKind.Intrinsic, res!.Kind);
      Assert.Equal(expectedIds[i], res.Intrinsic);
    }

    Assert.Same(compilation.Types.Int, Local(compilation, "i1").Type);
    Assert.Same(
        compilation.Types.GetUnion(compilation.Prelude.ResultUnion, [compilation.Types.Int, compilation.Types.String]),
        Local(compilation, "i2").Type);
    Assert.Same(compilation.Types.Float, Local(compilation, "f1").Type);
    Assert.Same(
        compilation.Types.GetUnion(compilation.Prelude.ResultUnion, [compilation.Types.Float, compilation.Types.String]),
        Local(compilation, "f2").Type);
    Assert.Same(compilation.Types.Rune, Local(compilation, "r1").Type);
    Assert.Same(compilation.Types.Float, Local(compilation, "rnd1").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "rnd2").Type);
    Assert.Same(compilation.Types.Regex, Local(compilation, "rx").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "fe").Type);

    // panic(...) yields NeverType and records NeverToTarget when returned as int.
    CallExpressionSyntax panicCall = calls[^1];
    Assert.Same(compilation.Types.Never, compilation.RootModel.GetType(panicCall));
    Assert.Equal(ConversionKind.NeverToTarget, compilation.RootModel.GetConversion(panicCall)!.Kind);
  }

  [Fact]
  public void StdlibMemberMethods_StringVectorMapSetRegex_RecordExactIntrinsicIds() {
    Compilation compilation = Compile("""
      val s = " Hello "
      val slen = s:len + s:length
      val s_low = s:lower()
      val s_up = s:upper()
      val s_tr = s:trim()
      val s_ts = s:trim_start()
      val s_te = s:trim_end()
      val s_ow = s:to_owned()
      val s_cl = s:clone()
      val s_cs = s:to_cstring()
      val s_c2 = s:cstr()
      val s_co = s:contains("He")
      val s_sw = s:starts_with(" ")
      val s_ew = s:ends_with(" ")
      val s_io = s:index_of("l")
      val s_lo = s:last_index_of("l")
      val s_sp = s:split(" ")
      val s_rp = s:replace("l", "x")
      val s_sb = s:substring(1, 3)

      var v = [1, 2, 3]
      val vinfo = v:len + v:length + v:cap + v:capacity
      v:push(4)
      v:add(5)
      val vpop = v:pop()
      val vj = v:join(",")
      v:each(fn (x int) { println(x) })
      v:each(fn (x int, i int) { println(i) })
      val vm1 = v:map(fn (x int) -> "$x")
      val vm2 = v:map(fn (x int, i int) -> x + i)
      val vf1 = v:filter(fn (x int) -> x > 1)
      val vf2 = v:filter(fn (x int, i int) -> i > 0)
      val vfd1 = v:find(fn (x int) -> x == 2)
      val vfd2 = v:find(fn (x int, i int) -> i == 1)
      val vfi1 = v:find_index(fn (x int) -> x == 2)
      val vfi2 = v:find_index(fn (x int, i int) -> i == 1)
      val van1 = v:any(fn (x int) -> x > 2)
      val van2 = v:any(fn (x int, i int) -> i > 0)
      val val1 = v:all(fn (x int) -> x > 0)
      val val2 = v:all(fn (x int, i int) -> i >= 0)
      val vct1 = v:count(fn (x int) -> x > 1)
      val vct2 = v:count(fn (x int, i int) -> i > 0)

      var m = map[string, int] { "a" -> 1 }
      val mlen = m:len + m:length
      val mkeys = m:keys()
      val mvals = m:values()
      val mrem = m:remove("a")

      var st = set[int] { 1, 2 }
      val stlen = st:len + st:length
      st:add(3)
      st:remove(2)
      val sany = st:any(fn (x int) -> x > 1)
      val sall = st:all(fn (x int) -> x > 0)
      st:each(fn (x int) { println(x) })

      val rx = regex("[0-9]+")
      val rpat = rx:pattern
      val rhm = rx:has_match("a1")
      val rmp = rx:match_prefix("12a")
      val rfnd = rx:find("a12b")
      val rfa = rx:find_all("1a2")
      val rrpl = rx:replace("1a2", "#")
      """);

    Assert.Empty(CheckerErrors(compilation));

    IntrinsicId[] expectedCallIds = [
      IntrinsicId.StrLower,
      IntrinsicId.StrUpper,
      IntrinsicId.StrTrim,
      IntrinsicId.StrTrimStart,
      IntrinsicId.StrTrimEnd,
      IntrinsicId.StrClone,
      IntrinsicId.StrClone,
      IntrinsicId.StrToCString,
      IntrinsicId.StrToCString,
      IntrinsicId.StrContains,
      IntrinsicId.StrStartsWith,
      IntrinsicId.StrEndsWith,
      IntrinsicId.StrIndexOf,
      IntrinsicId.StrLastIndexOf,
      IntrinsicId.StrSplit,
      IntrinsicId.StrReplace,
      IntrinsicId.StrSubstring,
      IntrinsicId.VecPush,
      IntrinsicId.VecPush,
      IntrinsicId.VecPop,
      IntrinsicId.VecJoin,
      IntrinsicId.VecEach1,
      IntrinsicId.Println,
      IntrinsicId.VecEach2,
      IntrinsicId.Println,
      IntrinsicId.VecMap1,
      IntrinsicId.VecMap2,
      IntrinsicId.VecFilter1,
      IntrinsicId.VecFilter2,
      IntrinsicId.VecFind1,
      IntrinsicId.VecFind2,
      IntrinsicId.VecFindIndex1,
      IntrinsicId.VecFindIndex2,
      IntrinsicId.VecAny1,
      IntrinsicId.VecAny2,
      IntrinsicId.VecAll1,
      IntrinsicId.VecAll2,
      IntrinsicId.VecCount1,
      IntrinsicId.VecCount2,
      IntrinsicId.MapKeys,
      IntrinsicId.MapValues,
      IntrinsicId.MapRemove,
      IntrinsicId.SetAdd,
      IntrinsicId.SetRemove,
      IntrinsicId.SetAny,
      IntrinsicId.SetAll,
      IntrinsicId.SetEach,
      IntrinsicId.Println,
      IntrinsicId.RegexCompile,
      IntrinsicId.RegexHasMatch,
      IntrinsicId.RegexMatchPrefix,
      IntrinsicId.RegexFind,
      IntrinsicId.RegexFindAll,
      IntrinsicId.RegexReplace,
    ];

    List<CallExpressionSyntax> calls = All<CallExpressionSyntax>(compilation);
    Assert.Equal(expectedCallIds.Length, calls.Count);
    for (int i = 0; i < expectedCallIds.Length; i++) {
      CallResolution? res = compilation.RootModel.GetCallResolution(calls[i]);
      Assert.NotNull(res);
      Assert.Equal(CallKind.Intrinsic, res!.Kind);
      Assert.Equal(expectedCallIds[i], res.Intrinsic);
    }

    Assert.Same(compilation.Types.GetVector(compilation.Types.String), Local(compilation, "vm1").Type);
    Assert.Same(compilation.Types.GetVector(compilation.Types.Int), Local(compilation, "vm2").Type);
    Assert.Same(compilation.Types.GetVector(compilation.Types.String), Local(compilation, "mkeys").Type);
    Assert.Same(compilation.Types.GetVector(compilation.Types.Int), Local(compilation, "mvals").Type);
    Assert.Same(
        compilation.Types.GetUnion(compilation.Prelude.OptionUnion, [compilation.Types.Int]),
        Local(compilation, "mrem").Type);
    Assert.Same(compilation.Types.String, Local(compilation, "rpat").Type);
    Assert.Same(compilation.Types.GetVector(compilation.Types.String), Local(compilation, "rfa").Type);
  }

  [Fact]
  public void ConformanceCallAndTypeErrCases_MatchErrTexts() {
    string repoRoot = ParserTestHelpers.FindRepositoryRoot();
    string conformanceRoot = Path.Combine(repoRoot, "conformance");

    string[] cases = [
      "functions/err-call-arity.pino",
      "functions/err-return-type-mismatch.pino",
      "types/err-float-to-int-argument.pino",
      "types/err-float-to-int-return.pino",
      "unions/err-constructor-arg-type.pino",
      "variables/inference-string-rejected.pino",
    ];

    foreach (string relPath in cases) {
      string fullPath = Path.Combine(conformanceRoot, relPath);
      string expectedText = File.ReadAllText(Path.ChangeExtension(fullPath, ".err")).Trim();
      ParseResult parsed = Parser.Parse(new SourceText(File.ReadAllText(fullPath), fullPath));
      Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);

      Assert.Contains(
          compilation.Diagnostics,
          d => d.Severity == DiagnosticSeverity.Error
              && d.Message.Contains(expectedText, StringComparison.Ordinal));
    }
  }
}
