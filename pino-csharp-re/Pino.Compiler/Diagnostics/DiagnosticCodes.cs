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
  public const string DeprecatedVisibilitySyntax = "PN2021";
  public const string InvalidMutModifierPlacement = "PN2022";
  public const string RemovedIdentityOperator = "PN2023";

  // -------------------------------------------------------------------------
  // PN3xxx — Checker
  // -------------------------------------------------------------------------

  // Names, Scopes, Visibility & Modules (PN3001–PN3049)
  public const string UndeclaredIdentifier = "PN3001";
  public const string DuplicateDeclaration = "PN3002";
  public const string UnknownType = "PN3003";
  public const string ModuleNotFound = "PN3004";
  public const string ModuleImportCycle = "PN3005";
  public const string ModuleMemberNotFound = "PN3006";
  public const string SymbolNotPublic = "PN3007";
  public const string ModuleDefinesMain = "PN3008";
  public const string PrivateFieldAccess = "PN3009";
  public const string InvalidTypeNameCasing = "PN3010";
  public const string GenericParameterNameConflict = "PN3011";

  // Types, Annotations, Generics & Interfaces (PN3101–PN3149)
  public const string InvalidVoidTypePosition = "PN3101";
  public const string TypeMismatch = "PN3102";
  public const string BranchTypeMismatch = "PN3103";
  public const string CannotInferReturnType = "PN3104";
  public const string ConflictingReturnTypes = "PN3105";
  public const string InconsistentGenericInference = "PN3106";
  public const string GenericConstraintNotSatisfied = "PN3107";
  public const string InterfaceNotSatisfied = "PN3108";
  public const string CannotInferLambdaParameter = "PN3109";
  public const string CannotInferGenericArguments = "PN3110";
  public const string StaticMethodUsesInstanceGeneric = "PN3111";
  public const string InvalidConditionType = "PN3112";
  public const string DefaultParameterNotSupported = "PN3113";

  // Expressions, Operators, Calls, Structs & Collections (PN3201–PN3249)
  public const string CannotAssignToImmutable = "PN3201";
  public const string InvalidAssignmentTarget = "PN3202";
  public const string InvalidOperatorOperands = "PN3203";
  public const string NotCallable = "PN3204";
  public const string WrongArgumentCount = "PN3205";
  public const string ValInitializedWithOptionNone = "PN3206";
  public const string MissingStructField = "PN3207";
  public const string UnknownMember = "PN3208";
  public const string InvalidStaticOrInstanceAccess = "PN3209";
  public const string InvalidImplicitIt = "PN3210";
  public const string InvalidIndexOperation = "PN3211";

  // Patterns, Match, Unions, Enums & Tuples (PN3301–PN3349)
  public const string MixedPatternBindings = "PN3301";
  public const string TooManyPatternBindings = "PN3302";
  public const string LabelPatternOnPositionalVariant = "PN3303";
  public const string UnknownPatternFieldLabel = "PN3304";
  public const string DuplicatePatternFieldLabel = "PN3305";
  public const string NonExhaustiveMatch = "PN3306";
  public const string InvalidIsPattern = "PN3307";
  public const string UnknownEnumMember = "PN3308";
  public const string TupleLiteralOutsideReturn = "PN3309";
  public const string DuplicateTupleLabel = "PN3310";
  public const string UnknownTupleField = "PN3311";
  public const string DuplicateDestructureVariable = "PN3312";
  public const string VariantArgumentMismatch = "PN3313";
  public const string BareIdentifierPatternNotAllowed = "PN3314";

  // Control Flow, Definite Return, Error Handling & Entry Point (PN3401–PN3449)
  public const string BreakOrContinueOutsideLoop = "PN3401";
  public const string ReturnOutsideFunction = "PN3402";
  public const string BareReturnInNonVoidFunction = "PN3403";
  public const string MissingReturnPath = "PN3404";
  public const string YieldOutsideValidContext = "PN3405";
  public const string YieldTypeMismatch = "PN3406";
  public const string BubbleOutsideCompatibleFunction = "PN3407";
  public const string BubbleErrorTypeMismatch = "PN3408";
  public const string BubbleOnNonResultOrOption = "PN3409";
  public const string InvalidTopLevelInProgramMode = "PN3410";
  public const string ReturnTypeMismatch = "PN3411";
  public const string MainFunctionHasParameters = "PN3412";

  // Constant Checks & Warnings (PN3501–PN3599)
  public const string ConstantDivisionByZero = "PN3501";
  public const string NegativeVectorLength = "PN3502";
  public const string NegativeVectorCapacity = "PN3503";
  public const string VectorCapacityBelowLength = "PN3504";
  public const string ConstantFalseLoop = "PN3550";
  public const string UnreachableMatchArm = "PN3551";

  // -------------------------------------------------------------------------
  // PN4xxx — IR / Codegen (reserved)
  // -------------------------------------------------------------------------
}
