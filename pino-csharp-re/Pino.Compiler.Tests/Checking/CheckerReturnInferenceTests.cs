using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerReturnInferenceTests {
  private static FunctionSymbol FunctionOf(Compilation compilation, string name) {
    FunctionDeclarationSyntax decl = All<FunctionDeclarationSyntax>(compilation)
        .Single(f => TextOf(compilation, f.NameToken) == name);
    return Assert.IsType<FunctionSymbol>(compilation.RootModel.GetDeclaredSymbol(decl));
  }

  private static MethodSymbol MethodOf(Compilation compilation, string name) {
    MethodDeclarationSyntax decl = All<MethodDeclarationSyntax>(compilation)
        .Single(m => TextOf(compilation, m.NameToken) == name);
    return Assert.IsType<MethodSymbol>(compilation.RootModel.GetDeclaredSymbol(decl));
  }

  /// <summary>
  /// Verifies the ticket 038 invariant: zero <see cref="PendingReturnType"/> instances ever escape
  /// into <see cref="SemanticModel"/> (node types, conversions, declared/referenced symbols, or scopes).
  /// </summary>
  private static void AssertNoPendingReturnTypesInModel(Compilation compilation) {
    SemanticModel model = compilation.RootModel;
    var visitedTypes = new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);

    void CheckType(TypeSymbol? type) {
      if (type is null || !visitedTypes.Add(type)) {
        return;
      }

      Assert.False(
          string.Equals(type.GetType().Name, "PendingReturnType", StringComparison.Ordinal),
          $"PendingReturnType leaked into SemanticModel: {type.DisplayName}");

      switch (type) {
        case VectorType vec:
          CheckType(vec.ElementType);
          break;
        case MapType map:
          CheckType(map.KeyType);
          CheckType(map.ValueType);
          break;
        case SetType set:
          CheckType(set.ElementType);
          break;
        case TupleType tuple:
          foreach (TupleField f in tuple.Fields) {
            CheckType(f.Type);
          }
          break;
        case FunctionType fn:
          foreach (FunctionParameter p in fn.Parameters) {
            CheckType(p.Type);
          }
          CheckType(fn.ReturnType);
          break;
        case StructType st:
          foreach (TypeSymbol a in st.TypeArguments) {
            CheckType(a);
          }
          break;
        case UnionType ut:
          foreach (TypeSymbol a in ut.TypeArguments) {
            CheckType(a);
          }
          break;
        case InterfaceType it:
          foreach (TypeSymbol a in it.TypeArguments) {
            CheckType(a);
          }
          break;
      }
    }

    void CheckSymbol(Symbol? symbol) {
      switch (symbol) {
        case LocalSymbol local:
          CheckType(local.Type);
          break;
        case ParameterSymbol param:
          CheckType(param.Type);
          break;
        case FieldSymbol field:
          CheckType(field.Type);
          break;
        case FunctionSymbol fn:
          foreach (ParameterSymbol p in fn.Parameters) {
            CheckType(p.Type);
          }
          CheckType(fn.ReturnType);
          break;
        case MethodSymbol method:
          foreach (ParameterSymbol p in method.Parameters) {
            CheckType(p.Type);
          }
          CheckType(method.ReturnType);
          break;
      }
    }

    foreach (SyntaxNode node in All<SyntaxNode>(compilation)) {
      CheckType(model.GetType(node));
      CheckSymbol(model.GetDeclaredSymbol(node));
      CheckSymbol(model.GetReferencedSymbol(node));
      if (model.GetConversion(node) is { } conv) {
        CheckType(conv.TargetType);
        CheckType(conv.ImplicitLambdaType);
        CheckSymbol(conv.ImplicitItSymbol);
      }

      if (model.GetScope(node) is { } scope) {
        foreach (Symbol sym in scope.DeclaredSymbols.Values) {
          CheckSymbol(sym);
        }
      }

      if (model.GetCaptures(node) is { } captures) {
        foreach (CaptureEntry entry in captures.Entries) {
          CheckSymbol(entry.Symbol);
        }
      }
    }
  }

  [Fact]
  public void SelfRecursion_D050_EarlyLateAndLocalValBaseCases_InferIntWithZeroPendingTypes() {
    Compilation compilation = Compile("""
      fn fact_early(n int) {
        if n == 0 {
          return 1
        }
        return n * fact_early(n - 1)
      }

      fn fact_late(n int) {
        if n > 0 {
          return n * fact_late(n - 1)
        }
        return 1
      }

      fn fact_local(n int) {
        val base = 1
        if n > 0 {
          val sub = fact_local(n - 1)
          return n * sub
        }
        return base
      }

      val r1 = fact_early(5)
      val r2 = fact_late(5)
      val r3 = fact_local(5)
      """);

    Assert.Empty(CheckerErrors(compilation));
    Assert.Same(compilation.Types.Int, FunctionOf(compilation, "fact_early").ReturnType);
    Assert.Equal(ReturnInferenceState.Resolved, FunctionOf(compilation, "fact_early").ReturnState);
    Assert.Same(compilation.Types.Int, FunctionOf(compilation, "fact_late").ReturnType);
    Assert.Same(compilation.Types.Int, FunctionOf(compilation, "fact_local").ReturnType);
    Assert.Same(compilation.Types.Int, Local(compilation, "r1").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "r2").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "r3").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "sub").Type);
    AssertNoPendingReturnTypesInModel(compilation);
  }

  [Fact]
  public void MutualRecursion_D050_BothDeclarationOrdersAndEarlyLateBaseCases_Converge() {
    Compilation compilation = Compile("""
      # Early base case (even first)
      fn is_even(n int) {
        if n == 0 {
          return true
        }
        return is_odd(n - 1)
      }
      fn is_odd(n int) {
        if n == 0 {
          return false
        }
        return is_even(n - 1)
      }

      # Late base case, forward-calling order (b before a)
      fn b_first(n int) {
        return a_second(n)
      }
      fn a_second(n int) {
        if n > 0 {
          return b_first(n - 1)
        }
        return 1
      }

      # Late base case, reverse order (a before b)
      fn a_first(n int) {
        if n > 0 {
          return b_second(n - 1)
        }
        return "done"
      }
      fn b_second(n int) {
        return a_first(n)
      }

      val e = is_even(10)
      val o = is_odd(7)
      val num = b_first(4)
      val msg = b_second(3)
      """);

    Assert.Empty(CheckerErrors(compilation));
    Assert.Same(compilation.Types.Bool, FunctionOf(compilation, "is_even").ReturnType);
    Assert.Same(compilation.Types.Bool, FunctionOf(compilation, "is_odd").ReturnType);
    Assert.Same(compilation.Types.Int, FunctionOf(compilation, "b_first").ReturnType);
    Assert.Same(compilation.Types.Int, FunctionOf(compilation, "a_second").ReturnType);
    Assert.Same(compilation.Types.String, FunctionOf(compilation, "a_first").ReturnType);
    Assert.Same(compilation.Types.String, FunctionOf(compilation, "b_second").ReturnType);
    Assert.Same(compilation.Types.Bool, Local(compilation, "e").Type);
    Assert.Same(compilation.Types.Bool, Local(compilation, "o").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "num").Type);
    Assert.Same(compilation.Types.String, Local(compilation, "msg").Type);
    AssertNoPendingReturnTypesInModel(compilation);
  }

  [Fact]
  public void UnresolvedRecursionCycle_FNE4_EmitsPN3104AndSetsErrorType() {
    Compilation selfCycle = Compile("""
      fn inf(n int) {
        return inf(n)
      }
      val x = inf(1)
      """);

    Diagnostic err = Assert.Single(CheckerErrors(selfCycle));
    Assert.Equal(DiagnosticCodes.CannotInferReturnType, err.Code);
    Assert.Contains("explicit return type", err.Message);
    Assert.Same(selfCycle.Types.Error, FunctionOf(selfCycle, "inf").ReturnType);
    Assert.Same(selfCycle.Types.Error, Local(selfCycle, "x").Type);
    AssertNoPendingReturnTypesInModel(selfCycle);

    Compilation mutualCycle = Compile("""
      fn ping(n int) {
        return pong(n)
      }
      fn pong(n int) {
        return ping(n)
      }
      """);

    Diagnostic[] mutualErrs = CheckerErrors(mutualCycle);
    Assert.Equal(2, mutualErrs.Length);
    Assert.All(mutualErrs, static d => {
      Assert.Equal(DiagnosticCodes.CannotInferReturnType, d.Code);
      Assert.Contains("explicit return type", d.Message);
    });
    Assert.Same(mutualCycle.Types.Error, FunctionOf(mutualCycle, "ping").ReturnType);
    Assert.Same(mutualCycle.Types.Error, FunctionOf(mutualCycle, "pong").ReturnType);
    AssertNoPendingReturnTypesInModel(mutualCycle);
  }

  [Fact]
  public void MixedReturns_FN29AndFNE5AndOQ12_WidensIntFloatAndRejectsConflicts() {
    Compilation okMix = Compile("""
      fn mix(b bool) {
        if b {
          return 1
        }
        return 2.5
      }

      fn rec_mix(n int) {
        if n > 1 {
          return rec_mix(n - 1)
        }
        if n == 1 {
          return 1
        }
        return 0.5
      }

      val a = mix(true)
      val b = rec_mix(3)
      """);

    Assert.Empty(CheckerErrors(okMix));
    Assert.Same(okMix.Types.Float, FunctionOf(okMix, "mix").ReturnType);
    Assert.Same(okMix.Types.Float, FunctionOf(okMix, "rec_mix").ReturnType);
    Assert.Same(okMix.Types.Float, Local(okMix, "a").Type);
    Assert.Same(okMix.Types.Float, Local(okMix, "b").Type);

    // Every int-typed return expression in both functions gets ConversionKind.IntToFloat.
    List<ReturnStatementSyntax> returns = All<ReturnStatementSyntax>(okMix);
    AssertIntToFloat(okMix, returns[0].Expression!);
    AssertNoConversion(okMix, returns[1].Expression!);
    AssertIntToFloat(okMix, returns[3].Expression!);
    AssertNoConversion(okMix, returns[4].Expression!);
    AssertNoPendingReturnTypesInModel(okMix);

    // FN-E5 struct conflict with RelatedLocation and interface help.
    Compilation structClash = Compile("""
      struct Dog { name string }
      struct Cat { name string }
      fn clash(b bool) {
        if b {
          return Dog { name: "Rex" }
        }
        return Cat { name: "Tom" }
      }
      """);

    Diagnostic structErr = Assert.Single(CheckerErrors(structClash));
    Assert.Equal(DiagnosticCodes.ConflictingReturnTypes, structErr.Code);
    Assert.Contains("conflicting return types", structErr.Message);
    Assert.NotEmpty(structErr.Related);
    Assert.NotNull(structErr.Help);
    Assert.Contains("interface", structErr.Help!, StringComparison.OrdinalIgnoreCase);
    Assert.Same(structClash.Types.Error, FunctionOf(structClash, "clash").ReturnType);
    AssertNoPendingReturnTypesInModel(structClash);

    // OQ-12: int/string and Struct/Interface conflicts emit PN3105.
    Compilation otherConflicts = Compile("""
      interface Shape { fn area() int }
      struct Square {
        side int
        fn area() int { return side * side }
      }

      fn int_or_str(b bool) {
        if b { return 1 }
        return "x"
      }

      fn struct_or_iface(s Shape, b bool) {
        if b { return s }
        return Square { side: 1 }
      }
      """);

    Diagnostic[] errs = CheckerErrors(otherConflicts);
    Assert.Equal(2, errs.Length);
    Assert.All(errs, static d => {
      Assert.Equal(DiagnosticCodes.ConflictingReturnTypes, d.Code);
      Assert.Contains("conflicting return types", d.Message);
      Assert.NotEmpty(d.Related);
    });
    Assert.Same(otherConflicts.Types.Error, FunctionOf(otherConflicts, "int_or_str").ReturnType);
    Assert.Same(otherConflicts.Types.Error, FunctionOf(otherConflicts, "struct_or_iface").ReturnType);
    AssertNoPendingReturnTypesInModel(otherConflicts);
  }

  [Fact]
  public void BareReturnAndVoidInference_OQ13AndFNE7AndHoisting_WorkCorrectly() {
    // OQ-13 Option 1: bare return + value return infers value type and emits PN3403.
    Compilation bareWithValue = Compile("""
      fn f(b bool) {
        if b {
          return
        }
        return 1
      }
      val x = f(false)
      """);

    Diagnostic bareErr = Assert.Single(CheckerErrors(bareWithValue));
    Assert.Equal(DiagnosticCodes.BareReturnInNonVoidFunction, bareErr.Code);
    Assert.Contains("must return a value", bareErr.Message);
    Assert.Same(bareWithValue.Types.Int, FunctionOf(bareWithValue, "f").ReturnType);
    Assert.Same(bareWithValue.Types.Int, Local(bareWithValue, "x").Type);
    AssertNoPendingReturnTypesInModel(bareWithValue);

    // Hoisted void function and recursive void function with bare return.
    Compilation voidFuncs = Compile("""
      greet("Pino")
      countdown(3)

      fn greet(name string) {
        println("hello $name")
      }

      fn countdown(n int) {
        if n <= 0 {
          return
        }
        countdown(n - 1)
      }
      """);

    Assert.Empty(CheckerErrors(voidFuncs));
    Assert.Same(voidFuncs.Types.Void, FunctionOf(voidFuncs, "greet").ReturnType);
    Assert.Same(voidFuncs.Types.Void, FunctionOf(voidFuncs, "countdown").ReturnType);
    AssertNoPendingReturnTypesInModel(voidFuncs);
  }

  [Fact]
  public void NestedBlockHoistedRecursionAndStructMethodsUsingSelf_InferReturnTypesCorrectly() {
    Compilation compilation = Compile("""
      # D-165: nested-block hoisted recursive unannotated function
      fn outer() {
        val r = inner(4)
        fn inner(n int) {
          if n > 0 {
            return n * inner(n - 1)
          }
          return 1
        }
        return r
      }

      # External call to unannotated struct method using `self` before struct declaration (hoisted)
      val c = Counter { value: 21 }
      val doubled = c:doubled()
      val rec_sum = c:sum_down(4)

      # Unannotated embedded method promoted to outer struct
      val b = Boxed { value: 10, label: "ok" }
      val promoted = b:doubled()

      # Interface satisfied by struct with unannotated method
      interface HasArea {
        fn area() int
      }
      struct Square {
        side int
        fn area() {
          return self:side * side
        }
      }
      val shape HasArea = Square { side: 5 }

      struct Counter {
        value int
        fn doubled() {
          return self:value * 2
        }
        fn sum_down(n int) {
          if n > 0 {
            return n + self:sum_down(n - 1)
          }
          return self:value
        }
      }

      struct Boxed {
        Counter
        label string
      }

      val o = outer()
      """);

    Assert.Empty(CheckerErrors(compilation));
    Assert.Same(compilation.Types.Int, FunctionOf(compilation, "outer").ReturnType);
    Assert.Same(compilation.Types.Int, FunctionOf(compilation, "inner").ReturnType);
    Assert.Same(compilation.Types.Int, Local(compilation, "o").Type);
    Assert.Same(compilation.Types.Int, MethodOf(compilation, "doubled").ReturnType);
    Assert.Same(compilation.Types.Int, MethodOf(compilation, "sum_down").ReturnType);
    Assert.Same(compilation.Types.Int, MethodOf(compilation, "area").ReturnType);
    Assert.Same(compilation.Types.Int, Local(compilation, "doubled").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "rec_sum").Type);
    Assert.Same(compilation.Types.Int, Local(compilation, "promoted").Type);
    AssertNoPendingReturnTypesInModel(compilation);
  }

  [Fact]
  public void ConformanceReturnInferenceCases_PassPositivesAndMatchErrTexts() {
    string repoRoot = ParserTestHelpers.FindRepositoryRoot();
    string conformanceRoot = Path.Combine(repoRoot, "conformance");

    string[] positiveCases = [
      "functions/infer-return-type.pino",
      "functions/infer-late-base-case.pino",
      "functions/infer-mutual-recursion.pino",
      "functions/infer-float-from-mixed-returns.pino",
      "functions/return-bare-before-brace.pino",
    ];

    foreach (string relPath in positiveCases) {
      string fullPath = Path.Combine(conformanceRoot, relPath);
      ParseResult parsed = Parser.Parse(new SourceText(File.ReadAllText(fullPath), fullPath));
      Assert.Empty(parsed.Diagnostics);
      Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
      Assert.Empty(CheckerErrors(compilation));
      AssertNoPendingReturnTypesInModel(compilation);
    }

    string[] errCases = [
      "functions/err-recursion-no-return-type.pino",
      "functions/err-conflicting-struct-returns.pino",
      "functions/err-mixed-return-int-string.pino",
      "functions/err-mixed-return-struct-interface.pino",
      "functions/err-bare-return-with-value.pino",
    ];

    foreach (string relPath in errCases) {
      string fullPath = Path.Combine(conformanceRoot, relPath);
      string expectedText = File.ReadAllText(Path.ChangeExtension(fullPath, ".err")).Trim();
      ParseResult parsed = Parser.Parse(new SourceText(File.ReadAllText(fullPath), fullPath));
      Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);

      Assert.Contains(
          compilation.Diagnostics,
          d => d.Severity == DiagnosticSeverity.Error
              && d.Message.Contains(expectedText, StringComparison.Ordinal));
      AssertNoPendingReturnTypesInModel(compilation);
    }
  }
}
