using System;
using System.IO;

namespace Pino;

public static class ModuleResolver {
  public static string ResolveModuleFilePath(string? currentFilePath, string moduleName, string? explicitModulesDir = null) {
    var filename = moduleName.ToLower() + ".pino";

    if (!string.IsNullOrEmpty(explicitModulesDir)) {
      var candidate = Path.Combine(explicitModulesDir, filename);
      if (File.Exists(candidate)) return candidate;
    }

    var startDir = !string.IsNullOrEmpty(currentFilePath)
        ? Path.GetDirectoryName(currentFilePath) ?? System.Environment.CurrentDirectory
        : System.Environment.CurrentDirectory;

    var current = new DirectoryInfo(startDir);
    while (current != null) {
      // Check current/modules/filename
      var candidateInModules = Path.Combine(current.FullName, "modules", filename);
      if (File.Exists(candidateInModules)) {
        return candidateInModules;
      }

      // If current dir itself is named "modules"
      if (current.Name.Equals("modules", StringComparison.OrdinalIgnoreCase)) {
        var candidateDirect = Path.Combine(current.FullName, filename);
        if (File.Exists(candidateDirect)) {
          return candidateDirect;
        }
      }

      current = current.Parent;
    }

    // Fallback path if not found (used for descriptive error message)
    var fallbackBase = !string.IsNullOrEmpty(currentFilePath)
        ? Path.GetDirectoryName(currentFilePath) ?? System.Environment.CurrentDirectory
        : System.Environment.CurrentDirectory;
    return Path.Combine(fallbackBase, "modules", filename);
  }
}
