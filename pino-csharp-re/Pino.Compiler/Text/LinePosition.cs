namespace Pino.Compiler.Text;

public readonly record struct LinePosition(int Line, int Column);

public readonly record struct LinePositionSpan(LinePosition Start, LinePosition End);
