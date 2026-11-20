using System.Reflection;

using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Syntax;

public sealed class SyntaxHierarchyTests {
  [Fact]
  public void AllConcreteSyntaxNodes_AreSealedInSyntaxNamespaceAndEndWithSyntax() {
    Assembly assembly = typeof(SyntaxNode).Assembly;
    Type[] concreteNodes = assembly.GetTypes()
        .Where(static t => !t.IsAbstract && typeof(SyntaxNode).IsAssignableFrom(t))
        .ToArray();

    Assert.NotEmpty(concreteNodes);

    foreach (Type type in concreteNodes) {
      Assert.True(type.IsSealed, $"{type.FullName} must be sealed.");
      Assert.Equal("Pino.Compiler.Syntax", type.Namespace);
      Assert.EndsWith("Syntax", type.Name, StringComparison.Ordinal);
    }
  }

  [Fact]
  public void SyntaxElement_DistinguishesNodeAndToken() {
    var token = new Token(TokenKind.Identifier, new SourceSpan(0, 1));
    var node = new IdentifierExpressionSyntax(0, new SourceSpan(0, 1), token);

    var tokenElement = new SyntaxElement(token);
    Assert.True(tokenElement.IsToken);
    Assert.False(tokenElement.IsNode);
    Assert.Equal(token, tokenElement.AsToken());
    Assert.Throws<InvalidOperationException>(() => tokenElement.AsNode());

    var nodeElement = new SyntaxElement(node);
    Assert.True(nodeElement.IsNode);
    Assert.False(nodeElement.IsToken);
    Assert.Same(node, nodeElement.AsNode());
    Assert.Throws<InvalidOperationException>(() => nodeElement.AsToken());
  }
}
