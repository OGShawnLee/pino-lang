using System.Collections.Immutable;

using Pino.Compiler.Text;

namespace Pino.Compiler.Diagnostics;

public sealed class DiagnosticBag {
  private readonly List<Diagnostic> _diagnostics = [];

  public int Count => _diagnostics.Count;

  public bool HasErrors { get; private set; }

  internal IReadOnlyList<Diagnostic> Items => _diagnostics;

  internal void ReplaceAll(IEnumerable<Diagnostic> updated) {
    List<Diagnostic> next = [.. updated];
    _diagnostics.Clear();
    HasErrors = false;
    foreach (Diagnostic diagnostic in next) {
      if (diagnostic is not null) {
        AddDiagnostic(diagnostic);
      }
    }
  }

  public void ReportError(
      string code,
      SourceSpan span,
      string message,
      ImmutableArray<RelatedLocation> related = default,
      DiagnosticTags tags = DiagnosticTags.None,
      string? help = null) {
    Report(DiagnosticSeverity.Error, code, span, message, related, tags, help);
  }

  public void ReportWarning(
      string code,
      SourceSpan span,
      string message,
      ImmutableArray<RelatedLocation> related = default,
      DiagnosticTags tags = DiagnosticTags.None,
      string? help = null) {
    Report(DiagnosticSeverity.Warning, code, span, message, related, tags, help);
  }

  public void ReportInfo(
      string code,
      SourceSpan span,
      string message,
      ImmutableArray<RelatedLocation> related = default,
      DiagnosticTags tags = DiagnosticTags.None,
      string? help = null) {
    Report(DiagnosticSeverity.Info, code, span, message, related, tags, help);
  }

  public void ReportHint(
      string code,
      SourceSpan span,
      string message,
      ImmutableArray<RelatedLocation> related = default,
      DiagnosticTags tags = DiagnosticTags.None,
      string? help = null) {
    Report(DiagnosticSeverity.Hint, code, span, message, related, tags, help);
  }

  public void Report(
      DiagnosticSeverity severity,
      string code,
      SourceSpan span,
      string message,
      ImmutableArray<RelatedLocation> related = default,
      DiagnosticTags tags = DiagnosticTags.None,
      string? help = null) {
    AddDiagnostic(new Diagnostic(
        code ?? string.Empty,
        severity,
        span,
        message ?? string.Empty,
        related,
        tags,
        help));
  }

  public void AddRange(IEnumerable<Diagnostic> diagnostics) {
    if (diagnostics is null) {
      return;
    }

    foreach (Diagnostic diagnostic in diagnostics) {
      if (diagnostic is not null) {
        AddDiagnostic(diagnostic);
      }
    }
  }

  public void AddRange(DiagnosticBag other) {
    if (other is null || ReferenceEquals(this, other)) {
      return;
    }

    foreach (Diagnostic diagnostic in other._diagnostics) {
      AddDiagnostic(diagnostic);
    }
  }

  public ImmutableArray<Diagnostic> ToImmutableSorted() {
    if (_diagnostics.Count == 0) {
      return [];
    }

    return [.. _diagnostics
            .OrderBy(static d => d.Span.Start)
            .ThenBy(static d => d.Span.Length)];
  }

  private void AddDiagnostic(Diagnostic diagnostic) {
    if (diagnostic.Severity == DiagnosticSeverity.Error) {
      HasErrors = true;
    }

    _diagnostics.Add(diagnostic);
  }
}
