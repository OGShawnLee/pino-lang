using System.Diagnostics;

using Pino.Compiler.Checking;
using Pino.Compiler.CodeGen.C;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.IR.Passes;
using Pino.Compiler.Parsing;
using Pino.Compiler.Tests.Conformance;
using Pino.Compiler.Text;
using Pino.Vm;

namespace Pino.Compiler.Tests.CodeGen;

public sealed class StringAppendInPlaceTests : IDisposable {
  private const string Append200kSource = """
    fn build_assign(n int) int {
      var s = ""
      var i = 0
      for i < n {
        s = s + "ab,"
        i += 1
      }
      return s:len
    }

    fn build_plus_assign(n int) int {
      var s = ""
      var i = 0
      for i < n {
        s += "xy"
        i += 1
      }
      return s:len
    }

    println(build_assign(200000))
    println(build_plus_assign(200000))
    """;

  private const string Append200kExpected = "600000\n400000";

  private const string StructFieldAppend200kSource = """
    struct Builder {
      mut buf string

      mut fn add_plus_assign(n int) {
        var i = 0
        for i < n {
          self:buf += "ab,"
          i += 1
        }
      }

      mut fn add_assign(n int) {
        var i = 0
        for i < n {
          self:buf = self:buf + "xy"
          i += 1
        }
      }
    }

    var b1 = Builder { buf: "" }
    b1:add_plus_assign(200000)
    var b2 = Builder { buf: "" }
    b2:add_assign(200000)
    println(b1:buf:len)
    println(b2:buf:len)
    """;

  private const string VecElemAppend200kSource = """
    fn build_vec_plus_assign(n int) int {
      var xs = [""]
      var i = 0
      for i < n {
        xs[0] += "ab,"
        i += 1
      }
      return xs[0]:len
    }

    fn build_vec_assign(n int) int {
      var xs = [""]
      var i = 0
      for i < n {
        xs[0] = xs[0] + "xy"
        i += 1
      }
      return xs[0]:len
    }

    println(build_vec_plus_assign(200000))
    println(build_vec_assign(200000))
    """;

  private const string MutParamAppend200kSource = """
    fn add_plus_assign(mut s string, piece string) {
      s += piece
    }

    fn add_assign(mut s string, piece string) {
      s = s + piece
    }

    fn run_plus_assign(n int) int {
      var s = ""
      var i = 0
      for i < n {
        add_plus_assign(mut s, "ab,")
        i += 1
      }
      return s:len
    }

    fn run_assign(n int) int {
      var s = ""
      var i = 0
      for i < n {
        add_assign(mut s, "xy")
        i += 1
      }
      return s:len
    }

    println(run_plus_assign(200000))
    println(run_assign(200000))
    """;

  private readonly string _work = Path.Combine(Path.GetTempPath(), "pino-str-append-" + Guid.NewGuid().ToString("N"));
  private readonly string _repoRoot = CCompilerDriver.FindRepositoryRoot();

  public StringAppendInPlaceTests() {
    Directory.CreateDirectory(_work);
  }

  public void Dispose() {
    try {
      Directory.Delete(_work, recursive: true);
    } catch (IOException) {
    } catch (UnauthorizedAccessException) {
    }
  }

  [Theory]
  [InlineData("strings/append-aliasing-local.pino")]
  [InlineData("strings/append-aliasing-vector.pino")]
  [InlineData("strings/append-self-concat.pino")]
  [InlineData("strings/append-slice-end.pino")]
  [InlineData("strings/append-non-ascii.pino")]
  [InlineData("strings/append-function-arg.pino")]
  [InlineData("strings/append-aliasing-struct.pino")]
  [InlineData("strings/append-aliasing-vec-elem.pino")]
  [InlineData("strings/append-aliasing-mut-param.pino")]
  public void AliasingCases_PassWithZeroLeaksOnVmAndCDebugRc(string relativeCasePath) {
    PreparedCase prepared = ConformanceRunner.PrepareCase(relativeCasePath);
    Assert.Null(prepared.CompileTimeOutcome);

    EngineRun vmRun = ConformanceRunner.Run(ConformanceEngine.ReVm, prepared);
    CaseVerdict vmVerdict = ConformanceRunner.Judge(relativeCasePath, vmRun);
    Assert.True(vmVerdict.Passed, $"[ReVm] {relativeCasePath}: {vmVerdict.Detail}");

    EngineRun cRun = ConformanceRunner.Run(ConformanceEngine.ReC, prepared);
    CaseVerdict cVerdict = ConformanceRunner.Judge(relativeCasePath, cRun);
    Assert.True(cVerdict.Passed, $"[ReC --debug-rc] {relativeCasePath}: {cVerdict.Detail}");
    Assert.Equal(string.Empty, cRun.Stderr.Trim());
  }

  [Fact]
  public void Append200k_AmortizedLinear_Vm() => RunLinearTimeVm("local", Append200kSource, Append200kExpected);

  [Fact]
  public void Append200k_AmortizedLinear_Tcc() => RunLinearTimeTcc("local", Append200kSource, Append200kExpected);

  [Fact]
  public void Append200k_AmortizedLinear_Gcc() => RunLinearTimeGcc("local", Append200kSource, Append200kExpected);

  [Theory]
  [InlineData("struct_field")]
  [InlineData("vec_elem")]
  [InlineData("mut_param")]
  public void PlaceAppend200k_AmortizedLinear_Vm(string kind) =>
      RunLinearTimeVm(kind, PlaceSourceFor(kind), Append200kExpected);

  [Theory]
  [InlineData("struct_field")]
  [InlineData("vec_elem")]
  [InlineData("mut_param")]
  public void PlaceAppend200k_AmortizedLinear_Tcc(string kind) =>
      RunLinearTimeTcc(kind, PlaceSourceFor(kind), Append200kExpected);

  [Theory]
  [InlineData("struct_field")]
  [InlineData("vec_elem")]
  [InlineData("mut_param")]
  public void PlaceAppend200k_AmortizedLinear_Gcc(string kind) =>
      RunLinearTimeGcc(kind, PlaceSourceFor(kind), Append200kExpected);

  private static string PlaceSourceFor(string kind) => kind switch {
    "struct_field" => StructFieldAppend200kSource,
    "vec_elem" => VecElemAppend200kSource,
    "mut_param" => MutParamAppend200kSource,
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };

  private void RunLinearTimeVm(string label, string sourceCode, string expectedOutput) {
    TimeSpan limit = TestTimeScale.Apply(TimeSpan.FromSeconds(5));
    (SourceText source, _, IrModule ir) = CompileToPostRcIr(sourceCode, $"append_200k_{label}_vm.pino");

    var host = new BufferedVmHost();
    using var cts = new CancellationTokenSource(limit);
    var sw = Stopwatch.StartNew();
    PinoVmResult vmResult;
    try {
      var vm = new PinoVm(ir, host, new PinoVmOptions {
        VerifyRc = true,
        RootSource = source,
        CancellationToken = cts.Token,
      });
      vmResult = vm.Run();
    } catch (OperationCanceledException) {
      Assert.Fail($"VM timed out after {limit.TotalSeconds:F1}s on 200,000 {label} string appends.");
      return;
    }

    sw.Stop();
    Assert.Equal(0, vmResult.ExitCode);
    Assert.Null(vmResult.RcError);
    Assert.True(vmResult.LeakReport is null or { IsClean: true }, vmResult.LeakReport?.Format());
    Assert.Equal(expectedOutput, Normalize(host.StdOut));
    Assert.True(sw.Elapsed <= limit, $"VM ({label}) took {sw.Elapsed.TotalMilliseconds:F1} ms (limit {limit.TotalMilliseconds:F0} ms).");
  }

  private void RunLinearTimeTcc(string label, string sourceCode, string expectedOutput) {
    TimeSpan limit = TestTimeScale.Apply(TimeSpan.FromSeconds(5));
    (_, Compilation compilation, IrModule ir) = CompileToPostRcIr(sourceCode, $"append_200k_{label}_tcc.pino");
    string cSource = CEmitter.Emit(ir, compilation);

    string tccExe = Path.Combine(_work, OperatingSystem.IsWindows() ? $"append_{label}_tcc.exe" : $"append_{label}_tcc");
    CBuildResult tccBuild = CCompilerDriver.Build(new CBuildOptions(
        CSource: cSource,
        OutputExecutablePath: tccExe,
        Release: false,
        DebugRc: true,
        RepoRoot: _repoRoot));
    Assert.True(tccBuild.Succeeded, $"TCC build failed ({label}):\n{tccBuild.Stderr}\n{tccBuild.Stdout}");

    var sw = Stopwatch.StartNew();
    CExecResult tccExec = CCompilerDriver.RunExecutable(tccExe, _work, failOnLeak: true, closeStdin: true, timeout: limit);
    sw.Stop();
    Assert.False(tccExec.TimedOut, $"TinyCC binary timed out after {limit.TotalSeconds:F1}s on 200,000 {label} string appends.");
    Assert.Equal(0, tccExec.ExitCode);
    Assert.Equal(expectedOutput, Normalize(tccExec.Stdout));
    Assert.Equal(string.Empty, tccExec.Stderr.Trim());
    Assert.True(sw.Elapsed <= limit, $"TinyCC ({label}) took {sw.Elapsed.TotalMilliseconds:F1} ms (limit {limit.TotalMilliseconds:F0} ms).");
  }

  private void RunLinearTimeGcc(string label, string sourceCode, string expectedOutput) {
    if (CCompilerDriver.FindReleaseCompiler() is null) {
      return;
    }

    TimeSpan limit = TestTimeScale.Apply(TimeSpan.FromSeconds(5));
    (_, Compilation compilation, IrModule ir) = CompileToPostRcIr(sourceCode, $"append_200k_{label}_gcc.pino");
    string cSource = CEmitter.Emit(ir, compilation);

    string gccExe = Path.Combine(_work, OperatingSystem.IsWindows() ? $"append_{label}_gcc.exe" : $"append_{label}_gcc");
    CBuildResult gccBuild = CCompilerDriver.Build(new CBuildOptions(
        CSource: cSource,
        OutputExecutablePath: gccExe,
        Release: true,
        DebugRc: false,
        RepoRoot: _repoRoot));
    Assert.True(gccBuild.Succeeded, $"GCC build failed ({label}):\n{gccBuild.Stderr}\n{gccBuild.Stdout}");

    var sw = Stopwatch.StartNew();
    CExecResult gccExec = CCompilerDriver.RunExecutable(gccExe, _work, failOnLeak: false, closeStdin: true, timeout: limit);
    sw.Stop();
    Assert.False(gccExec.TimedOut, $"GCC -O2 binary timed out after {limit.TotalSeconds:F1}s on 200,000 {label} string appends.");
    Assert.Equal(0, gccExec.ExitCode);
    Assert.Equal(expectedOutput, Normalize(gccExec.Stdout));
    Assert.True(sw.Elapsed <= limit, $"GCC -O2 ({label}) took {sw.Elapsed.TotalMilliseconds:F1} ms (limit {limit.TotalMilliseconds:F0} ms).");
  }

  private (SourceText Source, Compilation Compilation, IrModule Ir) CompileToPostRcIr(string sourceText, string fileName) {
    string pinoPath = Path.Combine(_work, fileName);
    File.WriteAllText(pinoPath, sourceText, CEmitter.Utf8NoBom);
    var source = new SourceText(sourceText, pinoPath);

    var parsed = Parser.Parse(source);
    Assert.Empty(parsed.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

    IrModule ir = IrLowerer.Lower(compilation);
    RcInsertPass.Run(ir);
    IrVerificationResult verify = IrVerifier.VerifyAfterRc(ir);
    Assert.True(verify.IsValid, string.Join("\n", verify.Errors));
    return (source, compilation, ir);
  }

  private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
