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

      case "v":
      case "version":
        Console.WriteLine("Pino version: 0.1.0 (.NET 10)");
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
