using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("pino-csharp.tests")]

namespace Pino;

public partial class Checker {
  private readonly Dictionary<string, StructDeclaration> _structs = new();
  private readonly Dictionary<string, InterfaceDeclaration> _interfaces = new();
  private readonly Dictionary<string, EnumDeclaration> _enums = new();
  private readonly Dictionary<string, UnionDeclaration> _unions = new();
  private readonly Dictionary<string, FunctionDeclaration> _functions = new();
  private readonly List<StructDeclaration> _specializedStructs = new();
  private readonly List<FunctionDeclaration> _specializedFunctions = new();
  private readonly List<UnionDeclaration> _specializedUnions = new();
  private readonly Dictionary<string, List<string>> _specializedTypesArgs = new();

  public bool IsModule { get; set; } = false;

  // Environment/scopes for variable checking
  private readonly Stack<Dictionary<string, string>> _scopes = new();

  // Context for current struct and method being checked
  private StructDeclaration? _currentStruct = null;
  private bool _inStaticMethod = false;
  private string _currentReturnType = "";
  private string _currentYieldType = "";
  private bool _isCheckingReturn = false;

  // Cache of checked modules to prevent double-checking
  internal readonly Dictionary<string, Checker> _moduleCheckers = new();
  private static readonly HashSet<string> _currentlyCheckingModules = new();
  private string _currentFilePath = "";
  // The resolved modules directory — inherited by child module checkers so
  // they don't recompute it relative to their own (sub)path.
  private string? _modulesDir = null;

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
    { "time", "fn() float" },
    { "sleep", "fn(int)" },
    { "type", "fn(any) string" },
    { "str", "fn(any) string" },
    { "clear", "fn()" },
    { "regex", "fn(string) regex" },
    { "panic", "fn(string) any" },
    { "read_file", "fn(string) Result[string, IOError]" },
    { "write_file", "fn(string, string) Result[string, IOError]" },
    { "file_exists", "fn(string) bool" }
  };

  public StructDeclaration? FindStruct(string name) {
    if (name.Contains("::")) {
      var parts = name.Split("::");
      var modName = parts[0];
      var localName = parts[1];
      if (_moduleCheckers.TryGetValue(modName, out var modChecker)) {
        var imported = modChecker.FindStruct(localName);
        if (imported != null) {
          if (!imported.IsPublic) {
            throw new Exception($"TYPE CHECK ERROR: Struct '{name}' is not public.");
          }
          return imported;
        }
      }
      return null;
    }
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
    if (name.Contains("::")) {
      var parts = name.Split("::");
      var modName = parts[0];
      var localName = parts[1];
      if (_moduleCheckers.TryGetValue(modName, out var modChecker)) {
        var imported = modChecker.FindInterface(localName);
        if (imported != null) {
          if (!imported.IsPublic) {
            throw new Exception($"TYPE CHECK ERROR: Interface '{name}' is not public.");
          }
          return imported;
        }
      }
      return null;
    }
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
    if (name.Contains("::")) {
      var parts = name.Split("::");
      var modName = parts[0];
      var localName = parts[1];
      if (_moduleCheckers.TryGetValue(modName, out var modChecker)) {
        var imported = modChecker.FindEnum(localName);
        if (imported != null) {
          if (!imported.IsPublic) {
            throw new Exception($"TYPE CHECK ERROR: Enum '{name}' is not public.");
          }
          return imported;
        }
      }
      return null;
    }
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

  public UnionDeclaration? FindUnion(string name) {
    if (name.Contains("::")) {
      var parts = name.Split("::");
      var modName = parts[0];
      var localName = parts[1];
      if (_moduleCheckers.TryGetValue(modName, out var modChecker)) {
        var imported = modChecker.FindUnion(localName);
        if (imported != null) {
          if (!imported.IsPublic) {
            throw new Exception($"TYPE CHECK ERROR: Union '{name}' is not public.");
          }
          return imported;
        }
      }
      return null;
    }
    if (_unions.TryGetValue(name, out var localUnion)) {
      return localUnion;
    }
    foreach (var modChecker in _moduleCheckers.Values) {
      var importedUnion = modChecker.FindUnion(name);
      if (importedUnion != null && importedUnion.IsPublic) {
        return importedUnion;
      }
    }
    return null;
  }

  public void Check(ProgramStatement program) {
    var previousFilePath = _currentFilePath;
    if (!string.IsNullOrEmpty(program.FilePath)) {
      _currentFilePath = program.FilePath;
    }

    try {
      PushScope();
      _specializedStructs.Clear();
      _specializedFunctions.Clear();
      _specializedUnions.Clear();

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
          case UnionDeclaration unionDecl:
            _unions[unionDecl.Identifier] = unionDecl;
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
              stmt is UnionDeclaration ||
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

      var concreteStructs = _specializedStructs.Where(s => IsConcreteType(s.Identifier)).ToList();
      var concreteUnions = _specializedUnions.Where(u => IsConcreteType(u.Identifier)).ToList();

      program.Statements.InsertRange(0, concreteStructs);
      program.Statements.InsertRange(0, _specializedFunctions);
      program.Statements.InsertRange(0, concreteUnions);

      PopScope();
    } finally {
      _currentFilePath = previousFilePath;
    }
  }

  private void ResolveAndCheckModule(string moduleName) {
    if (_moduleCheckers.ContainsKey(moduleName)) return;

    if (_currentlyCheckingModules.Contains(moduleName)) {
      throw new Exception($"TYPE CHECK ERROR: Circular dependency detected while type checking module '{moduleName}'.");
    }
    _currentlyCheckingModules.Add(moduleName);

    try {
      var filename = moduleName.ToLower() + ".pino";

      // Use the already-resolved modules directory if available (propagated from
      // parent checker), otherwise compute it from the current file path.
      // This prevents double-appending "modules" when a module file imports
      // another module (e.g. .../modules/modules/entities.pino).
      var modulesDir = _modulesDir;
      if (string.IsNullOrEmpty(modulesDir)) {
        var baseDir = !string.IsNullOrEmpty(_currentFilePath)
            ? Path.GetDirectoryName(_currentFilePath) ?? System.Environment.CurrentDirectory
            : System.Environment.CurrentDirectory;
        modulesDir = Path.Combine(baseDir, "modules");
      }

      var filePath = Path.Combine(modulesDir, filename);

      if (!File.Exists(filePath)) {
        throw new Exception($"TYPE CHECK ERROR: Module '{moduleName}' not found. Expected file at '{filePath}'.");
      }

      var program = Parser.ParseFile(filePath);

      // Propagate the resolved modulesDir so transitive imports resolve correctly.
      var moduleChecker = new Checker { IsModule = true, _modulesDir = modulesDir };
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

  private bool _suppressVariableDeclaration = false;

  private void DeclareVariable(string name, string type) {
    if (_suppressVariableDeclaration) return;
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
      if (builtInSig.StartsWith("fn(")) {
        int depth = 1;
        int closingParenIdx = -1;
        for (int i = 3; i < builtInSig.Length; i++) {
          if (builtInSig[i] == '(') depth++;
          else if (builtInSig[i] == ')') {
            depth--;
            if (depth == 0) {
              closingParenIdx = i;
              break;
            }
          }
        }
        if (closingParenIdx != -1) {
          string retType = builtInSig.Substring(closingParenIdx + 1).Trim();
          return string.IsNullOrEmpty(retType) ? "any" : retType;
        }
      }
      return "any";
    }

    // Look up identifier type
    string idType = ResolveIdentifierType(callee);
    if (idType.StartsWith("fn(")) {
      int depth = 1;
      int closingParenIdx = -1;
      for (int i = 3; i < idType.Length; i++) {
        if (idType[i] == '(') depth++;
        else if (idType[i] == ')') {
          depth--;
          if (depth == 0) {
            closingParenIdx = i;
            break;
          }
        }
      }
      if (closingParenIdx != -1) {
        string ret = idType.Substring(closingParenIdx + 1).Trim();
        return string.IsNullOrEmpty(ret) ? "any" : ret;
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

  private void ResolveImplicitLambdaParameters(FunctionLambdaExpression lambda, string expectedType) {
    if (expectedType.StartsWith("fn(")) {
      var sig = ParseFnSignature(expectedType);
      if (sig != null && sig.Params.Count == lambda.Parameters.Count) {
        for (int i = 0; i < lambda.Parameters.Count; i++) {
          if (lambda.Parameters[i].Typing == "implicit" || string.IsNullOrEmpty(lambda.Parameters[i].Typing)) {
            lambda.Parameters[i] = lambda.Parameters[i] with { Typing = sig.Params[i] };
          }
        }
        lambda.InferredType = null;
      }
    }
  }

  private void ResolveImplicitLambdas(List<Expression> arguments, List<VariableDeclaration> parameters, List<GenericParam>? genericParams, List<string>? genericArgs) {
    var subst = new Dictionary<string, string>();
    var genericParamsSet = genericParams != null 
        ? new HashSet<string>(genericParams.Select(p => p.Name)) 
        : new HashSet<string>();

    if (genericArgs != null && genericParams != null) {
      for (int i = 0; i < Math.Min(genericParams.Count, genericArgs.Count); i++) {
        subst[genericParams[i].Name] = NormalizeType(genericArgs[i]);
      }
    } else if (genericParams != null && genericParams.Count > 0) {
      for (int i = 0; i < Math.Min(parameters.Count, arguments.Count); i++) {
        var arg = arguments[i];
        if (arg is FunctionLambdaExpression lambda) {
          bool hasImplicit = lambda.Parameters.Any(p => p.Typing == "implicit" || string.IsNullOrEmpty(p.Typing));
          if (hasImplicit) continue;
        }
        try {
          string argType = InferType(arg);
          InferGenericParamsFromTypes(parameters[i].Typing, argType, subst, genericParamsSet);
        } catch {
          // Ignore failures during early type inference
        }
      }
    }

    for (int i = 0; i < Math.Min(parameters.Count, arguments.Count); i++) {
      var arg = arguments[i];
      if (arg is FunctionLambdaExpression lambda) {
        string expectedType = parameters[i].Typing;
        if (genericParams != null && genericParams.Count > 0) {
          expectedType = SubstituteType(expectedType, subst);
        }
        ResolveImplicitLambdaParameters(lambda, expectedType);
      }
    }
  }

  public static string FormatNotDefinedError(string typeName, string name) {
    if (name.Contains("::")) {
      var parts = name.Split("::");
      return $"TYPE CHECK ERROR: {typeName} '{parts[1]}' is not defined in module '{parts[0]}'.";
    }
    return $"TYPE CHECK ERROR: {typeName} '{name}' is not defined.";
  }

  public static bool TryParseTupleType(string typeStr, out List<(string Label, string Type)> fields) {
    fields = new List<(string Label, string Type)>();
    typeStr = typeStr.Trim();
    if (!typeStr.StartsWith("@(") || !typeStr.EndsWith(")")) {
      return false;
    }
    string inner = typeStr.Substring(2, typeStr.Length - 3).Trim();
    if (string.IsNullOrEmpty(inner)) {
      return true;
    }
    
    int braceDepth = 0;
    int bracketDepth = 0;
    int parenDepth = 0;
    var fieldStrings = new List<string>();
    int lastIndex = 0;
    
    for (int i = 0; i < inner.Length; i++) {
      char c = inner[i];
      if (c == '{') braceDepth++;
      else if (c == '}') braceDepth--;
      else if (c == '[') bracketDepth++;
      else if (c == ']') bracketDepth--;
      else if (c == '(') parenDepth++;
      else if (c == ')') parenDepth--;
      else if (c == ',' && braceDepth == 0 && bracketDepth == 0 && parenDepth == 0) {
        fieldStrings.Add(inner.Substring(lastIndex, i - lastIndex).Trim());
        lastIndex = i + 1;
      }
    }
    fieldStrings.Add(inner.Substring(lastIndex).Trim());
    
    foreach (var fieldStr in fieldStrings) {
      if (string.IsNullOrWhiteSpace(fieldStr)) continue;
      int colonIdx = fieldStr.IndexOf(':');
      if (colonIdx == -1) {
        return false;
      }
      string label = fieldStr.Substring(0, colonIdx).Trim();
      string type = fieldStr.Substring(colonIdx + 1).Trim();
      fields.Add((label, type));
    }
    return true;
  }

  private bool IsConcreteType(string typeName) {
    if (string.IsNullOrEmpty(typeName)) return false;
    if (typeName == "int" || typeName == "float" || typeName == "string" || typeName == "bool" || typeName == "void" || typeName == "rune" || typeName == "any") {
      return true;
    }
    if (typeName.StartsWith("[]")) {
      return IsConcreteType(typeName.Substring(2));
    }
    if (typeName.StartsWith("@(")) {
      return true;
    }
    if (typeName.StartsWith("map[")) {
      return true;
    }
    if (typeName.StartsWith("fn(")) {
      return true;
    }
    if (typeName.Contains('_')) {
      if (_specializedTypesArgs.TryGetValue(typeName, out var args)) {
        return args.All(IsConcreteType);
      }
      var parts = typeName.Split('_');
      for (int i = 1; i < parts.Length; i++) {
        if (!IsConcreteType(parts[i])) return false;
      }
      return true;
    }
    if (FindStruct(typeName) != null || FindUnion(typeName) != null || FindEnum(typeName) != null) {
      return true;
    }
    return false;
  }
}

