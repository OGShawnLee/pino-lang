namespace Pino.Compiler.Diagnostics;

[Flags]
public enum DiagnosticTags {
  None = 0,
  Unnecessary = 1 << 0,
  Deprecated = 1 << 1,
}
