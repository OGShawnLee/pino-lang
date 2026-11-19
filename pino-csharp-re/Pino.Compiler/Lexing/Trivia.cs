using Pino.Compiler.Text;

namespace Pino.Compiler.Lexing;

public readonly record struct Trivia(TriviaKind Kind, SourceSpan Span);
