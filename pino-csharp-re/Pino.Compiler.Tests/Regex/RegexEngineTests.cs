using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

using Pino.Compiler.CodeGen.C;
using Pino.Vm.Regex;

namespace Pino.Compiler.Tests.RegexEngine;

/// <summary>
/// Unit tests, differential fuzzer (C# vs C vs .NET ECMAScript oracle), and linear-time guarantees
/// for Pino's regex engine (ticket 086, D-190).
/// </summary>
public sealed class RegexEngineTests : IClassFixture<RegexEngineTests.HarnessFixture> {
  private readonly HarnessFixture _fixture;
  private readonly string _work;

  public RegexEngineTests(HarnessFixture fixture) {
    _fixture = fixture;
    _work = fixture.WorkDir;
  }

  public sealed class HarnessFixture : IDisposable {
    public string WorkDir { get; } = Path.Combine(Path.GetTempPath(), "pino-regex-tests-" + Guid.NewGuid().ToString("N"));

    private readonly Lazy<string> _harnessExe;

    public HarnessFixture() {
      Directory.CreateDirectory(WorkDir);
      _harnessExe = new Lazy<string>(BuildHarness);
    }

    public string HarnessExe => _harnessExe.Value;

    public void Dispose() {
      try {
        Directory.Delete(WorkDir, recursive: true);
      } catch (IOException) {
      } catch (UnauthorizedAccessException) {
      }
    }

    private string BuildHarness() {
      string exe = Path.Combine(WorkDir, OperatingSystem.IsWindows() ? "re_harness.exe" : "re_harness");
      string root = CCompilerDriver.FindRepositoryRoot();
      string runtimeRe = Path.Combine(root, "runtime-re");
      string harnessC = Path.Combine(WorkDir, "re_harness.c");
      File.WriteAllText(harnessC, HarnessSource, CEmitter.Utf8NoBom);

      string? cc = CCompilerDriver.FindReleaseCompiler();
      string[] sources = [
        harnessC,
        Path.Combine(runtimeRe, "pino_alloc.c"),
        Path.Combine(runtimeRe, "pino_str.c"),
        Path.Combine(runtimeRe, "pino_io.c"),
        Path.Combine(runtimeRe, "pino_regex.c"),
      ];

      if (cc is not null) {
        List<string> args = ["-O2", "-std=c99", "-Wall", "-Wextra", "-Werror", "-DPINO_DEBUG_RC", $"-I{runtimeRe}", .. sources, "-lm", "-o", exe];
        CExecResult build = CCompilerDriver.RunProcess(cc, args, root, closeStdin: true, timeout: TimeSpan.FromMinutes(2));
        Assert.True(build.ExitCode == 0, $"Harness gcc build failed:\n{build.Stderr}");
        return exe;
      }

      string? tcc = CCompilerDriver.FindBundledTcc(root);
      Assert.NotNull(tcc);
      List<string> tccArgs = ["-DPINO_DEBUG_RC", $"-I{runtimeRe}", .. sources];
      if (!OperatingSystem.IsWindows()) {
        tccArgs.Add("-lm");
      }

      tccArgs.AddRange(["-o", exe]);
      CExecResult tccBuild = CCompilerDriver.RunProcess(tcc!, tccArgs, root, closeStdin: true, timeout: TimeSpan.FromMinutes(1));
      Assert.True(tccBuild.ExitCode == 0, $"Harness tcc build failed:\n{tccBuild.Stderr}");
      return exe;
    }
  }

  private string BuildCHarness() => _fixture.HarnessExe;

  [Fact]
  public void InvalidPatterns_ReportExactErrorOnBothEngines() {
    (string Pattern, string Expected)[] cases = [
      ("[", "Invalid pattern '[': missing ]."),
      ("[abc", "Invalid pattern '[abc': missing ]."),
      ("(abc", "Invalid pattern '(abc': missing )."),
      ("(?:", "Invalid pattern '(?:': missing )."),
      ("abc)", "Invalid pattern 'abc)': unexpected )."),
      ("*", "Invalid pattern '*': nothing to repeat before '*'."),
      ("+a", "Invalid pattern '+a': nothing to repeat before '+'."),
      ("?a", "Invalid pattern '?a': nothing to repeat before '?'."),
      ("a**", "Invalid pattern 'a**': nothing to repeat before '*'."),
      ("a*??", "Invalid pattern 'a*??': nothing to repeat before '?'."),
      ("(?i)*", "Invalid pattern '(?i)*': nothing to repeat before '*'."),
      ("{2}", "Invalid pattern '{2}': nothing to repeat before '{2}'."),
      ("\\q", "Invalid pattern '\\q': invalid escape '\\q'."),
      ("\\", "Invalid pattern '\\': invalid escape '\\'."),
      ("\\x4", "Invalid pattern '\\x4': invalid escape '\\x'."),
      ("\\xZZ", "Invalid pattern '\\xZZ': invalid escape '\\x'."),
      ("\\x{}", "Invalid pattern '\\x{}': invalid escape '\\x'."),
      ("\\x{110000}", "Invalid pattern '\\x{110000}': invalid escape '\\x'."),
      ("\\x{D800}", "Invalid pattern '\\x{D800}': invalid escape '\\x'."),
      ("a{}", "Invalid pattern 'a{}': invalid repetition count {}."),
      ("a{abc}", "Invalid pattern 'a{abc}': invalid repetition count {abc}."),
      ("a{5,2}", "Invalid pattern 'a{5,2}': invalid repetition count {5,2}."),
      ("a{,2}", "Invalid pattern 'a{,2}': invalid repetition count {,2}."),
      ("a{1001}", "Invalid pattern 'a{1001}': repetition count above 1000."),
      ("a{0,1001}", "Invalid pattern 'a{0,1001}': repetition count above 1000."),
      ("a{99999999999}", "Invalid pattern 'a{99999999999}': repetition count above 1000."),
      ("[z-a]", "Invalid pattern '[z-a]': invalid character range z-a."),
      ("[9-0]", "Invalid pattern '[9-0]': invalid character range 9-0."),
      ("(a)\\1", "Invalid pattern '(a)\\1': backreferences like '\\1' aren't supported; Pino's regex always runs in linear time."),
      ("\\9", "Invalid pattern '\\9': backreferences like '\\9' aren't supported; Pino's regex always runs in linear time."),
      ("(?=a)", "Invalid pattern '(?=a)': lookaround like '(?=' isn't supported; Pino's regex always runs in linear time."),
      ("(?!a)", "Invalid pattern '(?!a)': lookaround like '(?!' isn't supported; Pino's regex always runs in linear time."),
      ("(?<=a)", "Invalid pattern '(?<=a)': lookaround like '(?<=' isn't supported; Pino's regex always runs in linear time."),
      ("(?<!a)", "Invalid pattern '(?<!a)': lookaround like '(?<!' isn't supported; Pino's regex always runs in linear time."),
      ("(?x)a", "Invalid pattern '(?x)a': unknown flag 'x'."),
      ("(?i-u)a", "Invalid pattern '(?i-u)a': unknown flag 'u'."),
    ];

    foreach ((string pattern, string expectedMessage) in cases) {
      PinoRegexParseException ex = Assert.Throws<PinoRegexParseException>(() => PinoRegex.Compile(pattern));
      Assert.Equal(expectedMessage, ex.Message);
    }

    string harnessExe = BuildCHarness();
    string inputPath = Path.Combine(_work, "err-in.bin");
    string outputPath = Path.Combine(_work, "err-out.bin");
    using (var bw = new BinaryWriter(File.Create(inputPath), Encoding.UTF8)) {
      bw.Write(cases.Length);
      foreach ((string pattern, _) in cases) {
        byte[] patBytes = Encoding.UTF8.GetBytes(pattern);
        bw.Write(patBytes.Length);
        bw.Write(patBytes);
        bw.Write(0);
        bw.Write(0);
      }
    }

    CExecResult run = CCompilerDriver.RunProcess(harnessExe, [inputPath, outputPath], _work, failOnLeak: true, closeStdin: true);
    Assert.True(run.ExitCode == 0, $"C harness failed: {run.Stderr}");

    using var br = new BinaryReader(File.OpenRead(outputPath), Encoding.UTF8);
    foreach ((string pattern, string expectedMessage) in cases) {
      byte status = br.ReadByte();
      Assert.True(status == 0, $"Expected compile error in C for '{pattern}'.");
      int errLen = br.ReadInt32();
      string cErr = Encoding.UTF8.GetString(br.ReadBytes(errLen));
      Assert.Equal(expectedMessage, cErr);
    }
  }

  [Fact]
  public void RequiredExamples_MatchSpecExactly() {
    Assert.True(PinoRegex.Compile("(?i)ñandú").HasMatch("ÑANDÚ"));
    Assert.True(PinoRegex.Compile("(?i)á").HasMatch("Á"));
    Assert.True(PinoRegex.Compile("(?i)s").HasMatch("ſ"));
    Assert.True(PinoRegex.Compile("(?i)k").HasMatch("K"));
    Assert.True(PinoRegex.Compile("(?i)ß").HasMatch("ẞ"));
    Assert.Equal("-b-c-", PinoRegex.Compile("a*").Replace("baaac", "-"));

    PinoRegex email = PinoRegex.Compile(@"\w+@\w+\.com");
    string sample = "a@b.com x c@d.com";
    byte[] utf8 = Encoding.UTF8.GetBytes(sample);
    List<string> found = [.. email.FindAll(utf8).Select(m => Encoding.UTF8.GetString(utf8, m.Start, m.End - m.Start))];
    Assert.Equal(["a@b.com", "c@d.com"], found);
  }

  [Fact]
  public void LinearTime_HostilePatternsOn50kInputFinishQuicklyOnBothEngines() {
    string[] patterns = ["(a*)*b", "(a|a)*b", "(x+x+)+y"];
    string[] texts = [new string('a', 50_000), new string('a', 50_000), new string('x', 50_000)];
    TimeSpan limit = TestTimeScale.Apply(TimeSpan.FromSeconds(3));

    // C# engine
    var swCs = Stopwatch.StartNew();
    for (int i = 0; i < patterns.Length; i++) {
      PinoRegex rx = PinoRegex.Compile(patterns[i]);
      Assert.False(rx.HasMatch(texts[i]));
      Assert.False(rx.Find(texts[i], out _, out _));
    }

    swCs.Stop();
    Assert.True(swCs.Elapsed < limit, $"C# linear-time test took {swCs.Elapsed.TotalMilliseconds:F1} ms (limit {limit.TotalMilliseconds:F0} ms).");

    // C engine (compiled with release compiler if available, else TCC)
    string harnessExe = BuildCHarness();
    string inputPath = Path.Combine(_work, "linear-in.bin");
    string outputPath = Path.Combine(_work, "linear-out.bin");
    using (var bw = new BinaryWriter(File.Create(inputPath), Encoding.UTF8)) {
      bw.Write(patterns.Length);
      for (int i = 0; i < patterns.Length; i++) {
        byte[] patBytes = Encoding.UTF8.GetBytes(patterns[i]);
        byte[] txtBytes = Encoding.UTF8.GetBytes(texts[i]);
        bw.Write(patBytes.Length);
        bw.Write(patBytes);
        bw.Write(txtBytes.Length);
        bw.Write(txtBytes);
        bw.Write(0);
      }
    }

    var swC = Stopwatch.StartNew();
    CExecResult run = CCompilerDriver.RunProcess(harnessExe, [inputPath, outputPath], _work, failOnLeak: true, closeStdin: true);
    swC.Stop();
    Assert.True(run.ExitCode == 0, $"C harness failed: {run.Stderr}");
    Assert.True(swC.Elapsed < limit, $"C linear-time test took {swC.Elapsed.TotalMilliseconds:F1} ms (limit {limit.TotalMilliseconds:F0} ms).");

    using var br = new BinaryReader(File.OpenRead(outputPath), Encoding.UTF8);
    for (int i = 0; i < patterns.Length; i++) {
      Assert.Equal(1, br.ReadByte()); // compiled OK
      Assert.Equal(0, br.ReadByte()); // has_match == false
      Assert.Equal(-1, br.ReadInt32()); // match_prefix == none
      Assert.Equal(-1, br.ReadInt32()); // find start == none
      Assert.Equal(-1, br.ReadInt32()); // find end == none
      Assert.Equal(0, br.ReadInt32()); // find_all count == 0
      int repLen = br.ReadInt32();
      br.ReadBytes(repLen);
    }
  }

  [Fact]
  public void DifferentialFuzzer_20000SeededCases_CSharpEqualsCAndMatchesOracle() {
    const int TotalCases = 20_000;
    var rng = new Random(0x50170086);
    var cases = new (string Pattern, string Text, string Repl, bool OracleEligible)[TotalCases];

    for (int i = 0; i < TotalCases; i++) {
      bool oracleSafe = i < 15_000; // 15k oracle-comparable ASCII cases + 5k full-syntax Unicode/flag/anchor cases
      string pattern = GeneratePattern(rng, depth: 0, oracleSafe);
      string text = GenerateText(rng, oracleSafe);
      string repl = i % 3 == 0 ? "-" : (i % 3 == 1 ? "" : "$1");
      cases[i] = (pattern, text, repl, oracleSafe);
    }

    // Write all 20,000 cases to binary file and run C harness in a single process invocation.
    string harnessExe = BuildCHarness();
    string inputPath = Path.Combine(_work, "fuzz-in.bin");
    string outputPath = Path.Combine(_work, "fuzz-out.bin");

    using (var bw = new BinaryWriter(File.Create(inputPath), Encoding.UTF8)) {
      bw.Write(TotalCases);
      for (int i = 0; i < TotalCases; i++) {
        byte[] patBytes = Encoding.UTF8.GetBytes(cases[i].Pattern);
        byte[] txtBytes = Encoding.UTF8.GetBytes(cases[i].Text);
        byte[] repBytes = Encoding.UTF8.GetBytes(cases[i].Repl);
        bw.Write(patBytes.Length);
        bw.Write(patBytes);
        bw.Write(txtBytes.Length);
        bw.Write(txtBytes);
        bw.Write(repBytes.Length);
        bw.Write(repBytes);
      }
    }

    CExecResult run = CCompilerDriver.RunProcess(
        harnessExe,
        [inputPath, outputPath],
        _work,
        failOnLeak: true,
        closeStdin: true,
        timeout: TestTimeScale.Apply(TimeSpan.FromMinutes(2)));
    Assert.True(run.ExitCode == 0, $"C fuzzer harness failed (exit {run.ExitCode}):\n{run.Stderr}\n{run.Stdout}");

    int oracleChecked = 0;
    using var br = new BinaryReader(File.OpenRead(outputPath), Encoding.UTF8);

    for (int i = 0; i < TotalCases; i++) {
      (string pattern, string text, string repl, bool oracleEligible) = cases[i];
      byte[] patBytes = Encoding.UTF8.GetBytes(pattern);
      byte[] txtBytes = Encoding.UTF8.GetBytes(text);
      byte[] repBytes = Encoding.UTF8.GetBytes(repl);

      byte cStatus = br.ReadByte();
      Assert.True(cStatus == 1, $"Case #{i} pattern '{pattern}' failed to compile in C.");

      bool cHasMatch = br.ReadByte() != 0;
      int cPrefixEnd = br.ReadInt32();
      int cFindStart = br.ReadInt32();
      int cFindEnd = br.ReadInt32();
      int cAllCount = br.ReadInt32();
      var cAll = new (int Start, int End)[cAllCount];
      for (int j = 0; j < cAllCount; j++) {
        cAll[j] = (br.ReadInt32(), br.ReadInt32());
      }

      int cRepLen = br.ReadInt32();
      byte[] cRepBytes = br.ReadBytes(cRepLen);

      // Run C# engine on the exact same inputs.
      PinoRegex rx = PinoRegex.Compile(patBytes);
      bool csHasMatch = rx.HasMatch(txtBytes);
      int csPrefixEnd = rx.MatchPrefix(txtBytes, out int pEnd) ? pEnd : -1;
      bool csFound = rx.Find(txtBytes, out int csStart, out int csEnd);
      if (!csFound) {
        csStart = -1;
        csEnd = -1;
      }

      List<(int Start, int End)> csAll = rx.FindAll(txtBytes);
      byte[] csRepBytes = rx.Replace(txtBytes, repBytes);

      string ctx = $"Case #{i}: pattern=/{pattern}/ text={FormatEscaped(text)} repl='{repl}'";
      Assert.True(csHasMatch == cHasMatch, $"{ctx}: has_match C#={csHasMatch} vs C={cHasMatch}");
      Assert.True(csPrefixEnd == cPrefixEnd, $"{ctx}: match_prefix C#={csPrefixEnd} vs C={cPrefixEnd}");
      Assert.True(csStart == cFindStart && csEnd == cFindEnd, $"{ctx}: find C#=({csStart},{csEnd}) vs C=({cFindStart},{cFindEnd})");
      Assert.True(csAll.Count == cAll.Length, $"{ctx}: find_all count C#={csAll.Count} vs C={cAll.Length}");
      for (int j = 0; j < csAll.Count; j++) {
        Assert.True(csAll[j] == cAll[j], $"{ctx}: find_all[{j}] C#={csAll[j]} vs C={cAll[j]}");
      }

      Assert.True(csRepBytes.AsSpan().SequenceEqual(cRepBytes), $"{ctx}: replace mismatch C# vs C");

      // Oracle comparison against .NET Regex(ECMAScript) where semantics agree.
      if (oracleEligible && IsOracleComparable(pattern, text)) {
        var dotNet = new System.Text.RegularExpressions.Regex(pattern, RegexOptions.ECMAScript, TimeSpan.FromSeconds(1));
        Match m = dotNet.Match(text);
        Assert.True(csHasMatch == m.Success, $"{ctx}: has_match Pino={csHasMatch} vs .NET={m.Success}");
        if (m.Success) {
          Assert.True(csStart == m.Index && csEnd == m.Index + m.Length,
              $"{ctx}: find Pino=({csStart},{csEnd}) vs .NET=({m.Index},{m.Index + m.Length})");
        }

        oracleChecked++;
      }
    }

    Assert.True(oracleChecked > 5000, $"Expected > 5000 oracle checks, got {oracleChecked}.");
  }

  /// <summary>
  /// Filters out the documented cases where .NET RegexOptions.ECMAScript differs from RE2/Go:
  /// - non-multiline '$' matching before a trailing '\n' in .NET
  /// - '\s' matching '\v' (0x0B) in .NET ECMAScript (RE2 \s is [\t\n\f\r ])
  /// - nullable-branch-first repetition loops where ECMAScript RepeatMatcher stops on empty iteration
  /// </summary>
  private static bool IsOracleComparable(string pattern, string text) {
    if (text.Contains('\v')) {
      return false;
    }

    if (pattern.Contains('$') && text.EndsWith('\n')) {
      return false;
    }

    // Avoid nullable-first loops (e.g. (|a)* or (a?)*) where ECMAScript RepeatMatcher diverges from Thompson NFA.
    if (pattern.Contains(")*") || pattern.Contains(")+") || pattern.Contains("){")) {
      return false;
    }

    return true;
  }

  private static string GeneratePattern(Random rng, int depth, bool oracleSafe) {
    int branches = depth == 0 && rng.Next(4) == 0 ? rng.Next(2, 4) : 1;
    var sb = new StringBuilder();
    for (int b = 0; b < branches; b++) {
      if (b > 0) {
        sb.Append('|');
      }

      int parts = rng.Next(1, depth == 0 ? 5 : 3);
      for (int p = 0; p < parts; p++) {
        string atom = GenerateAtom(rng, depth, oracleSafe);
        sb.Append(atom);
        if (atom.Length > 0 && !IsAnchor(atom) && !IsFlagDirective(atom) && rng.Next(3) == 0) {
          sb.Append(GenerateQuantifier(rng));
        }
      }
    }

    return sb.ToString();
  }

  private static bool IsAnchor(string atom) =>
      atom is "^" or "$" or "\\b" or "\\B" or "\\A" or "\\z";

  private static bool IsFlagDirective(string atom) =>
      atom.StartsWith("(?", StringComparison.Ordinal) && !atom.Contains(':');

  private static string GenerateAtom(Random rng, int depth, bool oracleSafe) {
    int maxChoice = oracleSafe ? 9 : 13;
    int choice = rng.Next(maxChoice);
    return choice switch {
      0 or 1 or 2 => PickAsciiLiteral(rng),
      3 => ".",
      4 => GenerateCharClass(rng, oracleSafe),
      5 => PickShorthand(rng),
      6 => PickAnchor(rng, oracleSafe),
      7 when depth < 2 => "(" + GeneratePattern(rng, depth + 1, oracleSafe) + ")",
      8 when depth < 2 => "(?:" + GeneratePattern(rng, depth + 1, oracleSafe) + ")",
      9 => PickUnicodeLiteral(rng),
      10 => PickFlagDirective(rng),
      11 when depth < 2 => "(?" + PickFlagChars(rng) + ":" + GeneratePattern(rng, depth + 1, oracleSafe: false) + ")",
      12 => PickEscapeLiteral(rng),
      _ => PickAsciiLiteral(rng),
    };
  }

  private static string PickAsciiLiteral(Random rng) {
    const string Chars = "abcde012_ @.-";
    char c = Chars[rng.Next(Chars.Length)];
    return c == '.' ? "\\." : c.ToString();
  }

  private static string PickUnicodeLiteral(Random rng) {
    string[] runes = ["ñ", "Ñ", "á", "Á", "ß", "ẞ", "σ", "Σ", "ς", "🌲"];
    return runes[rng.Next(runes.Length)];
  }

  private static string PickEscapeLiteral(Random rng) {
    string[] escapes = ["\\n", "\\t", "\\r", "\\f", "\\v", "\\x41", "\\x{00F1}", "\\\\", "\\+", "\\*", "\\?"];
    return escapes[rng.Next(escapes.Length)];
  }

  private static string PickShorthand(Random rng) {
    string[] shorts = ["\\d", "\\D", "\\w", "\\W", "\\s", "\\S"];
    return shorts[rng.Next(shorts.Length)];
  }

  private static string PickAnchor(Random rng, bool oracleSafe) {
    string[] anchors = oracleSafe
        ? ["^", "$", "\\b", "\\B"]
        : ["^", "$", "\\b", "\\B", "\\A", "\\z"];
    return anchors[rng.Next(anchors.Length)];
  }

  private static string PickFlagDirective(Random rng) => "(?" + PickFlagChars(rng) + ")";

  private static string PickFlagChars(Random rng) {
    string[] sets = ["i", "m", "s", "im", "is", "ms", "ims", "-i", "-m", "-s"];
    return sets[rng.Next(sets.Length)];
  }

  private static string GenerateCharClass(Random rng, bool oracleSafe) {
    var sb = new StringBuilder("[");
    if (rng.Next(3) == 0) {
      sb.Append('^');
    }

    int items = rng.Next(1, 4);
    for (int i = 0; i < items; i++) {
      int kind = rng.Next(oracleSafe ? 3 : 4);
      if (kind == 0) {
        const string Pool = "abcde012_";
        sb.Append(Pool[rng.Next(Pool.Length)]);
      } else if (kind == 1) {
        char lo = (char) ('a' + rng.Next(5));
        char hi = (char) (lo + rng.Next(4));
        sb.Append(lo).Append('-').Append(hi);
      } else if (kind == 2) {
        sb.Append(PickShorthand(rng));
      } else {
        sb.Append(PickUnicodeLiteral(rng));
      }
    }

    sb.Append(']');
    return sb.ToString();
  }

  private static string GenerateQuantifier(Random rng) {
    string q = rng.Next(7) switch {
      0 => "*",
      1 => "+",
      2 => "?",
      3 => $"{{{rng.Next(0, 4)}}}",
      4 => $"{{{rng.Next(0, 3)},}}",
      5 => $"{{{rng.Next(0, 2)},{rng.Next(2, 5)}}}",
      _ => "*",
    };
    if (rng.Next(3) == 0) {
      q += "?";
    }

    return q;
  }

  private static string GenerateText(Random rng, bool oracleSafe) {
    int len = rng.Next(0, 24);
    if (len == 0) {
      return string.Empty;
    }

    var sb = new StringBuilder(len);
    const string AsciiAlphabet = "abcdeABC012_ \t\n@.-";
    string[] unicodeAlphabet = ["a", "b", "c", "A", "B", "0", "1", "_", " ", "\n", "ñ", "Ñ", "á", "Á", "ß", "ẞ", "σ", "Σ", "🌲"];
    for (int i = 0; i < len; i++) {
      if (oracleSafe) {
        sb.Append(AsciiAlphabet[rng.Next(AsciiAlphabet.Length)]);
      } else {
        sb.Append(unicodeAlphabet[rng.Next(unicodeAlphabet.Length)]);
      }
    }

    return sb.ToString();
  }

  private static string FormatEscaped(string s) =>
      s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

  private const string HarnessSource = """
      #include "pino_regex.h"
      #include "pino_rt.h"
      #include <stdint.h>
      #include <stdio.h>
      #include <stdlib.h>
      #include <string.h>

      static int32_t read_i32(FILE* f) {
        int32_t v = 0;
        if (fread(&v, 4, 1, f) != 1) {
          fprintf(stderr, "short read\n");
          exit(2);
        }
        return v;
      }

      static void write_u8(FILE* f, uint8_t v) { fwrite(&v, 1, 1, f); }
      static void write_i32(FILE* f, int32_t v) { fwrite(&v, 4, 1, f); }

      static char* read_bytes(FILE* f, int32_t* len_out) {
        int32_t n = read_i32(f);
        *len_out = n;
        char* buf = (char*)malloc((size_t)n + 1);
        if (n > 0 && fread(buf, 1, (size_t)n, f) != (size_t)n) {
          fprintf(stderr, "short read bytes\n");
          exit(2);
        }
        buf[n] = '\0';
        return buf;
      }

      int main(int argc, char** argv) {
        if (argc < 3) {
          return 2;
        }
        FILE* fin = fopen(argv[1], "rb");
        FILE* fout = fopen(argv[2], "wb");
        if (!fin || !fout) {
          return 2;
        }

        int32_t count = read_i32(fin);
        for (int32_t i = 0; i < count; i++) {
          int32_t pat_len = 0, txt_len = 0, rep_len = 0;
          char* pat = read_bytes(fin, &pat_len);
          char* txt = read_bytes(fin, &txt_len);
          char* rep = read_bytes(fin, &rep_len);

          size_t err_cap = (size_t)pat_len + 256;
          char* err_buf = (char*)malloc(err_cap);
          err_buf[0] = '\0';

          PinoReProg* prog = pino_re_compile(pat, (size_t)pat_len, err_buf, err_cap);
          if (prog == NULL) {
            write_u8(fout, 0);
            int32_t elen = (int32_t)strlen(err_buf);
            write_i32(fout, elen);
            fwrite(err_buf, 1, (size_t)elen, fout);
            free(err_buf);
            free(pat);
            free(txt);
            free(rep);
            continue;
          }
          free(err_buf);

          write_u8(fout, 1);
          bool has = pino_re_has_match(prog, txt, (size_t)txt_len);
          write_u8(fout, has ? 1 : 0);

          size_t p_end = 0;
          if (pino_re_match_prefix(prog, txt, (size_t)txt_len, &p_end)) {
            write_i32(fout, (int32_t)p_end);
          } else {
            write_i32(fout, -1);
          }

          size_t f_start = 0, f_end = 0;
          if (pino_re_find(prog, txt, (size_t)txt_len, &f_start, &f_end)) {
            write_i32(fout, (int32_t)f_start);
            write_i32(fout, (int32_t)f_end);
          } else {
            write_i32(fout, -1);
            write_i32(fout, -1);
          }

          /* High-level find_all and replace via PinoRegex wrapper */
          PinoRegex* rx = (PinoRegex*)PINO_RC_ALLOC(pino_cur_alloc, sizeof(PinoRegex), 0, 0, "regex");
          rx->pattern = PINO_STR_EMPTY;
          rx->prog = prog;

          PinoString txt_s = {NULL, txt, (int64_t)txt_len};
          PinoString rep_s = {NULL, rep, (int64_t)rep_len};

          PinoVec_string* all = pino_regex_find_all(rx, txt_s);
          write_i32(fout, (int32_t)all->len);
          for (int64_t j = 0; j < all->len; j++) {
            int32_t st = (int32_t)(all->data[j].ptr - txt);
            int32_t en = st + (int32_t)all->data[j].byte_len;
            write_i32(fout, st);
            write_i32(fout, en);
          }
          pino_vec_string_release(all);

          PinoString replaced = pino_regex_replace(rx, txt_s, rep_s);
          write_i32(fout, (int32_t)replaced.byte_len);
          if (replaced.byte_len > 0) {
            fwrite(replaced.ptr, 1, (size_t)replaced.byte_len, fout);
          }
          pino_str_release(replaced);

          pino_regex_release(rx);
          free(pat);
          free(txt);
          free(rep);
        }

        fclose(fin);
        fclose(fout);
        return 0;
      }
      """;
}
