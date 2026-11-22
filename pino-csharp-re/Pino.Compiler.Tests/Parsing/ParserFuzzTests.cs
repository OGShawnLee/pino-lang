using System.Diagnostics;
using System.Text;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

using Xunit.Abstractions;

namespace Pino.Compiler.Tests.Parsing;

/// <summary>Exhaustive, seeded-random, and deep-nesting fuzz tests for <see cref="Parser.Parse(SourceText)"/>.</summary>
public sealed class ParserFuzzTests(ITestOutputHelper output) {
  private const int FuzzSeed = 20261120;
  private const int RandomInputCount = 25_000;
  private const int MaxRandomTokens = 60;
  private const int DeepNestingDepth = 10_000;

  private static readonly TimeSpan s_normalTimeout = TimeSpan.FromMilliseconds(500);
  private static readonly TimeSpan s_nestingTimeout = TimeSpan.FromSeconds(2);

  private static readonly string[] s_exhaustiveAlphabet = [
    "{", "}", "(", ")", "[", "]", ",", ":", "::", "->",
    "=", "-", "?", "or", "not", "fn", "match", "when", "if", "else",
    "@", "x", "1", "\"s\"", "\n",
  ];

  private static readonly string[] s_randomFragments = [
    .. s_exhaustiveAlphabet,
    "val", "var", "struct", "union", "enum", "interface", "test", "module",
    "import", "from", "pub", "static", "for", "in", "is", "and", "return",
    "yield", "break", "continue", "assert", "then", "as", "true", "false",
    "\"a $x b\"", "\"$(x + 1)\"", "\"$if\"", "\"\"\"multi\nline $x\"\"\"",
    "@@Generic[T]", "@@Generic[T is Shape]", "@pub_testing", "@generic[T]",
    "map[K, V]", "set[T]", "Pool[T]", "Handle[T]", "Pool[", "Handle[", "[]int", "@(a: 1, b)", "=>", "+=", "===", "!==",
    "~", "a-b", "a -b", "a- b", "@@Testable", "//", "//=", "val x int =", "@b: c", "mut",
    "val mut", "var mut", "mut val",
  ];

  private static bool s_warmedUp;

  [Fact]
  public void ExhaustiveZeroToThreeTokenSequences_MaintainAllInvariants() {
    EnsureWarmedUp();

    int count = 0;
    TimeSpan maxElapsed = TimeSpan.Zero;

    // Length 0
    CheckFuzzInput(string.Empty, "exhaustive[len=0, index=0]", s_normalTimeout, ref maxElapsed);
    count++;

    int len = s_exhaustiveAlphabet.Length;

    // Length 1
    for (int i = 0; i < len; i++) {
      CheckFuzzInput(s_exhaustiveAlphabet[i], $"exhaustive[len=1, index={count}]", s_normalTimeout, ref maxElapsed);
      count++;
    }

    // Length 2
    for (int i = 0; i < len; i++) {
      for (int j = 0; j < len; j++) {
        string text = $"{s_exhaustiveAlphabet[i]} {s_exhaustiveAlphabet[j]}";
        CheckFuzzInput(text, $"exhaustive[len=2, index={count}]", s_normalTimeout, ref maxElapsed);
        count++;
      }
    }

    // Length 3
    for (int i = 0; i < len; i++) {
      for (int j = 0; j < len; j++) {
        for (int k = 0; k < len; k++) {
          string text = $"{s_exhaustiveAlphabet[i]} {s_exhaustiveAlphabet[j]} {s_exhaustiveAlphabet[k]}";
          CheckFuzzInput(text, $"exhaustive[len=3, index={count}]", s_normalTimeout, ref maxElapsed);
          count++;
        }
      }
    }

    Assert.Equal(16_276, count);
    output.WriteLine($"Exhaustive 0-3 tokens: {count} inputs passed, max={maxElapsed.TotalMilliseconds:F2} ms (limit={s_normalTimeout.TotalMilliseconds:F0} ms).");
  }

  [Fact]
  public void SeededRandomTokenSequences_MaintainAllInvariants() {
    EnsureWarmedUp();

    var random = new Random(FuzzSeed);
    var sb = new StringBuilder(512);
    TimeSpan maxElapsed = TimeSpan.Zero;

    for (int i = 0; i < RandomInputCount; i++) {
      sb.Clear();
      int tokenCount = random.Next(1, MaxRandomTokens + 1);
      for (int j = 0; j < tokenCount; j++) {
        if (j > 0 && random.Next(100) >= 15) {
          sb.Append(' ');
        }

        sb.Append(s_randomFragments[random.Next(s_randomFragments.Length)]);
      }

      string text = sb.ToString();
      CheckFuzzInput(text, $"random[seed={FuzzSeed}, i={i}]", s_normalTimeout, ref maxElapsed);
    }

    output.WriteLine($"Seeded random ({FuzzSeed}): {RandomInputCount} inputs passed, max={maxElapsed.TotalMilliseconds:F2} ms (limit={s_normalTimeout.TotalMilliseconds:F0} ms).");
  }

  [Theory]
  [InlineData("parens_balanced")]
  [InlineData("parens_unclosed")]
  [InlineData("brackets_balanced")]
  [InlineData("brackets_unclosed")]
  [InlineData("braces_balanced")]
  [InlineData("braces_unclosed")]
  [InlineData("fn_arrow")]
  [InlineData("match_when_balanced")]
  [InlineData("match_when_unclosed")]
  public void DeepNestingTenThousandLevels_MaintainsInvariantsWithinTimeLimit(string kind) {
    EnsureWarmedUp();

    string source = kind switch {
      "parens_balanced" => new string('(', DeepNestingDepth) + "1" + new string(')', DeepNestingDepth),
      "parens_unclosed" => new string('(', DeepNestingDepth) + "1",
      "brackets_balanced" => new string('[', DeepNestingDepth) + "1" + new string(']', DeepNestingDepth),
      "brackets_unclosed" => new string('[', DeepNestingDepth) + "1",
      "braces_balanced" => new string('{', DeepNestingDepth) + "1" + new string('}', DeepNestingDepth),
      "braces_unclosed" => new string('{', DeepNestingDepth) + "1",
      "fn_arrow" => Repeat("fn () -> ", DeepNestingDepth) + "1",
      "match_when_balanced" => Repeat("match x { when 1 { ", DeepNestingDepth) + "1" + new string('}', DeepNestingDepth * 2),
      "match_when_unclosed" => Repeat("match x { when 1 { ", DeepNestingDepth) + "1",
      _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    TimeSpan maxElapsed = TimeSpan.Zero;
    CheckFuzzInput(source, $"nesting[{kind}]", s_nestingTimeout, ref maxElapsed);
    output.WriteLine($"Nesting '{kind}' (depth={DeepNestingDepth}): {maxElapsed.TotalMilliseconds:F2} ms (limit={s_nestingTimeout.TotalMilliseconds:F0} ms).");
  }

  private static void EnsureWarmedUp() {
    if (s_warmedUp) {
      return;
    }

    _ = Parser.Parse(new SourceText("fn warmup() { val x = 1 }\nstruct Foo[T] { a T }\n{ ( \"a $x\""));
    s_warmedUp = true;
  }

  private static string Repeat(string fragment, int count) {
    var sb = new StringBuilder(fragment.Length * count);
    for (int i = 0; i < count; i++) {
      sb.Append(fragment);
    }

    return sb.ToString();
  }

  private static void CheckFuzzInput(string text, string label, TimeSpan timeLimit, ref TimeSpan maxElapsed) {
    ParseResult result;
    var sw = Stopwatch.StartNew();
    try {
      result = Parser.Parse(new SourceText(text));
    } catch (Exception ex) {
      Assert.Fail($"{label} threw {ex.GetType().Name}: {ex.Message}\nInput: {EscapeText(text)}");
      return;
    }

    sw.Stop();
    if (sw.Elapsed > maxElapsed) {
      maxElapsed = sw.Elapsed;
    }

    if (sw.Elapsed >= timeLimit) {
      Assert.Fail($"{label} exceeded time limit ({sw.Elapsed.TotalMilliseconds:F1} ms >= {timeLimit.TotalMilliseconds:F0} ms).\nInput: {EscapeText(text)}");
    }

    AssertFuzzInvariants(result, label, text);
  }

  private static void AssertFuzzInvariants(ParseResult result, string label, string text) {
    LexResult lex = result.Lex;
    int sourceLength = lex.Source.Length;

    if (result.NodeCount <= 0) {
      Assert.Fail($"{label}: NodeCount={result.NodeCount} <= 0.\nInput: {EscapeText(text)}");
    }

    // 1. Dense node IDs 0..NodeCount-1 and in-bounds spans.
    bool[] seenIds = new bool[result.NodeCount];
    int seenCount = 0;
    var stack = new Stack<SyntaxNode>();
    stack.Push(result.Root);
    while (stack.Count > 0) {
      SyntaxNode node = stack.Pop();
      if ((uint) node.Id >= (uint) result.NodeCount) {
        Assert.Fail($"{label}: Node Id {node.Id} out of range [0, {result.NodeCount}).\nInput: {EscapeText(text)}");
      }

      if (seenIds[node.Id]) {
        Assert.Fail($"{label}: Duplicate node Id {node.Id}.\nInput: {EscapeText(text)}");
      }

      seenIds[node.Id] = true;
      seenCount++;

      if (node.Span.Start < 0 || node.Span.End > sourceLength) {
        Assert.Fail($"{label}: Node {node.GetType().Name} span {node.Span} out of bounds [0, {sourceLength}].\nInput: {EscapeText(text)}");
      }

      bool expectedMissing = node is MissingExpressionSyntax
          or MissingTypeSyntax
          or MissingPatternSyntax
          or MissingStatementSyntax;
      if (node.IsMissing != expectedMissing) {
        Assert.Fail($"{label}: Node {node.GetType().Name} IsMissing={node.IsMissing} expected {expectedMissing}.\nInput: {EscapeText(text)}");
      }

      foreach (SyntaxElement child in node.GetChildren()) {
        if (child.IsNode) {
          stack.Push(child.AsNode());
        }
      }
    }

    if (seenCount != result.NodeCount) {
      var missingIds = new List<int>();
      for (int id = 0; id < result.NodeCount; id++) {
        if (!seenIds[id]) {
          missingIds.Add(id);
        }
      }
      Assert.Fail($"{label}: Dense NodeCount mismatch: NodeCount={result.NodeCount}, seen={seenCount}, missingIds=[{string.Join(", ", missingIds)}].\nTree: {SyntaxPrinter.Print(result.Root, result.Lex.Source)}\nInput: {EscapeText(text)}");
    }

    // 2. Full token accounting, strictly monotonic offsets, Missing length == 0.
    int realIndex = 0;
    int lastStart = -1;
    foreach (Token token in result.Root.DescendantTokens()) {
      if (token.Span.Start < 0 || token.Span.End > sourceLength) {
        Assert.Fail($"{label}: Token {token} span out of bounds [0, {sourceLength}].\nInput: {EscapeText(text)}");
      }

      if (token.Span.Start < lastStart) {
        Assert.Fail($"{label}: Token {token} out of order after {lastStart}.\nInput: {EscapeText(text)}");
      }

      lastStart = token.Span.Start;

      if ((token.Flags & TokenFlags.Missing) != 0) {
        if (token.Span.Length != 0) {
          Assert.Fail($"{label}: Missing token {token} has non-zero length {token.Span.Length}.\nInput: {EscapeText(text)}");
        }

        if (realIndex < lex.Tokens.Length && token == lex.Tokens[realIndex]) {
          realIndex++;
        }
      } else {
        if (realIndex >= lex.Tokens.Length || token != lex.Tokens[realIndex]) {
          string expected = realIndex < lex.Tokens.Length ? lex.Tokens[realIndex].ToString() : "<end>";
          Assert.Fail($"{label}: Token accounting mismatch at index {realIndex}: got {token}, expected {expected}.\nInput: {EscapeText(text)}");
        }

        realIndex++;
      }
    }

    if (realIndex != lex.Tokens.Length) {
      Assert.Fail($"{label}: Token accounting stopped at {realIndex}/{lex.Tokens.Length}.\nInput: {EscapeText(text)}");
    }

    // 3. Sorted diagnostics, in-bounds spans, and at most one parser diagnostic per token position.
    int prevStart = -1;
    int prevLength = -1;
    var parserDiagPositions = new HashSet<int>();
    foreach (Diagnostic diag in result.Diagnostics) {
      if (diag.Span.Start < 0 || diag.Span.End > sourceLength) {
        Assert.Fail($"{label}: Diagnostic {diag} span out of bounds [0, {sourceLength}].\nInput: {EscapeText(text)}");
      }

      if (diag.Span.Start < prevStart || (diag.Span.Start == prevStart && diag.Span.Length < prevLength)) {
        Assert.Fail($"{label}: Diagnostics not sorted: {diag} after ({prevStart}, {prevLength}).\nInput: {EscapeText(text)}");
      }

      prevStart = diag.Span.Start;
      prevLength = diag.Span.Length;

      foreach (RelatedLocation related in diag.Related) {
        if (related.Span.Start < 0 || related.Span.End > sourceLength) {
          Assert.Fail($"{label}: Related span {related.Span} out of bounds [0, {sourceLength}].\nInput: {EscapeText(text)}");
        }
      }

      if (diag.Code.StartsWith("PN2", StringComparison.Ordinal)) {
        if (!parserDiagPositions.Add(diag.Span.Start)) {
          string allDiags = string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code}@{d.Span.Start}+{d.Span.Length}"));
          Assert.Fail($"{label}: Multiple parser diagnostics at offset {diag.Span.Start}: {allDiags}\nInput: {EscapeText(text)}");
        }
      }
    }
  }

  private static string EscapeText(string text) {
    if (text.Length > 300) {
      text = text[..300] + "...";
    }

    var sb = new StringBuilder(text.Length + 16);
    sb.Append('"');
    foreach (char ch in text) {
      switch (ch) {
        case '\\': sb.Append(@"\\"); break;
        case '"': sb.Append("\\\""); break;
        case '\n': sb.Append(@"\n"); break;
        case '\r': sb.Append(@"\r"); break;
        case '\t': sb.Append(@"\t"); break;
        case '\0': sb.Append(@"\0"); break;
        default:
          if (char.IsControl(ch)) {
            sb.Append($"\\u{(int) ch:X4}");
          } else {
            sb.Append(ch);
          }
          break;
      }
    }
    sb.Append('"');
    return sb.ToString();
  }
}
