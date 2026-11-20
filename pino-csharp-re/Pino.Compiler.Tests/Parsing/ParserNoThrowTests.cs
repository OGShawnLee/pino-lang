using Pino.Compiler.Lexing;
using Pino.Compiler.Parsing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Parsing;

public sealed class ParserNoThrowTests {
  private static readonly string[] s_alphabet = [
    "a",
    "1",
    "\"s\"",
    "(",
    ")",
    "[",
    "]",
    "{",
    "}",
    ",",
    ":",
    "::",
    "->",
    "=>",
    "?",
    "-",
    "+",
    "=",
    "@",
    "fn",
    "if",
    "then",
    "else",
    "match",
    "when",
    "return",
    "yield",
    "or",
    "is",
    "in",
    "map",
    "set",
    "\n",
    "$",
  ];

  [Fact]
  public void AllZeroToTwoTokenSequences_NeverThrowAndMaintainInvariants() {
    CheckInput(string.Empty);

    foreach (string first in s_alphabet) {
      CheckInput(first);
      foreach (string second in s_alphabet) {
        CheckInput($"{first} {second}");
      }
    }

    // Also test tight adjacency for spacing-sensitive pairs.
    CheckInput("a-b");
    CheckInput("a -b");
    CheckInput("a- b");
    CheckInput("=>");
    CheckInput("@@");
  }

  private static void CheckInput(string source) {
    LexResult lex = Lexer.Lex(new SourceText(source));
    ParserTestHelpers.AssertInvariants(Parser.ParseStandaloneExpression(lex), expectEOF: false, expectNoDiagnostics: false);
    ParserTestHelpers.AssertInvariants(Parser.ParseStandaloneType(lex), expectEOF: false, expectNoDiagnostics: false);
    ParserTestHelpers.AssertInvariants(Parser.ParseStandalonePattern(lex), expectEOF: false, expectNoDiagnostics: false);
    ParserTestHelpers.AssertInvariants(Parser.ParseStandaloneBlock(lex), expectEOF: false, expectNoDiagnostics: false);
  }
}
