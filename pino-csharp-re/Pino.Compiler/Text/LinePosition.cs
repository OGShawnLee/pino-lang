namespace Pino.Compiler.Text;

/// <summary>
/// A 1-based line and column position within a <see cref="SourceText"/>.
/// Both <see cref="Line"/> and <see cref="Column"/> are 1-based, and <see cref="Column"/> is
/// measured in UTF-16 code units from the start of the line (matching LSP UTF-16 positions
/// except for 1-based indexing).
/// </summary>
public readonly record struct LinePosition(int Line, int Column);

/// <summary>
/// A half-open 1-based line and column range (<see cref="Start"/> to <see cref="End"/>)
/// measured in UTF-16 code units.
/// </summary>
public readonly record struct LinePositionSpan(LinePosition Start, LinePosition End);
