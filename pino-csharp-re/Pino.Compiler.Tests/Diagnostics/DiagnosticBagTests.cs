using System.Collections.Immutable;

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
  public void WarningsInfoAndHintsDoNotSetHasErrors() {
    var bag = new DiagnosticBag();

    bag.ReportWarning(DiagnosticCodes.InvalidNumericSeparator, new SourceSpan(0, 3), "separator warning");
    bag.ReportInfo(DiagnosticCodes.UnexpectedCharacter, new SourceSpan(4, 2), "info message");
    bag.ReportHint(DiagnosticCodes.InvalidStringEscape, new SourceSpan(7, 2), "hint message");

    Assert.Equal(3, bag.Count);
    Assert.False(bag.HasErrors);

    ImmutableArray<Diagnostic> sorted = bag.ToImmutableSorted();
    Assert.Equal(DiagnosticSeverity.Warning, sorted[0].Severity);
    Assert.Equal(DiagnosticSeverity.Info, sorted[1].Severity);
    Assert.Equal(DiagnosticSeverity.Hint, sorted[2].Severity);
  }

  [Fact]
  public void ReportErrorSetsHasErrorsAndNeverThrows() {
    var bag = new DiagnosticBag();

    bag.ReportError(DiagnosticCodes.UnterminatedString, SourceSpan.Empty, "unexpected EOF");
    bag.ReportError(null!, new SourceSpan(10, 0), null!);

    Assert.Equal(2, bag.Count);
    Assert.True(bag.HasErrors);

    ImmutableArray<Diagnostic> sorted = bag.ToImmutableSorted();
    Assert.Equal(string.Empty, sorted[1].Code);
    Assert.Equal(string.Empty, sorted[1].Message);
  }

  [Fact]
  public void DiagnosticOptionalMetadataDefaultsAndPreservesValues() {
    var plain = new Diagnostic(
        DiagnosticCodes.UnexpectedCharacter,
        DiagnosticSeverity.Error,
        new SourceSpan(0, 1),
        "unexpected");
    Assert.True(plain.Related.IsEmpty);
    Assert.Equal(DiagnosticTags.None, plain.Tags);
    Assert.Null(plain.Help);

    var related = ImmutableArray.Create(new RelatedLocation(new SourceSpan(10, 2), "opened here", "main.pino"));
    var rich = new Diagnostic(
        DiagnosticCodes.UnterminatedInterpolation,
        DiagnosticSeverity.Hint,
        new SourceSpan(15, 1),
        "unclosed interpolation",
        related,
        DiagnosticTags.Unnecessary | DiagnosticTags.Deprecated,
        "Add a closing ')'.");

    Assert.Equal(DiagnosticSeverity.Hint, rich.Severity);
    Assert.Equal(related, rich.Related);
    Assert.Equal(DiagnosticTags.Unnecessary | DiagnosticTags.Deprecated, rich.Tags);
    Assert.Equal("Add a closing ')'.", rich.Help);
  }

  [Fact]
  public void DiagnosticCodesCatalogContainsUniqueLexerCodes() {
    string[] codes =
    [
        DiagnosticCodes.UnexpectedCharacter,
        DiagnosticCodes.InvalidNumericSeparator,
        DiagnosticCodes.InvalidNumberSuffix,
        DiagnosticCodes.EmptyRune,
        DiagnosticCodes.InvalidRuneEscape,
        DiagnosticCodes.MultiRuneLiteral,
        DiagnosticCodes.UnterminatedRune,
        DiagnosticCodes.UnterminatedString,
        DiagnosticCodes.UnterminatedMultilineString,
        DiagnosticCodes.InvalidStringEscape,
        DiagnosticCodes.UnterminatedStringEscape,
        DiagnosticCodes.UnterminatedInterpolation,
        DiagnosticCodes.InvalidRuneSurrogate,
        DiagnosticCodes.MultilineStringInSingleLineInterpolation,
    ];

    Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
    Assert.All(codes, static code => Assert.StartsWith("PN1", code, StringComparison.Ordinal));
    Assert.DoesNotContain("PN1008", codes);
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

    ImmutableArray<Diagnostic> sorted = bag.ToImmutableSorted();

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
