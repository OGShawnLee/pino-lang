using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Tests.Parsing;

public sealed class ParserDeclarationTests {
  [Fact]
  public void FunctionStructInterfaceEnumUnionModuleAndTestDeclarations_ParseCleanly() {
    const string Source = """
      module Shapes

      import Math
      from Entities import Program, AST

      pub fn main {
        return
      }

      fn add(a int b int, c int = 1) @(sum int, ok bool) {
        return @(sum: a + b + c, ok: true)
      }

      @@Generic[T is Shape]
      pub interface Repository {
        name string
        fn find(id string) T
        fn save(item T)
      }

      @@Generic[T is Shape]
      pub struct Box {
        Base
        value T
        @@Generic[U]
        static fn make(v U) Box[U] {
          return Box[U] { value: v }
        }
        fn get() T {
          return value
        }
      }

      pub enum Color { Red, Green Blue }

      @@Generic[T]
      pub union Result {
        Ok(T)
        Err(string)
        Empty
      }

      test "smoke" {
        assert true
      }
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source);
    Assert.Equal(10, result.Root.Items.Length);
    Assert.IsType<ModuleDeclarationSyntax>(result.Root.Items[0]);
    Assert.IsType<ImportStatementSyntax>(result.Root.Items[1]);
    Assert.IsType<FromImportStatementSyntax>(result.Root.Items[2]);

    FunctionDeclarationSyntax mainFn = Assert.IsType<FunctionDeclarationSyntax>(result.Root.Items[3]);
    Assert.Null(mainFn.OpenParenToken);
    Assert.NotNull(mainFn.Visibility);

    FunctionDeclarationSyntax addFn = Assert.IsType<FunctionDeclarationSyntax>(result.Root.Items[4]);
    Assert.Equal(3, addFn.Parameters.Length);
    Assert.IsType<TupleTypeSyntax>(addFn.ReturnType);

    InterfaceDeclarationSyntax iface = Assert.IsType<InterfaceDeclarationSyntax>(result.Root.Items[5]);
    Assert.NotNull(iface.GenericDecorator);
    Assert.Equal(3, iface.Members.Length);
    Assert.IsType<InterfacePropertySyntax>(iface.Members[0]);
    InterfaceMethodSyntax findSig = Assert.IsType<InterfaceMethodSyntax>(iface.Members[1]);
    Assert.NotNull(findSig.ReturnType);
    InterfaceMethodSyntax saveSig = Assert.IsType<InterfaceMethodSyntax>(iface.Members[2]);
    Assert.Null(saveSig.ReturnType);

    StructDeclarationSyntax box = Assert.IsType<StructDeclarationSyntax>(result.Root.Items[6]);
    Assert.NotNull(box.GenericDecorator);
    Assert.Equal(4, box.Members.Length);
    Assert.IsType<EmbeddedStructSyntax>(box.Members[0]);
    Assert.IsType<FieldDeclarationSyntax>(box.Members[1]);
    MethodDeclarationSyntax staticMake = Assert.IsType<MethodDeclarationSyntax>(box.Members[2]);
    Assert.NotNull(staticMake.GenericDecorator);
    Assert.NotNull(staticMake.StaticKeyword);
    Assert.IsType<MethodDeclarationSyntax>(box.Members[3]);

    EnumDeclarationSyntax color = Assert.IsType<EnumDeclarationSyntax>(result.Root.Items[7]);
    Assert.Equal(3, color.Members.Length);

    UnionDeclarationSyntax res = Assert.IsType<UnionDeclarationSyntax>(result.Root.Items[8]);
    Assert.NotNull(res.GenericDecorator);
    Assert.Equal(3, res.Variants.Length);

    Assert.IsType<TestDeclarationSyntax>(result.Root.Items[9]);
  }

  [Fact]
  public void UnionPayloads_PairPositionalVsNamedVariantD146AndMixedPN2015() {
    const string Positional = """
      union Shape {
        Pair(int, string)
        None
      }
      """;
    ParseResult posRes = ParserTestHelpers.ParseFile(Positional);
    UnionDeclarationSyntax posUnion = Assert.IsType<UnionDeclarationSyntax>(Assert.Single(posRes.Root.Items));
    UnionVariantSyntax pair = Assert.IsType<UnionVariantSyntax>(posUnion.Variants[0]);
    Assert.Equal(2, pair.Fields.Length);
    Assert.Null(pair.Fields[0].NameToken);
    Assert.Null(pair.Fields[1].NameToken);

    const string Named = """
      union U {
        Variant(a B)
        Two(x int y string)
      }
      """;
    ParseResult namedRes = ParserTestHelpers.ParseFile(Named);
    UnionDeclarationSyntax namedUnion = Assert.IsType<UnionDeclarationSyntax>(Assert.Single(namedRes.Root.Items));
    UnionVariantSyntax variant = Assert.IsType<UnionVariantSyntax>(namedUnion.Variants[0]);
    UnionVariantFieldSyntax singleField = Assert.Single(variant.Fields);
    Assert.Equal("a", namedRes.Lex.GetText(singleField.NameToken!.Value).ToString());
    Assert.Equal("B", namedRes.Lex.GetText(Assert.IsType<NamedTypeSyntax>(singleField.Type).NameToken).ToString());
    Assert.Equal(2, Assert.IsType<UnionVariantSyntax>(namedUnion.Variants[1]).Fields.Length);

    const string Mixed = """
      union Bad {
        Int(n int)
        Str(string)
      }
      """;
    ParseResult mixedRes = ParserTestHelpers.ParseFile(Mixed, expectNoDiagnostics: false);
    Diagnostic pn2015 = Assert.Single(mixedRes.Diagnostics);
    Assert.Equal(DiagnosticCodes.MixedUnionPayloadForms, pn2015.Code);
  }

  [Fact]
  public void EmptyBodies_EmitPN2016() {
    ParseResult emptyEnum = ParserTestHelpers.ParseFile("enum E {}", expectNoDiagnostics: false);
    Assert.Equal(DiagnosticCodes.EmptyDeclarationBody, Assert.Single(emptyEnum.Diagnostics).Code);

    ParseResult emptyUnion = ParserTestHelpers.ParseFile("union U {}", expectNoDiagnostics: false);
    Assert.Equal(DiagnosticCodes.EmptyDeclarationBody, Assert.Single(emptyUnion.Diagnostics).Code);

    ParseResult emptyGeneric = ParserTestHelpers.ParseFile("@@Generic[]\nfn f() {}", expectNoDiagnostics: false);
    Assert.Equal(DiagnosticCodes.EmptyDeclarationBody, Assert.Single(emptyGeneric.Diagnostics).Code);
  }

  [Fact]
  public void DeprecatedGenericForms_EmitPN2014AndRecoverParameters() {
    ParseResult oldAt = ParserTestHelpers.ParseFile("@generic[T is Shape]\nfn f(x T) {}", expectNoDiagnostics: false);
    Diagnostic d1 = Assert.Single(oldAt.Diagnostics);
    Assert.Equal(DiagnosticCodes.DeprecatedGenericSyntax, d1.Code);
    Assert.Contains("@@Generic", d1.Help, StringComparison.Ordinal);
    FunctionDeclarationSyntax fn = Assert.IsType<FunctionDeclarationSyntax>(Assert.Single(oldAt.Root.Items));
    Assert.NotNull(fn.GenericDecorator);
    GenericParameterSyntax gp1 = Assert.Single(fn.GenericDecorator.Parameters);
    Assert.Equal("T", oldAt.Lex.GetText(gp1.NameToken).ToString());
    Assert.NotNull(gp1.ConstraintType);

    ParseResult inlineStruct = ParserTestHelpers.ParseFile("struct Pair[A, B] { a A }", expectNoDiagnostics: false);
    Diagnostic d2 = Assert.Single(inlineStruct.Diagnostics);
    Assert.Equal(DiagnosticCodes.DeprecatedGenericSyntax, d2.Code);
    Assert.Contains("@@Generic", d2.Help, StringComparison.Ordinal);
    StructDeclarationSyntax st = Assert.IsType<StructDeclarationSyntax>(Assert.Single(inlineStruct.Root.Items));
    Assert.NotNull(st.GenericDecorator);
    Assert.Equal(2, st.GenericDecorator.Parameters.Length);

    ParseResult inlineUnion = ParserTestHelpers.ParseFile("union Opt[T] { Some(T) None }", expectNoDiagnostics: false);
    Diagnostic d3 = Assert.Single(inlineUnion.Diagnostics);
    Assert.Equal(DiagnosticCodes.DeprecatedGenericSyntax, d3.Code);
    UnionDeclarationSyntax un = Assert.IsType<UnionDeclarationSyntax>(Assert.Single(inlineUnion.Root.Items));
    Assert.NotNull(un.GenericDecorator);
    Assert.Single(un.GenericDecorator.Parameters);
  }

  [Fact]
  public void ArrowBodyOnFunctionOrMethod_EmitsPN2019AndWrapsExpression() {
    ParseResult fnArrow = ParserTestHelpers.ParseFile("fn double(n int) int -> n * 2", expectNoDiagnostics: false);
    Diagnostic d1 = Assert.Single(fnArrow.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedBlockBody, d1.Code);
    FunctionDeclarationSyntax fn = Assert.IsType<FunctionDeclarationSyntax>(Assert.Single(fnArrow.Root.Items));
    Assert.NotNull(fn.ArrowToken);
    Assert.IsType<ExpressionStatementSyntax>(Assert.Single(fn.Body.Items));

    ParseResult methodArrow = ParserTestHelpers.ParseFile(
        "struct Dog {\n  fn sound() string -> \"Woof\"\n}",
        expectNoDiagnostics: false);
    Diagnostic d2 = Assert.Single(methodArrow.Diagnostics);
    Assert.Equal(DiagnosticCodes.ExpectedBlockBody, d2.Code);
    StructDeclarationSyntax dog = Assert.IsType<StructDeclarationSyntax>(Assert.Single(methodArrow.Root.Items));
    MethodDeclarationSyntax method = Assert.IsType<MethodDeclarationSyntax>(Assert.Single(dog.Members));
    Assert.NotNull(method.ArrowToken);
    Assert.IsType<ExpressionStatementSyntax>(Assert.Single(method.Body.Items));
  }

  [Fact]
  public void GenericDecorator_AllowedTargetsWithBounds() {
    const string Source = """
      @@Generic[T is Shape]
      struct S {
        x T
        @@Generic[U is Shape]
        fn m(u U) {}
      }

      @@Generic[T is Shape]
      union U {
        One(T)
      }

      @@Generic[T is Shape]
      interface I {
        fn get() T
      }

      @@Generic[T is Shape]
      fn f(x T) {}
      """;

    ParseResult result = ParserTestHelpers.ParseFile(Source);
    Assert.Equal(4, result.Root.Items.Length);
    Assert.NotNull(Assert.IsType<StructDeclarationSyntax>(result.Root.Items[0]).GenericDecorator);
    Assert.NotNull(Assert.IsType<UnionDeclarationSyntax>(result.Root.Items[1]).GenericDecorator);
    Assert.NotNull(Assert.IsType<InterfaceDeclarationSyntax>(result.Root.Items[2]).GenericDecorator);
    Assert.NotNull(Assert.IsType<FunctionDeclarationSyntax>(result.Root.Items[3]).GenericDecorator);
  }

  [Fact]
  public void FromImport_NewlineWithoutCommaEndsImportList() {
    const string SingleLine = """
      from M import a, b
      foo()
      """;

    ParseResult r1 = ParserTestHelpers.ParseFile(SingleLine);
    Assert.Equal(2, r1.Root.Items.Length);
    FromImportStatementSyntax imp1 = Assert.IsType<FromImportStatementSyntax>(r1.Root.Items[0]);
    Assert.Equal(2, imp1.Names.Length);
    Assert.IsType<ExpressionStatementSyntax>(r1.Root.Items[1]);

    const string MultilineWithTrailingCommaContinuation = """
      from Entities import
        Program,
        AST
      val x = 1
      """;

    ParseResult r2 = ParserTestHelpers.ParseFile(MultilineWithTrailingCommaContinuation);
    Assert.Equal(2, r2.Root.Items.Length);
    FromImportStatementSyntax imp2 = Assert.IsType<FromImportStatementSyntax>(r2.Root.Items[0]);
    Assert.Equal(2, imp2.Names.Length);
    Assert.IsType<VariableDeclarationSyntax>(r2.Root.Items[1]);
  }

  [Fact]
  public void PubAndPubTestingCombined_EmitsParseErrorAndKeepsTokenAccounting() {
    ParseResult r1 = ParserTestHelpers.ParseFile("pub @pub_testing fn foo() {}", expectNoDiagnostics: false);
    Assert.Single(r1.Diagnostics);
    Assert.Contains(r1.Root.Items, static n => n is FunctionDeclarationSyntax);

    ParseResult r2 = ParserTestHelpers.ParseFile("@pub_testing pub fn foo() {}", expectNoDiagnostics: false);
    Assert.Single(r2.Diagnostics);
    Assert.Contains(r2.Root.Items, static n => n is FunctionDeclarationSyntax);
  }
}
