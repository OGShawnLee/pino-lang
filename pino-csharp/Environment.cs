using System;
using System.Collections.Generic;

namespace Pino;

public class Environment {
  private readonly Dictionary<string, (object? Value, bool IsConstant, string Typing)> _values = new();
  private readonly Environment? _parent;
  public HashSet<string> PublicExports { get; } = new();

  public Environment(Environment? parent = null) {
    _parent = parent;
  }

  public void Define(string name, object? value, bool isConstant, string typing = "") {
    if (_values.ContainsKey(name)) {
      throw new Exception($"RUNTIME ERROR: Variable '{name}' is already defined in this scope.");
    }
    _values[name] = (value, isConstant, typing);
  }

  public void Assign(string name, object? value) {
    if (_values.ContainsKey(name)) {
      var binding = _values[name];
      if (binding.IsConstant) {
        throw new Exception($"RUNTIME ERROR: Cannot reassign constant variable '{name}'.");
      }
      _values[name] = (value, false, binding.Typing);
      return;
    }

    if (_parent != null) {
      _parent.Assign(name, value);
      return;
    }

    throw new Exception($"RUNTIME ERROR: Variable '{name}' is not declared.");
  }

  public object? Get(string name) {
    if (_values.ContainsKey(name)) {
      return _values[name].Value;
    }

    if (_parent != null) {
      return _parent.Get(name);
    }

    Console.WriteLine($"Get failed for variable '{name}'!");
    var curr = this;
    int depth = 0;
    while (curr != null) {
      Console.WriteLine($"Depth {depth}: keys = [{string.Join(", ", curr._values.Keys)}]");
      curr = curr._parent;
      depth++;
    }

    throw new Exception($"RUNTIME ERROR: Variable '{name}' is not declared.");
  }

  public bool Exists(string name) {
    if (_values.ContainsKey(name)) return true;
    if (_parent != null) return _parent.Exists(name);
    return false;
  }
}
