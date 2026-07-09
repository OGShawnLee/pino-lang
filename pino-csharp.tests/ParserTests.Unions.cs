using Xunit;
using System;
using System.Collections.Generic;
using Pino;

namespace pino_csharp.tests;

public class ParserUnionsTests {
  [Fact]
  public void TestParseUnionDeclaration() {
    var input = @"
      union Entity {
        Person(string)
        Animal(string, string)
        Ghost
      }
    ";
    var program = Parser.ParseProgramString(input, injectPrelude: false);
    Assert.Single(program.Statements);

    var unionDecl = Assert.IsType<UnionDeclaration>(program.Statements[0]);
    Assert.Equal("Entity", unionDecl.Identifier);
    Assert.Equal(3, unionDecl.Variants.Count);

    var v0 = unionDecl.Variants[0];
    Assert.Equal("Person", v0.Identifier);
    Assert.Single(v0.AssociatedTypes);
    Assert.Equal("string", v0.AssociatedTypes[0]);

    var v1 = unionDecl.Variants[1];
    Assert.Equal("Animal", v1.Identifier);
    Assert.Equal(2, v1.AssociatedTypes.Count);
    Assert.Equal("string", v1.AssociatedTypes[0]);
    Assert.Equal("string", v1.AssociatedTypes[1]);

    var v2 = unionDecl.Variants[2];
    Assert.Equal("Ghost", v2.Identifier);
    Assert.Empty(v2.AssociatedTypes);
  }

  [Fact]
  public void TestParseWhenPattern() {
    var input = @"
      match x {
        when Entity::Person(name) {
          println(name)
        }
        else {
          println(""fallback"")
        }
      }
    ";
    var program = Parser.ParseProgramString(input, injectPrelude: false);
    Assert.Single(program.Statements);

    var matchStmt = Assert.IsType<MatchStatement>(program.Statements[0]);
    var condExpr = Assert.IsType<IdentifierExpression>(matchStmt.Condition);
    Assert.Equal("x", condExpr.Name);

    Assert.Single(matchStmt.Branches);
    var whenBranch = matchStmt.Branches[0];

    Assert.Single(whenBranch.Conditions);
    var pat = Assert.IsType<VariantPattern>(whenBranch.Conditions[0]);
    Assert.Equal("Entity", pat.UnionName);
    Assert.Equal("Person", pat.VariantName);
    Assert.Single(pat.SubPatterns);

    var subPat = Assert.IsType<IdentifierPattern>(pat.SubPatterns[0]);
    Assert.Equal("name", subPat.Name);
  }

  [Fact]
  public void TestParseUnionIsExpression() {
    var input = @"
      val check = x is Option::Some
      val not_check = x is not Option::Some
      val bind_check = x is Option::Some(value)
      val gen_check = state is RemoteData[int]::Success
    ";
    var program = Parser.ParseProgramString(input, injectPrelude: false);
    Assert.Equal(4, program.Statements.Count);

    // 1. check = x is Option::Some
    var v0 = Assert.IsType<VariableDeclaration>(program.Statements[0]);
    var isExpr0 = Assert.IsType<IsExpression>(v0.Value);
    Assert.False(isExpr0.IsNot);
    var pat0 = Assert.IsType<VariantPattern>(isExpr0.Pattern);
    Assert.Equal("Option", pat0.UnionName);
    Assert.Equal("Some", pat0.VariantName);
    Assert.Empty(pat0.SubPatterns);

    // 2. not_check = x is not Option::Some
    var v1 = Assert.IsType<VariableDeclaration>(program.Statements[1]);
    var isExpr1 = Assert.IsType<IsExpression>(v1.Value);
    Assert.True(isExpr1.IsNot);

    // 3. bind_check = x is Option::Some(value)
    var v2 = Assert.IsType<VariableDeclaration>(program.Statements[2]);
    var isExpr2 = Assert.IsType<IsExpression>(v2.Value);
    var pat2 = Assert.IsType<VariantPattern>(isExpr2.Pattern);
    Assert.Single(pat2.SubPatterns);
    var subPat = Assert.IsType<IdentifierPattern>(pat2.SubPatterns[0]);
    Assert.Equal("value", subPat.Name);

    // 4. gen_check = state is RemoteData[int]::Success
    var v3 = Assert.IsType<VariableDeclaration>(program.Statements[3]);
    var isExpr3 = Assert.IsType<IsExpression>(v3.Value);
    var pat3 = Assert.IsType<VariantPattern>(isExpr3.Pattern);
    Assert.Equal("RemoteData[int]", pat3.UnionName);
    Assert.Equal("Success", pat3.VariantName);
  }
}
