using Pino.Compiler.Text;

namespace Pino.Compiler.Lexing;

public readonly record struct Token(TokenKind Kind, SourceSpan Span);
