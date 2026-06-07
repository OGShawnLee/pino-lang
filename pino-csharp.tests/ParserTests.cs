using Xunit;
using System.Collections.Generic;
using Pino;

namespace pino_csharp.tests;

public class ParserTests {
  [Fact]
  public void TestParseVariableDeclaration() {
    var input = "val name = \"Shawn\"";
    var stmt = Parser.ParseString(input);

    var varDecl = Assert.IsType<VariableDeclaration>(stmt);
    Assert.Equal("name", varDecl.Identifier);
    Assert.Equal(VariableKind.Constant, varDecl.Kind);

    var valExpr = Assert.IsType<LiteralExpression>(varDecl.Value);
    Assert.Equal("Shawn", valExpr.Value);
    Assert.Equal(LiteralType.String, valExpr.LiteralType);
  }

  [Fact]
  public void TestParseFunctionDeclaration() {
    var input = @"fn add(a int, b int) {
      return a + b
    }";
    var stmt = Parser.ParseString(input);

    var fnDecl = Assert.IsType<FunctionDeclaration>(stmt);
    Assert.Equal("add", fnDecl.Identifier);
    Assert.Equal(2, fnDecl.Parameters.Count);
    Assert.Equal("a", fnDecl.Parameters[0].Identifier);
    Assert.Equal("int", fnDecl.Parameters[0].Typing);
    Assert.Equal("b", fnDecl.Parameters[1].Identifier);
    Assert.Equal("int", fnDecl.Parameters[1].Typing);

    var block = Assert.IsType<BlockStatement>(fnDecl.Body);
    Assert.Single(block.Statements);

    var retStmt = Assert.IsType<ReturnStatement>(block.Statements[0]);
    var binExpr = Assert.IsType<BinaryExpression>(retStmt.Argument);
    Assert.Equal(OperatorType.Addition, binExpr.Operator);
    Assert.Equal("a", Assert.IsType<IdentifierExpression>(binExpr.Left).Name);
    Assert.Equal("b", Assert.IsType<IdentifierExpression>(binExpr.Right).Name);
  }

  [Fact]
  public void TestParseStructDeclaration() {
    var input = @"struct Vector2 {
      x int
      y int
      fn magnitude {
        return x
      }
    }";
    var stmt = Parser.ParseString(input);

    var structDecl = Assert.IsType<StructDeclaration>(stmt);
    Assert.Equal("Vector2", structDecl.Identifier);
    Assert.Equal(2, structDecl.Fields.Count);
    Assert.Equal("x", structDecl.Fields[0].Identifier);
    Assert.Equal("y", structDecl.Fields[1].Identifier);

    Assert.Single(structDecl.Methods);
    Assert.Equal("magnitude", structDecl.Methods[0].Identifier);
  }

  [Fact]
  public void TestParseMatchStatement() {
    var input = @"match name {
      when ""Shawn"" { return true }
      else { return false }
    }";
    var stmt = Parser.ParseString(input);

    var matchStmt = Assert.IsType<MatchStatement>(stmt);
    Assert.Equal("name", Assert.IsType<IdentifierExpression>(matchStmt.Condition).Name);
    Assert.Single(matchStmt.Branches);

    var branch = matchStmt.Branches[0];
    Assert.Single(branch.Conditions);
    Assert.Equal("Shawn", Assert.IsType<LiteralExpression>(branch.Conditions[0]).Value);

    var alternate = Assert.IsType<ElseStatement>(matchStmt.Alternate);
    var altBlock = Assert.IsType<BlockStatement>(alternate.Body);
    Assert.IsType<ReturnStatement>(altBlock.Statements[0]);
  }

  [Fact]
  public void TestParseOperatorPrecedence() {
    var input = "1 + 2 * 3";
    var stmt = Parser.ParseString(input);

    var binExpr = Assert.IsType<BinaryExpression>(stmt);
    Assert.Equal(OperatorType.Addition, binExpr.Operator);
    Assert.Equal("1", Assert.IsType<LiteralExpression>(binExpr.Left).Value);

    var rightBin = Assert.IsType<BinaryExpression>(binExpr.Right);
    Assert.Equal(OperatorType.Multiplication, rightBin.Operator);
    Assert.Equal("2", Assert.IsType<LiteralExpression>(rightBin.Left).Value);
    Assert.Equal("3", Assert.IsType<LiteralExpression>(rightBin.Right).Value);
  }

  [Fact]
  public void TestParseStringInterpolationExpression() {
    var input = "\"Base Attack: $(player:attack)\"";
    var stmt = Parser.ParseString(input);

    var binExpr = Assert.IsType<BinaryExpression>(stmt);
    Assert.Equal(OperatorType.Addition, binExpr.Operator);

    var leftLit = Assert.IsType<LiteralExpression>(binExpr.Left);
    Assert.Equal("Base Attack: ", leftLit.Value);
    Assert.Equal(LiteralType.String, leftLit.LiteralType);

    var rightMember = Assert.IsType<BinaryExpression>(binExpr.Right);
    Assert.Equal(OperatorType.MemberAccess, rightMember.Operator);
    Assert.Equal("player", Assert.IsType<IdentifierExpression>(rightMember.Left).Name);
    Assert.Equal("attack", Assert.IsType<IdentifierExpression>(rightMember.Right).Name);
  }

  [Fact]
  public void TestImplicitLambdaArgumentWrapping() {
    var input = "map(it * 3)";
    var stmt = Parser.ParseString(input);

    var call = Assert.IsType<FunctionCallExpression>(stmt);
    Assert.Equal("map", call.Callee);
    Assert.Single(call.Arguments);

    var lambda = Assert.IsType<FunctionLambdaExpression>(call.Arguments[0]);
    Assert.Single(lambda.Parameters);
    Assert.Equal("it", lambda.Parameters[0].Identifier);

    var block = Assert.IsType<BlockStatement>(lambda.Body);
    Assert.Single(block.Statements);
    var ret = Assert.IsType<ReturnStatement>(block.Statements[0]);
    
    var bin = Assert.IsType<BinaryExpression>(ret.Argument);
    Assert.Equal(OperatorType.Multiplication, bin.Operator);
    Assert.Equal("it", Assert.IsType<IdentifierExpression>(bin.Left).Name);
    Assert.Equal("3", Assert.IsType<LiteralExpression>(bin.Right).Value);
  }

  [Fact]
  public void TestNoImplicitLambdaWrappingWhenDeclared() {
    var input = @"fn test(it int) {
      map(it * 3)
    }";
    var stmt = Parser.ParseString(input);
    var fn = Assert.IsType<FunctionDeclaration>(stmt);
    var block = Assert.IsType<BlockStatement>(fn.Body);
    Assert.Single(block.Statements);
    
    var call = Assert.IsType<FunctionCallExpression>(block.Statements[0]);
    Assert.Equal("map", call.Callee);
    Assert.Single(call.Arguments);
    
    var bin = Assert.IsType<BinaryExpression>(call.Arguments[0]);
    Assert.Equal(OperatorType.Multiplication, bin.Operator);
    Assert.Equal("it", Assert.IsType<IdentifierExpression>(bin.Left).Name);
  }

  [Fact]
  public void TestStaticMemberAccessVsStructInstanceAmbiguity() {
    var input = @"if difficulty == Difficulty::Medium {
      
    }";
    var stmt = Parser.ParseString(input);
    var ifStmt = Assert.IsType<IfStatement>(stmt);
    
    var bin = Assert.IsType<BinaryExpression>(ifStmt.Condition);
    Assert.Equal(OperatorType.Equal, bin.Operator);
    
    var rightBin = Assert.IsType<BinaryExpression>(bin.Right);
    Assert.Equal(OperatorType.StaticMemberAccess, rightBin.Operator);
    Assert.Equal("Difficulty", Assert.IsType<IdentifierExpression>(rightBin.Left).Name);
    Assert.Equal("Medium", Assert.IsType<IdentifierExpression>(rightBin.Right).Name);
    
    var body = Assert.IsType<BlockStatement>(ifStmt.Consequent);
    Assert.Empty(body.Statements);
  }
}
