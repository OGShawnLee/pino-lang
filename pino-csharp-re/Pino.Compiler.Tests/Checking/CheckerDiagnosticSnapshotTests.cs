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
}
