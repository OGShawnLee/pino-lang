using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Tests.Parsing;

public sealed class ParserExpressionTests {
  [Fact]
  public void PrecedenceExample_MatchesSpecSExpression() {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression("a + b * c");
    Assert.Equal(
        "(BinaryExpression (IdentifierExpression a) + (BinaryExpression (IdentifierExpression b) * (IdentifierExpression c)))",
        SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Theory]
  [InlineData("a = b or c", "(AssignmentExpression (IdentifierExpression a) = (BinaryExpression (IdentifierExpression b) or (IdentifierExpression c)))")]
  [InlineData("a or b and c", "(BinaryExpression (IdentifierExpression a) or (BinaryExpression (IdentifierExpression b) and (IdentifierExpression c)))")]
  [InlineData("a and b == c", "(BinaryExpression (IdentifierExpression a) and (BinaryExpression (IdentifierExpression b) == (IdentifierExpression c)))")]
  [InlineData("a and x is U::V", "(BinaryExpression (IdentifierExpression a) and (IsPatternExpression (IdentifierExpression x) is (VariantPattern U :: V)))")]
  [InlineData("a == b < c", "(BinaryExpression (IdentifierExpression a) == (BinaryExpression (IdentifierExpression b) < (IdentifierExpression c)))")]
  [InlineData("a == b in c", "(BinaryExpression (IdentifierExpression a) == (BinaryExpression (IdentifierExpression b) in (IdentifierExpression c)))")]
  [InlineData("1 + 1 in xs", "(BinaryExpression (BinaryExpression (LiteralExpression 1) + (LiteralExpression 1)) in (IdentifierExpression xs))")]
  [InlineData("a not in xs", "(BinaryExpression (IdentifierExpression a) not in (IdentifierExpression xs))")]
  [InlineData("a < b + c", "(BinaryExpression (IdentifierExpression a) < (BinaryExpression (IdentifierExpression b) + (IdentifierExpression c)))")]
  [InlineData("a + b * c", "(BinaryExpression (IdentifierExpression a) + (BinaryExpression (IdentifierExpression b) * (IdentifierExpression c)))")]
  [InlineData("-a * b", "(BinaryExpression (UnaryExpression - (IdentifierExpression a)) * (IdentifierExpression b))")]
  [InlineData("not a == b", "(BinaryExpression (UnaryExpression not (IdentifierExpression a)) == (IdentifierExpression b))")]
  [InlineData("-arr[0]", "(UnaryExpression - (IndexExpression (IdentifierExpression arr) (LiteralExpression 0)))")]
  [InlineData("-f()?", "(UnaryExpression - (BubbleExpression (CallExpression (IdentifierExpression f)) ?))")]
  [InlineData("not a and b", "(BinaryExpression (UnaryExpression not (IdentifierExpression a)) and (IdentifierExpression b))")]
  public void AdjacentPrecedenceLevels_AssociateAsSpecified(string source, string expected) {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(source);
    Assert.Equal(expected, SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Theory]
  [InlineData("a = b = 0", "(AssignmentExpression (IdentifierExpression a) = (AssignmentExpression (IdentifierExpression b) = (LiteralExpression 0)))")]
  [InlineData("a += b -= 1", "(AssignmentExpression (IdentifierExpression a) += (AssignmentExpression (IdentifierExpression b) -= (LiteralExpression 1)))")]
  [InlineData("a or b or c", "(BinaryExpression (BinaryExpression (IdentifierExpression a) or (IdentifierExpression b)) or (IdentifierExpression c))")]
  [InlineData("a and b and c", "(BinaryExpression (BinaryExpression (IdentifierExpression a) and (IdentifierExpression b)) and (IdentifierExpression c))")]
  [InlineData("a == b != c === d !== e", "(BinaryExpression (BinaryExpression (BinaryExpression (BinaryExpression (IdentifierExpression a) == (IdentifierExpression b)) != (IdentifierExpression c)) === (IdentifierExpression d)) !== (IdentifierExpression e))")]
  [InlineData("a < b <= c > d >= e", "(BinaryExpression (BinaryExpression (BinaryExpression (BinaryExpression (IdentifierExpression a) < (IdentifierExpression b)) <= (IdentifierExpression c)) > (IdentifierExpression d)) >= (IdentifierExpression e))")]
  [InlineData("a + b - c", "(BinaryExpression (BinaryExpression (IdentifierExpression a) + (IdentifierExpression b)) - (IdentifierExpression c))")]
  [InlineData("a * b / c % d", "(BinaryExpression (BinaryExpression (BinaryExpression (IdentifierExpression a) * (IdentifierExpression b)) / (IdentifierExpression c)) % (IdentifierExpression d))")]
  [InlineData("--a", "(UnaryExpression - (UnaryExpression - (IdentifierExpression a)))")]
  [InlineData("not not a", "(UnaryExpression not (UnaryExpression not (IdentifierExpression a)))")]
  [InlineData("a[0][1]", "(IndexExpression (IndexExpression (IdentifierExpression a) (LiteralExpression 0)) (LiteralExpression 1))")]
  public void Associativity_AtEachLevelIsCorrect(string source, string expected) {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression(source);
    Assert.Equal(expected, SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Fact]
  public void IsPatternAndAnd_ParsesCorrectly() {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression("x is U::V(v) and v > 0");
    Assert.Equal(
        "(BinaryExpression (IsPatternExpression (IdentifierExpression x) is (VariantPattern U :: V (IdentifierPattern v))) and (BinaryExpression (IdentifierExpression v) > (LiteralExpression 0)))",
        SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Fact]
  public void IsNotPattern_ParsesCorrectly() {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression("x is not Option::None");
    Assert.Equal(
        "(IsPatternExpression (IdentifierExpression x) is not (VariantPattern Option :: None))",
        SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Fact]
  public void RecoveryOrBlock_BindsTighterThanAddition_D140() {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression("a + b or { yield 0 }");
    Assert.Equal(
        "(BinaryExpression (IdentifierExpression a) + (RecoveryExpression (IdentifierExpression b) or (RecoveryBlock (YieldStatement (LiteralExpression 0)))))",
        SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Fact]
  public void Lambdas_UntypedAndTypedForms() {
    ParseResult<ExpressionSyntax> untyped = ParserTestHelpers.ParseExpression("fn (n, m) -> n + m");
    Assert.Equal(
        "(LambdaExpression (Parameter n) (Parameter m) -> (BinaryExpression (IdentifierExpression n) + (IdentifierExpression m)))",
        SyntaxPrinter.Print(untyped.Node, untyped.Lex.Source));

    // D-145: without a comma, `fn (n m)` is 1 typed parameter `n` of type `m`.
    ParseResult<ExpressionSyntax> typedSingle = ParserTestHelpers.ParseExpression("fn (n m) -> n");
    Assert.Equal(
        "(LambdaExpression (Parameter n (NamedType m)) -> (IdentifierExpression n))",
        SyntaxPrinter.Print(typedSingle.Node, typedSingle.Lex.Source));

    ParseResult<ExpressionSyntax> blockLambda = ParserTestHelpers.ParseExpression("fn (x int y int) { return x + y }");
    Assert.Equal(
        "(LambdaExpression (Parameter x (NamedType int)) (Parameter y (NamedType int)) (BlockStatement (ReturnStatement (BinaryExpression (IdentifierExpression x) + (IdentifierExpression y)))))",
        SyntaxPrinter.Print(blockLambda.Node, blockLambda.Lex.Source));
  }

  [Fact]
  public void IfThenElseExpression_ParsesCorrectly() {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression("if a > 0 then a else -a");
    Assert.Equal(
        "(IfExpression (BinaryExpression (IdentifierExpression a) > (LiteralExpression 0)) (IdentifierExpression a) (UnaryExpression - (IdentifierExpression a)))",
        SyntaxPrinter.Print(result.Node, result.Lex.Source));
  }

  [Fact]
  public void MatchExpression_ArrowAndBlockArms_AndErrors() {
    ParseResult<ExpressionSyntax> valid = ParserTestHelpers.ParseExpression(
        "match x { when 1, 2 -> 10 when 3 4 { yield 20 } else -> 0 }");
    Assert.Equal(
        "(MatchExpression (IdentifierExpression x) (WhenArm (LiteralPattern (LiteralExpression 1)) (LiteralPattern (LiteralExpression 2)) -> (LiteralExpression 10)) (WhenArm (LiteralPattern (LiteralExpression 3)) (LiteralPattern (LiteralExpression 4)) (BlockStatement (YieldStatement (LiteralExpression 20)))) (ElseArm -> (LiteralExpression 0)))",
        SyntaxPrinter.Print(valid.Node, valid.Lex.Source));

    // AllowBraceLiteral = false in match subject so `Foo { ... }` is not a struct literal.
    ParseResult<ExpressionSyntax> subjNoBrace = ParserTestHelpers.ParseExpression(
        "match Foo { when 1 -> 2 }");
    Assert.IsType<IdentifierExpressionSyntax>(Assert.IsType<MatchExpressionSyntax>(subjNoBrace.Node).Subject);

    // MATCH-E4: `-> {` emits PN2013
    ParseResult<ExpressionSyntax> arrowBlock = ParserTestHelpers.ParseExpression(
        "match x { when 2 -> { yield 1 } }",
        expectNoDiagnostics: false);
    Diagnostic pn2013 = Assert.Single(arrowBlock.Diagnostics);
    Assert.Equal(DiagnosticCodes.ArrowBeforeMatchBlock, pn2013.Code);
    Assert.Equal("{", arrowBlock.Lex.Source.AsSpan(pn2013.Span).ToString());

    // MATCH-E6: `-> return` emits PN2002
    ParseResult<ExpressionSyntax> arrowReturn = ParserTestHelpers.ParseExpression(
        "match x { when 1 -> return }",
        expectNoDiagnostics: false);
    Diagnostic pn2002 = Assert.Single(arrowReturn.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedExpression, pn2002.Code);
    Assert.Equal("return", arrowReturn.Lex.Source.AsSpan(pn2002.Span).ToString());

    // Duplicate else emits PN2018
    ParseResult<ExpressionSyntax> dupElse = ParserTestHelpers.ParseExpression(
        "match x { else -> 1 else -> 2 }",
        expectNoDiagnostics: false);
    Diagnostic pn2018 = Assert.Single(dupElse.Diagnostics);
    Assert.Equal(DiagnosticCodes.DuplicateElseArm, pn2018.Code);
    Assert.Equal("else", dupElse.Lex.Source.AsSpan(pn2018.Span).ToString());
  }

  [Fact]
  public void InterpolatedStrings_IncludingNested_ParseCorrectly() {
    ParseResult<ExpressionSyntax> simple = ParserTestHelpers.ParseExpression("\"hello $name!\"");
    Assert.Equal(
        "(InterpolatedStringExpression (StringTextPart hello ) (StringInterpolationPart (IdentifierExpression name)) (StringTextPart !))",
        SyntaxPrinter.Print(simple.Node, simple.Lex.Source));

    ParseResult<ExpressionSyntax> nested = ParserTestHelpers.ParseExpression("\"outer $(\"inner $x\") end\"");
    Assert.Equal(
        "(InterpolatedStringExpression (StringTextPart outer ) (StringInterpolationPart (InterpolatedStringExpression (StringTextPart inner ) (StringInterpolationPart (IdentifierExpression x)))) (StringTextPart  end))",
        SyntaxPrinter.Print(nested.Node, nested.Lex.Source));
  }

  [Fact]
  public void CollectionForms_AllParseCorrectly() {
    ParseResult<ExpressionSyntax> emptyLit = ParserTestHelpers.ParseExpression("[]");
    Assert.Equal("(VectorLiteralExpression)", SyntaxPrinter.Print(emptyLit.Node, emptyLit.Lex.Source));

    ParseResult<ExpressionSyntax> vecLit = ParserTestHelpers.ParseExpression("[1 2, 3]");
    Assert.Equal(
        "(VectorLiteralExpression (LiteralExpression 1) (LiteralExpression 2) (LiteralExpression 3))",
        SyntaxPrinter.Print(vecLit.Node, vecLit.Lex.Source));

    ParseResult<ExpressionSyntax> vecEmptyTyped = ParserTestHelpers.ParseExpression("[]int");
    Assert.Equal(
        "(VectorInitializerExpression (NamedType int))",
        SyntaxPrinter.Print(vecEmptyTyped.Node, vecEmptyTyped.Lex.Source));

    ParseResult<ExpressionSyntax> vecInit = ParserTestHelpers.ParseExpression("[]int { len: 3, init: 0 cap: 8 }");
    Assert.Equal(
        "(VectorInitializerExpression (NamedType int) (VectorProperty len : (LiteralExpression 3)) (VectorProperty init : (LiteralExpression 0)) (VectorProperty cap : (LiteralExpression 8)))",
        SyntaxPrinter.Print(vecInit.Node, vecInit.Lex.Source));

    ParseResult<ExpressionSyntax> emptyMap = ParserTestHelpers.ParseExpression("map[string, int]");
    Assert.Equal(
        "(MapLiteralExpression (MapType map (NamedType string) (NamedType int)))",
        SyntaxPrinter.Print(emptyMap.Node, emptyMap.Lex.Source));

    ParseResult<ExpressionSyntax> mapWithEntries = ParserTestHelpers.ParseExpression("map[string, int] { \"a\" -> 1 \"b\" -> 2 }");
    Assert.Equal(
        "(MapLiteralExpression (MapType map (NamedType string) (NamedType int)) (MapEntry (InterpolatedStringExpression (StringTextPart a)) -> (LiteralExpression 1)) (MapEntry (InterpolatedStringExpression (StringTextPart b)) -> (LiteralExpression 2)))",
        SyntaxPrinter.Print(mapWithEntries.Node, mapWithEntries.Lex.Source));

    ParseResult<ExpressionSyntax> emptySet = ParserTestHelpers.ParseExpression("set[int]");
    Assert.Equal(
        "(SetLiteralExpression (SetType set (NamedType int)))",
        SyntaxPrinter.Print(emptySet.Node, emptySet.Lex.Source));

    ParseResult<ExpressionSyntax> setWithItems = ParserTestHelpers.ParseExpression("set[int] { 1, 2 3 }");
    Assert.Equal(
        "(SetLiteralExpression (SetType set (NamedType int)) (LiteralExpression 1) (LiteralExpression 2) (LiteralExpression 3))",
        SyntaxPrinter.Print(setWithItems.Node, setWithItems.Lex.Source));
  }

  [Fact]
  public void StructAndTupleLiterals_ParseCorrectly() {
    ParseResult<ExpressionSyntax> point = ParserTestHelpers.ParseExpression("Point { x: 1, y }");
    Assert.Equal(
        "(StructLiteralExpression (NamedType Point) (StructFieldInit x : (LiteralExpression 1)) (StructFieldInit y))",
        SyntaxPrinter.Print(point.Node, point.Lex.Source));

    ParseResult<ExpressionSyntax> qualified = ParserTestHelpers.ParseExpression("Mod::Point { x: 1 }");
    Assert.Equal(
        "(StructLiteralExpression (QualifiedType Mod :: Point) (StructFieldInit x : (LiteralExpression 1)))",
        SyntaxPrinter.Print(qualified.Node, qualified.Lex.Source));

    ParseResult<ExpressionSyntax> generic = ParserTestHelpers.ParseExpression("Pair[int, string] { first: 1 second: \"a\" }");
    Assert.Equal(
        "(StructLiteralExpression (GenericType (NamedType Pair) (TypeArgumentList (NamedType int) (NamedType string))) (StructFieldInit first : (LiteralExpression 1)) (StructFieldInit second : (InterpolatedStringExpression (StringTextPart a))))",
        SyntaxPrinter.Print(generic.Node, generic.Lex.Source));

    ParseResult<ExpressionSyntax> tuple = ParserTestHelpers.ParseExpression("@( a: 1, b )");
    Assert.Equal(
        "(TupleLiteralExpression (TupleField a : (LiteralExpression 1)) (TupleField b))",
        SyntaxPrinter.Print(tuple.Node, tuple.Lex.Source));
  }

  [Fact]
  public void PostfixDisambiguation_D076_D055_GenericCalls() {
    // D-076: r:items[1] is member access then index.
    ParseResult<ExpressionSyntax> memberIndex = ParserTestHelpers.ParseExpression("r:items[1]");
    Assert.Equal(
        "(IndexExpression (MemberAccessExpression (IdentifierExpression r) : items) (LiteralExpression 1))",
        SyntaxPrinter.Print(memberIndex.Node, memberIndex.Lex.Source));

    // D-055: fns[0](9) is index then call.
    ParseResult<ExpressionSyntax> indexedCall = ParserTestHelpers.ParseExpression("fns[0](9)");
    Assert.Equal(
        "(CallExpression (IndexExpression (IdentifierExpression fns) (LiteralExpression 0)) (Argument (LiteralExpression 9)))",
        SyntaxPrinter.Print(indexedCall.Node, indexedCall.Lex.Source));

    // Step 4: fns[i](9) parses as generic call (checker reinterprets if fns is a value).
    ParseResult<ExpressionSyntax> identBracketCall = ParserTestHelpers.ParseExpression("fns[i](9)");
    Assert.Equal(
        "(CallExpression (IdentifierExpression fns) (TypeArgumentList (NamedType i)) (Argument (LiteralExpression 9)))",
        SyntaxPrinter.Print(identBracketCall.Node, identBracketCall.Lex.Source));

    ParseResult<ExpressionSyntax> genericCall = ParserTestHelpers.ParseExpression("print_area[Square](s)");
    Assert.Equal(
        "(CallExpression (IdentifierExpression print_area) (TypeArgumentList (NamedType Square)) (Argument (IdentifierExpression s)))",
        SyntaxPrinter.Print(genericCall.Node, genericCall.Lex.Source));

    ParseResult<ExpressionSyntax> genericMethod = ParserTestHelpers.ParseExpression("xs:map[string](f)");
    Assert.Equal(
        "(CallExpression (MemberAccessExpression (IdentifierExpression xs) : map) (TypeArgumentList (NamedType string)) (Argument (IdentifierExpression f)))",
        SyntaxPrinter.Print(genericMethod.Node, genericMethod.Lex.Source));

    ParseResult<ExpressionSyntax> genericStatic = ParserTestHelpers.ParseExpression("Option[int]::Some(1)");
    Assert.Equal(
        "(CallExpression (StaticAccessExpression (IdentifierExpression Option) (TypeArgumentList (NamedType int)) :: Some) (Argument (LiteralExpression 1)))",
        SyntaxPrinter.Print(genericStatic.Node, genericStatic.Lex.Source));
  }

  [Fact]
  public void BareBraceInExpression_EmitsPN2017WithHelp() {
    ParseResult<ExpressionSyntax> result = ParserTestHelpers.ParseExpression("{ 1, 2 }", expectNoDiagnostics: false);
    Diagnostic diag = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticCodes.UnexpectedBlockInExpression, diag.Code);
    Assert.Equal(0, diag.Span.Start);
    Assert.Equal(1, diag.Span.Length);
    Assert.NotNull(diag.Help);
    Assert.Contains("set[T]", diag.Help, StringComparison.Ordinal);
    Assert.IsType<ErrorExpressionSyntax>(result.Node);
  }
}
