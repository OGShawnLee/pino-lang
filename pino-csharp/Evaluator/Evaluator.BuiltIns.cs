using System;
using System.Collections.Generic;
using System.Linq;

namespace Pino;

public partial class Evaluator {
  // Built-in functions
  private class PrintlnFunction : IPinoCallable {
    public int Arity => -1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      Console.WriteLine(string.Join(" ", arguments.Select(evaluator.FormatVal)));
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
      return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
      return evaluator.FormatVal(arguments[0]);
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
}
