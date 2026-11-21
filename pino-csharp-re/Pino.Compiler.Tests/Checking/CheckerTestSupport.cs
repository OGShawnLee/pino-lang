using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Checking;

/// <summary>Shared helpers for the Phase 3a checker tests (expressions, operators and loops).</summary>
internal static class CheckerTestSupport {
  public static Compilation Compile(string code, string? filePath = "test.pino") {
    ParseResult parsed = Parser.Parse(new SourceText(code, filePath));
    Assert.Empty(parsed.Diagnostics);
    return Compilation.Create(parsed);
  }

  /// <summary>Compiles source that is expected to contain parse errors (A2 half-typed shapes).</summary>
  public static Compilation CompileTolerant(string code, string? filePath = "test.pino") {
    ParseResult parsed = Parser.Parse(new SourceText(code, filePath));
    return Compilation.Create(parsed);
  }

  /// <summary>Checker (<c>PN3xxx</c>) errors only; lexer and parser diagnostics are excluded.</summary>
  public static Diagnostic[] CheckerErrors(Compilation compilation) =>
      [.. compilation.Diagnostics.Where(static d =>
          d.Severity == DiagnosticSeverity.Error && d.Code.StartsWith("PN3", StringComparison.Ordinal))];

  public static IEnumerable<T> Descendants<T>(SyntaxNode root) where T : SyntaxNode {
    if (root is T match) {
      yield return match;
    }

    foreach (SyntaxElement element in root.GetChildren()) {
      if (!element.IsNode) {
        continue;
      }

      foreach (T inner in Descendants<T>(element.AsNode())) {
        yield return inner;
      }
    }
  }

  public static List<T> All<T>(Compilation compilation) where T : SyntaxNode =>
      [.. Descendants<T>(compilation.RootTree.Root)];

  public static string TextOf(Compilation compilation, Token token) =>
      compilation.RootTree.Lex.GetText(token).ToString();

  public static VariableDeclarationSyntax Declaration(Compilation compilation, string name) =>
      All<VariableDeclarationSyntax>(compilation).Single(
          d => TextOf(compilation, d.NameToken) == name);

  public static LocalSymbol Local(Compilation compilation, string name) =>
      Assert.IsType<LocalSymbol>(compilation.RootModel.GetDeclaredSymbol(Declaration(compilation, name)));

  public static TypeSymbol? InitializerType(Compilation compilation, string name) =>
      compilation.RootModel.GetType(Declaration(compilation, name).Initializer);

  public static ConversionInfo? ConversionOf(Compilation compilation, SyntaxNode node) =>
      compilation.RootModel.GetConversion(node);

  public static void AssertIntToFloat(Compilation compilation, SyntaxNode node) {
    ConversionInfo? conversion = ConversionOf(compilation, node);
    Assert.NotNull(conversion);
    Assert.Equal(ConversionKind.IntToFloat, conversion!.Kind);
    Assert.Same(compilation.Types.Float, conversion.TargetType);
  }

  public static void AssertNoConversion(Compilation compilation, SyntaxNode node) =>
      Assert.Null(ConversionOf(compilation, node));
}
