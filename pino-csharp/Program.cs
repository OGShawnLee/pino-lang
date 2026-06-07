using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

using Pino;

namespace pino_csharp;

class Program {
  static void Main(string[] args) {
    if (args.Length == 0) {
      var defaultPath = Path.Combine(System.Environment.CurrentDirectory, "main.pino");
      if (File.Exists(defaultPath)) {
        RunFile(defaultPath);
      } else {
        ShowHelp();
      }
      return;
    }

    var command = args[0].ToLower();

    switch (command) {
      case "h":
      case "help":
        ShowHelp();
        break;

      case "repl":
        StartRepl();
        break;

      case "run":
        var fileName = args.Length > 1 ? args[1] : "main.pino";
        var filePath = Path.Combine(System.Environment.CurrentDirectory, fileName);
        if (!File.Exists(filePath)) {
          Console.WriteLine($"Error: File '{fileName}' not found.");
          System.Environment.Exit(1);
        }
        RunFile(filePath);
        break;

      case "watch":
        bool useTranspiler = false;
        string watchFileName = "main.pino";

        for (int i = 1; i < args.Length; i++) {
          var arg = args[i];
          if (arg == "-t" || arg == "--transpile" || arg == "--compile") {
            useTranspiler = true;
          } else {
            watchFileName = arg;
          }
        }

        var watchFilePath = Path.Combine(System.Environment.CurrentDirectory, watchFileName);
        if (!File.Exists(watchFilePath)) {
          Console.WriteLine($"Error: File '{watchFileName}' not found.");
          System.Environment.Exit(1);
        }
        WatchFile(watchFilePath, useTranspiler);
        break;

      case "v":
      case "version":
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0";
        Console.WriteLine($"Pino version: {version} (.NET 10)");
        break;

      case "update":
        RunUpdate();
        break;

      case "compile":
        var compileFileName = args.Length > 1 ? args[1] : "main.pino";
        var compileFilePath = Path.Combine(System.Environment.CurrentDirectory, compileFileName);
        if (!File.Exists(compileFilePath)) {
          Console.WriteLine($"Error: File '{compileFileName}' not found.");
          System.Environment.Exit(1);
        }
        var outputPath = args.Length > 2 ? args[2] : null;
        CompileAndRun(compileFilePath, outputPath);
        break;

      default:
        Console.WriteLine("Invalid command. Type 'help' for usage.");
        break;
    }
  }

  static void ShowHelp() {
    Console.WriteLine("Usage: pino [command] [arguments]");
    Console.WriteLine("Commands:");
    Console.WriteLine("  help, h             : Display this help message");
    Console.WriteLine("  repl                : Start the Pino interactive REPL");
    Console.WriteLine("  run [file-name]     : Run the given .pino file (defaults to main.pino)");
    Console.WriteLine("  compile [file] [out]: Compile and run the file, or output to binary if output path is provided");
    Console.WriteLine("  watch [file] [-t]   : Monitor and execute (or transpile with -t) the file in real-time on save");
    Console.WriteLine("  version, v          : Show Pino version information");
    Console.WriteLine("  update              : Check for and install compiler updates");
    Console.WriteLine("  <empty>             : Run main.pino in current directory if exists");
  }

  static void RunUpdate() {
    Console.WriteLine("🌲 Checking for updates...");
    var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 2, 0);

    try {
      using var client = new HttpClient();
      client.DefaultRequestHeaders.UserAgent.ParseAdd("PinoCompiler-Updater");
      var responseStr = client.GetStringAsync("https://api.github.com/repos/OGShawnLee/pino-lang/releases/latest").Result;

      using var doc = System.Text.Json.JsonDocument.Parse(responseStr);
      var root = doc.RootElement;
      var tagName = root.GetProperty("tag_name").GetString();

      if (tagName == null || !tagName.StartsWith("v")) {
        Console.WriteLine("Error: Could not retrieve a valid version tag from GitHub.");
        return;
      }

      var cleanTagName = tagName.Substring(1);
      if (!Version.TryParse(cleanTagName, out var latestVersion)) {
        Console.WriteLine($"Error: Could not parse latest version tag '{tagName}'.");
        return;
      }

      if (latestVersion <= currentVersion) {
        Console.WriteLine($"Pino is already up to date (current version: {currentVersion.ToString(3)}).");
        return;
      }

      Console.WriteLine($"A new version of Pino is available: {tagName} (current version: {currentVersion.ToString(3)}).");
      Console.Write("Do you want to update? (y/n): ");
      var input = Console.ReadLine()?.Trim().ToLower();
      if (input != "y" && input != "yes") {
        Console.WriteLine("Update cancelled.");
        return;
      }

      // Detect OS and locate corresponding asset
      var isWindows = OperatingSystem.IsWindows();
      var isMac = OperatingSystem.IsMacOS();
      var isLinux = OperatingSystem.IsLinux();

      string? downloadUrl = null;
      string? assetName = null;

      foreach (var asset in root.GetProperty("assets").EnumerateArray()) {
        var name = asset.GetProperty("name").GetString() ?? "";
        if (isWindows && name.Contains("windows") && name.EndsWith(".zip")) {
          downloadUrl = asset.GetProperty("browser_download_url").GetString();
          assetName = name;
          break;
        } else if (isMac && name.Contains("macos") && name.EndsWith(".tar.gz")) {
          downloadUrl = asset.GetProperty("browser_download_url").GetString();
          assetName = name;
          break;
        } else if (isLinux && name.Contains("linux") && name.EndsWith(".tar.gz")) {
          downloadUrl = asset.GetProperty("browser_download_url").GetString();
          assetName = name;
          break;
        }
      }

      if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(assetName)) {
        Console.WriteLine("Error: No precompiled binaries found for your operating system.");
        return;
      }

      var tempPath = Path.Combine(Path.GetTempPath(), assetName);
      Console.WriteLine($"Downloading update from {downloadUrl}...");
      
      var bytes = client.GetByteArrayAsync(downloadUrl).Result;
      File.WriteAllBytes(tempPath, bytes);

      var tempExtractDir = Path.Combine(Path.GetTempPath(), "pino-update-extract");
      if (Directory.Exists(tempExtractDir)) {
        Directory.Delete(tempExtractDir, true);
      }
      Directory.CreateDirectory(tempExtractDir);

      Console.WriteLine("Extracting files...");
      if (assetName.EndsWith(".zip")) {
        System.IO.Compression.ZipFile.ExtractToDirectory(tempPath, tempExtractDir);
      } else if (assetName.EndsWith(".tar.gz")) {
        using var fs = File.OpenRead(tempPath);
        using var gzip = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
        System.Formats.Tar.TarFile.ExtractToDirectory(gzip, tempExtractDir, overwriteFiles: true);
      }

      // Locate new executable inside extraction folder
      var searchPattern = isWindows ? "pino.exe" : "pino";
      var extractedFiles = Directory.GetFiles(tempExtractDir, searchPattern, SearchOption.AllDirectories);
      if (extractedFiles.Length == 0) {
        // Fallback
        extractedFiles = Directory.GetFiles(tempExtractDir, isWindows ? "pino*.exe" : "pino*", SearchOption.AllDirectories);
      }

      if (extractedFiles.Length == 0) {
        Console.WriteLine("Error: Could not find the new pino executable inside the downloaded archive.");
        return;
      }

      var newBinPath = extractedFiles[0];
      var currentExePath = System.Environment.ProcessPath;
      if (string.IsNullOrEmpty(currentExePath)) {
        Console.WriteLine("Error: Could not locate the path of the currently executing process.");
        return;
      }

      Console.WriteLine("Replacing executable...");
      if (isWindows) {
        var oldExePath = currentExePath + ".old";
        if (File.Exists(oldExePath)) {
          try { File.Delete(oldExePath); } catch { /* Ignore */ }
        }
        File.Move(currentExePath, oldExePath);
        File.Copy(newBinPath, currentExePath, true);
      } else {
        File.Delete(currentExePath);
        File.Copy(newBinPath, currentExePath, true);

        // Chmod +x on Unix
        try {
          var chmodInfo = new System.Diagnostics.ProcessStartInfo("chmod", $"+x \"{currentExePath}\"") {
            CreateNoWindow = true,
            UseShellExecute = false
          };
          System.Diagnostics.Process.Start(chmodInfo)?.WaitForExit();
        } catch {
          // Ignore
        }
      }

      // Cleanup
      try {
        File.Delete(tempPath);
        Directory.Delete(tempExtractDir, true);
      } catch {
        // Ignore cleanup failures
      }

      Console.WriteLine($"🌲 Success! Pino has been updated to version {tagName}.");
    } catch (Exception ex) {
      Console.WriteLine($"Error occurred during update: {ex.Message}");
    }
  }

  static void RunFile(string path) {
    try {
      var program = Parser.ParseFile(path);
      var evaluator = new Evaluator();
      evaluator.Execute(program);
    } catch (Exception ex) {
      Console.WriteLine(ex.ToString());
    }
  }

  static void CompileAndRun(string path, string? outputPath) {
    try {
      var program = Parser.ParseFile(path);
      var transpiledCode = Transpiler.Transpile(program);

      var tempDir = Path.Combine(Path.GetTempPath(), "pino_transpile_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);

      try {
        var programCsPath = Path.Combine(tempDir, "Program.cs");
        File.WriteAllText(programCsPath, transpiledCode);

        var csprojContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>";
        var csprojPath = Path.Combine(tempDir, "pino_temp.csproj");
        File.WriteAllText(csprojPath, csprojContent);

        if (string.IsNullOrEmpty(outputPath)) {
          var startInfo = new System.Diagnostics.ProcessStartInfo {
            FileName = "dotnet",
            Arguments = $"run -c Release --project \"{csprojPath}\"",
            UseShellExecute = false
          };

          using var process = System.Diagnostics.Process.Start(startInfo);
          process?.WaitForExit();
        } else {
          var absOutputPath = Path.IsPathRooted(outputPath) 
            ? outputPath 
            : Path.Combine(System.Environment.CurrentDirectory, outputPath);

          var outputDir = Path.Combine(tempDir, "bin_out");
          var startInfo = new System.Diagnostics.ProcessStartInfo {
            FileName = "dotnet",
            Arguments = $"build -c Release -o \"{outputDir}\" --nologo \"{csprojPath}\"",
            UseShellExecute = false
          };

          using var process = System.Diagnostics.Process.Start(startInfo);
          process?.WaitForExit();

          if (process?.ExitCode == 0) {
            var ext = OperatingSystem.IsWindows() ? ".exe" : "";
            var generatedExe = Path.Combine(outputDir, "pino_temp" + ext);

            if (File.Exists(generatedExe)) {
              var parentDir = Path.GetDirectoryName(absOutputPath);
              if (!string.IsNullOrEmpty(parentDir)) {
                Directory.CreateDirectory(parentDir);
              }
              File.Copy(generatedExe, absOutputPath, true);
              Console.WriteLine($"🌲 Success! Compiled to {absOutputPath}");
            } else {
              Console.WriteLine("Error: Could not locate compiled executable.");
            }
          } else {
            Console.WriteLine("Error: Compilation failed.");
            File.WriteAllText(Path.Combine(System.Environment.CurrentDirectory, "../transpiled_debug.cs"), transpiledCode);
          }
        }
      } finally {
        try {
          Directory.Delete(tempDir, true);
        } catch {
          // Ignore
        }
      }
    } catch (Exception ex) {
      Console.WriteLine(ex.ToString());
    }
  }

  static void SafeClearConsole() {
    try {
      if (!Console.IsOutputRedirected) {
        Console.Clear();
      }
    } catch {
      // Ignore
    }
  }

  static void WatchFile(string path, bool useTranspiler) {
    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
    if (string.IsNullOrEmpty(exePath)) {
      throw new Exception("RUNTIME ERROR: Could not locate pino-csharp executable path.");
    }

    var directory = Path.GetDirectoryName(path);
    if (string.IsNullOrEmpty(directory)) {
      directory = System.Environment.CurrentDirectory;
    }

    object processLock = new object();
    System.Diagnostics.Process? childProcess = null;

    void StartChild() {
      lock (processLock) {
        if (childProcess != null && !childProcess.HasExited) {
          try {
            childProcess.Kill(true);
            childProcess.WaitForExit();
          } catch {
            // Ignore
          }
        }

        SafeClearConsole();
        string modeStr = useTranspiler ? "transpiled JIT" : "interpreted";
        Console.WriteLine($"[Pino Watcher] Monitoring '{Path.GetFileName(path)}' ({modeStr} mode)... Press Ctrl+C to stop.\n");

        var startInfo = new System.Diagnostics.ProcessStartInfo {
          FileName = exePath,
          Arguments = useTranspiler ? $"compile \"{path}\"" : $"run \"{path}\"",
          UseShellExecute = false
        };

        try {
          childProcess = System.Diagnostics.Process.Start(startInfo);
        } catch (Exception ex) {
          Console.WriteLine($"[Pino Watcher] Error starting process: {ex.Message}");
        }
      }
    }

    // Initial run
    StartChild();

    using var watcher = new FileSystemWatcher(directory, Path.GetFileName(path));
    watcher.NotifyFilter = NotifyFilters.LastWrite;

    DateTime lastRead = DateTime.MinValue;

    watcher.Changed += (sender, e) => {
      if (DateTime.UtcNow - lastRead < TimeSpan.FromMilliseconds(200)) return;
      lastRead = DateTime.UtcNow;

      System.Threading.Thread.Sleep(50);
      StartChild();
    };

    watcher.EnableRaisingEvents = true;

    while (true) {
      System.Threading.Thread.Sleep(1000);
    }
  }

  static void StartRepl() {
    Console.WriteLine("Welcome to the Pino REPL (.NET 10)");
    Console.WriteLine("Type '.exit' to stop the REPL.");
    var evaluator = new Evaluator();
    var globalEnv = new Pino.Environment();

    while (true) {
      Console.Write("> ");
      var line = Console.ReadLine();
      if (line == null || line == ".exit") break;
      if (string.IsNullOrWhiteSpace(line)) continue;

      try {
        var stmt = Parser.ParseString(line);
        if (stmt is Expression expr) {
          var val = evaluator.Evaluate(expr, globalEnv);
          if (val != null) {
            Console.WriteLine(FormatVal(val));
          }
        } else if (stmt != null) {
          evaluator.Execute(stmt, globalEnv);
        }
      } catch (Exception ex) {
        Console.WriteLine($"Error: {ex.Message}");
      }
    }
  }

  static string FormatVal(object? arg) {
    if (arg is List<object?> list) {
      return "[" + string.Join(", ", list.Select(FormatVal)) + "]";
    }
    return arg?.ToString() ?? "null";
  }
}
