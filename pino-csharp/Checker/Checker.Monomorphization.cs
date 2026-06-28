using System;
using System.Collections.Generic;
using System.Linq;

namespace Pino;

public partial class Checker {
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
      }
      var baseInterface = FindInterface(baseName);
      if (baseInterface != null && baseInterface.GenericParams != null && baseInterface.GenericParams.Count > 0) {
        return MonomorphizeInterface(baseName, substitutedArgs);
      }
      var baseUnion = FindUnion(baseName);
      if (baseUnion != null && baseUnion.GenericParams != null && baseUnion.GenericParams.Count > 0) {
        return MonomorphizeUnion(baseName, substitutedArgs);
      }
      return baseName + "[" + string.Join(", ", substitutedArgs) + "]";
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

    VerifyGenericConstraints(baseStruct.GenericParams, concreteArgs, baseName);

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
      subst[baseStruct.GenericParams[i].Name] = concreteArgs[i];
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
        var conditions = new List<Pattern>();
        foreach (var c in whenStmt.Conditions) {
          conditions.Add(SubstitutePatternTypes(c, subst)!);
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

  private Pattern SubstitutePatternTypes(Pattern pattern, Dictionary<string, string> subst) {
    switch (pattern) {
      case LiteralPattern lit:
        return new LiteralPattern(SubstituteExpressionTypes(lit.Value, subst)!);
      case IdentifierPattern id:
        return id;
      case VariantPattern varPat:
        var newSubPatterns = new List<Pattern>();
        foreach (var sp in varPat.SubPatterns) {
          newSubPatterns.Add(SubstitutePatternTypes(sp, subst));
        }
        string newUnionName = SubstituteType(varPat.UnionName, subst);
        return new VariantPattern(newUnionName, varPat.VariantName, newSubPatterns);
      default:
        return pattern;
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

      case StructInstanceExpression inst: {
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
      }

      case FunctionCallExpression call: {
        var args = new List<Expression>();
        foreach (var a in call.Arguments) {
          args.Add(SubstituteExpressionTypes(a, subst)!);
        }
        var genArgs = new List<string>();
        if (call.GenericArgs != null) {
          foreach (var ga in call.GenericArgs) {
            genArgs.Add(SubstituteType(ga, subst));
          }
        }
        return call with {
          Callee = SubstituteType(call.Callee, subst),
          Arguments = args,
          GenericArgs = call.GenericArgs != null ? genArgs : null
        };
      }

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

      case MatchStatement match:
        return (MatchStatement)SubstituteStatementTypes(match, subst)!;

      default:
        return expr;
    }
  }

  private void InferGenericParamsFromTypes(string pattern, string concrete, Dictionary<string, string> subst, HashSet<string> genericParams) {
    if (genericParams.Contains(pattern)) {
      if (!subst.ContainsKey(pattern) || subst[pattern] == "any") {
        subst[pattern] = concrete;
      } else if (subst[pattern] != concrete && concrete != "any") {
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
      var genericParamsSet = new HashSet<string>(baseStruct.GenericParams.Select(p => p.Name));
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
        if (subst.TryGetValue(param.Name, out var concrete)) {
          concreteArgs.Add(concrete);
        } else {
          missing.Add(param.Name);
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

  private string MonomorphizeFunction(string baseName, List<string> concreteArgs) {
    if (!_functions.TryGetValue(baseName, out var baseFn)) {
      throw new Exception($"TYPE CHECK ERROR: Function '{baseName}' is not defined.");
    }
    if (baseFn.GenericParams == null || baseFn.GenericParams.Count == 0) {
      throw new Exception($"TYPE CHECK ERROR: Function '{baseName}' is not a generic function.");
    }
    if (baseFn.GenericParams.Count != concreteArgs.Count) {
      throw new Exception($"TYPE CHECK ERROR: Function '{baseName}' expects {baseFn.GenericParams.Count} generic parameters, but got {concreteArgs.Count} arguments.");
    }

    VerifyGenericConstraints(baseFn.GenericParams, concreteArgs, baseName);

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

    if (_functions.ContainsKey(specializedName)) {
      return specializedName;
    }

    var subst = new Dictionary<string, string>();
    for (int i = 0; i < baseFn.GenericParams.Count; i++) {
      subst[baseFn.GenericParams[i].Name] = concreteArgs[i];
    }

    var specializedParams = new List<VariableDeclaration>();
    foreach (var param in baseFn.Parameters) {
      specializedParams.Add(param with { Typing = SubstituteType(param.Typing, subst) });
    }
    string substitutedReturn = SubstituteType(baseFn.ReturnType, subst);
    var specializedBody = SubstituteStatementTypes(baseFn.Body, subst);

    var specializedFn = new FunctionDeclaration(
      specializedName,
      specializedParams,
      specializedBody,
      substitutedReturn,
      IsStatic: baseFn.IsStatic,
      IsPublic: baseFn.IsPublic,
      GenericParams: null
    );

    _functions[specializedName] = specializedFn;
    _specializedFunctions.Add(specializedFn);

    CheckStatement(specializedFn);

    return specializedName;
  }

  private string MonomorphizeMethod(StructDeclaration structDecl, FunctionDeclaration baseMethod, List<string> concreteArgs) {
    if (baseMethod.GenericParams == null || baseMethod.GenericParams.Count == 0) {
      throw new Exception($"TYPE CHECK ERROR: Method '{baseMethod.Identifier}' is not generic.");
    }
    if (baseMethod.GenericParams.Count != concreteArgs.Count) {
      throw new Exception($"TYPE CHECK ERROR: Method '{baseMethod.Identifier}' expects {baseMethod.GenericParams.Count} generic parameters, but got {concreteArgs.Count} arguments.");
    }

    VerifyGenericConstraints(baseMethod.GenericParams, concreteArgs, baseMethod.Identifier);

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
    string specializedName = $"{baseMethod.Identifier}_{string.Join("_", cleanArgs)}";

    if (structDecl.Methods.Any(m => m.Identifier == specializedName)) {
      return specializedName;
    }

    var subst = new Dictionary<string, string>();
    for (int i = 0; i < baseMethod.GenericParams.Count; i++) {
      subst[baseMethod.GenericParams[i].Name] = concreteArgs[i];
    }

    var specializedParams = new List<VariableDeclaration>();
    foreach (var param in baseMethod.Parameters) {
      specializedParams.Add(param with { Typing = SubstituteType(param.Typing, subst) });
    }
    string substitutedReturn = SubstituteType(baseMethod.ReturnType, subst);
    var specializedBody = SubstituteStatementTypes(baseMethod.Body, subst);

    var specializedMethod = new FunctionDeclaration(
      specializedName,
      specializedParams,
      specializedBody,
      substitutedReturn,
      IsStatic: baseMethod.IsStatic,
      IsPublic: baseMethod.IsPublic,
      GenericParams: null
    );

    structDecl.Methods.Add(specializedMethod);

    var oldStruct = _currentStruct;
    var oldStatic = _inStaticMethod;
    _currentStruct = structDecl;
    _inStaticMethod = specializedMethod.IsStatic;
    CheckStatement(specializedMethod);
    _currentStruct = oldStruct;
    _inStaticMethod = oldStatic;

    return specializedName;
  }

  public string MonomorphizeFunctionCall(FunctionCallExpression call) {
    if (!_functions.TryGetValue(call.Callee, out var baseFn)) {
      throw new Exception($"TYPE CHECK ERROR: Function '{call.Callee}' is not defined.");
    }
    if (baseFn.GenericParams == null || baseFn.GenericParams.Count == 0) {
      return call.Callee;
    }

    ResolveImplicitLambdas(call.Arguments, baseFn.Parameters, baseFn.GenericParams, call.GenericArgs);

    List<string> concreteArgs;
    if (call.GenericArgs != null && call.GenericArgs.Count > 0) {
      if (call.GenericArgs.Count != baseFn.GenericParams.Count) {
        throw new Exception($"TYPE CHECK ERROR: Function '{call.Callee}' expects {baseFn.GenericParams.Count} generic parameters, but got {call.GenericArgs.Count} arguments.");
      }
      concreteArgs = call.GenericArgs.Select(NormalizeType).ToList();
    } else {
      var subst = new Dictionary<string, string>();
      var genericParamsSet = new HashSet<string>(baseFn.GenericParams.Select(p => p.Name));
      
      var argTypes = call.Arguments.Select(InferType).ToList();
      for (int i = 0; i < Math.Min(baseFn.Parameters.Count, argTypes.Count); i++) {
        InferGenericParamsFromTypes(baseFn.Parameters[i].Typing, argTypes[i], subst, genericParamsSet);
      }

      var missing = new List<string>();
      concreteArgs = new List<string>();
      foreach (var param in baseFn.GenericParams) {
        if (subst.TryGetValue(param.Name, out var concrete)) {
          concreteArgs.Add(concrete);
        } else {
          missing.Add(param.Name);
        }
      }
      if (missing.Count > 0) {
        throw new Exception($"TYPE CHECK ERROR: Could not infer generic parameter(s) '{string.Join(", ", missing)}' for function '{call.Callee}'.");
      }
    }

    string specializedName = MonomorphizeFunction(call.Callee, concreteArgs);
    call.Callee = specializedName;
    return specializedName;
  }

  public string MonomorphizeMethodCall(StructDeclaration structDecl, FunctionCallExpression methodCall) {
    var baseMethod = structDecl.Methods.Find(m => m.Identifier == methodCall.Callee);
    if (baseMethod == null) {
      throw new Exception($"TYPE CHECK ERROR: Method '{methodCall.Callee}' is not defined on struct '{structDecl.Identifier}'.");
    }
    if (baseMethod.GenericParams == null || baseMethod.GenericParams.Count == 0) {
      return methodCall.Callee;
    }

    ResolveImplicitLambdas(methodCall.Arguments, baseMethod.Parameters, baseMethod.GenericParams, methodCall.GenericArgs);

    List<string> concreteArgs;
    if (methodCall.GenericArgs != null && methodCall.GenericArgs.Count > 0) {
      if (methodCall.GenericArgs.Count != baseMethod.GenericParams.Count) {
        throw new Exception($"TYPE CHECK ERROR: Method '{methodCall.Callee}' expects {baseMethod.GenericParams.Count} generic parameters, but got {methodCall.GenericArgs.Count} arguments.");
      }
      concreteArgs = methodCall.GenericArgs.Select(NormalizeType).ToList();
    } else {
      var subst = new Dictionary<string, string>();
      var genericParamsSet = new HashSet<string>(baseMethod.GenericParams.Select(p => p.Name));
      
      var argTypes = methodCall.Arguments.Select(InferType).ToList();
      for (int i = 0; i < Math.Min(baseMethod.Parameters.Count, argTypes.Count); i++) {
        InferGenericParamsFromTypes(baseMethod.Parameters[i].Typing, argTypes[i], subst, genericParamsSet);
      }

      var missing = new List<string>();
      concreteArgs = new List<string>();
      foreach (var param in baseMethod.GenericParams) {
        if (subst.TryGetValue(param.Name, out var concrete)) {
          concreteArgs.Add(concrete);
        } else {
          missing.Add(param.Name);
        }
      }
      if (missing.Count > 0) {
        throw new Exception($"TYPE CHECK ERROR: Could not infer generic parameter(s) '{string.Join(", ", missing)}' for method '{methodCall.Callee}'.");
      }
    }

    string specializedName = MonomorphizeMethod(structDecl, baseMethod, concreteArgs);
    methodCall.Callee = specializedName;
    return specializedName;
  }

  private string MonomorphizeInterface(string baseName, List<string> concreteArgs) {
    var baseInterface = FindInterface(baseName);
    if (baseInterface == null) {
      throw new Exception($"TYPE CHECK ERROR: Interface '{baseName}' is not defined.");
    }
    if (baseInterface.GenericParams == null || baseInterface.GenericParams.Count == 0) {
      throw new Exception($"TYPE CHECK ERROR: Interface '{baseName}' is not a generic interface.");
    }
    if (baseInterface.GenericParams.Count != concreteArgs.Count) {
      throw new Exception($"TYPE CHECK ERROR: Interface '{baseName}' expects {baseInterface.GenericParams.Count} generic parameters, but got {concreteArgs.Count} arguments.");
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

    if (_interfaces.ContainsKey(specializedName)) {
      return specializedName;
    }

    var subst = new Dictionary<string, string>();
    for (int i = 0; i < baseInterface.GenericParams.Count; i++) {
      subst[baseInterface.GenericParams[i].Name] = concreteArgs[i];
    }

    var specializedFields = new List<VariableDeclaration>();
    foreach (var field in baseInterface.Fields) {
      string substitutedTyping = SubstituteType(field.Typing, subst);
      specializedFields.Add(field with { Typing = substitutedTyping });
    }

    var specializedMethods = new List<FunctionDeclaration>();
    foreach (var method in baseInterface.Methods) {
      var specializedParams = new List<VariableDeclaration>();
      foreach (var param in method.Parameters) {
        specializedParams.Add(param with { Typing = SubstituteType(param.Typing, subst) });
      }
      string substitutedReturn = SubstituteType(method.ReturnType, subst);

      specializedMethods.Add(method with {
        Parameters = specializedParams,
        ReturnType = substitutedReturn
      });
    }

    var specializedInterface = new InterfaceDeclaration(
      specializedName,
      specializedFields,
      specializedMethods,
      GenericParams: null,
      IsPublic: baseInterface.IsPublic
    );

    _interfaces[specializedName] = specializedInterface;

    return specializedName;
  }

  private string MonomorphizeUnion(string baseName, List<string> concreteArgs) {
    var baseUnion = FindUnion(baseName);
    if (baseUnion == null) {
      throw new Exception($"TYPE CHECK ERROR: Union '{baseName}' is not defined.");
    }
    if (baseUnion.GenericParams == null || baseUnion.GenericParams.Count == 0) {
      throw new Exception($"TYPE CHECK ERROR: Union '{baseName}' is not a generic union.");
    }
    if (baseUnion.GenericParams.Count != concreteArgs.Count) {
      throw new Exception($"TYPE CHECK ERROR: Union '{baseName}' expects {baseUnion.GenericParams.Count} generic parameters, but got {concreteArgs.Count} arguments.");
    }

    VerifyGenericConstraints(baseUnion.GenericParams, concreteArgs, baseName);

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

    if (_unions.ContainsKey(specializedName)) {
      return specializedName;
    }

    var subst = new Dictionary<string, string>();
    for (int i = 0; i < baseUnion.GenericParams.Count; i++) {
      subst[baseUnion.GenericParams[i].Name] = concreteArgs[i];
    }

    var specializedVariants = new List<UnionVariant>();
    foreach (var variant in baseUnion.Variants) {
      var specializedTypes = new List<string>();
      foreach (var t in variant.AssociatedTypes) {
        specializedTypes.Add(SubstituteType(t, subst));
      }
      specializedVariants.Add(new UnionVariant(variant.Identifier, specializedTypes));
    }

    var specializedUnion = new UnionDeclaration(
      specializedName,
      specializedVariants,
      GenericParams: null,
      IsPublic: baseUnion.IsPublic
    );

    _unions[specializedName] = specializedUnion;
    _specializedUnions.Add(specializedUnion);

    return specializedName;
  }

  private void VerifyGenericConstraints(List<GenericParam> genericParams, List<string> concreteArgs, string targetName) {
    if (genericParams == null || concreteArgs == null) return;
    for (int i = 0; i < Math.Min(genericParams.Count, concreteArgs.Count); i++) {
      var param = genericParams[i];
      if (string.IsNullOrEmpty(param.Constraint)) continue;

      var concreteArg = concreteArgs[i];
      string normalizedConstraint = NormalizeType(param.Constraint);
      var interfaceDecl = FindInterface(normalizedConstraint);
      if (interfaceDecl != null) {
        var structDecl = FindStruct(concreteArg);
        if (structDecl == null || !ImplementsInterface(structDecl, interfaceDecl)) {
          throw new Exception($"TYPE CHECK ERROR: Type '{concreteArg}' does not satisfy generic constraint '{param.Constraint}' for parameter '{param.Name}'.");
        }
      } else {
        if (!IsCompatible(concreteArg, normalizedConstraint)) {
          throw new Exception($"TYPE CHECK ERROR: Type '{concreteArg}' does not satisfy generic constraint '{param.Constraint}' for parameter '{param.Name}'.");
        }
      }
    }
  }

  private string MonomorphizeUnionAccess(UnionDeclaration unionDecl, string callee, List<string> argTypes, string expectedType) {
    var variant = unionDecl.Variants.Find(v => v.Identifier == callee);
    if (variant == null) {
      throw new Exception($"TYPE CHECK ERROR: Union '{unionDecl.Identifier}' has no variant '{callee}'.");
    }

    var subst = new Dictionary<string, string>();
    var genericParamsSet = new HashSet<string>(unionDecl.GenericParams.Select(p => p.Name));

    // 1. Infer from arguments
    for (int i = 0; i < Math.Min(variant.AssociatedTypes.Count, argTypes.Count); i++) {
      InferGenericParamsFromTypes(variant.AssociatedTypes[i], argTypes[i], subst, genericParamsSet);
    }

    // 2. Infer from expected type context if available
    if (!string.IsNullOrEmpty(expectedType)) {
      if (expectedType.StartsWith(unionDecl.Identifier + "[")) {
         int bracketIdx = expectedType.IndexOf('[');
         if (bracketIdx != -1 && expectedType.EndsWith("]")) {
           string argsStr = expectedType.Substring(bracketIdx + 1, expectedType.Length - bracketIdx - 2);
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

           if (subArgs.Count == unionDecl.GenericParams.Count) {
             for (int i = 0; i < unionDecl.GenericParams.Count; i++) {
               string paramName = unionDecl.GenericParams[i].Name;
               if (!subst.TryGetValue(paramName, out var existing) || existing == "any") {
                 subst[paramName] = subArgs[i];
               }
             }
           }
         }
       } else {
         var specUnion = FindUnion(expectedType);
         if (specUnion != null) {
           for (int vIdx = 0; vIdx < Math.Min(unionDecl.Variants.Count, specUnion.Variants.Count); vIdx++) {
             var baseVar = unionDecl.Variants[vIdx];
             var specVar = specUnion.Variants[vIdx];
             for (int i = 0; i < Math.Min(baseVar.AssociatedTypes.Count, specVar.AssociatedTypes.Count); i++) {
               InferGenericParamsFromTypes(baseVar.AssociatedTypes[i], specVar.AssociatedTypes[i], subst, genericParamsSet);
             }
           }
         }
       }
     }

     // 3. Any still missing fall back to "any"
     var concreteArgs = new List<string>();
     foreach (var param in unionDecl.GenericParams) {
       if (subst.TryGetValue(param.Name, out var concrete)) {
         concreteArgs.Add(concrete);
       } else {
         concreteArgs.Add("any");
       }
     }

     return MonomorphizeUnion(unionDecl.Identifier, concreteArgs);
   }
 }
