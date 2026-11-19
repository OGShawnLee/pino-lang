using System.Collections.Immutable;

using Pino.Compiler.Text;

namespace Pino.Compiler.Diagnostics;

public sealed class DiagnosticBag {
  private readonly List<Diagnostic> _diagnostics = [];

  public int Count => _diagnostics.Count;

  public bool HasErrors { get; private set; }

  public void ReportError(string code, SourceSpan span, string message) {
    AddDiagnostic(new Diagnostic(
        code ?? string.Empty,
        DiagnosticSeverity.Error,
        span,
        message ?? string.Empty));
  }

  public void ReportWarning(string code, SourceSpan span, string message) {
    AddDiagnostic(new Diagnostic(
        code ?? string.Empty,
        DiagnosticSeverity.Warning,
        span,
        message ?? string.Empty));
  }

  public void ReportInfo(string code, SourceSpan span, string message) {
    AddDiagnostic(new Diagnostic(
        code ?? string.Empty,
        DiagnosticSeverity.Info,
        span,
        message ?? string.Empty));
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
