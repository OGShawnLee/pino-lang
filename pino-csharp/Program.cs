using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

using Pino;

namespace pino_csharp;

class Program {
  static void Main(string[] args) {
    bool useVM = false;
    bool watchCompile = false;
    var argList = new List<string>(args);
    if (argList.Contains("--vm")) {
      useVM = true;
      argList.Remove("--vm");
    }
    if (argList.Contains("--c") || argList.Contains("--compile")) {
      watchCompile = true;
      argList.Remove("--c");
      argList.Remove("--compile");
    }

    if (argList.Count == 0) {
      var defaultPath = Path.Combine(System.Environment.CurrentDirectory, "main.pino");
      if (File.Exists(defaultPath)) {
        RunFile(defaultPath, useVM);
      } else {
        ShowHelp();
      }
      return;
    }

    var command = argList[0].ToLower();

    switch (command) {
      case "h":
      case "help":
        ShowHelp();
        break;

      case "repl":
        StartRepl();
        break;

      case "run":
        var fileName = argList.Count > 1 ? argList[1] : "main.pino";
        var filePath = Path.Combine(System.Environment.CurrentDirectory, fileName);
        if (!File.Exists(filePath)) {
          Console.WriteLine($"Error: File '{fileName}' not found.");
          System.Environment.Exit(1);
        }
        if (watchCompile) {
          CompileAndRunWatch(filePath);
        } else {
          RunFile(filePath, useVM);
        }
        break;

      case "watch":
        var watchFileName = argList.Count > 1 ? argList[1] : "main.pino";
        var watchFilePath = Path.Combine(System.Environment.CurrentDirectory, watchFileName);
        if (!File.Exists(watchFilePath)) {
          Console.WriteLine($"Error: File '{watchFileName}' not found.");
          System.Environment.Exit(1);
        }
        WatchFile(watchFilePath, useVM, watchCompile);
        break;

      case "v":
      case "version":
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.3.2";
        Console.WriteLine($"Pino version: {version} (.NET 10)");
        break;

      case "update":
        RunUpdate();
        break;

      case "play":
        PlayGame(argList.Count > 1 ? argList[1] : null);
        break;

      case "compile":
        var compileFileName = argList.Count > 1 ? argList[1] : "main.pino";
        var compileFilePath = Path.Combine(System.Environment.CurrentDirectory, compileFileName);
        if (!File.Exists(compileFilePath)) {
          Console.WriteLine($"Error: File '{compileFileName}' not found.");
          System.Environment.Exit(1);
        }
        CompileFile(compileFilePath);
        break;

      default:
        Console.WriteLine("Invalid command. Type 'help' for usage.");
        break;
    }
  }

  static void ShowHelp() {
    Console.WriteLine("Usage: pino [command] [arguments] [flags]");
    Console.WriteLine("Commands:");
    Console.WriteLine("  help, h             : Display this help message");
    Console.WriteLine("  repl                : Start the Pino interactive REPL");
    Console.WriteLine("  run [file-name]     : Run the given .pino file (defaults to main.pino)");
    Console.WriteLine("  compile [file-name] : Compile the given .pino file to a native executable (defaults to main.pino)");
    Console.WriteLine("  watch [file-name]   : Monitor and execute the file in real-time on save (defaults to main.pino)");
    Console.WriteLine("  play [game-name]    : Launch an interactive console game from the pino.games directory");
    Console.WriteLine("  play update         : Download or update the official Pino games library from GitHub");
    Console.WriteLine("  version, v          : Show Pino version information");
    Console.WriteLine("  update              : Check for and install compiler updates");
    Console.WriteLine("  <empty>             : Run main.pino in current directory if exists");
    Console.WriteLine();
    Console.WriteLine("Flags:");
    Console.WriteLine("  --vm                : Run code using the bytecode VM (available for run, watch)");
    Console.WriteLine("  --c, --compile      : Transpile to C, compile using TCC, execute, and auto-delete binary (available for run, watch)");
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

  static void RunFile(string path, bool useVM = false) {
    try {
      var program = Parser.ParseFile(path);
      var checker = new Checker();
      checker.Check(program);

      bool hasMainFunc = false;
      bool hasExplicitMainCall = false;
      foreach (var stmt in program.Statements) {
        if (stmt is FunctionDeclaration fnDecl && fnDecl.Identifier == "main") {
          hasMainFunc = true;
        }
        if (stmt is FunctionCallExpression call && call.Callee == "main") {
          hasExplicitMainCall = true;
        }
      }

      if (useVM) {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("⚠️ [PinoVM - EXPERIMENTAL]: The PinoVM engine is experimental and currently supports only pure calculation, functions, and basic control flow. Complex structures will not work and will fail.");
        Console.ResetColor();

        var compiler = new Compiler();
        var vmFn = compiler.Compile(program);
        var evaluator = new Evaluator();
        var vm = new VM(evaluator, evaluator.Globals);
        vm.Execute(vmFn);

        if (hasMainFunc && !hasExplicitMainCall) {
          if (evaluator.Globals.Exists("main") && evaluator.Globals.Get("main") is IPinoCallable mainCallable && mainCallable.Arity == 0) {
            var result = mainCallable.Call(evaluator, new List<object?>());
            HandleMainResult(result);
          }
        }
      } else {
        var evaluator = new Evaluator();
        evaluator.Execute(program);

        if (hasMainFunc && !hasExplicitMainCall) {
          if (evaluator.Globals.Exists("main") && evaluator.Globals.Get("main") is IPinoCallable mainCallable && mainCallable.Arity == 0) {
            var result = mainCallable.Call(evaluator, new List<object?>());
            HandleMainResult(result);
          }
        }
      }
    } catch (PinoPanicException ex) {
      Console.ForegroundColor = ConsoleColor.Red;
      Console.Error.WriteLine($"thread 'main' panicked at '{ex.PanicMessage}'");
      if (ex.CallStack != null && ex.CallStack.Count > 0) {
        Console.Error.WriteLine("stack backtrace:");
        int idx = 0;
        foreach (var frame in ex.CallStack) {
          Console.Error.WriteLine($"   {idx++}: {frame}");
        }
      }
      Console.ResetColor();
      System.Environment.Exit(101);
    } catch (Exception ex) {
      Console.WriteLine(ex.ToString());
    }
  }

  private static void HandleMainResult(object? result) {
    if (result is PinoUnionValue unionVal) {
      if (unionVal.VariantName == "Failure") {
        Console.ForegroundColor = ConsoleColor.Red;
        var errPayload = unionVal.Payload.Count > 0 ? unionVal.Payload[0] : "";
        Console.Error.WriteLine($"Error: {errPayload}");
        Console.ResetColor();
        System.Environment.Exit(1);
      } else if (unionVal.VariantName == "None") {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine("Error: Option::None");
        Console.ResetColor();
        System.Environment.Exit(1);
      }
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

  static void WatchFile(string path, bool useVM = false, bool watchCompile = false) {
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

        if (watchCompile) {
          SafeClearConsole();
          Console.WriteLine($"[Pino Watcher] Monitoring and compiling '{Path.GetFileName(path)}'... Press Ctrl+C to stop.\n");
          CompileAndRunWatch(path);
        } else {
          SafeClearConsole();
          Console.WriteLine($"[Pino Watcher] Monitoring '{Path.GetFileName(path)}'... Press Ctrl+C to stop.\n");

          var startInfo = new System.Diagnostics.ProcessStartInfo {
            FileName = exePath,
            Arguments = $"run \"{path}\"{(useVM ? " --vm" : "")}",
            UseShellExecute = false
          };

          try {
            childProcess = System.Diagnostics.Process.Start(startInfo);
          } catch (Exception ex) {
            Console.WriteLine($"[Pino Watcher] Error starting process: {ex.Message}");
          }
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

  static void CompileAndRunWatch(string path) {
    try {
      var program = Parser.ParseFile(path);
      var checker = new Checker();
      checker.Check(program);

      // Transpile to C code
      var transpiler = new TranspilerC();
      var cCode = transpiler.Transpile(program);

      var currentDir = System.Environment.CurrentDirectory;
      var cFilePath = Path.Combine(currentDir, "pino_output.c");
      File.WriteAllText(cFilePath, cCode);

      string? tccDir = null;
      var dir = new DirectoryInfo(AppContext.BaseDirectory);
      while (dir != null) {
        var potentialTcc = Path.Combine(dir.FullName, "tooling", "tcc", "tcc.exe");
        if (File.Exists(potentialTcc)) {
          tccDir = Path.Combine(dir.FullName, "tooling", "tcc");
          break;
        }
        dir = dir.Parent;
      }

      if (tccDir == null) {
        var localTcc = Path.Combine(System.Environment.CurrentDirectory, "tooling", "tcc", "tcc.exe");
        if (File.Exists(localTcc)) {
          tccDir = Path.Combine(System.Environment.CurrentDirectory, "tooling", "tcc");
        }
      }

      if (tccDir == null) {
        var targetTccDir = Path.Combine(System.Environment.CurrentDirectory, "tooling", "tcc");
        DownloadTcc(targetTccDir);
        tccDir = targetTccDir;
      }

      var tccPath = Path.Combine(tccDir, "tcc.exe");
      var runtimeCPath = Path.Combine(tccDir, "..", "..", "runtime", "runtime.c");
      var runtimeCInfo = new FileInfo(runtimeCPath);
      if (!runtimeCInfo.Exists) {
        runtimeCPath = Path.Combine(System.Environment.CurrentDirectory, "runtime", "runtime.c");
      }
      var reCPath = Path.Combine(Path.GetDirectoryName(runtimeCPath)!, "re.c");

      var outputExeName = Path.GetFileNameWithoutExtension(path) + ".exe";
      var outputExePath = Path.Combine(currentDir, outputExeName);

      var hasGc = false;
      var libDir = Path.Combine(tccDir, "lib");
      if (Directory.Exists(libDir)) {
        if (File.Exists(Path.Combine(libDir, "gc.lib")) || File.Exists(Path.Combine(libDir, "libgc.a"))) {
          hasGc = true;
        }
      }
      var gcFlags = hasGc ? "-DPINO_GC -lgc" : "";

      var startInfo = new System.Diagnostics.ProcessStartInfo {
        FileName = tccPath,
        Arguments = $"{gcFlags} \"{cFilePath}\" \"{runtimeCPath}\" \"{reCPath}\" -o \"{outputExePath}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var process = System.Diagnostics.Process.Start(startInfo);
      if (process == null) {
        Console.WriteLine("Error: Failed to start TCC compiler process.");
        return;
      }
      process.WaitForExit();

      var stdout = process.StandardOutput.ReadToEnd();
      var stderr = process.StandardError.ReadToEnd();

      if (process.ExitCode != 0) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[ERROR] TCC compilation failed:");
        Console.WriteLine(stderr);
        Console.ResetColor();
        return;
      }

      try {
        File.Delete(cFilePath);
      } catch { /* Ignore */ }

      // Successfully compiled! Now execute it.
      Console.ForegroundColor = ConsoleColor.Green;
      Console.WriteLine($"[SYSTEM] Compiled successfully: {outputExeName}");
      Console.ResetColor();
      Console.WriteLine("--- Execution Output ---");

      var execStartInfo = new System.Diagnostics.ProcessStartInfo {
        FileName = outputExePath,
        UseShellExecute = false
      };

      using var execProcess = System.Diagnostics.Process.Start(execStartInfo);
      if (execProcess != null) {
        execProcess.WaitForExit();
      }

      Console.WriteLine("------------------------");

      // Delete the executable after execution
      try {
        File.Delete(outputExePath);
      } catch { /* Ignore */ }

    } catch (Exception ex) {
      Console.ForegroundColor = ConsoleColor.Red;
      Console.WriteLine($"Error compiling program: {ex.Message}");
      Console.ResetColor();
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

  static void PlayGame(string? gameName) {
    var gamesDir = Path.Combine(System.Environment.CurrentDirectory, "pino.games");
    if (!Directory.Exists(gamesDir)) {
      gamesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pino.games");
    }

    if (gameName == "--update" || gameName == "update") {
      DownloadGames(gamesDir);
      return;
    }

    if (!Directory.Exists(gamesDir)) {
      Console.WriteLine("pino.games directory not found locally.");
      Console.Write("Would you like to download and install the official Pino games? (y/n): ");
      var ans = Console.ReadLine()?.Trim().ToLower();
      if (ans == "y" || ans == "yes") {
        gamesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pino.games");
        DownloadGames(gamesDir);
      } else {
        Console.WriteLine("Play command aborted.");
        return;
      }
    }

    if (!Directory.Exists(gamesDir)) {
      Console.WriteLine("Error: Games directory does not exist.");
      return;
    }

    if (!string.IsNullOrEmpty(gameName)) {
      if (!gameName.EndsWith(".pino")) {
        gameName += ".pino";
      }
      var gamePath = Path.Combine(gamesDir, gameName);
      if (!File.Exists(gamePath)) {
        Console.WriteLine($"Error: Game '{gameName}' not found in '{gamesDir}'.");
        return;
      }
      RunFile(gamePath);
      return;
    }

    var files = Directory.GetFiles(gamesDir, "*.pino");
    if (files.Length == 0) {
      Console.WriteLine($"Error: No .pino games found in '{gamesDir}'.");
      return;
    }

    Console.WriteLine("================================================");
    Console.WriteLine("          🎮 PINO GAMES STATION 🎮             ");
    Console.WriteLine("================================================");
    Console.WriteLine("Select a game to play:");
    for (int i = 0; i < files.Length; i++) {
      Console.WriteLine($" {i + 1}. {Path.GetFileNameWithoutExtension(files[i])}");
    }
    Console.WriteLine("================================================");

    while (true) {
      Console.Write($"Choose game (1-{files.Length}) or '.exit': ");
      var input = Console.ReadLine()?.Trim();
      if (input == ".exit") {
        break;
      }
      if (int.TryParse(input, out var idx) && idx >= 1 && idx <= files.Length) {
        var selectedGame = files[idx - 1];
        SafeClearConsole();
        RunFile(selectedGame);
        break;
      }
      Console.WriteLine("Invalid selection. Please try again.");
    }
  }

  static void DownloadGames(string targetDir) {
    Console.WriteLine("🌲 Downloading Pino games repository...");
    try {
      using var client = new HttpClient();
      client.DefaultRequestHeaders.UserAgent.ParseAdd("PinoCompiler-GamesDownloader");

      var zipBytes = client.GetByteArrayAsync("https://github.com/OGShawnLee/pino-lang/archive/refs/heads/main.zip").Result;

      using var ms = new MemoryStream(zipBytes);
      using var archive = new ZipArchive(ms);

      Directory.CreateDirectory(targetDir);

      int count = 0;
      foreach (var entry in archive.Entries) {
        var parts = entry.FullName.Split('/');
        if (parts.Length > 2 && parts[1] == "pino.games" && !string.IsNullOrEmpty(parts[2])) {
          var destPath = Path.Combine(targetDir, parts[2]);
          entry.ExtractToFile(destPath, overwrite: true);
          count++;
        }
      }

      Console.WriteLine($"🌲 Success! Downloaded and installed {count} games in '{targetDir}'.");
    } catch (Exception ex) {
      Console.WriteLine($"Error downloading games: {ex.Message}");
    }
  }

  static void CompileFile(string path) {
    try {
      var program = Parser.ParseFile(path);
      var checker = new Checker();
      checker.Check(program);

      // Transpile to C code
      var transpiler = new TranspilerC();
      var cCode = transpiler.Transpile(program);

      var currentDir = System.Environment.CurrentDirectory;
      var cFilePath = Path.Combine(currentDir, "pino_output.c");
      File.WriteAllText(cFilePath, cCode);
      Console.WriteLine($"[SYSTEM] C code generated at: {cFilePath}");

      string? tccDir = null;
      var dir = new DirectoryInfo(AppContext.BaseDirectory);
      while (dir != null) {
        var potentialTcc = Path.Combine(dir.FullName, "tooling", "tcc", "tcc.exe");
        if (File.Exists(potentialTcc)) {
          tccDir = Path.Combine(dir.FullName, "tooling", "tcc");
          break;
        }
        dir = dir.Parent;
      }

      if (tccDir == null) {
        var localTcc = Path.Combine(System.Environment.CurrentDirectory, "tooling", "tcc", "tcc.exe");
        if (File.Exists(localTcc)) {
          tccDir = Path.Combine(System.Environment.CurrentDirectory, "tooling", "tcc");
        }
      }

      if (tccDir == null) {
        var targetTccDir = Path.Combine(System.Environment.CurrentDirectory, "tooling", "tcc");
        DownloadTcc(targetTccDir);
        tccDir = targetTccDir;
      }

      var tccPath = Path.Combine(tccDir, "tcc.exe");
      var runtimeCPath = Path.Combine(tccDir, "..", "..", "runtime", "runtime.c");
      var runtimeCInfo = new FileInfo(runtimeCPath);
      if (!runtimeCInfo.Exists) {
        runtimeCPath = Path.Combine(System.Environment.CurrentDirectory, "runtime", "runtime.c");
      }
      var reCPath = Path.Combine(Path.GetDirectoryName(runtimeCPath)!, "re.c");

      var outputExeName = Path.GetFileNameWithoutExtension(path) + ".exe";
      var outputExePath = Path.Combine(currentDir, outputExeName);

      var hasGc = false;
      var libDir = Path.Combine(tccDir, "lib");
      if (Directory.Exists(libDir)) {
        if (File.Exists(Path.Combine(libDir, "gc.lib")) || File.Exists(Path.Combine(libDir, "libgc.a"))) {
          hasGc = true;
        }
      }
      var gcFlags = hasGc ? "-DPINO_GC -lgc" : "";

      var startInfo = new System.Diagnostics.ProcessStartInfo {
        FileName = tccPath,
        Arguments = $"{gcFlags} \"{cFilePath}\" \"{runtimeCPath}\" \"{reCPath}\" -o \"{outputExePath}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      Console.WriteLine($"[SYSTEM] Compiling native binary using TCC...");
      Console.WriteLine($"[DEBUG] TCC Arguments: {startInfo.Arguments}");
      using var process = System.Diagnostics.Process.Start(startInfo);
      if (process == null) {
        Console.WriteLine("Error: Failed to start TCC compiler process.");
        System.Environment.Exit(1);
      }
      process.WaitForExit();

      var stdout = process.StandardOutput.ReadToEnd();
      var stderr = process.StandardError.ReadToEnd();

      if (process.ExitCode != 0) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[ERROR] TCC compilation failed:");
        Console.WriteLine(stderr);
        Console.ResetColor();
        System.Environment.Exit(1);
      }

      try {
        File.Delete(cFilePath);
      } catch { /* Ignore */ }

      Console.ForegroundColor = ConsoleColor.Green;
      Console.WriteLine($"[SUCCESS] Compiled native binary successfully: {outputExeName}");
      Console.ResetColor();

    } catch (Exception ex) {
      Console.ForegroundColor = ConsoleColor.Red;
      Console.WriteLine($"Error compiling program: {ex.Message}");
      Console.ResetColor();
      System.Environment.Exit(1);
    }
  }

  static void DownloadTcc(string targetDir) {
    Console.WriteLine("🌲 Bundled TCC compiler not found. Downloading toolchain automatically...");
    try {
      using var client = new HttpClient();
      client.DefaultRequestHeaders.UserAgent.ParseAdd("PinoCompiler-TccDownloader");

      var zipBytes = client.GetByteArrayAsync("https://download.savannah.gnu.org/releases/tinycc/tcc-0.9.27-win64-bin.zip").Result;

      using var ms = new MemoryStream(zipBytes);
      using var archive = new ZipArchive(ms);

      Directory.CreateDirectory(targetDir);

      foreach (var entry in archive.Entries) {
        var relativePath = entry.FullName;
        if (relativePath.StartsWith("tcc/")) {
          var subPath = relativePath.Substring(4);
          if (string.IsNullOrEmpty(subPath)) continue;

          var destPath = Path.Combine(targetDir, subPath);
          if (entry.FullName.EndsWith("/")) {
            Directory.CreateDirectory(destPath);
          } else {
            var parentDir = Path.GetDirectoryName(destPath);
            if (parentDir != null) {
              Directory.CreateDirectory(parentDir);
            }
            entry.ExtractToFile(destPath, overwrite: true);
          }
        }
      }

      Console.WriteLine("🌲 Success! Bundled TCC compiler installed successfully.");
    } catch (Exception ex) {
      Console.WriteLine($"Error downloading TCC: {ex.Message}");
      System.Environment.Exit(1);
    }
  }
}
