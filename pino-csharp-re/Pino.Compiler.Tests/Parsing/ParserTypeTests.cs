using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Tests.Parsing;

public sealed class ParserTypeTests {
  [Theory]
  [InlineData("int", "(NamedType int)")]
  [InlineData("Mod::T", "(QualifiedType Mod :: T)")]
  [InlineData("Name[T, U]", "(GenericType (NamedType Name) (TypeArgumentList (NamedType T) (NamedType U)))")]
  [InlineData("Mod::Name[T U]", "(GenericType (QualifiedType Mod :: Name) (TypeArgumentList (NamedType T) (NamedType U)))")]
  [InlineData("[]int", "(VectorType (NamedType int))")]
  [InlineData("[][]string", "(VectorType (VectorType (NamedType string)))")]
  [InlineData("map[string, int]", "(MapType map (NamedType string) (NamedType int))")]
  [InlineData("set[bool]", "(SetType set (NamedType bool))")]
  [InlineData("fn (int, string) -> bool", "(FunctionType (FunctionTypeParameter (NamedType int)) (FunctionTypeParameter (NamedType string)) -> (NamedType bool))")]
  [InlineData("fn (int string) -> bool", "(FunctionType (FunctionTypeParameter (NamedType int)) (FunctionTypeParameter (NamedType string)) -> (NamedType bool))")]
  [InlineData("fn () -> int", "(FunctionType -> (NamedType int))")]
  [InlineData("fn -> int", "(FunctionType -> (NamedType int))")]
  [InlineData("fn (int)", "(FunctionType (FunctionTypeParameter (NamedType int)))")]
  [InlineData("fn (mut int) -> void", "(FunctionType (FunctionTypeParameter mut (NamedType int)) -> (NamedType void))")]
  [InlineData("fn (mut int, string) -> bool", "(FunctionType (FunctionTypeParameter mut (NamedType int)) (FunctionTypeParameter (NamedType string)) -> (NamedType bool))")]
  [InlineData("fn", "(FunctionType)")]
  [InlineData("fn () -> @(a int, b string)", "(FunctionType -> (TupleType (TupleFieldType a (NamedType int)) (TupleFieldType b (NamedType string))))")]
  [InlineData("@(name string, age int)", "(TupleType (TupleFieldType name (NamedType string)) (TupleFieldType age (NamedType int)))")]
  public void AllTypeForms_ParseCorrectly(string source, string expected) {
    ParseResult<TypeSyntax> result = ParserTestHelpers.ParseType(source);
    Assert.Equal(expected, SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Fact]
  public void MapTypeRequiresComma() {
    ParseResult<TypeSyntax> result = ParserTestHelpers.ParseType("map[string int]", expectNoDiagnostics: false);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedToken, diag.Code);
    Assert.Equal("int", result.Lex.Source.AsSpan(diag.Span).ToString());
  }

  [Fact]
  public void UnexpectedTokenInType_EmitsPN2004() {
    ParseResult<TypeSyntax> result = ParserTestHelpers.ParseType("123", expectEOF: false, expectNoDiagnostics: false);
    Assert.True(result.Node.IsMissing);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedType, diag.Code);
    Assert.Equal("123", result.Lex.Source.AsSpan(diag.Span).ToString());
  }

  [Fact]
  public void EmptyTypeArgumentList_EmitsPN2004() {
    ParseResult<TypeSyntax> result = ParserTestHelpers.ParseType("Box[]", expectNoDiagnostics: false);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedType, diag.Code);
    Assert.Equal("]", result.Lex.Source.AsSpan(diag.Span).ToString());
  }
}
