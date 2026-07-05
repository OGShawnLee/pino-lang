using System;
using System.Collections.Generic;

namespace Pino;

public class VM {
  public enum VMResult {
    OK,
    COMPILE_ERROR,
    RUNTIME_ERROR
  }

  private struct CallFrame {
    public PinoVMFunction Function;
    public int Ip;
    public int Slots; // Stack offset for this frame's locals
  }

  private const int StackMax = 2048;
  private const int FramesMax = 64;

  private readonly object?[] _stack = new object?[StackMax];
  private int _stackTop = 0;

  private readonly CallFrame[] _frames = new CallFrame[FramesMax];
  private int _frameCount = 0;

  private readonly Evaluator _evaluator;
  private readonly Environment _globals;

  public VM(Evaluator evaluator, Environment globals) {
    _evaluator = evaluator;
    _globals = globals;
  }

  public object? Execute(PinoVMFunction function, List<object?>? args = null) {
    _stackTop = 0;
    _frameCount = 0;

    // Push the function onto stack (slot 0 of frame 0)
    Push(function);

    if (args != null) {
      foreach (var arg in args) {
        Push(arg);
      }
    }

    _frames[_frameCount++] = new CallFrame {
      Function = function,
      Ip = 0,
      Slots = 0
    };
    _evaluator.CallStack.Push(function.Name);

    try {
      var result = Run();
      if (result == VMResult.OK) {
        return _stackTop > 0 ? Pop() : null;
      }
      throw new Exception("RUNTIME ERROR: VM execution failed.");
    } finally {
      _evaluator.CallStack.Clear();
    }
  }

  private VMResult Run() {
    // Cache the top-most frame in local variables for register speed
    int frameIndex = _frameCount - 1;
    PinoVMFunction function = _frames[frameIndex].Function;
    List<byte> code = function.Chunk.Code;
    List<object?> constants = function.Chunk.Constants;
    List<GlobalBox?> globalBoxes = function.Chunk.GlobalBoxes;
    int ip = _frames[frameIndex].Ip;
    int slots = _frames[frameIndex].Slots;

    while (true) {
      byte instruction = code[ip++];

      switch ((OperationCode)instruction) {
        case OperationCode.OP_CONSTANT: {
          byte b1 = code[ip++];
          byte b2 = code[ip++];
          ushort idx = (ushort)((b1 << 8) | b2);
          Push(constants[idx]);
          break;
        }

        case OperationCode.OP_TUPLE: {
          byte b1 = code[ip++];
          byte b2 = code[ip++];
          ushort idx = (ushort)((b1 << 8) | b2);
          var labels = (List<string>)constants[idx];
          var fields = new Dictionary<string, object?>();
          for (int i = labels.Count - 1; i >= 0; i--) {
            fields[labels[i]] = Pop();
          }
          Push(new PinoTupleResult(fields));
          break;
        }

        case OperationCode.OP_UNPACK_TUPLE: {
          byte b1 = code[ip++];
          byte b2 = code[ip++];
          ushort idx = (ushort)((b1 << 8) | b2);
          var labels = (List<string>)constants[idx];
          var tuple = Pop();
          if (tuple is not PinoTupleResult tupleRes) {
            throw new Exception("RUNTIME ERROR: Expected a tuple on top of stack during OP_UNPACK_TUPLE.");
          }
          foreach (var label in labels) {
            if (!tupleRes.Fields.TryGetValue(label, out var val)) {
              throw new Exception($"RUNTIME ERROR: Field '{label}' not found in tuple.");
            }
            Push(val);
          }
          break;
        }

        case OperationCode.OP_TRUE:
          Push(true);
          break;

        case OperationCode.OP_FALSE:
          Push(false);
          break;

        case OperationCode.OP_NIL:
          Push(null);
          break;

        case OperationCode.OP_POP:
          Pop();
          break;

        case OperationCode.OP_ADD: {
          var b = _stack[--_stackTop];
          var a = _stack[_stackTop - 1];
          _stack[_stackTop - 1] = Add(a, b);
          break;
        }

        case OperationCode.OP_SUB: {
          var b = _stack[--_stackTop];
          var a = _stack[_stackTop - 1];
          _stack[_stackTop - 1] = Subtract(a, b);
          break;
        }

        case OperationCode.OP_MUL: {
          var b = _stack[--_stackTop];
          var a = _stack[_stackTop - 1];
          _stack[_stackTop - 1] = Multiply(a, b);
          break;
        }

        case OperationCode.OP_DIV: {
          var b = _stack[--_stackTop];
          var a = _stack[_stackTop - 1];
          _stack[_stackTop - 1] = Divide(a, b);
          break;
        }

        case OperationCode.OP_MOD: {
          var b = _stack[--_stackTop];
          var a = _stack[_stackTop - 1];
          _stack[_stackTop - 1] = Modulus(a, b);
          break;
        }

        case OperationCode.OP_EQUAL: {
          var b = _stack[--_stackTop];
          var a = _stack[_stackTop - 1];
          _stack[_stackTop - 1] = Equal(a, b);
          break;
        }

        case OperationCode.OP_NOT_EQUAL: {
          var b = _stack[--_stackTop];
          var a = _stack[_stackTop - 1];
          _stack[_stackTop - 1] = !Equal(a, b);
          break;
        }

        case OperationCode.OP_LESS: {
          var b = _stack[--_stackTop];
          var a = _stack[_stackTop - 1];
          _stack[_stackTop - 1] = LessThan(a, b);
          break;
        }

        case OperationCode.OP_LESS_EQUAL: {
          var b = _stack[--_stackTop];
          var a = _stack[_stackTop - 1];
          _stack[_stackTop - 1] = LessThanEqual(a, b);
          break;
        }

        case OperationCode.OP_GREATER: {
          var b = _stack[--_stackTop];
          var a = _stack[_stackTop - 1];
          _stack[_stackTop - 1] = GreaterThan(a, b);
          break;
        }

        case OperationCode.OP_GREATER_EQUAL: {
          var b = _stack[--_stackTop];
          var a = _stack[_stackTop - 1];
          _stack[_stackTop - 1] = GreaterThanEqual(a, b);
          break;
        }

        case OperationCode.OP_ADD_INT: {
          var b = (long)_stack[--_stackTop]!;
          var a = (long)_stack[_stackTop - 1]!;
          _stack[_stackTop - 1] = BoxCache.Box(a + b);
          break;
        }

        case OperationCode.OP_SUB_INT: {
          var b = (long)_stack[--_stackTop]!;
          var a = (long)_stack[_stackTop - 1]!;
          _stack[_stackTop - 1] = BoxCache.Box(a - b);
          break;
        }

        case OperationCode.OP_MUL_INT: {
          var b = (long)_stack[--_stackTop]!;
          var a = (long)_stack[_stackTop - 1]!;
          _stack[_stackTop - 1] = BoxCache.Box(a * b);
          break;
        }

        case OperationCode.OP_DIV_INT: {
          var b = (long)_stack[--_stackTop]!;
          var a = (long)_stack[_stackTop - 1]!;
          _stack[_stackTop - 1] = BoxCache.Box(a / b);
          break;
        }

        case OperationCode.OP_MOD_INT: {
          var b = (long)_stack[--_stackTop]!;
          var a = (long)_stack[_stackTop - 1]!;
          _stack[_stackTop - 1] = BoxCache.Box(a % b);
          break;
        }

        case OperationCode.OP_EQUAL_INT: {
          var b = (long)_stack[--_stackTop]!;
          var a = (long)_stack[_stackTop - 1]!;
          _stack[_stackTop - 1] = a == b;
          break;
        }

        case OperationCode.OP_LESS_INT: {
          var b = (long)_stack[--_stackTop]!;
          var a = (long)_stack[_stackTop - 1]!;
          _stack[_stackTop - 1] = a < b;
          break;
        }

        case OperationCode.OP_LESS_EQUAL_INT: {
          var b = (long)_stack[--_stackTop]!;
          var a = (long)_stack[_stackTop - 1]!;
          _stack[_stackTop - 1] = a <= b;
          break;
        }

        case OperationCode.OP_GREATER_INT: {
          var b = (long)_stack[--_stackTop]!;
          var a = (long)_stack[_stackTop - 1]!;
          _stack[_stackTop - 1] = a > b;
          break;
        }

        case OperationCode.OP_GREATER_EQUAL_INT: {
          var b = (long)_stack[--_stackTop]!;
          var a = (long)_stack[_stackTop - 1]!;
          _stack[_stackTop - 1] = a >= b;
          break;
        }

        case OperationCode.OP_AND: {
          var b = _stack[--_stackTop];
          var a = _stack[_stackTop - 1];
          _stack[_stackTop - 1] = IsTruthy(a) && IsTruthy(b);
          break;
        }

        case OperationCode.OP_OR: {
          var b = _stack[--_stackTop];
          var a = _stack[_stackTop - 1];
          _stack[_stackTop - 1] = IsTruthy(a) || IsTruthy(b);
          break;
        }

        case OperationCode.OP_NOT:
          _stack[_stackTop - 1] = !IsTruthy(_stack[_stackTop - 1]);
          break;

        case OperationCode.OP_DEFINE_GLOBAL: {
          byte b1 = code[ip++];
          byte b2 = code[ip++];
          ushort idx = (ushort)((b1 << 8) | b2);
          var box = globalBoxes[idx];
          if (box == null) {
            string name = (string)constants[idx]!;
            box = _globals.GetBox(name);
            globalBoxes[idx] = box;
          }
          box.Value = Pop();
          break;
        }

        case OperationCode.OP_GET_GLOBAL: {
          byte b1 = code[ip++];
          byte b2 = code[ip++];
          ushort idx = (ushort)((b1 << 8) | b2);
          var box = globalBoxes[idx];
          if (box == null) {
            string name = (string)constants[idx]!;
            box = _globals.GetBox(name);
            globalBoxes[idx] = box;
          }
          Push(box.Value);
          break;
        }

        case OperationCode.OP_SET_GLOBAL: {
          byte b1 = code[ip++];
          byte b2 = code[ip++];
          ushort idx = (ushort)((b1 << 8) | b2);
          var box = globalBoxes[idx];
          if (box == null) {
            string name = (string)constants[idx]!;
            box = _globals.GetBox(name);
            globalBoxes[idx] = box;
          }
          box.Value = Peek(0);
          break;
        }

        case OperationCode.OP_GET_LOCAL: {
          byte slot = code[ip++];
          Push(_stack[slots + slot]);
          break;
        }

        case OperationCode.OP_SET_LOCAL: {
          byte slot = code[ip++];
          _stack[slots + slot] = Peek(0);
          break;
        }

        case OperationCode.OP_JUMP: {
          byte b1 = code[ip++];
          byte b2 = code[ip++];
          short offset = (short)((b1 << 8) | b2);
          ip += offset;
          break;
        }

        case OperationCode.OP_JUMP_IF_FALSE: {
          byte b1 = code[ip++];
          byte b2 = code[ip++];
          short offset = (short)((b1 << 8) | b2);
          if (!IsTruthy(Peek(0))) {
            ip += offset;
          }
          break;
        }

        case OperationCode.OP_STRING_LEN: {
          var val = Pop();
          string s = (string)val!;
          int count = 0;
          foreach (var _ in s.EnumerateRunes()) count++;
          Push((long)count);
          break;
        }

        case OperationCode.OP_STRING_GET_INDEX: {
          var indexVal = Pop();
          var colVal = Pop();
          long idx = Convert.ToInt64(indexVal);
          string s = (string)colVal!;
          int i = 0;
          int codePoint = 0;
          foreach (var r in s.EnumerateRunes()) {
            if (i == (int)idx) {
              codePoint = r.Value;
              break;
            }
            i++;
          }
          Push(new PinoRune(codePoint));
          break;
        }

        case OperationCode.OP_LIST_LEN: {
          var val = Pop();
          Push((long)((List<object?>)val!).Count);
          break;
        }

        case OperationCode.OP_LIST_GET_INDEX: {
          var indexVal = Pop();
          var colVal = Pop();
          long idx = Convert.ToInt64(indexVal);
          Push(((List<object?>)colVal!)[(int)idx]);
          break;
        }

        case OperationCode.OP_CALL: {
          byte argCount = code[ip++];
          _frames[frameIndex].Ip = ip;
          if (!CallValue(Peek(argCount), argCount)) {
            return VMResult.RUNTIME_ERROR;
          }
          // Reload local variables for the active call frame
          frameIndex = _frameCount - 1;
          function = _frames[frameIndex].Function;
          code = function.Chunk.Code;
          constants = function.Chunk.Constants;
          globalBoxes = function.Chunk.GlobalBoxes;
          ip = _frames[frameIndex].Ip;
          slots = _frames[frameIndex].Slots;
          break;
        }

        case OperationCode.OP_RETURN: {
          var result = Pop();
          if (_evaluator.CallStack.Count > 0) _evaluator.CallStack.Pop();
          _frameCount--;
          if (_frameCount == 0) {
            Push(result);
            return VMResult.OK;
          }
          _stackTop = slots;
          Push(result);
          // Restore parent frame
          frameIndex = _frameCount - 1;
          function = _frames[frameIndex].Function;
          code = function.Chunk.Code;
          constants = function.Chunk.Constants;
          globalBoxes = function.Chunk.GlobalBoxes;
          ip = _frames[frameIndex].Ip;
          slots = _frames[frameIndex].Slots;
          break;
        }

        default:
          return VMResult.RUNTIME_ERROR;
      }
    }
  }

  private void Push(object? value) {
    if (_stackTop >= StackMax) {
      throw new Exception("RUNTIME ERROR: Stack overflow.");
    }
    _stack[_stackTop++] = value;
  }

  private object? Pop() {
    if (_stackTop == 0) {
      throw new Exception("RUNTIME ERROR: Stack underflow.");
    }
    return _stack[--_stackTop];
  }

  private object? Peek(int distance) {
    return _stack[_stackTop - 1 - distance];
  }

  private bool CallValue(object? callee, int argCount) {
    if (callee is PinoVMFunction vmFn) {
      if (vmFn.Arity != argCount) {
        throw new Exception($"RUNTIME ERROR: Expected {vmFn.Arity} arguments, but got {argCount}.");
      }
      if (_frameCount >= FramesMax) {
        throw new Exception("RUNTIME ERROR: Call stack overflow.");
      }
      _frames[_frameCount++] = new CallFrame {
        Function = vmFn,
        Ip = 0,
        Slots = _stackTop - argCount - 1
      };
      _evaluator.CallStack.Push(vmFn.Name);
      return true;
    }

    if (callee is IPinoCallable callable) {
      var args = new List<object?>();
      for (int i = 0; i < argCount; i++) {
        args.Insert(0, Pop());
      }
      Pop(); // Pop the callee
      var result = callable.Call(_evaluator, args);
      Push(result);
      return true;
    }

    throw new Exception("RUNTIME ERROR: Value is not callable.");
  }

  // --- ARITHMETIC / COMPARISON UTILITIES ---
  private object Add(object? left, object? right) {
    if (left is PinoRune r1 && right is PinoRune r2) return r1.ToString() + r2.ToString();
    if (left is PinoRune r3 && right is long l1) return new PinoRune(r3.CodePoint + (int)l1);
    if (left is long l2 && right is PinoRune r4) return new PinoRune(r4.CodePoint + (int)l2);
    if (left is long l && right is long r) {
      return BoxCache.Box(l + r);
    }
    if (left is double d1 && right is double d2) {
      return d1 + d2;
    }
    if (left is string || right is string) {
      return (left?.ToString() ?? "") + (right?.ToString() ?? "");
    }
    if (left is double || right is double) {
      return Convert.ToDouble(left) + Convert.ToDouble(right);
    }
    return BoxCache.Box(Convert.ToInt64(left) + Convert.ToInt64(right));
  }

  private object Subtract(object? left, object? right) {
    if (left is PinoRune r1 && right is long l1) return new PinoRune(r1.CodePoint - (int)l1);
    if (left is PinoRune r2 && right is PinoRune r3) return (long)(r2.CodePoint - r3.CodePoint);
    if (left is long l && right is long r) {
      return BoxCache.Box(l - r);
    }
    if (left is double d1 && right is double d2) {
      return d1 - d2;
    }
    if (left is double || right is double) {
      return Convert.ToDouble(left) - Convert.ToDouble(right);
    }
    return BoxCache.Box(Convert.ToInt64(left) - Convert.ToInt64(right));
  }

  private object Multiply(object? left, object? right) {
    if (left is long l && right is long r) {
      return BoxCache.Box(l * r);
    }
    if (left is double d1 && right is double d2) {
      return d1 * d2;
    }
    if (left is double || right is double) {
      return Convert.ToDouble(left) * Convert.ToDouble(right);
    }
    return BoxCache.Box(Convert.ToInt64(left) * Convert.ToInt64(right));
  }

  private object Divide(object? left, object? right) {
    if (left is long l && right is long r) {
      return BoxCache.Box(l / r);
    }
    if (left is double d1 && right is double d2) {
      return d1 / d2;
    }
    if (left is double || right is double) {
      return Convert.ToDouble(left) / Convert.ToDouble(right);
    }
    return BoxCache.Box(Convert.ToInt64(left) / Convert.ToInt64(right));
  }

  private object Modulus(object? left, object? right) {
    if (left is long l && right is long r) {
      return BoxCache.Box(l % r);
    }
    if (left is double d1 && right is double d2) {
      return d1 % d2;
    }
    if (left is double || right is double) {
      return Convert.ToDouble(left) % Convert.ToDouble(right);
    }
    return BoxCache.Box(Convert.ToInt64(left) % Convert.ToInt64(right));
  }

  private bool Equal(object? left, object? right) {
    if (left == null && right == null) return true;
    if (left == null || right == null) return false;
    if (left is PinoRune r1 && right is PinoRune r2) return r1.CodePoint == r2.CodePoint;
    if (left is long l1 && right is long l2) return l1 == l2;
    if (left is double d1 && right is double d2) return d1 == d2;
    if (IsNumeric(left) && IsNumeric(right)) {
      return Convert.ToDouble(left) == Convert.ToDouble(right);
    }
    return Equals(left, right);
  }

  private bool LessThan(object? left, object? right) {
    if (left is PinoRune r1 && right is PinoRune r2) return r1.CodePoint < r2.CodePoint;
    if (left is long l1 && right is long l2) return l1 < l2;
    if (left is double d1 && right is double d2) return d1 < d2;
    if (left is double || right is double) {
      return Convert.ToDouble(left) < Convert.ToDouble(right);
    }
    return Convert.ToInt64(left) < Convert.ToInt64(right);
  }

  private bool LessThanEqual(object? left, object? right) {
    if (left is PinoRune r1 && right is PinoRune r2) return r1.CodePoint <= r2.CodePoint;
    if (left is long l1 && right is long l2) return l1 <= l2;
    if (left is double d1 && right is double d2) return d1 <= d2;
    if (left is double || right is double) {
      return Convert.ToDouble(left) <= Convert.ToDouble(right);
    }
    return Convert.ToInt64(left) <= Convert.ToInt64(right);
  }

  private bool GreaterThan(object? left, object? right) {
    if (left is PinoRune r1 && right is PinoRune r2) return r1.CodePoint > r2.CodePoint;
    if (left is long l1 && right is long l2) return l1 > l2;
    if (left is double d1 && right is double d2) return d1 > d2;
    if (left is double || right is double) {
      return Convert.ToDouble(left) > Convert.ToDouble(right);
    }
    return Convert.ToInt64(left) > Convert.ToInt64(right);
  }

  private bool GreaterThanEqual(object? left, object? right) {
    if (left is PinoRune r1 && right is PinoRune r2) return r1.CodePoint >= r2.CodePoint;
    if (left is long l1 && right is long l2) return l1 >= l2;
    if (left is double d1 && right is double d2) return d1 >= d2;
    if (left is double || right is double) {
      return Convert.ToDouble(left) >= Convert.ToDouble(right);
    }
    return Convert.ToInt64(left) >= Convert.ToInt64(right);
  }

  private bool IsNumeric(object? val) {
    return val is double || val is long || val is int || val is float;
  }

  private bool IsTruthy(object? val) {
    if (val == null) return false;
    if (val is bool b) return b;
    return true;
  }
}

internal static class BoxCache {
  private static readonly object[] Cache = new object[1024];

  static BoxCache() {
    for (int i = 0; i < Cache.Length; i++) {
      Cache[i] = (long)(i - 256);
    }
  }

  public static object Box(long val) {
    if (val >= -256 && val < 768) {
      return Cache[val + 256];
    }
    return val;
  }
}
