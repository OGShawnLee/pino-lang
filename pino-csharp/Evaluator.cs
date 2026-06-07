using System;
using System.Collections.Generic;
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

  public PinoStruct(string name, List<VariableDeclaration> fields, List<FunctionDeclaration> methods) {
    Name = name;
    Fields = fields;
    Methods = methods;
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

// Evaluator / Interpreter
public class Evaluator {
  private readonly Environment _globals = new();

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
    Execute(statement, _globals);
  }

  public void Execute(Statement statement, Environment env) {
    switch (statement) {
      case ProgramStatement prog:
        foreach (var stmt in prog.Statements) {
          Execute(stmt, env);
        }
        break;

      case BlockStatement block:
        ExecuteBlock(block.Statements, new Environment(env));
        break;

      case ReturnStatement ret:
        var retVal = ret.Argument != null ? Evaluate(ret.Argument, env) : null;
        throw new PinoReturnException(retVal);

      case LoopStatement loop:
        ExecuteLoop(loop, env);
        break;

      case IfStatement ifs:
        var cond = Evaluate(ifs.Condition, env);
        if (IsTruthy(cond)) {
          Execute(ifs.Consequent, env);
        } else if (ifs.Alternate != null) {
          Execute(ifs.Alternate, env);
        }
        break;

      case ElseStatement elseStmt:
        Execute(elseStmt.Body, env);
        break;

      case MatchStatement match:
        ExecuteMatch(match, env);
        break;

      case VariableDeclaration varDecl:
        if (varDecl.Kind == VariableKind.Constant || varDecl.Kind == VariableKind.Variable) {
          var val = varDecl.Value != null ? Evaluate(varDecl.Value, env) : null;
          env.Define(varDecl.Identifier, val, varDecl.Kind == VariableKind.Constant);
        }
        break;

      case FunctionDeclaration fnDecl:
        var fn = new PinoFunction(fnDecl, env);
        env.Define(fnDecl.Identifier, fn, true);
        break;

      case StructDeclaration structDecl:
        var @struct = new PinoStruct(structDecl.Identifier, structDecl.Fields, structDecl.Methods);
        env.Define(structDecl.Identifier, @struct, true);
        break;

      case EnumDeclaration enumDecl:
        var @enum = new PinoEnum(enumDecl.Identifier, enumDecl.Members);
        env.Define(enumDecl.Identifier, @enum, true);
        break;

      case Expression expr:
        Evaluate(expr, env);
        break;

      default:
        throw new Exception($"RUNTIME ERROR: Unknown statement type '{statement.GetType().Name}'.");
    }
  }

  private void ExecuteBlock(List<Statement> statements, Environment env) {
    foreach (var stmt in statements) {
      Execute(stmt, env);
    }
  }

  private void ExecuteLoop(LoopStatement loop, Environment env) {
    switch (loop.Kind) {
      case LoopKind.Infinite:
        while (true) {
          try {
            Execute(loop.Body, new Environment(env));
          } catch (PinoBreakException) { break; } catch (PinoContinueException) { continue; }
        }
        break;

      case LoopKind.ForTimes:
        var timesVal = Evaluate(loop.Begin!, env);
        long limit = timesVal is long l ? l : Convert.ToInt64(timesVal);
        for (long i = 0; i < limit; i++) {
          var childEnv = new Environment(env);
          childEnv.Define("it", i, true);
          try {
            Execute(loop.Body, childEnv);
          } catch (PinoBreakException) { break; } catch (PinoContinueException) { continue; }
        }
        break;

      case LoopKind.ForIn:
        var collection = Evaluate(loop.End!, env);
        var loopVar = (loop.Begin as IdentifierExpression)?.Name ?? throw new Exception("RUNTIME ERROR: Loop variable must be an identifier.");

        if (collection is List<object?> list) {
          foreach (var item in list) {
            var childEnv = new Environment(env);
            childEnv.Define(loopVar, item, false);
            try {
              Execute(loop.Body, childEnv);
            } catch (PinoBreakException) { break; } catch (PinoContinueException) { continue; }
          }
        } else if (collection is long rangeLimit) {
          for (long i = 0; i < rangeLimit; i++) {
            var childEnv = new Environment(env);
            childEnv.Define(loopVar, i, false);
            try {
              Execute(loop.Body, childEnv);
            } catch (PinoBreakException) { break; } catch (PinoContinueException) { continue; }
          }
        } else {
          throw new Exception("RUNTIME ERROR: Cannot iterate over non-iterable object.");
        }
        break;
    }
  }

  private void ExecuteMatch(MatchStatement match, Environment env) {
    var matchVal = Evaluate(match.Condition, env);
    bool matched = false;

    foreach (var branch in match.Branches) {
      foreach (var condExpr in branch.Conditions) {
        var condVal = Evaluate(condExpr, env);
        if (Equals(matchVal, condVal)) {
          Execute(branch.Body, env);
          matched = true;
          break;
        }
      }
      if (matched) break;
    }

    if (!matched && match.Alternate != null) {
      Execute(match.Alternate, env);
    }
  }

  // --- EVALUATING EXPRESSIONS ---
  public object? Evaluate(Expression expression, Environment env) {
    switch (expression) {
      case LiteralExpression lit:
        switch (lit.LiteralType) {
          case LiteralType.Boolean:
            return bool.Parse(lit.Value);
          case LiteralType.Integer:
            return long.Parse(lit.Value.Replace("_", ""));
          case LiteralType.Float:
            return double.Parse(lit.Value.Replace("_", ""), System.Globalization.CultureInfo.InvariantCulture);
          case LiteralType.String:
            var str = lit.Value;
            if (lit.Injections != null) {
              foreach (var inj in lit.Injections) {
                var val = env.Get(inj);
                str = str.Replace("$" + inj, val?.ToString() ?? "");
              }
            }
            return str;
          default:
            return lit.Value;
        }

      case IdentifierExpression id:
        // Special check for break/continue
        if (id.Name == "break") throw new PinoBreakException();
        if (id.Name == "continue") throw new PinoContinueException();
        return env.Get(id.Name);

      case BinaryExpression bin:
        // Handle member access and static member access separately
        if (bin.Operator == OperatorType.MemberAccess) {
          return EvaluateMemberAccess(bin.Left, bin.Right, env);
        }
        if (bin.Operator == OperatorType.StaticMemberAccess) {
          return EvaluateStaticMemberAccess(bin.Left, bin.Right, env);
        }

        // Handle assignment (=)
        if (bin.Operator == OperatorType.Assignment) {
          var val = Evaluate(bin.Right, env);

          if (bin.Left is IdentifierExpression id) {
            env.Assign(id.Name, val);
            return val;
          }

          if (bin.Left is BinaryExpression memberAccess && memberAccess.Operator == OperatorType.MemberAccess) {
            var target = Evaluate(memberAccess.Left, env);
            if (target is PinoStructInstance targetInstance) {
              if (memberAccess.Right is not IdentifierExpression propId) {
                throw new Exception("RUNTIME ERROR: Left side of member assignment must end with a property name.");
              }
              targetInstance.Fields[propId.Name] = val;
              if (env.Exists(propId.Name)) {
                env.Assign(propId.Name, val);
              }
              return val;
            }
            throw new Exception("RUNTIME ERROR: Cannot assign to property of non-struct object.");
          }

          throw new Exception("RUNTIME ERROR: Left side of assignment must be an identifier or member access.");
        }

        // Handle compound assignments (+=, -=, *=, /=, %=)
        if (bin.Operator == OperatorType.AdditionAssignment ||
            bin.Operator == OperatorType.SubtractionAssignment ||
            bin.Operator == OperatorType.MultiplicationAssignment ||
            bin.Operator == OperatorType.DivisionAssignment ||
            bin.Operator == OperatorType.ModulusAssignment) {
          var delta = Evaluate(bin.Right, env);
          var baseOp = bin.Operator switch {
            OperatorType.AdditionAssignment => OperatorType.Addition,
            OperatorType.SubtractionAssignment => OperatorType.Subtraction,
            OperatorType.MultiplicationAssignment => OperatorType.Multiplication,
            OperatorType.DivisionAssignment => OperatorType.Division,
            OperatorType.ModulusAssignment => OperatorType.Modulus,
            _ => throw new NotImplementedException()
          };

          if (bin.Left is IdentifierExpression id) {
            var currentVal = env.Get(id.Name);
            var newVal = EvaluateBinaryOperation(currentVal, baseOp, delta);
            env.Assign(id.Name, newVal);
            return newVal;
          }

          if (bin.Left is BinaryExpression memberAccess && memberAccess.Operator == OperatorType.MemberAccess) {
            var target = Evaluate(memberAccess.Left, env);
            if (target is PinoStructInstance targetInstance) {
              if (memberAccess.Right is not IdentifierExpression propId) {
                throw new Exception("RUNTIME ERROR: Left side of compound member assignment must end with a property name.");
              }
              var currentVal = targetInstance.Fields[propId.Name];
              var newVal = EvaluateBinaryOperation(currentVal, baseOp, delta);
              targetInstance.Fields[propId.Name] = newVal;
              if (env.Exists(propId.Name)) {
                env.Assign(propId.Name, newVal);
              }
              return newVal;
            }
            throw new Exception("RUNTIME ERROR: Cannot assign to property of non-struct object.");
          }

          throw new Exception("RUNTIME ERROR: Left side of compound assignment must be an identifier or member access.");
        }

        var left = Evaluate(bin.Left, env);
        var right = Evaluate(bin.Right, env);

        return EvaluateBinaryOperation(left, bin.Operator, right);

      case TernaryExpression tern:
        var tCond = Evaluate(tern.Condition, env);
        return IsTruthy(tCond) ? Evaluate(tern.Consequent, env) : Evaluate(tern.Alternate, env);

      case VectorExpression vec:
        if (vec.Elements != null) {
          return vec.Elements.Select(e => Evaluate(e, env)).ToList();
        } else {
          // Vector init constructor: []type { len: limit, init: expr }
          var lenVal = Evaluate(vec.Len!, env);
          long length = lenVal is long l ? l : Convert.ToInt64(lenVal);
          var initList = new List<object?>();

          for (long i = 0; i < length; i++) {
            var initEnv = new Environment(env);
            initEnv.Define("it", i, true);

            var val = Evaluate(vec.Init!, initEnv);
            if (val is IPinoCallable initCallable) {
              initList.Add(initCallable.Call(this, new List<object?> { i }));
            } else {
              initList.Add(val);
            }
          }
          return initList;
        }

      case StructInstanceExpression inst:
        var structDefObj = env.Get(inst.StructName);
        if (structDefObj is not PinoStruct structDef) {
          throw new Exception($"RUNTIME ERROR: Struct '{inst.StructName}' is not defined.");
        }

        var instance = new PinoStructInstance(structDef);
        foreach (var prop in inst.Properties) {
          var val = prop.Value != null ? Evaluate(prop.Value, env) : null;
          instance.Fields[prop.Identifier] = val;
        }
        return instance;

      case FunctionCallExpression call:
        var callableObj = env.Get(call.Callee);
        if (callableObj is not IPinoCallable callable) {
          throw new Exception($"RUNTIME ERROR: '{call.Callee}' is not callable.");
        }

        var args = call.Arguments.Select(a => Evaluate(a, env)).ToList();
        if (callable.Arity != -1 && callable.Arity != args.Count) {
          throw new Exception($"RUNTIME ERROR: Function '{call.Callee}' expected {callable.Arity} arguments, but got {args.Count}.");
        }

        return callable.Call(this, args);

      case FunctionLambdaExpression lambda:
        return new PinoLambda(lambda, env);

      default:
        throw new Exception($"RUNTIME ERROR: Unknown expression type '{expression.GetType().Name}'.");
    }
  }

  private object? EvaluateMemberAccess(Expression leftExpr, Expression rightExpr, Environment env) {
    var leftVal = Evaluate(leftExpr, env);

    if (leftVal is PinoStructInstance instance) {
      // Case 1: instance:method(...) where right is FunctionCallExpression
      if (rightExpr is FunctionCallExpression methodCall) {
        var methodDecl = instance.Struct.Methods.Find(m => m.Identifier == methodCall.Callee);
        if (methodDecl == null) {
          throw new Exception($"RUNTIME ERROR: Struct '{instance.Struct.Name}' has no method '{methodCall.Callee}'.");
        }

        // Create a method closure environment that has access to all struct instance fields directly
        var methodEnv = new Environment(env);
        foreach (var field in instance.Fields) {
          methodEnv.Define(field.Key, field.Value, false);
        }

        methodEnv.Define("this", instance, true);
        methodEnv.Define("self", instance, true);

        var callable = new PinoFunction(methodDecl, methodEnv);
        var methodArgs = methodCall.Arguments.Select(a => Evaluate(a, env)).ToList();
        var result = callable.Call(this, methodArgs);

        // Copy back modified fields to struct instance
        foreach (var fieldKey in instance.Fields.Keys.ToList()) {
          instance.Fields[fieldKey] = methodEnv.Get(fieldKey);
        }

        return result;
      }

      // Case 2: instance:property where right is IdentifierExpression
      if (rightExpr is IdentifierExpression propId) {
        if (instance.Fields.ContainsKey(propId.Name)) {
          return instance.Fields[propId.Name];
        }
        throw new Exception($"RUNTIME ERROR: Struct '{instance.Struct.Name}' has no property '{propId.Name}'.");
      }
    } else if (leftVal is List<object?> list) {
      // Case 3: vector:len or vector:length
      if (rightExpr is IdentifierExpression listId && (listId.Name == "length" || listId.Name == "len")) {
        return (long)list.Count;
      }

      // Case 4: vector method calls
      if (rightExpr is FunctionCallExpression methodCall) {
        var methodName = methodCall.Callee;
        var methodArgs = methodCall.Arguments.Select(a => Evaluate(a, env)).ToList();

        if (methodName == "each") {
          if (methodArgs.Count < 1 || methodArgs[0] is not IPinoCallable func) {
            throw new Exception("RUNTIME ERROR: each() expects a callable argument.");
          }
          for (int i = 0; i < list.Count; i++) {
            var args = new List<object?>();
            if (func.Arity == 1) {
              args.Add(list[i]);
            } else if (func.Arity == 2) {
              args.Add(list[i]);
              args.Add((long)i);
            } else {
              args.Add(list[i]);
            }
            func.Call(this, args);
          }
          return null;
        }

        if (methodName == "map") {
          if (methodArgs.Count < 1 || methodArgs[0] is not IPinoCallable func) {
            throw new Exception("RUNTIME ERROR: map() expects a callable argument.");
          }
          var mappedList = new List<object?>();
          for (int i = 0; i < list.Count; i++) {
            var args = new List<object?>();
            if (func.Arity == 1) {
              args.Add(list[i]);
            } else if (func.Arity == 2) {
              args.Add(list[i]);
              args.Add((long)i);
            } else {
              args.Add(list[i]);
            }
            mappedList.Add(func.Call(this, args));
          }
          return mappedList;
        }

        if (methodName == "filter") {
          if (methodArgs.Count < 1 || methodArgs[0] is not IPinoCallable func) {
            throw new Exception("RUNTIME ERROR: filter() expects a callable argument.");
          }
          var filteredList = new List<object?>();
          for (int i = 0; i < list.Count; i++) {
            var args = new List<object?>();
            if (func.Arity == 1) {
              args.Add(list[i]);
            } else if (func.Arity == 2) {
              args.Add(list[i]);
              args.Add((long)i);
            } else {
              args.Add(list[i]);
            }
            if (IsTruthy(func.Call(this, args))) {
              filteredList.Add(list[i]);
            }
          }
          return filteredList;
        }

        if (methodName == "push" || methodName == "add") {
          if (methodArgs.Count < 1) {
            throw new Exception("RUNTIME ERROR: push() expects an item to add.");
          }
          list.Add(methodArgs[0]);
          return list;
        }

        if (methodName == "pop") {
          if (list.Count == 0) return null;
          var last = list[list.Count - 1];
          list.RemoveAt(list.Count - 1);
          return last;
        }

        throw new Exception($"RUNTIME ERROR: Vector has no method '{methodName}'.");
      }
    } else if (leftVal is string str) {
      if (rightExpr is IdentifierExpression propId) {
        if (propId.Name == "len" || propId.Name == "length") {
          return (long)str.Length;
        }
        throw new Exception($"RUNTIME ERROR: String has no property '{propId.Name}'.");
      }

      if (rightExpr is FunctionCallExpression methodCall) {
        var methodName = methodCall.Callee;
        var methodArgs = methodCall.Arguments.Select(a => Evaluate(a, env)).ToList();

        if (methodName == "lower") {
          if (methodArgs.Count != 0) throw new Exception("RUNTIME ERROR: lower() expects 0 arguments.");
          return str.ToLowerInvariant();
        }
        if (methodName == "upper") {
          if (methodArgs.Count != 0) throw new Exception("RUNTIME ERROR: upper() expects 0 arguments.");
          return str.ToUpperInvariant();
        }
        if (methodName == "trim") {
          if (methodArgs.Count != 0) throw new Exception("RUNTIME ERROR: trim() expects 0 arguments.");
          return str.Trim();
        }
        if (methodName == "contains") {
          if (methodArgs.Count != 1 || methodArgs[0] is not string sub) {
            throw new Exception("RUNTIME ERROR: contains() expects 1 string argument.");
          }
          return str.Contains(sub);
        }
        if (methodName == "split") {
          if (methodArgs.Count != 1 || methodArgs[0] is not string sep) {
            throw new Exception("RUNTIME ERROR: split() expects 1 string argument.");
          }
          return str.Split(new[] { sep }, StringSplitOptions.None).Cast<object?>().ToList();
        }
        if (methodName == "replace") {
          if (methodArgs.Count != 2 || methodArgs[0] is not string oldStr || methodArgs[1] is not string newStr) {
            throw new Exception("RUNTIME ERROR: replace() expects 2 string arguments.");
          }
          return str.Replace(oldStr, newStr);
        }
        throw new Exception($"RUNTIME ERROR: String has no method '{methodName}'.");
      }
    }

    throw new Exception($"RUNTIME ERROR: Invalid member access target.");
  }

  private object? EvaluateStaticMemberAccess(Expression leftExpr, Expression rightExpr, Environment env) {
    var enumName = (leftExpr as IdentifierExpression)?.Name ?? throw new Exception("RUNTIME ERROR: Left side of '::' must be an enum name.");
    var memberName = (rightExpr as IdentifierExpression)?.Name ?? throw new Exception("RUNTIME ERROR: Right side of '::' must be an enum member.");

    var enumObj = env.Get(enumName);
    if (enumObj is not PinoEnum pinoEnum) {
      throw new Exception($"RUNTIME ERROR: Enum '{enumName}' is not defined.");
    }

    if (!pinoEnum.Members.Contains(memberName)) {
      throw new Exception($"RUNTIME ERROR: Enum '{enumName}' has no member '{memberName}'.");
    }

    return new PinoEnumValue(enumName, memberName);
  }

    bool IsNumeric(object? val) => val is double || val is long || val is int || val is float;

  private object EvaluateBinaryOperation(object? left, OperatorType op, object? right) {
    // Handle String Concatenation
    if (op == OperatorType.Addition && (left is string || right is string)) {
      return (left?.ToString() ?? "") + (right?.ToString() ?? "");
    }

    // Numeric parsing helper
    double GetDouble(object? val) => val is double d ? d : Convert.ToDouble(val);
    long GetLong(object? val) => val is long l ? l : Convert.ToInt64(val);

    bool isFloat = left is double || right is double;

    switch (op) {
      case OperatorType.Addition:
        return isFloat ? GetDouble(left) + GetDouble(right) : GetLong(left) + GetLong(right);
      case OperatorType.Subtraction:
        return isFloat ? GetDouble(left) - GetDouble(right) : GetLong(left) - GetLong(right);
      case OperatorType.Multiplication:
        return isFloat ? GetDouble(left) * GetDouble(right) : GetLong(left) * GetLong(right);
      case OperatorType.Division:
        return isFloat ? GetDouble(left) / GetDouble(right) : GetLong(left) / GetLong(right);
      case OperatorType.Modulus:
        return isFloat ? GetDouble(left) % GetDouble(right) : GetLong(left) % GetLong(right);

      case OperatorType.LessThan:
        return isFloat ? GetDouble(left) < GetDouble(right) : GetLong(left) < GetLong(right);
      case OperatorType.LessThanEqual:
        return isFloat ? GetDouble(left) <= GetDouble(right) : GetLong(left) <= GetLong(right);
      case OperatorType.GreaterThan:
        return isFloat ? GetDouble(left) > GetDouble(right) : GetLong(left) > GetLong(right);
      case OperatorType.GreaterThanEqual:
        return isFloat ? GetDouble(left) >= GetDouble(right) : GetLong(left) >= GetLong(right);

      case OperatorType.Equal:
        if (IsNumeric(left) && IsNumeric(right)) {
          return GetDouble(left) == GetDouble(right);
        }
        return Equals(left, right);
      case OperatorType.NotEqual:
        if (IsNumeric(left) && IsNumeric(right)) {
          return GetDouble(left) != GetDouble(right);
        }
        return !Equals(left, right);

      case OperatorType.And:
        return IsTruthy(left) && IsTruthy(right);
      case OperatorType.Or:
        return IsTruthy(left) || IsTruthy(right);

      default:
        throw new Exception($"RUNTIME ERROR: Operator '{op}' not supported for numeric operations.");
    }
  }

  public string FormatVal(object? arg) {
    if (arg is List<object?> list) {
      return "[" + string.Join(", ", list.Select(FormatVal)) + "]";
    }
    return arg?.ToString() ?? "null";
  }

  private bool IsTruthy(object? value) {
    if (value == null) return false;
    if (value is bool b) return b;
    return true;
  }

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
      if (double.TryParse(arg, out var d)) return (long)d;
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
      return (long)_rand.Next(0, (int)max);
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
      System.Threading.Thread.Sleep((int)ms);
      return null;
    }
  }

  private class TypeFunction : IPinoCallable {
    public int Arity => 1;
    public object? Call(Evaluator evaluator, List<object?> arguments) {
      var val = arguments[0];
      if (val == null) return "null";
      if (val is bool) return "bool";
      if (val is long) return "int";
      if (val is double) return "float";
      if (val is string) return "string";
      if (val is List<object?>) return "vector";
      if (val is PinoStructInstance) return "struct";
      if (val is IPinoCallable) return "function";
      if (val is PinoEnumValue) return "enum";
      return val.GetType().Name.ToLower();
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
