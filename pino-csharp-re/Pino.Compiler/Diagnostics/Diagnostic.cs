using System.Collections.Immutable;

using Pino.Compiler.Text;

namespace Pino.Compiler.Diagnostics;

public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    SourceSpan Span,
    string Message,
    ImmutableArray<RelatedLocation> Related = default,
    DiagnosticTags Tags = DiagnosticTags.None,
    string? Help = null) {
  public ImmutableArray<RelatedLocation> Related { get; init; } = Related.IsDefault ? [] : Related;
}
