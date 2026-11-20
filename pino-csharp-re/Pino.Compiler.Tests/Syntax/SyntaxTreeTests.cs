using System.Collections.Immutable;

using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Syntax;

public sealed class SyntaxTreeTests {
  private static Token Missing(TokenKind kind, int position) =>
      new(kind, new SourceSpan(position, 0), TokenFlags.Missing);

  private static IEnumerable<SyntaxNode> WalkNodes(SyntaxNode root) {
    yield return root;
    foreach (SyntaxElement child in root.GetChildren()) {
      if (child.IsNode) {
        foreach (SyntaxNode descendant in WalkNodes(child.AsNode())) {
          yield return descendant;
        }
      }
    }
  }

  private static void AssertTokenAccountingAndMissingInvariant(LexResult lex, SyntaxNode root) {
    Token[] expectedRealTokens = [.. lex.Tokens.Where(static t => t.Kind != TokenKind.EOF)];
    Token[] actualTokens = [.. root.DescendantTokens()];

    Token[] actualRealTokens = [.. actualTokens.Where(static t => (t.Flags & TokenFlags.Missing) == 0)];
    Assert.Equal(expectedRealTokens, actualRealTokens);

    foreach (Token missing in actualTokens.Where(static t => (t.Flags & TokenFlags.Missing) != 0)) {
      Assert.Equal(0, missing.Span.Length);
    }

    int lastStart = -1;
    foreach (Token token in actualTokens) {
      Assert.True(token.Span.Start >= lastStart, $"Token {token} out of order after {lastStart}.");
      lastStart = token.Span.Start;
    }

    foreach (SyntaxNode node in WalkNodes(root)) {
      bool isMissingType = node is MissingExpressionSyntax
          or MissingTypeSyntax
          or MissingPatternSyntax
          or MissingStatementSyntax;
      Assert.Equal(isMissingType, node.IsMissing);
    }
  }

  [Fact]
  public void ValidValAssignmentTree_AccountsForAllNonEOFTokens() {
    LexResult lex = Lexer.Lex(new SourceText("val x = a + b"));
    ImmutableArray<Token> t = lex.Tokens;

    var left = new IdentifierExpressionSyntax(1, t[3].Span, t[3]);
    var right = new IdentifierExpressionSyntax(2, t[5].Span, t[5]);
    var add = new BinaryExpressionSyntax(3, SourceSpan.FromBounds(t[3].Span.Start, t[5].Span.End), left, t[4], right);
    var decl = new VariableDeclarationSyntax(0, SourceSpan.FromBounds(t[0].Span.Start, t[5].Span.End), t[0], t[1], t[2], add);

    AssertTokenAccountingAndMissingInvariant(lex, decl);
    Assert.Equal(t.Where(static tok => tok.Kind != TokenKind.EOF), decl.DescendantTokens());
  }

  [Fact]
  public void A2_Row1_MemberAccessWithMissingIdentifier() {
    LexResult lex = Lexer.Lex(new SourceText("obj:"));
    ImmutableArray<Token> t = lex.Tokens;

    var receiver = new IdentifierExpressionSyntax(1, t[0].Span, t[0]);
    Token missingIdent = Missing(TokenKind.Identifier, t[1].Span.End);
    var access = new MemberAccessExpressionSyntax(0, SourceSpan.FromBounds(t[0].Span.Start, t[1].Span.End), receiver, t[1], missingIdent);

    AssertTokenAccountingAndMissingInvariant(lex, access);
    Assert.Contains(access.DescendantTokens(), static tok => (tok.Flags & TokenFlags.Missing) != 0 && tok.Kind == TokenKind.Identifier);
  }

  [Fact]
  public void A2_Row2_StaticAccessWithMissingIdentifier() {
    LexResult lex = Lexer.Lex(new SourceText("Mod::"));
    ImmutableArray<Token> t = lex.Tokens;

    var target = new IdentifierExpressionSyntax(1, t[0].Span, t[0]);
    Token missingIdent = Missing(TokenKind.Identifier, t[1].Span.End);
    var access = new StaticAccessExpressionSyntax(0, SourceSpan.FromBounds(t[0].Span.Start, t[1].Span.End), target, t[1], missingIdent);

    AssertTokenAccountingAndMissingInvariant(lex, access);
    Assert.Contains(access.DescendantTokens(), static tok => (tok.Flags & TokenFlags.Missing) != 0 && tok.Kind == TokenKind.Identifier);
  }

  [Fact]
  public void A2_Row3_CallWithTrailingCommaAndMissingCloseParen() {
    LexResult lex = Lexer.Lex(new SourceText("foo(a,"));
    ImmutableArray<Token> t = lex.Tokens;

    var callee = new IdentifierExpressionSyntax(1, t[0].Span, t[0]);
    var argExpr = new IdentifierExpressionSyntax(2, t[2].Span, t[2]);
    var arg = new ArgumentSyntax(3, SourceSpan.FromBounds(t[2].Span.Start, t[3].Span.End), argExpr, t[3]);
    Token missingClose = Missing(TokenKind.CloseParen, t[3].Span.End);
    var call = new CallExpressionSyntax(0, SourceSpan.FromBounds(t[0].Span.Start, t[3].Span.End), callee, t[1], [arg], missingClose);

    AssertTokenAccountingAndMissingInvariant(lex, call);
    Assert.Contains(call.DescendantTokens(), static tok => (tok.Flags & TokenFlags.Missing) != 0 && tok.Kind == TokenKind.CloseParen);
  }

  [Fact]
  public void A2_Row4_ValWithMissingInitializer() {
    LexResult lex = Lexer.Lex(new SourceText("val x ="));
    ImmutableArray<Token> t = lex.Tokens;

    var missingExpr = new MissingExpressionSyntax(1, new SourceSpan(t[2].Span.End, 0));
    var decl = new VariableDeclarationSyntax(0, SourceSpan.FromBounds(t[0].Span.Start, t[2].Span.End), t[0], t[1], t[2], missingExpr);

    AssertTokenAccountingAndMissingInvariant(lex, decl);
    Assert.True(decl.Initializer.IsMissing);
  }

  [Fact]
  public void A2_Row5_IfWithUnclosedBlock() {
    LexResult lex = Lexer.Lex(new SourceText("if x {"));
    ImmutableArray<Token> t = lex.Tokens;

    var cond = new IdentifierExpressionSyntax(1, t[1].Span, t[1]);
    Token missingClose = Missing(TokenKind.CloseBrace, t[2].Span.End);
    var block = new BlockStatementSyntax(2, new SourceSpan(t[2].Span.Start, t[2].Span.Length), t[2], [], missingClose);
    var ifStmt = new IfStatementSyntax(0, SourceSpan.FromBounds(t[0].Span.Start, t[2].Span.End), t[0], cond, block, null, null);

    AssertTokenAccountingAndMissingInvariant(lex, ifStmt);
    Assert.Contains(ifStmt.DescendantTokens(), static tok => (tok.Flags & TokenFlags.Missing) != 0 && tok.Kind == TokenKind.CloseBrace);
  }

  [Fact]
  public void A2_Row6_MatchWithWhenAndMissingPatternAndCloseBrace() {
    LexResult lex = Lexer.Lex(new SourceText("match x { when"));
    ImmutableArray<Token> t = lex.Tokens;

    var subject = new IdentifierExpressionSyntax(1, t[1].Span, t[1]);
    var missingPattern = new MissingPatternSyntax(2, new SourceSpan(t[3].Span.End, 0));
    var whenArm = new WhenArmSyntax(3, t[3].Span, t[3], [missingPattern], null, null, null);
    Token missingClose = Missing(TokenKind.CloseBrace, t[3].Span.End);
    var match = new MatchExpressionSyntax(0, SourceSpan.FromBounds(t[0].Span.Start, t[3].Span.End), t[0], subject, t[2], [whenArm], missingClose);

    AssertTokenAccountingAndMissingInvariant(lex, match);
    Assert.True(whenArm.Patterns[0].IsMissing);
    Assert.Contains(match.DescendantTokens(), static tok => (tok.Flags & TokenFlags.Missing) != 0 && tok.Kind == TokenKind.CloseBrace);
  }

  [Fact]
  public void A2_Row7_FunctionWithMissingCloseParenAndMissingBodyBraces() {
    LexResult lex = Lexer.Lex(new SourceText("fn foo("));
    ImmutableArray<Token> t = lex.Tokens;

    int end = t[2].Span.End;
    Token missingCloseParen = Missing(TokenKind.CloseParen, end);
    Token missingOpenBrace = Missing(TokenKind.OpenBrace, end);
    Token missingCloseBrace = Missing(TokenKind.CloseBrace, end);
    var body = new BlockStatementSyntax(1, new SourceSpan(end, 0), missingOpenBrace, [], missingCloseBrace);
    var fn = new FunctionDeclarationSyntax(0, SourceSpan.FromBounds(t[0].Span.Start, end), null, t[0], t[1], t[2], [], missingCloseParen, null, body);

    AssertTokenAccountingAndMissingInvariant(lex, fn);
    Token[] missingTokens = [.. fn.DescendantTokens().Where(static tok => (tok.Flags & TokenFlags.Missing) != 0)];
    Assert.Equal([TokenKind.CloseParen, TokenKind.OpenBrace, TokenKind.CloseBrace], missingTokens.Select(static tok => tok.Kind));
  }

  [Fact]
  public void A2_Row8_StructLiteralWithFieldAndMissingCloseBrace() {
    LexResult lex = Lexer.Lex(new SourceText("Point { x: 1,"));
    ImmutableArray<Token> t = lex.Tokens;

    var typeName = new NamedTypeSyntax(1, t[0].Span, t[0]);
    var valExpr = new LiteralExpressionSyntax(2, t[4].Span, t[4]);
    var field = new StructFieldInitSyntax(3, SourceSpan.FromBounds(t[2].Span.Start, t[5].Span.End), t[2], t[3], valExpr, t[5]);
    Token missingClose = Missing(TokenKind.CloseBrace, t[5].Span.End);
    var lit = new StructLiteralExpressionSyntax(0, SourceSpan.FromBounds(t[0].Span.Start, t[5].Span.End), typeName, t[1], [field], missingClose);

    AssertTokenAccountingAndMissingInvariant(lex, lit);
    Assert.Contains(lit.DescendantTokens(), static tok => (tok.Flags & TokenFlags.Missing) != 0 && tok.Kind == TokenKind.CloseBrace);
  }

  [Fact]
  public void A2_Row9_VectorLiteralWithTwoElementsAndMissingCloseBracket() {
    LexResult lex = Lexer.Lex(new SourceText("[1, 2"));
    ImmutableArray<Token> t = lex.Tokens;

    var first = new LiteralExpressionSyntax(1, t[1].Span, t[1]);
    var second = new LiteralExpressionSyntax(2, t[3].Span, t[3]);
    Token missingClose = Missing(TokenKind.CloseBracket, t[3].Span.End);
    var vec = new VectorLiteralExpressionSyntax(0, SourceSpan.FromBounds(t[0].Span.Start, t[3].Span.End), t[0], [first, second], missingClose, [t[2]]);

    AssertTokenAccountingAndMissingInvariant(lex, vec);
    Assert.Contains(vec.DescendantTokens(), static tok => (tok.Flags & TokenFlags.Missing) != 0 && tok.Kind == TokenKind.CloseBracket);
  }
}
