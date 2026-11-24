using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

using Pino.Compiler.CodeGen.C;
using Pino.Compiler.Diagnostics;

namespace Pino.Cli;

public static partial class CliApp {
  private const string ReleasesLatestUrl =
      "https://api.github.com/repos/OGShawnLee/pino-lang/releases/latest";

  public static string GetVersion() {
    string? raw = typeof(CliApp).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    if (string.IsNullOrWhiteSpace(raw)) {
      Version? asmVer = typeof(CliApp).Assembly.GetName().Version;
      return asmVer is not null ? asmVer.ToString(3) : "0.4.0";
    }

    int plus = raw.IndexOf('+', StringComparison.Ordinal);
    return plus >= 0 ? raw[..plus] : raw;
  }

  // ---------------------------------------------------------------------------
  // `version` / `v` / `--version`
  // ---------------------------------------------------------------------------

  private static int RunVersion(List<string> args, TextWriter stdout) {
    if (args.Count > 0) {
      throw new CliUsageException($"unexpected argument '{args[0]}' for 'version'");
    }

    stdout.Write($"pino {GetVersion()}\n");
    return 0;
  }

  // ---------------------------------------------------------------------------
  // `init [name]`
  // ---------------------------------------------------------------------------

  private static int RunInit(List<string> args, TextWriter stdout, TextWriter stderr, CliEnvironment environment) {
    foreach (string arg in args) {
      if (arg.StartsWith('-')) {
        throw new CliUsageException($"unknown option '{arg}'");
      }
    }

    if (args.Count > 1) {
      throw new CliUsageException("'init' takes at most one project name");
    }

    string? projectName = args.Count == 1 ? args[0] : null;
    string baseDir = environment.EffectiveWorkingDirectory;
    string targetDir;
    string displayName;

    if (string.IsNullOrWhiteSpace(projectName)) {
      targetDir = Path.GetFullPath(baseDir);
      displayName = Path.GetFileName(targetDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
      if (string.IsNullOrEmpty(displayName)) {
        displayName = "current directory";
      }
    } else {
      targetDir = Path.IsPathRooted(projectName)
          ? Path.GetFullPath(projectName)
          : Path.GetFullPath(Path.Combine(baseDir, projectName));
      displayName = projectName;
    }

    string mainPinoPath = Path.Combine(targetDir, "main.pino");
    if (File.Exists(mainPinoPath)) {
      stderr.Write($"pino: 'main.pino' already exists in '{ToDisplayPath(targetDir)}'; nothing was created.\n");
      return 1;
    }

    Directory.CreateDirectory(targetDir);
    string modulesDir = Path.Combine(targetDir, "modules");
    string testDir = Path.Combine(targetDir, "test");
    Directory.CreateDirectory(modulesDir);
    Directory.CreateDirectory(testDir);

    const string MainSource = """
        from utils import add

        fn main {
          val result = add(20, 22)
          println("Welcome to Pino! Result: $result")
        }
        """ + "\n";

    const string UtilsSource = """
        module utils

        pub fn add(a int, b int) int {
          return a + b
        }
        """ + "\n";

    const string TestSource = """
        from utils import add

        test "should add numbers correctly" {
          assert(add(2, 3) == 5)
        }
        """ + "\n";

    const string GitIgnoreSource = """
        *.exe
        pino_output.c
        *.pdb
        *.c
        """ + "\n";

    var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    File.WriteAllText(mainPinoPath, MainSource, utf8NoBom);
    File.WriteAllText(Path.Combine(modulesDir, "utils.pino"), UtilsSource, utf8NoBom);
    File.WriteAllText(Path.Combine(testDir, "main_test.pino"), TestSource, utf8NoBom);
    File.WriteAllText(Path.Combine(targetDir, ".gitignore"), GitIgnoreSource, utf8NoBom);

    stdout.Write($"Initialized new Pino project in {displayName}\n");
    stdout.Write("  main.pino\n");
    stdout.Write("  modules/\n");
    stdout.Write("    utils.pino\n");
    stdout.Write("  test/\n");
    stdout.Write("    main_test.pino\n");
    stdout.Write("  .gitignore\n\n");
    stdout.Write("Get started:\n");
    if (!string.IsNullOrWhiteSpace(projectName)) {
      stdout.Write($"  cd {projectName}\n");
    }

    stdout.Write("  pino run\n");
    return 0;
  }

  // ---------------------------------------------------------------------------
  // `compile [file] [out.c]` (alias `c`)
  // ---------------------------------------------------------------------------

  private static int RunCompile(
      List<string> args,
      DiagnosticRenderOptions diagOptions,
      TextWriter stdout,
      TextWriter stderr,
      CliEnvironment environment) {
    foreach (string arg in args) {
      if (arg.StartsWith('-')) {
        throw new CliUsageException($"unknown option '{arg}'");
      }
    }

    if (args.Count > 2) {
      throw new CliUsageException("'compile' takes at most an input file and an output path");
    }

    string requestedInput = args.Count >= 1 ? args[0] : "main.pino";
    string inputPath = ValidateSingleFilePath(requestedInput, "compile", environment);

    if (!TryCompilePinoFileToC(inputPath, requestedInput, diagOptions, stderr, out string? cSource)) {
      return 1;
    }

    string outputPath;
    if (args.Count == 2) {
      outputPath = ResolvePath(args[1], environment);
    } else {
      outputPath = Path.ChangeExtension(inputPath, ".c");
    }

    string? parentDir = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(parentDir)) {
      Directory.CreateDirectory(parentDir);
    }

    File.WriteAllText(outputPath, cSource!, CEmitter.Utf8NoBom);
    string displayOut = args.Count == 2 ? ToDisplayPath(args[1]) : ToDisplayPath(Path.GetRelativePath(environment.EffectiveWorkingDirectory, outputPath));
    stdout.Write($"pino: wrote {displayOut}\n");
    return 0;
  }

  // ---------------------------------------------------------------------------
  // `watch [file] [--vm | --c]`
  // ---------------------------------------------------------------------------

  private static int RunWatch(
      List<string> args,
      DiagnosticRenderOptions diagOptions,
      TextReader stdin,
      TextWriter stdout,
      TextWriter stderr,
      CliEnvironment environment) {
    bool useVm = false;
    bool useC = false;
    bool verifyRc = false;
    bool debugRc = false;
    var files = new List<string>();

    for (int i = 0; i < args.Count; i++) {
      string arg = args[i];
      switch (arg) {
        case OptVm:
          useVm = true;
          break;
        case OptC:
          useC = true;
          break;
        case OptVerifyRc:
          verifyRc = true;
          break;
        case OptDebugRc:
          debugRc = true;
          break;
        default:
          if (arg.StartsWith('-')) {
            throw new CliUsageException($"unknown option '{arg}'");
          }

          files.Add(arg);
          break;
      }
    }

    if (useVm && useC) {
      throw new CliUsageException("cannot combine '--vm' and '--c'");
    }

    if (files.Count > 1) {
      throw new CliUsageException("'watch' takes at most one file");
    }

    string requestedFile = files.Count == 1 ? files[0] : "main.pino";
    string resolvedFile = ValidateSingleFilePath(requestedFile, "watch", environment);
    string watchDir = Path.GetDirectoryName(resolvedFile) ?? environment.EffectiveWorkingDirectory;
    IWatchDriver driver = environment.WatchDriver ?? DefaultWatchDriver.Instance;

    driver.Watch(watchDir, token => {
      if (!environment.IsOutputRedirected) {
        stdout.Write("\u001b[2J\u001b[H");
      }

      stdout.Write($"Watching '{ToDisplayPath(requestedFile)}'... Press Ctrl+C to stop.\n");
      stdout.Flush();

      if (useC) {
        _ = RunWithCEngine(resolvedFile, requestedFile, debugRc, diagOptions, stdout, stderr, environment, token);
      } else {
        _ = RunOnVm(resolvedFile, requestedFile, verifyRc, diagOptions, stdin, stdout, stderr, token);
      }

      stdout.Flush();
      stderr.Flush();
    });

    return 0;
  }

  // ---------------------------------------------------------------------------
  // `play [game]` and `play update`
  // ---------------------------------------------------------------------------

  private static int RunPlay(
      List<string> args,
      DiagnosticRenderOptions diagOptions,
      TextReader stdin,
      TextWriter stdout,
      TextWriter stderr,
      CliEnvironment environment) {
    foreach (string arg in args) {
      if (arg.StartsWith('-')) {
        throw new CliUsageException($"unknown option '{arg}'");
      }
    }

    if (args.Count > 1) {
      throw new CliUsageException("'play' takes at most one game name or 'update'");
    }

    string? target = args.Count == 1 ? args[0] : null;
    string exeGamesDir = Path.Combine(environment.EffectiveExecutableDirectory, "games");
    if (string.Equals(target, "update", StringComparison.Ordinal)) {
      return DownloadOfficialGames(exeGamesDir, stdout, stderr, environment);
    }

    string? gamesDir = FindGamesDirectory(environment);
    if (gamesDir is null) {
      stdout.Write("No games found. Download the official Pino games? (y/n): ");
      stdout.Flush();
      string? answer = stdin.ReadLine()?.Trim();
      if (string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
          || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase)) {
        int dlExit = DownloadOfficialGames(exeGamesDir, stdout, stderr, environment);
        if (dlExit != 0) {
          return dlExit;
        }

        gamesDir = exeGamesDir;
      } else {
        stdout.Write("Play cancelled.\n");
        return 0;
      }
    }

    if (!string.IsNullOrEmpty(target)) {
      string fileName = target.EndsWith(".pino", StringComparison.OrdinalIgnoreCase) ? target : target + ".pino";
      string gamePath = Path.Combine(gamesDir, fileName);
      if (!File.Exists(gamePath)) {
        stderr.Write($"pino: game '{fileName}' was not found in '{ToDisplayPath(gamesDir)}'.\n");
        return 1;
      }

      return RunGameOnVm(gamePath, fileName, diagOptions, stdin, stdout, stderr);
    }

    string[] files = Directory.GetFiles(gamesDir, "*.pino")
        .OrderBy(static f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (files.Length == 0) {
      stderr.Write($"pino: no .pino games found in '{ToDisplayPath(gamesDir)}'.\n");
      return 1;
    }

    stdout.Write("Select a game to play:\n");
    for (int i = 0; i < files.Length; i++) {
      stdout.Write(string.Create(
          CultureInfo.InvariantCulture,
          $"  {i + 1}. {Path.GetFileNameWithoutExtension(files[i])}\n"));
    }

    while (true) {
      stdout.Write(string.Create(CultureInfo.InvariantCulture, $"Choose a game (1-{files.Length}) or '.exit': "));
      stdout.Flush();
      string? input = stdin.ReadLine()?.Trim();
      if (input is null || string.Equals(input, ".exit", StringComparison.Ordinal)) {
        return 0;
      }

      if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int choice)
          && choice >= 1
          && choice <= files.Length) {
        string chosen = files[choice - 1];
        return RunGameOnVm(chosen, Path.GetFileName(chosen), diagOptions, stdin, stdout, stderr);
      }

      stdout.Write(string.Create(
          CultureInfo.InvariantCulture,
          $"Invalid selection. Enter a number from 1 to {files.Length} or '.exit'.\n"));
    }
  }

  private static int RunGameOnVm(
      string gamePath,
      string displayName,
      DiagnosticRenderOptions diagOptions,
      TextReader stdin,
      TextWriter stdout,
      TextWriter stderr) {
    using var eofCts = new CancellationTokenSource();
    using var guardedIn = new EofCancellingTextReader(stdin, eofCts);
    try {
      return RunOnVm(gamePath, displayName, verifyRc: false, diagOptions, guardedIn, stdout, stderr, eofCts.Token);
    } catch (OperationCanceledException) when (eofCts.IsCancellationRequested) {
      stdout.Flush();
      return 0;
    }
  }

  private sealed class EofCancellingTextReader(TextReader inner, CancellationTokenSource cts) : TextReader {
    private int _eofCount;

    public override string? ReadLine() {
      string? line = inner.ReadLine();
      if (line is null) {
        _eofCount++;
        if (_eofCount >= 2) {
          cts.Cancel();
        }
      } else {
        _eofCount = 0;
      }

      return line;
    }
  }

  private static string? FindGamesDirectory(CliEnvironment environment) {
    string localGames = Path.Combine(environment.EffectiveWorkingDirectory, "games");
    if (Directory.Exists(localGames)) {
      return localGames;
    }

    string exeGames = Path.Combine(environment.EffectiveExecutableDirectory, "games");
    if (Directory.Exists(exeGames)) {
      return exeGames;
    }

    return null;
  }

  private static int DownloadOfficialGames(
      string targetDir,
      TextWriter stdout,
      TextWriter stderr,
      CliEnvironment environment) {
    string version = GetVersion();
    string url = $"https://github.com/OGShawnLee/pino-lang/archive/refs/tags/v{version}.zip";
    IPinoHttpFetcher fetcher = environment.HttpFetcher ?? DefaultPinoHttpFetcher.Instance;

    PinoHttpResponse response;
    try {
      response = fetcher.Get(url);
    } catch (Exception ex) {
      stderr.Write($"pino: could not download games: {OneLine(ex.Message)}\n");
      return 1;
    }

    if (response.StatusCode == 404) {
      stderr.Write($"pino: no games were published for version {version}.\n");
      return 1;
    }

    if (response.StatusCode < 200 || response.StatusCode >= 300) {
      stderr.Write(string.Create(
          CultureInfo.InvariantCulture,
          $"pino: could not download games (HTTP {response.StatusCode}).\n"));
      return 1;
    }

    try {
      Directory.CreateDirectory(targetDir);
      int count = 0;
      using var ms = new MemoryStream(response.Bytes);
      using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
      foreach (ZipArchiveEntry entry in archive.Entries) {
        string[] parts = entry.FullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Extract only top-level <repo-root>/games/<file>.pino (never pino.games/).
        if (parts.Length == 3
            && string.Equals(parts[1], "games", StringComparison.Ordinal)
            && parts[2].EndsWith(".pino", StringComparison.OrdinalIgnoreCase)) {
          string destPath = Path.Combine(targetDir, parts[2]);
          entry.ExtractToFile(destPath, overwrite: true);
          count++;
        }
      }

      stdout.Write(string.Create(
          CultureInfo.InvariantCulture,
          $"Installed {count} games in '{ToDisplayPath(targetDir)}'.\n"));
      return 0;
    } catch (Exception ex) {
      stderr.Write($"pino: could not extract games archive: {OneLine(ex.Message)}\n");
      return 1;
    }
  }

  // ---------------------------------------------------------------------------
  // `update`
  // ---------------------------------------------------------------------------

  private static int RunUpdate(
      List<string> args,
      TextReader stdin,
      TextWriter stdout,
      TextWriter stderr,
      CliEnvironment environment) {
    if (args.Count > 0) {
      throw new CliUsageException($"unexpected argument '{args[0]}' for 'update'");
    }

    string currentVersionText = GetVersion();
    if (!Version.TryParse(currentVersionText, out Version? currentVersion)) {
      currentVersion = new Version(0, 4, 0);
    }

    IPinoHttpFetcher fetcher = environment.HttpFetcher ?? DefaultPinoHttpFetcher.Instance;
    PinoHttpResponse metaResponse;
    try {
      metaResponse = fetcher.Get(ReleasesLatestUrl);
    } catch (Exception ex) {
      stderr.Write($"pino: could not check for updates: {OneLine(ex.Message)}\n");
      return 1;
    }

    if (metaResponse.StatusCode < 200 || metaResponse.StatusCode >= 300) {
      stderr.Write(string.Create(
          CultureInfo.InvariantCulture,
          $"pino: could not check for updates (HTTP {metaResponse.StatusCode}).\n"));
      return 1;
    }

    string tagName;
    string? downloadUrl = null;
    string? expectedAsset = ExpectedReleaseAssetName(environment.EffectiveOsPlatform, environment.EffectiveArchitecture);
    if (expectedAsset is null) {
      stderr.Write("pino: precompiled updates are not available for this operating system and architecture.\n");
      return 1;
    }

    try {
      using JsonDocument doc = JsonDocument.Parse(metaResponse.Bytes);
      JsonElement root = doc.RootElement;
      if (!root.TryGetProperty("tag_name", out JsonElement tagEl) || tagEl.GetString() is not { } tag || tag.Length == 0) {
        stderr.Write("pino: could not read a valid version tag from GitHub.\n");
        return 1;
      }

      tagName = tag;
      string cleanTag = tagName.StartsWith('v') || tagName.StartsWith('V') ? tagName[1..] : tagName;
      if (!Version.TryParse(cleanTag, out Version? latestVersion)) {
        stderr.Write($"pino: could not parse release version '{tagName}'.\n");
        return 1;
      }

      if (latestVersion <= currentVersion) {
        stdout.Write($"Pino is already up to date (version {currentVersionText}).\n");
        return 0;
      }

      if (root.TryGetProperty("assets", out JsonElement assetsEl) && assetsEl.ValueKind == JsonValueKind.Array) {
        foreach (JsonElement asset in assetsEl.EnumerateArray()) {
          string? name = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
          if (string.Equals(name, expectedAsset, StringComparison.Ordinal)) {
            downloadUrl = asset.TryGetProperty("browser_download_url", out JsonElement u) ? u.GetString() : null;
            break;
          }
        }
      }
    } catch (JsonException ex) {
      stderr.Write($"pino: could not parse update response: {OneLine(ex.Message)}\n");
      return 1;
    }

    if (string.IsNullOrEmpty(downloadUrl)) {
      stderr.Write($"pino: no release archive '{expectedAsset}' was found for {tagName}.\n");
      return 1;
    }

    stdout.Write($"Pino {tagName} is available (you have {currentVersionText}). Update now? (y/n): ");
    stdout.Flush();
    string? answer = stdin.ReadLine()?.Trim();
    if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase)) {
      stdout.Write("Update cancelled.\n");
      return 0;
    }

    PinoHttpResponse archiveResponse;
    try {
      archiveResponse = fetcher.Get(downloadUrl);
    } catch (Exception ex) {
      stderr.Write($"pino: could not download update: {OneLine(ex.Message)}\n");
      return 1;
    }

    if (archiveResponse.StatusCode < 200 || archiveResponse.StatusCode >= 300) {
      stderr.Write(string.Create(
          CultureInfo.InvariantCulture,
          $"pino: could not download update (HTTP {archiveResponse.StatusCode}).\n"));
      return 1;
    }

    string? currentExePath = environment.EffectiveExecutablePath;
    if (string.IsNullOrEmpty(currentExePath)) {
      stderr.Write("pino: could not locate the current pino executable.\n");
      return 1;
    }

    string installDir = environment.EffectiveExecutableDirectory;
    string tempDir = Path.Combine(Path.GetTempPath(), "pino-update-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);

    try {
      if (expectedAsset.EndsWith(".zip", StringComparison.Ordinal)) {
        using var ms = new MemoryStream(archiveResponse.Bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.ExtractToDirectory(tempDir, overwriteFiles: true);
      } else if (expectedAsset.EndsWith(".tar.gz", StringComparison.Ordinal)) {
        using var ms = new MemoryStream(archiveResponse.Bytes);
        using var gzip = new GZipStream(ms, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, tempDir, overwriteFiles: true);
      }

      string sourceRoot = FindExtractedInstallRoot(tempDir, environment.EffectiveOsPlatform == OSPlatform.Windows);
      ReplaceInstallDirectory(sourceRoot, installDir, currentExePath, environment.EffectiveOsPlatform == OSPlatform.Windows);
      stdout.Write($"Updated Pino to {tagName}.\n");
      return 0;
    } catch (Exception ex) {
      stderr.Write($"pino: could not install update: {OneLine(ex.Message)}\n");
      return 1;
    } finally {
      try {
        Directory.Delete(tempDir, recursive: true);
      } catch (IOException) {
      } catch (UnauthorizedAccessException) {
      }
    }
  }

  private static string? ExpectedReleaseAssetName(OSPlatform os, Architecture arch) {
    if (os == OSPlatform.Windows && arch == Architecture.X64) {
      return "pino-windows-x64.zip";
    }

    if (os == OSPlatform.Linux && arch == Architecture.X64) {
      return "pino-linux-x64.tar.gz";
    }

    if (os == OSPlatform.OSX && arch == Architecture.X64) {
      return "pino-macos-x64.tar.gz";
    }

    if (os == OSPlatform.OSX && arch == Architecture.Arm64) {
      return "pino-macos-arm64.tar.gz";
    }

    return null;
  }

  private static string FindExtractedInstallRoot(string extractDir, bool isWindows) {
    string exeName = isWindows ? "pino.exe" : "pino";
    string[] matches = Directory.GetFiles(extractDir, exeName, SearchOption.AllDirectories);
    if (matches.Length > 0) {
      return Path.GetDirectoryName(matches[0]) ?? extractDir;
    }

    string[] subDirs = Directory.GetDirectories(extractDir);
    if (subDirs.Length == 1 && Directory.GetFiles(extractDir).Length == 0) {
      return subDirs[0];
    }

    return extractDir;
  }

  private static void ReplaceInstallDirectory(string sourceRoot, string installDir, string currentExePath, bool isWindows) {
    Directory.CreateDirectory(installDir);
    string exeFileName = Path.GetFileName(currentExePath);

    foreach (string srcFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)) {
      string rel = Path.GetRelativePath(sourceRoot, srcFile);
      string destFile = Path.Combine(installDir, rel);
      string? destParent = Path.GetDirectoryName(destFile);
      if (!string.IsNullOrEmpty(destParent)) {
        Directory.CreateDirectory(destParent);
      }

      bool isCurrentExe = string.Equals(Path.GetFullPath(destFile), Path.GetFullPath(currentExePath), StringComparison.OrdinalIgnoreCase)
          || string.Equals(rel, exeFileName, StringComparison.OrdinalIgnoreCase);

      if (isWindows && isCurrentExe && File.Exists(destFile)) {
        string oldExe = destFile + ".old";
        if (File.Exists(oldExe)) {
          try {
            File.Delete(oldExe);
          } catch (IOException) {
          } catch (UnauthorizedAccessException) {
          }
        }

        File.Move(destFile, oldExe);
        File.Copy(srcFile, destFile, overwrite: true);
      } else {
        File.Copy(srcFile, destFile, overwrite: true);
      }
    }

    if (!isWindows && !OperatingSystem.IsWindows()) {
      string[] unixExecutables = [
        Path.Combine(installDir, exeFileName),
        Path.Combine(installDir, "tooling", "tcc", "tcc"),
      ];
      foreach (string targetExe in unixExecutables) {
        if (!File.Exists(targetExe)) {
          continue;
        }

        try {
          using Process? p = Process.Start(new ProcessStartInfo("chmod", $"+x \"{targetExe}\"") {
            CreateNoWindow = true,
            UseShellExecute = false,
          });
          p?.WaitForExit();
        } catch (Exception) {
          // Best-effort permission update on Unix.
        }
      }
    }
  }
}
