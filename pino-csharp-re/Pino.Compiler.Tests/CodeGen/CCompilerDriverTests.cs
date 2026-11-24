using System.Runtime.InteropServices;

using Pino.Cli;
using Pino.Compiler.CodeGen.C;

namespace Pino.Compiler.Tests.CodeGen;

/// <summary>
/// Coverage for ticket 075 toolchain lookup order, system C compiler fallback,
/// per-OS missing-compiler diagnostics, and VM independence from C toolchains.
/// </summary>
public sealed class CCompilerDriverTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "pino-driver-tests-" + Guid.NewGuid().ToString("N"));

  public CCompilerDriverTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try {
      Directory.Delete(_root, recursive: true);
    } catch (IOException) {
    } catch (UnauthorizedAccessException) {
    }
  }

  [Fact]
  public void FindRepositoryRoot_PrefersExecutableDirectoryOverCurrentDirectoryRepo() {
    string installedDir = CreateFakeRuntimeTree("installed-pino", includeTcc: true);
    string repoDir = CreateFakeRuntimeTree("other-repo", includeTcc: true);
    string nestedWorkDir = Path.Combine(repoDir, "examples", "demo");
    Directory.CreateDirectory(nestedWorkDir);

    string resolved = CCompilerDriver.FindRepositoryRoot(
        startDirectory: nestedWorkDir,
        executableDirectory: installedDir);

    Assert.Equal(Path.GetFullPath(installedDir), Path.GetFullPath(resolved));
    string? tcc = CCompilerDriver.FindBundledTcc(resolved);
    Assert.NotNull(tcc);
    Assert.StartsWith(Path.GetFullPath(installedDir), Path.GetFullPath(tcc!), StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void FindRepositoryRoot_FallsBackToRepoWalkWhenExecutableDirectoryHasNoRuntime() {
    string emptyExeDir = Path.Combine(_root, "dev-bin");
    Directory.CreateDirectory(emptyExeDir);
    string repoDir = CreateFakeRuntimeTree("dev-repo", includeTcc: true);
    string nestedWorkDir = Path.Combine(repoDir, "sub", "project");
    Directory.CreateDirectory(nestedWorkDir);

    string resolved = CCompilerDriver.FindRepositoryRoot(
        startDirectory: nestedWorkDir,
        executableDirectory: emptyExeDir);

    Assert.Equal(Path.GetFullPath(repoDir), Path.GetFullPath(resolved));
  }

  [Fact]
  public void FindSystemCompiler_PrefersCcThenClangThenGcc() {
    string ext = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
    string dirGcc = Path.Combine(_root, "path-gcc");
    string dirClang = Path.Combine(_root, "path-clang");
    string dirCc = Path.Combine(_root, "path-cc");
    Directory.CreateDirectory(dirGcc);
    Directory.CreateDirectory(dirClang);
    Directory.CreateDirectory(dirCc);

    string gccPath = Path.Combine(dirGcc, "gcc" + ext);
    string clangPath = Path.Combine(dirClang, "clang" + ext);
    string ccPath = Path.Combine(dirCc, "cc" + ext);
    File.WriteAllText(gccPath, "fake");
    File.WriteAllText(clangPath, "fake");
    File.WriteAllText(ccPath, "fake");

    string allThree = string.Join(Path.PathSeparator, [dirGcc, dirClang, dirCc]);
    Assert.Equal(ccPath, CCompilerDriver.FindSystemCompiler(allThree));

    string clangAndGcc = string.Join(Path.PathSeparator, [dirGcc, dirClang]);
    Assert.Equal(clangPath, CCompilerDriver.FindSystemCompiler(clangAndGcc));

    Assert.Equal(gccPath, CCompilerDriver.FindSystemCompiler(dirGcc));
    Assert.Null(CCompilerDriver.FindSystemCompiler(string.Empty));
  }

  [Fact]
  public void Build_WithoutBundledTcc_FallsBackToSystemCompilerAndRuns() {
    string? systemCc = CCompilerDriver.FindSystemCompiler();
    if (systemCc is null) {
      return; // System C compiler fallback requires cc/clang/gcc on PATH.
    }

    string realRepo = CCompilerDriver.FindRepositoryRoot();
    string noTccInstall = Path.Combine(_root, "no-tcc-install");
    CopyRealRuntimeFiles(realRepo, noTccInstall);
    Assert.Null(CCompilerDriver.FindBundledTcc(noTccInstall));

    const string MinimalC = """
        #include "pino_rt.h"
        #include <stdio.h>
        int main(void) {
          PinoString s = PINO_STR_LIT("fallback-ok");
          pino_println_n(&s, 1);
          return 0;
        }
        """;

    string outExe = Path.Combine(_root, OperatingSystem.IsWindows() ? "fallback.exe" : "fallback");
    CBuildResult build = CCompilerDriver.Build(new CBuildOptions(
        CSource: MinimalC,
        OutputExecutablePath: outExe,
        Release: false,
        DebugRc: true,
        ExecutableDirectory: noTccInstall,
        WorkingDirectory: _root));

    Assert.True(build.Succeeded, $"System compiler fallback failed ({build.CompilerUsed}):\n{build.Stderr}\n{build.Stdout}");
    Assert.Equal(systemCc, build.CompilerUsed);

    CExecResult exec = CCompilerDriver.RunExecutable(outExe, _root, failOnLeak: true, closeStdin: true);
    Assert.Equal(0, exec.ExitCode);
    Assert.Equal("fallback-ok", exec.Stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Trim());
  }

  [Theory]
  [InlineData("macos", "pino: --c needs a C compiler. Install Apple's command line tools with: xcode-select --install")]
  [InlineData("linux", "pino: --c needs a C compiler and the C library headers (for example: sudo apt install build-essential).")]
  [InlineData("windows", "pino: --c needs TinyCC, which should come with Pino. Reinstall Pino, or put gcc or clang on your PATH.")]
  public void MissingCompiler_PrintsExactOsSentenceAndExitsOne_AcrossRunBuildAndTest(string osKey, string expectedLine) {
    OSPlatform platform = osKey switch {
      "macos" => OSPlatform.OSX,
      "linux" => OSPlatform.Linux,
      "windows" => OSPlatform.Windows,
      _ => throw new ArgumentOutOfRangeException(nameof(osKey)),
    };

    string noCompilerInstall = CreateFakeRuntimeTree("no-compiler-" + osKey, includeTcc: false);
    string fakeExe = Path.Combine(noCompilerInstall, platform == OSPlatform.Windows ? "pino.exe" : "pino");
    File.WriteAllText(fakeExe, "fake");

    string projectDir = Path.Combine(_root, "proj-" + osKey);
    Directory.CreateDirectory(projectDir);
    File.WriteAllText(Path.Combine(projectDir, "main.pino"), "println(\"hi\")\n");
    File.WriteAllText(Path.Combine(projectDir, "sample_test.pino"), "test \"ok\" { assert(true) }\n");

    foreach (string[] cmdArgs in new string[][] {
      ["run", "--c", "main.pino"],
      ["build", "main.pino"],
      ["test", "--c"],
    }) {
      var stdout = new StringWriter { NewLine = "\n" };
      var stderr = new StringWriter { NewLine = "\n" };
      var env = new CliEnvironment(
          name => string.Equals(name, "PATH", StringComparison.OrdinalIgnoreCase) ? string.Empty : null,
          IsOutputRedirected: true,
          SupportsAnsi: false,
          Notes: new Dictionary<string, string>(),
          WorkingDirectory: projectDir,
          ExecutablePath: fakeExe,
          OsPlatform: platform);

      int exitCode = CliApp.Run(cmdArgs, TextReader.Null, stdout, stderr, env);

      Assert.Equal(1, exitCode);
      Assert.Equal(string.Empty, stdout.ToString());
      Assert.Equal(expectedLine + "\n", stderr.ToString());
    }
  }

  [Fact]
  public void VmRun_WorksWithoutAnyCCompilerOrTcc() {
    string bareInstall = Path.Combine(_root, "bare-vm-install");
    Directory.CreateDirectory(bareInstall);
    string fakeExe = Path.Combine(bareInstall, OperatingSystem.IsWindows() ? "pino.exe" : "pino");
    File.WriteAllText(fakeExe, "fake");

    string workDir = Path.Combine(_root, "vm-only-work");
    Directory.CreateDirectory(workDir);
    File.WriteAllText(Path.Combine(workDir, "main.pino"), "println(\"vm works without cc\", 20 + 22)\n");

    var stdout = new StringWriter { NewLine = "\n" };
    var stderr = new StringWriter { NewLine = "\n" };
    var env = new CliEnvironment(
        name => string.Equals(name, "PATH", StringComparison.OrdinalIgnoreCase) ? string.Empty : null,
        IsOutputRedirected: true,
        SupportsAnsi: false,
        Notes: new Dictionary<string, string>(),
        WorkingDirectory: workDir,
        ExecutablePath: fakeExe);

    int exitCode = CliApp.Run(["run", "main.pino"], TextReader.Null, stdout, stderr, env);

    Assert.Equal(0, exitCode);
    Assert.Equal("vm works without cc 42\n", stdout.ToString());
    Assert.Equal(string.Empty, stderr.ToString());
  }

  [Theory]
  [InlineData("tcc: error: file 'crt1.o' not found", true)]
  [InlineData("pino_rt.h:20: error: include file 'stdio.h' not found", true)]
  [InlineData("fatal error: stdio.h: No such file or directory", true)]
  [InlineData("syntax error before '}'", false)]
  public void MissingLinuxLibcHeadersDetection_MatchesExpectedErrors(string stderr, bool expected) {
    Assert.Equal(expected, CCompilerDriver.LooksLikeMissingLinuxLibcHeaders(stderr));
    Assert.Contains("libc6-dev", CCompilerDriver.FormatMissingLinuxHeadersMessage(), StringComparison.Ordinal);
    Assert.Contains("glibc-devel", CCompilerDriver.FormatMissingLinuxHeadersMessage(), StringComparison.Ordinal);
  }

  private string CreateFakeRuntimeTree(string name, bool includeTcc) {
    string dir = Path.Combine(_root, name);
    Directory.CreateDirectory(Path.Combine(dir, "runtime-re"));
    Directory.CreateDirectory(Path.Combine(dir, "runtime"));
    File.WriteAllText(Path.Combine(dir, "runtime-re", "pino_rt.h"), "/* fake */\n");
    File.WriteAllText(Path.Combine(dir, "runtime", "re.c"), "/* fake */\n");
    if (includeTcc) {
      string tccDir = Path.Combine(dir, "tooling", "tcc");
      Directory.CreateDirectory(tccDir);
      File.WriteAllText(Path.Combine(tccDir, OperatingSystem.IsWindows() ? "tcc.exe" : "tcc"), "fake-tcc");
    }

    return dir;
  }

  private static void CopyRealRuntimeFiles(string sourceRoot, string destRoot) {
    string destRuntimeRe = Path.Combine(destRoot, "runtime-re");
    string destRuntime = Path.Combine(destRoot, "runtime");
    Directory.CreateDirectory(destRuntimeRe);
    Directory.CreateDirectory(destRuntime);

    foreach (string file in Directory.EnumerateFiles(Path.Combine(sourceRoot, "runtime-re"))) {
      if (file.EndsWith(".c", StringComparison.Ordinal) || file.EndsWith(".h", StringComparison.Ordinal)) {
        File.Copy(file, Path.Combine(destRuntimeRe, Path.GetFileName(file)), overwrite: true);
      }
    }

    File.Copy(Path.Combine(sourceRoot, "runtime", "re.c"), Path.Combine(destRuntime, "re.c"), overwrite: true);
    File.Copy(Path.Combine(sourceRoot, "runtime", "re.h"), Path.Combine(destRuntime, "re.h"), overwrite: true);
  }
}
