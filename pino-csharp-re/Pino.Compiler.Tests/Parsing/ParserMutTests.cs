using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Parsing;

/// <summary>
/// Ticket 046: <c>mut</c> modifier parsing across all 7 positions, <c>PN2022</c> placement errors,
/// <c>PN2023</c> removed identity operators (<c>===</c>/<c>!==</c>), A2 half-typed recovery, and A3 locality.
/// </summary>
public sealed class ParserMutTests {
  // ---------------------------------------------------------------------------------------------
  // All 7 `mut` positions
  // ---------------------------------------------------------------------------------------------

  [Fact]
  public void StructFieldsAndMethods_SupportMutAndPubMut() {
    const string Source = """
      struct Player {
        Base
        mut hp int
        pub mut score int
        @@Testable mut debug_flag bool
        mut callback fn (mut int) -> void
        fn read() int { return hp }
        mut fn heal(amt int) { hp += amt }
        pub mut fn damage(amt int) { hp -= amt }
        @@Testable mut fn reset() { hp = 0 }
        @@Generic[T]
        pub mut fn apply(item T) {}
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source);
    StructDeclarationSyntax player = Assert.IsType<StructDeclarationSyntax>(Assert.Single(result.Root.Items));
    Assert.Equal(10, player.Members.Length);

    Assert.IsType<EmbeddedStructSyntax>(player.Members[0]);

    FieldDeclarationSyntax hp = Assert.IsType<FieldDeclarationSyntax>(player.Members[1]);
    Assert.Null(hp.Visibility);
    Assert.Equal(TokenKind.MutKeyword, hp.MutKeyword?.Kind);
    Assert.Equal("(FieldDeclaration mut hp (NamedType int))", SyntaxPrinter.Print(hp, result.Lex.Source));

    FieldDeclarationSyntax score = Assert.IsType<FieldDeclarationSyntax>(player.Members[2]);
    Assert.True(score.Visibility?.IsPub);
    Assert.Equal(TokenKind.MutKeyword, score.MutKeyword?.Kind);
    Assert.Equal("(FieldDeclaration (Visibility pub) mut score (NamedType int))", SyntaxPrinter.Print(score, result.Lex.Source));

    FieldDeclarationSyntax debugFlag = Assert.IsType<FieldDeclarationSyntax>(player.Members[3]);
    Assert.False(debugFlag.Visibility?.IsPub);
    Assert.Equal(TokenKind.MutKeyword, debugFlag.MutKeyword?.Kind);

    FieldDeclarationSyntax callback = Assert.IsType<FieldDeclarationSyntax>(player.Members[4]);
    Assert.Equal(TokenKind.MutKeyword, callback.MutKeyword?.Kind);
    FunctionTypeSyntax cbType = Assert.IsType<FunctionTypeSyntax>(callback.Type);
    FunctionTypeParameterSyntax cbParam = Assert.Single(cbType.Parameters);
    Assert.Equal(TokenKind.MutKeyword, cbParam.MutKeyword?.Kind);

    MethodDeclarationSyntax read = Assert.IsType<MethodDeclarationSyntax>(player.Members[5]);
    Assert.Null(read.MutKeyword);

    MethodDeclarationSyntax heal = Assert.IsType<MethodDeclarationSyntax>(player.Members[6]);
    Assert.Equal(TokenKind.MutKeyword, heal.MutKeyword?.Kind);

    MethodDeclarationSyntax damage = Assert.IsType<MethodDeclarationSyntax>(player.Members[7]);
    Assert.True(damage.Visibility?.IsPub);
    Assert.Equal(TokenKind.MutKeyword, damage.MutKeyword?.Kind);

    MethodDeclarationSyntax reset = Assert.IsType<MethodDeclarationSyntax>(player.Members[8]);
    Assert.False(reset.Visibility?.IsPub);
    Assert.Equal(TokenKind.MutKeyword, reset.MutKeyword?.Kind);

    MethodDeclarationSyntax apply = Assert.IsType<MethodDeclarationSyntax>(player.Members[9]);
    Assert.NotNull(apply.GenericDecorator);
    Assert.True(apply.Visibility?.IsPub);
    Assert.Equal(TokenKind.MutKeyword, apply.MutKeyword?.Kind);
  }

  [Fact]
  public void InterfacePropertiesAndMethods_SupportMut() {
    const string Source = """
      interface MutableEntity {
        id string
        mut hp int
        fn read() int
        mut fn tick(dt float) bool
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source);
    InterfaceDeclarationSyntax iface = Assert.IsType<InterfaceDeclarationSyntax>(Assert.Single(result.Root.Items));
    Assert.Equal(4, iface.Members.Length);

    InterfacePropertySyntax idProp = Assert.IsType<InterfacePropertySyntax>(iface.Members[0]);
    Assert.Null(idProp.MutKeyword);

    InterfacePropertySyntax hpProp = Assert.IsType<InterfacePropertySyntax>(iface.Members[1]);
    Assert.Equal(TokenKind.MutKeyword, hpProp.MutKeyword?.Kind);
    Assert.Equal("(InterfaceProperty mut hp (NamedType int))", SyntaxPrinter.Print(hpProp, result.Lex.Source));

    InterfaceMethodSyntax readMethod = Assert.IsType<InterfaceMethodSyntax>(iface.Members[2]);
    Assert.Null(readMethod.MutKeyword);

    InterfaceMethodSyntax tickMethod = Assert.IsType<InterfaceMethodSyntax>(iface.Members[3]);
    Assert.Equal(TokenKind.MutKeyword, tickMethod.MutKeyword?.Kind);
    Assert.Equal(
        "(InterfaceMethod mut tick (Parameter dt (NamedType float)) (NamedType bool))",
        SyntaxPrinter.Print(tickMethod, result.Lex.Source));
  }

  [Fact]
  public void FunctionMethodAndLambdaParameters_SupportMut() {
    const string Source = """
      fn bump(mut p Point, amt int) {
        val f = fn (mut x, y) -> x + y
        val g = fn (mut a int b string) { return a }
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source);
    FunctionDeclarationSyntax fn = Assert.IsType<FunctionDeclarationSyntax>(Assert.Single(result.Root.Items));
    Assert.Equal(2, fn.Parameters.Length);
    Assert.Equal(TokenKind.MutKeyword, fn.Parameters[0].MutKeyword?.Kind);
    Assert.Null(fn.Parameters[1].MutKeyword);
    Assert.Equal("(Parameter mut p (NamedType Point))", SyntaxPrinter.Print(fn.Parameters[0], result.Lex.Source));

    VariableDeclarationSyntax fDecl = Assert.IsType<VariableDeclarationSyntax>(fn.Body.Items[0]);
    LambdaExpressionSyntax fLambda = Assert.IsType<LambdaExpressionSyntax>(fDecl.Initializer);
    Assert.Equal(2, fLambda.Parameters.Length);
    Assert.Equal(TokenKind.MutKeyword, fLambda.Parameters[0].MutKeyword?.Kind);
    Assert.Null(fLambda.Parameters[0].Type);
    Assert.Null(fLambda.Parameters[1].MutKeyword);

    VariableDeclarationSyntax gDecl = Assert.IsType<VariableDeclarationSyntax>(fn.Body.Items[1]);
    LambdaExpressionSyntax gLambda = Assert.IsType<LambdaExpressionSyntax>(gDecl.Initializer);
    Assert.Equal(2, gLambda.Parameters.Length);
    Assert.Equal(TokenKind.MutKeyword, gLambda.Parameters[0].MutKeyword?.Kind);
    Assert.NotNull(gLambda.Parameters[0].Type);
    Assert.Null(gLambda.Parameters[1].MutKeyword);
    Assert.NotNull(gLambda.Parameters[1].Type);
  }

  [Theory]
  [InlineData("fn (mut int) -> void", "(FunctionType (FunctionTypeParameter mut (NamedType int)) -> (NamedType void))")]
  [InlineData("fn (mut []int, string) -> bool", "(FunctionType (FunctionTypeParameter mut (VectorType (NamedType int))) (FunctionTypeParameter (NamedType string)) -> (NamedType bool))")]
  [InlineData("fn (mut int mut string)", "(FunctionType (FunctionTypeParameter mut (NamedType int)) (FunctionTypeParameter mut (NamedType string)))")]
  [InlineData("fn (mut Point, mut map[string, int]) -> int", "(FunctionType (FunctionTypeParameter mut (NamedType Point)) (FunctionTypeParameter mut (MapType map (NamedType string) (NamedType int))) -> (NamedType int))")]
  public void FunctionTypes_SupportMutPerParameter(string source, string expected) {
    ParseResult<TypeSyntax> result = ParserTestHelpers.ParseType(source);
    Assert.Equal(expected, SyntaxPrinter.Print(result.Node, result.Lex.Source));
    FunctionTypeSyntax fnType = Assert.IsType<FunctionTypeSyntax>(result.Node);
    Assert.Equal(TokenKind.MutKeyword, fnType.Parameters[0].MutKeyword?.Kind);
  }

  [Fact]
  public void CallArguments_SupportMutAndCommaOptionalLists() {
    ParseResult<ExpressionSyntax> single = ParserTestHelpers.ParseExpression("bump(mut a)");
    Assert.Equal(
        "(CallExpression (IdentifierExpression bump) (Argument mut (IdentifierExpression a)))",
        SyntaxPrinter.Print(single.Node, single.Lex.Source));

    ParseResult<ExpressionSyntax> memberArg = ParserTestHelpers.ParseExpression("heal(mut g:player, 10)");
    Assert.Equal(
        "(CallExpression (IdentifierExpression heal) (Argument mut (MemberAccessExpression (IdentifierExpression g) : player)) (Argument (LiteralExpression 10)))",
        SyntaxPrinter.Print(memberArg.Node, memberArg.Lex.Source));

    // Comma-optional call arguments (FN-6): `mut` starts the next argument rather than continuing the previous expression.
    ParseResult<ExpressionSyntax> commaOptional = ParserTestHelpers.ParseExpression("swap(a mut b mut c)");
    CallExpressionSyntax call = Assert.IsType<CallExpressionSyntax>(commaOptional.Node);
    Assert.Equal(3, call.Arguments.Length);
    Assert.Null(call.Arguments[0].MutKeyword);
    Assert.Equal(TokenKind.MutKeyword, call.Arguments[1].MutKeyword?.Kind);
    Assert.Equal(TokenKind.MutKeyword, call.Arguments[2].MutKeyword?.Kind);

    ParseResult<ExpressionSyntax> indexed = ParserTestHelpers.ParseExpression("update(mut v[0], mut m[\"k\"])");
    CallExpressionSyntax indexedCall = Assert.IsType<CallExpressionSyntax>(indexed.Node);
    Assert.Equal(2, indexedCall.Arguments.Length);
    Assert.Equal(TokenKind.MutKeyword, indexedCall.Arguments[0].MutKeyword?.Kind);
    Assert.IsType<IndexExpressionSyntax>(indexedCall.Arguments[0].Expression);
    Assert.Equal(TokenKind.MutKeyword, indexedCall.Arguments[1].MutKeyword?.Kind);
    Assert.IsType<IndexExpressionSyntax>(indexedCall.Arguments[1].Expression);
  }

  // ---------------------------------------------------------------------------------------------
  // PN2022: Invalid `mut` modifier placement & lossless recovery
  // ---------------------------------------------------------------------------------------------

  [Theory]
  [InlineData("struct S { mut pub x int }")]
  [InlineData("struct S { mut @@Testable x int }")]
  [InlineData("struct S { mut pub fn f() {} }")]
  [InlineData("struct S { mut @@Testable fn f() {} }")]
  [InlineData("struct S { static mut fn f() {} }")]
  [InlineData("struct S { mut static fn f() {} }")]
  [InlineData("struct S { pub static mut fn f() {} }")]
  [InlineData("struct S { mut pub static fn f() {} }")]
  public void InvalidMutModifierPlacement_EmitsPN2022AndRecoversLosslessly(string source) {
    ParseResult result = ParserTestHelpers.ParseFile(source, expectNoDiagnostics: false);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.InvalidMutModifierPlacement, diag.Code);
    Assert.Equal("mut", result.Lex.Source.AsSpan(diag.Span).ToString());
    Assert.Contains("'pub mut'", diag.Message, StringComparison.Ordinal);
    Assert.Contains("'static'", diag.Message, StringComparison.Ordinal);
    Assert.NotNull(diag.Help);
    Assert.Contains("pub mut", diag.Help, StringComparison.Ordinal);

    StructDeclarationSyntax st = Assert.IsType<StructDeclarationSyntax>(Assert.Single(result.Root.Items));
    SyntaxNode member = Assert.Single(st.Members);
    Assert.True(member is FieldDeclarationSyntax or MethodDeclarationSyntax);
  }

  [Fact]
  public void DuplicateMutModifier_EmitsPN2022AndKeepsTokenAccounting() {
    ParseResult result = ParserTestHelpers.ParseFile("struct S { mut mut x int }", expectNoDiagnostics: false);
    Assert.Contains(result.Diagnostics, static d => d.Code == DiagnosticCodes.InvalidMutModifierPlacement);
    StructDeclarationSyntax st = Assert.IsType<StructDeclarationSyntax>(Assert.Single(result.Root.Items));
    Assert.Contains(st.Members, static m => m is FieldDeclarationSyntax);
  }

  [Fact]
  public void MutBeforeBareEmbeddedStructName_EmitsPN2004ExpectedType() {
    ParseResult result = ParserTestHelpers.ParseFile("struct S {\n  mut EmbeddedStruct\n}", expectNoDiagnostics: false);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedType, diag.Code);
    StructDeclarationSyntax st = Assert.IsType<StructDeclarationSyntax>(Assert.Single(result.Root.Items));
    FieldDeclarationSyntax field = Assert.IsType<FieldDeclarationSyntax>(Assert.Single(st.Members));
    Assert.Equal(TokenKind.MutKeyword, field.MutKeyword?.Kind);
    Assert.True(field.Type.IsMissing);
  }

  // ---------------------------------------------------------------------------------------------
  // `mut` rejected as identifier or binding keyword
  // ---------------------------------------------------------------------------------------------

  [Theory]
  [InlineData("val mut = 1")]
  [InlineData("var mut = 1")]
  [InlineData("fn mut() {}")]
  [InlineData("fn f(mut mut int) {}")]
  public void MutAsIdentifier_EmitsPN2006(string source) {
    ParseResult result = ParserTestHelpers.ParseFile(source, expectNoDiagnostics: false);
    Assert.Contains(result.Diagnostics, static d => d.Code == DiagnosticCodes.ExpectedIdentifier);
  }

  [Fact]
  public void MutAsBindingKeyword_EmitsParseError() {
    ParseResult result = ParserTestHelpers.ParseFile("mut x = 1", expectNoDiagnostics: false);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedStatement, diag.Code);
  }

  // ---------------------------------------------------------------------------------------------
  // PN2023: Removed identity operators `===` and `!==`
  // ---------------------------------------------------------------------------------------------

  [Fact]
  public void RemovedIdentityOperators_EmitPN2023AndRecoverAsBinaryExpression() {
    ParseResult<ExpressionSyntax> eq = ParserTestHelpers.ParseExpression("a === b", expectNoDiagnostics: false);
    Diagnostic eqDiag = Assert.Single(eq.Diagnostics);
    Assert.Equal(DiagnosticCodes.RemovedIdentityOperator, eqDiag.Code);
    Assert.Equal("===", eq.Lex.Source.AsSpan(eqDiag.Span).ToString());
    Assert.Contains("'===' has been removed", eqDiag.Message, StringComparison.Ordinal);
    Assert.Equal("replace '===' with '=='", eqDiag.Help);
    BinaryExpressionSyntax eqBin = Assert.IsType<BinaryExpressionSyntax>(eq.Node);
    Assert.Equal(TokenKind.EqualEqual, eqBin.OperatorToken.Kind);
    Assert.Equal(TokenKind.Assign, eqBin.SecondaryOperatorToken?.Kind);
    Assert.Equal("(BinaryExpression (IdentifierExpression a) == = (IdentifierExpression b))", SyntaxPrinter.Print(eq.Node, eq.Lex.Source));

    ParseResult<ExpressionSyntax> neq = ParserTestHelpers.ParseExpression("a !== b", expectNoDiagnostics: false);
    Diagnostic neqDiag = Assert.Single(neq.Diagnostics);
    Assert.Equal(DiagnosticCodes.RemovedIdentityOperator, neqDiag.Code);
    Assert.Equal("!==", neq.Lex.Source.AsSpan(neqDiag.Span).ToString());
    Assert.Contains("'!==' has been removed", neqDiag.Message, StringComparison.Ordinal);
    Assert.Equal("replace '!==' with '!='", neqDiag.Help);
    BinaryExpressionSyntax neqBin = Assert.IsType<BinaryExpressionSyntax>(neq.Node);
    Assert.Equal(TokenKind.BangEqual, neqBin.OperatorToken.Kind);
    Assert.Equal(TokenKind.Assign, neqBin.SecondaryOperatorToken?.Kind);
    Assert.Equal("(BinaryExpression (IdentifierExpression a) != = (IdentifierExpression b))", SyntaxPrinter.Print(neq.Node, neq.Lex.Source));
  }

  [Fact]
  public void SpacedDoubleEqualsThenAssign_DoesNotEmitPN2023() {
    ParseResult<ExpressionSyntax> spaced = ParserTestHelpers.ParseExpression("a == = b", expectNoDiagnostics: false);
    Assert.DoesNotContain(spaced.Diagnostics, static d => d.Code == DiagnosticCodes.RemovedIdentityOperator);
    Assert.Contains(spaced.Diagnostics, static d => d.Code == DiagnosticCodes.ExpectedExpression);

    ParseResult<ExpressionSyntax> spacedNeq = ParserTestHelpers.ParseExpression("a != = b", expectNoDiagnostics: false);
    Assert.DoesNotContain(spacedNeq.Diagnostics, static d => d.Code == DiagnosticCodes.RemovedIdentityOperator);
    Assert.Contains(spacedNeq.Diagnostics, static d => d.Code == DiagnosticCodes.ExpectedExpression);
  }

  [Fact]
  public void ChainedAndHalfTypedIdentityOperators_MaintainInvariants() {
    ParseResult<ExpressionSyntax> chained = ParserTestHelpers.ParseExpression("a === b !== c", expectNoDiagnostics: false);
    Assert.Equal(2, chained.Diagnostics.Length);
    Assert.All(chained.Diagnostics, static d => Assert.Equal(DiagnosticCodes.RemovedIdentityOperator, d.Code));

    ParseResult<ExpressionSyntax> halfTyped = ParserTestHelpers.ParseExpression("a ===", expectNoDiagnostics: false);
    Assert.Equal(2, halfTyped.Diagnostics.Length);
    Assert.Equal(DiagnosticCodes.RemovedIdentityOperator, halfTyped.Diagnostics[0].Code);
    Assert.Equal(DiagnosticCodes.ExpectedExpression, halfTyped.Diagnostics[1].Code);
    BinaryExpressionSyntax bin = Assert.IsType<BinaryExpressionSyntax>(halfTyped.Node);
    Assert.True(bin.Right.IsMissing);
  }

  // ---------------------------------------------------------------------------------------------
  // A2 half-typed `mut` inputs & A3 edit locality
  // ---------------------------------------------------------------------------------------------

  [Theory]
  [InlineData("struct S { mut")]
  [InlineData("struct S { mut }")]
  [InlineData("struct S { mut pub }")]
  [InlineData("struct S { static mut }")]
  [InlineData("interface I { mut")]
  [InlineData("interface I { mut }")]
  [InlineData("fn f(mut")]
  [InlineData("fn f(mut)")]
  [InlineData("fn f(mut, a int) {}")]
  [InlineData("val f = fn (mut")]
  [InlineData("val f = fn (mut) -> 1")]
  [InlineData("val g fn (mut) -> void = h")]
  [InlineData("val g fn (mut, int) -> void = h")]
  [InlineData("f(mut")]
  [InlineData("f(mut)")]
  [InlineData("f(a, mut)")]
  [InlineData("f(mut, a)")]
  public void HalfTypedMutInputs_RecoverWithoutCrashAndPreserveAllTokens(string source) {
    ParseResult result = ParserTestHelpers.ParseFile(source, expectNoDiagnostics: false);
    Assert.NotEmpty(result.Diagnostics);
  }

  [Fact]
  public void EditLocality_BrokenMutDeclarationBeforeColumn1Function_PreservesTail() {
    const string Clean = """
      struct Counter {
        mut count int
        mut fn inc(mut x int) { count += x }
      }

      fn tail_ok() int {
        return 42
      }
      """;

    const string Broken = """
      struct Counter {
        mut
        mut fn inc(mut) { count +=

      fn tail_ok() int {
        return 42
      }
      """;

    ParseResult cleanParse = ParserTestHelpers.ParseFile(Clean);
    ParseResult brokenParse = ParserTestHelpers.ParseFile(Broken, expectNoDiagnostics: false);

    Assert.True(brokenParse.Root.Items.Length >= 2);
    SyntaxNode cleanTail = cleanParse.Root.Items[^1];
    SyntaxNode brokenTail = brokenParse.Root.Items[^1];
    Assert.True(
        SyntaxEquivalence.AreEquivalent(cleanTail, cleanParse.Lex.Source, brokenTail, brokenParse.Lex.Source, out string? diff),
        diff);
  }
}
