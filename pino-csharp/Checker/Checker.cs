using System;
using System.Collections.Generic;
using System.IO;

namespace Pino;

public partial class Checker {
  private readonly Dictionary<string, StructDeclaration> _structs = new();
  private readonly Dictionary<string, InterfaceDeclaration> _interfaces = new();
  private readonly Dictionary<string, EnumDeclaration> _enums = new();
  private readonly Dictionary<string, FunctionDeclaration> _functions = new();

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
}
