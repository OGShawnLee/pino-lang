using Pino.Compiler.Diagnostics;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Diagnostics;

public sealed class DiagnosticBagTests {
  [Fact]
  public void EmptyBagHasNoErrorsAndReturnsEmptySortedArray() {
    var bag = new DiagnosticBag();

    Assert.Equal(0, bag.Count);
    Assert.False(bag.HasErrors);
    Assert.True(bag.ToImmutableSorted().IsEmpty);
  }

  [Fact]
  public void WarningsAndInfoDoNotSetHasErrors() {
    var bag = new DiagnosticBag();

    bag.ReportWarning("PINO100", new SourceSpan(0, 3), "unused variable");
    bag.ReportInfo("PINO001", new SourceSpan(4, 2), "hint");

    Assert.Equal(2, bag.Count);
    Assert.False(bag.HasErrors);
  }

  [Fact]
  public void ReportErrorSetsHasErrorsAndNeverThrows() {
    var bag = new DiagnosticBag();

    bag.ReportError("PINO200", SourceSpan.Empty, "unexpected EOF");
    bag.ReportError(null!, new SourceSpan(10, 0), null!);

    Assert.Equal(2, bag.Count);
    Assert.True(bag.HasErrors);

    var sorted = bag.ToImmutableSorted();
    Assert.Equal(string.Empty, sorted[1].Code);
    Assert.Equal(string.Empty, sorted[1].Message);
  }

  [Fact]
  public void ToImmutableSortedOrdersBySpanStartThenLengthDeterministically() {
    var bag = new DiagnosticBag();

    bag.ReportError("E3", new SourceSpan(10, 5), "later long");
    bag.ReportWarning("W1", new SourceSpan(2, 4), "early wider");
    bag.ReportInfo("I1", new SourceSpan(2, 1), "early narrow");
    bag.ReportError("E1", new SourceSpan(2, 4), "early wider second");
    bag.ReportError("E0", new SourceSpan(0, 2), "first");
    bag.ReportError("E2", new SourceSpan(10, 1), "later short");

    var sorted = bag.ToImmutableSorted();

    Assert.Equal(6, sorted.Length);
    Assert.Equal(["E0", "I1", "W1", "E1", "E2", "E3"], sorted.Select(d => d.Code));
  }

  [Fact]
  public void AddRangeCombinesDiagnosticsAndPropagatesHasErrors() {
    var warnings = new DiagnosticBag();
    warnings.ReportWarning("W1", new SourceSpan(5, 2), "warn");

    var errors = new DiagnosticBag();
    errors.ReportError("E1", new SourceSpan(1, 3), "err");

    warnings.AddRange(errors);
    Assert.True(warnings.HasErrors);
    Assert.Equal(2, warnings.Count);
    Assert.Equal(["E1", "W1"], warnings.ToImmutableSorted().Select(d => d.Code));

    warnings.AddRange((DiagnosticBag) null!);
    warnings.AddRange((IEnumerable<Diagnostic>) null!);
    warnings.AddRange(warnings);
    Assert.Equal(2, warnings.Count);

    var extra = new Diagnostic[]
    {
            new("I2", DiagnosticSeverity.Info, new SourceSpan(0, 1), "info"),
            null!,
    };
    warnings.AddRange(extra);
    Assert.Equal(3, warnings.Count);
    Assert.Equal(["I2", "E1", "W1"], warnings.ToImmutableSorted().Select(d => d.Code));
  }
}
