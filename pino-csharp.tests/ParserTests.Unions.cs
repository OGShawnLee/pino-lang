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
}
