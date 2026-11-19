namespace Pino.Compiler.Lexing;

[Flags]
public enum TokenFlags : ushort {
  None = 0,
  LeadingNewline = 1 << 0,
  LeadingWhitespace = 1 << 1,
  LeadingComment = 1 << 2,
  Missing = 1 << 3,
}
