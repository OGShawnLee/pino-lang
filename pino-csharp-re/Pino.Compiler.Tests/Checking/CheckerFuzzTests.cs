using System.Diagnostics;
using System.Text;

using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Lexing;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

using Xunit.Abstractions;

namespace Pino.Compiler.Tests.Checking;

/// <summary>
/// Full-corpus and seeded fuzz testing for the checker: verifies every corpus file and 5,500+
/// generated/mutated inputs respect AD-002, A4/type-safety, span-bounds, sorting, and 5s hang guards.
/// </summary>
public sealed class CheckerFuzzTests(ITestOutputHelper output) {
  private static readonly string s_repositoryRoot = ParserTestHelpers.FindRepositoryRoot();
  private static readonly TimeSpan s_perInputTimeout = TimeSpan.FromSeconds(5);

  private static readonly string[] s_seedSnippets = [
    "val x = 1\nval y = x + 2\n",
    """
    union Opt[T] { Some(v T), None }
    fn unwrap(o Opt[int]) int {
      return match o {
        when Opt::Some(x) { yield x }
        when Opt::None { yield 0 }
      }
    }
    """,
    """
    enum Color { Red, Green, Blue }
    fn pick(c Color) int {
      if c is Color::Red { return 1 }
      return match c {
        when Color::Red { yield 1 }
        when Color::Green { yield 2 }
        when Color::Blue { yield 3 }
      }
    }
    """,
    """
    fn rec_a(n int) {
      if n <= 0 { return 0 }
      return rec_b(n - 1)
    }
    fn rec_b(n int) {
      return rec_a(n - 1)
    }
    """,
    """
    struct Box[T] { pub v T }
    fn make() {
      val b = Box[int] { v: 1 / 0 }
      val f = fn(x int) int { return x + b.v }
      println(f(2))
    }
    """,
    """
    fn flow(a Option[int], b Result[string, int]) {
      if a is Option::Some(x) and b is Result::Failure(e) {
        println(x + e)
      }
      for {
        val g = fn() { break }
        continue
      }
    }
    """,
    """
    val z = 1 - 1
    val bad = []int { len: -1, cap: -2 }
    for false { break }
    """,
  ];

  private static readonly string[] s_tokens = [
    "fn", "val", "var", "pub", "match", "when", "else", "if", "for", "return", "yield",
    "break", "continue", "is", "not", "and", "or", "union", "enum", "struct", "interface",
    "int", "float", "string", "bool", "rune", "void", "Option", "Result", "::", "->",
    "(", ")", "{", "}", "[", "]", ":", ",", ".", "=", "/=", "//=", "/", "//", "%", "+", "-", "*",
    "x", "y", "a", "b", "T", "Some", "None", "Success", "Failure", "0", "1", "-1", "\"s\"", "'c'", "true", "false", "\n",
  ];

  [Fact]
  public void All024CorpusFiles_CheckInvariantsWithinHangGuard() {
    string[] files = LexerCorpusTests.CorpusFiles
        .Cast<object[]>()
        .Select(static row => (string) row[0])
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    Assert.NotEmpty(files);

    var totalSw = Stopwatch.StartNew();
    TimeSpan worstTime = TimeSpan.Zero;
    string worstFile = string.Empty;

    foreach (string relativePath in files) {
      string fullPath = Path.Combine(s_repositoryRoot, relativePath);
      if (string.Equals(relativePath, "projects/c-compiler/modules/parser.pino", StringComparison.OrdinalIgnoreCase)
          && !File.Exists(fullPath)) {
        continue;
      }

      string text = File.ReadAllText(fullPath);
      var source = new SourceText(text, fullPath);

      var sw = Stopwatch.StartNew();
      ParseResult parsed = Parser.Parse(source);
      Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
      sw.Stop();

      if (sw.Elapsed > worstTime) {
        worstTime = sw.Elapsed;
        worstFile = relativePath;
      }

      Assert.True(sw.Elapsed < s_perInputTimeout, $"{relativePath} took {sw.Elapsed.TotalSeconds:F2}s (limit {s_perInputTimeout.TotalSeconds}s).");
      AssertSemanticInvariants(relativePath, compilation);
    }

    totalSw.Stop();
    output.WriteLine($"Checked {files.Length} corpus files in {totalSw.Elapsed.TotalSeconds:F2}s (worst: {worstFile} in {worstTime.TotalMilliseconds:F1}ms).");
  }

  [Fact]
  public void SeededFuzzer_FiveThousandFiveHundredInputs_HoldAllInvariants() {
    const int Count = 5500;
    var rng = new Random(0x50494E4F); // "PINO"
    var totalSw = Stopwatch.StartNew();
    TimeSpan worstTime = TimeSpan.Zero;
    int worstIndex = -1;

    for (int i = 0; i < Count; i++) {
      string code = i switch {
        < 100 => GenerateDeepProgram(i),
        _ => MutateSeed(rng, i),
      };

      var source = new SourceText(code, $"fuzz_{i}.pino");
      var sw = Stopwatch.StartNew();
      ParseResult parsed = Parser.Parse(source);
      Compilation compilation = Compilation.Create(parsed);
      sw.Stop();

      if (sw.Elapsed > worstTime) {
        worstTime = sw.Elapsed;
        worstIndex = i;
      }

      Assert.True(sw.Elapsed < s_perInputTimeout, $"fuzz_{i}.pino took {sw.Elapsed.TotalSeconds:F2}s.");
      AssertSemanticInvariants($"fuzz_{i}.pino", compilation);
    }

    totalSw.Stop();
    output.WriteLine($"Fuzzed {Count} checker inputs in {totalSw.Elapsed.TotalSeconds:F2}s (worst: fuzz_{worstIndex}.pino in {worstTime.TotalMilliseconds:F1}ms).");
  }

  private static string GenerateDeepProgram(int seed) {
    var sb = new StringBuilder();
    if (seed % 3 == 0) {
      sb.Append("struct Node { pub next Node }\n");
      sb.Append("fn cycle_a(x) { return cycle_b(x) }\n");
      sb.Append("fn cycle_b(y) { return cycle_a(y) }\n");
    }

    sb.Append("fn deep(x int) int {\n  var acc = x\n");
    int depth = 25 + (seed % 40);
    for (int d = 0; d < depth; d++) {
      sb.Append("  if acc > ").Append(d).Append(" {\n");
    }
    sb.Append("  acc += 1\n");
    for (int d = 0; d < depth; d++) {
      sb.Append("  }\n");
    }
    sb.Append("  return acc\n}\n");
    return sb.ToString();
  }

  private static string MutateSeed(Random rng, int iteration) {
    string baseText = s_seedSnippets[iteration % s_seedSnippets.Length];
    int mode = rng.Next(4);
    if (mode == 0) {
      int lineCount = rng.Next(4, 18);
      var sb = new StringBuilder();
      for (int l = 0; l < lineCount; l++) {
        int len = rng.Next(1, 10);
        for (int t = 0; t < len; t++) {
          sb.Append(s_tokens[rng.Next(s_tokens.Length)]).Append(' ');
        }
        sb.Append('\n');
      }
      return sb.ToString();
    }

    var chars = baseText.ToCharArray().ToList();
    int edits = rng.Next(1, 6);
    for (int e = 0; e < edits && chars.Count > 0; e++) {
      int pos = rng.Next(chars.Count);
      switch (rng.Next(3)) {
        case 0:
          chars.RemoveAt(pos);
          break;
        case 1:
          string ins = " " + s_tokens[rng.Next(s_tokens.Length)] + " ";
          chars.InsertRange(pos, ins);
          break;
        default:
          chars[pos] = (char) rng.Next(32, 126);
          break;
      }
    }

    return new string(chars.ToArray());
  }

  private static void AssertSemanticInvariants(string label, Compilation compilation) {
    _ = compilation.Reachability;
    _ = compilation.GenericInstantiations;

    foreach (SemanticModel model in compilation.SemanticModels) {
      _ = model.AssignedBindings;
      int textLen = model.Tree.Source.Length;
      Diagnostic? prev = null;

      foreach (Diagnostic diag in model.Diagnostics) {
        Assert.False(string.IsNullOrEmpty(diag.Code), $"{label}: empty diagnostic code.");
        Assert.InRange(diag.Span.Start, 0, textLen);
        Assert.InRange(diag.Span.End, diag.Span.Start, textLen);

        if (prev is not null) {
          bool sorted = prev.Span.Start < diag.Span.Start
              || (prev.Span.Start == diag.Span.Start && prev.Span.Length <= diag.Span.Length);
          Assert.True(sorted, $"{label}: diagnostics out of order: {prev.Code}@{prev.Span} before {diag.Code}@{diag.Span}.");
        }
        prev = diag;
      }

      var stack = new Stack<SyntaxNode>();
      stack.Push(model.Tree.Root);
      while (stack.Count > 0) {
        SyntaxNode current = stack.Pop();
        _ = model.GetDeclaredSymbol(current);
        _ = model.GetReferencedSymbol(current);
        _ = model.GetConversion(current);
        if (current is CallExpressionSyntax callNode) {
          _ = model.GetCallResolution(callNode);
        }
        _ = model.GetCaptures(current);
        _ = model.GetLoopInfo(current);
        _ = model.GetPatternBindings(current);
        _ = model.GetConstantValue(current);

        if (current is ExpressionSyntax expr) {
          TypeSymbol? exprType = model.GetType(expr);
          Assert.NotNull(exprType);
          Assert.False(exprType is TypeVariableType, $"{label}: leaked TypeVariableType on {expr.GetType().Name}.");
          Assert.False(exprType is PendingReturnType, $"{label}: leaked PendingReturnType on {expr.GetType().Name}.");
        }

        foreach (SyntaxElement elem in current.GetChildren()) {
          if (elem.IsNode) {
            stack.Push(elem.AsNode());
          }
        }
      }
    }
  }
}
