using System.Text;

using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Parsing;

internal static class SyntaxPrinter {
  public static string Print(SyntaxNode node, SourceText source) {
    ArgumentNullException.ThrowIfNull(node);
    ArgumentNullException.ThrowIfNull(source);

    var sb = new StringBuilder();
    AppendNode(sb, node, source);
    return sb.ToString();
  }

  private static void AppendNode(StringBuilder sb, SyntaxNode node, SourceText source) {
    sb.Append('(');
    string name = node.GetType().Name;
    sb.Append(name.EndsWith("Syntax", StringComparison.Ordinal) ? name[..^6] : name);

    foreach (SyntaxElement child in node.GetChildren()) {
      if (child.IsNode) {
        sb.Append(' ');
        AppendNode(sb, child.AsNode(), source);
      } else {
        Token token = child.AsToken();
        if ((token.Flags & TokenFlags.Missing) != 0) {
          sb.Append(" <missing ").Append(token.Kind).Append('>');
        } else if (IsPrintableToken(token.Kind)) {
          sb.Append(' ').Append(source.AsSpan(token.Span));
        }
      }
    }

    sb.Append(')');
  }

  private static bool IsPrintableToken(TokenKind kind) =>
      kind is TokenKind.Identifier
          or TokenKind.IntLiteral
          or TokenKind.FloatLiteral
          or TokenKind.RuneLiteral
          or TokenKind.TrueLiteral
          or TokenKind.FalseLiteral
          or TokenKind.StringPart
          or TokenKind.Error
          or TokenKind.And
          or TokenKind.Or
          or TokenKind.Not
          or TokenKind.InKeyword
          or TokenKind.IsKeyword
          or TokenKind.Assign
          or TokenKind.Plus
          or TokenKind.PlusAssign
          or TokenKind.Minus
          or TokenKind.MinusAssign
          or TokenKind.Star
          or TokenKind.StarAssign
          or TokenKind.Slash
          or TokenKind.SlashAssign
          or TokenKind.Percent
          or TokenKind.PercentAssign
          or TokenKind.Less
          or TokenKind.LessEqual
          or TokenKind.Greater
          or TokenKind.GreaterEqual
          or TokenKind.EqualEqual
          or TokenKind.BangEqual
          or TokenKind.EqualEqualEqual
          or TokenKind.BangEqualEqual
          or TokenKind.Colon
          or TokenKind.ColonColon
          or TokenKind.Arrow
          or TokenKind.Question;
}
