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
