using Pino.Compiler.Text;

namespace Pino.Compiler.Diagnostics;

public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    SourceSpan Span,
    string Message);
