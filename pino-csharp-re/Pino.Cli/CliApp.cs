using System.Globalization;
using System.Text;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Cli;

/// <summary>The <c>pino-re</c> command line: a front-end checker for the C# rewrite.</summary>
public static class CliApp {
  private const string Prefix = "pino-re: ";

  private const string Usage = """
      pino-re: the Pino front-end checker

      Usage:
        pino-re check <path>...   Lex and parse .pino files (or directories, searched
                                  recursively) and print their diagnostics.
        pino-re tree <file>       Print the syntax tree of one file, then its diagnostics.
        pino-re --help            Show this help (also -h, or no arguments).

      Options (before or after the paths):
        --color auto|always|never   Color the output (default: auto; off when output is
                                    redirected, NO_COLOR is set, or ANSI is unsupported).
        --voice pino|plain          Include the '= Pino:' notes, or leave them out
                                    (default: pino).

      Exit codes:
        0  no errors
        1  errors were found
        2  bad usage or an unreadable path
      """ + "\n";

  public static int Run(string[] args, TextWriter stdout, TextWriter stderr, CliEnvironment environment) {
    ArgumentNullException.ThrowIfNull(args);
    ArgumentNullException.ThrowIfNull(stdout);
    ArgumentNullException.ThrowIfNull(stderr);
    ArgumentNullException.ThrowIfNull(environment);

    try {
      return RunCore(args, stdout, stderr, environment);
    } catch (CliUsageException ex) {
      WriteError(stderr, ex.Message);
      return 2;
    } catch (Exception ex) {
      WriteError(stderr, "internal error: " + OneLine(ex.Message));
      return 2;
    }
  }

  private static int RunCore(string[] args, TextWriter stdout, TextWriter stderr, CliEnvironment environment) {
    if (args.Length == 0) {
      stdout.Write(Usage);
      return 0;
    }

    string colorMode = "auto";
    string voiceMode = "pino";
    var positionals = new List<string>();

    for (int i = 0; i < args.Length; i++) {
      string arg = args[i];
      if (arg is "--help" or "-h") {
        stdout.Write(Usage);
        return 0;
      }

      if (arg is "--color") {
        colorMode = ReadOptionValue(args, ref i, ["auto", "always", "never"]);
      } else if (arg is "--voice") {
        voiceMode = ReadOptionValue(args, ref i, ["pino", "plain"]);
      } else if (arg.StartsWith('-')) {
        throw new CliUsageException($"unknown option '{arg}'");
      } else {
        positionals.Add(arg);
      }
    }

    if (positionals.Count == 0) {
      throw new CliUsageException("missing command (expected 'check' or 'tree')");
    }

    string command = positionals[0];
    List<string> paths = positionals.GetRange(1, positionals.Count - 1);

    var options = new DiagnosticRenderOptions {
      Color = ResolveColor(colorMode, environment),
      Voice = voiceMode == "plain" ? DiagnosticVoice.Plain : DiagnosticVoice.Pino,
      Notes = environment.Notes,
    };

    return command switch {
      "check" => RunCheck(paths, options, stdout),
      "tree" => RunTree(paths, options, stdout),
      _ => throw new CliUsageException($"unknown command '{command}'"),
    };
  }

  private static string ReadOptionValue(string[] args, ref int index, string[] allowed) {
    string option = args[index];
    if (index + 1 >= args.Length) {
      throw new CliUsageException($"missing value for option '{option}'");
    }

    string value = args[++index];
    if (Array.IndexOf(allowed, value) < 0) {
      throw new CliUsageException($"bad value '{value}' for option '{option}' (expected {string.Join(", ", allowed)})");
    }

    return value;
  }

  private static bool ResolveColor(string mode, CliEnvironment environment) {
    switch (mode) {
      case "always":
        return true;
      case "never":
        return false;
      default:
        return !environment.IsOutputRedirected
            && environment.SupportsAnsi
            && string.IsNullOrEmpty(environment.GetEnvironmentVariable("NO_COLOR"));
    }
  }

  private static int RunCheck(List<string> paths, DiagnosticRenderOptions options, TextWriter stdout) {
    if (paths.Count == 0) {
      throw new CliUsageException("missing path (usage: pino-re check <path>...)");
    }

    List<InputFile> files = CollectFiles(paths);

    // Read everything before printing so an unreadable file never leaves partial output behind.
    var sources = new List<SourceText>(files.Count);
    foreach (InputFile file in files) {
      sources.Add(new SourceText(ReadFile(file.FullPath, file.DisplayPath), file.DisplayPath));
    }

    int errors = 0;
    int warnings = 0;
    bool wroteFile = false;

    foreach (SourceText source in sources) {
      ParseResult result = Parser.Parse(source);
      (int fileErrors, int fileWarnings) = Count(result);
      errors += fileErrors;
      warnings += fileWarnings;

      if (result.Diagnostics.IsEmpty) {
        continue;
      }

      if (wroteFile) {
        stdout.Write('\n');
      }

      stdout.Write(DiagnosticRenderer.RenderAll(result.Diagnostics, source, options));
      wroteFile = true;
    }

    stdout.Write(string.Create(
        CultureInfo.InvariantCulture,
        $"checked {files.Count} file(s): {errors} error(s), {warnings} warning(s)\n"));
    return errors > 0 ? 1 : 0;
  }

  private static int RunTree(List<string> paths, DiagnosticRenderOptions options, TextWriter stdout) {
    if (paths.Count == 0) {
      throw new CliUsageException("missing path (usage: pino-re tree <file>)");
    }

    if (paths.Count > 1) {
      throw new CliUsageException("'tree' takes exactly one file");
    }

    string path = paths[0];
    if (Directory.Exists(path)) {
      throw new CliUsageException($"'{path}' is a directory; 'tree' takes exactly one file");
    }

    if (!File.Exists(path)) {
      throw new CliUsageException($"no such file or directory: '{path}'");
    }

    string displayPath = ToDisplayPath(path);
    var source = new SourceText(ReadFile(path, displayPath), displayPath);
    ParseResult result = Parser.Parse(source);

    var sb = new StringBuilder();
    AppendTree(sb, result.Root, source);
    stdout.Write(sb.ToString());

    if (!result.Diagnostics.IsEmpty) {
      stdout.Write('\n');
      stdout.Write(DiagnosticRenderer.RenderAll(result.Diagnostics, source, options));
    }

    return Count(result).Errors > 0 ? 1 : 0;
  }

  private static (int Errors, int Warnings) Count(ParseResult result) {
    int errors = 0;
    int warnings = 0;
    foreach (Diagnostic diagnostic in result.Diagnostics) {
      if (diagnostic.Severity == DiagnosticSeverity.Error) {
        errors++;
      } else if (diagnostic.Severity == DiagnosticSeverity.Warning) {
        warnings++;
      }
    }

    return (errors, warnings);
  }

  private static void AppendTree(StringBuilder sb, SyntaxNode root, SourceText source) {
    var stack = new Stack<(SyntaxElement Element, int Depth)>();
    stack.Push((new SyntaxElement(root), 0));

    while (stack.Count > 0) {
      (SyntaxElement element, int depth) = stack.Pop();
      sb.Append(' ', depth * 2);

      if (element.IsToken) {
        Token token = element.AsToken();
        sb.Append(token.Kind.ToString());
        if ((token.Flags & TokenFlags.Missing) != 0) {
          sb.Append(" <missing>");
        } else {
          sb.Append(" '").Append(TokenText(token, source)).Append('\'');
        }

        sb.Append('\n');
        continue;
      }

      SyntaxNode node = element.AsNode();
      string name = node.GetType().Name;
      if (name.EndsWith("Syntax", StringComparison.Ordinal) && name.Length > "Syntax".Length) {
        name = name[..^"Syntax".Length];
      }

      sb.Append(name).Append('\n');

      SyntaxElement[] children = node.GetChildren().ToArray();
      for (int i = children.Length - 1; i >= 0; i--) {
        stack.Push((children[i], depth + 1));
      }
    }
  }

  private static string TokenText(Token token, SourceText source) {
    int start = Math.Clamp(token.Span.Start, 0, source.Length);
    int end = Math.Clamp(token.Span.End, start, source.Length);
    return source.Text.Substring(start, end - start);
  }

  private static List<InputFile> CollectFiles(List<string> paths) {
    var byFullPath = new Dictionary<string, InputFile>(StringComparer.Ordinal);

    foreach (string path in paths) {
      if (File.Exists(path)) {
        Add(byFullPath, new InputFile(ToDisplayPath(path), Path.GetFullPath(path)));
      } else if (Directory.Exists(path)) {
        string root = ToDisplayPath(path).TrimEnd('/');
        IEnumerable<string> found;
        try {
          found = Directory.EnumerateFiles(path, "*.pino", SearchOption.AllDirectories).ToList();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
          throw new CliUsageException($"cannot read directory '{path}': {OneLine(ex.Message)}");
        }

        foreach (string file in found) {
          string relative = ToDisplayPath(Path.GetRelativePath(path, file));
          Add(byFullPath, new InputFile(root + "/" + relative, Path.GetFullPath(file)));
        }
      } else {
        throw new CliUsageException($"no such file or directory: '{path}'");
      }
    }

    return byFullPath.Values
        .OrderBy(static f => f.DisplayPath, StringComparer.Ordinal)
        .ToList();
  }

  private static void Add(Dictionary<string, InputFile> byFullPath, InputFile file) {
    byFullPath.TryAdd(file.FullPath, file);
  }

  private static string ReadFile(string path, string displayPath) {
    try {
      return File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      throw new CliUsageException($"cannot read '{displayPath}': {OneLine(ex.Message)}");
    }
  }

  private static string ToDisplayPath(string path) {
    string display = path.Replace(Path.DirectorySeparatorChar, '/');
    return Path.AltDirectorySeparatorChar == '/' ? display : display.Replace(Path.AltDirectorySeparatorChar, '/');
  }

  private static void WriteError(TextWriter stderr, string message) {
    stderr.Write(Prefix + OneLine(message) + "\n");
  }

  private static string OneLine(string text) =>
      text.ReplaceLineEndings(" ").Trim();

  private readonly record struct InputFile(string DisplayPath, string FullPath);

  private sealed class CliUsageException(string message) : Exception(message);
}
