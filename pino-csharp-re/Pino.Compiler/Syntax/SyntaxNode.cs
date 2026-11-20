using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Syntax;

public sealed record ParseResult(
    SourceText Source,
    LexResult Lex,
    CompilationUnitSyntax Root,
    int NodeCount,
    ImmutableArray<Diagnostic> Diagnostics);

public readonly record struct SyntaxElement {
  private readonly SyntaxNode? _node;
  private readonly Token _token;

  public SyntaxElement(SyntaxNode node) {
    ArgumentNullException.ThrowIfNull(node);
    _node = node;
    _token = default;
    IsNode = true;
  }

  public SyntaxElement(Token token) {
    _node = null;
    _token = token;
    IsNode = false;
  }

  public bool IsNode { get; }

  public bool IsToken => !IsNode;

  public SyntaxNode AsNode() =>
      IsNode ? _node! : throw new InvalidOperationException("SyntaxElement does not hold a SyntaxNode.");

  public Token AsToken() =>
      IsToken ? _token : throw new InvalidOperationException("SyntaxElement does not hold a Token.");

  public static implicit operator SyntaxElement(SyntaxNode node) => new(node);

  public static implicit operator SyntaxElement(Token token) => new(token);
}

public abstract record SyntaxNode(int Id, SourceSpan Span) {
  public virtual bool IsMissing => false;

  public abstract IEnumerable<SyntaxElement> GetChildren();

  public IEnumerable<Token> DescendantTokens() {
    var stack = new Stack<IEnumerator<SyntaxElement>>();
    stack.Push(GetChildren().GetEnumerator());
    try {
      while (stack.Count > 0) {
        IEnumerator<SyntaxElement> current = stack.Peek();
        if (!current.MoveNext()) {
          stack.Pop().Dispose();
          continue;
        }

        SyntaxElement element = current.Current;
        if (element.IsToken) {
          yield return element.AsToken();
        } else {
          stack.Push(element.AsNode().GetChildren().GetEnumerator());
        }
      }
    } finally {
      while (stack.Count > 0) {
        stack.Pop().Dispose();
      }
    }
  }

  protected static IEnumerable<SyntaxElement> InterleaveBySpan<TNode>(
      ImmutableArray<TNode> nodes,
      ImmutableArray<Token> commas) where TNode : SyntaxNode {
    if (commas.IsDefaultOrEmpty) {
      foreach (TNode node in nodes) {
        yield return node;
      }
      yield break;
    }

    int ni = 0;
    int ci = 0;
    while (ni < nodes.Length && ci < commas.Length) {
      if (commas[ci].Span.Start < nodes[ni].Span.Start) {
        yield return commas[ci++];
      } else {
        yield return nodes[ni++];
      }
    }

    while (ni < nodes.Length) {
      yield return nodes[ni++];
    }

    while (ci < commas.Length) {
      yield return commas[ci++];
    }
  }

  protected static IEnumerable<SyntaxElement> InterleaveTokensBySpan(
      ImmutableArray<Token> tokens,
      ImmutableArray<Token> commas) {
    if (commas.IsDefaultOrEmpty) {
      foreach (Token token in tokens) {
        yield return token;
      }
      yield break;
    }

    int ti = 0;
    int ci = 0;
    while (ti < tokens.Length && ci < commas.Length) {
      if (commas[ci].Span.Start < tokens[ti].Span.Start) {
        yield return commas[ci++];
      } else {
        yield return tokens[ti++];
      }
    }

    while (ti < tokens.Length) {
      yield return tokens[ti++];
    }

    while (ci < commas.Length) {
      yield return commas[ci++];
    }
  }
}

public abstract record ExpressionSyntax(int Id, SourceSpan Span) : SyntaxNode(Id, Span);

public abstract record StatementSyntax(int Id, SourceSpan Span) : SyntaxNode(Id, Span);

public abstract record DeclarationSyntax(int Id, SourceSpan Span) : SyntaxNode(Id, Span);

public abstract record TypeSyntax(int Id, SourceSpan Span) : SyntaxNode(Id, Span);

public abstract record PatternSyntax(int Id, SourceSpan Span) : SyntaxNode(Id, Span);

public sealed record CompilationUnitSyntax(
    int Id,
    SourceSpan Span,
    ImmutableArray<SyntaxNode> Items,
    Token EOFToken) : SyntaxNode(Id, Span) {
  public override IEnumerable<SyntaxElement> GetChildren() {
    foreach (SyntaxNode item in Items) {
      yield return item;
    }
    yield return EOFToken;
  }
}
