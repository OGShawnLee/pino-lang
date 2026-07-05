using System;
using System.Collections.Generic;
using System.IO;

namespace Pino;

public partial class Evaluator {
  public void Execute(Statement statement, Environment env) {
    switch (statement) {
      case ProgramStatement prog:
        var previousFilePath = _currentFilePath;
        if (!string.IsNullOrEmpty(prog.FilePath)) {
          _currentFilePath = prog.FilePath;
        }
        try {
          foreach (var stmt in prog.Statements) {
            Execute(stmt, env);
          }
        } finally {
          _currentFilePath = previousFilePath;
        }
        break;

      case BlockStatement block:
        ExecuteBlock(block.Statements, new Environment(env));
        break;

      case ReturnStatement ret:
        var retVal = ret.Argument != null ? Evaluate(ret.Argument, env) : null;
        throw new PinoReturnException(retVal);

      case YieldStatement yield:
        var yieldVal = Evaluate(yield.Value, env);
        throw new PinoYieldException(yieldVal);

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
          if (varDecl.IsPublic) {
            env.PublicExports.Add(varDecl.Identifier);
          }
        }
        break;

      case TupleDestructuringDeclaration destDecl: {
        var val = Evaluate(destDecl.Value, env);
        if (val is not PinoTupleResult tupleRes) {
          throw new Exception("RUNTIME ERROR: Expected a tuple return value during destructuring.");
        }
        foreach (var field in destDecl.Fields) {
          if (!tupleRes.Fields.TryGetValue(field.Label, out var fieldVal)) {
            throw new Exception($"RUNTIME ERROR: Field '{field.Label}' not found in tuple.");
          }
          env.Define(field.Identifier, fieldVal, destDecl.Kind == VariableKind.Constant);
        }
        break;
      }

      case FunctionDeclaration fnDecl:
        var fn = new PinoFunction(fnDecl, env);
        env.Define(fnDecl.Identifier, fn, true);
        if (fnDecl.IsPublic) {
          env.PublicExports.Add(fnDecl.Identifier);
        }
        break;

      case StructDeclaration structDecl:
        var consolidatedFields = new List<VariableDeclaration>();
        var consolidatedMethods = new List<FunctionDeclaration>();

        foreach (var parentName in structDecl.InheritedStructs) {
          var parentObj = env.Get(parentName);
          if (parentObj is PinoStruct parentStruct) {
            consolidatedFields.AddRange(parentStruct.Fields);
            consolidatedMethods.AddRange(parentStruct.Methods);
          } else {
            throw new Exception($"RUNTIME ERROR: Parent struct '{parentName}' is not defined.");
          }
        }

        foreach (var field in structDecl.Fields) {
          consolidatedFields.RemoveAll(f => f.Identifier == field.Identifier);
          consolidatedFields.Add(field);
        }
        foreach (var method in structDecl.Methods) {
          consolidatedMethods.RemoveAll(m => m.Identifier == method.Identifier);
          consolidatedMethods.Add(method);
        }

        var @struct = new PinoStruct(structDecl.Identifier, consolidatedFields, consolidatedMethods, structDecl.InheritedStructs, env);
        env.Define(structDecl.Identifier, @struct, true);
        if (structDecl.IsPublic) {
          env.PublicExports.Add(structDecl.Identifier);
        }
        break;

      case EnumDeclaration enumDecl:
        var @enum = new PinoEnum(enumDecl.Identifier, enumDecl.Members);
        env.Define(enumDecl.Identifier, @enum, true);
        if (enumDecl.IsPublic) {
          env.PublicExports.Add(enumDecl.Identifier);
        }
        break;

      case InterfaceDeclaration:
        // Checked statically by Checker, ignored during execution.
        break;

      case UnionDeclaration unionDecl:
        var union = new PinoUnion(unionDecl.Identifier, unionDecl.Variants);
        env.Define(unionDecl.Identifier, union, true);
        if (unionDecl.IsPublic) {
          env.PublicExports.Add(unionDecl.Identifier);
        }
        break;

      case ModuleDeclaration:
        // Handled during load/resolution, ignored during sequential execution.
        break;

      case ImportStatement impStmt:
        var module = ResolveAndLoadModule(impStmt.ModuleName);
        env.Define(impStmt.ModuleName, module, true);
        break;

      case FromImportStatement fromImpStmt:
        var fromModule = ResolveAndLoadModule(fromImpStmt.ModuleName);
        foreach (var name in fromImpStmt.Imports) {
          if (!fromModule.PublicExports.Contains(name)) {
            throw new Exception($"RUNTIME ERROR: Module '{fromImpStmt.ModuleName}' does not export '{name}' (or it is private).");
          }
          env.Define(name, fromModule.Environment.Get(name), true);
        }
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

      case LoopKind.While:
        while (IsTruthy(Evaluate(loop.Begin!, env))) {
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
          for (int i = 0; i < list.Count; i++) {
            var item = list[i];
            var childEnv = new Environment(env);
            childEnv.Define(loopVar, item, false);
            if (!string.IsNullOrEmpty(loop.KeyVar)) {
              childEnv.Define(loop.KeyVar, (long) i, false);
            }
            try {
              Execute(loop.Body, childEnv);
            } catch (PinoBreakException) { break; } catch (PinoContinueException) { continue; }
          }
        } else if (collection is Dictionary<object, object?> dict) {
          foreach (var kvp in dict) {
            var childEnv = new Environment(env);
            if (!string.IsNullOrEmpty(loop.KeyVar)) {
              childEnv.Define(loop.KeyVar, kvp.Key, false);
              childEnv.Define(loopVar, kvp.Value, false);
            } else {
              childEnv.Define(loopVar, kvp.Key, false);
            }
            try {
              Execute(loop.Body, childEnv);
            } catch (PinoBreakException) { break; } catch (PinoContinueException) { continue; }
          }
        } else if (collection is long rangeLimit) {
          for (long i = 0; i < rangeLimit; i++) {
            var childEnv = new Environment(env);
            childEnv.Define(loopVar, i, false);
            if (!string.IsNullOrEmpty(loop.KeyVar)) {
              childEnv.Define(loop.KeyVar, i, false);
            }
            try {
              Execute(loop.Body, childEnv);
            } catch (PinoBreakException) { break; } catch (PinoContinueException) { continue; }
          }
        } else if (collection is string str) {
          long idx = 0;
          foreach (var r in str.EnumerateRunes()) {
            var item = new PinoRune(r.Value);
            var childEnv = new Environment(env);
            childEnv.Define(loopVar, item, false);
            if (!string.IsNullOrEmpty(loop.KeyVar)) {
              childEnv.Define(loop.KeyVar, idx, false);
            }
            try {
              Execute(loop.Body, childEnv);
            } catch (PinoBreakException) { break; } catch (PinoContinueException) { continue; }
            idx++;
          }
        } else {
          throw new Exception("RUNTIME ERROR: Cannot iterate over non-iterable object.");
        }
        break;
    }
  }

  private void ExecuteMatch(MatchStatement match, Environment env) {
    EvaluateMatch(match, env);
  }

  private PinoModule ResolveAndLoadModule(string moduleName) {
    if (_moduleCache.TryGetValue(moduleName, out var cachedModule)) {
      return cachedModule;
    }

    var filename = moduleName.ToLower() + ".pino";
    var baseDir = !string.IsNullOrEmpty(_currentFilePath)
        ? Path.GetDirectoryName(_currentFilePath) ?? System.Environment.CurrentDirectory
        : System.Environment.CurrentDirectory;
    var modulesDir = Path.Combine(baseDir, "modules");
    var filePath = Path.Combine(modulesDir, filename);

    if (!File.Exists(filePath)) {
      throw new Exception($"RUNTIME ERROR: Module '{moduleName}' not found. Expected file at '{filePath}'.");
    }

    ProgramStatement program;
    try {
      program = Parser.ParseFile(filePath);
    } catch (Exception ex) {
      throw new Exception($"RUNTIME ERROR: Failed to parse module '{moduleName}': {ex.Message}");
    }

    if (_currentlyLoadingModules.Contains(moduleName)) {
      throw new Exception($"RUNTIME ERROR: Circular dependency detected while importing module '{moduleName}'.");
    }
    _currentlyLoadingModules.Add(moduleName);

    try {
      var moduleChecker = new Checker();
      try {
        moduleChecker.Check(program);
      } catch {
        // Ignore type checking errors to let runtime handle it
      }

      var moduleEnv = new Environment(_globals);
      foreach (var stmt in program.Statements) {
        if (stmt is ModuleDeclaration modDecl) {
          if (modDecl.Identifier != moduleName) {
            throw new Exception($"RUNTIME ERROR: Module name mismatch. Declared '{modDecl.Identifier}' in file, but imported as '{moduleName}'.");
          }
          continue;
        }
        Execute(stmt, moduleEnv);
      }

      var pinoModule = new PinoModule(moduleName, moduleEnv, moduleEnv.PublicExports);
      _moduleCache[moduleName] = pinoModule;
      return pinoModule;
    } finally {
      _currentlyLoadingModules.Remove(moduleName);
    }
  }

  private bool MatchPattern(Pattern pattern, object? value, Environment env, Dictionary<string, object?> bindings) {
    switch (pattern) {
      case LiteralPattern lit:
        var litVal = Evaluate(lit.Value, env);
        return Equals(value, litVal);

      case IdentifierPattern id:
        bindings[id.Name] = value;
        return true;

      case VariantPattern varPat:
        if (value is PinoEnumValue enumVal) {
          return enumVal.EnumName == varPat.UnionName && enumVal.Member == varPat.VariantName;
        }
        if (value is PinoUnionValue unionVal) {
          if (unionVal.UnionName == varPat.UnionName && unionVal.VariantName == varPat.VariantName) {
            if (varPat.SubPatterns.Count != unionVal.Payload.Count) {
              return false;
            }
            for (int i = 0; i < varPat.SubPatterns.Count; i++) {
              if (!MatchPattern(varPat.SubPatterns[i], unionVal.Payload[i], env, bindings)) {
                return false;
              }
            }
            return true;
          }
        }
        return false;

      default:
        return false;
    }
  }
}
