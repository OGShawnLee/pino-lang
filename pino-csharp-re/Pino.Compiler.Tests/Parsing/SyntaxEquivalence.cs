using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Parsing;

/// <summary>Structural equality helper ignoring node IDs, source spans, and non-missing trivia flags.</summary>
internal static class SyntaxEquivalence {
  public static bool AreEquivalent(
      SyntaxNode a,
      SourceText sourceA,
      SyntaxNode b,
      SourceText sourceB,
      out string? difference) {
    if (a.GetType() != b.GetType()) {
      difference = $"Node type mismatch: {a.GetType().Name} vs {b.GetType().Name}.";
      return false;
    }

    using IEnumerator<SyntaxElement> enumA = a.GetChildren().GetEnumerator();
    using IEnumerator<SyntaxElement> enumB = b.GetChildren().GetEnumerator();

    int childIndex = 0;
    while (true) {
      bool hasA = enumA.MoveNext();
      bool hasB = enumB.MoveNext();
      if (hasA != hasB) {
        difference = $"Child count mismatch in {a.GetType().Name} at child #{childIndex}.";
        return false;
      }

      if (!hasA) {
        difference = null;
        return true;
      }

      SyntaxElement childA = enumA.Current;
      SyntaxElement childB = enumB.Current;
      if (childA.IsNode != childB.IsNode) {
        difference = $"Child kind mismatch (node vs token) in {a.GetType().Name} at child #{childIndex}.";
        return false;
      }

      if (childA.IsNode) {
        if (!AreEquivalent(childA.AsNode(), sourceA, childB.AsNode(), sourceB, out difference)) {
          return false;
        }
      } else {
        Token tokenA = childA.AsToken();
        Token tokenB = childB.AsToken();
        if (tokenA.Kind != tokenB.Kind) {
          difference = $"Token kind mismatch in {a.GetType().Name} at child #{childIndex}: {tokenA.Kind} vs {tokenB.Kind}.";
          return false;
        }

        bool missingA = (tokenA.Flags & TokenFlags.Missing) != 0;
        bool missingB = (tokenB.Flags & TokenFlags.Missing) != 0;
        if (missingA != missingB) {
          difference = $"Token missing-flag mismatch in {a.GetType().Name} at child #{childIndex} ({tokenA.Kind}): {missingA} vs {missingB}.";
          return false;
        }

        if (!missingA) {
          ReadOnlySpan<char> textA = sourceA.AsSpan(tokenA.Span);
          ReadOnlySpan<char> textB = sourceB.AsSpan(tokenB.Span);
          if (!textA.SequenceEqual(textB)) {
            difference = $"Token text mismatch in {a.GetType().Name} at child #{childIndex} ({tokenA.Kind}): '{textA.ToString()}' vs '{textB.ToString()}'.";
            return false;
          }
        }
      }

      childIndex++;
    }
  }
}
