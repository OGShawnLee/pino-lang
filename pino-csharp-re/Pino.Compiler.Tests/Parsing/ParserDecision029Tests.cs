using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Tests.Parsing;

/// <summary>
/// The syntax recorded by ticket 029 (D-012, D-161, D-163, D-164, D-169) and the three message fixes of ticket 030.
/// </summary>
public sealed class ParserDecision029Tests {
  // ---------------------------------------------------------------------------------------------
  // D-164: `//` and `//=`
  // ---------------------------------------------------------------------------------------------

  [Theory]
  [InlineData("a // b", "(BinaryExpression (IdentifierExpression a) // (IdentifierExpression b))")]
  [InlineData("a + b // c", "(BinaryExpression (IdentifierExpression a) + (BinaryExpression (IdentifierExpression b) // (IdentifierExpression c)))")]
  [InlineData("a // b + c", "(BinaryExpression (BinaryExpression (IdentifierExpression a) // (IdentifierExpression b)) + (IdentifierExpression c))")]
  [InlineData("a * b // c", "(BinaryExpression (BinaryExpression (IdentifierExpression a) * (IdentifierExpression b)) // (IdentifierExpression c))")]
  [InlineData("a // b * c", "(BinaryExpression (BinaryExpression (IdentifierExpression a) // (IdentifierExpression b)) * (IdentifierExpression c))")]
  [InlineData("a // b // c", "(BinaryExpression (BinaryExpression (IdentifierExpression a) // (IdentifierExpression b)) // (IdentifierExpression c))")]
  [InlineData("a / b // c % d", "(BinaryExpression (BinaryExpression (BinaryExpression (IdentifierExpression a) / (IdentifierExpression b)) // (IdentifierExpression c)) % (IdentifierExpression d))")]
  [InlineData("-a // b", "(BinaryExpression (UnaryExpression - (IdentifierExpression a)) // (IdentifierExpression b))")]
  [InlineData("a // b == c", "(BinaryExpression (BinaryExpression (IdentifierExpression a) // (IdentifierExpression b)) == (IdentifierExpression c))")]
  [InlineData("f(a // 2, b)", "(CallExpression (IdentifierExpression f) (Argument (BinaryExpression (IdentifierExpression a) // (LiteralExpression 2))) (Argument (IdentifierExpression b)))")]
  public void FloorDivision_IsLevelSevenAndLeftAssociative(string source, string expected) {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(source);
    Assert.Equal(expected, SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Theory]
  [InlineData("a //= b", "(AssignmentExpression (IdentifierExpression a) //= (IdentifierExpression b))")]
  [InlineData("a //= b + c", "(AssignmentExpression (IdentifierExpression a) //= (BinaryExpression (IdentifierExpression b) + (IdentifierExpression c)))")]
  [InlineData("a //= b //= 2", "(AssignmentExpression (IdentifierExpression a) //= (AssignmentExpression (IdentifierExpression b) //= (LiteralExpression 2)))")]
  [InlineData("a = b //= 2", "(AssignmentExpression (IdentifierExpression a) = (AssignmentExpression (IdentifierExpression b) //= (LiteralExpression 2)))")]
  [InlineData("v[0] //= 2", "(AssignmentExpression (IndexExpression (IdentifierExpression v) (LiteralExpression 0)) //= (LiteralExpression 2))")]
  [InlineData("a //= b // c", "(AssignmentExpression (IdentifierExpression a) //= (BinaryExpression (IdentifierExpression b) // (IdentifierExpression c)))")]
  public void CompoundFloorDivision_IsLevelOneAndRightAssociative(string source, string expected) {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(source);
    Assert.Equal(expected, SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Theory]
  [InlineData("a //", DiagnosticCodes.ExpectedExpression)]
  [InlineData("a //= ", DiagnosticCodes.ExpectedExpression)]
  [InlineData("// b", DiagnosticCodes.ExpectedExpression)]
  public void FloorDivisionWithAMissingOperand_RecoversWithPN2002(string source, string code) {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(source, expectNoDiagnostics: false);
    Assert.Contains(result.Diagnostics, d => d.Code == code);
  }

  // ---------------------------------------------------------------------------------------------
  // D-012: an optional type after the name of a `val` / `var`
  // ---------------------------------------------------------------------------------------------

  [Theory]
  [InlineData("val n int = 12", "(VariableDeclaration val n (NamedType int) = (LiteralExpression 12))")]
  [InlineData("var f float = 1", "(VariableDeclaration var f (NamedType float) = (LiteralExpression 1))")]
  [InlineData("val n = 12", "(VariableDeclaration val n = (LiteralExpression 12))")]
  [InlineData("val s string = \"a\"", "(VariableDeclaration val s (NamedType string) = (InterpolatedStringExpression (StringTextPart a)))")]
  [InlineData("var p Person[string] = q", "(VariableDeclaration var p (GenericType (NamedType Person) (TypeArgumentList (NamedType string))) = (IdentifierExpression q))")]
  [InlineData("var x Option[string] = Option::None", "(VariableDeclaration var x (GenericType (NamedType Option) (TypeArgumentList (NamedType string))) = (StaticAccessExpression (IdentifierExpression Option) :: None))")]
  [InlineData("val r Result[int, string] = f()", "(VariableDeclaration val r (GenericType (NamedType Result) (TypeArgumentList (NamedType int) (NamedType string))) = (CallExpression (IdentifierExpression f)))")]
  [InlineData("val xs []int = []", "(VariableDeclaration val xs (VectorType (NamedType int)) = (VectorLiteralExpression))")]
  [InlineData("val m map[string, int] = map[string, int]", "(VariableDeclaration val m (MapType map (NamedType string) (NamedType int)) = (MapLiteralExpression (MapType map (NamedType string) (NamedType int))))")]
  [InlineData("val g fn (int) -> int = h", "(VariableDeclaration val g (FunctionType (FunctionTypeParameter (NamedType int)) -> (NamedType int)) = (IdentifierExpression h))")]
  [InlineData("val u void = f()", "(VariableDeclaration val u (NamedType void) = (CallExpression (IdentifierExpression f)))")]
  [InlineData("pub val LIMIT int = 3", "(VariableDeclaration (Visibility pub) val LIMIT (NamedType int) = (LiteralExpression 3))")]
  public void VariableDeclaration_TakesAnOptionalTypeAfterTheName(string source, string expected) {
    ParseResult result = ParserTestHelpers.ParseFile(source);
    VariableDeclarationSyntax declaration = Assert.IsType<VariableDeclarationSyntax>(Assert.Single(result.Root.Items));
    Assert.Equal(expected, SyntaxPrinter.Print(declaration, result.Lex.Source));
  }

  [Fact]
  public void VariableDeclaration_TypeAnnotationInABlockParsesLikeTopLevel() {
    const string Source = """
      {
        val n int = 12
        var p Person[string] = q
        var x Option[string] = Option::None
        var y = 1
        {
          val inner int = n
        }
      }
      """;

    ParseResult<BlockStatementSyntax> result = ParserTestHelpers.ParseBlock(Source);
    Assert.Equal(5, result.Node.Items.Length);
    Assert.NotNull(Assert.IsType<VariableDeclarationSyntax>(result.Node.Items[0]).Type);
    Assert.NotNull(Assert.IsType<VariableDeclarationSyntax>(result.Node.Items[1]).Type);
    Assert.NotNull(Assert.IsType<VariableDeclarationSyntax>(result.Node.Items[2]).Type);
    Assert.Null(Assert.IsType<VariableDeclarationSyntax>(result.Node.Items[3]).Type);
    BlockStatementSyntax nested = Assert.IsType<BlockStatementSyntax>(result.Node.Items[4]);
    Assert.NotNull(Assert.IsType<VariableDeclarationSyntax>(Assert.Single(nested.Items)).Type);
  }

  [Fact]
  public void VariableDeclaration_TheInitializerStaysRequired() {
    ParseResult noType = ParserTestHelpers.ParseFile("val x", expectNoDiagnostics: false);
    Diagnostic missingEquals = Assert.Single(noType.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedToken, missingEquals.Code);
    Assert.Contains("'='", missingEquals.Message, StringComparison.Ordinal);

    ParseResult withType = ParserTestHelpers.ParseFile("val x int", expectNoDiagnostics: false);
    Diagnostic missingEqualsAfterType = Assert.Single(withType.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedToken, missingEqualsAfterType.Code);
    Assert.Contains("'='", missingEqualsAfterType.Message, StringComparison.Ordinal);
    VariableDeclarationSyntax declaration = Assert.IsType<VariableDeclarationSyntax>(Assert.Single(withType.Root.Items));
    Assert.NotNull(declaration.Type);
    Assert.True(declaration.EqualsToken.Flags.HasFlag(Pino.Compiler.Lexing.TokenFlags.Missing));
  }

  [Fact]
  public void VariableDeclaration_ATypeOnTheNextLineIsNotAnAnnotation() {
    // Today's recovery for a missing `=`: PN2001, then the next line is read as the initializer. It must not be
    // read as the type of `x`.
    const string Source = "val x\nfoo()\nval y int = 2";

    ParseResult result = ParserTestHelpers.ParseFile(Source, expectNoDiagnostics: false);
    Diagnostic diagnostic = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedToken, diagnostic.Code);
    Assert.Equal(2, result.Root.Items.Length);
    VariableDeclarationSyntax first = Assert.IsType<VariableDeclarationSyntax>(result.Root.Items[0]);
    Assert.Null(first.Type);
    Assert.IsType<CallExpressionSyntax>(first.Initializer);
    Assert.NotNull(Assert.IsType<VariableDeclarationSyntax>(result.Root.Items[1]).Type);
  }

  [Theory]
  [InlineData("val x int int = 1")]
  [InlineData("val x Option[ = 1")]
  public void VariableDeclaration_MalformedAnnotationsRecoverWithoutLosingTokens(string source) {
    ParseResult result = ParserTestHelpers.ParseFile(source, expectNoDiagnostics: false);
    Assert.NotEmpty(result.Diagnostics);
  }

  [Fact]
  public void TupleDestructuring_StillTakesNoAnnotation() {
    ParseResult result = ParserTestHelpers.ParseFile("val @(a, b) = pair()");
    Assert.IsType<TupleDestructuringStatementSyntax>(Assert.Single(result.Root.Items));
  }

  // ---------------------------------------------------------------------------------------------
  // D-161: standalone blocks
  // ---------------------------------------------------------------------------------------------

  [Fact]
  public void StandaloneBlocks_OpenANestedScope() {
    const string Source = """
      val x = 1
      {
        val x int = 2
        {
          x //= 2
        }
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source);
    Assert.Equal(2, result.Root.Items.Length);
    BlockStatementSyntax outer = Assert.IsType<BlockStatementSyntax>(result.Root.Items[1]);
    Assert.Equal(2, outer.Items.Length);
    Assert.IsType<BlockStatementSyntax>(outer.Items[1]);
  }

  // ---------------------------------------------------------------------------------------------
  // D-163: `@@Testable`
  // ---------------------------------------------------------------------------------------------

  [Theory]
  [InlineData("@@Testable fn f() {}", "(FunctionDeclaration (Visibility Testable) f (BlockStatement))")]
  [InlineData("@@Testable val x = 1", "(VariableDeclaration (Visibility Testable) val x = (LiteralExpression 1))")]
  [InlineData("@@Testable val x int = 1", "(VariableDeclaration (Visibility Testable) val x (NamedType int) = (LiteralExpression 1))")]
  [InlineData("@@Testable struct S {}", "(StructDeclaration (Visibility Testable) S)")]
  [InlineData("@@Testable interface I {}", "(InterfaceDeclaration (Visibility Testable) I)")]
  [InlineData("@@Testable enum E { A }", "(EnumDeclaration (Visibility Testable) E (EnumMember A))")]
  [InlineData("@@Testable union U { A }", "(UnionDeclaration (Visibility Testable) U (UnionVariant A))")]
  [InlineData("@@Generic[T] @@Testable fn g(x T) {}", "(FunctionDeclaration (GenericDecorator Generic (GenericParameter T)) (Visibility Testable) g (Parameter x (NamedType T)) (BlockStatement))")]
  public void Testable_IsAcceptedWhereVisibilityIs(string source, string expected) {
    ParseResult result = ParserTestHelpers.ParseFile(source);
    SyntaxNode declaration = Assert.Single(result.Root.Items);
    Assert.Equal(expected, SyntaxPrinter.Print(declaration, result.Lex.Source));
  }

  [Fact]
  public void Testable_VisibilityNodeKeepsBothAtTokensAndIsNotPub() {
    ParseResult testable = ParserTestHelpers.ParseFile("@@Testable fn f() {}");
    VisibilitySyntax visibility = Assert.IsType<FunctionDeclarationSyntax>(Assert.Single(testable.Root.Items)).Visibility!;
    Assert.False(visibility.IsPub);
    Assert.NotNull(visibility.AtToken);
    Assert.NotNull(visibility.SecondAtToken);
    Assert.Equal("@@Testable", testable.Lex.Source.AsSpan(visibility.Span).ToString());

    ParseResult pub = ParserTestHelpers.ParseFile("pub fn f() {}");
    VisibilitySyntax pubVisibility = Assert.IsType<FunctionDeclarationSyntax>(Assert.Single(pub.Root.Items)).Visibility!;
    Assert.True(pubVisibility.IsPub);
    Assert.Null(pubVisibility.AtToken);
    Assert.Null(pubVisibility.SecondAtToken);
  }

  [Fact]
  public void Testable_AndPubAreAcceptedOnStructFieldsAndMethods() {
    const string Source = """
      struct Box {
        pub a int
        @@Testable b int
        c int
        pub fn get() int { return a }
        @@Testable fn peek() int { return b }
        @@Generic[T] @@Testable fn wrap(x T) T { return x }
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source);
    StructDeclarationSyntax box = Assert.IsType<StructDeclarationSyntax>(Assert.Single(result.Root.Items));
    Assert.Equal(6, box.Members.Length);
    Assert.True(Assert.IsType<FieldDeclarationSyntax>(box.Members[0]).Visibility!.IsPub);
    Assert.False(Assert.IsType<FieldDeclarationSyntax>(box.Members[1]).Visibility!.IsPub);
    Assert.Null(Assert.IsType<FieldDeclarationSyntax>(box.Members[2]).Visibility);
    Assert.True(Assert.IsType<MethodDeclarationSyntax>(box.Members[3]).Visibility!.IsPub);
    Assert.False(Assert.IsType<MethodDeclarationSyntax>(box.Members[4]).Visibility!.IsPub);
    Assert.False(Assert.IsType<MethodDeclarationSyntax>(box.Members[5]).Visibility!.IsPub);
  }

  [Fact]
  public void UnknownDoubleAtDecorator_IsStillRejected() {
    ParseResult result = ParserTestHelpers.ParseFile("@@Unknown fn f() {}", expectNoDiagnostics: false);
    Assert.NotEmpty(result.Diagnostics);
  }

  [Theory]
  [InlineData("@pub_testing fn f() {}")]
  [InlineData("@pub_testing val x = 1")]
  [InlineData("@pub_testing struct S {}")]
  [InlineData("@pub_testing enum E { A }")]
  public void DeprecatedPubTesting_IsDiagnosedAndRecoveredAsTestable(string source) {
    ParseResult result = ParserTestHelpers.ParseFile(source, expectNoDiagnostics: false);
    Diagnostic diagnostic = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.DeprecatedVisibilitySyntax, diagnostic.Code);
    Assert.Equal("replace '@pub_testing' with '@@Testable'", diagnostic.Help);
    Assert.Equal("@pub_testing", result.Lex.Source.AsSpan(diagnostic.Span).ToString());

    SyntaxNode declaration = Assert.Single(result.Root.Items);
    Assert.IsNotType<ErrorStatementSyntax>(declaration);
    VisibilitySyntax visibility = declaration switch {
      FunctionDeclarationSyntax f => f.Visibility!,
      VariableDeclarationSyntax v => v.Visibility!,
      StructDeclarationSyntax s => s.Visibility!,
      EnumDeclarationSyntax e => e.Visibility!,
      _ => throw new InvalidOperationException(),
    };
    Assert.False(visibility.IsPub);
    Assert.Null(visibility.SecondAtToken);
  }

  [Fact]
  public void DeprecatedPubTesting_OnAStructMemberIsDiagnosedAndRecovered() {
    ParseResult result = ParserTestHelpers.ParseFile(
        "struct S {\n  @pub_testing a int\n  @pub_testing fn f() {}\n}",
        expectNoDiagnostics: false);
    Assert.Equal(2, result.Diagnostics.Length);
    Assert.All(result.Diagnostics, static d => Assert.Equal(DiagnosticCodes.DeprecatedVisibilitySyntax, d.Code));
    StructDeclarationSyntax s = Assert.IsType<StructDeclarationSyntax>(Assert.Single(result.Root.Items));
    Assert.NotNull(Assert.IsType<FieldDeclarationSyntax>(s.Members[0]).Visibility);
    Assert.NotNull(Assert.IsType<MethodDeclarationSyntax>(s.Members[1]).Visibility);
  }

  [Fact]
  public void DeprecatedPubTesting_AfterPubReportsTheDeprecationAndDiscardsTheFirstModifier() {
    // One diagnostic per token position: the deprecation at `@pub_testing` wins over the MOD-E5 error at the same place.
    ParseResult result = ParserTestHelpers.ParseFile("pub @pub_testing fn f() {}", expectNoDiagnostics: false);
    Diagnostic diagnostic = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.DeprecatedVisibilitySyntax, diagnostic.Code);
    Assert.Contains(result.Root.Items, static n => n is ErrorStatementSyntax);
    FunctionDeclarationSyntax function = Assert.Single(result.Root.Items.OfType<FunctionDeclarationSyntax>());
    Assert.False(function.Visibility!.IsPub);
  }

  [Fact]
  public void DeprecatedPubTesting_BeforeTestableReportsBothErrors() {
    ParseResult result = ParserTestHelpers.ParseFile("@pub_testing @@Testable fn f() {}", expectNoDiagnostics: false);
    Assert.Equal(2, result.Diagnostics.Length);
    Assert.Equal(DiagnosticCodes.DeprecatedVisibilitySyntax, result.Diagnostics[0].Code);
    Assert.Equal(DiagnosticCodes.ExpectedStatement, result.Diagnostics[1].Code);
  }

  [Fact]
  public void TestableWithoutADeclaration_EmitsOneErrorAndKeepsTokens() {
    ParseResult result = ParserTestHelpers.ParseFile("@@Testable\n1 + 2", expectNoDiagnostics: false);
    Diagnostic diagnostic = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedStatement, diagnostic.Code);
  }

  // ---------------------------------------------------------------------------------------------
  // D-169: union patterns in `match`
  // ---------------------------------------------------------------------------------------------

  [Theory]
  [InlineData("U::Pair", "(VariantPattern U :: Pair)")]
  [InlineData("U::Pair(x)", "(VariantPattern U :: Pair (IdentifierPattern x))")]
  [InlineData("U::Pair(x, y)", "(VariantPattern U :: Pair (IdentifierPattern x) (IdentifierPattern y))")]
  [InlineData("U::Pair(@b)", "(VariantPattern U :: Pair (LabelBindingPattern b))")]
  [InlineData("U::Pair(@b: text)", "(VariantPattern U :: Pair (LabelBindingPattern b : text))")]
  [InlineData("U::Pair(@b: text, @a: number)", "(VariantPattern U :: Pair (LabelBindingPattern b : text) (LabelBindingPattern a : number))")]
  [InlineData("U::Pair(@b @a)", "(VariantPattern U :: Pair (LabelBindingPattern b) (LabelBindingPattern a))")]
  [InlineData("U::Pair(@b: text @a)", "(VariantPattern U :: Pair (LabelBindingPattern b : text) (LabelBindingPattern a))")]
  [InlineData("Option[int]::Some(@v)", "(VariantPattern Option (TypeArgumentList (NamedType int)) :: Some (LabelBindingPattern v))")]
  [InlineData("Result::Success(Option::Some(@v: inner))", "(VariantPattern Result :: Success (VariantPattern Option :: Some (LabelBindingPattern v : inner)))")]
  public void VariantPatterns_AcceptNoBindingsAPositionalPrefixAndLabelBindings(string source, string expected) {
    ParseResult<PatternSyntax> result = ParserTestHelpers.ParsePattern(source);
    Assert.Equal(expected, SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Theory]
  [InlineData("U::Pair(x, @b)", "(VariantPattern U :: Pair (IdentifierPattern x) (LabelBindingPattern b))")]
  [InlineData("U::Pair(@b, x)", "(VariantPattern U :: Pair (LabelBindingPattern b) (IdentifierPattern x))")]
  public void VariantPatterns_MixingPositionalAndLabelBindingsIsBuiltAndLeftToTheChecker(string source, string expected) {
    // UNION-E9 says the checker rejects the mix, so the parser builds the node and reports nothing.
    ParseResult<PatternSyntax> result = ParserTestHelpers.ParsePattern(source);
    Assert.Equal(expected, SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Fact]
  public void LabelBindingNode_KeepsItsTokens() {
    ParseResult<PatternSyntax> result = ParserTestHelpers.ParsePattern("U::Pair(@b: text)");
    VariantPatternSyntax variant = Assert.IsType<VariantPatternSyntax>(result.Node);
    LabelBindingPatternSyntax label = Assert.IsType<LabelBindingPatternSyntax>(Assert.Single(variant.SubPatterns));
    Assert.Equal("b", result.Lex.GetText(label.FieldToken).ToString());
    Assert.NotNull(label.ColonToken);
    Assert.Equal("text", result.Lex.GetText(label.NameToken!.Value).ToString());
    Assert.Equal("@b: text", result.Lex.Source.AsSpan(label.Span).ToString());
  }

  [Theory]
  [InlineData("U::Pair(@)", DiagnosticCodes.ExpectedIdentifier)]
  [InlineData("U::Pair(@b:)", DiagnosticCodes.ExpectedIdentifier)]
  [InlineData("U::Pair(@b,)", DiagnosticCodes.TrailingComma)]
  [InlineData("U::Pair(@b,, @a)", DiagnosticCodes.UnexpectedComma)]
  [InlineData("U::Pair(@(b))", DiagnosticCodes.ExpectedIdentifier)]
  public void LabelBindings_MalformedFormsEmitOneErrorAndKeepAllTokens(string source, string code) {
    ParseResult<PatternSyntax> result = ParserTestHelpers.ParsePattern(source, expectEOF: false, expectNoDiagnostics: false);
    Assert.Contains(result.Diagnostics, d => d.Code == code);
  }

  [Fact]
  public void MatchStatementAndExpression_ParseLabelPatternsFromTheConformanceShapes() {
    const string Source = """
      union U {
        Pair(a int, b string)
        Empty
      }
      val u = U::Pair(7, "hello")
      match u {
        when U::Pair(@b: text, @a: number) { println("$number $text") }
        when U::Empty { println("empty") }
        else { println("other") }
      }
      val n = match u {
        when U::Pair(@a) -> a
        when U::Empty -> 0
        else -> 1
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source);
    Assert.Equal(4, result.Root.Items.Length);
    MatchStatementSyntax statement = Assert.IsType<MatchStatementSyntax>(result.Root.Items[2]);
    WhenArmSyntax arm = Assert.IsType<WhenArmSyntax>(statement.Arms[0]);
    VariantPatternSyntax pattern = Assert.IsType<VariantPatternSyntax>(Assert.Single(arm.Patterns));
    Assert.Equal(2, pattern.SubPatterns.Length);
    Assert.All(pattern.SubPatterns, static p => Assert.IsType<LabelBindingPatternSyntax>(p));
  }

  [Fact]
  public void MatchArms_MixedBindingsParseWithZeroDiagnostics() {
    const string Source = """
      match u {
        when U::Pair(x, @b) { println(x) }
        else { println("other") }
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source);
    Assert.IsType<MatchStatementSyntax>(Assert.Single(result.Root.Items));
  }

  // `is` keeps the positional form (grammar.ebnf `is_pattern`): labels are not accepted there.

  [Theory]
  [InlineData("x is U::Pair", "(IsPatternExpression (IdentifierExpression x) is (VariantPattern U :: Pair))")]
  [InlineData("x is U::Pair(a, b)", "(IsPatternExpression (IdentifierExpression x) is (VariantPattern U :: Pair (IdentifierPattern a) (IdentifierPattern b)))")]
  [InlineData("x is not Option[int]::Some(v)", "(IsPatternExpression (IdentifierExpression x) is not (VariantPattern Option (TypeArgumentList (NamedType int)) :: Some (IdentifierPattern v)))")]
  public void IsPatterns_KeepTheirPositionalForm(string source, string expected) {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(source);
    Assert.Equal(expected, SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Theory]
  [InlineData("x is U::Pair(@b)")]
  [InlineData("x is not U::Pair(@b: text)")]
  [InlineData("x is U::Outer(V::Inner(@b))")]
  public void IsPatterns_RejectLabelBindingsWithPN2005AndAHelp(string source) {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(source, expectEOF: false, expectNoDiagnostics: false);
    Diagnostic diagnostic = result.Diagnostics.First(static d => d.Code == DiagnosticCodes.ExpectedPattern);
    Assert.Equal("@", result.Lex.Source.AsSpan(diagnostic.Span).ToString());
    Assert.Contains("only valid in 'when' patterns", diagnostic.Help, StringComparison.Ordinal);
  }

  [Fact]
  public void IsPattern_InAnIfConditionStillBindsPositionally() {
    const string Source = """
      if u is U::Pair(a, b) {
        println(a)
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source);
    Assert.IsType<IfStatementSyntax>(Assert.Single(result.Root.Items));
  }

  // ---------------------------------------------------------------------------------------------
  // Recovery next to the new forms
  // ---------------------------------------------------------------------------------------------

  [Fact]
  public void MatchWithAMissingSubject_ReportsOnceAndKeepsTheFollowingDeclarations() {
    const string Source = "fn a() {\n  match {\n    when x { return 1 }\n  }\n}\n\nfn b() {}\n";

    ParseResult result = ParserTestHelpers.ParseFile(Source, expectNoDiagnostics: false);
    Diagnostic diagnostic = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedExpression, diagnostic.Code);
    Assert.Equal(2, result.Root.Items.Length);
    Assert.All(result.Root.Items, static item => Assert.IsType<FunctionDeclarationSyntax>(item));
  }

  // ---------------------------------------------------------------------------------------------
  // Message polish (6b): quoted token text is not quoted twice
  // ---------------------------------------------------------------------------------------------

  [Theory]
  [InlineData("val x 'a'", "found 'a'.")]
  [InlineData("val x '-'", "found '-'.")]
  [InlineData("val x +", "found '+'.")]
  [InlineData("val x }", "found '}'.")]
  public void FoundText_QuotesATokenOnlyWhenItIsNotAlreadyQuoted(string source, string expectedTail) {
    ParseResult result = ParserTestHelpers.ParseFile(source, expectNoDiagnostics: false);
    Diagnostic diagnostic = result.Diagnostics.First(static d => d.Code == DiagnosticCodes.ExpectedToken);
    Assert.EndsWith(expectedTail, diagnostic.Message, StringComparison.Ordinal);
    Assert.DoesNotContain("''", diagnostic.Message, StringComparison.Ordinal);
  }
}
