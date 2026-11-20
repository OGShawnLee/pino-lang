using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Parsing;

internal sealed record ParseResult<TNode>(
    LexResult Lex,
    TNode Node,
    int NodeCount,
    int EndTokenIndex,
    ImmutableArray<Diagnostic> Diagnostics) where TNode : SyntaxNode;
