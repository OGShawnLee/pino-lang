using System;
using System.IO;

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
        var watchFileName = args.Length > 1 ? args[1] : "main.pino";
        var watchFilePath = Path.Combine(System.Environment.CurrentDirectory, watchFileName);
        if (!File.Exists(watchFilePath)) {
          Console.WriteLine($"Error: File '{watchFileName}' not found.");
          System.Environment.Exit(1);
        }
        WatchFile(watchFilePath);
        break;

      case "v":
      case "version":
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0";
        Console.WriteLine($"Pino version: {version} (.NET 10)");
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
    Console.WriteLine("  watch [file-name]   : Monitor and execute the file in real-time on save (defaults to main.pino)");
    Console.WriteLine("  version, v          : Show Pino version information");
    Console.WriteLine("  <empty>             : Run main.pino in current directory if exists");
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

  static void SafeClearConsole() {
    try {
      if (!Console.IsOutputRedirected) {
        Console.Clear();
      }
    } catch {
      // Ignore
    }
  }

  static void WatchFile(string path) {
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
        Console.WriteLine($"[Pino Watcher] Monitoring '{Path.GetFileName(path)}'... Press Ctrl+C to stop.\n");

        var startInfo = new System.Diagnostics.ProcessStartInfo {
          FileName = exePath,
          Arguments = $"run \"{path}\"",
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
