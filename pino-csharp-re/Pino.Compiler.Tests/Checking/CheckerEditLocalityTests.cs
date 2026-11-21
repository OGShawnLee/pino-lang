using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Checking;

/// <summary>
/// Edit-locality checks (design §10 item 5): a localized edit or syntax/type error in declaration K
/// must leave independent tail declarations identically typed with zero spurious diagnostics.
/// </summary>
public sealed class CheckerEditLocalityTests {
  [Fact]
  public void EditOrErrorInDeclarationK_LeavesIndependentTailDeclarationsUnchanged() {
    const string Baseline = """
      struct Point {
        pub x int
        pub y int
      }

      fn prefix_ok(n int) int {
        return n + 1
      }

      fn edited_region(flag bool) int {
        if flag {
          return 10
        }
        return 20
      }

      fn tail_one(p Point) int {
        val sum = p:x + p:y
        return match sum {
          when 0 { yield 0 }
          else { yield sum * 2 }
        }
      }

      fn tail_two(o Option[int]) int {
        if o is Option::Some(v) {
          return v + 1
        }
        return 0
      }
      """;

    const string EditedBrokenSyntax = """
      struct Point {
        pub x int
        pub y int
      }

      fn prefix_ok(n int) int {
        return n + 1
      }

      fn edited_region(flag bool) int {
        if flag {
          val broken = unknown_sym +
          return "wrong_type"
        }
      }

      fn tail_one(p Point) int {
        val sum = p:x + p:y
        return match sum {
          when 0 { yield 0 }
          else { yield sum * 2 }
        }
      }

      fn tail_two(o Option[int]) int {
        if o is Option::Some(v) {
          return v + 1
        }
        return 0
      }
      """;

    Compilation clean = Compilation.Create(Parser.Parse(new SourceText(Baseline, "locality.pino")));
    Assert.Empty(clean.Diagnostics);

    Compilation edited = Compilation.Create(Parser.Parse(new SourceText(EditedBrokenSyntax, "locality.pino")));
    Assert.NotEmpty(edited.Diagnostics);

    int tailOffsetClean = Baseline.IndexOf("fn tail_one", StringComparison.Ordinal);
    int tailOffsetEdited = EditedBrokenSyntax.IndexOf("fn tail_one", StringComparison.Ordinal);
    Assert.Equal(0, tailOffsetClean < 0 ? -1 : 0);
    Assert.Equal(0, tailOffsetEdited < 0 ? -1 : 0);

    Diagnostic[] tailErrors = edited.Diagnostics
        .Where(d => d.Span.Start >= tailOffsetEdited)
        .ToArray();
    Assert.Empty(tailErrors);

    FunctionDeclarationSyntax cleanTail1 = GetFunction(clean, "tail_one");
    FunctionDeclarationSyntax editedTail1 = GetFunction(edited, "tail_one");
    FunctionDeclarationSyntax cleanTail2 = GetFunction(clean, "tail_two");
    FunctionDeclarationSyntax editedTail2 = GetFunction(edited, "tail_two");

    AssertSameSubtreeTypes(clean.RootModel, cleanTail1, edited.RootModel, editedTail1);
    AssertSameSubtreeTypes(clean.RootModel, cleanTail2, edited.RootModel, editedTail2);
  }

  [Fact]
  public void BrokenMutDeclarationInMiddle_LeavesIndependentMutTailIdentical() {
    const string Baseline = """
      struct Counter {
        mut n int
        mut fn inc() { n += 1 }
      }

      fn edited_mut(mut c Counter) {
        c:inc()
      }

      fn tail_mut(mut c Counter) int {
        c:inc()
        return c:n
      }
      """;

    const string Broken = """
      struct Counter {
        mut n int
        mut fn inc() { n += 1 }
      }

      fn edited_mut(mut c Counter) {
        val f = fn () { c:inc() }
        c:missing_field =
      }

      fn tail_mut(mut c Counter) int {
        c:inc()
        return c:n
      }
      """;

    Compilation clean = Compilation.Create(Parser.Parse(new SourceText(Baseline, "mut_locality.pino")));
    Assert.Empty(clean.Diagnostics);

    Compilation edited = Compilation.Create(Parser.Parse(new SourceText(Broken, "mut_locality.pino")));
    Assert.NotEmpty(edited.Diagnostics);

    int tailOffset = Broken.IndexOf("fn tail_mut", StringComparison.Ordinal);
    Assert.Empty(edited.Diagnostics.Where(d => d.Span.Start >= tailOffset));

    FunctionDeclarationSyntax cleanTail = GetFunction(clean, "tail_mut");
    FunctionDeclarationSyntax editedTail = GetFunction(edited, "tail_mut");
    AssertSameSubtreeTypes(clean.RootModel, cleanTail, edited.RootModel, editedTail);

    CallExpressionSyntax cleanCall = CheckerTestSupport.Descendants<CallExpressionSyntax>(cleanTail).Single();
    CallExpressionSyntax editedCall = CheckerTestSupport.Descendants<CallExpressionSyntax>(editedTail).Single();
    Assert.Equal(
        clean.RootModel.GetMutationPlace(cleanCall)?.RootKind,
        edited.RootModel.GetMutationPlace(editedCall)?.RootKind);
  }

  private static FunctionDeclarationSyntax GetFunction(Compilation compilation, string name) =>
      CheckerTestSupport.Descendants<FunctionDeclarationSyntax>(compilation.RootTree.Root)
          .Single(fn => CheckerTestSupport.TextOf(compilation, fn.NameToken) == name);

  private static void AssertSameSubtreeTypes(
      SemanticModel expectedModel,
      SyntaxNode expectedNode,
      SemanticModel actualModel,
      SyntaxNode actualNode) {
    Assert.Equal(expectedNode.GetType(), actualNode.GetType());
    if (expectedNode is ExpressionSyntax expectedExpr && actualNode is ExpressionSyntax actualExpr) {
      TypeSymbol? expectedType = expectedModel.GetType(expectedExpr);
      TypeSymbol? actualType = actualModel.GetType(actualExpr);
      Assert.NotNull(expectedType);
      Assert.NotNull(actualType);
      Assert.Equal(expectedType!.DisplayName, actualType!.DisplayName);
    }

    SyntaxNode[] expectedChildren = expectedNode.GetChildren()
        .Where(static c => c.IsNode)
        .Select(static c => c.AsNode())
        .ToArray();
    SyntaxNode[] actualChildren = actualNode.GetChildren()
        .Where(static c => c.IsNode)
        .Select(static c => c.AsNode())
        .ToArray();
    Assert.Equal(expectedChildren.Length, actualChildren.Length);

    for (int i = 0; i < expectedChildren.Length; i++) {
      AssertSameSubtreeTypes(expectedModel, expectedChildren[i], actualModel, actualChildren[i]);
    }
  }
}
