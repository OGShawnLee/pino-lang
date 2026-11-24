using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

using Pino.Compiler.Checking;
using Pino.Compiler.CodeGen.C;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.IR.Passes;
using Pino.Compiler.Parsing;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;
using Pino.Vm;

using Xunit.Abstractions;

namespace Pino.Compiler.Tests.IR;

/// <summary>
/// Differential tests comparing <c>ReVm</c> (shadow-RC on) against <c>ReC</c> (TCC + <c>-DPINO_DEBUG_RC</c>
/// + <c>PINO_FAIL_ON_LEAK=1</c>) across every <c>conformance/**/*.out</c> case and 220 deterministic
/// seeded random valid Pino programs (<c>056/design.md</c> §8).
/// </summary>
public sealed class DifferentialTests(ITestOutputHelper output) {
  private const int FuzzProgramCount = 220;

  private static readonly string s_repoRoot = ParserTestHelpers.FindRepositoryRoot();
  private static readonly string s_conformanceRoot = Path.Combine(s_repoRoot, "conformance");

  [Fact]
  public void ConformanceOutCases_ReVmAndReCProduceIdenticalStdoutExitCodeAndZeroLeaks() {
    string[] outCases = Directory.EnumerateFiles(s_conformanceRoot, "*.pino", SearchOption.AllDirectories)
        .Where(static f => File.Exists(Path.ChangeExtension(f, ".out")))
        .OrderBy(static f => f, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Assert.True(outCases.Length >= 421, $"Expected at least 421 .out conformance cases, found {outCases.Length}.");

    var failures = new ConcurrentBag<(string CasePath, string Detail)>();
    var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4) };

    Parallel.ForEach(outCases, options, fullPath => {
      string rel = Path.GetRelativePath(s_conformanceRoot, fullPath).Replace('\\', '/');
      string expectedOut = Normalize(File.ReadAllText(Path.ChangeExtension(fullPath, ".out")));
      string sourceText = File.ReadAllText(fullPath);

      string? failure = RunDifferentialCase(
          label: rel,
          source: sourceText,
          filePath: fullPath,
          useFileSystemModules: true,
          expectedStdout: expectedOut);
      if (failure is not null) {
        failures.Add((rel, failure));
      }
    });

    (string CasePath, string Detail)[] orderedFailures = failures
        .OrderBy(static f => f.CasePath, StringComparer.Ordinal)
        .ToArray();

    output.WriteLine($"Conformance differential sweep: {outCases.Length - orderedFailures.Length}/{outCases.Length} matched (ReVm == ReC, 0 leaks).");
    Assert.True(
        orderedFailures.Length == 0,
        $"Conformance differential failures ({orderedFailures.Length}/{outCases.Length}):\n"
        + string.Join("\n\n", orderedFailures.Take(10).Select(static f => f.Detail)));
  }

  [Fact]
  public void RandomizedValidPrograms_ReVmAndReCProduceIdenticalStdoutExitCodeAndZeroLeaks() {
    int[] seeds = Enumerable.Range(1, FuzzProgramCount).ToArray();
    var failures = new ConcurrentBag<(int Seed, string Detail)>();
    var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4) };

    Parallel.ForEach(seeds, options, seed => {
      string program = RandomProgramGenerator.Generate(seed);
      string? failure = RunDifferentialCase(
          label: $"seed={seed}",
          source: program,
          filePath: $"fuzz_seed_{seed}.pino",
          useFileSystemModules: false,
          expectedStdout: null);
      if (failure is not null) {
        failures.Add((seed, $"{failure}\n--- program (seed={seed}) ---\n{program}"));
      }
    });

    (int Seed, string Detail)[] orderedFailures = failures
        .OrderBy(static f => f.Seed)
        .ToArray();

    output.WriteLine($"Differential fuzzer: {FuzzProgramCount - orderedFailures.Length}/{FuzzProgramCount} programs matched (seeds 1..{FuzzProgramCount}, 0 mismatches, 0 leaks).");
    Assert.True(
        orderedFailures.Length == 0,
        $"Differential fuzzer failures ({orderedFailures.Length}/{FuzzProgramCount}):\n"
        + string.Join("\n\n====================\n\n", orderedFailures.Take(5).Select(static f => f.Detail)));
  }

  private static string? RunDifferentialCase(
      string label,
      string source,
      string filePath,
      bool useFileSystemModules,
      string? expectedStdout) {
    var src = new SourceText(source, filePath);
    var parsed = Parser.Parse(src);
    Diagnostic[] parseErrors = [.. parsed.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)];
    if (parseErrors.Length > 0) {
      return $"{label}: parse error(s):\n" + string.Join("\n", parseErrors.Select(static d => $"{d.Code}: {d.Message}"));
    }

    Compilation compilation = useFileSystemModules
        ? Compilation.Create(parsed, FileSystemModuleSource.Instance)
        : Compilation.Create(parsed);
    Diagnostic[] checkErrors = [.. compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)];
    if (checkErrors.Length > 0) {
      return $"{label}: checker error(s):\n" + string.Join("\n", checkErrors.Select(static d => $"{d.Code}: {d.Message}"));
    }

    IrModule ir = IrLowerer.Lower(compilation);
    RcInsertPass.Run(ir);
    IrVerificationResult verify = IrVerifier.Verify(ir, IrVerifyStage.PostRc);
    if (!verify.IsValid) {
      return $"{label}: PostRc IR verification failed:\n" + string.Join("\n", verify.Errors);
    }

    // 1. Run on ReVm with shadow-RC verification enabled.
    var vmHost = new BufferedVmHost();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    PinoVmResult vmResult;
    try {
      var vm = new PinoVm(ir, vmHost, new PinoVmOptions {
        VerifyRc = true,
        RootSource = src,
        CancellationToken = cts.Token,
      });
      vmResult = vm.Run();
    } catch (Exception ex) {
      return $"{label}: ReVm threw {ex.GetType().Name}: {ex.Message}";
    }

    if (vmResult.RcError is not null) {
      return $"{label}: ReVm shadow-RC error: {vmResult.RcError}";
    }

    if (vmResult.ExitCode != 0) {
      return $"{label}: ReVm exited with code {vmResult.ExitCode}.\n--- vm stderr ---\n{vmHost.StdErr}";
    }

    if (vmResult.LeakReport is { IsClean: false } leaky) {
      return $"{label}: ReVm shadow-RC leak:\n{leaky.Format()}";
    }

    // 2. Emit C99, compile with TCC + -DPINO_DEBUG_RC, and run with PINO_FAIL_ON_LEAK=1.
    string cSource = CEmitter.Emit(ir, compilation);
    string tempDir = Path.Combine(Path.GetTempPath(), "pino-re-diff-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    try {
      string exePath = Path.Combine(tempDir, OperatingSystem.IsWindows() ? "diff_case.exe" : "diff_case");
      CBuildResult build = CCompilerDriver.Build(new CBuildOptions(
          CSource: cSource,
          OutputExecutablePath: exePath,
          Release: false,
          DebugRc: true,
          RepoRoot: s_repoRoot));
      if (!build.Succeeded) {
        return $"{label}: ReC build failed ({build.CompilerUsed}):\n{build.Stderr}\n{build.Stdout}";
      }

      CExecResult exec = CCompilerDriver.RunExecutable(
          exePath,
          workingDirectory: tempDir,
          failOnLeak: true,
          closeStdin: true,
          timeout: TimeSpan.FromSeconds(10));
      if (exec.TimedOut) {
        return $"{label}: ReC timed out.";
      }

      if (exec.ExitCode != vmResult.ExitCode) {
        return $"{label}: exit code mismatch (ReVm={vmResult.ExitCode}, ReC={exec.ExitCode}).\n--- ReC stderr ---\n{exec.Stderr}";
      }

      if (exec.Stderr.Contains("LEAK:", StringComparison.Ordinal)) {
        return $"{label}: ReC reported RC leak:\n{exec.Stderr}";
      }

      string vmOut = Normalize(vmHost.StdOut);
      string cOut = Normalize(exec.Stdout);
      if (!string.Equals(vmOut, cOut, StringComparison.Ordinal)) {
        return $"{label}: stdout mismatch between ReVm and ReC.\n--- ReVm ---\n{vmOut}\n--- ReC ---\n{cOut}";
      }

      if (expectedStdout is not null && !string.Equals(vmOut, expectedStdout, StringComparison.Ordinal)) {
        return $"{label}: stdout mismatch against .out.\n--- expected ---\n{expectedStdout}\n--- actual ---\n{vmOut}";
      }

      return null;
    } finally {
      TryDeleteDir(tempDir);
    }
  }

  private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

  private static void TryDeleteDir(string path) {
    try {
      Directory.Delete(path, recursive: true);
    } catch (IOException) {
    } catch (UnauthorizedAccessException) {
    }
  }

  /// <summary>
  /// Deterministic generator of small, valid, terminating Pino programs covering arithmetic, loops,
  /// nested structs, vectors, maps, sets, pools, match/is, ?/or, closures (including mutable captures),
  /// mut parameters, and strings.
  /// </summary>
  private static class RandomProgramGenerator {
    private static readonly string[] s_words = ["alpha", "beta", "gamma", "delta", "echo", "fox", "kite", "luna"];

    public static string Generate(int seed) {
      var rng = new Random(seed * 7919 + 1337);
      var sb = new StringBuilder(2048);

      bool includeRecursiveUnion = seed % 3 == 0;

      // Common types & helpers used by the generated blocks.
      sb.AppendLine("enum Mood { Calm, Alert, Bold }");
      sb.AppendLine();
      sb.AppendLine("struct Item {");
      sb.AppendLine("  pub id int");
      sb.AppendLine("  pub mut score int");
      sb.AppendLine("  pub mut tag string");
      sb.AppendLine();
      sb.AppendLine("  mut fn add_score(d int) {");
      sb.AppendLine("    self:score += d");
      sb.AppendLine("  }");
      sb.AppendLine();
      sb.AppendLine("  fn summary() string {");
      sb.AppendLine("    return \"$id:$score:$tag\"");
      sb.AppendLine("  }");
      sb.AppendLine("}");
      sb.AppendLine();
      sb.AppendLine("struct Bag {");
      sb.AppendLine("  pub mut items []Item");
      sb.AppendLine("  pub mut name string");
      sb.AppendLine();
      sb.AppendLine("  mut fn push_item(it Item) {");
      sb.AppendLine("    self:items:push(it)");
      sb.AppendLine("  }");
      sb.AppendLine("}");
      sb.AppendLine();
      sb.AppendLine("union Payload {");
      sb.AppendLine("  Num(n int)");
      sb.AppendLine("  Text(s string)");
      sb.AppendLine("  Empty");
      sb.AppendLine("}");
      sb.AppendLine();

      if (includeRecursiveUnion) {
        sb.AppendLine("union Chain {");
        sb.AppendLine("  Link(head int, tail Chain)");
        sb.AppendLine("  Nil");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("fn sum_chain(c Chain) int {");
        sb.AppendLine("  return match c {");
        sb.AppendLine("    when Chain::Link(h, rest) -> h + sum_chain(rest)");
        sb.AppendLine("    when Chain::Nil -> 0");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();
      }

      sb.AppendLine("fn safe_div(a int, b int) Result[int, string] {");
      sb.AppendLine("  if b == 0 {");
      sb.AppendLine("    return Result::Failure(\"div0\")");
      sb.AppendLine("  }");
      sb.AppendLine("  return Result::Success(a // b)");
      sb.AppendLine("}");
      sb.AppendLine();
      sb.AppendLine("fn chain_div(a int, b int, c int) Result[int, string] {");
      sb.AppendLine("  val x = safe_div(a, b)?");
      sb.AppendLine("  val y = safe_div(x + 10, c)?");
      sb.AppendLine("  return Result::Success(y)");
      sb.AppendLine("}");
      sb.AppendLine();
      sb.AppendLine("fn pick_opt(xs []int, idx int) Option[int] {");
      sb.AppendLine("  if idx >= 0 and idx < xs:len {");
      sb.AppendLine("    return Option::Some(xs[idx])");
      sb.AppendLine("  }");
      sb.AppendLine("  return Option::None");
      sb.AppendLine("}");
      sb.AppendLine();
      sb.AppendLine("fn bump_int(mut x int, d int) {");
      sb.AppendLine("  x += d");
      sb.AppendLine("}");
      sb.AppendLine();
      sb.AppendLine("fn swap_item_scores(mut a Item, mut b Item) {");
      sb.AppendLine("  val tmp = a:score");
      sb.AppendLine("  a:score = b:score");
      sb.AppendLine("  b:score = tmp");
      sb.AppendLine("}");
      sb.AppendLine();
      sb.AppendLine("fn add_from_copy(mut dst []int, src []int) {");
      sb.AppendLine("  for x in src {");
      sb.AppendLine("    dst:push(x + 1)");
      sb.AppendLine("  }");
      sb.AppendLine("}");
      sb.AppendLine();
      sb.AppendLine("fn apply_twice(x int, f fn (int) -> int) int {");
      sb.AppendLine("  return f(f(x))");
      sb.AppendLine("}");
      sb.AppendLine();

      sb.AppendLine("fn main() {");

      // Pick 5 distinct feature blocks per seed, guaranteeing every feature gets thorough coverage.
      int[] features = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
      for (int i = features.Length - 1; i > 0; i--) {
        int j = rng.Next(i + 1);
        (features[i], features[j]) = (features[j], features[i]);
      }

      int blockCount = 5 + (seed % 3); // 5..7 blocks per program
      for (int i = 0; i < blockCount; i++) {
        int feat = features[i];
        sb.AppendLine("  {");
        switch (feat) {
          case 0:
            EmitArithmeticAndLoops(sb, rng);
            break;
          case 1:
            EmitNestedStructsAndCoW(sb, rng);
            break;
          case 2:
            EmitVectorsAndCallbacks(sb, rng);
            break;
          case 3:
            EmitMaps(sb, rng);
            break;
          case 4:
            EmitSets(sb, rng);
            break;
          case 5:
            EmitPools(sb, rng);
            break;
          case 6:
            EmitMatchAndIs(sb, rng, includeRecursiveUnion);
            break;
          case 7:
            EmitErrorHandling(sb, rng);
            break;
          case 8:
            EmitClosures(sb, rng);
            break;
          case 9:
            EmitMutCalls(sb, rng);
            break;
          case 10:
            EmitStringsAndRunes(sb, rng);
            break;
        }

        sb.AppendLine("  }");
      }

      sb.AppendLine("}");
      return sb.ToString();
    }

    private static void EmitArithmeticAndLoops(StringBuilder sb, Random rng) {
      int a = rng.Next(-40, 41);
      int b = rng.Next(1, 10) * (rng.Next(2) == 0 ? 1 : -1);
      int limit = rng.Next(3, 9);
      double f1 = rng.Next(-20, 21) / 2.0;
      double f2 = rng.Next(1, 9) / 2.0;
      string f1Str = f1.ToString("0.0", CultureInfo.InvariantCulture);
      string f2Str = f2.ToString("0.0", CultureInfo.InvariantCulture);

      sb.AppendLine($"    var acc = {a}");
      sb.AppendLine($"    val q = {a} // {b}");
      sb.AppendLine($"    val r = {a} % {b}");
      sb.AppendLine($"    val flt = ({f1Str} + {f2Str}) * 2.0");
      sb.AppendLine($"    for i in {limit} {{");
      sb.AppendLine("      if i == 2 {");
      sb.AppendLine("        continue");
      sb.AppendLine("      }");
      sb.AppendLine("      acc += i * 3");
      sb.AppendLine("      if acc > 60 {");
      sb.AppendLine("        break");
      sb.AppendLine("      }");
      sb.AppendLine("    }");
      sb.AppendLine("    val flag = (acc > 0 and q != 999) or r == 0");
      sb.AppendLine("    println(\"arith\", acc, q, r, flt, flag)");
    }

    private static void EmitNestedStructsAndCoW(StringBuilder sb, Random rng) {
      int s1 = rng.Next(1, 20);
      int s2 = rng.Next(1, 20);
      int delta = rng.Next(1, 10);
      string w1 = PickWord(rng);
      string w2 = PickWord(rng);

      sb.AppendLine($"    var bag = Bag {{ items: [Item {{ id: 1, score: {s1}, tag: \"{w1}\" }}], name: \"{w2}\" }}");
      sb.AppendLine("    val snap = bag");
      sb.AppendLine($"    bag:push_item(Item {{ id: 2, score: {s2}, tag: \"extra\" }})");
      sb.AppendLine($"    bag:items[0]:add_score({delta})");
      sb.AppendLine("    println(\"struct\", bag:items:len, snap:items:len, bag:items[0]:summary(), snap:items[0]:summary())");
    }

    private static void EmitVectorsAndCallbacks(StringBuilder sb, Random rng) {
      int n = rng.Next(3, 7);
      var elems = new int[n];
      for (int i = 0; i < n; i++) {
        elems[i] = rng.Next(-10, 20);
      }

      int threshold = rng.Next(-2, 8);
      int mult = rng.Next(2, 5);
      int pushed = rng.Next(10, 30);

      sb.AppendLine($"    val mut v = [{string.Join(", ", elems)}]");
      sb.AppendLine("    val snap = v");
      sb.AppendLine($"    v:push({pushed})");
      sb.AppendLine("    val popped = v:pop()");
      sb.AppendLine("    v[0] = v[0] + 1");
      sb.AppendLine($"    val filtered = v:filter(it > {threshold}):map(it * {mult})");
      sb.AppendLine($"    val has_big = v:any(it > {threshold})");
      sb.AppendLine("    val all_ok = v:all(it > -100)");
      sb.AppendLine($"    val cnt = v:count(it >= {threshold})");
      sb.AppendLine("    println(\"vec\", v, snap, popped, filtered:join(\"-\"), has_big, all_ok, cnt)");
    }

    private static void EmitMaps(StringBuilder sb, Random rng) {
      string k1 = PickWord(rng);
      string k2 = PickWord(rng) + "_2";
      string k3 = PickWord(rng) + "_3";
      int v1 = rng.Next(1, 50);
      int v2 = rng.Next(1, 50);
      int v3 = rng.Next(1, 50);

      sb.AppendLine($"    val mut m = map[string, int] {{ \"{k1}\" -> {v1}, \"{k2}\" -> {v2} }}");
      sb.AppendLine("    val snap = m");
      sb.AppendLine($"    m[\"{k3}\"] = {v3}");
      sb.AppendLine($"    m[\"{k1}\"] = {v1 + 5}");
      sb.AppendLine($"    val rem = m:remove(\"{k2}\")");
      sb.AppendLine("    var sum = 0");
      sb.AppendLine("    for k, v in m {");
      sb.AppendLine("      sum += v + k:len");
      sb.AppendLine("    }");
      sb.AppendLine($"    println(\"map\", m, snap, rem, \"{k1}\" in m, \"{k2}\" in m, sum, m:keys(), m:values())");
    }

    private static void EmitSets(StringBuilder sb, Random rng) {
      int a = rng.Next(1, 10);
      int b = a + rng.Next(1, 10);
      int c = b + rng.Next(1, 10);
      int d = c + rng.Next(1, 10);

      sb.AppendLine($"    val mut s = set[int] {{ {a}, {b}, {c} }}");
      sb.AppendLine("    val snap = s");
      sb.AppendLine($"    s:add({d})");
      sb.AppendLine($"    s:add({a})");
      sb.AppendLine($"    s:remove({b})");
      sb.AppendLine("    var total = 0");
      sb.AppendLine("    for x in s {");
      sb.AppendLine("      total += x");
      sb.AppendLine("    }");
      sb.AppendLine($"    println(\"set\", s, snap, {a} in s, {b} in s, s:any(it > {c}), s:all(it > 0), total)");
    }

    private static void EmitPools(StringBuilder sb, Random rng) {
      int s1 = rng.Next(1, 30);
      int s2 = rng.Next(1, 30);
      int s3 = rng.Next(1, 30);
      int s4 = rng.Next(1, 30);
      string w1 = PickWord(rng);
      string w2 = PickWord(rng);

      sb.AppendLine("    val mut p = Pool[Item]");
      sb.AppendLine($"    val h1 = p:add(Item {{ id: 1, score: {s1}, tag: \"{w1}\" }})");
      sb.AppendLine($"    val h2 = p:add(Item {{ id: 2, score: {s2}, tag: \"{w2}\" }})");
      sb.AppendLine($"    val h3 = p:add(Item {{ id: 3, score: {s3}, tag: \"mid\" }})");
      sb.AppendLine("    val snap = p");
      sb.AppendLine("    val removed = p:remove(h2)");
      sb.AppendLine($"    val h4 = p:add(Item {{ id: 4, score: {s4}, tag: \"reused\" }})");
      sb.AppendLine("    p[h1]:add_score(7)");
      sb.AppendLine("    var sum = 0");
      sb.AppendLine("    for h, item in p {");
      sb.AppendLine("      if h in p {");
      sb.AppendLine("        sum += item:score");
      sb.AppendLine("      }");
      sb.AppendLine("    }");
      sb.AppendLine("    println(\"pool\", p:len, snap:len, h2 in p, h2 in snap, removed, p:get(h4), sum, p:handles())");
      sb.AppendLine("    p:clear()");
      sb.AppendLine("    val h5 = p:add(Item { id: 5, score: 99, tag: \"fresh\" })");
      sb.AppendLine("    println(\"pool_clear\", p:len, h1 in p, h5 in p, p)");
    }

    private static void EmitMatchAndIs(StringBuilder sb, Random rng, bool includeRecursiveUnion) {
      int choice = rng.Next(3);
      int num = rng.Next(1, 50);
      string word = PickWord(rng);

      string init = choice switch {
        0 => $"Payload::Num({num})",
        1 => $"Payload::Text(\"{word}\")",
        _ => "Payload::Empty",
      };

      sb.AppendLine($"    val p = {init}");
      sb.AppendLine("    val desc = match p {");
      sb.AppendLine("      when Payload::Num(n) -> \"num:\" + str(n * 2)");
      sb.AppendLine("      when Payload::Text(s) -> \"txt:\" + s");
      sb.AppendLine("      when Payload::Empty -> \"empty\"");
      sb.AppendLine("    }");
      sb.AppendLine("    var is_big = false");
      sb.AppendLine("    if p is Payload::Num(n) and n > 20 {");
      sb.AppendLine("      is_big = true");
      sb.AppendLine("    }");
      int moodIdx = rng.Next(3);
      string mood = moodIdx switch { 0 => "Mood::Calm", 1 => "Mood::Alert", _ => "Mood::Bold" };
      sb.AppendLine($"    val m = {mood}");
      sb.AppendLine("    val mv = match m {");
      sb.AppendLine("      when Mood::Calm -> 1");
      sb.AppendLine("      when Mood::Alert -> 2");
      sb.AppendLine("      when Mood::Bold -> 3");
      sb.AppendLine("    }");
      sb.AppendLine("    println(\"match\", desc, is_big, m, mv)");

      if (includeRecursiveUnion) {
        int c1 = rng.Next(1, 10);
        int c2 = rng.Next(1, 10);
        int c3 = rng.Next(1, 10);
        sb.AppendLine($"    val ch = Chain::Link({c1}, Chain::Link({c2}, Chain::Link({c3}, Chain::Nil)))");
        sb.AppendLine("    println(\"chain\", sum_chain(ch))");
      }
    }

    private static void EmitErrorHandling(StringBuilder sb, Random rng) {
      int a = rng.Next(10, 100);
      int b = rng.Next(3) == 0 ? 0 : rng.Next(1, 6);
      int c = rng.Next(3) == 0 ? 0 : rng.Next(1, 6);
      int idx = rng.Next(-1, 5);

      sb.AppendLine($"    val r = chain_div({a}, {b}, {c}) or {{ yield -1 }}");
      sb.AppendLine($"    val o = pick_opt([10, 20, 30], {idx}) or {{ yield -99 }}");
      sb.AppendLine("    println(\"err\", r, o)");
    }

    private static void EmitClosures(StringBuilder sb, Random rng) {
      int start = rng.Next(0, 15);
      int step = rng.Next(1, 7);
      int baseVal = rng.Next(1, 10);

      sb.AppendLine($"    val k = {baseVal}");
      sb.AppendLine($"    var counter = {start}");
      sb.AppendLine("    val mut log = []int");
      sb.AppendLine("    val inc = fn (x int) {");
      sb.AppendLine($"      counter += x + {step}");
      sb.AppendLine("      log:push(counter)");
      sb.AppendLine("      return counter + k");
      sb.AppendLine("    }");
      sb.AppendLine("    val a = inc(2)");
      sb.AppendLine("    val b = apply_twice(3, fn (n int) -> n + k)");
      sb.AppendLine("    val c = inc(4)");
      sb.AppendLine("    println(\"clos\", a, b, c, counter, log)");
    }

    private static void EmitMutCalls(StringBuilder sb, Random rng) {
      int start = rng.Next(1, 20);
      int delta = rng.Next(1, 10);
      int s1 = rng.Next(1, 50);
      int s2 = rng.Next(1, 50);

      sb.AppendLine($"    var n = {start}");
      sb.AppendLine($"    bump_int(mut n, {delta})");
      sb.AppendLine($"    val mut items = [Item {{ id: 1, score: {s1}, tag: \"a\" }}, Item {{ id: 2, score: {s2}, tag: \"b\" }}]");
      sb.AppendLine("    swap_item_scores(mut items[0], mut items[1])");
      sb.AppendLine("    val mut nums = [1, 2]");
      sb.AppendLine("    add_from_copy(mut nums, nums)");
      sb.AppendLine("    println(\"mut\", n, items[0]:score, items[1]:score, nums)");
    }

    private static void EmitStringsAndRunes(StringBuilder sb, Random rng) {
      string w1 = PickWord(rng);
      string w2 = PickWord(rng);
      sb.AppendLine($"    val raw = \"  {w1}-{w2}  \"");
      sb.AppendLine("    val tr = raw:trim()");
      sb.AppendLine("    val up = tr:upper()");
      sb.AppendLine("    val sub = tr:substring(0, 3)");
      sb.AppendLine("    val parts = tr:split(\"-\")");
      sb.AppendLine("    val rep = tr:replace(\"-\", \"_\")");
      sb.AppendLine("    var rcount = 0");
      sb.AppendLine("    for ch in sub {");
      sb.AppendLine("      rcount += 1");
      sb.AppendLine("    }");
      sb.AppendLine("    println(\"str\", tr, up, sub, parts, rep, tr:index_of(\"-\"), rcount, sub + '!')");
    }

    private static string PickWord(Random rng) => s_words[rng.Next(s_words.Length)];
  }
}
