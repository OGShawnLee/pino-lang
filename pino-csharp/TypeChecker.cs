using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pino;

public class TypeChecker {
  // Global registries
  private readonly Dictionary<string, StructDeclaration> _structs = new();
  private readonly Dictionary<string, InterfaceDeclaration> _interfaces = new();
  private readonly Dictionary<string, EnumDeclaration> _enums = new();
  private readonly Dictionary<string, FunctionDeclaration> _functions = new();

  // Environment/scopes for variable checking
  private readonly Stack<Dictionary<string, string>> _scopes = new();

  // Context for current struct and method being checked
  private StructDeclaration? _currentStruct = null;
  private bool _inStaticMethod = false;

  // Cache of checked modules to prevent double-checking
  private readonly Dictionary<string, TypeChecker> _moduleCheckers = new();
  private readonly HashSet<string> _currentlyCheckingModules = new();

  // Guard against infinite recursion during return type inference of recursive functions
  private readonly HashSet<string> _inferringFunctions = new();

  // Standard library definitions
  private static readonly Dictionary<string, string> BuiltInFunctions = new() {
    { "println", "fn(...)" },
    { "readline", "fn(...) string" },
    { "int", "fn(...) int" },
    { "float", "fn(...) float" },
    { "rand", "fn(...) float" },
    { "time", "fn() int" },
    { "sleep", "fn(int)" },
    { "type", "fn(any) string" },
    { "str", "fn(any) string" },
    { "clear", "fn()" }
  };

  public StructDeclaration? FindStruct(string name) {
    if (_structs.TryGetValue(name, out var localStruct)) {
      return localStruct;
    }
    foreach (var modChecker in _moduleCheckers.Values) {
      var importedStruct = modChecker.FindStruct(name);
      if (importedStruct != null && importedStruct.IsPublic) {
        return importedStruct;
      }
    }
    return null;
  }

  public InterfaceDeclaration? FindInterface(string name) {
    if (_interfaces.TryGetValue(name, out var localInterface)) {
      return localInterface;
    }
    foreach (var modChecker in _moduleCheckers.Values) {
      var importedInterface = modChecker.FindInterface(name);
      if (importedInterface != null && importedInterface.IsPublic) {
        return importedInterface;
      }
    }
    return null;
  }

  public EnumDeclaration? FindEnum(string name) {
    if (_enums.TryGetValue(name, out var localEnum)) {
      return localEnum;
    }
    foreach (var modChecker in _moduleCheckers.Values) {
      var importedEnum = modChecker.FindEnum(name);
      if (importedEnum != null && importedEnum.IsPublic) {
        return importedEnum;
      }
    }
    return null;
  }

  public void Check(ProgramStatement program) {
    PushScope();

    // Pass 1: Gather global symbols
    foreach (var stmt in program.Statements) {
      switch (stmt) {
        case StructDeclaration structDecl:
          _structs[structDecl.Identifier] = structDecl;
          break;
        case InterfaceDeclaration interfaceDecl:
          _interfaces[interfaceDecl.Identifier] = interfaceDecl;
          break;
        case EnumDeclaration enumDecl:
          _enums[enumDecl.Identifier] = enumDecl;
          break;
        case FunctionDeclaration fnDecl:
          _functions[fnDecl.Identifier] = fnDecl;
          break;
      }
    }

    // Pass 2: Check all statements
    foreach (var stmt in program.Statements) {
      CheckStatement(stmt);
    }

    PopScope();
  }

  private void ResolveAndCheckModule(string moduleName) {
    if (_moduleCheckers.ContainsKey(moduleName)) return;

    if (_currentlyCheckingModules.Contains(moduleName)) {
      throw new Exception($"TYPE CHECK ERROR: Circular dependency detected while type checking module '{moduleName}'.");
    }
    _currentlyCheckingModules.Add(moduleName);

    try {
      var filename = moduleName.ToLower() + ".pino";
      var modulesDir = Path.Combine(System.Environment.CurrentDirectory, "modules");
      var filePath = Path.Combine(modulesDir, filename);

      if (!File.Exists(filePath)) {
        throw new Exception($"TYPE CHECK ERROR: Module '{moduleName}' not found. Expected file at '{filePath}'.");
      }

      var program = Parser.ParseFile(filePath);
      var moduleChecker = new TypeChecker();
      moduleChecker.Check(program);

      _moduleCheckers[moduleName] = moduleChecker;
    } finally {
      _currentlyCheckingModules.Remove(moduleName);
    }
  }

  private void PushScope() {
    _scopes.Push(new Dictionary<string, string>());
  }

  private void PopScope() {
    if (_scopes.Count > 0) {
      _scopes.Pop();
    }
  }

  private void DeclareVariable(string name, string type) {
    if (_scopes.Count > 0) {
      _scopes.Peek()[name] = type;
    }
  }

  public string ResolveIdentifierType(string name) {
    foreach (var scope in _scopes) {
      if (scope.TryGetValue(name, out var type)) {
        return type;
      }
    }

    // Check global functions
    if (_functions.TryGetValue(name, out var fnDecl)) {
      return GetFunctionSignatureString(fnDecl);
    }

    // Check built-in functions
    if (BuiltInFunctions.TryGetValue(name, out var builtInSig)) {
      return builtInSig;
    }

    // If it's a known struct, return it as type name
    if (FindStruct(name) != null) {
      return name;
    }

    // If it's a known interface, return it
    if (FindInterface(name) != null) {
      return name;
    }

    return "any";
  }

  public string ResolveFunctionReturnType(string callee) {
    if (_functions.TryGetValue(callee, out var fnDecl)) {
      return InferFunctionReturnType(fnDecl);
    }

    if (BuiltInFunctions.TryGetValue(callee, out var builtInSig)) {
      // Extract return type if present (e.g. "fn(...) string" -> "string")
      int lastSpace = builtInSig.LastIndexOf(' ');
      if (lastSpace != -1) {
        return builtInSig.Substring(lastSpace + 1);
      }
      return "any";
    }

    // Look up identifier type
    string idType = ResolveIdentifierType(callee);
    if (idType.StartsWith("fn(")) {
      // Parse return type from signature string
      int closingParen = idType.LastIndexOf(')');
      if (closingParen != -1 && closingParen < idType.Length - 1) {
        return idType.Substring(closingParen + 1).Trim();
      }
    }

    return "any";
  }

  private string GetFunctionSignatureString(FunctionDeclaration? fn = null, List<VariableDeclaration>? parameters = null, FunctionLambdaExpression? lambda = null) {
    var paramTypes = new List<string>();
    var paramsList = fn != null ? fn.Parameters : (parameters ?? lambda?.Parameters);
    if (paramsList != null) {
      foreach (var p in paramsList) {
        paramTypes.Add(string.IsNullOrEmpty(p.Typing) ? "any" : p.Typing);
      }
    }
    string retType = "any";
    if (fn != null) {
      retType = InferFunctionReturnType(fn);
    } else if (lambda != null) {
      PushScope();
      foreach (var param in lambda.Parameters) {
        DeclareVariable(param.Identifier, string.IsNullOrEmpty(param.Typing) ? "any" : param.Typing);
      }
      var returns = FindReturnStatements(lambda.Body);
      if (returns.Count > 0) {
        retType = returns[0].Argument != null ? InferType(returns[0].Argument!) : "any";
      }
      PopScope();
    }
    return $"fn({string.Join(", ", paramTypes)}) {retType}";
  }

  private void CheckStatement(Statement statement) {
    switch (statement) {
      case VariableDeclaration varDecl:
        if (varDecl.Kind == VariableKind.Constant || varDecl.Kind == VariableKind.Variable) {
          string valType = varDecl.Value != null ? InferType(varDecl.Value) : "any";
          string expectedType = varDecl.Typing;

          if (!string.IsNullOrEmpty(expectedType)) {
            if (!IsCompatible(valType, expectedType)) {
              throw new Exception($"TYPE CHECK ERROR: Cannot assign type '{valType}' to variable '{varDecl.Identifier}' of type '{expectedType}'.");
            }
            DeclareVariable(varDecl.Identifier, expectedType);
          } else {
            DeclareVariable(varDecl.Identifier, valType);
          }

          if (varDecl.Value != null) {
            CheckExpression(varDecl.Value);
          }
        }
        break;

      case FunctionDeclaration fnDecl:
        PushScope();
        if (_currentStruct != null && !_inStaticMethod) {
          DeclareVariable("this", _currentStruct.Identifier);
          DeclareVariable("self", _currentStruct.Identifier);
          ResolveStructMembers(_currentStruct.Identifier, out var fields, out var _);
          foreach (var field in fields) {
            DeclareVariable(field.Identifier, field.Typing);
          }
        }
        foreach (var param in fnDecl.Parameters) {
          DeclareVariable(param.Identifier, param.Typing);
        }
        if (fnDecl.Body != null) {
          CheckStatement(fnDecl.Body);
        }
        PopScope();
        InferFunctionReturnType(fnDecl);
        break;

      case StructDeclaration structDecl:
        var oldStruct = _currentStruct;
        var oldStatic = _inStaticMethod;
        _currentStruct = structDecl;
        foreach (var field in structDecl.Fields) {
          if (field.Value != null) {
            CheckExpression(field.Value);
          }
        }
        foreach (var method in structDecl.Methods) {
          _inStaticMethod = method.IsStatic;
          CheckStatement(method);
        }
        _currentStruct = oldStruct;
        _inStaticMethod = oldStatic;
        break;

      case InterfaceDeclaration:
        break;

      case BlockStatement block:
        PushScope();
        foreach (var s in block.Statements) {
          CheckStatement(s);
        }
        PopScope();
        break;

      case IfStatement ifs:
        CheckExpression(ifs.Condition);
        CheckStatement(ifs.Consequent);
        if (ifs.Alternate != null) {
          CheckStatement(ifs.Alternate);
        }
        break;

      case ElseStatement els:
        CheckStatement(els.Body);
        break;

      case ReturnStatement ret:
        if (ret.Argument != null) {
          CheckExpression(ret.Argument);
        }
        break;

      case LoopStatement loop:
        PushScope();
        if (loop.Kind == LoopKind.ForIn) {
          if (loop.Begin is IdentifierExpression id) {
            string colType = loop.End != null ? InferType(loop.End) : "any";
            string loopVarType = "any";
            string keyVarType = "int";
            if (colType.StartsWith("[]")) {
              loopVarType = colType.Substring(2);
              keyVarType = "int";
            } else if (colType.StartsWith("map[")) {
              int commaIdx = colType.IndexOf(',');
              if (commaIdx != -1) {
                keyVarType = colType.Substring(4, commaIdx - 4).Trim();
                loopVarType = colType.Substring(commaIdx + 1, colType.Length - commaIdx - 2).Trim();
              }
              if (string.IsNullOrEmpty(loop.KeyVar)) {
                loopVarType = keyVarType;
              }
            } else if (colType == "int" || colType == "float") {
              loopVarType = "int";
              keyVarType = "int";
            }
            DeclareVariable(id.Name, loopVarType);
            if (!string.IsNullOrEmpty(loop.KeyVar)) {
              DeclareVariable(loop.KeyVar, keyVarType);
            }
          }
          if (loop.End != null) {
            CheckExpression(loop.End);
          }
        } else if (loop.Kind == LoopKind.ForTimes) {
          if (loop.Begin != null) {
            CheckExpression(loop.Begin);
          }
          DeclareVariable("it", "int");
        }
        CheckStatement(loop.Body);
        PopScope();
        break;

      case MatchStatement match:
        CheckExpression(match.Condition);
        foreach (var branch in match.Branches) {
          foreach (var cond in branch.Conditions) {
            CheckExpression(cond);
          }
          CheckStatement(branch.Body);
        }
        if (match.Alternate != null) {
          CheckStatement(match.Alternate);
        }
        break;

      case ImportStatement imp:
        ResolveAndCheckModule(imp.ModuleName);
        DeclareVariable(imp.ModuleName, "module");
        break;

      case FromImportStatement fromImp:
        ResolveAndCheckModule(fromImp.ModuleName);
        if (_moduleCheckers.TryGetValue(fromImp.ModuleName, out var modChecker)) {
          foreach (var name in fromImp.Imports) {
            string type = modChecker.ResolveIdentifierType(name);
            DeclareVariable(name, type);
          }
        }
        break;

      case Expression expr:
        CheckExpression(expr);
        break;
    }
  }

  private void CheckExpression(Expression expr) {
    switch (expr) {
      case IdentifierExpression id:
        if (_currentStruct != null && _inStaticMethod) {
          if (id.Name == "this" || id.Name == "self") {
            throw new Exception($"TYPE CHECK ERROR: Cannot access '{id.Name}' from static method in struct '{_currentStruct.Identifier}'.");
          }
          bool isLocal = false;
          foreach (var scope in _scopes) {
            if (scope.ContainsKey(id.Name)) {
              isLocal = true;
              break;
            }
          }
          if (!isLocal) {
            ResolveStructMembers(_currentStruct.Identifier, out var fields, out var _);
            if (fields.Any(f => f.Identifier == id.Name)) {
              throw new Exception($"TYPE CHECK ERROR: Cannot access instance field '{id.Name}' from static method in struct '{_currentStruct.Identifier}'.");
            }
          }
        }
        break;

      case BinaryExpression bin: {
          CheckExpression(bin.Left);
          CheckExpression(bin.Right);

          if (bin.Operator == OperatorType.Assignment) {
            string leftType = InferType(bin.Left);
            string rightType = InferType(bin.Right);
            if (!IsCompatible(rightType, leftType)) {
              throw new Exception($"TYPE CHECK ERROR: Cannot assign type '{rightType}' to target of type '{leftType}'.");
            }
          } else if (bin.Operator == OperatorType.MemberAccess) {
            string leftType = InferType(bin.Left);
            var accessStructDecl = FindStruct(leftType);
            if (accessStructDecl != null) {
              ResolveStructMembers(leftType, out var _, out var allMethods);
              if (bin.Right is FunctionCallExpression methodCall) {
                var method = allMethods.Find(m => m.Identifier == methodCall.Callee);
                if (method != null) {
                  if (method.IsStatic) {
                    throw new Exception($"TYPE CHECK ERROR: Cannot call static method '{method.Identifier}' of struct '{accessStructDecl.Identifier}' on an instance.");
                  }
                  // Validate arguments
                  var memberCallArgTypes = methodCall.Arguments.Select(InferType).ToList();
                  if (method.Parameters.Count != memberCallArgTypes.Count) {
                    throw new Exception($"TYPE CHECK ERROR: Method '{method.Identifier}' expected {method.Parameters.Count} arguments, but got {memberCallArgTypes.Count}.");
                  }
                  for (int i = 0; i < method.Parameters.Count; i++) {
                    if (!IsCompatible(memberCallArgTypes[i], method.Parameters[i].Typing)) {
                      throw new Exception($"TYPE CHECK ERROR: Argument {i + 1} for method '{method.Identifier}' expected type '{method.Parameters[i].Typing}', but got '{memberCallArgTypes[i]}'.");
                    }
                  }
                } else {
                  throw new Exception($"TYPE CHECK ERROR: Struct '{accessStructDecl.Identifier}' does not have method '{methodCall.Callee}'.");
                }
              } else if (bin.Right is IdentifierExpression propId) {
                var method = allMethods.Find(m => m.Identifier == propId.Name);
                if (method != null && method.IsStatic) {
                  throw new Exception($"TYPE CHECK ERROR: Cannot access static method '{method.Identifier}' as instance member.");
                }
              }
            }
          } else if (bin.Operator == OperatorType.StaticMemberAccess) {
            if (bin.Left is IdentifierExpression structId) {
              string structName = structId.Name;
              var staticAccessStructDecl = FindStruct(structName);
              if (staticAccessStructDecl != null) {
                ResolveStructMembers(structName, out var _, out var allMethods);
                if (bin.Right is FunctionCallExpression methodCall) {
                  var method = allMethods.Find(m => m.Identifier == methodCall.Callee);
                  if (method == null) {
                    throw new Exception($"TYPE CHECK ERROR: Struct '{structName}' has no static method '{methodCall.Callee}'.");
                  }
                  if (!method.IsStatic) {
                    throw new Exception($"TYPE CHECK ERROR: Method '{method.Identifier}' of struct '{structName}' is not static.");
                  }
                  // Validate arguments
                  var staticCallArgTypes = methodCall.Arguments.Select(InferType).ToList();
                  if (method.Parameters.Count != staticCallArgTypes.Count) {
                    throw new Exception($"TYPE CHECK ERROR: Static method '{method.Identifier}' expected {method.Parameters.Count} arguments, but got {staticCallArgTypes.Count}.");
                  }
                  for (int i = 0; i < method.Parameters.Count; i++) {
                    if (!IsCompatible(staticCallArgTypes[i], method.Parameters[i].Typing)) {
                      throw new Exception($"TYPE CHECK ERROR: Argument {i + 1} for static method '{method.Identifier}' expected type '{method.Parameters[i].Typing}', but got '{staticCallArgTypes[i]}'.");
                    }
                  }
                } else if (bin.Right is IdentifierExpression methodId) {
                  var method = allMethods.Find(m => m.Identifier == methodId.Name);
                  if (method == null) {
                    throw new Exception($"TYPE CHECK ERROR: Struct '{structName}' has no static method '{methodId.Name}'.");
                  }
                  if (!method.IsStatic) {
                    throw new Exception($"TYPE CHECK ERROR: Method '{method.Identifier}' of struct '{structName}' is not static.");
                  }
                } else {
                  throw new Exception($"TYPE CHECK ERROR: Invalid static member access on struct '{structName}'.");
                }
              }
            }
          }
          break;
        }

      case TernaryExpression tern:
        CheckExpression(tern.Condition);
        CheckExpression(tern.Consequent);
        CheckExpression(tern.Alternate);
        break;

      case VectorExpression vec:
        if (vec.Elements != null) {
          foreach (var el in vec.Elements) {
            CheckExpression(el);
          }
        }
        if (vec.Len != null) CheckExpression(vec.Len);
        if (vec.Init != null) CheckExpression(vec.Init);
        break;

      case StructInstanceExpression inst:
        var structDecl = FindStruct(inst.StructName);
        if (structDecl == null) {
          throw new Exception($"TYPE CHECK ERROR: Struct '{inst.StructName}' is not defined.");
        } else {
          ResolveStructMembers(inst.StructName, out var allFields, out var _);
          foreach (var prop in inst.Properties) {
            var field = allFields.Find(f => f.Identifier == prop.Identifier);
            if (field == null) {
              throw new Exception($"TYPE CHECK ERROR: Struct '{inst.StructName}' does not have field '{prop.Identifier}'.");
            }
            if (prop.Value != null) {
              CheckExpression(prop.Value);
              string propType = InferType(prop.Value);
              if (!IsCompatible(propType, field.Typing)) {
                throw new Exception($"TYPE CHECK ERROR: Cannot assign type '{propType}' to field '{prop.Identifier}' of type '{field.Typing}' in struct '{inst.StructName}'.");
              }
            }
          }
        }
        break;

      case FunctionCallExpression call:
        var argTypes = call.Arguments.Select(InferType).ToList();
        foreach (var arg in call.Arguments) {
          CheckExpression(arg);
        }

        if (_functions.TryGetValue(call.Callee, out var fnDecl)) {
          if (fnDecl.Parameters.Count != argTypes.Count) {
            throw new Exception($"TYPE CHECK ERROR: Function '{call.Callee}' expected {fnDecl.Parameters.Count} arguments, but got {argTypes.Count}.");
          }
          for (int i = 0; i < fnDecl.Parameters.Count; i++) {
            if (!IsCompatible(argTypes[i], fnDecl.Parameters[i].Typing)) {
              throw new Exception($"TYPE CHECK ERROR: Argument {i + 1} for function '{call.Callee}' expected type '{fnDecl.Parameters[i].Typing}', but got '{argTypes[i]}'.");
            }
          }
        }
        break;

      case FunctionLambdaExpression lambda:
        PushScope();
        foreach (var param in lambda.Parameters) {
          DeclareVariable(param.Identifier, param.Typing);
        }
        CheckStatement(lambda.Body);
        PopScope();
        break;

      case IndexAccessExpression idx:
        CheckExpression(idx.Target);
        CheckExpression(idx.Index);
        break;

      case MapExpression map:
        foreach (var entry in map.Entries) {
          CheckExpression(entry.Key);
          CheckExpression(entry.Value);
          string kType = InferType(entry.Key);
          string vType = InferType(entry.Value);
          if (!IsCompatible(kType, map.KeyType)) {
            throw new Exception($"TYPE CHECK ERROR: Map key expected type '{map.KeyType}', but got '{kType}'.");
          }
          if (!IsCompatible(vType, map.ValueType)) {
            throw new Exception($"TYPE CHECK ERROR: Map value expected type '{map.ValueType}', but got '{vType}'.");
          }
        }
        break;
    }
  }

  private string InferType(Expression expr) {
    switch (expr) {
      case LiteralExpression lit:
        return lit.LiteralType switch {
          LiteralType.Boolean => "bool",
          LiteralType.Integer => "int",
          LiteralType.Float => "float",
          LiteralType.String => "string",
          _ => "any"
        };

      case IdentifierExpression id:
        return ResolveIdentifierType(id.Name);

      case VectorExpression vec:
        if (vec.Elements != null && vec.Elements.Count > 0) {
          string firstType = InferType(vec.Elements[0]);
          return "[]" + firstType;
        }
        if (!string.IsNullOrEmpty(vec.Typing)) {
          return "[]" + vec.Typing;
        }
        return "[]any";

      case StructInstanceExpression inst:
        return inst.StructName;

      case MapExpression map:
        return $"map[{map.KeyType}, {map.ValueType}]";

      case FunctionCallExpression call:
        return ResolveFunctionReturnType(call.Callee);

      case BinaryExpression bin:
        if (bin.Operator == OperatorType.MemberAccess) {
          string leftType = InferType(bin.Left);
          var structDecl = FindStruct(leftType);
          if (structDecl != null) {
            ResolveStructMembers(leftType, out var allFields, out var allMethods);
            if (bin.Right is IdentifierExpression propId) {
              var field = allFields.Find(f => f.Identifier == propId.Name);
              if (field != null) return field.Typing;
              var method = allMethods.Find(m => m.Identifier == propId.Name && !m.IsStatic);
              if (method != null) return GetFunctionSignatureString(method);
            } else if (bin.Right is FunctionCallExpression methodCall) {
              var method = allMethods.Find(m => m.Identifier == methodCall.Callee && !m.IsStatic);
              if (method != null) return InferFunctionReturnType(method);
            }
          }
          if (leftType.StartsWith("[]")) {
            string elemType = leftType.Substring(2);
            if (bin.Right is IdentifierExpression arrayId && (arrayId.Name == "len" || arrayId.Name == "length")) {
              return "int";
            }
            if (bin.Right is FunctionCallExpression methodCall) {
              string callee = methodCall.Callee;
              if (callee == "map") {
                if (methodCall.Arguments.Count > 0) {
                  string callbackType = InferType(methodCall.Arguments[0]);
                  if (callbackType.StartsWith("fn(")) {
                    int lastClose = callbackType.LastIndexOf(')');
                    if (lastClose != -1 && lastClose < callbackType.Length - 1) {
                      string retType = callbackType.Substring(lastClose + 1).Trim();
                      return "[]" + retType;
                    }
                  }
                }
                return "[]any";
              }
              if (callee == "filter" || callee == "push" || callee == "add") {
                return leftType;
              }
              if (callee == "pop" || callee == "find") {
                return elemType;
              }
              if (callee == "find_index") {
                return "int";
              }
              if (callee == "any" || callee == "all") {
                return "bool";
              }
              if (callee == "each") {
                return "any";
              }
            }
          }
          if (leftType.StartsWith("map[")) {
            if (bin.Right is IdentifierExpression mapId && (mapId.Name == "len" || mapId.Name == "length")) {
              return "int";
            }
            if (bin.Right is FunctionCallExpression methodCall) {
              string callee = methodCall.Callee;
              int commaIdx = leftType.IndexOf(',');
              if (commaIdx != -1) {
                string keyType = leftType.Substring(4, commaIdx - 4).Trim();
                string valType = leftType.Substring(commaIdx + 1, leftType.Length - commaIdx - 2).Trim();
                if (callee == "keys") {
                  return "[]" + keyType;
                }
                if (callee == "values") {
                  return "[]" + valType;
                }
                if (callee == "remove") {
                  return valType;
                }
              }
            }
          }
          if (leftType == "string") {
            if (bin.Right is IdentifierExpression strId && (strId.Name == "len" || strId.Name == "length")) {
              return "int";
            }
            if (bin.Right is FunctionCallExpression methodCall) {
              string callee = methodCall.Callee;
              if (callee == "lower" || callee == "upper" || callee == "trim" || callee == "replace") {
                return "string";
              }
              if (callee == "contains") {
                return "bool";
              }
              if (callee == "split") {
                return "[]string";
              }
            }
          }
          return "any";
        }

        if (bin.Operator == OperatorType.StaticMemberAccess) {
          if (bin.Left is IdentifierExpression modId) {
            string modName = modId.Name;
            if (FindEnum(modName) != null) {
              return modName;
            }
            var structDecl = FindStruct(modName);
            if (structDecl != null) {
              ResolveStructMembers(modName, out var _, out var allMethods);
              if (bin.Right is IdentifierExpression methodId) {
                var method = allMethods.Find(m => m.Identifier == methodId.Name && m.IsStatic);
                if (method != null) return GetFunctionSignatureString(method);
              } else if (bin.Right is FunctionCallExpression methodCall) {
                var method = allMethods.Find(m => m.Identifier == methodCall.Callee && m.IsStatic);
                if (method != null) return InferFunctionReturnType(method);
              }
              return "any";
            }
            if (_moduleCheckers.TryGetValue(modName, out var modChecker)) {
              if (bin.Right is StructInstanceExpression modInst) {
                return modInst.StructName;
              }
              if (bin.Right is FunctionCallExpression modCall) {
                return modChecker.ResolveFunctionReturnType(modCall.Callee);
              }
              if (bin.Right is IdentifierExpression modMember) {
                return modChecker.ResolveIdentifierType(modMember.Name);
              }
            }
          }
          return "any";
        }

        if (bin.Operator == OperatorType.Addition) {
          if (InferType(bin.Left) == "string" || InferType(bin.Right) == "string") {
            return "string";
          }
        }

        if (bin.Operator == OperatorType.Equal || bin.Operator == OperatorType.NotEqual ||
            bin.Operator == OperatorType.LessThan || bin.Operator == OperatorType.LessThanEqual ||
            bin.Operator == OperatorType.GreaterThan || bin.Operator == OperatorType.GreaterThanEqual ||
            bin.Operator == OperatorType.In) {
          return "bool";
        }

        return InferType(bin.Left);

      case TernaryExpression tern:
        return InferType(tern.Consequent);

      case FunctionLambdaExpression lambda:
        return GetFunctionSignatureString(null, lambda.Parameters, lambda);

      case IndexAccessExpression idx:
        string targetType = InferType(idx.Target);
        if (targetType.StartsWith("[]")) {
          return targetType.Substring(2);
        }
        if (targetType.StartsWith("map[")) {
          int commaIdx = targetType.IndexOf(',');
          if (commaIdx != -1) {
            return targetType.Substring(commaIdx + 1, targetType.Length - commaIdx - 2).Trim();
          }
        }
        if (targetType == "string") {
          return "string";
        }
        return "any";

      default:
        return "any";
    }
  }

  private static bool ParseFunctionSignature(string signature, out List<string> paramsList, out string returnType) {
    paramsList = new List<string>();
    returnType = "any";
    if (string.IsNullOrEmpty(signature) || !signature.StartsWith("fn(")) return false;

    int depth = 1;
    int closingParenIdx = -1;
    for (int i = 3; i < signature.Length; i++) {
      if (signature[i] == '(') depth++;
      else if (signature[i] == ')') {
        depth--;
        if (depth == 0) {
          closingParenIdx = i;
          break;
        }
      }
    }

    if (closingParenIdx == -1) return false;

    string paramsStr = signature.Substring(3, closingParenIdx - 3).Trim();
    returnType = signature.Substring(closingParenIdx + 1).Trim();
    if (string.IsNullOrEmpty(returnType)) {
      returnType = "any";
    }

    if (paramsStr == "...") {
      paramsList = null!;
    } else if (!string.IsNullOrEmpty(paramsStr)) {
      int nestedDepth = 0;
      int start = 0;
      for (int i = 0; i < paramsStr.Length; i++) {
        char c = paramsStr[i];
        if (c == '(' || c == '[') nestedDepth++;
        else if (c == ')' || c == ']') nestedDepth--;
        else if (c == ',' && nestedDepth == 0) {
          paramsList.Add(paramsStr.Substring(start, i - start).Trim());
          start = i + 1;
        }
      }
      paramsList.Add(paramsStr.Substring(start).Trim());
    }

    return true;
  }

  private bool IsCompatible(string srcType, string destType) {
    if (destType == "any" || srcType == "any" || string.IsNullOrEmpty(destType)) {
      return true;
    }

    if (srcType == destType) {
      return true;
    }

    if ((srcType == "int" || srcType == "float") && (destType == "int" || destType == "float")) {
      return true;
    }

    if (srcType.StartsWith("fn(") && destType.StartsWith("fn(")) {
      if (!ParseFunctionSignature(srcType, out var srcParams, out var srcRet) ||
          !ParseFunctionSignature(destType, out var destParams, out var destRet)) {
        return false;
      }

      if (!IsCompatible(srcRet, destRet)) {
        return false;
      }

      if (destParams == null) {
        return true;
      }
      if (srcParams == null) {
        return false;
      }
      if (srcParams.Count != destParams.Count) {
        return false;
      }
      for (int i = 0; i < srcParams.Count; i++) {
        if (!IsCompatible(destParams[i], srcParams[i])) {
          return false;
        }
      }
      return true;
    }

    var interfaceDecl = FindInterface(destType);
    if (interfaceDecl != null) {
      var structDecl = FindStruct(srcType);
      if (structDecl != null) {
        return ImplementsInterface(structDecl, interfaceDecl);
      }
      return false;
    }

    return false;
  }

  private bool ImplementsInterface(StructDeclaration structDecl, InterfaceDeclaration interfaceDecl) {
    ResolveStructMembers(structDecl.Identifier, out var _, out var allMethods);
    var instanceMethods = allMethods.Where(m => !m.IsStatic).ToList();
    foreach (var reqMethod in interfaceDecl.Methods) {
      var implMethod = instanceMethods.Find(m => m.Identifier == reqMethod.Identifier);
      if (implMethod == null) {
        return false;
      }

      if (implMethod.Parameters.Count != reqMethod.Parameters.Count) {
        return false;
      }

      for (int i = 0; i < reqMethod.Parameters.Count; i++) {
        var reqParamType = reqMethod.Parameters[i].Typing;
        var implParamType = implMethod.Parameters[i].Typing;
        if (!IsCompatible(implParamType, reqParamType)) {
          return false;
        }
      }

      string reqRetType = InferFunctionReturnType(reqMethod);
      string implRetType = InferFunctionReturnType(implMethod);
      if (!IsCompatible(implRetType, reqRetType)) {
        return false;
      }
    }

    return true;
  }

  private void ResolveStructMembers(string structName, out List<VariableDeclaration> allFields, out List<FunctionDeclaration> allMethods) {
    allFields = new List<VariableDeclaration>();
    allMethods = new List<FunctionDeclaration>();

    var structDecl = FindStruct(structName);
    if (structDecl == null) return;

    foreach (var parentName in structDecl.InheritedStructs) {
      ResolveStructMembers(parentName, out var parentFields, out var parentMethods);
      allFields.AddRange(parentFields);
      allMethods.AddRange(parentMethods);
    }

    foreach (var field in structDecl.Fields) {
      allFields.RemoveAll(f => f.Identifier == field.Identifier);
      allFields.Add(field);
    }
    foreach (var method in structDecl.Methods) {
      allMethods.RemoveAll(m => m.Identifier == method.Identifier);
      allMethods.Add(method);
    }
  }

  private string InferFunctionReturnType(FunctionDeclaration fn) {
    string fnKey = fn.Identifier ?? "<lambda>";

    // Cycle guard must come first — covers BOTH explicit and inferred return type paths.
    // If we are already inferring this function, it is recursive.
    if (_inferringFunctions.Contains(fnKey)) {
      // Explicit return type is already known — return it directly to break the cycle.
      if (!string.IsNullOrEmpty(fn.ReturnType)) {
        return fn.ReturnType;
      }
      // No annotation — we cannot infer the type of a recursive call.
      throw new Exception(
        $"TYPE CHECK ERROR: Function '{fn.Identifier}' is recursive and requires an explicit return type annotation.\n" +
        $"  Hint: fn {fn.Identifier}({string.Join(" ", fn.Parameters.Select(p => p.Identifier + " " + p.Typing))}) <return_type> {{ ... }}"
      );
    }

    _inferringFunctions.Add(fnKey);
    try {
      if (!string.IsNullOrEmpty(fn.ReturnType)) {
        if (fn.Body == null) {
          return fn.ReturnType;
        }

        PushScope();
        foreach (var param in fn.Parameters) {
          DeclareVariable(param.Identifier, string.IsNullOrEmpty(param.Typing) ? "any" : param.Typing);
        }

        var returns = FindReturnStatements(fn.Body);
        foreach (var ret in returns) {
          string retType = ret.Argument != null ? InferType(ret.Argument) : "any";
          if (!IsCompatible(retType, fn.ReturnType)) {
            throw new Exception($"TYPE CHECK ERROR: Function '{fn.Identifier}' declared return type '{fn.ReturnType}', but returned '{retType}'.");
          }
        }

        PopScope();
        return fn.ReturnType;
      }

      if (fn.Body == null) {
        return "any";
      }

      PushScope();
      foreach (var param in fn.Parameters) {
        DeclareVariable(param.Identifier, string.IsNullOrEmpty(param.Typing) ? "any" : param.Typing);
      }

      var returns2 = FindReturnStatements(fn.Body);
      if (returns2.Count == 0) {
        PopScope();
        return "any";
      }

      string firstRetType = "any";
      bool first = true;
      foreach (var ret in returns2) {
        string retType = ret.Argument != null ? InferType(ret.Argument) : "any";
        if (first) {
          firstRetType = retType;
          first = false;
        } else {
          if (!IsCompatible(retType, firstRetType) && !IsCompatible(firstRetType, retType)) {
            throw new Exception($"TYPE CHECK ERROR: Function '{fn.Identifier}' has conflicting return types '{firstRetType}' and '{retType}'.");
          }
        }
      }

      PopScope();
      return firstRetType;
    } finally {
      _inferringFunctions.Remove(fnKey);
    }
  }

  private List<ReturnStatement> FindReturnStatements(Statement stmt) {
    var list = new List<ReturnStatement>();
    FindReturnStatementsRecursive(stmt, list);
    return list;
  }

  private void FindReturnStatementsRecursive(Statement stmt, List<ReturnStatement> list) {
    if (stmt == null) return;

    if (stmt is ReturnStatement ret) {
      list.Add(ret);
      return;
    }

    if (stmt is BlockStatement block) {
      foreach (var s in block.Statements) {
        FindReturnStatementsRecursive(s, list);
      }
    } else if (stmt is IfStatement ifs) {
      FindReturnStatementsRecursive(ifs.Consequent, list);
      if (ifs.Alternate != null) {
        FindReturnStatementsRecursive(ifs.Alternate, list);
      }
    } else if (stmt is ElseStatement els) {
      FindReturnStatementsRecursive(els.Body, list);
    } else if (stmt is LoopStatement loop) {
      FindReturnStatementsRecursive(loop.Body, list);
    } else if (stmt is MatchStatement match) {
      foreach (var branch in match.Branches) {
        FindReturnStatementsRecursive(branch.Body, list);
      }
      if (match.Alternate != null) {
        FindReturnStatementsRecursive(match.Alternate, list);
      }
    } else if (stmt is WhenStatement when) {
      FindReturnStatementsRecursive(when.Body, list);
    }
  }
}
