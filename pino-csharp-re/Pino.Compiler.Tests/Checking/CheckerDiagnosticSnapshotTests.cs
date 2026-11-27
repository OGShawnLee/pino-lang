using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerDiagnosticSnapshotTests {
  private static string RenderPlain(string code, string fileName = "sample.pino") {
    var source = new SourceText(code, fileName);
    ParseResult parsed = Parser.Parse(source);
    Assert.Empty(parsed.Diagnostics);
    Compilation compilation = Compilation.Create(parsed);
    var options = new DiagnosticRenderOptions {
      Color = false,
      Voice = DiagnosticVoice.Plain,
    };
    return DiagnosticRenderer.RenderAll(compilation.RootModel.Diagnostics, source, options);
  }

  [Fact]
  public void Snapshot_BareIdentifierInWhen_D034_IncludesHelp() {
    const string Code = "fn f(x int) int {\n  return match x {\n    when 1 { yield 1 }\n    when fallback { yield 0 }\n    else { yield 0 }\n  }\n}\n";
    string rendered = RenderPlain(Code);
    const string Expected =
        "error[PN3314]: Cannot use bare identifier 'fallback' as a 'when' pattern; use 'else' for a catch-all branch.\n" +
        " --> sample.pino:4:10\n" +
        "  |\n" +
        "4 |     when fallback { yield 0 }\n" +
        "  |          ^^^^^^^^\n" +
        "  = help: replace this 'when' arm with 'else' if you intended a catch-all branch\n";
    Assert.Equal(Expected, rendered);
  }

  [Fact]
  public void Snapshot_CompoundDivideOnInt_VAR_E9_IncludesHelp() {
    const string Code = "var n = 10\nn /= 2\n";
    string rendered = RenderPlain(Code);
    const string Expected =
        "error[PN3102]: Cannot use '/=' on an 'int' target because '/' produces a 'float'; use '//=' for integer division.\n" +
        " --> sample.pino:2:1\n" +
        "  |\n" +
        "2 | n /= 2\n" +
        "  | ^^^^^^\n" +
        "  = help: replace '/=' with '//=' for integer floor division\n";
    Assert.Equal(Expected, rendered);
  }

  [Fact]
  public void Snapshot_PubVarRejected_MOD_E9_IncludesHelp() {
    const string Code = "pub var count = 0\n";
    string rendered = RenderPlain(Code);
    const string Expected =
        "error[PN3007]: Top-level 'var' cannot be marked 'pub'; use 'pub val' or expose it through a 'pub fn'.\n" +
        " --> sample.pino:1:1\n" +
        "  |\n" +
        "1 | pub var count = 0\n" +
        "  | ^^^\n" +
        "  = help: use 'pub val' for an exported constant, or keep 'var' private and expose a 'pub fn'\n";
    Assert.Equal(Expected, rendered);
  }

  [Fact]
  public void Snapshot_ConstantFalseLoopWarning_LOOP_16() {
    const string Code = "for 1 > 2 {\n  println(\"never\")\n}\n";
    string rendered = RenderPlain(Code);
    const string Expected =
        "warning[PN3550]: Loop condition is always 'false'; the loop body will never execute.\n" +
        " --> sample.pino:1:5\n" +
        "  |\n" +
        "1 | for 1 > 2 {\n" +
        "  |     ^^^^^\n";
    Assert.Equal(Expected, rendered);
  }

  [Fact]
  public void Snapshot_MissingReturnPath_D050() {
    const string Code = "fn sign(x int) int {\n  if x > 0 {\n    return 1\n  }\n}\n";
    string rendered = RenderPlain(Code);
    const string Expected =
        "error[PN3404]: Function 'sign' does not always return a value of type 'int' on all code paths.\n" +
        " --> sample.pino:1:4\n" +
        "  |\n" +
        "1 | fn sign(x int) int {\n" +
        "  |    ^^^^\n";
    Assert.Equal(Expected, rendered);
  }

  [Fact]
  public void Snapshot_DirectRecursiveStruct_PN3114_IncludesRelatedAndHelp() {
    const string Code = "struct Bad { x Bad }\n";
    string rendered = RenderPlain(Code);
    const string Expected =
        "error[PN3114]: Struct 'Bad' cannot contain itself directly because it would have infinite size.\n" +
        " --> sample.pino:1:8\n" +
        "  |\n" +
        "1 | struct Bad { x Bad }\n" +
        "  |        ^^^\n" +
        "  |              - field 'x' refers back to 'Bad' here\n" +
        "  = help: wrap the field in 'Option[Bad]', store it in '[]Bad' or a recursive union, or use 'Handle[Bad]' with a 'Pool[Bad]'\n";
    Assert.Equal(Expected, rendered);
  }

  [Fact]
  public void Snapshot_DeepValMutation_PN3220_IncludesRelatedAndHelp() {
    const string Code = "val xs = [1, 2]\nxs:push(3)\n";
    string rendered = RenderPlain(Code);
    const string Expected =
        "error[PN3220]: Cannot mutate immutable variable 'xs' of type '[]int'.\n" +
        " --> sample.pino:2:1\n" +
        "  |\n" +
        "1 | val xs = [1, 2]\n" +
        "  |     -- 'xs' is declared immutable here\n" +
        "2 | xs:push(3)\n" +
        "  | ^^^^^^^^^^\n" +
        "  = help: declare 'xs' with 'val mut' to change its contents, or with 'var' to also reassign it\n";
    Assert.Equal(Expected, rendered);
  }

  [Fact]
  public void Snapshot_ReadOnlyFieldMutation_PN3221_IncludesRelatedAndHelp() {
    const string Code = "struct Point {\n  x int\n}\nvar p = Point { x: 1 }\np:x = 2\n";
    string rendered = RenderPlain(Code);
    const string Expected =
        "error[PN3221]: Cannot mutate read-only field 'x' of struct 'Point'.\n" +
        " --> sample.pino:5:1\n" +
        "  |\n" +
        "2 |   x int\n" +
        "  |   - field 'x' is declared read-only here\n" +
        "...\n" +
        "5 | p:x = 2\n" +
        "  | ^^^^^^^\n" +
        "  = help: add 'mut' to field 'x' in struct 'Point' to allow mutation\n";
    Assert.Equal(Expected, rendered);
  }

  [Fact]
  public void Snapshot_MutateSelfInNonMutMethod_PN3222_IncludesRelatedAndHelp() {
    const string Code = "struct Counter {\n  mut n int\n  fn inc() {\n    self:n += 1\n  }\n}\n";
    string rendered = RenderPlain(Code);
    const string Expected =
        "error[PN3222]: Cannot mutate 'self' inside non-mutating method 'Counter:inc'.\n" +
        " --> sample.pino:4:5\n" +
        "  |\n" +
        "3 |   fn inc() {\n" +
        "  |      --- method 'inc' is declared non-mutating here\n" +
        "4 |     self:n += 1\n" +
        "  |     ^^^^^^^^^^^\n" +
        "  = help: mark the method as 'mut fn inc(...)'\n";
    Assert.Equal(Expected, rendered);
  }

  [Fact]
  public void Snapshot_MissingAndOverlappingMutCallArgs_PN3223AndPN3226_IncludeRelatedAndHelp() {
    const string MissingCode = "fn bump(mut n int) {\n  n += 1\n}\nvar a = 1\nbump(a)\n";
    string renderedMissing = RenderPlain(MissingCode);
    const string ExpectedMissing =
        "error[PN3223]: Argument 1 for function 'bump' is a 'mut' parameter and must be marked 'mut' at the call site.\n" +
        " --> sample.pino:5:6\n" +
        "  |\n" +
        "1 | fn bump(mut n int) {\n" +
        "  |             - parameter 'n' is declared 'mut' here\n" +
        "...\n" +
        "5 | bump(a)\n" +
        "  |      ^\n" +
        "  = help: write 'mut a' at this call site\n";
    Assert.Equal(ExpectedMissing, renderedMissing);

    const string OverlapCode = "fn swap(mut a int, mut b int) {\n  val t = a\n  a = b\n  b = t\n}\nvar x = 1\nswap(mut x, mut x)\n";
    string renderedOverlap = RenderPlain(OverlapCode);
    const string ExpectedOverlap =
        "error[PN3226]: Cannot use 'x' twice as 'mut' in one call to 'swap'.\n" +
        " --> sample.pino:7:13\n" +
        "  |\n" +
        "7 | swap(mut x, mut x)\n" +
        "  |             ^^^^^\n" +
        "  |      ----- 'x' is already mutably borrowed here\n" +
        "  = help: pass it once as 'mut', or pass a copy for the other argument\n";
    Assert.Equal(ExpectedOverlap, renderedOverlap);
  }

  [Fact]
  public void Snapshot_UnexpectedInvalidAndBoundMut_PN3224PN3225PN3227_IncludeHelp() {
    const string UnexpectedCode = "fn read(n int) {}\nvar x = 1\nread(mut x)\n";
    const string ExpectedUnexpected =
        "error[PN3224]: Argument 1 for function 'read' is not a 'mut' parameter; remove 'mut' at the call site.\n" +
        " --> sample.pino:3:6\n" +
        "  |\n" +
        "1 | fn read(n int) {}\n" +
        "  |         - parameter 'n' is not declared 'mut' here\n" +
        "...\n" +
        "3 | read(mut x)\n" +
        "  |      ^^^^^\n" +
        "  = help: remove 'mut' before 'x'\n";
    Assert.Equal(ExpectedUnexpected, RenderPlain(UnexpectedCode));

    const string RvalueCode = "[1, 2]:push(3)\n";
    const string ExpectedRvalue =
        "error[PN3225]: Cannot mutate immutable or temporary expression.\n" +
        " --> sample.pino:1:1\n" +
        "  |\n" +
        "1 | [1, 2]:push(3)\n" +
        "  | ^^^^^^^^^^^^^^\n" +
        "  = help: bind the value to a 'var' first if you need to mutate it\n";
    Assert.Equal(ExpectedRvalue, RenderPlain(RvalueCode));

    const string BoundCode = "struct Counter {\n  mut n int\n  mut fn inc() {\n    n += 1\n  }\n}\nvar c = Counter { n: 0 }\nval f = c:inc\n";
    const string ExpectedBound =
        "error[PN3227]: Cannot bind mutating method 'Counter:inc' as a standalone function value.\n" +
        " --> sample.pino:8:9\n" +
        "  |\n" +
        "3 |   mut fn inc() {\n" +
        "  |          --- mutating method 'inc' is declared here\n" +
        "...\n" +
        "8 | val f = c:inc\n" +
        "  |         ^^^^^\n" +
        "  = help: wrap the call in a lambda: fn () { c:inc() }\n";
    Assert.Equal(ExpectedBound, RenderPlain(BoundCode));
  }

  [Fact]
  public void Snapshot_CaptureMutParam_PN3228_IncludesRelatedAndHelp() {
    const string Code = "fn leak(mut n int) fn () -> int {\n  return fn () -> n\n}\n";
    string rendered = RenderPlain(Code);
    const string Expected =
        "error[PN3228]: Cannot capture 'mut' parameter 'n' inside a closure.\n" +
        " --> sample.pino:2:19\n" +
        "  |\n" +
        "1 | fn leak(mut n int) fn () -> int {\n" +
        "  |             - parameter 'n' is declared 'mut' here\n" +
        "2 |   return fn () -> n\n" +
        "  |                   ^\n" +
        "  = help: copy the needed value into a local 'val' before the closure, or avoid capturing 'n'\n";
    Assert.Equal(Expected, rendered);
  }

  [Fact]
  public void Snapshot_ValMutReassignAndNonMutableInterior_PN3229AndPN3230_IncludeHelp() {
    const string ReassignCode = "val mut xs = [1, 2]\nxs = [3, 4]\n";
    const string ExpectedReassign =
        "error[PN3229]: Cannot reassign 'val mut' variable 'xs'.\n" +
        " --> sample.pino:2:1\n" +
        "  |\n" +
        "1 | val mut xs = [1, 2]\n" +
        "  |         -- 'xs' is declared with 'val mut' here\n" +
        "2 | xs = [3, 4]\n" +
        "  | ^^\n" +
        "  = help: declare it with 'var' to allow reassignment\n";
    Assert.Equal(ExpectedReassign, RenderPlain(ReassignCode));

    const string ScalarCode = "val mut n = 0\n";
    const string ExpectedScalar =
        "error[PN3230]: Type 'int' has nothing to mutate; 'val mut' requires a type with mutable contents.\n" +
        " --> sample.pino:1:1\n" +
        "  |\n" +
        "1 | val mut n = 0\n" +
        "  | ^^^^^^^\n" +
        "  = help: use 'val' if it never changes, or 'var' to reassign it\n";
    Assert.Equal(ExpectedScalar, RenderPlain(ScalarCode));
  }

  [Fact]
  public void Snapshot_DedupedReadOnlyFieldMutation_PN3221_ShowsFirstAndAdditionalSites() {
    const string Code = "struct Point {\n  x int\n}\nvar p = Point { x: 1 }\np:x = 2\np:x = 3\np:x = 4\np:x = 5\np:x = 6\n";
    const string Expected =
        "error[PN3221]: Cannot mutate read-only field 'x' of struct 'Point'.\n" +
        " --> sample.pino:5:1\n" +
        "  |\n" +
        "2 |   x int\n" +
        "  |   - field 'x' is declared read-only here\n" +
        "...\n" +
        "5 | p:x = 2\n" +
        "  | ^^^^^^^\n" +
        "6 | p:x = 3\n" +
        "  | ------- also mutated here\n" +
        "7 | p:x = 4\n" +
        "  | ------- also mutated here (and 2 more places in this file)\n" +
        "  = help: add 'mut' to field 'x' in struct 'Point' to allow mutation\n";
    Assert.Equal(Expected, RenderPlain(Code));
  }

  [Fact]
  public void Snapshot_SlashDivisionHint_PN3102_PointsAtSlashAndExplains() {
    const string Code = "val n = 9\nval r = rand(n / 2 + 1)\n";
    const string Expected =
        "error[PN3102]: Argument 1 for function 'rand' expected type 'int', but got 'float'.\n" +
        " --> sample.pino:2:14\n" +
        "  |\n" +
        "2 | val r = rand(n / 2 + 1)\n" +
        "  |              ^^^^^^^^^\n" +
        "  |                - the '/' operator always produces a float\n" +
        "  = help: use '//' for whole-number division, or int(...) to convert\n";
    Assert.Equal(Expected, RenderPlain(Code));
  }

  [Fact]
  public void Snapshot_LostWriteWarning_PN3552_IncludesCopyLocationAndHelp() {
    const string Code = "struct Item {\n  mut hp int\n}\nvar xs = [Item { hp: 10 }]\nvar item = xs[0]\nitem:hp -= 1\n";
    const string Expected =
        "warning[PN3552]: Changes to 'item' do not update 'xs[0]', because 'item' is a separate copy.\n" +
        " --> sample.pino:6:1\n" +
        "  |\n" +
        "5 | var item = xs[0]\n" +
        "  |     ---- 'item' is copied from 'xs[0]' here\n" +
        "6 | item:hp -= 1\n" +
        "  | ^^^^^^^^^^^^\n" +
        "  = help: mutate 'xs[0]' directly, assign 'item' back to 'xs[0]', or pass it to a 'mut' function\n";
    Assert.Equal(Expected, RenderPlain(Code));
  }
}
