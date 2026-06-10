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
  
  // Cache of checked modules to prevent double-checking
  private readonly Dictionary<string, TypeChecker> _moduleCheckers = new();
  private readonly HashSet<string> _currentlyCheckingModules = new();
  
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
      var returns = FindReturnStatements(lambda.Body);
      if (returns.Count > 0) {
        retType = returns[0].Argument != null ? InferType(returns[0].Argument!) : "any";
      }
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
        foreach (var param in fnDecl.Parameters) {
          DeclareVariable(param.Identifier, param.Typing);
        }
        if (fnDecl.Body != null) {
          CheckStatement(fnDecl.Body);
        }
        PopScope();
        break;
        
      case StructDeclaration structDecl:
        foreach (var field in structDecl.Fields) {
          if (field.Value != null) {
            CheckExpression(field.Value);
          }
        }
        foreach (var method in structDecl.Methods) {
          CheckStatement(method);
        }
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
            if (colType.StartsWith("[]")) {
              loopVarType = colType.Substring(2);
            } else if (colType == "int" || colType == "float") {
              loopVarType = "int";
            }
            DeclareVariable(id.Name, loopVarType);
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
      case BinaryExpression bin:
        CheckExpression(bin.Left);
        CheckExpression(bin.Right);
        
        if (bin.Operator == OperatorType.Assignment) {
          string leftType = InferType(bin.Left);
          string rightType = InferType(bin.Right);
          if (!IsCompatible(rightType, leftType)) {
            throw new Exception($"TYPE CHECK ERROR: Cannot assign type '{rightType}' to target of type '{leftType}'.");
          }
        }
        break;
        
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
          foreach (var prop in inst.Properties) {
            var field = structDecl.Fields.Find(f => f.Identifier == prop.Identifier);
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
              throw new Exception($"TYPE CHECK ERROR: Argument {i+1} for function '{call.Callee}' expected type '{fnDecl.Parameters[i].Typing}', but got '{argTypes[i]}'.");
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
            if (bin.Right is IdentifierExpression propId) {
              var field = structDecl.Fields.Find(f => f.Identifier == propId.Name);
              if (field != null) return field.Typing;
              var method = structDecl.Methods.Find(m => m.Identifier == propId.Name);
              if (method != null) return GetFunctionSignatureString(method);
            } else if (bin.Right is FunctionCallExpression methodCall) {
              var method = structDecl.Methods.Find(m => m.Identifier == methodCall.Callee);
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
    foreach (var reqMethod in interfaceDecl.Methods) {
      var implMethod = structDecl.Methods.Find(m => m.Identifier == reqMethod.Identifier);
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

  private string InferFunctionReturnType(FunctionDeclaration fn) {
    if (fn.Body == null) {
      return "any";
    }
    
    var returns = FindReturnStatements(fn.Body);
    if (returns.Count == 0) {
      return "any";
    }
    
    string firstRetType = "any";
    bool first = true;
    foreach (var ret in returns) {
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
    
    return firstRetType;
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
