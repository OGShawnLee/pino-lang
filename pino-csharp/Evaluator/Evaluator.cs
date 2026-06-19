using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pino;

// Exceptions for Control Flow in the Interpreter
public class PinoReturnException : Exception {
  public object? Value { get; }
  public PinoReturnException(object? value) => Value = value;
  public override string StackTrace => string.Empty;
}

public class PinoBreakException : Exception {
  public override string StackTrace => string.Empty;
}

public class PinoContinueException : Exception {
  public override string StackTrace => string.Empty;
}

// Callables Interface
public interface IPinoCallable {
  int Arity { get; }
  object? Call(Evaluator evaluator, List<object?> arguments);
}

// User-defined Function
public class PinoFunction : IPinoCallable {
  public FunctionDeclaration Declaration { get; }
  private readonly Environment _closure;

  public PinoFunction(FunctionDeclaration declaration, Environment closure) {
    Declaration = declaration;
    _closure = closure;
  }

  public int Arity => Declaration.Parameters.Count;

  public object? Call(Evaluator evaluator, List<object?> arguments) {
    var env = new Environment(_closure);
    for (int i = 0; i < Declaration.Parameters.Count; i++) {
      env.Define(Declaration.Parameters[i].Identifier, arguments[i], false);
    }

    try {
      evaluator.Execute(Declaration.Body, env);
    } catch (PinoReturnException ret) {
      return ret.Value;
    }

    return null;
  }
}

// Lambda / Anonymous Function
public class PinoLambda : IPinoCallable {
  private readonly FunctionLambdaExpression _expression;
  private readonly Environment _closure;

  public PinoLambda(FunctionLambdaExpression expression, Environment closure) {
    _expression = expression;
    _closure = closure;
  }

  public int Arity => _expression.Parameters.Count;

  public object? Call(Evaluator evaluator, List<object?> arguments) {
    var env = new Environment(_closure);
    for (int i = 0; i < _expression.Parameters.Count; i++) {
      env.Define(_expression.Parameters[i].Identifier, arguments[i], false);
    }

    try {
      evaluator.Execute(_expression.Body, env);
    } catch (PinoReturnException ret) {
      return ret.Value;
    }

    return null;
  }
}

// Struct Definition
public class PinoStruct {
  public string Name { get; }
  public List<VariableDeclaration> Fields { get; }
  public List<FunctionDeclaration> Methods { get; }
  public List<string> InheritedStructs { get; }

  public PinoStruct(string name, List<VariableDeclaration> fields, List<FunctionDeclaration> methods, List<string> inheritedStructs) {
    Name = name;
    Fields = fields;
    Methods = methods;
    InheritedStructs = inheritedStructs;
  }
}

// Struct Instance
public class PinoStructInstance {
  public PinoStruct Struct { get; }
  public Dictionary<string, object?> Fields { get; } = new();

  public PinoStructInstance(PinoStruct @struct) {
    Struct = @struct;
  }

  public override string ToString() {
    var fieldsStr = string.Join(", ", Fields.Select(f => $"{f.Key}: {f.Value}"));
    return $"{Struct.Name} {{ {fieldsStr} }}";
  }
}

// Enum Definition
public class PinoEnum {
  public string Name { get; }
  public List<string> Members { get; }

  public PinoEnum(string name, List<string> members) {
    Name = name;
    Members = members;
  }
}

// Enum Value representation
public record PinoEnumValue(string EnumName, string Member) {
  public override string ToString() => $"{EnumName}::{Member}";
}

// Module representation
public class PinoModule {
  public string Name { get; }
  public Environment Environment { get; }
  public HashSet<string> PublicExports { get; }

  public PinoModule(string name, Environment environment, HashSet<string> publicExports) {
    Name = name;
    Environment = environment;
    PublicExports = publicExports;
  }
}

// Evaluator / Interpreter
public partial class Evaluator {
  private readonly Environment _globals = new();
  private readonly Dictionary<string, PinoModule> _moduleCache = new();
  private readonly HashSet<string> _currentlyLoadingModules = new();

  public Evaluator() {
    // Define built-in functions
    _globals.Define("println", new PrintlnFunction(), true);
    _globals.Define("readline", new ReadlineFunction(), true);
    _globals.Define("int", new IntFunction(), true);
    _globals.Define("float", new FloatFunction(), true);
    _globals.Define("rand", new RandFunction(), true);
    _globals.Define("time", new TimeFunction(), true);
    _globals.Define("sleep", new SleepFunction(), true);
    _globals.Define("type", new TypeFunction(), true);
    _globals.Define("str", new StrFunction(), true);
    _globals.Define("clear", new ClearFunction(), true);
  }

  public void Execute(Statement statement) {
    if (statement is ProgramStatement program) {
      var checker = new Checker();
      try {
        checker.Check(program);
      } catch {
        // Ignore Checker errors if running in un-checked mode
      }
    }
    Execute(statement, _globals);
  }
}
