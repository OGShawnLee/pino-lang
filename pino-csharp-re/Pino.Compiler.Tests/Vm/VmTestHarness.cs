using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.IR.Passes;
using Pino.Compiler.Parsing;
using Pino.Compiler.Text;
using Pino.Vm;

namespace Pino.Compiler.Tests.Vm;

internal sealed record VmRun(PinoVmResult Result, BufferedVmHost Host, IrModule Module, PinoVm Vm) {
  public string Stdout => Host.StdOut;

  public string Stderr => Host.StdErr;
}

/// <summary>Compiles Pino source to post-RC IR and runs it on <see cref="PinoVm"/>.</summary>
internal static class VmTestHarness {
  public static readonly string RepoRoot = Parsing.ParserTestHelpers.FindRepositoryRoot();

  public static readonly string ConformanceRoot = Path.Combine(RepoRoot, "conformance");

  public static Compilation Compile(string source, string path, bool useFileSystemModules) {
    var parsed = Parser.Parse(new SourceText(source, path));
    Assert.Empty(parsed.Diagnostics.Where(static d => d.Severity != DiagnosticSeverity.Hint));
    Compilation compilation = useFileSystemModules
        ? Compilation.Create(parsed, FileSystemModuleSource.Instance)
        : Compilation.Create(parsed);
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
    return compilation;
  }

  public static IrModule BuildPostRc(string source, string path = "vm_test.pino", bool useFileSystemModules = false) {
    Compilation compilation = Compile(source, path, useFileSystemModules);
    IrModule module = IrLowerer.Lower(compilation);
    RcInsertPass.Run(module);
    IrVerificationResult verification = IrVerifier.VerifyAfterRc(module);
    Assert.True(verification.IsValid, "Post-RC verification failed:\n" + string.Join("\n", verification.Errors));
    return module;
  }

  public static VmRun Run(
      string source,
      bool verifyRc = true,
      string input = "",
      string path = "vm_test.pino",
      IVmFileSystem? fs = null) {
    IrModule module = BuildPostRc(source, path);
    return Execute(module, verifyRc, input, new SourceText(source, path), fs);
  }

  public static VmRun RunFile(string fullPath, bool verifyRc = true) {
    string content = File.ReadAllText(fullPath);
    IrModule module = BuildPostRc(content, fullPath, useFileSystemModules: true);
    return Execute(module, verifyRc, string.Empty, new SourceText(content, fullPath), fs: null);
  }

  public static VmRun Execute(IrModule module, bool verifyRc, string input, SourceText? source, IVmFileSystem? fs = null, PinoVmOptions? options = null) {
    var host = new BufferedVmHost(input, fs);
    var vm = new PinoVm(module, host, options ?? new PinoVmOptions { VerifyRc = verifyRc, RootSource = source });
    PinoVmResult result = vm.Run();
    return new VmRun(result, host, module, vm);
  }

  public static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
