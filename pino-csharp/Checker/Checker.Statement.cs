using System;
using System.Collections.Generic;

namespace Pino;

public partial class Checker {
  private void CheckStatement(Statement statement) {
    switch (statement) {
      case VariableDeclaration varDecl:
        if (varDecl.Kind == VariableKind.Constant || varDecl.Kind == VariableKind.Variable) {
          string expectedType = NormalizeType(varDecl.Typing);
          string valType = varDecl.Value != null ? InferType(varDecl.Value, expectedType) : "any";

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
        bool isMethod = _currentStruct != null && !_inStaticMethod;
        DeclareVariable(fnDecl.Identifier, GetFunctionSignatureString(fnDecl, parentStructName: isMethod ? _currentStruct!.Identifier : null));

        if (_currentStruct != null && _inStaticMethod && _currentStruct.GenericParams != null && _currentStruct.GenericParams.Count > 0) {
          foreach (var param in fnDecl.Parameters) {
            if (TypeReferencesGenericParam(param.Typing, _currentStruct.GenericParams)) {
              throw new Exception($"TYPE CHECK ERROR: Static method '{fnDecl.Identifier}' cannot use instance-bound generic parameter '{param.Typing}' of struct '{_currentStruct.Identifier}'.");
            }
          }
          if (TypeReferencesGenericParam(fnDecl.ReturnType, _currentStruct.GenericParams)) {
            throw new Exception($"TYPE CHECK ERROR: Static method '{fnDecl.Identifier}' cannot use instance-bound generic parameter '{fnDecl.ReturnType}' of struct '{_currentStruct.Identifier}'.");
          }
        }

        var tempDecls = new List<string>();
        if (fnDecl.GenericParams != null && fnDecl.GenericParams.Count > 0) {
          foreach (var param in fnDecl.GenericParams) {
            if (FindStruct(param.Name) != null || FindInterface(param.Name) != null || FindEnum(param.Name) != null || IsPrimitiveType(param.Name)) {
              throw new Exception($"TYPE CHECK ERROR: Generic parameter '{param.Name}' in function '{fnDecl.Identifier}' conflicts with an existing defined type name.");
            }
            if (!string.IsNullOrEmpty(param.Constraint)) {
              string normalizedConstraint = NormalizeType(param.Constraint);
              var constraintInterface = FindInterface(normalizedConstraint);
              if (constraintInterface != null) {
                _interfaces[param.Name] = constraintInterface with { Identifier = param.Name };
              } else {
                var constraintStruct = FindStruct(normalizedConstraint);
                if (constraintStruct != null) {
                  _structs[param.Name] = constraintStruct with { Identifier = param.Name };
                } else {
                  _structs[param.Name] = new StructDeclaration(param.Name, new(), new(), new());
                }
              }
            } else {
              _structs[param.Name] = new StructDeclaration(param.Name, new(), new(), new());
            }
            tempDecls.Add(param.Name);
          }
        }

        if (isMethod) {
          PushScope();
          DeclareVariable("this", _currentStruct!.Identifier);
          DeclareVariable("self", _currentStruct!.Identifier);
          ResolveStructMembers(_currentStruct.Identifier, out var fields, out var _);
          foreach (var field in fields) {
            DeclareVariable(field.Identifier, field.Typing);
          }
        }
        string previousReturnType = _currentReturnType;
        _currentReturnType = fnDecl.ReturnType;
        try {
          PushScope();
          foreach (var param in fnDecl.Parameters) {
            DeclareVariable(param.Identifier, NormalizeType(param.Typing));
          }
          if (fnDecl.Body != null) {
            CheckStatement(fnDecl.Body);
          }
          PopScope();
          if (isMethod) {
            PopScope();
          }
          InferFunctionReturnType(fnDecl, isMethod ? _currentStruct!.Identifier : null);
        } finally {
          _currentReturnType = previousReturnType;
        }

        foreach (var name in tempDecls) {
          _interfaces.Remove(name);
          _structs.Remove(name);
        }
        break;

      case StructDeclaration structDecl:
        var structTempDecls = new List<string>();
        if (structDecl.GenericParams != null && structDecl.GenericParams.Count > 0) {
          foreach (var param in structDecl.GenericParams) {
            if (FindStruct(param.Name) != null || FindInterface(param.Name) != null || FindEnum(param.Name) != null || IsPrimitiveType(param.Name)) {
              throw new Exception($"TYPE CHECK ERROR: Generic parameter '{param.Name}' in struct '{structDecl.Identifier}' conflicts with an existing defined type name.");
            }
            if (!string.IsNullOrEmpty(param.Constraint)) {
              string normalizedConstraint = NormalizeType(param.Constraint);
              var constraintInterface = FindInterface(normalizedConstraint);
              if (constraintInterface != null) {
                _interfaces[param.Name] = constraintInterface with { Identifier = param.Name };
              } else {
                var constraintStruct = FindStruct(normalizedConstraint);
                if (constraintStruct != null) {
                  _structs[param.Name] = constraintStruct with { Identifier = param.Name };
                } else {
                  _structs[param.Name] = new StructDeclaration(param.Name, new(), new(), new());
                }
              }
            } else {
              _structs[param.Name] = new StructDeclaration(param.Name, new(), new(), new());
            }
            structTempDecls.Add(param.Name);
          }
        }
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

          var hiddenStructParams = new Dictionary<string, (InterfaceDeclaration?, StructDeclaration?)>();
          if (method.IsStatic && structDecl.GenericParams != null) {
            foreach (var p in structDecl.GenericParams) {
              _interfaces.TryGetValue(p.Name, out var intf);
              _structs.TryGetValue(p.Name, out var strct);
              hiddenStructParams[p.Name] = (intf, strct);
              _interfaces.Remove(p.Name);
              _structs.Remove(p.Name);
            }
          }

          CheckStatement(method);

          foreach (var kvp in hiddenStructParams) {
            if (kvp.Value.Item1 != null) _interfaces[kvp.Key] = kvp.Value.Item1;
            if (kvp.Value.Item2 != null) _structs[kvp.Key] = kvp.Value.Item2;
          }
        }
        _currentStruct = oldStruct;
        _inStaticMethod = oldStatic;

        foreach (var name in structTempDecls) {
          _interfaces.Remove(name);
          _structs.Remove(name);
        }
        break;

      case InterfaceDeclaration:
      case EnumDeclaration:
      case UnionDeclaration:
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
          InferType(ret.Argument, _currentReturnType);
          CheckExpression(ret.Argument);
        }
        break;

      case LoopStatement loop:
        if (loop.Kind == LoopKind.ForIn) {
          string colType = "any";
          if (loop.End != null) {
            CheckExpression(loop.End);
            colType = InferType(loop.End);
          }
          PushScope();
          if (loop.Begin is IdentifierExpression id) {
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
            } else if (colType == "string") {
              loopVarType = "rune";
              keyVarType = "int";
            }
            DeclareVariable(id.Name, loopVarType);
            if (!string.IsNullOrEmpty(loop.KeyVar)) {
              DeclareVariable(loop.KeyVar, keyVarType);
            }
          }
        } else if (loop.Kind == LoopKind.ForTimes) {
          if (loop.Begin != null) {
            CheckExpression(loop.Begin);
            string exprType = InferType(loop.Begin);
            if (exprType == "bool") {
              loop.Kind = LoopKind.While;
            }
          }
          PushScope();
          if (loop.Kind == LoopKind.ForTimes) {
            DeclareVariable("it", "int");
          }
        } else if (loop.Kind == LoopKind.While) {
          if (loop.Begin != null) {
            CheckExpression(loop.Begin);
          }
          PushScope();
        } else {
          PushScope();
        }
        CheckStatement(loop.Body);
        PopScope();
        break;

      case MatchStatement match:
        string condType = InferType(match.Condition);
        CheckExpression(match.Condition);
        foreach (var branch in match.Branches) {
          PushScope();
          foreach (var cond in branch.Conditions) {
            CheckPattern(cond, condType);
          }
          if (branch.Body is BlockStatement block) {
            PushScope();
            foreach (var s in block.Statements) {
              CheckStatement(s);
            }
            PopScope();
          } else {
            CheckStatement(branch.Body);
          }
          PopScope();
        }
        if (match.Alternate != null) {
          CheckStatement(match.Alternate);
        } else {
          var unionDecl = FindUnion(condType);
          if (unionDecl != null) {
            var allVariants = unionDecl.Variants.Select(v => v.Identifier).ToHashSet();
            var matchedVariants = new HashSet<string>();
            foreach (var branch in match.Branches) {
              foreach (var cond in branch.Conditions) {
                if (cond is VariantPattern varPat) {
                  if (varPat.UnionName == condType || 
                      (condType.StartsWith(varPat.UnionName + "_") && FindUnion(varPat.UnionName) != null)) {
                    matchedVariants.Add(varPat.VariantName);
                  }
                }
              }
            }
            var missing = allVariants.Except(matchedVariants).ToList();
            if (missing.Count > 0) {
              throw new Exception($"TYPE CHECK ERROR: Match statement on union '{condType}' is not exhaustive. Missing variant(s): {string.Join(", ", missing)}.");
            }
          } else {
            var enumDecl = FindEnum(condType);
            if (enumDecl != null) {
              var allMembers = enumDecl.Members.ToHashSet();
              var matchedMembers = new HashSet<string>();
              foreach (var branch in match.Branches) {
                foreach (var cond in branch.Conditions) {
                  if (cond is VariantPattern varPat) {
                    if (varPat.UnionName == condType) {
                      matchedMembers.Add(varPat.VariantName);
                    }
                  }
                }
              }
              var missing = allMembers.Except(matchedMembers).ToList();
              if (missing.Count > 0) {
                throw new Exception($"TYPE CHECK ERROR: Match statement on enum '{condType}' is not exhaustive. Missing member(s): {string.Join(", ", missing)}.");
              }
            }
          }
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

  private bool IsPrimitiveType(string name) {
    return name == "bool" || name == "int" || name == "float" || name == "string" || name == "rune" || name == "regex" || name == "any";
  }

  private bool TypeReferencesGenericParam(string typing, List<GenericParam> structGenericParams) {
    if (string.IsNullOrEmpty(typing) || structGenericParams == null || structGenericParams.Count == 0) return false;
    foreach (var gp in structGenericParams) {
      var pattern = $@"\b{gp.Name}\b";
      if (System.Text.RegularExpressions.Regex.IsMatch(typing, pattern)) return true;
    }
    return false;
  }

  private void CheckPattern(Pattern pattern, string targetType) {
    switch (pattern) {
      case LiteralPattern lit:
        CheckExpression(lit.Value);
        string litType = InferType(lit.Value);
        if (!IsCompatible(litType, targetType)) {
          throw new Exception($"TYPE CHECK ERROR: Match pattern literal type '{litType}' is not compatible with condition type '{targetType}'.");
        }
        break;

      case IdentifierPattern id:
        DeclareVariable(id.Name, targetType);
        break;

      case VariantPattern varPat:
        {
          targetType = NormalizeType(targetType);
          string patternUnionName = varPat.UnionName;
          var baseUnion = FindUnion(patternUnionName);
          if (baseUnion != null && baseUnion.GenericParams != null && baseUnion.GenericParams.Count > 0) {
            if (targetType.StartsWith(patternUnionName + "_")) {
              varPat.UnionName = targetType;
            }
          }
        }
        var unionDecl = FindUnion(varPat.UnionName);
        if (unionDecl == null) {
          var enumDecl = FindEnum(varPat.UnionName);
          if (enumDecl != null) {
            if (!enumDecl.Members.Contains(varPat.VariantName)) {
              throw new Exception($"TYPE CHECK ERROR: Enum '{varPat.UnionName}' has no member '{varPat.VariantName}'.");
            }
            if (varPat.SubPatterns.Count > 0) {
              throw new Exception($"TYPE CHECK ERROR: Enum member '{varPat.VariantName}' cannot have associated values.");
            }
            if (!IsCompatible(varPat.UnionName, targetType)) {
              throw new Exception($"TYPE CHECK ERROR: Cannot match enum member of type '{varPat.UnionName}' against condition of type '{targetType}'.");
            }
            break;
          }
          throw new Exception($"TYPE CHECK ERROR: Union '{varPat.UnionName}' is not defined.");
        }
        if (!IsCompatible(varPat.UnionName, targetType)) {
          throw new Exception($"TYPE CHECK ERROR: Cannot match union pattern of type '{varPat.UnionName}' against condition of type '{targetType}'.");
        }
        var variant = unionDecl.Variants.Find(v => v.Identifier == varPat.VariantName);
        if (variant == null) {
          throw new Exception($"TYPE CHECK ERROR: Union '{varPat.UnionName}' has no variant '{varPat.VariantName}'.");
        }
        if (varPat.SubPatterns.Count != variant.AssociatedTypes.Count) {
          throw new Exception($"TYPE CHECK ERROR: Union variant '{varPat.VariantName}' expects {variant.AssociatedTypes.Count} subpatterns, but pattern has {varPat.SubPatterns.Count}.");
        }
        for (int i = 0; i < varPat.SubPatterns.Count; i++) {
          CheckPattern(varPat.SubPatterns[i], variant.AssociatedTypes[i]);
        }
        break;
    }
  }
}
