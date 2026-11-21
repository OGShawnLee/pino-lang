using Pino.Compiler.Syntax;

namespace Pino.Compiler.Checking;

/// <summary>
/// Resolves imported module names to parsed syntax trees and classifies test files
/// without coupling the checker core to filesystem path rules.
/// </summary>
public interface IModuleSource {
  bool TryResolveModule(
      string? importingFilePath,
      string moduleName,
      out ParseResult? parseResult,
      out string? expectedPath);

  bool IsTestFile(string? filePath);
}

public sealed class NullModuleSource : IModuleSource {
  public static NullModuleSource Instance { get; } = new();

  private NullModuleSource() { }

  public bool TryResolveModule(
      string? importingFilePath,
      string moduleName,
      out ParseResult? parseResult,
      out string? expectedPath) {
    parseResult = null;
    expectedPath = null;
    return false;
  }

  public bool IsTestFile(string? filePath) => false;
}
