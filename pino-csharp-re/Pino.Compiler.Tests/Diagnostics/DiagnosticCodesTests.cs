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
