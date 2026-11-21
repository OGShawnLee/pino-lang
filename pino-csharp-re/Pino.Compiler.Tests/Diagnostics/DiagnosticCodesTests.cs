using System.Reflection;

using Pino.Compiler.Diagnostics;

namespace Pino.Compiler.Tests.Diagnostics;

public sealed class DiagnosticCodesTests {
  [Fact]
  public void ParserCodesPN2001ThroughPN2020_ExistWithExpectedNames() {
    var expected = new (string Name, string Code)[] {
      (nameof(DiagnosticCodes.ExpectedToken), "PN2001"),
      (nameof(DiagnosticCodes.ExpectedExpression), "PN2002"),
      (nameof(DiagnosticCodes.ExpectedStatement), "PN2003"),
      (nameof(DiagnosticCodes.ExpectedType), "PN2004"),
      (nameof(DiagnosticCodes.ExpectedPattern), "PN2005"),
      (nameof(DiagnosticCodes.ExpectedIdentifier), "PN2006"),
      (nameof(DiagnosticCodes.TrailingComma), "PN2007"),
      (nameof(DiagnosticCodes.UnexpectedComma), "PN2008"),
      (nameof(DiagnosticCodes.AmbiguousMinusInList), "PN2009"),
      (nameof(DiagnosticCodes.DeprecatedFatArrow), "PN2010"),
      (nameof(DiagnosticCodes.UnbracedIfBranch), "PN2011"),
      (nameof(DiagnosticCodes.ElseArrowAfterIf), "PN2012"),
      (nameof(DiagnosticCodes.ArrowBeforeMatchBlock), "PN2013"),
      (nameof(DiagnosticCodes.DeprecatedGenericSyntax), "PN2014"),
      (nameof(DiagnosticCodes.MixedUnionPayloadForms), "PN2015"),
      (nameof(DiagnosticCodes.EmptyDeclarationBody), "PN2016"),
      (nameof(DiagnosticCodes.UnexpectedBlockInExpression), "PN2017"),
      (nameof(DiagnosticCodes.DuplicateElseArm), "PN2018"),
      (nameof(DiagnosticCodes.ExpectedBlockBody), "PN2019"),
      (nameof(DiagnosticCodes.MaxNestingDepthExceeded), "PN2020"),
    };

    Assert.Equal(20, expected.Length);

    Type type = typeof(DiagnosticCodes);
    foreach ((string name, string code) in expected) {
      FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
      Assert.NotNull(field);
      Assert.True(field.IsLiteral && !field.IsInitOnly, $"{name} must be const.");
      Assert.Equal(code, field.GetRawConstantValue());
    }

    Assert.Equal(expected.Length, expected.Select(static e => e.Code).Distinct().Count());
  }

  [Fact]
  public void ParserCodePN2021_DeprecatedVisibilitySyntax_Exists() {
    Assert.Equal("PN2021", DiagnosticCodes.DeprecatedVisibilitySyntax);
  }

  [Fact]
  public void ParserCodesPN2022AndPN2023_MutAndRemovedIdentityOps_Exist() {
    Assert.Equal("PN2022", DiagnosticCodes.InvalidMutModifierPlacement);
    Assert.Equal("PN2023", DiagnosticCodes.RemovedIdentityOperator);
  }

  [Fact]
  public void CheckerCodesPN3xxx_ExistWithExpectedNames() {
    var expected = new (string Name, string Code)[] {
      (nameof(DiagnosticCodes.UndeclaredIdentifier), "PN3001"),
      (nameof(DiagnosticCodes.DuplicateDeclaration), "PN3002"),
      (nameof(DiagnosticCodes.UnknownType), "PN3003"),
      (nameof(DiagnosticCodes.ModuleNotFound), "PN3004"),
      (nameof(DiagnosticCodes.ModuleImportCycle), "PN3005"),
      (nameof(DiagnosticCodes.ModuleMemberNotFound), "PN3006"),
      (nameof(DiagnosticCodes.SymbolNotPublic), "PN3007"),
      (nameof(DiagnosticCodes.ModuleDefinesMain), "PN3008"),
      (nameof(DiagnosticCodes.PrivateFieldAccess), "PN3009"),
      (nameof(DiagnosticCodes.InvalidTypeNameCasing), "PN3010"),
      (nameof(DiagnosticCodes.GenericParameterNameConflict), "PN3011"),
      (nameof(DiagnosticCodes.InvalidVoidTypePosition), "PN3101"),
      (nameof(DiagnosticCodes.TypeMismatch), "PN3102"),
      (nameof(DiagnosticCodes.BranchTypeMismatch), "PN3103"),
      (nameof(DiagnosticCodes.CannotInferReturnType), "PN3104"),
      (nameof(DiagnosticCodes.ConflictingReturnTypes), "PN3105"),
      (nameof(DiagnosticCodes.InconsistentGenericInference), "PN3106"),
      (nameof(DiagnosticCodes.GenericConstraintNotSatisfied), "PN3107"),
      (nameof(DiagnosticCodes.InterfaceNotSatisfied), "PN3108"),
      (nameof(DiagnosticCodes.CannotInferLambdaParameter), "PN3109"),
      (nameof(DiagnosticCodes.CannotInferGenericArguments), "PN3110"),
      (nameof(DiagnosticCodes.StaticMethodUsesInstanceGeneric), "PN3111"),
      (nameof(DiagnosticCodes.InvalidConditionType), "PN3112"),
      (nameof(DiagnosticCodes.DefaultParameterNotSupported), "PN3113"),
      (nameof(DiagnosticCodes.CannotAssignToImmutable), "PN3201"),
      (nameof(DiagnosticCodes.InvalidAssignmentTarget), "PN3202"),
      (nameof(DiagnosticCodes.InvalidOperatorOperands), "PN3203"),
      (nameof(DiagnosticCodes.NotCallable), "PN3204"),
      (nameof(DiagnosticCodes.WrongArgumentCount), "PN3205"),
      (nameof(DiagnosticCodes.ValInitializedWithOptionNone), "PN3206"),
      (nameof(DiagnosticCodes.MissingStructField), "PN3207"),
      (nameof(DiagnosticCodes.UnknownMember), "PN3208"),
      (nameof(DiagnosticCodes.InvalidStaticOrInstanceAccess), "PN3209"),
      (nameof(DiagnosticCodes.InvalidImplicitIt), "PN3210"),
      (nameof(DiagnosticCodes.InvalidIndexOperation), "PN3211"),
      (nameof(DiagnosticCodes.MixedPatternBindings), "PN3301"),
      (nameof(DiagnosticCodes.TooManyPatternBindings), "PN3302"),
      (nameof(DiagnosticCodes.LabelPatternOnPositionalVariant), "PN3303"),
      (nameof(DiagnosticCodes.UnknownPatternFieldLabel), "PN3304"),
      (nameof(DiagnosticCodes.DuplicatePatternFieldLabel), "PN3305"),
      (nameof(DiagnosticCodes.NonExhaustiveMatch), "PN3306"),
      (nameof(DiagnosticCodes.InvalidIsPattern), "PN3307"),
      (nameof(DiagnosticCodes.UnknownEnumMember), "PN3308"),
      (nameof(DiagnosticCodes.TupleLiteralOutsideReturn), "PN3309"),
      (nameof(DiagnosticCodes.DuplicateTupleLabel), "PN3310"),
      (nameof(DiagnosticCodes.UnknownTupleField), "PN3311"),
      (nameof(DiagnosticCodes.DuplicateDestructureVariable), "PN3312"),
      (nameof(DiagnosticCodes.VariantArgumentMismatch), "PN3313"),
      (nameof(DiagnosticCodes.BareIdentifierPatternNotAllowed), "PN3314"),
      (nameof(DiagnosticCodes.BreakOrContinueOutsideLoop), "PN3401"),
      (nameof(DiagnosticCodes.ReturnOutsideFunction), "PN3402"),
      (nameof(DiagnosticCodes.BareReturnInNonVoidFunction), "PN3403"),
      (nameof(DiagnosticCodes.MissingReturnPath), "PN3404"),
      (nameof(DiagnosticCodes.YieldOutsideValidContext), "PN3405"),
      (nameof(DiagnosticCodes.YieldTypeMismatch), "PN3406"),
      (nameof(DiagnosticCodes.BubbleOutsideCompatibleFunction), "PN3407"),
      (nameof(DiagnosticCodes.BubbleErrorTypeMismatch), "PN3408"),
      (nameof(DiagnosticCodes.BubbleOnNonResultOrOption), "PN3409"),
      (nameof(DiagnosticCodes.InvalidTopLevelInProgramMode), "PN3410"),
      (nameof(DiagnosticCodes.ReturnTypeMismatch), "PN3411"),
      (nameof(DiagnosticCodes.MainFunctionHasParameters), "PN3412"),
      (nameof(DiagnosticCodes.ConstantDivisionByZero), "PN3501"),
      (nameof(DiagnosticCodes.NegativeVectorLength), "PN3502"),
      (nameof(DiagnosticCodes.NegativeVectorCapacity), "PN3503"),
      (nameof(DiagnosticCodes.VectorCapacityBelowLength), "PN3504"),
      (nameof(DiagnosticCodes.ConstantFalseLoop), "PN3550"),
      (nameof(DiagnosticCodes.UnreachableMatchArm), "PN3551"),
    };

    Assert.Equal(67, expected.Length);

    Type type = typeof(DiagnosticCodes);
    foreach ((string name, string code) in expected) {
      FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
      Assert.NotNull(field);
      Assert.True(field.IsLiteral && !field.IsInitOnly, $"{name} must be const.");
      Assert.Equal(code, field.GetRawConstantValue());
    }

    Assert.Equal(expected.Length, expected.Select(static e => e.Code).Distinct(StringComparer.Ordinal).Count());
  }

  [Fact]
  public void AllPublicConstantsInDiagnosticCodes_AreUnique() {
    FieldInfo[] fields = typeof(DiagnosticCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(static f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
        .ToArray();

    string[] values = fields.Select(static f => (string) f.GetRawConstantValue()!).ToArray();
    Assert.NotEmpty(values);
    Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
  }
}
