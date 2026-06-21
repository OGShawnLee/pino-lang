using System;
using System.Collections.Generic;
using System.Linq;

namespace Pino;

public partial class Evaluator {
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
        return LookUpVariable(id, env);

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

          if (bin.Left is IdentifierExpression idExpr) {
            AssignVariable(idExpr, val, env);
            return val;
          }

          if (bin.Left is IndexAccessExpression indexAccess) {
            var target = Evaluate(indexAccess.Target, env);
            var assignIndexVal = Evaluate(indexAccess.Index, env);
            if (target is List<object?> assignList) {
              long assignIdx = assignIndexVal is long l ? l : Convert.ToInt64(assignIndexVal);
              if (assignIdx < 0 || assignIdx >= assignList.Count) {
                throw new Exception($"RUNTIME ERROR: Index {assignIdx} out of range for vector of size {assignList.Count}.");
              }
              assignList[(int) assignIdx] = val;
              return val;
            }
            if (target is Dictionary<object, object?> assignDict) {
              if (assignIndexVal == null) {
                throw new Exception("RUNTIME ERROR: Map key cannot be null.");
              }
              assignDict[assignIndexVal] = val;
              return val;
            }
            throw new Exception("RUNTIME ERROR: Cannot assign to index of non-vector and non-map object.");
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

          if (bin.Left is IdentifierExpression idExpr) {
            var currentVal = LookUpVariable(idExpr, env);
            var newVal = EvaluateBinaryOperation(currentVal, baseOp, delta);
            AssignVariable(idExpr, newVal, env);
            return newVal;
          }

          if (bin.Left is IndexAccessExpression indexAccess) {
            var target = Evaluate(indexAccess.Target, env);
            var compoundIndexVal = Evaluate(indexAccess.Index, env);
            if (target is List<object?> compoundList) {
              long compoundIdx = compoundIndexVal is long l ? l : Convert.ToInt64(compoundIndexVal);
              if (compoundIdx < 0 || compoundIdx >= compoundList.Count) {
                throw new Exception($"RUNTIME ERROR: Index {compoundIdx} out of range for vector of size {compoundList.Count}.");
              }
              var currentVal = compoundList[(int) compoundIdx];
              var newVal = EvaluateBinaryOperation(currentVal, baseOp, delta);
              compoundList[(int) compoundIdx] = newVal;
              return newVal;
            }
            if (target is Dictionary<object, object?> compoundDict) {
              if (compoundIndexVal == null) {
                throw new Exception("RUNTIME ERROR: Map key cannot be null.");
              }
              if (!compoundDict.ContainsKey(compoundIndexVal)) {
                throw new Exception($"RUNTIME ERROR: Key '{compoundIndexVal}' not found in map.");
              }
              var currentVal = compoundDict[compoundIndexVal];
              var newVal = EvaluateBinaryOperation(currentVal, baseOp, delta);
              compoundDict[compoundIndexVal] = newVal;
              return newVal;
            }
            throw new Exception("RUNTIME ERROR: Cannot assign to index of non-vector and non-map object.");
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
          // Vector init constructor: []type { len: limit, init: expr } | []type
          var lenVal = vec.Len == null ? 0 : Evaluate(vec.Len!, env);
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
        object? callableObj;
        if (call.Distance != -1) {
          callableObj = env.GetAt(call.Distance, call.Callee);
        } else {
          callableObj = env.Get(call.Callee);
        }
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

      case IndexAccessExpression indexAccess:
        var targetVal = Evaluate(indexAccess.Target, env);
        var readIndexVal = Evaluate(indexAccess.Index, env);

        if (targetVal is List<object?> readList) {
          long readIdx = readIndexVal is long l ? l : Convert.ToInt64(readIndexVal);
          if (readIdx < 0 || readIdx >= readList.Count) {
            throw new Exception($"RUNTIME ERROR: Index {readIdx} out of range for vector of size {readList.Count}.");
          }
          return readList[(int) readIdx];
        }
        if (targetVal is Dictionary<object, object?> readDict) {
          if (readIndexVal == null) {
            throw new Exception("RUNTIME ERROR: Map key cannot be null.");
          }
          if (!readDict.ContainsKey(readIndexVal)) {
            throw new Exception($"RUNTIME ERROR: Key '{readIndexVal}' not found in map.");
          }
          return readDict[readIndexVal];
        }
        if (targetVal is string readStr) {
          long readIdx = readIndexVal is long l ? l : Convert.ToInt64(readIndexVal);
          if (readIdx < 0 || readIdx >= readStr.Length) {
            throw new Exception($"RUNTIME ERROR: Index {readIdx} out of range for string of length {readStr.Length}.");
          }
          return readStr[(int) readIdx].ToString();
        }
        throw new Exception("RUNTIME ERROR: Cannot apply index access to non-vector, non-string, and non-map object.");

      case MapExpression map:
        var mapDict = new Dictionary<object, object?>();
        foreach (var entry in map.Entries) {
          var k = Evaluate(entry.Key, env);
          if (k == null) {
            throw new Exception("RUNTIME ERROR: Map key cannot be null.");
          }
          var v = Evaluate(entry.Value, env);
          mapDict[k] = v;
        }
        return mapDict;

      default:
        throw new Exception($"RUNTIME ERROR: Unknown expression type '{expression.GetType().Name}'.");
    }
  }

  private object? EvaluateMemberAccess(Expression leftExpr, Expression rightExpr, Environment env) {
    var leftVal = Evaluate(leftExpr, env);

    if (leftVal is PinoStructInstance instance) {
      // Case 1: instance:method(...) where right is FunctionCallExpression
      if (rightExpr is FunctionCallExpression methodCall) {
        var methodDecl = instance.Struct.Methods.Find(m => m.Identifier == methodCall.Callee && !m.IsStatic);
        if (methodDecl == null) {
          throw new Exception($"RUNTIME ERROR: Struct '{instance.Struct.Name}' has no instance method '{methodCall.Callee}'.");
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
    } else if (leftVal is Dictionary<object, object?> dict) {
      // Case 5: map:len or map:length
      if (rightExpr is IdentifierExpression mapId && (mapId.Name == "length" || mapId.Name == "len")) {
        return (long) dict.Count;
      }

      // Case 6: map method calls
      if (rightExpr is FunctionCallExpression methodCall) {
        var methodName = methodCall.Callee;
        var methodArgs = methodCall.Arguments.Select(a => Evaluate(a, env)).ToList();

        if (methodName == "keys") {
          if (methodArgs.Count != 0) throw new Exception("RUNTIME ERROR: keys() expects 0 arguments.");
          return dict.Keys.Cast<object?>().ToList();
        }
        if (methodName == "values") {
          if (methodArgs.Count != 0) throw new Exception("RUNTIME ERROR: values() expects 0 arguments.");
          return dict.Values.ToList();
        }
        if (methodName == "remove") {
          if (methodArgs.Count != 1) throw new Exception("RUNTIME ERROR: remove() expects 1 argument.");
          var key = methodArgs[0];
          if (key == null) throw new Exception("RUNTIME ERROR: remove() key cannot be null.");
          if (dict.TryGetValue(key, out var removedVal)) {
            dict.Remove(key);
            return removedVal;
          }
          return null;
        }
        throw new Exception($"RUNTIME ERROR: Map has no method '{methodName}'.");
      }
    } else if (leftVal is List<object?> list) {
      // Case 3: vector:len or vector:length
      if (rightExpr is IdentifierExpression listId && (listId.Name == "length" || listId.Name == "len")) {
        return (long) list.Count;
      }

      // Case 4: vector method calls
      if (rightExpr is FunctionCallExpression methodCall) {
        var methodName = methodCall.Callee;
        var methodArgs = methodCall.Arguments.Select(a => Evaluate(a, env)).ToList();

        if (methodName == "each") {
          if (methodArgs.Count < 1 || methodArgs[0] is not IPinoCallable func) {
            throw new Exception("RUNTIME ERROR: each() expects a callable argument.");
          }
          var args = new List<object?> { null };
          if (func.Arity == 2) {
            args.Add(0L);
          }
          for (int i = 0; i < list.Count; i++) {
            args[0] = list[i];
            if (func.Arity == 2) {
              args[1] = (long) i;
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
          var args = new List<object?> { null };
          if (func.Arity == 2) {
            args.Add(0L);
          }
          for (int i = 0; i < list.Count; i++) {
            args[0] = list[i];
            if (func.Arity == 2) {
              args[1] = (long) i;
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
          var args = new List<object?> { null };
          if (func.Arity == 2) {
            args.Add(0L);
          }
          for (int i = 0; i < list.Count; i++) {
            args[0] = list[i];
            if (func.Arity == 2) {
              args[1] = (long) i;
            }
            if (IsTruthy(func.Call(this, args))) {
              filteredList.Add(list[i]);
            }
          }
          return filteredList;
        }

        if (methodName == "find") {
          if (methodArgs.Count < 1 || methodArgs[0] is not IPinoCallable func) {
            throw new Exception("RUNTIME ERROR: find() expects a callable argument.");
          }
          var args = new List<object?> { null };
          if (func.Arity == 2) {
            args.Add(0L);
          }
          for (int i = 0; i < list.Count; i++) {
            args[0] = list[i];
            if (func.Arity == 2) {
              args[1] = (long) i;
            }
            if (IsTruthy(func.Call(this, args))) {
              return list[i];
            }
          }
          return null;
        }

        if (methodName == "find_index") {
          if (methodArgs.Count < 1 || methodArgs[0] is not IPinoCallable func) {
            throw new Exception("RUNTIME ERROR: find_index() expects a callable argument.");
          }
          var args = new List<object?> { null };
          if (func.Arity == 2) {
            args.Add(0L);
          }
          for (int i = 0; i < list.Count; i++) {
            args[0] = list[i];
            if (func.Arity == 2) {
              args[1] = (long) i;
            }
            if (IsTruthy(func.Call(this, args))) {
              return (long) i;
            }
          }
          return -1L;
        }

        if (methodName == "any") {
          if (methodArgs.Count < 1 || methodArgs[0] is not IPinoCallable func) {
            throw new Exception("RUNTIME ERROR: any() expects a callable argument.");
          }
          var args = new List<object?> { null };
          if (func.Arity == 2) {
            args.Add(0L);
          }
          for (int i = 0; i < list.Count; i++) {
            args[0] = list[i];
            if (func.Arity == 2) {
              args[1] = (long) i;
            }
            if (IsTruthy(func.Call(this, args))) {
              return true;
            }
          }
          return false;
        }

        if (methodName == "all") {
          if (methodArgs.Count < 1 || methodArgs[0] is not IPinoCallable func) {
            throw new Exception("RUNTIME ERROR: all() expects a callable argument.");
          }
          var args = new List<object?> { null };
          if (func.Arity == 2) {
            args.Add(0L);
          }
          for (int i = 0; i < list.Count; i++) {
            args[0] = list[i];
            if (func.Arity == 2) {
              args[1] = (long) i;
            }
            if (!IsTruthy(func.Call(this, args))) {
              return false;
            }
          }
          return true;
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
          return (long) str.Length;
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
    var leftVal = Evaluate(leftExpr, env);

    if (leftVal is PinoModule module) {
      // Case 1: module::method(...)
      if (rightExpr is FunctionCallExpression methodCall) {
        var memberName = methodCall.Callee;
        if (!module.PublicExports.Contains(memberName)) {
          throw new Exception($"RUNTIME ERROR: Member '{memberName}' is not exported by module '{module.Name}' (or is private).");
        }
        var callableObj = module.Environment.Get(memberName);
        if (callableObj is not IPinoCallable callable) {
          throw new Exception($"RUNTIME ERROR: '{memberName}' is not callable.");
        }
        var methodArgs = methodCall.Arguments.Select(a => Evaluate(a, env)).ToList();
        return callable.Call(this, methodArgs);
      }

      // Case 2: module::member reference
      if (rightExpr is IdentifierExpression memberId) {
        var memberName = memberId.Name;
        if (!module.PublicExports.Contains(memberName)) {
          throw new Exception($"RUNTIME ERROR: Member '{memberName}' is not exported by module '{module.Name}' (or is private).");
        }
        return module.Environment.Get(memberName);
      }

      // Case 3: module::StructInstanceExpression
      if (rightExpr is StructInstanceExpression structInst) {
        var structName = structInst.StructName;
        if (!module.PublicExports.Contains(structName)) {
          throw new Exception($"RUNTIME ERROR: Member '{structName}' is not exported by module '{module.Name}' (or is private).");
        }
        var structDefObj = module.Environment.Get(structName);
        if (structDefObj is not PinoStruct moduleStructDef) {
          throw new Exception($"RUNTIME ERROR: '{structName}' is not a struct.");
        }
        var instance = new PinoStructInstance(moduleStructDef);
        foreach (var prop in structInst.Properties) {
          var val = prop.Value != null ? Evaluate(prop.Value, env) : null;
          instance.Fields[prop.Identifier] = val;
        }
        return instance;
      }

      throw new Exception("RUNTIME ERROR: Right side of '::' must be a member name, function call, or struct instance.");
    }

    if (leftVal is PinoStruct structDef) {
      if (rightExpr is FunctionCallExpression methodCall) {
        var methodDecl = structDef.Methods.Find(m => m.Identifier == methodCall.Callee && m.IsStatic);
        if (methodDecl == null) {
          throw new Exception($"RUNTIME ERROR: Struct '{structDef.Name}' has no static method '{methodCall.Callee}'.");
        }
        var callable = new PinoFunction(methodDecl, env);
        var methodArgs = methodCall.Arguments.Select(a => Evaluate(a, env)).ToList();
        return callable.Call(this, methodArgs);
      }

      if (rightExpr is IdentifierExpression memberId) {
        var methodDecl = structDef.Methods.Find(m => m.Identifier == memberId.Name && m.IsStatic);
        if (methodDecl == null) {
          throw new Exception($"RUNTIME ERROR: Struct '{structDef.Name}' has no static method '{memberId.Name}'.");
        }
        return new PinoFunction(methodDecl, env);
      }

      throw new Exception("RUNTIME ERROR: Right side of '::' for a struct must be a static method name or static method call.");
    }

    if (leftVal is PinoEnum pinoEnum) {
      var memberNameEnum = (rightExpr as IdentifierExpression)?.Name ?? throw new Exception("RUNTIME ERROR: Right side of '::' for an enum must be a member name.");
      if (!pinoEnum.Members.Contains(memberNameEnum)) {
        throw new Exception($"RUNTIME ERROR: Enum '{pinoEnum.Name}' has no member '{memberNameEnum}'.");
      }
      return new PinoEnumValue(pinoEnum.Name, memberNameEnum);
    }

    throw new Exception($"RUNTIME ERROR: Target is neither a module, struct, nor an enum.");
  }

  private bool IsNumeric(object? val) => val is double || val is long || val is int || val is float;

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
        if (isFloat) return GetDouble(left) + GetDouble(right);
        return GetLong(left) + GetLong(right);
      case OperatorType.Subtraction:
        if (isFloat) return GetDouble(left) - GetDouble(right);
        return GetLong(left) - GetLong(right);
      case OperatorType.Multiplication:
        if (isFloat) return GetDouble(left) * GetDouble(right);
        return GetLong(left) * GetLong(right);
      case OperatorType.Division:
        if (isFloat) return GetDouble(left) / GetDouble(right);
        return GetLong(left) / GetLong(right);
      case OperatorType.Modulus:
        if (isFloat) return GetDouble(left) % GetDouble(right);
        return GetLong(left) % GetLong(right);

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
      case OperatorType.In:
        if (right is Dictionary<object, object?> inMap) {
          return left != null && inMap.ContainsKey(left);
        }
        if (right is List<object?> inList) {
          return inList.Contains(left);
        }
        if (right is string inStr) {
          if (left is not string leftStr) {
            throw new Exception("RUNTIME ERROR: Left side of 'in' operator must be a string when right side is a string.");
          }
          return inStr.Contains(leftStr);
        }
        throw new Exception($"RUNTIME ERROR: 'in' operator not supported for type '{right?.GetType().Name ?? "null"}'.");

      default:
        throw new Exception($"RUNTIME ERROR: Operator '{op}' not supported for numeric operations.");
    }
  }

  public string FormatVal(object? arg) {
    if (arg is List<object?> list) {
      return "[" + string.Join(", ", list.Select(FormatVal)) + "]";
    }
    if (arg is Dictionary<object, object?> dict) {
      var entries = dict.Select(kv => {
        var keyStr = kv.Key is string ? $"\"{kv.Key}\"" : FormatVal(kv.Key);
        var valStr = kv.Value is string ? $"\"{kv.Value}\"" : FormatVal(kv.Value);
        return $"{keyStr}: {valStr}";
      });
      return "{" + string.Join(", ", entries) + "}";
    }
    return arg?.ToString() ?? "null";
  }

  private bool IsTruthy(object? value) {
    if (value == null) return false;
    if (value is bool b) return b;
    return true;
  }

  private object? LookUpVariable(IdentifierExpression id, Environment env) {
    if (id.Distance != -1) {
      return env.GetAt(id.Distance, id.Name);
    }
    return env.Get(id.Name);
  }

  private void AssignVariable(IdentifierExpression id, object? value, Environment env) {
    if (id.Distance != -1) {
      env.AssignAt(id.Distance, id.Name, value);
    } else {
      env.Assign(id.Name, value);
    }
  }
}
