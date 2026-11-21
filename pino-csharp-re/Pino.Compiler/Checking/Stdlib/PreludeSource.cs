using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

/// <summary>
/// Embedded <c>prelude.pino</c> declaring pure-Pino standard library unions
/// (<c>Option[T]</c>, <c>Result[T, E]</c>, <c>IOError</c>), parsed once with the real parser.
/// </summary>
public static class PreludeSource {
  public const string VirtualPath = "<prelude>";

  public const string Text = """
    @@Generic[T]
    pub union Option {
      Some(T)
      None
    }

    @@Generic[T, E]
    pub union Result {
      Success(T)
      Failure(E)
    }

    pub union IOError {
      NotFound(string)
      PermissionDenied(string)
      AlreadyExists(string)
      Gremlin(string)
    }
    """;

  private static readonly Lazy<ParseResult> s_parsed = new(
      static () => Parser.Parse(new SourceText(Text, VirtualPath)));

  public static ParseResult Parsed => s_parsed.Value;
}
