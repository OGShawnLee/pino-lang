using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

using static Pino.Compiler.Tests.Checking.CheckerTestSupport;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerMutFoundationsTests {
  [Fact]
  public void Phase2MutModifiers_PopulateSymbolsPromotedMembersAndPerMethodSelf() {
    Compilation compilation = Compile("""
      struct Base {
        ro_base int
        pub mut rw_base int

        fn read_base() int {
          return ro_base
        }

        mut fn write_base(v int) {
          rw_base = v
        }
      }

      struct Derived {
        Base
        ro_own string
        mut rw_own int

        fn read_own() string {
          return ro_own
        }

        mut fn write_own(mut delta int, readonly_arg int) {
          rw_own += delta + readonly_arg
        }
      }

      interface Tracked {
        ro_prop int
        mut rw_prop int
        fn inspect() int
        mut fn update(mut amount int) void
      }

      fn top_helper(mut x int, y string) {}
      """);

    Assert.Empty(CheckerErrors(compilation));
    SemanticModel model = compilation.RootModel;
    Scope modScope = model.GetScope(compilation.RootTree.Root)!;

    var baseSym = Assert.IsType<StructSymbol>(modScope.DeclaredSymbols["Base"]);
    Assert.False(baseSym.Fields.Single(static f => f.Name == "ro_base").IsMutable);
    Assert.True(baseSym.Fields.Single(static f => f.Name == "rw_base").IsMutable);
    Assert.False(baseSym.Methods.Single(static m => m.Name == "read_base").IsMutating);
    Assert.True(baseSym.Methods.Single(static m => m.Name == "write_base").IsMutating);

    var derivedSym = Assert.IsType<StructSymbol>(modScope.DeclaredSymbols["Derived"]);
    Assert.False(derivedSym.AllFields.Single(static f => f.Name == "ro_base").IsMutable);
    Assert.True(derivedSym.AllFields.Single(static f => f.Name == "rw_base").IsMutable);
    Assert.False(derivedSym.AllFields.Single(static f => f.Name == "ro_own").IsMutable);
    Assert.True(derivedSym.AllFields.Single(static f => f.Name == "rw_own").IsMutable);
    Assert.False(derivedSym.Methods.Single(static m => m.Name == "read_base").IsMutating);
    Assert.True(derivedSym.Methods.Single(static m => m.Name == "write_base").IsMutating);
    Assert.False(derivedSym.Methods.Single(static m => m.Name == "read_own").IsMutating);
    Assert.True(derivedSym.Methods.Single(static m => m.Name == "write_own").IsMutating);

    MethodSymbol writeOwn = derivedSym.Methods.Single(static m => m.Name == "write_own");
    Assert.True(writeOwn.Parameters[0].IsMutable);
    Assert.False(writeOwn.Parameters[1].IsMutable);

    // Per-method `self` binding reflects IsMutating per method scope.
    MethodDeclarationSyntax[] derivedMethods = All<MethodDeclarationSyntax>(compilation)
        .Where(m => TextOf(compilation, m.NameToken) is "read_own" or "write_own")
        .ToArray();
    Scope readScope = model.GetScope(derivedMethods[0])!;
    Scope writeScope = model.GetScope(derivedMethods[1])!;
    Assert.True(readScope.TryLookupLocal("self", out Symbol? readSelfSym));
    Assert.True(writeScope.TryLookupLocal("self", out Symbol? writeSelfSym));
    var readSelf = Assert.IsType<LocalSymbol>(readSelfSym);
    var writeSelf = Assert.IsType<LocalSymbol>(writeSelfSym);
    Assert.NotSame(readSelf, writeSelf);
    Assert.False(readSelf.IsMutable);
    Assert.True(writeSelf.IsMutable);

    var trackedIface = Assert.IsType<InterfaceSymbol>(modScope.DeclaredSymbols["Tracked"]);
    Assert.False(trackedIface.Properties.Single(static p => p.Name == "ro_prop").IsMutable);
    Assert.True(trackedIface.Properties.Single(static p => p.Name == "rw_prop").IsMutable);
    Assert.False(trackedIface.Methods.Single(static m => m.Name == "inspect").IsMutating);
    Assert.True(trackedIface.Methods.Single(static m => m.Name == "update").IsMutating);
    Assert.True(trackedIface.Methods.Single(static m => m.Name == "update").Parameters[0].IsMutable);

    var topHelper = Assert.IsType<FunctionSymbol>(modScope.DeclaredSymbols["top_helper"]);
    Assert.True(topHelper.Parameters[0].IsMutable);
    Assert.False(topHelper.Parameters[1].IsMutable);
  }

  [Fact]
  public void FunctionTypeMutParameters_EnforceNonConvertibilityContextualLambdasAndGenerics() {
    Compilation ok = Compile("""
      fn mutate_num(mut n int) {
        n += 1
      }

      fn read_num(n int) int {
        return n + 1
      }

      val f1 fn (mut int) -> void = mutate_num
      val f2 fn (int) -> int = read_num
      val f3 fn (mut int) -> void = fn (x) { x += 1 }
      val f4 fn (mut int) -> void = fn (mut x int) { x += 1 }

      @@Generic[T]
      fn accept_cb(cb fn (mut T) -> void, x T) {}
      accept_cb(fn (mut n int) {}, 10)
      """);

    Assert.Empty(CheckerErrors(ok));
    TypeInterner types = ok.Types;
    FunctionType mutIntToVoid = types.GetFunction([new FunctionParameter(types.Int, true)], types.Void);
    FunctionType immutIntToInt = types.GetFunction([new FunctionParameter(types.Int, false)], types.Int);
    Assert.Same(mutIntToVoid, Local(ok, "f1").Type);
    Assert.Same(immutIntToInt, Local(ok, "f2").Type);
    Assert.Same(mutIntToVoid, Local(ok, "f3").Type);
    Assert.Same(mutIntToVoid, Local(ok, "f4").Type);
    Assert.Equal("fn (mut int) -> void", mutIntToVoid.DisplayName);

    // Untyped contextual lambda parameter inherits IsMutable = true.
    LambdaExpressionSyntax inferredLambda = All<LambdaExpressionSyntax>(ok)[0];
    var inferredParam = Assert.IsType<ParameterSymbol>(ok.RootModel.GetDeclaredSymbol(inferredLambda.Parameters[0]));
    Assert.True(inferredParam.IsMutable);

    // Non-convertibility: fn (mut int) -> void != fn (int) -> void in all directions, plus typed/untyped mismatch.
    Compilation bad = Compile("""
      fn mutate_num(mut n int) {}
      fn read_num(n int) {}

      val a fn (int) -> void = mutate_num
      val b fn (mut int) -> void = read_num
      val c fn (mut int) -> void = fn (x int) {}
      val d fn (int) -> void = fn (mut x int) {}
      val e fn (int) -> void = fn (mut x) {}

      fn take_mut_cb(cb fn (mut int) -> int) {}
      take_mut_cb(it + 1)

      @@Generic[T]
      fn accept_mut_cb(cb fn (mut T) -> void, x T) {}
      accept_mut_cb(fn (n int) {}, 10)
      """);

    Diagnostic[] errors = CheckerErrors(bad);
    Assert.Equal(7, errors.Length);
    Assert.All(errors, static d => Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code));
  }

  [Fact]
  public void InterfaceMutMatching_IFACE6AndIFACEE7AndGenericBounds_CoverAllCombinations() {
    // 1. All valid combinations (including safe covariance: mut fn iface accepts plain fn; ro prop accepts mut field).
    Compilation valid = Compile("""
      interface ReadOnlyContract {
        val_prop int
        fn read_step(x int) int
      }

      interface MutableContract {
        mut rw_prop int
        mut fn write_step(mut x int) void
      }

      struct PureImpl {
        val_prop int
        rw_prop int
        fn read_step(x int) int { return x }
        fn write_step(mut x int) {}
      }

      struct MutPropForReadOnly {
        mut val_prop int
        fn read_step(x int) int { return x }
      }

      struct MutImpl {
        mut rw_prop int
        mut fn write_step(mut x int) {}
      }

      struct MutImplOnlyReadOnlyProp {
        mut val_prop int
        mut rw_prop int
        fn read_step(x int) int { return x }
        mut fn write_step(mut x int) {}
      }

      # PureImpl satisfies ReadOnlyContract; MutPropForReadOnly satisfies ReadOnlyContract (mut field -> ro prop OK).
      val c1 ReadOnlyContract = PureImpl { val_prop: 1, rw_prop: 2 }
      val c2 ReadOnlyContract = MutPropForReadOnly { val_prop: 3 }

      # MutImpl satisfies MutableContract (mut fn -> mut fn, mut field -> mut prop).
      val c3 MutableContract = MutImpl { rw_prop: 4 }

      # PureImpl with plain fn satisfies MutableContract's mut fn when rw_prop is mut.
      struct PlainMethodMutField {
        mut rw_prop int
        fn write_step(mut x int) {}
      }
      val c4 MutableContract = PlainMethodMutField { rw_prop: 5 }

      # Generic interface bound with mut fn & mut prop (GEN-8 / GEN-10).
      @@Generic[T is MutableContract]
      fn use_mut_bound(item T) int {
        return item:rw_prop
      }
      val b1 = use_mut_bound(MutImpl { rw_prop: 6 })
      val b2 = use_mut_bound(PlainMethodMutField { rw_prop: 7 })
      """);

    Assert.Empty(CheckerErrors(valid));

    // 2. All invalid combinations:
    //    - mut fn struct -> non-mut interface method (PN3108, detail: method '...' must not be 'mut')
    //    - read-only struct field -> mut interface property (PN3108, detail: field '...' must be 'mut')
    //    - param mut mismatch on method (fn (mut int) vs fn (int)) (PN3108)
    //    - param mut mismatch on callable field (STRUCT-12) (PN3108)
    //    - generic constraint violated due to mut mismatch (PN3107)
    Compilation invalid = Compile("""
      interface ReadOnlyIface {
        fn step() void
      }

      interface MutPropIface {
        mut count int
      }

      interface MutParamIface {
        fn apply(mut x int) void
      }

      struct BadMutMethod {
        mut fn step() {}
      }

      struct BadReadOnlyField {
        count int
      }

      struct BadParamMismatch {
        fn apply(x int) {}
      }

      struct BadCallableField {
        apply fn (int) -> void
      }

      @@Generic[T is ReadOnlyIface]
      fn run_ro(x T) {}

      @@Generic[T is MutPropIface]
      fn run_mut_prop(x T) {}

      interface SuperIface {
        ro_val int
        mut fn step() void
      }

      interface SubIfaceOk {
        mut ro_val int
        fn step() void
      }

      interface SubIfaceBadMethod {
        ro_val int
        mut fn step() void
      }

      interface SuperRoIface {
        fn step() void
      }

      interface SuperMutPropIface {
        mut count int
      }

      interface SubIfaceRoProp {
        count int
      }

      fn accept_super(s SuperIface) {}
      fn accept_super_ro(s SuperRoIface) {}
      fn accept_super_mut_prop(s SuperMutPropIface) {}

      fn check_iface_subtyping(ok SubIfaceOk, badM SubIfaceBadMethod, badP SubIfaceRoProp) {
        accept_super(ok)
        accept_super_ro(badM)
        accept_super_mut_prop(badP)
      }

      val i1 ReadOnlyIface = BadMutMethod {}
      val i2 MutPropIface = BadReadOnlyField { count: 1 }
      val i3 MutParamIface = BadParamMismatch {}
      val i4 MutParamIface = BadCallableField { apply: fn (x int) {} }
      run_ro(BadMutMethod {})
      run_mut_prop(BadReadOnlyField { count: 1 })
      """);

    Diagnostic[] errors = CheckerErrors(invalid);
    Assert.Equal(8, errors.Length);

    Assert.Contains(errors, static d =>
        d.Code == DiagnosticCodes.InterfaceNotSatisfied
        && d.Message.Contains("expected type 'ReadOnlyIface'", StringComparison.Ordinal)
        && d.Message.Contains("method 'step' must not be 'mut'", StringComparison.Ordinal));
    Assert.Contains(errors, static d =>
        d.Code == DiagnosticCodes.InterfaceNotSatisfied
        && d.Message.Contains("expected type 'MutPropIface'", StringComparison.Ordinal)
        && d.Message.Contains("field 'count' must be 'mut'", StringComparison.Ordinal));
    Assert.Contains(errors, static d =>
        d.Code == DiagnosticCodes.InterfaceNotSatisfied
        && d.Message.Contains("expected type 'SuperRoIface'", StringComparison.Ordinal)
        && d.Message.Contains("method 'step' must not be 'mut'", StringComparison.Ordinal));
    Assert.Contains(errors, static d =>
        d.Code == DiagnosticCodes.InterfaceNotSatisfied
        && d.Message.Contains("expected type 'SuperMutPropIface'", StringComparison.Ordinal)
        && d.Message.Contains("field 'count' must be 'mut'", StringComparison.Ordinal));
    Assert.Equal(
        2,
        errors.Count(static d =>
            d.Code == DiagnosticCodes.InterfaceNotSatisfied
            && d.Message.Contains("expected type 'MutParamIface'", StringComparison.Ordinal)));
    Assert.Contains(errors, static d =>
        d.Code == DiagnosticCodes.GenericConstraintNotSatisfied
        && d.Message.Contains("method 'step' must not be 'mut'", StringComparison.Ordinal));
    Assert.Contains(errors, static d =>
        d.Code == DiagnosticCodes.GenericConstraintNotSatisfied
        && d.Message.Contains("field 'count' must be 'mut'", StringComparison.Ordinal));
  }

  [Fact]
  public void MutationPlaceSideTable_NullSafetyAndContractsWorkCorrectly() {
    Compilation compilation = Compile("val x = 1\n");
    SemanticModel model = compilation.RootModel;

    Assert.Throws<ArgumentNullException>(() => model.GetMutationPlace(null!));
    Assert.Null(model.GetMutationPlace(compilation.RootTree.Root));

    var outOfRange = new IdentifierExpressionSyntax(
        compilation.RootTree.NodeCount + 42,
        SourceSpan.Empty,
        new Pino.Compiler.Lexing.Token(Pino.Compiler.Lexing.TokenKind.Identifier, SourceSpan.Empty));
    Assert.Null(model.GetMutationPlace(outOfRange));

    // Verify all AD-003 MutationKind / MutationRootKind enum values and record construction.
    Assert.Equal(6, Enum.GetValues<MutationKind>().Length);
    Assert.Equal(5, Enum.GetValues<MutationRootKind>().Length);
    var dummySym = new LocalSymbol("v", compilation.Types.Int, true, LocalKind.Variable, 0, SourceSpan.Empty, null);
    var info = new MutationPlaceInfo(MutationKind.MutatingCall, dummySym, MutationRootKind.MutParameter, true, true);
    Assert.Equal(MutationKind.MutatingCall, info.Kind);
    Assert.Same(dummySym, info.RootSymbol);
    Assert.Equal(MutationRootKind.MutParameter, info.RootKind);
    Assert.True(info.RequiresCoWCheck);
    Assert.True(info.PassByInOut);
  }
}
