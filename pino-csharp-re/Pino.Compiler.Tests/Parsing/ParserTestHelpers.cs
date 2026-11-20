using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Parsing;

internal static class ParserTestHelpers {
  public static ParseResult ParseFile(string source, bool expectNoDiagnostics = true) {
    ParseResult result = Parser.Parse(new SourceText(source));
    AssertFileInvariants(result, expectNoDiagnostics);
    return result;
  }

  public static ParseResult<ExpressionSyntax> ParseExpression(string source, bool expectEOF = true, bool expectNoDiagnostics = true) {
    LexResult lex = Lexer.Lex(new SourceText(source));
    ParseResult<ExpressionSyntax> result = Parser.ParseStandaloneExpression(lex);
    AssertInvariants(result, expectEOF, expectNoDiagnostics);
    return result;
  }

  public static ParseResult<TypeSyntax> ParseType(string source, bool expectEOF = true, bool expectNoDiagnostics = true) {
    LexResult lex = Lexer.Lex(new SourceText(source));
    ParseResult<TypeSyntax> result = Parser.ParseStandaloneType(lex);
    AssertInvariants(result, expectEOF, expectNoDiagnostics);
    return result;
  }

  public static ParseResult<PatternSyntax> ParsePattern(string source, bool expectEOF = true, bool expectNoDiagnostics = true) {
    LexResult lex = Lexer.Lex(new SourceText(source));
    ParseResult<PatternSyntax> result = Parser.ParseStandalonePattern(lex);
    AssertInvariants(result, expectEOF, expectNoDiagnostics);
    return result;
  }

  public static ParseResult<BlockStatementSyntax> ParseBlock(string source, bool expectEOF = true, bool expectNoDiagnostics = true) {
    LexResult lex = Lexer.Lex(new SourceText(source));
    ParseResult<BlockStatementSyntax> result = Parser.ParseStandaloneBlock(lex);
    AssertInvariants(result, expectEOF, expectNoDiagnostics);
    return result;
  }

  public static void AssertFileInvariants(
      ParseResult result,
      bool expectNoDiagnostics = false) {
    LexResult lex = result.Lex;
    if (expectNoDiagnostics) {
      Assert.Empty(lex.Diagnostics);
      Assert.Empty(result.Diagnostics);
    }

    AssertTokenAccounting(lex, result.Root, lex.Tokens.Length - 1, includesEOF: true);
    AssertNodesAndDiagnostics(result.Root, result.NodeCount, result.Diagnostics, lex.Source.Length, lex.Source.Text);
  }

  public static void AssertInvariants<TNode>(
      ParseResult<TNode> result,
      bool expectEOF = true,
      bool expectNoDiagnostics = false) where TNode : SyntaxNode {
    LexResult lex = result.Lex;
    if (expectNoDiagnostics) {
      Assert.Empty(lex.Diagnostics);
      Assert.Empty(result.Diagnostics);
    }

    if (expectEOF) {
      Assert.Equal(lex.Tokens.Length - 1, result.EndTokenIndex);
      Assert.Equal(TokenKind.EOF, lex.Tokens[result.EndTokenIndex].Kind);
    } else {
      Assert.InRange(result.EndTokenIndex, 0, lex.Tokens.Length - 1);
    }

    AssertTokenAccounting(lex, result.Node, result.EndTokenIndex, includesEOF: false);
    AssertNodesAndDiagnostics(result.Node, result.NodeCount, result.Diagnostics, lex.Source.Length, lex.Source.Text);
  }

  private static void AssertTokenAccounting(
      LexResult lex,
      SyntaxNode root,
      int endTokenIndex,
      bool includesEOF) {
    int sourceLength = lex.Source.Length;
    Token[] expectedRealTokens = includesEOF
        ? [.. lex.Tokens]
        : [.. lex.Tokens.Take(endTokenIndex)];
    Token[] actualTokens = [.. root.DescendantTokens()];
    Token[] actualRealTokens = [.. actualTokens.Where(static t => (t.Flags & TokenFlags.Missing) == 0)];
    Assert.Equal(expectedRealTokens, actualRealTokens);

    if (includesEOF) {
      Token[] expectedNonEOF = [.. lex.Tokens.Where(static t => t.Kind != TokenKind.EOF)];
      Token[] actualNonEOF = [.. actualRealTokens.Where(static t => t.Kind != TokenKind.EOF)];
      Assert.Equal(expectedNonEOF, actualNonEOF);
      Assert.Equal(TokenKind.EOF, actualRealTokens[^1].Kind);
    }

    int lastStart = -1;
    foreach (Token token in actualTokens) {
      if ((token.Flags & TokenFlags.Missing) != 0) {
        Assert.Equal(0, token.Span.Length);
      }

      Assert.True(token.Span.Start >= lastStart, $"Token {token} out of order after {lastStart}.");
      Assert.InRange(token.Span.Start, 0, sourceLength);
      Assert.InRange(token.Span.End, 0, sourceLength);
      lastStart = token.Span.Start;
    }
  }

  private static void AssertNodesAndDiagnostics(
      SyntaxNode root,
      int nodeCount,
      ImmutableArray<Diagnostic> diagnostics,
      int sourceLength,
      string sourceText) {
    var seenIds = new HashSet<int>();
    foreach (SyntaxNode node in WalkNodes(root)) {
      Assert.InRange(node.Id, 0, nodeCount - 1);
      Assert.True(seenIds.Add(node.Id), $"Duplicate node Id {node.Id}.");
      Assert.InRange(node.Span.Start, 0, sourceLength);
      Assert.InRange(node.Span.End, 0, sourceLength);

      bool isMissingType = node is MissingExpressionSyntax
          or MissingTypeSyntax
          or MissingPatternSyntax
          or MissingStatementSyntax;
      Assert.Equal(isMissingType, node.IsMissing);
    }

    Assert.True(
        nodeCount == seenIds.Count,
        $"Dense NodeCount mismatch for '{sourceText}': NodeCount={nodeCount}, seen={seenIds.Count}");

    int prevStart = -1;
    int prevLen = -1;
    foreach (Diagnostic diag in diagnostics) {
      Assert.InRange(diag.Span.Start, 0, sourceLength);
      Assert.InRange(diag.Span.End, 0, sourceLength);
      Assert.True(
          diag.Span.Start > prevStart || (diag.Span.Start == prevStart && diag.Span.Length >= prevLen),
          $"Diagnostics not sorted: {diag} after ({prevStart}, {prevLen}).");
      prevStart = diag.Span.Start;
      prevLen = diag.Span.Length;

      foreach (RelatedLocation related in diag.Related) {
        Assert.InRange(related.Span.Start, 0, sourceLength);
        Assert.InRange(related.Span.End, 0, sourceLength);
      }
    }
  }

  private static IEnumerable<SyntaxNode> WalkNodes(SyntaxNode root) {
    var stack = new Stack<SyntaxNode>();
    stack.Push(root);
    while (stack.Count > 0) {
      SyntaxNode current = stack.Pop();
      yield return current;
      foreach (SyntaxElement child in current.GetChildren()) {
        if (child.IsNode) {
          stack.Push(child.AsNode());
        }
      }
    }
  }
}
