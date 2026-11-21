using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

internal static class ModulePathHelper {
  public static bool IsTestFile(string? filePath) {
    if (string.IsNullOrEmpty(filePath)) {
      return false;
    }

    string fileName = Path.GetFileName(filePath);
    if (fileName.EndsWith("_test.pino", StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    string? directory = Path.GetDirectoryName(filePath);
    if (string.IsNullOrEmpty(directory)) {
      return false;
    }

    string[] segments = directory.Split(
        ['/', '\\'],
        StringSplitOptions.RemoveEmptyEntries);
    foreach (string segment in segments) {
      if (string.Equals(segment, "test", StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  public static string NormalizeMemoryPath(string path) {
    string normalized = path.Replace('\\', '/');
    while (normalized.Contains("//", StringComparison.Ordinal)) {
      normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
    }

    return normalized.TrimEnd('/');
  }
}

/// <summary>
/// Resolves imported Pino modules on the local filesystem following D-063 / MOD-7:
/// walks up parent directories from the importing file, checking
/// <c>modules/&lt;Name&gt;.pino</c> before <c>modules/&lt;lowercased&gt;.pino</c> at each level.
/// </summary>
public sealed class FileSystemModuleSource : IModuleSource {
  public static FileSystemModuleSource Instance { get; } = new();

  private FileSystemModuleSource() { }

  public bool TryResolveModule(
      string? importingFilePath,
      string moduleName,
      out ParseResult? parseResult,
      out string? expectedPath) {
    parseResult = null;
    expectedPath = null;

    if (string.IsNullOrWhiteSpace(moduleName)) {
      return false;
    }

    string startDir = ResolveStartDirectory(importingFilePath);
    string? resolved = FindModuleFile(startDir, moduleName);
    if (resolved is null) {
      expectedPath = Path.Combine(startDir, "modules", $"{moduleName}.pino");
      return false;
    }

    string content = File.ReadAllText(resolved);
    var source = new SourceText(content, resolved);
    parseResult = Parser.Parse(source);
    expectedPath = resolved;
    return true;
  }

  public bool IsTestFile(string? filePath) => ModulePathHelper.IsTestFile(filePath);

  private static string ResolveStartDirectory(string? importingFilePath) {
    if (string.IsNullOrWhiteSpace(importingFilePath)) {
      return Environment.CurrentDirectory;
    }

    try {
      string full = Path.GetFullPath(importingFilePath);
      return Path.GetDirectoryName(full) ?? Environment.CurrentDirectory;
    } catch (ArgumentException) {
      return Environment.CurrentDirectory;
    }
  }

  private static string? FindModuleFile(string startDir, string moduleName) {
    var current = new DirectoryInfo(startDir);
    while (current is not null) {
      string modulesDir = Path.Combine(current.FullName, "modules");
      string? inModules = TryFindExactOrLower(modulesDir, moduleName);
      if (inModules is not null) {
        return inModules;
      }

      if (current.Name.Equals("modules", StringComparison.OrdinalIgnoreCase)) {
        string? inCurrent = TryFindExactOrLower(current.FullName, moduleName);
        if (inCurrent is not null) {
          return inCurrent;
        }
      }

      current = current.Parent;
    }

    return null;
  }

  private static string? TryFindExactOrLower(string directory, string moduleName) {
    if (!Directory.Exists(directory)) {
      return null;
    }

    string exactFile = $"{moduleName}.pino";
    string exactPath = Path.Combine(directory, exactFile);
    if (File.Exists(exactPath)) {
      return GetCanonicalOnDiskPath(directory, exactFile, exactPath);
    }

    string lowerFile = $"{moduleName.ToLowerInvariant()}.pino";
    if (!string.Equals(lowerFile, exactFile, StringComparison.Ordinal)) {
      string lowerPath = Path.Combine(directory, lowerFile);
      if (File.Exists(lowerPath)) {
        return GetCanonicalOnDiskPath(directory, lowerFile, lowerPath);
      }
    }

    return null;
  }

  private static string GetCanonicalOnDiskPath(string directory, string fileName, string fallbackFullPath) {
    try {
      foreach (string match in Directory.EnumerateFiles(directory, fileName)) {
        return Path.GetFullPath(match);
      }
    } catch (IOException) {
      // Fall back to the probed path if enumeration fails.
    }

    return Path.GetFullPath(fallbackFullPath);
  }
}

/// <summary>
/// In-memory <see cref="IModuleSource"/> for unit tests with deterministic case-sensitive
/// path lookup and zero filesystem I/O.
/// </summary>
public sealed class InMemoryModuleSource : IModuleSource {
  private readonly Dictionary<string, string> _sources = new(StringComparer.Ordinal);
  private readonly Dictionary<string, ParseResult> _parsed = new(StringComparer.Ordinal);

  public InMemoryModuleSource() { }

  public InMemoryModuleSource(IEnumerable<KeyValuePair<string, string>> files) {
    ArgumentNullException.ThrowIfNull(files);
    foreach ((string path, string content) in files) {
      AddFile(path, content);
    }
  }

  public void AddFile(string path, string content) {
    ArgumentNullException.ThrowIfNull(path);
    ArgumentNullException.ThrowIfNull(content);
    string normalized = ModulePathHelper.NormalizeMemoryPath(path);
    _sources[normalized] = content;
    _parsed.Remove(normalized);
  }

  public void AddParsedFile(string path, ParseResult parseResult) {
    ArgumentNullException.ThrowIfNull(path);
    ArgumentNullException.ThrowIfNull(parseResult);
    string normalized = ModulePathHelper.NormalizeMemoryPath(path);
    _parsed[normalized] = parseResult;
    _sources[normalized] = parseResult.Source.Text;
  }

  public bool TryResolveModule(
      string? importingFilePath,
      string moduleName,
      out ParseResult? parseResult,
      out string? expectedPath) {
    parseResult = null;
    expectedPath = null;

    if (string.IsNullOrWhiteSpace(moduleName)) {
      return false;
    }

    string startDir = GetParentDirectory(
        string.IsNullOrEmpty(importingFilePath)
            ? string.Empty
            : ModulePathHelper.NormalizeMemoryPath(importingFilePath));

    string? dir = startDir;
    while (dir is not null) {
      string modulesDir = string.IsNullOrEmpty(dir) ? "modules" : $"{dir}/modules";
      if (TryGetCandidate(modulesDir, moduleName, out expectedPath, out parseResult)) {
        return true;
      }

      if (IsModulesDirectory(dir) && TryGetCandidate(dir, moduleName, out expectedPath, out parseResult)) {
        return true;
      }

      if (string.IsNullOrEmpty(dir)) {
        break;
      }

      dir = GetParentDirectory(dir);
    }

    string fallbackPrefix = string.IsNullOrEmpty(startDir) ? "modules" : $"{startDir}/modules";
    expectedPath = $"{fallbackPrefix}/{moduleName}.pino";
    return false;
  }

  public bool IsTestFile(string? filePath) => ModulePathHelper.IsTestFile(filePath);

  private bool TryGetCandidate(
      string directory,
      string moduleName,
      out string? resolvedPath,
      out ParseResult? parseResult) {
    string exactPath = string.IsNullOrEmpty(directory)
        ? $"{moduleName}.pino"
        : $"{directory}/{moduleName}.pino";
    if (TryLoadPath(exactPath, out parseResult)) {
      resolvedPath = exactPath;
      return true;
    }

    string lowerName = moduleName.ToLowerInvariant();
    if (!string.Equals(lowerName, moduleName, StringComparison.Ordinal)) {
      string lowerPath = string.IsNullOrEmpty(directory)
          ? $"{lowerName}.pino"
          : $"{directory}/{lowerName}.pino";
      if (TryLoadPath(lowerPath, out parseResult)) {
        resolvedPath = lowerPath;
        return true;
      }
    }

    resolvedPath = null;
    parseResult = null;
    return false;
  }

  private bool TryLoadPath(string path, out ParseResult? parseResult) {
    if (_parsed.TryGetValue(path, out parseResult)) {
      return true;
    }

    if (_sources.TryGetValue(path, out string? content)) {
      parseResult = Parser.Parse(new SourceText(content, path));
      _parsed[path] = parseResult;
      return true;
    }

    parseResult = null;
    return false;
  }

  private static string GetParentDirectory(string path) {
    int lastSlash = path.LastIndexOf('/');
    return lastSlash <= 0 ? string.Empty : path[..lastSlash];
  }

  private static bool IsModulesDirectory(string dir) {
    if (string.IsNullOrEmpty(dir)) {
      return false;
    }

    int lastSlash = dir.LastIndexOf('/');
    string name = lastSlash < 0 ? dir : dir[(lastSlash + 1)..];
    return string.Equals(name, "modules", StringComparison.OrdinalIgnoreCase);
  }
}
