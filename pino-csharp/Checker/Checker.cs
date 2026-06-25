using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pino;

public partial class Checker {
  private readonly Dictionary<string, StructDeclaration> _structs = new();
  private readonly Dictionary<string, InterfaceDeclaration> _interfaces = new();
  private readonly Dictionary<string, EnumDeclaration> _enums = new();
  private readonly Dictionary<string, FunctionDeclaration> _functions = new();
  private readonly List<StructDeclaration> _specializedStructs = new();

  public bool IsModule { get; set; } = false;

  // Environment/scopes for variable checking
  private readonly Stack<Dictionary<string, string>> _scopes = new();

  // Context for current struct and method being checked
  private StructDeclaration? _currentStruct = null;
  private bool _inStaticMethod = false;

  // Cache of checked modules to prevent double-checking
  private readonly Dictionary<string, Checker> _moduleCheckers = new();
  private readonly HashSet<string> _currentlyCheckingModules = new();

  // Guard against infinite recursion during return type inference of recursive functions
  private readonly HashSet<string> _inferringFunctions = new();

  // Standard library definitions
  private static readonly Dictionary<string, string> BuiltInFunctions = new() {
    { "println", "fn(...)" },
    { "readline", "fn(...) string" },
    { "int", "fn(...) int" },
    { "rune", "fn(any) rune" },
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
    _specializedStructs.Clear();

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

    // Strict Program Mode validation when main is defined
    bool hasMainFunc = _functions.ContainsKey("main");
    if (hasMainFunc) {
      if (IsModule) {
        throw new Exception("TYPE CHECK ERROR: An imported module cannot define a 'main' function. Only the main execution entry file can define 'main()'.");
      }
      foreach (var stmt in program.Statements) {
        if (stmt is StructDeclaration ||
            stmt is InterfaceDeclaration ||
            stmt is EnumDeclaration ||
            stmt is FunctionDeclaration ||
            stmt is ImportStatement ||
            stmt is FromImportStatement ||
            stmt is ModuleDeclaration) {
          continue;
        }

        if (stmt is VariableDeclaration varDecl) {
          if (varDecl.Kind != VariableKind.Constant) {
            throw new Exception($"TYPE CHECK ERROR: Global variable '{varDecl.Identifier}' must be declared with 'val' (constants only). Global mutable variables 'var' are forbidden when a 'main' function is defined.");
          }
          continue;
        }

        // Forbid any other statements/expressions at top level
        throw new Exception($"TYPE CHECK ERROR: Statements with side-effects (loops, conditionals, assignments, loose expression calls) are not allowed at the top level when a 'main' function is defined. All execution must start inside 'main()'. Forbidden statement type: '{stmt.GetType().Name}'.");
      }
    }

    // Pass 2: Check all statements
    foreach (var stmt in program.Statements) {
      CheckStatement(stmt);
    }

    program.Statements.InsertRange(0, _specializedStructs);

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
      var moduleChecker = new Checker { IsModule = true };
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

  private void Resolve(Expression expr, string name) {
    int distance = 0;
    foreach (var scope in _scopes) {
      if (scope.ContainsKey(name)) {
        expr.Distance = distance;
        return;
      }
      distance++;
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

  private string GetFunctionSignatureString(FunctionDeclaration? fn = null, List<VariableDeclaration>? parameters = null, FunctionLambdaExpression? lambda = null, string? parentStructName = null) {
    var paramTypes = new List<string>();
    var paramsList = fn != null ? fn.Parameters : (parameters ?? lambda?.Parameters);
    if (paramsList != null) {
      foreach (var p in paramsList) {
        paramTypes.Add(string.IsNullOrEmpty(p.Typing) ? "any" : p.Typing);
      }
    }
    string retType = "any";
    if (fn != null) {
      retType = InferFunctionReturnType(fn, parentStructName);
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

  // --- GENERIC MONOMORPHIZATION HELPERS ---

  public string NormalizeType(string typing) {
    return SubstituteType(typing, new Dictionary<string, string>());
  }

  private string SubstituteType(string typing, Dictionary<string, string> subst) {
    if (string.IsNullOrEmpty(typing)) return typing;
    if (subst.TryGetValue(typing, out var concrete)) {
      return concrete;
    }
    if (typing.StartsWith("[]")) {
      return "[]" + SubstituteType(typing.Substring(2), subst);
    }
    if (typing.StartsWith("map[")) {
      var parts = SplitMapTypes(typing);
      if (parts != null) {
        return $"map[{SubstituteType(parts.Item1, subst)}, {SubstituteType(parts.Item2, subst)}]";
      }
    }
    if (typing.StartsWith("fn(")) {
      var sig = ParseFnSignature(typing);
      if (sig != null) {
        var newParams = new List<string>();
        foreach (var p in sig.Params) {
          newParams.Add(SubstituteType(p, subst));
        }
        return $"fn({string.Join(", ", newParams)}) {SubstituteType(sig.ReturnType, subst)}";
      }
    }
    
    int bracketIdx = typing.IndexOf('[');
    if (bracketIdx != -1 && typing.EndsWith("]")) {
      string baseName = typing.Substring(0, bracketIdx);
      string argsStr = typing.Substring(bracketIdx + 1, typing.Length - bracketIdx - 2);
      // Split argsStr by comma respecting nesting
      var subArgs = new List<string>();
      int start = 0;
      int depth = 0;
      for (int i = 0; i < argsStr.Length; i++) {
        if (argsStr[i] == '[') depth++;
        else if (argsStr[i] == ']') depth--;
        else if (argsStr[i] == ',' && depth == 0) {
          subArgs.Add(argsStr.Substring(start, i - start).Trim());
          start = i + 1;
        }
      }
      subArgs.Add(argsStr.Substring(start).Trim());
      
      var substitutedArgs = new List<string>();
      foreach (var arg in subArgs) {
        substitutedArgs.Add(SubstituteType(arg, subst));
      }
      
      var baseStruct = FindStruct(baseName);
      if (baseStruct != null && baseStruct.GenericParams != null && baseStruct.GenericParams.Count > 0) {
        return MonomorphizeStruct(baseName, substitutedArgs);
      } else {
        return baseName + "[" + string.Join(", ", substitutedArgs) + "]";
      }
    }
    
    return typing;
  }

  private Tuple<string, string>? SplitMapTypes(string mapType) {
    if (!mapType.StartsWith("map[")) return null;
    int len = mapType.Length;
    int depth = 0;
    int commaIdx = -1;
    for (int i = 4; i < len - 1; i++) {
      if (mapType[i] == '[') depth++;
      else if (mapType[i] == ']') depth--;
      else if (mapType[i] == ',' && depth == 0) {
        commaIdx = i;
        break;
      }
    }
    if (commaIdx == -1) return null;
    string key = mapType.Substring(4, commaIdx - 4).Trim();
    string val = mapType.Substring(commaIdx + 1, len - 1 - (commaIdx + 1)).Trim();
    return Tuple.Create(key, val);
  }

  private class FnSig {
    public List<string> Params = new();
    public string ReturnType = "any";
  }

  private FnSig? ParseFnSignature(string fnType) {
    if (!fnType.StartsWith("fn(")) return null;
    int closingParen = -1;
    int depth = 0;
    for (int i = 2; i < fnType.Length; i++) {
      if (fnType[i] == '(') depth++;
      else if (fnType[i] == ')') {
        depth--;
        if (depth == 0) {
          closingParen = i;
          break;
        }
      }
    }
    if (closingParen == -1) return null;
    string paramsStr = fnType.Substring(3, closingParen - 3).Trim();
    string retType = fnType.Substring(closingParen + 1).Trim();

    var sig = new FnSig {
      ReturnType = string.IsNullOrEmpty(retType) ? "any" : retType
    };

    if (!string.IsNullOrEmpty(paramsStr)) {
      int start = 0;
      int pDepth = 0;
      for (int i = 0; i < paramsStr.Length; i++) {
        if (paramsStr[i] == '(' || paramsStr[i] == '[') pDepth++;
        else if (paramsStr[i] == ')' || paramsStr[i] == ']') pDepth--;
        else if (paramsStr[i] == ',' && pDepth == 0) {
          sig.Params.Add(paramsStr.Substring(start, i - start).Trim());
          start = i + 1;
        }
      }
      sig.Params.Add(paramsStr.Substring(start).Trim());
    }
    return sig;
  }

  private string MonomorphizeStruct(string baseName, List<string> concreteArgs) {
    var baseStruct = FindStruct(baseName);
    if (baseStruct == null) {
      throw new Exception($"TYPE CHECK ERROR: Struct '{baseName}' is not defined.");
    }
    if (baseStruct.GenericParams == null || baseStruct.GenericParams.Count == 0) {
      throw new Exception($"TYPE CHECK ERROR: Struct '{baseName}' is not a generic struct.");
    }
    if (baseStruct.GenericParams.Count != concreteArgs.Count) {
      throw new Exception($"TYPE CHECK ERROR: Struct '{baseName}' expects {baseStruct.GenericParams.Count} generic parameters, but got {concreteArgs.Count} arguments.");
    }

    var cleanArgs = new List<string>();
    foreach (var arg in concreteArgs) {
      string clean = arg
        .Replace("[]", "vector_")
        .Replace("[", "_")
        .Replace("]", "_")
        .Replace(",", "_")
        .Replace(" ", "_")
        .Trim('_');
      while (clean.Contains("__")) {
        clean = clean.Replace("__", "_");
      }
      cleanArgs.Add(clean);
    }
    string specializedName = $"{baseName}_{string.Join("_", cleanArgs)}";

    if (_structs.ContainsKey(specializedName)) {
      return specializedName;
    }

    var subst = new Dictionary<string, string>();
    for (int i = 0; i < baseStruct.GenericParams.Count; i++) {
      subst[baseStruct.GenericParams[i]] = concreteArgs[i];
    }

    var specializedFields = new List<VariableDeclaration>();
    foreach (var field in baseStruct.Fields) {
      string substitutedTyping = SubstituteType(field.Typing, subst);
      specializedFields.Add(field with { Typing = substitutedTyping });
    }

    var specializedMethods = new List<FunctionDeclaration>();
    foreach (var method in baseStruct.Methods) {
      var specializedParams = new List<VariableDeclaration>();
      foreach (var param in method.Parameters) {
        specializedParams.Add(param with { Typing = SubstituteType(param.Typing, subst) });
      }
      string substitutedReturn = SubstituteType(method.ReturnType, subst);
      var specializedBody = SubstituteStatementTypes(method.Body, subst);

      specializedMethods.Add(method with {
        Parameters = specializedParams,
        ReturnType = substitutedReturn,
        Body = specializedBody
      });
    }

    var specializedStruct = new StructDeclaration(
      specializedName,
      specializedFields,
      specializedMethods,
      baseStruct.InheritedStructs,
      GenericParams: null,
      IsPublic: baseStruct.IsPublic
    );

    _structs[specializedName] = specializedStruct;
    _specializedStructs.Add(specializedStruct);

    CheckStatement(specializedStruct);

    return specializedName;
  }

  private Statement? SubstituteStatementTypes(Statement? statement, Dictionary<string, string> subst) {
    if (statement == null) return null;
    switch (statement) {
      case BlockStatement block:
        var substitutedStats = new List<Statement>();
        foreach (var s in block.Statements) {
          var sub = SubstituteStatementTypes(s, subst);
          if (sub != null) substitutedStats.Add(sub);
        }
        return block with { Statements = substitutedStats };

      case ReturnStatement ret:
        return ret with { Argument = SubstituteExpressionTypes(ret.Argument, subst) };

      case LoopStatement loop:
        return loop with {
          Begin = SubstituteExpressionTypes(loop.Begin, subst),
          End = SubstituteExpressionTypes(loop.End, subst),
          Body = SubstituteStatementTypes(loop.Body, subst)!
        };

      case IfStatement ifs:
        return ifs with {
          Condition = SubstituteExpressionTypes(ifs.Condition, subst)!,
          Consequent = SubstituteStatementTypes(ifs.Consequent, subst)!,
          Alternate = SubstituteStatementTypes(ifs.Alternate, subst)
        };

      case ElseStatement els:
        return els with { Body = SubstituteStatementTypes(els.Body, subst)! };

      case WhenStatement whenStmt:
        var conditions = new List<Expression>();
        foreach (var c in whenStmt.Conditions) {
          conditions.Add(SubstituteExpressionTypes(c, subst)!);
        }
        return whenStmt with {
          Conditions = conditions,
          Body = SubstituteStatementTypes(whenStmt.Body, subst)!
        };

      case MatchStatement match:
        var branches = new List<WhenStatement>();
        foreach (var b in match.Branches) {
          branches.Add((WhenStatement)SubstituteStatementTypes(b, subst)!);
        }
        return match with {
          Condition = SubstituteExpressionTypes(match.Condition, subst)!,
          Branches = branches,
          Alternate = (ElseStatement?)SubstituteStatementTypes(match.Alternate, subst)
        };

      case VariableDeclaration varDecl:
        return varDecl with {
          Typing = SubstituteType(varDecl.Typing, subst),
          Value = SubstituteExpressionTypes(varDecl.Value, subst)
        };

      case FunctionDeclaration fnDecl:
        var parameters = new List<VariableDeclaration>();
        foreach (var p in fnDecl.Parameters) {
          parameters.Add((VariableDeclaration)SubstituteStatementTypes(p, subst)!);
        }
        return fnDecl with {
          Parameters = parameters,
          ReturnType = SubstituteType(fnDecl.ReturnType, subst),
          Body = SubstituteStatementTypes(fnDecl.Body, subst)
        };

      default:
        if (statement is Expression expr) {
          return SubstituteExpressionTypes(expr, subst);
        }
        return statement;
    }
  }

  private Expression? SubstituteExpressionTypes(Expression? expr, Dictionary<string, string> subst) {
    if (expr == null) return null;
    switch (expr) {
      case LiteralExpression lit:
        return lit;

      case IdentifierExpression id:
        return id;

      case BinaryExpression bin:
        return bin with {
          Left = SubstituteExpressionTypes(bin.Left, subst)!,
          Right = SubstituteExpressionTypes(bin.Right, subst)!
        };

      case TernaryExpression tern:
        return tern with {
          Condition = SubstituteExpressionTypes(tern.Condition, subst)!,
          Consequent = SubstituteExpressionTypes(tern.Consequent, subst)!,
          Alternate = SubstituteExpressionTypes(tern.Alternate, subst)!
        };

      case VectorExpression vec:
        var elements = new List<Expression>();
        if (vec.Elements != null) {
          foreach (var el in vec.Elements) {
            elements.Add(SubstituteExpressionTypes(el, subst)!);
          }
        }
        return vec with {
          Elements = vec.Elements != null ? elements : null,
          Len = SubstituteExpressionTypes(vec.Len, subst),
          Init = SubstituteExpressionTypes(vec.Init, subst),
          Typing = SubstituteType(vec.Typing, subst)
        };

      case StructInstanceExpression inst:
        var props = new List<VariableDeclaration>();
        foreach (var p in inst.Properties) {
          props.Add((VariableDeclaration)SubstituteStatementTypes(p, subst)!);
        }
        var genArgs = new List<string>();
        if (inst.GenericArgs != null) {
          foreach (var ga in inst.GenericArgs) {
            genArgs.Add(SubstituteType(ga, subst));
          }
        }
        return inst with {
          StructName = SubstituteType(inst.StructName, subst),
          Properties = props,
          GenericArgs = inst.GenericArgs != null ? genArgs : null
        };

      case FunctionCallExpression call:
        var args = new List<Expression>();
        foreach (var a in call.Arguments) {
          args.Add(SubstituteExpressionTypes(a, subst)!);
        }
        return call with { Arguments = args };

      case FunctionLambdaExpression lambda:
        var lambdaParams = new List<VariableDeclaration>();
        foreach (var p in lambda.Parameters) {
          lambdaParams.Add((VariableDeclaration)SubstituteStatementTypes(p, subst)!);
        }
        return lambda with {
          Parameters = lambdaParams,
          Body = SubstituteStatementTypes(lambda.Body, subst)!
        };

      case IndexAccessExpression idx:
        return idx with {
          Target = SubstituteExpressionTypes(idx.Target, subst)!,
          Index = SubstituteExpressionTypes(idx.Index, subst)!
        };

      case MapExpression map:
        return map with {
          KeyType = SubstituteType(map.KeyType, subst),
          ValueType = SubstituteType(map.ValueType, subst),
          Entries = map.Entries.Select(e => new KeyValuePair<Expression, Expression>(
            SubstituteExpressionTypes(e.Key, subst)!,
            SubstituteExpressionTypes(e.Value, subst)!
          )).ToList()
        };

      default:
        return expr;
    }
  }

  private void InferGenericParamsFromTypes(string pattern, string concrete, Dictionary<string, string> subst, HashSet<string> genericParams) {
    if (genericParams.Contains(pattern)) {
      if (!subst.ContainsKey(pattern)) {
        subst[pattern] = concrete;
      } else if (subst[pattern] != concrete) {
        if (IsCompatible(concrete, subst[pattern])) {
          // keep existing
        } else if (IsCompatible(subst[pattern], concrete)) {
          subst[pattern] = concrete;
        } else {
          throw new Exception($"TYPE CHECK ERROR: Inconsistent types for generic parameter '{pattern}': '{subst[pattern]}' vs '{concrete}'.");
        }
      }
      return;
    }
    if (pattern.StartsWith("[]") && concrete.StartsWith("[]")) {
      InferGenericParamsFromTypes(pattern.Substring(2), concrete.Substring(2), subst, genericParams);
      return;
    }
    if (pattern.StartsWith("map[")) {
      var patternParts = SplitMapTypes(pattern);
      if (patternParts != null) {
        if (concrete.StartsWith("map[")) {
          var concreteParts = SplitMapTypes(concrete);
          if (concreteParts != null) {
            InferGenericParamsFromTypes(patternParts.Item1, concreteParts.Item1, subst, genericParams);
            InferGenericParamsFromTypes(patternParts.Item2, concreteParts.Item2, subst, genericParams);
          }
        } else {
          // If we have map[Key, Value] pattern but the concrete is not map, maybe it's "any"
          // We can't infer from it, or we throw. Let's do nothing for now.
        }
      }
      return;
    }
    if (pattern.StartsWith("fn(")) {
      var patternSig = ParseFnSignature(pattern);
      if (patternSig != null) {
        if (concrete.StartsWith("fn(")) {
          var concreteSig = ParseFnSignature(concrete);
          if (concreteSig != null && patternSig.Params.Count == concreteSig.Params.Count) {
            for (int i = 0; i < patternSig.Params.Count; i++) {
              InferGenericParamsFromTypes(patternSig.Params[i], concreteSig.Params[i], subst, genericParams);
            }
            InferGenericParamsFromTypes(patternSig.ReturnType, concreteSig.ReturnType, subst, genericParams);
          }
        }
      }
      return;
    }
  }

  private string MonomorphizeStructInstance(StructInstanceExpression inst) {
    var baseStruct = FindStruct(inst.StructName);
    if (baseStruct == null) {
      throw new Exception($"TYPE CHECK ERROR: Struct '{inst.StructName}' is not defined.");
    }
    if (baseStruct.GenericParams == null || baseStruct.GenericParams.Count == 0) {
      return inst.StructName;
    }

    List<string> concreteArgs;
    if (inst.GenericArgs != null && inst.GenericArgs.Count > 0) {
      if (inst.GenericArgs.Count != baseStruct.GenericParams.Count) {
        throw new Exception($"TYPE CHECK ERROR: Struct '{inst.StructName}' expects {baseStruct.GenericParams.Count} generic parameters, but got {inst.GenericArgs.Count} arguments.");
      }
      concreteArgs = inst.GenericArgs.Select(NormalizeType).ToList();
    } else {
      var subst = new Dictionary<string, string>();
      var genericParamsSet = new HashSet<string>(baseStruct.GenericParams);
      ResolveStructMembers(inst.StructName, out var allFields, out var _);

      foreach (var prop in inst.Properties) {
        var field = allFields.Find(f => f.Identifier == prop.Identifier);
        if (field != null && prop.Value != null) {
          string inferredPropType = InferType(prop.Value);
          InferGenericParamsFromTypes(field.Typing, inferredPropType, subst, genericParamsSet);
        }
      }

      var missing = new List<string>();
      concreteArgs = new List<string>();
      foreach (var param in baseStruct.GenericParams) {
        if (subst.TryGetValue(param, out var concrete)) {
          concreteArgs.Add(concrete);
        } else {
          missing.Add(param);
        }
      }
      if (missing.Count > 0) {
        throw new Exception($"TYPE CHECK ERROR: Could not infer generic parameter(s) '{string.Join(", ", missing)}' for struct '{inst.StructName}'.");
      }
    }

    string specializedName = MonomorphizeStruct(inst.StructName, concreteArgs);
    inst.StructName = specializedName;
    return specializedName;
  }
}
