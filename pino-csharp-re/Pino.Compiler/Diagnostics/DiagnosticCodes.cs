namespace Pino.Compiler.Diagnostics;

/// <summary>
/// Central catalog of compiler diagnostic codes.
/// Reserved ranges:
/// <list type="bullet">
/// <item><c>PN1xxx</c>: Lexer</item>
/// <item><c>PN2xxx</c>: Parser</item>
/// <item><c>PN3xxx</c>: Checker</item>
/// <item><c>PN4xxx</c>: IR / Codegen</item>
/// </list>
/// </summary>
public static class DiagnosticCodes {
  // -------------------------------------------------------------------------
  // PN1xxx — Lexer
  // -------------------------------------------------------------------------

  public const string UnexpectedCharacter = "PN1001";
  public const string InvalidNumericSeparator = "PN1002";
  public const string InvalidNumberSuffix = "PN1003";
  public const string EmptyRune = "PN1004";
  public const string InvalidRuneEscape = "PN1005";
  public const string MultiRuneLiteral = "PN1006";
  public const string UnterminatedRune = "PN1007";

  // PN1008 was the temporary "strings not supported" stub from Ticket 002; it is retired.

  public const string UnterminatedString = "PN1009";
  public const string UnterminatedMultilineString = "PN1010";
  public const string InvalidStringEscape = "PN1011";
  public const string UnterminatedStringEscape = "PN1012";
  public const string UnterminatedInterpolation = "PN1013";
  public const string InvalidRuneSurrogate = "PN1014";
  public const string MultilineStringInSingleLineInterpolation = "PN1015";
  public const string ControlCharacterInLiteral = "PN1016";
  public const string KeywordInSimpleInterpolation = "PN1017";

  // -------------------------------------------------------------------------
  // PN2xxx — Parser (reserved)
  // -------------------------------------------------------------------------

  // -------------------------------------------------------------------------
  // PN3xxx — Checker (reserved)
  // -------------------------------------------------------------------------

  // -------------------------------------------------------------------------
  // PN4xxx — IR / Codegen (reserved)
  // -------------------------------------------------------------------------
}
