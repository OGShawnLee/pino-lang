using System;
using System.Collections.Generic;

namespace Pino;

public class GlobalBox {
  public object? Value { get; set; }
  public bool IsConstant { get; set; }
  public string Typing { get; set; }

  public GlobalBox(object? value, bool isConstant, string typing) {
    Value = value;
    IsConstant = isConstant;
    Typing = typing;
  }
}

public class Environment {
  private readonly Dictionary<string, GlobalBox> _values = new();
  private readonly Environment? _parent;
  public HashSet<string> PublicExports { get; } = new();

  public Environment(Environment? parent = null) {
    _parent = parent;
  }

  public void Define(string name, object? value, bool isConstant, string typing = "") {
    if (_values.TryGetValue(name, out var existing)) {
      existing.Value = value;
      existing.IsConstant = isConstant;
      existing.Typing = typing;
      return;
    }
    _values[name] = new GlobalBox(value, isConstant, typing);
  }

  public void Assign(string name, object? value) {
    if (_values.TryGetValue(name, out var box)) {
      if (box.IsConstant) {
        throw new Exception($"RUNTIME ERROR: Cannot reassign constant variable '{name}'.");
      }
      box.Value = value;
      return;
    }

    if (_parent != null) {
      _parent.Assign(name, value);
      return;
    }

    throw new Exception($"RUNTIME ERROR: Variable '{name}' is not declared.");
  }

  public object? Get(string name) {
    if (_values.TryGetValue(name, out var box)) {
      return box.Value;
    }

    if (_parent != null) {
      return _parent.Get(name);
    }

    throw new Exception($"RUNTIME ERROR: Variable '{name}' is not declared.");
  }

  public GlobalBox GetBox(string name) {
    if (_values.TryGetValue(name, out var box)) {
      return box;
    }
    if (_parent != null) {
      return _parent.GetBox(name);
    }
    box = new GlobalBox(null, false, "");
    _values[name] = box;
    return box;
  }

  public bool Exists(string name) {
    if (_values.ContainsKey(name)) return true;
    if (_parent != null) return _parent.Exists(name);
    return false;
  }

  public object? GetAt(int distance, string name) {
    if (distance == 0) return _values[name].Value;
    return Ancestor(distance)._values[name].Value;
  }

  public void AssignAt(int distance, string name, object? value) {
    var ancestor = distance == 0 ? this : Ancestor(distance);
    var box = ancestor._values[name];
    if (box.IsConstant) {
      throw new Exception($"RUNTIME ERROR: Cannot reassign constant variable '{name}'.");
    }
    box.Value = value;
  }

  private Environment Ancestor(int distance) {
    var environment = this;
    for (int i = 0; i < distance; i++) {
      environment = environment._parent ?? throw new Exception($"RUNTIME ERROR: Scope ancestor at distance {distance} not found.");
    }
    return environment;
  }
}
