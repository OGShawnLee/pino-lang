using System;
using System.Collections.Generic;
using System.Linq;

namespace Pino;

public partial class Evaluator {
  // Built-in functions
  private class PrintlnFunction : IPinoCallable {
    public int Arity => -1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      Console.WriteLine(string.Join(" ", arguments.Select(x => evaluator.FormatVal(x))));
      return null;
    }
  }

  private class ReadlineFunction : IPinoCallable {
    public int Arity => -1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      if (arguments.Count > 0) {
        Console.Write(arguments[0]);
      }
      return Console.ReadLine();
    }
  }

  private class IntFunction : IPinoCallable {
    public int Arity => 1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      var arg = arguments[0]?.ToString() ?? "0";
      if (double.TryParse(arg, out var d)) return (long) d;
      return long.Parse(arg);
    }
  }

  private class FloatFunction : IPinoCallable {
    public int Arity => 1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      return double.Parse(arguments[0]?.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
    }
  }

  private class RandFunction : IPinoCallable {
    private readonly Random _rand = new();
    public int Arity => -1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      if (arguments.Count == 0) {
        return _rand.NextDouble();
      }
      var maxVal = arguments[0];
      long max = maxVal is long l ? l : Convert.ToInt64(maxVal);
      return (long) _rand.Next(0, (int) max);
    }
  }

  private class TimeFunction : IPinoCallable {
    public int Arity => 0;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      return (double)(DateTime.UtcNow.Ticks - 621355968000000000L) / 10000.0;
    }
  }

  private class SleepFunction : IPinoCallable {
    public int Arity => 1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      var msVal = arguments[0];
      long ms = msVal is long l ? l : Convert.ToInt64(msVal);
      System.Threading.Thread.Sleep((int) ms);
      return null;
    }
  }

  private class TypeFunction : IPinoCallable {
    public int Arity => 1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      var val = arguments[0];
      if (val == null) return "null";
      if (val is bool) return "bool";
      if (val is PinoRune) return "rune";
      if (val is long) return "int";
      if (val is double) return "float";
      if (val is string) return "string";
      if (val is List<object?>) return "vector";
      if (val is Dictionary<object, object?>) return "map";
      if (val is PinoStructInstance) return "struct";
      if (val is IPinoCallable) return "function";
      if (val is PinoEnumValue) return "enum";
      if (val is PinoRegex) return "regex";
      return val.GetType().Name.ToLower();
    }
  }

  private class RuneFunction : IPinoCallable {
    public int Arity => 1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      var arg = arguments[0];
      if (arg == null) return new PinoRune(0);
      if (arg is PinoRune r) return r;
      if (arg is long l) return new PinoRune((int)l);
      if (arg is double d) return new PinoRune((int)d);
      if (arg is string s) {
        if (s.Length == 0) return new PinoRune(0);
        if (char.IsHighSurrogate(s[0]) && s.Length > 1 && char.IsLowSurrogate(s[1])) {
          return new PinoRune(char.ConvertToUtf32(s[0], s[1]));
        }
        return new PinoRune(s[0]);
      }
      throw new Exception($"RUNTIME ERROR: Cannot convert type '{arg?.GetType().Name ?? "null"}' to rune.");
    }
  }

  private class StrFunction : IPinoCallable {
    public int Arity => 1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      return evaluator.FormatVal(arguments[0], true);
    }
  }

  private class ClearFunction : IPinoCallable {
    public int Arity => 0;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      try {
        if (!Console.IsOutputRedirected) {
          Console.Clear();
        }
      } catch {
        // Ignore
      }
      return null;
    }
  }

  public class PinoRegex {
    public System.Text.RegularExpressions.Regex Value { get; }
    public string Pattern { get; }

    public PinoRegex(string pattern) {
      Pattern = pattern;
      Value = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.Compiled);
    }

    public override string ToString() => $"regex(\"{Pattern}\")";
  }

  private class RegexFunction : IPinoCallable {
    public int Arity => 1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      if (arguments.Count != 1 || arguments[0] is not string pattern) {
        throw new Exception("RUNTIME ERROR: regex() expects 1 string argument.");
      }
      return new PinoRegex(pattern);
    }
  }

  private class PanicFunction : IPinoCallable {
    public int Arity => 1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      string msg = arguments.Count > 0 ? arguments[0]?.ToString() ?? "" : "";
      var stack = evaluator.CallStack.ToList();
      throw new PinoPanicException(msg, stack);
    }
  }

  private class ReadFileFunction : IPinoCallable {
    public int Arity => 1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      string path = arguments[0]?.ToString() ?? "";
      try {
        string content = System.IO.File.ReadAllText(path);
        return new PinoUnionValue("Result", "Success", new List<object?> { content });
      } catch (System.IO.FileNotFoundException) {
        var ioErr = new PinoUnionValue("IOError", "NotFound", new List<object?> { path });
        return new PinoUnionValue("Result", "Failure", new List<object?> { ioErr });
      } catch (System.IO.DirectoryNotFoundException) {
        var ioErr = new PinoUnionValue("IOError", "NotFound", new List<object?> { path });
        return new PinoUnionValue("Result", "Failure", new List<object?> { ioErr });
      } catch (System.UnauthorizedAccessException) {
        var ioErr = new PinoUnionValue("IOError", "PermissionDenied", new List<object?> { path });
        return new PinoUnionValue("Result", "Failure", new List<object?> { ioErr });
      } catch (System.IO.IOException ex) {
        var ioErr = new PinoUnionValue("IOError", "Gremlin", new List<object?> { ex.Message });
        return new PinoUnionValue("Result", "Failure", new List<object?> { ioErr });
      } catch (Exception ex) {
        var ioErr = new PinoUnionValue("IOError", "Gremlin", new List<object?> { ex.Message });
        return new PinoUnionValue("Result", "Failure", new List<object?> { ioErr });
      }
    }
  }

  private class WriteFileFunction : IPinoCallable {
    public int Arity => 2;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      string path = arguments[0]?.ToString() ?? "";
      string content = arguments[1]?.ToString() ?? "";
      try {
        System.IO.File.WriteAllText(path, content);
        return new PinoUnionValue("Result", "Success", new List<object?> { path });
      } catch (System.IO.FileNotFoundException) {
        var ioErr = new PinoUnionValue("IOError", "NotFound", new List<object?> { path });
        return new PinoUnionValue("Result", "Failure", new List<object?> { ioErr });
      } catch (System.IO.DirectoryNotFoundException) {
        var ioErr = new PinoUnionValue("IOError", "NotFound", new List<object?> { path });
        return new PinoUnionValue("Result", "Failure", new List<object?> { ioErr });
      } catch (System.UnauthorizedAccessException) {
        var ioErr = new PinoUnionValue("IOError", "PermissionDenied", new List<object?> { path });
        return new PinoUnionValue("Result", "Failure", new List<object?> { ioErr });
      } catch (System.IO.IOException ex) {
        var ioErr = new PinoUnionValue("IOError", "Gremlin", new List<object?> { ex.Message });
        return new PinoUnionValue("Result", "Failure", new List<object?> { ioErr });
      } catch (Exception ex) {
        var ioErr = new PinoUnionValue("IOError", "Gremlin", new List<object?> { ex.Message });
        return new PinoUnionValue("Result", "Failure", new List<object?> { ioErr });
      }
    }
  }

  private class FileExistsFunction : IPinoCallable {
    public int Arity => 1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      string path = arguments[0]?.ToString() ?? "";
      return System.IO.File.Exists(path);
    }
  }
}

public class PinoPanicException : Exception {
  public string PanicMessage { get; }
  public List<string> CallStack { get; }

  public PinoPanicException(string message, List<string> callStack) : base(message) {
    PanicMessage = message;
    CallStack = callStack;
  }
}

public class PinoAssertException : Exception {
  public string AssertionExpression { get; }
  public string FilePath { get; }
  public int Line { get; }

  public PinoAssertException(string message, string assertionExpression, string filePath, int line) : base(message) {
    AssertionExpression = assertionExpression;
    FilePath = filePath;
    Line = line;
  }
}
