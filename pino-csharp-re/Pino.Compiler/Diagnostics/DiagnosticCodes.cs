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
  // PN2xxx — Parser
  // -------------------------------------------------------------------------

  public const string ExpectedToken = "PN2001";
  public const string ExpectedExpression = "PN2002";
  public const string ExpectedStatement = "PN2003";
  public const string ExpectedType = "PN2004";
  public const string ExpectedPattern = "PN2005";
  public const string ExpectedIdentifier = "PN2006";
  public const string TrailingComma = "PN2007";
  public const string UnexpectedComma = "PN2008";
  public const string AmbiguousMinusInList = "PN2009";
  public const string DeprecatedFatArrow = "PN2010";
  public const string UnbracedIfBranch = "PN2011";
  public const string ElseArrowAfterIf = "PN2012";
  public const string ArrowBeforeMatchBlock = "PN2013";
  public const string DeprecatedGenericSyntax = "PN2014";
  public const string MixedUnionPayloadForms = "PN2015";
  public const string EmptyDeclarationBody = "PN2016";
  public const string UnexpectedBlockInExpression = "PN2017";
  public const string DuplicateElseArm = "PN2018";
  public const string ExpectedBlockBody = "PN2019";
  public const string MaxNestingDepthExceeded = "PN2020";

  // -------------------------------------------------------------------------
  // PN3xxx — Checker (reserved)
  // -------------------------------------------------------------------------

  // -------------------------------------------------------------------------
  // PN4xxx — IR / Codegen (reserved)
  // -------------------------------------------------------------------------
}
