using System;
using System.Collections.Generic;
using System.IO;

namespace Pino;

public class PinoException : Exception {
  public string FilePath { get; }
  public string ErrorType { get; }
  public string RawMessage { get; }
  public int Line { get; }
  public int Column { get; }
  public int Length { get; }
  public string? Hint { get; }

  private static readonly Dictionary<string, string[]> FileCache = new();

  public static void RegisterFileLines(string filePath, string[] lines) {
    FileCache[filePath] = lines;
  }

  public PinoException(string filePath, string errorType, string message, int line, int column, int length, string? hint = null)
      : base(FormatMessage(filePath, errorType, message, line, column, length, hint)) {
    FilePath = filePath;
    ErrorType = errorType;
    RawMessage = message;
    Line = line;
    Column = column;
    Length = length;
    Hint = hint;
  }

  public PinoException(string filePath, string errorType, string message, Token token, string? hint = null)
      : this(filePath, errorType, message, token.Line, token.Column, token.Length, hint) {}

  private static string FormatMessage(string filePath, string errorType, string message, int line, int column, int length, string? hint) {
    var writer = new StringWriter();
    
    writer.Write($"\u001b[31;1m{errorType}: \u001b[0m{message}\n");
    if (!string.IsNullOrEmpty(filePath)) {
      writer.Write($"\u001b[34;1m  --> \u001b[0m{filePath}:{line}:{column}\n");

      string[]? lines = null;
      if (FileCache.TryGetValue(filePath, out var cached)) {
        lines = cached;
      } else if (File.Exists(filePath)) {
        try {
          lines = File.ReadAllLines(filePath);
          FileCache[filePath] = lines;
        } catch {}
      }

      if (lines != null && line - 1 >= 0 && line - 1 < lines.Length) {
        string lineContent = lines[line - 1];
        writer.Write($"\u001b[90m{line,5} | \u001b[0m{lineContent}\n");
        writer.Write("      | ");
        for (int i = 0; i < column - 1; i++) {
          if (i < lineContent.Length && lineContent[i] == '\t') {
            writer.Write('\t');
          } else {
            writer.Write(' ');
          }
        }
        writer.Write("\u001b[31;1m^");
        int len = Math.Max(1, length);
        for (int i = 1; i < len; i++) {
          writer.Write("-");
        }
        writer.Write("\u001b[0m\n");
      }
    }

    if (!string.IsNullOrEmpty(hint)) {
      writer.Write($"\u001b[36;1m   <- Hint: \u001b[0m{hint}\n");
    }

    return writer.ToString();
  }
}
