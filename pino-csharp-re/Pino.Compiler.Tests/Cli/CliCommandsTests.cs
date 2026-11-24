using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

using Pino.Cli;
using Pino.Compiler.CodeGen.C;

namespace Pino.Compiler.Tests.Cli;

/// <summary>
/// Coverage for ticket 074 CLI commands: default <c>main.pino</c>, <c>init</c>, <c>compile</c>/<c>c</c>,
/// <c>watch</c>, <c>play</c>/<c>play update</c>, <c>test</c>, <c>version</c>/<c>v</c>/<c>--version</c>,
/// <c>update</c>, <c>help</c>/<c>h</c>, and imported-module panic locations on both VM and C engines.
/// No network access and no real sleeps.
/// </summary>
public sealed class CliCommandsTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "pino-cli-cmds-" + Guid.NewGuid().ToString("N"));

  public CliCommandsTests() {
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
  public void NoArgs_RunsMainPinoWhenPresent_AndPrintsUsageWhenAbsent() {
    CliResult empty = Run([]);
    Assert.Equal(0, empty.ExitCode);
    Assert.Equal(string.Empty, empty.Stderr);
    Assert.Contains("pino init [name]", empty.Stdout, StringComparison.Ordinal);

    WriteFile("main.pino", "println(\"from default main\")\n");
    CliResult withMain = Run([]);
    Assert.Equal(0, withMain.ExitCode);
    Assert.Equal("from default main\n", withMain.Stdout);
    Assert.Equal(string.Empty, withMain.Stderr);
  }

  [Theory]
  [InlineData("--help")]
  [InlineData("-h")]
  [InlineData("help")]
  [InlineData("h")]
  public void HelpAliases_PrintUsageListingEveryCommand(string arg) {
    CliResult result = Run([arg]);

    Assert.Equal(0, result.ExitCode);
    Assert.Equal(string.Empty, result.Stderr);
    foreach (string cmd in new[] {
      "init", "run", "build", "compile", "watch", "check", "repl",
      "play", "play update", "test", "tree", "explain", "version", "update", "help",
    }) {
      Assert.Contains(cmd, result.Stdout, StringComparison.Ordinal);
    }
  }

  [Fact]
  public void Usage_ListsEveryCommandOptionUnderItsCommandAndMatchesParserTable() {
    CliResult result = Run(["--help"]);
    Assert.Equal(0, result.ExitCode);
    string usage = result.Stdout;

    // 1. Every command and every option in the single-source-of-truth table must appear in --help,
    // with each command's options indented directly beneath that command.
    foreach (CliApp.CliOptionSpec opt in CliApp.GlobalOptions) {
      Assert.Contains(opt.Name, usage, StringComparison.Ordinal);
    }

    for (int i = 0; i < CliApp.CommandSpecs.Length; i++) {
      CliApp.CliCommandSpec cmd = CliApp.CommandSpecs[i];
      int cmdIndex = usage.IndexOf(cmd.Signature, StringComparison.Ordinal);
      Assert.True(cmdIndex >= 0, $"Missing command '{cmd.Signature}' in usage.");

      int nextSectionIndex = i + 1 < CliApp.CommandSpecs.Length
          ? usage.IndexOf(CliApp.CommandSpecs[i + 1].Signature, cmdIndex + cmd.Signature.Length, StringComparison.Ordinal)
          : usage.IndexOf("\nGlobal options", cmdIndex, StringComparison.Ordinal);
      Assert.True(nextSectionIndex > cmdIndex);

      string section = usage[cmdIndex..nextSectionIndex];
      foreach (CliApp.CliOptionSpec opt in cmd.Options) {
        Assert.Contains(opt.Name, section, StringComparison.Ordinal);
      }
    }

    // 2. Also guard against anyone adding a hardcoded flag literal in CliApp*.cs without adding it to the table.
    string cliDir = Path.Combine(Parsing.ParserTestHelpers.FindRepositoryRoot(), "pino-csharp-re", "Pino.Cli");
    var flagRegex = new System.Text.RegularExpressions.Regex(@"""(--[a-z][a-z0-9-]*|-[a-zA-Z])""");
    foreach (string filePath in Directory.EnumerateFiles(cliDir, "CliApp*.cs")) {
      string src = File.ReadAllText(filePath);
      foreach (System.Text.RegularExpressions.Match match in flagRegex.Matches(src)) {
        string flag = match.Groups[1].Value;
        Assert.Contains(flag, usage, StringComparison.Ordinal);
      }
    }

    // 3. Help must stay in plain words with no internal compiler jargon (ticket 080 step 6a).
    foreach (string jargon in new[] { "IR VM", "shadow-RC", "DPINO_DEBUG_RC", "TCC", "C99", "= Pino:" }) {
      Assert.DoesNotContain(jargon, usage, StringComparison.Ordinal);
    }
  }

  [Theory]
  [InlineData("run")]
  [InlineData("build")]
  [InlineData("watch")]
  [InlineData("compile")]
  [InlineData("c")]
  public void Commands_DefaultToMainPinoAndExitTwoWhenMissing(string command) {
    CliResult result = Run([command]);

    Assert.Equal(2, result.ExitCode);
    Assert.Equal("pino: no such file or directory: 'main.pino'\n", result.Stderr);
  }

  [Fact]
  public void Init_CreatesValidProjectThatChecksAndRunsOnVmAndC_AndRefusesOverwrite() {
    CliResult init = Run(["init", "demo"]);

    Assert.Equal(0, init.ExitCode);
    Assert.Equal(string.Empty, init.Stderr);
    Assert.Contains("Initialized new Pino project in demo", init.Stdout, StringComparison.Ordinal);
    Assert.Contains("cd demo", init.Stdout, StringComparison.Ordinal);
    Assert.Contains("pino run", init.Stdout, StringComparison.Ordinal);

    string demoDir = Path.Combine(_root, "demo");
    Assert.True(File.Exists(Path.Combine(demoDir, "main.pino")));
    Assert.True(File.Exists(Path.Combine(demoDir, "modules", "utils.pino")));
    Assert.True(File.Exists(Path.Combine(demoDir, "test", "main_test.pino")));
    Assert.True(File.Exists(Path.Combine(demoDir, ".gitignore")));

    CliResult second = Run(["init", "demo"]);
    Assert.Equal(1, second.ExitCode);
    Assert.Equal(
        $"pino: 'main.pino' already exists in '{demoDir.Replace('\\', '/')}'; nothing was created.\n",
        second.Stderr);

    CliResult check = Run(["check", "."], workingDirectory: demoDir);
    Assert.Equal(0, check.ExitCode);

    CliResult runDefault = Run([], workingDirectory: demoDir);
    Assert.Equal(0, runDefault.ExitCode);
    Assert.Equal("Welcome to Pino! Result: 42\n", runDefault.Stdout);

    CliResult runVm = Run(["run"], workingDirectory: demoDir);
    Assert.Equal(0, runVm.ExitCode);
    Assert.Equal("Welcome to Pino! Result: 42\n", runVm.Stdout);

    CliResult runC = Run(["run", "--c"], workingDirectory: demoDir);
    Assert.Equal(0, runC.ExitCode);
    Assert.Equal("Welcome to Pino! Result: 42\n", runC.Stdout.Replace("\r\n", "\n", StringComparison.Ordinal));
  }

  [Fact]
  public void Compile_AndAliasC_WriteC99FileThatCompilesWithTcc() {
    WriteFile("main.pino", "println(\"compiled-c\", 40 + 2)\n");

    CliResult result = Run(["compile"]);
    Assert.Equal(0, result.ExitCode);
    Assert.Equal("pino: wrote main.c\n", result.Stdout);

    string mainC = Path.Combine(_root, "main.c");
    Assert.True(File.Exists(mainC));

    string exePath = Path.Combine(_root, OperatingSystem.IsWindows() ? "main_check.exe" : "main_check");
    CBuildResult build = CCompilerDriver.Build(new CBuildOptions(
        CSource: File.ReadAllText(mainC),
        OutputExecutablePath: exePath,
        Release: false,
        DebugRc: false));
    Assert.True(build.Succeeded, build.Stderr + build.Stdout);

    CExecResult exec = CCompilerDriver.RunExecutable(exePath, _root, failOnLeak: false, closeStdin: true);
    Assert.Equal(0, exec.ExitCode);
    Assert.Equal("compiled-c 42\n", exec.Stdout.Replace("\r\n", "\n", StringComparison.Ordinal));

    CliResult aliasResult = Run(["c", "main.pino", "out/custom.c"]);
    Assert.Equal(0, aliasResult.ExitCode);
    Assert.Equal("pino: wrote out/custom.c\n", aliasResult.Stdout);
    Assert.True(File.Exists(Path.Combine(_root, "out", "custom.c")));
  }

  [Fact]
  public void Watch_RunsImmediatelyAndRerunsOnTriggerWithoutSleeping() {
    string file = WriteFile("main.pino", "println(\"v1\")\n");
    int runs = 0;
    var driver = new FakeWatchDriver(trigger => {
      trigger(CancellationToken.None);
      runs++;
      File.WriteAllText(file, "println(\"v2\")\n");
      trigger(CancellationToken.None);
      runs++;
    });

    CliResult result = Run(["watch"], watchDriver: driver);

    Assert.Equal(0, result.ExitCode);
    Assert.Equal(2, runs);
    Assert.Contains("v1\n", result.Stdout, StringComparison.Ordinal);
    Assert.Contains("v2\n", result.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void Play_RunsNamedGameAndInteractiveMenu_AndPlayUpdateExtractsOnlyGamesPino() {
    string exeDir = Path.Combine(_root, "install");
    Directory.CreateDirectory(exeDir);
    string exePath = Path.Combine(exeDir, "pino.exe");
    File.WriteAllText(exePath, "fake-exe");

    byte[] archive = BuildZip(
        ("pino-lang-0.4.0/games/mini.pino", "println(\"playing mini\")\n"),
        ("pino-lang-0.4.0/games/README.md", "# ignore me\n"),
        ("pino-lang-0.4.0/pino.games/legacy.pino", "println(\"legacy\")\n"));

    var fetcher = new FakeHttpFetcher();
    string expectedUrl = $"https://github.com/OGShawnLee/pino-lang/archive/refs/tags/v{CliApp.GetVersion()}.zip";
    fetcher.Responses[expectedUrl] = new PinoHttpResponse(200, archive);

    CliResult update = Run(["play", "update"], executablePath: exePath, httpFetcher: fetcher);
    Assert.Equal(0, update.ExitCode);
    Assert.Contains("Installed 1 games in '", update.Stdout, StringComparison.Ordinal);
    Assert.True(File.Exists(Path.Combine(exeDir, "games", "mini.pino")));
    Assert.False(File.Exists(Path.Combine(exeDir, "games", "legacy.pino")));
    Assert.False(File.Exists(Path.Combine(exeDir, "games", "README.md")));

    CliResult named = Run(["play", "mini"], executablePath: exePath);
    Assert.Equal(0, named.ExitCode);
    Assert.Equal("playing mini\n", named.Stdout);

    CliResult menu = Run(["play"], stdinText: "1\n", executablePath: exePath);
    Assert.Equal(0, menu.ExitCode);
    Assert.Contains("1. mini", menu.Stdout, StringComparison.Ordinal);
    Assert.Contains("playing mini\n", menu.Stdout, StringComparison.Ordinal);

    CliResult menuExit = Run(["play"], stdinText: ".exit\n", executablePath: exePath);
    Assert.Equal(0, menuExit.ExitCode);
  }

  [Fact]
  public void Play_Handles404AndMissingGamesPrompt() {
    string exeDir = Path.Combine(_root, "empty_install");
    Directory.CreateDirectory(exeDir);
    string exePath = Path.Combine(exeDir, "pino.exe");
    File.WriteAllText(exePath, "fake-exe");

    var fetcher404 = new FakeHttpFetcher();
    string expectedUrl = $"https://github.com/OGShawnLee/pino-lang/archive/refs/tags/v{CliApp.GetVersion()}.zip";
    fetcher404.Responses[expectedUrl] = new PinoHttpResponse(404, []);

    CliResult notFound = Run(["play", "update"], executablePath: exePath, httpFetcher: fetcher404);
    Assert.Equal(1, notFound.ExitCode);
    Assert.Equal($"pino: no games were published for version {CliApp.GetVersion()}.\n", notFound.Stderr);

    CliResult promptNo = Run(["play"], stdinText: "n\n", executablePath: exePath, httpFetcher: fetcher404);
    Assert.Equal(0, promptNo.ExitCode);
    Assert.Contains("No games found. Download the official Pino games? (y/n): ", promptNo.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void ModulePanic_ReportsImportedModuleFileAndLine_OnBothVmAndCEngines() {
    WriteFile(Path.Combine("modules", "boom.pino"), """
        module boom

        pub fn explode() {
          val x = 1
          panic("from imported module")
        }
        """);
    WriteFile("main.pino", """
        from boom import explode

        fn main {
          explode()
        }
        """);

    CliResult vmRun = Run(["run", "--vm", "main.pino"]);
    CliResult cRun = Run(["run", "--c", "main.pino"]);

    Assert.Equal(101, vmRun.ExitCode);
    Assert.Equal(101, cRun.ExitCode);
    Assert.Contains("from imported module", vmRun.Stderr, StringComparison.Ordinal);
    Assert.Contains("modules/boom.pino:5:", vmRun.Stderr, StringComparison.Ordinal);
    Assert.Contains("from imported module", cRun.Stderr, StringComparison.Ordinal);
    Assert.Contains("modules/boom.pino:5:", cRun.Stderr, StringComparison.Ordinal);
  }

  [Fact]
  public void Test_RunsEmptyDirectoryWithNoTestsFoundAndExitsZero() {
    CliResult result = Run(["test"]);

    Assert.Equal(0, result.ExitCode);
    Assert.Equal("No tests found.\n", result.Stdout);
    Assert.Equal(string.Empty, result.Stderr);
  }

  [Theory]
  [InlineData("version")]
  [InlineData("v")]
  [InlineData("--version")]
  public void VersionCommands_PrintPinoVersionAndExitZero(string arg) {
    CliResult result = Run([arg]);

    Assert.Equal(0, result.ExitCode);
    Assert.Equal($"pino {CliApp.GetVersion()}\n", result.Stdout);
    Assert.Equal("pino 0.4.0\n", result.Stdout);
    Assert.Equal(string.Empty, result.Stderr);
  }

  [Fact]
  public void Update_HandlesUpToDateMissingAssetNetworkErrorAndWindowsInstall() {
    const string ReleasesUrl = "https://api.github.com/repos/OGShawnLee/pino-lang/releases/latest";

    // 1. Already up to date
    var upToDateFetcher = new FakeHttpFetcher();
    upToDateFetcher.Responses[ReleasesUrl] = new PinoHttpResponse(
        200,
        Encoding.UTF8.GetBytes("""{"tag_name":"v0.4.0","assets":[]}"""));
    CliResult current = Run(["update"], httpFetcher: upToDateFetcher, osPlatform: OSPlatform.Windows, architecture: Architecture.X64);
    Assert.Equal(0, current.ExitCode);
    Assert.Equal("Pino is already up to date (version 0.4.0).\n", current.Stdout);

    // 2. Missing asset for OS/arch
    var missingAssetFetcher = new FakeHttpFetcher();
    missingAssetFetcher.Responses[ReleasesUrl] = new PinoHttpResponse(
        200,
        Encoding.UTF8.GetBytes("""{"tag_name":"v0.5.0","assets":[{"name":"pino-linux-x64.tar.gz","browser_download_url":"https://example.invalid/linux.tar.gz"}]}"""));
    CliResult missingAsset = Run(["update"], httpFetcher: missingAssetFetcher, osPlatform: OSPlatform.OSX, architecture: Architecture.Arm64);
    Assert.Equal(1, missingAsset.ExitCode);
    Assert.Equal("pino: no release archive 'pino-macos-arm64.tar.gz' was found for v0.5.0.\n", missingAsset.Stderr);

    // 3. Network failure
    var failingFetcher = new FakeHttpFetcher { ThrowOnGet = new HttpRequestException("offline") };
    CliResult netErr = Run(["update"], httpFetcher: failingFetcher, osPlatform: OSPlatform.Windows, architecture: Architecture.X64);
    Assert.Equal(1, netErr.ExitCode);
    Assert.Equal("pino: could not check for updates: offline\n", netErr.Stderr);

    // 4. Full Windows install replacing executable (keeping pino.exe.old) + bundled tooling/runtime
    string installDir = Path.Combine(_root, "win_install");
    Directory.CreateDirectory(installDir);
    string exePath = Path.Combine(installDir, "pino.exe");
    File.WriteAllText(exePath, "old-binary");

    byte[] releaseZip = BuildZip(
        ("pino-windows-x64/pino.exe", "new-binary"),
        ("pino-windows-x64/tooling/tcc/tcc.exe", "new-tcc"),
        ("pino-windows-x64/runtime-re/pino_rt.h", "new-rt"));

    var installFetcher = new FakeHttpFetcher();
    installFetcher.Responses[ReleasesUrl] = new PinoHttpResponse(
        200,
        Encoding.UTF8.GetBytes("""{"tag_name":"v0.5.0","assets":[{"name":"pino-windows-x64.zip","browser_download_url":"https://example.invalid/pino-windows-x64.zip"}]}"""));
    installFetcher.Responses["https://example.invalid/pino-windows-x64.zip"] = new PinoHttpResponse(200, releaseZip);

    CliResult updated = Run(
        ["update"],
        stdinText: "y\n",
        executablePath: exePath,
        httpFetcher: installFetcher,
        osPlatform: OSPlatform.Windows,
        architecture: Architecture.X64);

    Assert.Equal(0, updated.ExitCode);
    Assert.Contains("Pino v0.5.0 is available (you have 0.4.0). Update now? (y/n): ", updated.Stdout, StringComparison.Ordinal);
    Assert.Contains("Updated Pino to v0.5.0.\n", updated.Stdout, StringComparison.Ordinal);
    Assert.Equal("new-binary", File.ReadAllText(exePath));
    Assert.Equal("old-binary", File.ReadAllText(exePath + ".old"));
    Assert.Equal("new-tcc", File.ReadAllText(Path.Combine(installDir, "tooling", "tcc", "tcc.exe")));
    Assert.Equal("new-rt", File.ReadAllText(Path.Combine(installDir, "runtime-re", "pino_rt.h")));
  }

  [Fact]
  public void Update_ExtractsTarGzForLinuxX64() {
    const string ReleasesUrl = "https://api.github.com/repos/OGShawnLee/pino-lang/releases/latest";
    string installDir = Path.Combine(_root, "linux_install");
    Directory.CreateDirectory(installDir);
    string exePath = Path.Combine(installDir, "pino");
    File.WriteAllText(exePath, "old-linux-bin");

    byte[] tarGz = BuildTarGz(
        ("pino-linux-x64/pino", "new-linux-bin"),
        ("pino-linux-x64/runtime-re/pino_rt.h", "linux-rt"));

    var fetcher = new FakeHttpFetcher();
    fetcher.Responses[ReleasesUrl] = new PinoHttpResponse(
        200,
        Encoding.UTF8.GetBytes("""{"tag_name":"v0.5.0","assets":[{"name":"pino-linux-x64.tar.gz","browser_download_url":"https://example.invalid/pino-linux-x64.tar.gz"}]}"""));
    fetcher.Responses["https://example.invalid/pino-linux-x64.tar.gz"] = new PinoHttpResponse(200, tarGz);

    CliResult result = Run(
        ["update"],
        stdinText: "y\n",
        executablePath: exePath,
        httpFetcher: fetcher,
        osPlatform: OSPlatform.Linux,
        architecture: Architecture.X64);

    Assert.Equal(0, result.ExitCode);
    Assert.Equal("new-linux-bin", File.ReadAllText(exePath));
    Assert.Equal("linux-rt", File.ReadAllText(Path.Combine(installDir, "runtime-re", "pino_rt.h")));
  }

  [Fact]
  public void Update_ReplacesWholeInstallFolderFor040ReleaseLayout() {
    const string ReleasesUrl = "https://api.github.com/repos/OGShawnLee/pino-lang/releases/latest";
    string installDir = Path.Combine(_root, "full_04_install");
    Directory.CreateDirectory(installDir);
    string exePath = Path.Combine(installDir, "pino.exe");
    File.WriteAllText(exePath, "old-0.4-binary");

    byte[] releaseZip = BuildZip(
        ("pino-windows-x64/pino.exe", "new-0.5-binary"),
        ("pino-windows-x64/runtime-re/pino_rt.h", "new-rt-h"),
        ("pino-windows-x64/runtime-re/pino_alloc.c", "new-alloc-c"),
        ("pino-windows-x64/runtime-re/pino_str.c", "new-str-c"),
        ("pino-windows-x64/runtime-re/pino_io.c", "new-io-c"),
        ("pino-windows-x64/runtime-re/pino_regex.c", "new-regex-c"),
        ("pino-windows-x64/runtime-re/pino_regex.h", "new-regex-h"),
        ("pino-windows-x64/runtime-re/pino_casefold.h", "new-casefold-h"),
        ("pino-windows-x64/tooling/tcc/tcc.exe", "new-tcc-exe"),
        ("pino-windows-x64/games/star_pino.pino", "println(\"star\")\n"),
        ("pino-windows-x64/games/jade_temple.pino", "println(\"jade\")\n"),
        ("pino-windows-x64/games/sable.pino", "println(\"sable\")\n"),
        ("pino-windows-x64/LICENSE", "MIT License"),
        ("pino-windows-x64/THIRD_PARTY_NOTICES.md", "TinyCC 0.9.27 LGPL-2.1"));

    var fetcher = new FakeHttpFetcher();
    fetcher.Responses[ReleasesUrl] = new PinoHttpResponse(
        200,
        Encoding.UTF8.GetBytes("""{"tag_name":"v0.5.0","assets":[{"name":"pino-windows-x64.zip","browser_download_url":"https://example.invalid/pino-windows-x64.zip"}]}"""));
    fetcher.Responses["https://example.invalid/pino-windows-x64.zip"] = new PinoHttpResponse(200, releaseZip);

    CliResult result = Run(
        ["update"],
        stdinText: "y\n",
        executablePath: exePath,
        httpFetcher: fetcher,
        osPlatform: OSPlatform.Windows,
        architecture: Architecture.X64);

    Assert.Equal(0, result.ExitCode);
    Assert.Equal("new-0.5-binary", File.ReadAllText(exePath));
    Assert.Equal("old-0.4-binary", File.ReadAllText(exePath + ".old"));
    Assert.Equal("new-rt-h", File.ReadAllText(Path.Combine(installDir, "runtime-re", "pino_rt.h")));
    Assert.Equal("new-regex-c", File.ReadAllText(Path.Combine(installDir, "runtime-re", "pino_regex.c")));
    Assert.Equal("new-regex-h", File.ReadAllText(Path.Combine(installDir, "runtime-re", "pino_regex.h")));
    Assert.Equal("new-tcc-exe", File.ReadAllText(Path.Combine(installDir, "tooling", "tcc", "tcc.exe")));
    Assert.Equal("println(\"star\")\n", File.ReadAllText(Path.Combine(installDir, "games", "star_pino.pino")));
    Assert.Equal("MIT License", File.ReadAllText(Path.Combine(installDir, "LICENSE")));
    Assert.Equal("TinyCC 0.9.27 LGPL-2.1", File.ReadAllText(Path.Combine(installDir, "THIRD_PARTY_NOTICES.md")));
  }

  private string WriteFile(string relativePath, string content) {
    string path = Path.Combine(_root, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
    return path;
  }

  private CliResult Run(
      string[] args,
      string stdinText = "",
      string? workingDirectory = null,
      string? executablePath = null,
      IPinoHttpFetcher? httpFetcher = null,
      IWatchDriver? watchDriver = null,
      OSPlatform? osPlatform = null,
      Architecture? architecture = null) {
    var stdin = new StringReader(stdinText);
    var stdout = new StringWriter { NewLine = "\n" };
    var stderr = new StringWriter { NewLine = "\n" };
    var env = new CliEnvironment(
        _ => null,
        IsOutputRedirected: true,
        SupportsAnsi: false,
        Notes: new Dictionary<string, string>(),
        WorkingDirectory: workingDirectory ?? _root,
        ExecutablePath: executablePath,
        HttpFetcher: httpFetcher,
        WatchDriver: watchDriver,
        OsPlatform: osPlatform,
        ProcessArchitecture: architecture);
    int exit = CliApp.Run(args, stdin, stdout, stderr, env);
    return new CliResult(exit, stdout.ToString(), stderr.ToString());
  }

  private static byte[] BuildZip(params (string EntryName, string Content)[] entries) {
    using var ms = new MemoryStream();
    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) {
      foreach ((string name, string content) in entries) {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
      }
    }

    return ms.ToArray();
  }

  private static byte[] BuildTarGz(params (string EntryName, string Content)[] entries) {
    using var ms = new MemoryStream();
    using (var gzip = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
    using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true)) {
      foreach ((string name, string content) in entries) {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name) {
          DataStream = new MemoryStream(bytes),
        };
        tar.WriteEntry(entry);
      }
    }

    return ms.ToArray();
  }

  private sealed record CliResult(int ExitCode, string Stdout, string Stderr);

  private sealed class FakeHttpFetcher : IPinoHttpFetcher {
    public Dictionary<string, PinoHttpResponse> Responses { get; } = new(StringComparer.Ordinal);

    public Exception? ThrowOnGet { get; init; }

    public PinoHttpResponse Get(string url) {
      if (ThrowOnGet is not null) {
        throw ThrowOnGet;
      }

      if (Responses.TryGetValue(url, out PinoHttpResponse? resp)) {
        return resp;
      }

      return new PinoHttpResponse(404, []);
    }
  }

  private sealed class FakeWatchDriver(Action<Action<CancellationToken>> script) : IWatchDriver {
    public void Watch(string directory, Action<CancellationToken> onRun) => script(onRun);
  }
}
