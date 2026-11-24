using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.Checking;

/// <summary>
/// Conformance verification against <c>conformance/</c>: every <c>.out</c> case compiles without errors
/// or leaked internal types, and every <c>.err</c> case deterministically hits parser rejection,
/// runtime-only expectation, or a matching <c>PN3xxx</c> checker diagnostic (<c>OQ-42</c>).
/// </summary>
public sealed class CheckerConformanceTests {
  private static readonly string s_repoRoot = ParserTestHelpers.FindRepositoryRoot();
  private static readonly string s_conformanceRoot = Path.Combine(s_repoRoot, "conformance");

  /// <summary>
  /// Conformance <c>.err</c> tests whose failure occurs at runtime rather than static type checking
  /// (explicit panics, missing map keys, runtime zero-division/modulo, out-of-range indexing/substring,
  /// invalid regex pattern). Verified non-stale by <see cref="AllowlistsAreNonStale"/>.
  /// </summary>
  public static readonly IReadOnlyDictionary<string, string> RuntimeErrCases =
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        ["errors/main-bubble.pino"] = "Runtime failure bubbled out of main via `?`.",
        ["errors/main-failure.pino"] = "Runtime failure returned from Result main.",
        ["errors/main-none.pino"] = "Runtime None returned from Option main.",
        ["errors/or-panic.pino"] = "Explicit runtime panic inside `or { ... }` recovery.",
        ["errors/panic-as-value.pino"] = "Explicit runtime `panic(...)` expression.",
        ["errors/panic-exit-code.pino"] = "Explicit runtime `panic(...)` exit-code check.",
        ["errors/panic-halts.pino"] = "Explicit runtime `panic(...)` halting execution.",
        ["errors/panic-nested.pino"] = "Explicit runtime `panic(...)` from nested call.",
        ["functions/err-mut-same-slot-runtime.pino"] = "Runtime D-184 exclusivity panic on aliased dynamic mut slots.",
        ["maps/err-missing-key.pino"] = "Runtime lookup of a missing map key.",
        ["pools/err-stale-index.pino"] = "Runtime panic on indexing a Pool[T] with a stale Handle[T].",
        ["stdlib-regex/err-invalid-pattern.pino"] = "Runtime regex compilation failure on invalid pattern string.",
        ["stdlib-string/err-substring-out-of-range.pino"] = "Runtime substring bounds violation on non-constant indices.",
        ["stdlib-vector/err-swap-out-of-range.pino"] = "Runtime vector swap bounds check on out-of-range index.",
        ["types/err-runtime-division-by-zero-float.pino"] = "Runtime float division by non-constant zero.",
        ["types/err-runtime-division-by-zero-int.pino"] = "Runtime int division by non-constant zero.",
        ["types/err-runtime-float-modulo-by-zero.pino"] = "Runtime float modulo by non-constant zero.",
        ["types/err-runtime-floor-division-by-zero.pino"] = "Runtime floor division by non-constant zero.",
        ["types/err-runtime-modulo-by-zero.pino"] = "Runtime modulo by non-constant zero.",
        ["types/err-runtime-int-min-div-overflow.pino"] = "Runtime INT64_MIN // -1 overflow (TYPE-E6).",
        ["vectors/err-index-out-of-range.pino"] = "Runtime vector bounds check on out-of-range index.",
      };

  public static TheoryData<string> PositiveCases() {
    var data = new TheoryData<string>();
    foreach (string file in Directory.EnumerateFiles(s_conformanceRoot, "*.pino", SearchOption.AllDirectories)
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      if (File.Exists(Path.ChangeExtension(file, ".out"))) {
        data.Add(ToConformanceRelativePath(file));
      }
    }
    return data;
  }

  public static TheoryData<string> NegativeCases() {
    var data = new TheoryData<string>();
    foreach (string file in Directory.EnumerateFiles(s_conformanceRoot, "*.pino", SearchOption.AllDirectories)
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      if (File.Exists(Path.ChangeExtension(file, ".err"))) {
        data.Add(ToConformanceRelativePath(file));
      }
    }
    return data;
  }

  [Theory]
  [MemberData(nameof(PositiveCases))]
  public void PositiveConformanceCases_CheckCleanWithResolvedExpressionTypes(string relativePath) {
    string fullPath = Path.Combine(s_conformanceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    string content = File.ReadAllText(fullPath);

    ParseResult parsed = Parser.Parse(new SourceText(content, fullPath));
    Assert.Empty(parsed.Diagnostics.Where(static d => d.Severity != DiagnosticSeverity.Hint));

    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
    Diagnostic[] errors = compilation.Diagnostics
        .Where(static d => d.Severity == DiagnosticSeverity.Error)
        .ToArray();
    Assert.True(
        errors.Length == 0,
        $"{relativePath} produced unexpected error(s): {string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"))}");

    foreach (SemanticModel model in compilation.SemanticModels) {
      AssertNoLeakedInternalTypes(relativePath, model);
    }
  }

  [Theory]
  [MemberData(nameof(NegativeCases))]
  public void NegativeConformanceCases_MatchExpectedPhaseAndDiagnostics(string relativePath) {
    string fullPath = Path.Combine(s_conformanceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    string errFullPath = Path.ChangeExtension(fullPath, ".err");
    string content = File.ReadAllText(fullPath);
    string expectedErr = File.ReadAllText(errFullPath).Trim();

    ParseResult parsed = Parser.Parse(new SourceText(content, fullPath));
    Diagnostic[] lexOrParseErrors = parsed.Diagnostics
        .Where(static d => d.Severity == DiagnosticSeverity.Error)
        .ToArray();
    if (lexOrParseErrors.Length > 0) {
      Assert.False(RuntimeErrCases.ContainsKey(relativePath), $"{relativePath} failed to parse but is listed in RuntimeErrCases.");
      bool matchesParseErrText = lexOrParseErrors.Any(d =>
          (d.Message + "\n" + d.Help).Contains(expectedErr, StringComparison.Ordinal));
      Assert.True(
          matchesParseErrText,
          $"{relativePath}: lex/parse diagnostic(s) [{string.Join(" | ", lexOrParseErrors.Select(d => $"{d.Code}: {d.Message} (help: {d.Help})"))}] do not contain .err text '{expectedErr}'.");
      return;
    }

    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
    Diagnostic[] checkerErrors = compilation.Diagnostics
        .Where(static d => d.Severity == DiagnosticSeverity.Error && d.Code.StartsWith("PN3", StringComparison.Ordinal))
        .ToArray();

    if (RuntimeErrCases.ContainsKey(relativePath)) {
      Assert.True(
          checkerErrors.Length == 0,
          $"Runtime conformance case {relativePath} produced static checker error(s): {string.Join("; ", checkerErrors.Select(e => $"{e.Code}: {e.Message}"))}");
      return;
    }

    Assert.True(
        checkerErrors.Length > 0,
        $"Expected checker error for {relativePath} (conformance .err: '{expectedErr}'), but got none.");

    bool matchesErrText = checkerErrors.Any(d =>
        (d.Message + "\n" + d.Help).Contains(expectedErr, StringComparison.Ordinal));
    Assert.True(
        matchesErrText,
        $"{relativePath}: checker diagnostic(s) [{string.Join(" | ", checkerErrors.Select(d => $"{d.Code}: {d.Message} (help: {d.Help})"))}] do not contain .err text '{expectedErr}'.");
  }

  [Fact]
  public void AllowlistsAreNonStale() {
    var allNegative = new HashSet<string>(
        Directory.EnumerateFiles(s_conformanceRoot, "*.pino", SearchOption.AllDirectories)
            .Where(static f => File.Exists(Path.ChangeExtension(f, ".err")))
            .Select(ToConformanceRelativePath),
        StringComparer.OrdinalIgnoreCase);

    foreach ((string runtimeCase, string reason) in RuntimeErrCases) {
      Assert.True(allNegative.Contains(runtimeCase), $"Stale RuntimeErrCases entry: {runtimeCase}");
      Assert.False(string.IsNullOrWhiteSpace(reason), $"Missing reason for RuntimeErrCases entry: {runtimeCase}");
    }
  }

  private static string ToConformanceRelativePath(string fullPath) {
    string rel = Path.GetRelativePath(s_conformanceRoot, fullPath);
    return rel.Replace('\\', '/');
  }

  private static void AssertNoLeakedInternalTypes(string relativePath, SemanticModel model) {
    var stack = new Stack<SyntaxNode>();
    stack.Push(model.Tree.Root);
    while (stack.Count > 0) {
      SyntaxNode current = stack.Pop();
      if (current is ExpressionSyntax expr) {
        TypeSymbol? type = model.GetType(expr);
        Assert.NotNull(type);
        Assert.False(type is TypeVariableType, $"{relativePath}: leaked TypeVariableType on {expr.GetType().Name}.");
        Assert.False(type is PendingReturnType, $"{relativePath}: leaked PendingReturnType on {expr.GetType().Name}.");
      }

      foreach (SyntaxElement elem in current.GetChildren()) {
        if (elem.IsNode) {
          stack.Push(elem.AsNode());
        }
      }
    }
  }
}
