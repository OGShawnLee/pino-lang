using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Tests.Parsing;

public sealed class ParserStatementTests {
  [Fact]
  public void VariableAndTupleDestructuringStatements_ParseCleanly() {
    const string Source = """
      {
        val x = 1
        var y = x + 2
        val @(a, b) = pair()
        var @(first: f, second) = pair()
        val @(p q) = pair()
      }
      """;

    ParseResult<BlockStatementSyntax> result = ParserTestHelpers.ParseBlock(Source);
    Assert.Equal(5, result.Node.Items.Length);

    VariableDeclarationSyntax valDecl = Assert.IsType<VariableDeclarationSyntax>(result.Node.Items[0]);
    Assert.Equal("x", result.Lex.GetText(valDecl.NameToken).ToString());
    Assert.Equal(
        "(VariableDeclaration val x = (LiteralExpression 1))",
        SyntaxPrinter.Print(valDecl, result.Lex.Source));

    VariableDeclarationSyntax varDecl = Assert.IsType<VariableDeclarationSyntax>(result.Node.Items[1]);
    Assert.Equal(
        "(VariableDeclaration var y = (BinaryExpression (IdentifierExpression x) + (LiteralExpression 2)))",
        SyntaxPrinter.Print(varDecl, result.Lex.Source));

    TupleDestructuringStatementSyntax d1 = Assert.IsType<TupleDestructuringStatementSyntax>(result.Node.Items[2]);
    Assert.Equal(2, d1.Fields.Length);
    Assert.Null(d1.Fields[0].ColonToken);

    TupleDestructuringStatementSyntax d2 = Assert.IsType<TupleDestructuringStatementSyntax>(result.Node.Items[3]);
    Assert.Equal("first", result.Lex.GetText(d2.Fields[0].LabelToken).ToString());
    Assert.Equal("f", result.Lex.GetText(d2.Fields[0].VariableToken!.Value).ToString());
    Assert.Null(d2.Fields[1].VariableToken);

    TupleDestructuringStatementSyntax d3 = Assert.IsType<TupleDestructuringStatementSyntax>(result.Node.Items[4]);
    Assert.Equal(2, d3.Fields.Length);
  }

  [Fact]
  public void ExpressionYieldAssertReturnBreakContinueAndImportStatements_ParseCleanly() {
    const string Source = """
      {
        import Math
        from Entities import Program, AST
        x = x + 1
        assert x > 0
        assert(x == 1)
        break
        continue
        yield x
        return x
      }
      """;

    ParseResult<BlockStatementSyntax> result = ParserTestHelpers.ParseBlock(Source);
    Assert.Equal(9, result.Node.Items.Length);
    Assert.IsType<ImportStatementSyntax>(result.Node.Items[0]);
    Assert.IsType<FromImportStatementSyntax>(result.Node.Items[1]);
    Assert.IsType<ExpressionStatementSyntax>(result.Node.Items[2]);
    Assert.IsType<AssertStatementSyntax>(result.Node.Items[3]);
    Assert.IsType<AssertStatementSyntax>(result.Node.Items[4]);
    Assert.IsType<BreakStatementSyntax>(result.Node.Items[5]);
    Assert.IsType<ContinueStatementSyntax>(result.Node.Items[6]);
    Assert.IsType<YieldStatementSyntax>(result.Node.Items[7]);
    Assert.IsType<ReturnStatementSyntax>(result.Node.Items[8]);
  }

  [Fact]
  public void IfStatementVsIfExpressionAtStatementStart() {
    const string Source = """
      {
        if a {
          x = 1
        } else if b {
          x = 2
        } else {
          x = 3
        }
        if cond then 10 else 20
        if p == (Point { x: 1 }) {
          y = 1
        }
      }
      """;

    ParseResult<BlockStatementSyntax> result = ParserTestHelpers.ParseBlock(Source);
    Assert.Equal(3, result.Node.Items.Length);

    IfStatementSyntax ifStmt = Assert.IsType<IfStatementSyntax>(result.Node.Items[0]);
    IfStatementSyntax elseIfStmt = Assert.IsType<IfStatementSyntax>(ifStmt.ElseBranch);
    Assert.IsType<BlockStatementSyntax>(elseIfStmt.ElseBranch);

    ExpressionStatementSyntax exprStmt = Assert.IsType<ExpressionStatementSyntax>(result.Node.Items[1]);
    Assert.IsType<IfExpressionSyntax>(exprStmt.Expression);

    IfStatementSyntax parenStructCond = Assert.IsType<IfStatementSyntax>(result.Node.Items[2]);
    Assert.IsType<BinaryExpressionSyntax>(parenStructCond.Condition);
  }

  [Fact]
  public void IfStatementErrors_EmitPN2011AndPN2012() {
    ParseResult<BlockStatementSyntax> unbracedThen = ParserTestHelpers.ParseBlock(
        "{\n  if x > 1 println(\"a\")\n}",
        expectNoDiagnostics: false);
    Diagnostic pn2011 = Assert.Single(unbracedThen.Diagnostics);
    Assert.Equal(DiagnosticCodes.UnbracedIfBranch, pn2011.Code);
    Assert.Contains("Expected '{'", pn2011.Message, StringComparison.Ordinal);
    IfStatementSyntax if1 = Assert.IsType<IfStatementSyntax>(Assert.Single(unbracedThen.Node.Items));
    Assert.Single(if1.ThenBranch.Items);

    ParseResult<BlockStatementSyntax> unbracedElse = ParserTestHelpers.ParseBlock(
        "{\n  if x { a() } else b()\n}",
        expectNoDiagnostics: false);
    Diagnostic pn2011Else = Assert.Single(unbracedElse.Diagnostics);
    Assert.Equal(DiagnosticCodes.UnbracedIfBranch, pn2011Else.Code);
    Assert.Contains("Expected '{'", pn2011Else.Message, StringComparison.Ordinal);

    ParseResult<BlockStatementSyntax> arrowElse = ParserTestHelpers.ParseBlock(
        "{\n  if x { a() } else -> b()\n}",
        expectNoDiagnostics: false);
    Diagnostic pn2012 = Assert.Single(arrowElse.Diagnostics);
    Assert.Equal(DiagnosticCodes.ElseArrowAfterIf, pn2012.Code);
    IfStatementSyntax if2 = Assert.IsType<IfStatementSyntax>(Assert.Single(arrowElse.Node.Items));
    Assert.NotNull(if2.ElseArrowToken);
    Assert.IsType<ExpressionStatementSyntax>(if2.ElseBranch);

    ParseResult<BlockStatementSyntax> fatArrowElse = ParserTestHelpers.ParseBlock(
        "{\n  if x { a() } else => b()\n}",
        expectNoDiagnostics: false);
    Diagnostic pn2012Fat = Assert.Single(fatArrowElse.Diagnostics);
    Assert.Equal(DiagnosticCodes.ElseArrowAfterIf, pn2012Fat.Code);
  }

  [Fact]
  public void ForStatementHeads_AllValidFormsAndTrailingCommaError() {
    const string Source = """
      {
        for { break }
        for 10 { println(it) }
        for x < 10 { x += 1 }
        for x in xs { println(x) }
        for k, v in m { println(k) }
        for k v in m { println(k) }
      }
      """;

    ParseResult<BlockStatementSyntax> result = ParserTestHelpers.ParseBlock(Source);
    Assert.Equal(6, result.Node.Items.Length);

    ForStatementSyntax f0 = Assert.IsType<ForStatementSyntax>(result.Node.Items[0]);
    Assert.Null(f0.Header);

    ForStatementSyntax f1 = Assert.IsType<ForStatementSyntax>(result.Node.Items[1]);
    Assert.IsType<ForConditionHeaderSyntax>(f1.Header);

    ForStatementSyntax f2 = Assert.IsType<ForStatementSyntax>(result.Node.Items[2]);
    Assert.IsType<ForConditionHeaderSyntax>(f2.Header);

    ForStatementSyntax f3 = Assert.IsType<ForStatementSyntax>(result.Node.Items[3]);
    ForInHeaderSyntax h3 = Assert.IsType<ForInHeaderSyntax>(f3.Header);
    Assert.Null(h3.CommaToken);
    Assert.Null(h3.SecondBindingToken);

    ForStatementSyntax f4 = Assert.IsType<ForStatementSyntax>(result.Node.Items[4]);
    ForInHeaderSyntax h4 = Assert.IsType<ForInHeaderSyntax>(f4.Header);
    Assert.NotNull(h4.CommaToken);
    Assert.NotNull(h4.SecondBindingToken);

    ForStatementSyntax f5 = Assert.IsType<ForStatementSyntax>(result.Node.Items[5]);
    ForInHeaderSyntax h5 = Assert.IsType<ForInHeaderSyntax>(f5.Header);
    Assert.Null(h5.CommaToken);
    Assert.NotNull(h5.SecondBindingToken);

    ParseResult<BlockStatementSyntax> trailingComma = ParserTestHelpers.ParseBlock(
        "{\n  for k, in m { println(k) }\n}",
        expectNoDiagnostics: false);
    Diagnostic pn2007 = Assert.Single(trailingComma.Diagnostics);
    Assert.Equal(DiagnosticCodes.TrailingComma, pn2007.Code);
    ForStatementSyntax fErr = Assert.IsType<ForStatementSyntax>(Assert.Single(trailingComma.Node.Items));
    ForInHeaderSyntax hErr = Assert.IsType<ForInHeaderSyntax>(fErr.Header);
    Assert.Equal("k", trailingComma.Lex.GetText(hErr.FirstBindingToken).ToString());
    Assert.NotNull(hErr.CommaToken);
    Assert.Null(hErr.SecondBindingToken);
  }

  [Fact]
  public void MatchStatements_BlockArmsPassAndArrowArmsEmitMatchE7() {
    const string Valid = """
      {
        match subj {
          when 1, 2 {
            println("a")
          }
          when Opt::Some(v) {
            println(v)
          }
          else {
            println("other")
          }
        }
      }
      """;

    ParseResult<BlockStatementSyntax> ok = ParserTestHelpers.ParseBlock(Valid);
    MatchStatementSyntax matchStmt = Assert.IsType<MatchStatementSyntax>(Assert.Single(ok.Node.Items));
    Assert.Equal(3, matchStmt.Arms.Length);

    const string InvalidArrows = """
      {
        match subj {
          when 1 -> println("a")
          else -> println("b")
        }
      }
      """;

    ParseResult<BlockStatementSyntax> err = ParserTestHelpers.ParseBlock(InvalidArrows, expectNoDiagnostics: false);
    Assert.Equal(2, err.Diagnostics.Length);
    Assert.All(err.Diagnostics, static d => {
      Assert.Equal(DiagnosticCodes.ExpectedToken, d.Code);
      Assert.Contains("Expected '{'", d.Message, StringComparison.Ordinal);
      Assert.Contains("match expressions", d.Message, StringComparison.Ordinal);
    });

    MatchStatementSyntax recovered = Assert.IsType<MatchStatementSyntax>(Assert.Single(err.Node.Items));
    WhenArmSyntax whenArm = Assert.IsType<WhenArmSyntax>(recovered.Arms[0]);
    Assert.NotNull(whenArm.ExpressionBody);
    ElseArmSyntax elseArm = Assert.IsType<ElseArmSyntax>(recovered.Arms[1]);
    Assert.NotNull(elseArm.ExpressionBody);
  }

  [Fact]
  public void OpenDecisionForms_ParseWithZeroDiagnostics() {
    const string Source = """
      pub val top = 1

      @pub_testing
      fn helper(name = "World", x void) void {
        {
          val inner = name
        }
      }

      pub struct Box {
        pub value int
        @pub_testing secret string
        pub fn get() int {
          return value
        }
        @pub_testing fn debug_val() void {}
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source);
    Assert.Equal(3, result.Root.Items.Length);

    VariableDeclarationSyntax topVal = Assert.IsType<VariableDeclarationSyntax>(result.Root.Items[0]);
    Assert.NotNull(topVal.Visibility);

    FunctionDeclarationSyntax fn = Assert.IsType<FunctionDeclarationSyntax>(result.Root.Items[1]);
    Assert.NotNull(fn.Visibility);
    Assert.Null(fn.Parameters[0].Type);
    Assert.NotNull(fn.Parameters[0].DefaultValue);
    NamedTypeSyntax voidParam = Assert.IsType<NamedTypeSyntax>(fn.Parameters[1].Type);
    Assert.Equal("void", result.Lex.GetText(voidParam.NameToken).ToString());
    NamedTypeSyntax voidReturn = Assert.IsType<NamedTypeSyntax>(fn.ReturnType);
    Assert.Equal("void", result.Lex.GetText(voidReturn.NameToken).ToString());
    Assert.IsType<BlockStatementSyntax>(Assert.Single(fn.Body.Items));

    StructDeclarationSyntax s = Assert.IsType<StructDeclarationSyntax>(result.Root.Items[2]);
    Assert.NotNull(s.Visibility);
    Assert.Equal(4, s.Members.Length);
    Assert.NotNull(Assert.IsType<FieldDeclarationSyntax>(s.Members[0]).Visibility);
    Assert.NotNull(Assert.IsType<FieldDeclarationSyntax>(s.Members[1]).Visibility);
    Assert.NotNull(Assert.IsType<MethodDeclarationSyntax>(s.Members[2]).Visibility);
    Assert.NotNull(Assert.IsType<MethodDeclarationSyntax>(s.Members[3]).Visibility);
  }
}
