using Pino.Compiler.Text;

namespace Pino.Compiler.Diagnostics;

public readonly record struct RelatedLocation(
    SourceSpan Span,
    string Message,
    string? FilePath = null);
