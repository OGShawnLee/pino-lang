using System;
using System.Collections.Generic;
using System.Linq;

namespace Pino;

public partial class Checker {
  private void CheckExpression(Expression expr) {
    switch (expr) {
      case IdentifierExpression id:
        Resolve(id, id.Name);
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
          if (bin.Operator != OperatorType.MemberAccess && bin.Operator != OperatorType.StaticMemberAccess) {
            CheckExpression(bin.Right);
          }

          if (bin.Operator == OperatorType.Assignment) {
            string leftType = InferType(bin.Left);
            string rightType = InferType(bin.Right);
            if (!IsCompatible(rightType, leftType)) {
              throw new Exception($"TYPE CHECK ERROR: Cannot assign type '{rightType}' to target of type '{leftType}'.");
            }
          } else if (bin.Operator == OperatorType.MemberAccess) {
            string leftType = InferType(bin.Left);
            var accessStructDecl = FindStruct(leftType);
            var accessInterfaceDecl = FindInterface(leftType);
            if (bin.Right is FunctionCallExpression methodCall) {
              if (accessStructDecl != null) {
                ResolveStructMembers(leftType, out var allFields, out var allMethods);
                var method = allMethods.Find(m => m.Identifier == methodCall.Callee);
                if (method != null) {
                  if (method.IsStatic) {
                    throw new Exception($"TYPE CHECK ERROR: Cannot call static method '{method.Identifier}' of struct '{accessStructDecl.Identifier}' on an instance.");
                  }
                  
                  ResolveImplicitLambdas(methodCall.Arguments, method.Parameters, method.GenericParams, methodCall.GenericArgs);

                  if (method.GenericParams != null && method.GenericParams.Count > 0) {
                    foreach (var arg in methodCall.Arguments) {
                      CheckExpression(arg);
                    }
                    MonomorphizeMethodCall(accessStructDecl, methodCall);
                    method = accessStructDecl.Methods.Find(m => m.Identifier == methodCall.Callee) ?? method;
                  } else {
                    foreach (var arg in methodCall.Arguments) {
                      CheckExpression(arg);
                    }
                  }

                  // Validate arguments
                  var memberCallArgTypes = new List<string>();
                  for (int i = 0; i < methodCall.Arguments.Count; i++) {
                    string expectedArgType = i < method.Parameters.Count ? method.Parameters[i].Typing : "";
                    memberCallArgTypes.Add(InferType(methodCall.Arguments[i], expectedArgType));
                  }
                  if (method.Parameters.Count != memberCallArgTypes.Count) {
                    throw new Exception($"TYPE CHECK ERROR: Method '{method.Identifier}' expected {method.Parameters.Count} arguments, but got {memberCallArgTypes.Count}.");
                  }
                  for (int i = 0; i < method.Parameters.Count; i++) {
                    if (!IsCompatible(memberCallArgTypes[i], method.Parameters[i].Typing)) {
                      throw new Exception($"TYPE CHECK ERROR: Argument {i + 1} for method '{method.Identifier}' expected type '{method.Parameters[i].Typing}', but got '{memberCallArgTypes[i]}'.");
                    }
                  }
                } else {
                  var field = allFields.Find(f => f.Identifier == methodCall.Callee);
                  if (field != null && field.Typing.StartsWith("fn(")) {
                    if (ParseFunctionSignature(field.Typing, out var paramsList, out var returnType)) {
                      if (paramsList != null) {
                        var tempParams = paramsList.Select((p, idx) => new VariableDeclaration(VariableKind.Parameter, $"p{idx}", null, p)).ToList();
                        ResolveImplicitLambdas(methodCall.Arguments, tempParams, null, null);
                      }
                      
                      foreach (var arg in methodCall.Arguments) {
                        CheckExpression(arg);
                      }

                      var memberCallArgTypes = methodCall.Arguments.Select(InferType).ToList();
                      if (paramsList != null) {
                        if (paramsList.Count != memberCallArgTypes.Count) {
                          throw new Exception($"TYPE CHECK ERROR: Callable field '{field.Identifier}' expected {paramsList.Count} arguments, but got {memberCallArgTypes.Count}.");
                        }
                        for (int i = 0; i < paramsList.Count; i++) {
                          if (!IsCompatible(memberCallArgTypes[i], paramsList[i])) {
                            throw new Exception($"TYPE CHECK ERROR: Argument {i + 1} for callable field '{field.Identifier}' expected type '{paramsList[i]}', but got '{memberCallArgTypes[i]}'.");
                          }
                        }
                      }
                    }
                  } else {
                    throw new Exception($"TYPE CHECK ERROR: Struct '{accessStructDecl.Identifier}' does not have method '{methodCall.Callee}'.");
                  }
                }
              } else if (accessInterfaceDecl != null) {
                var method = accessInterfaceDecl.Methods.Find(m => m.Identifier == methodCall.Callee);
                if (method != null) {
                  ResolveImplicitLambdas(methodCall.Arguments, method.Parameters, method.GenericParams, methodCall.GenericArgs);
                  
                  foreach (var arg in methodCall.Arguments) {
                    CheckExpression(arg);
                  }

                  // Validate arguments
                  var memberCallArgTypes = methodCall.Arguments.Select(InferType).ToList();
                  if (method.Parameters.Count != memberCallArgTypes.Count) {
                    throw new Exception($"TYPE CHECK ERROR: Method '{method.Identifier}' of interface '{accessInterfaceDecl.Identifier}' expected {method.Parameters.Count} arguments, but got {memberCallArgTypes.Count}.");
                  }
                  for (int i = 0; i < method.Parameters.Count; i++) {
                    if (!IsCompatible(memberCallArgTypes[i], method.Parameters[i].Typing)) {
                      throw new Exception($"TYPE CHECK ERROR: Argument {i + 1} for interface method '{method.Identifier}' expected type '{method.Parameters[i].Typing}', but got '{memberCallArgTypes[i]}'.");
                    }
                  }
                } else {
                  var field = accessInterfaceDecl.Fields.Find(f => f.Identifier == methodCall.Callee);
                  if (field != null && field.Typing.StartsWith("fn(")) {
                    if (ParseFunctionSignature(field.Typing, out var paramsList, out var returnType)) {
                      if (paramsList != null) {
                        var tempParams = paramsList.Select((p, idx) => new VariableDeclaration(VariableKind.Parameter, $"p{idx}", null, p)).ToList();
                        ResolveImplicitLambdas(methodCall.Arguments, tempParams, null, null);
                      }

                      foreach (var arg in methodCall.Arguments) {
                        CheckExpression(arg);
                      }

                      var memberCallArgTypes = methodCall.Arguments.Select(InferType).ToList();
                      if (paramsList != null) {
                        if (paramsList.Count != memberCallArgTypes.Count) {
                          throw new Exception($"TYPE CHECK ERROR: Callable field '{field.Identifier}' of interface '{accessInterfaceDecl.Identifier}' expected {paramsList.Count} arguments, but got {memberCallArgTypes.Count}.");
                        }
                        for (int i = 0; i < paramsList.Count; i++) {
                          if (!IsCompatible(memberCallArgTypes[i], paramsList[i])) {
                            throw new Exception($"TYPE CHECK ERROR: Argument {i + 1} for callable field '{field.Identifier}' of interface '{accessInterfaceDecl.Identifier}' expected type '{paramsList[i]}', but got '{memberCallArgTypes[i]}'.");
                          }
                        }
                      }
                    }
                  } else {
                    throw new Exception($"TYPE CHECK ERROR: Interface '{accessInterfaceDecl.Identifier}' does not have method or callable field '{methodCall.Callee}'.");
                  }
                }
              } else if (leftType.StartsWith("[]")) {
                string callee = methodCall.Callee;
                if (callee == "map" || callee == "filter" || callee == "any" || callee == "all" || callee == "each" || callee == "find") {
                  string elemType = leftType.Substring(2);
                  if (methodCall.Arguments.Count > 0) {
                    string expectedSig = callee == "filter" || callee == "any" || callee == "all" || callee == "find"
                        ? $"fn({elemType}) bool"
                        : $"fn({elemType}) any";
                    
                    var tempParams = new List<VariableDeclaration> {
                      new VariableDeclaration(VariableKind.Parameter, "it", null, elemType)
                    };
                    ResolveImplicitLambdas(methodCall.Arguments, tempParams, null, null);
                  }
                }

                foreach (var arg in methodCall.Arguments) {
                  CheckExpression(arg);
                }
              } else {
                foreach (var arg in methodCall.Arguments) {
                  CheckExpression(arg);
                }
              }
            } else {
              CheckExpression(bin.Right);
              if (accessStructDecl != null) {
                ResolveStructMembers(leftType, out var allFields, out var allMethods);
                if (bin.Right is IdentifierExpression propId) {
                  var method = allMethods.Find(m => m.Identifier == propId.Name);
                  if (method != null && method.IsStatic) {
                    throw new Exception($"TYPE CHECK ERROR: Cannot access static method '{method.Identifier}' as instance member.");
                  }
                  var field = allFields.Find(f => f.Identifier == propId.Name);
                  if (field == null && method == null) {
                    throw new Exception($"TYPE CHECK ERROR: Struct '{accessStructDecl.Identifier}' does not have field or instance method '{propId.Name}'.");
                  }
                }
              } else if (accessInterfaceDecl != null) {
                if (bin.Right is IdentifierExpression propId) {
                  var field = accessInterfaceDecl.Fields.Find(f => f.Identifier == propId.Name);
                  var method = accessInterfaceDecl.Methods.Find(m => m.Identifier == propId.Name);
                  if (field == null && method == null) {
                    throw new Exception($"TYPE CHECK ERROR: Interface '{accessInterfaceDecl.Identifier}' does not have property or method '{propId.Name}'.");
                  }
                }
              }
            }
          } else if (bin.Operator == OperatorType.StaticMemberAccess) {
            if (bin.Left is IdentifierExpression structId) {
              string structName = structId.Name;
              var staticAccessUnionDecl = FindUnion(structName);
              if (staticAccessUnionDecl != null) {
                if (staticAccessUnionDecl.GenericParams != null && staticAccessUnionDecl.GenericParams.Count > 0) {
                  if (structId.Name == staticAccessUnionDecl.Identifier) {
                    InferType(bin);
                    structName = structId.Name;
                    staticAccessUnionDecl = FindUnion(structName);
                  }
                }

                if (bin.Right is FunctionCallExpression methodCall) {
                  var variant = staticAccessUnionDecl.Variants.Find(v => v.Identifier == methodCall.Callee);
                  if (variant == null) {
                    throw new Exception($"TYPE CHECK ERROR: Union '{structName}' has no variant '{methodCall.Callee}'.");
                  }
                  foreach (var arg in methodCall.Arguments) {
                    CheckExpression(arg);
                  }
                  var staticCallArgTypes = new List<string>();
                  for (int i = 0; i < methodCall.Arguments.Count; i++) {
                    string expectedArgType = i < variant.AssociatedTypes.Count ? variant.AssociatedTypes[i] : "";
                    staticCallArgTypes.Add(InferType(methodCall.Arguments[i], expectedArgType));
                  }
                  if (variant.AssociatedTypes.Count != staticCallArgTypes.Count) {
                    throw new Exception($"TYPE CHECK ERROR: Variant constructor '{methodCall.Callee}' of union '{structName}' expected {variant.AssociatedTypes.Count} arguments, but got {staticCallArgTypes.Count}.");
                  }
                  for (int i = 0; i < variant.AssociatedTypes.Count; i++) {
                    if (!IsCompatible(staticCallArgTypes[i], variant.AssociatedTypes[i])) {
                      throw new Exception($"TYPE CHECK ERROR: Argument {i + 1} for variant constructor '{methodCall.Callee}' expected type '{variant.AssociatedTypes[i]}', but got '{staticCallArgTypes[i]}'.");
                    }
                  }
                } else if (bin.Right is IdentifierExpression methodId) {
                  var variant = staticAccessUnionDecl.Variants.Find(v => v.Identifier == methodId.Name);
                  if (variant == null) {
                    throw new Exception($"TYPE CHECK ERROR: Union '{structName}' has no variant '{methodId.Name}'.");
                  }
                } else {
                  throw new Exception($"TYPE CHECK ERROR: Invalid static variant access on union '{structName}'.");
                }
              } else {
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

                    ResolveImplicitLambdas(methodCall.Arguments, method.Parameters, method.GenericParams, methodCall.GenericArgs);
                    
                    if (method.GenericParams != null && method.GenericParams.Count > 0) {
                      foreach (var arg in methodCall.Arguments) {
                        CheckExpression(arg);
                      }
                      MonomorphizeMethodCall(staticAccessStructDecl, methodCall);
                      method = staticAccessStructDecl.Methods.Find(m => m.Identifier == methodCall.Callee) ?? method;
                    } else {
                      foreach (var arg in methodCall.Arguments) {
                        CheckExpression(arg);
                      }
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
                    CheckExpression(bin.Right);
                    var method = allMethods.Find(m => m.Identifier == methodId.Name && m.IsStatic);
                    if (method == null) {
                      throw new Exception($"TYPE CHECK ERROR: Struct '{structName}' has no static method '{methodId.Name}'.");
                    }
                    if (!method.IsStatic) {
                      throw new Exception($"TYPE CHECK ERROR: Method '{method.Identifier}' of struct '{structName}' is not static.");
                    }
                  } else {
                    throw new Exception($"TYPE CHECK ERROR: Invalid static member access on struct '{structName}'.");
                  }
                } else if (_moduleCheckers.TryGetValue(structName, out var modChecker)) {
                  if (bin.Right is StructInstanceExpression modInst) {
                    if (!modInst.StructName.StartsWith(structName + "::")) {
                      modInst.StructName = structName + "::" + modInst.StructName;
                    }
                    CheckExpression(modInst);
                  } else if (bin.Right is FunctionCallExpression modCall) {
                    foreach (var arg in modCall.Arguments) {
                      CheckExpression(arg);
                    }
                  }
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
        string monomorphizedName = MonomorphizeStructInstance(inst);
        var structDecl = FindStruct(monomorphizedName);
        if (structDecl == null) {
          throw new Exception(FormatNotDefinedError("Struct", monomorphizedName));
        } else {
          ResolveStructMembers(monomorphizedName, out var allFields, out var _);
          foreach (var prop in inst.Properties) {
            var field = allFields.Find(f => f.Identifier == prop.Identifier);
            if (field == null) {
              throw new Exception($"TYPE CHECK ERROR: Struct '{monomorphizedName}' does not have field '{prop.Identifier}'.");
            }
            if (prop.Value != null) {
              CheckExpression(prop.Value);
              string propType = InferType(prop.Value);
              if (!IsCompatible(propType, field.Typing)) {
                throw new Exception($"TYPE CHECK ERROR: Cannot assign type '{propType}' to field '{prop.Identifier}' of type '{field.Typing}' in struct '{monomorphizedName}'.");
              }
            }
          }
        }
        break;

      case FunctionCallExpression call:
        Resolve(call, call.Callee);
        _functions.TryGetValue(call.Callee, out var fnDecl);

        if (fnDecl != null) {
          ResolveImplicitLambdas(call.Arguments, fnDecl.Parameters, fnDecl.GenericParams, call.GenericArgs);
        } else {
          string calleeType = ResolveIdentifierType(call.Callee);
          if (calleeType.StartsWith("fn(")) {
            if (ParseFunctionSignature(calleeType, out var paramsList, out var _)) {
              if (paramsList != null) {
                var tempParams = paramsList.Select((p, idx) => new VariableDeclaration(VariableKind.Parameter, $"p{idx}", null, p)).ToList();
                ResolveImplicitLambdas(call.Arguments, tempParams, null, null);
              }
            }
          }
        }

        var argTypes = new List<string>();
        List<string>? expectedParams = null;
        if (fnDecl != null) {
          expectedParams = fnDecl.Parameters.Select(p => p.Typing).ToList();
        } else {
          string calleeType = ResolveIdentifierType(call.Callee);
          if (calleeType.StartsWith("fn(")) {
            if (ParseFunctionSignature(calleeType, out var paramsList, out var _)) {
              expectedParams = paramsList;
            }
          }
        }

        for (int i = 0; i < call.Arguments.Count; i++) {
          string expectedArgType = (expectedParams != null && i < expectedParams.Count) ? expectedParams[i] : "";
          argTypes.Add(InferType(call.Arguments[i], expectedArgType));
        }

        if (fnDecl != null) {
          if (fnDecl.GenericParams != null && fnDecl.GenericParams.Count > 0) {
            foreach (var arg in call.Arguments) {
              CheckExpression(arg);
            }
            string specializedName = MonomorphizeFunctionCall(call);
            _functions.TryGetValue(specializedName, out fnDecl);
          } else {
            foreach (var arg in call.Arguments) {
              CheckExpression(arg);
            }
          }

          if (fnDecl.Parameters.Count != argTypes.Count) {
            throw new Exception($"TYPE CHECK ERROR: Function '{call.Callee}' expected {fnDecl.Parameters.Count} arguments, but got {argTypes.Count}.");
          }
          for (int i = 0; i < fnDecl.Parameters.Count; i++) {
            if (!IsCompatible(argTypes[i], fnDecl.Parameters[i].Typing)) {
              throw new Exception($"TYPE CHECK ERROR: Argument {i + 1} for function '{call.Callee}' expected type '{fnDecl.Parameters[i].Typing}', but got '{argTypes[i]}'.");
            }
          }
        } else {
          foreach (var arg in call.Arguments) {
            CheckExpression(arg);
          }
        }
        break;

      case FunctionLambdaExpression lambda: {
        PushScope();
        foreach (var param in lambda.Parameters) {
          DeclareVariable(param.Identifier, param.Typing);
        }
        string oldReturnType = _currentReturnType;
        string lambdaRetType = "any";
        var lambdaReturns = FindReturnStatements(lambda.Body);
        if (lambdaReturns.Count > 0) {
          lambdaRetType = lambdaReturns[0].Argument != null ? InferType(lambdaReturns[0].Argument!) : "any";
        }
        _currentReturnType = lambdaRetType;
        try {
          CheckStatement(lambda.Body);
        } finally {
          _currentReturnType = oldReturnType;
        }
        PopScope();
        break;
      }

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

      case UnaryExpression un:
        CheckExpression(un.Right);
        break;

      case BubbleExpression bub:
        CheckExpression(bub.Value);
        break;

      case IsExpression isExpr:
        CheckExpression(isExpr.Value);
        break;

      case RecoveryExpression rec:
        CheckExpression(rec.Value);
        break;

      case MatchStatement match:
        CheckExpression(match.Condition);
        string condType = InferType(match.Condition);
        foreach (var branch in match.Branches) {
          PushScope();
          foreach (var cond in branch.Conditions) {
            CheckPattern(cond, condType);
          }
          if (branch.Body is BlockStatement block) {
            var yields = FindYieldStatements(block);
            string expectedBranchYieldType = "";
            if (yields.Count > 0) {
              expectedBranchYieldType = InferType(yields[0].Value);
            }
            var savedYield = _currentYieldType;
            _currentYieldType = expectedBranchYieldType;
            try {
              CheckStatement(block);
            } finally {
              _currentYieldType = savedYield;
            }
          } else if (branch.Body is Expression branchExpr) {
            CheckExpression(branchExpr);
          } else {
            CheckStatement(branch.Body);
          }
          PopScope();
        }
        if (match.Alternate != null) {
          if (match.Alternate.Body is BlockStatement block) {
            var yields = FindYieldStatements(block);
            string expectedBranchYieldType = "";
            if (yields.Count > 0) {
              expectedBranchYieldType = InferType(yields[0].Value);
            }
            var savedYield = _currentYieldType;
            _currentYieldType = expectedBranchYieldType;
            try {
              CheckStatement(block);
            } finally {
              _currentYieldType = savedYield;
            }
          } else if (match.Alternate.Body is Expression altExpr) {
            CheckExpression(altExpr);
          } else {
            CheckStatement(match.Alternate.Body);
          }
        }
        break;
    }

    InferType(expr);
  }

  private string InferType(Expression expr) {
    string type = InferTypeInternal(expr, "");
    expr.InferredType = type;
    return type;
  }

  private string InferType(Expression expr, string expectedType) {
    string type = InferTypeInternal(expr, expectedType);
    expr.InferredType = type;
    return type;
  }

  private string InferTypeInternal(Expression expr, string expectedType) {
    switch (expr) {
      case TupleLiteralExpression tuple: {
        if (!_isCheckingReturn) {
          throw new Exception("TYPE CHECK ERROR: Tuple literals can only be used as a return value of a function.");
        }
        var seenLabels = new HashSet<string>();
        foreach (var field in tuple.Fields) {
          if (!seenLabels.Add(field.Label)) {
            throw new Exception($"TYPE CHECK ERROR: Duplicate label '{field.Label}' in tuple literal.");
          }
        }
        var fieldStrings = new List<string>();
        foreach (var field in tuple.Fields) {
          string fieldType = InferType(field.Value);
          fieldStrings.Add($"{field.Label}:{fieldType}");
        }
        return $"@({string.Join(",", fieldStrings)})";
      }

      case LiteralExpression lit:
        return lit.LiteralType switch {
          LiteralType.Boolean => "bool",
          LiteralType.Integer => "int",
          LiteralType.Float => "float",
          LiteralType.String => "string",
          LiteralType.Rune => "rune",
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
        return MonomorphizeStructInstance(inst);

      case MapExpression map:
        return $"map[{map.KeyType}, {map.ValueType}]";

      case FunctionCallExpression call:
        if (_functions.TryGetValue(call.Callee, out var genericFn) && genericFn.GenericParams != null && genericFn.GenericParams.Count > 0) {
          MonomorphizeFunctionCall(call);
        }
        return ResolveFunctionReturnType(call.Callee);

      case UnaryExpression un: {
        string rightType = InferType(un.Right);
        if (un.Operator == OperatorType.Not) {
          if (!IsCompatible(rightType, "bool")) {
            throw new Exception($"TYPE CHECK ERROR: Operator 'not' requires operand of type 'bool', but got '{rightType}'.");
          }
          return "bool";
        } else if (un.Operator == OperatorType.Subtraction) {
          if (!IsCompatible(rightType, "int") && !IsCompatible(rightType, "float")) {
            throw new Exception($"TYPE CHECK ERROR: Unary operator '-' requires operand of type 'int' or 'float', but got '{rightType}'.");
          }
          return rightType;
        } else {
          throw new Exception($"TYPE CHECK ERROR: Unsupported unary operator '{un.Operator}'.");
        }
      }

      case BubbleExpression bub: {
        string innerType = InferTypeInternal(bub.Value, expectedType);
        innerType = NormalizeType(innerType);
        var unionDecl = FindUnion(innerType);
        if (unionDecl == null) {
          throw new Exception($"TYPE CHECK ERROR: Cannot use '?' operator on non-union type '{innerType}'.");
        }

        if (string.IsNullOrEmpty(_currentReturnType)) {
          throw new Exception("TYPE CHECK ERROR: Cannot use '?' operator in a function without a declared Result or Option return type.");
        }

        var successVar = unionDecl.Variants.Find(v => v.Identifier == "Success");
        var failureVar = unionDecl.Variants.Find(v => v.Identifier == "Failure");
        if (successVar != null && failureVar != null) {
          string normalizedOuter = NormalizeType(_currentReturnType);
          var outerUnion = FindUnion(normalizedOuter);
          if (outerUnion == null || !outerUnion.Variants.Exists(v => v.Identifier == "Success") || !outerUnion.Variants.Exists(v => v.Identifier == "Failure")) {
            throw new Exception($"TYPE CHECK ERROR: Cannot use '?' bubble operator in a function returning '{_currentReturnType}'. Enclosing function must return a Result.");
          }
          string innerErr = failureVar.AssociatedTypes[0];
          string outerErr = outerUnion.Variants.Find(v => v.Identifier == "Failure")!.AssociatedTypes[0];
          if (!IsCompatible(innerErr, outerErr)) {
            throw new Exception($"TYPE CHECK ERROR: Cannot bubble error of type '{innerErr}' in a function expecting error of type '{outerErr}'.");
          }
          string successType = successVar.AssociatedTypes[0];
          return successType;
        }

        var someVar = unionDecl.Variants.Find(v => v.Identifier == "Some");
        var noneVar = unionDecl.Variants.Find(v => v.Identifier == "None");
        if (someVar != null && noneVar != null) {
          string normalizedOuter = NormalizeType(_currentReturnType);
          var outerUnion = FindUnion(normalizedOuter);
          if (outerUnion == null || !outerUnion.Variants.Exists(v => v.Identifier == "Some") || !outerUnion.Variants.Exists(v => v.Identifier == "None")) {
            throw new Exception($"TYPE CHECK ERROR: Cannot use '?' bubble operator in a function returning '{_currentReturnType}'. Enclosing function must return an Option.");
          }
          string successType = someVar.AssociatedTypes[0];
          return successType;
        }

        throw new Exception($"TYPE CHECK ERROR: Cannot use '?' operator on union type '{innerType}'. It must be a Result or Option.");
      }

      case IsExpression isExpr: {
        string lhsType = InferType(isExpr.Value);
        string normLhsType = NormalizeType(lhsType);

        if (isExpr.Pattern is VariantPattern varPat) {
          string patternUnionName = varPat.UnionName;
          bool isExplicitGeneric = patternUnionName.Contains("[");

          if (isExplicitGeneric) {
            varPat.UnionName = NormalizeType(patternUnionName);
          } else {
            int concreteBracketIdx = normLhsType.IndexOf('_');
            string baseConcreteUnionName = concreteBracketIdx != -1 ? normLhsType.Substring(0, concreteBracketIdx) : normLhsType;

            if (patternUnionName == baseConcreteUnionName && normLhsType.Contains("_")) {
              varPat.UnionName = normLhsType;
            }
          }

          var targetUnion = FindUnion(varPat.UnionName);
          var targetEnum = FindEnum(varPat.UnionName);
          if (targetUnion == null && targetEnum == null) {
            throw new Exception($"TYPE CHECK ERROR: Cannot match variant '{varPat.VariantName}' against non-union type '{lhsType}'.");
          }
        }

        bool oldSuppress = _suppressVariableDeclaration;
        _suppressVariableDeclaration = true;
        try {
          CheckPattern(isExpr.Pattern, lhsType);
        } finally {
          _suppressVariableDeclaration = oldSuppress;
        }

        return "bool";
      }

      case RecoveryExpression rec: {
        string innerType = InferTypeInternal(rec.Value, expectedType);
        innerType = NormalizeType(innerType);
        var unionDecl = FindUnion(innerType);
        if (unionDecl == null) {
          throw new Exception($"TYPE CHECK ERROR: Cannot use 'or' block on non-union type '{innerType}'.");
        }

        string successType = "";
        string errType = "any";

        var successVar = unionDecl.Variants.Find(v => v.Identifier == "Success");
        var failureVar = unionDecl.Variants.Find(v => v.Identifier == "Failure");
        var someVar = unionDecl.Variants.Find(v => v.Identifier == "Some");
        var noneVar = unionDecl.Variants.Find(v => v.Identifier == "None");

        if (successVar != null && failureVar != null) {
          successType = successVar.AssociatedTypes[0];
          errType = failureVar.AssociatedTypes[0];
        } else if (someVar != null && noneVar != null) {
          successType = someVar.AssociatedTypes[0];
          errType = "string";
        } else {
          throw new Exception($"TYPE CHECK ERROR: Cannot use 'or' block on union type '{innerType}'. It must be a Result or Option.");
        }

        var savedYieldType = _currentYieldType;
        _currentYieldType = successType;
        PushScope();
        DeclareVariable("err", errType);
        try {
          CheckStatement(rec.Body);
        } finally {
          PopScope();
          _currentYieldType = savedYieldType;
        }

        return successType;
      }

      case BinaryExpression bin:
        if (bin.Operator == OperatorType.MemberAccess) {
          string leftType = InferType(bin.Left);
          var structDecl = FindStruct(leftType);
          var interfaceDecl = FindInterface(leftType);
          if (structDecl != null) {
            ResolveStructMembers(leftType, out var allFields, out var allMethods);
            if (bin.Right is IdentifierExpression propId) {
              var field = allFields.Find(f => f.Identifier == propId.Name);
              if (field != null) return field.Typing;
              var method = allMethods.Find(m => m.Identifier == propId.Name && !m.IsStatic);
              if (method != null) return GetFunctionSignatureString(method, parentStructName: leftType);
            } else if (bin.Right is FunctionCallExpression methodCall) {
              var method = allMethods.Find(m => m.Identifier == methodCall.Callee && !m.IsStatic);
              if (method != null) {
                if (method.GenericParams != null && method.GenericParams.Count > 0) {
                  MonomorphizeMethodCall(structDecl, methodCall);
                }
                var specMethod = structDecl.Methods.Find(m => m.Identifier == methodCall.Callee);
                return InferFunctionReturnType(specMethod ?? method, leftType);
              }
              var field = allFields.Find(f => f.Identifier == methodCall.Callee);
              if (field != null && field.Typing.StartsWith("fn(")) {
                if (ParseFunctionSignature(field.Typing, out var _, out var returnType)) {
                  return returnType;
                }
              }
            }
          } else if (interfaceDecl != null) {
            if (bin.Right is IdentifierExpression propId) {
              var field = interfaceDecl.Fields.Find(f => f.Identifier == propId.Name);
              if (field != null) return field.Typing;
              var method = interfaceDecl.Methods.Find(m => m.Identifier == propId.Name);
              if (method != null) return GetFunctionSignatureString(method);
            } else if (bin.Right is FunctionCallExpression methodCall) {
              var method = interfaceDecl.Methods.Find(m => m.Identifier == methodCall.Callee);
              if (method != null) {
                return !string.IsNullOrEmpty(method.ReturnType) ? method.ReturnType : "any";
              }
              var field = interfaceDecl.Fields.Find(f => f.Identifier == methodCall.Callee);
              if (field != null && field.Typing.StartsWith("fn(")) {
                if (ParseFunctionSignature(field.Typing, out var _, out var returnType)) {
                  return returnType;
                }
              }
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
              if (callee == "join") {
                return "string";
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
              if (callee == "lower" || callee == "upper" || callee == "trim" || callee == "trim_start" || callee == "trim_end" || callee == "replace" || callee == "substring") {
                return "string";
              }
              if (callee == "contains" || callee == "starts_with" || callee == "ends_with") {
                return "bool";
              }
              if (callee == "index_of") {
                return "int";
              }
              if (callee == "split") {
                return "[]string";
              }
            }
          }
          if (leftType == "regex") {
            if (bin.Right is IdentifierExpression regId && regId.Name == "pattern") {
              return "string";
            }
            if (bin.Right is FunctionCallExpression methodCall) {
              string callee = methodCall.Callee;
              if (callee == "has_match") {
                return "bool";
              }
              if (callee == "match_prefix" || callee == "find" || callee == "replace") {
                return "string";
              }
              if (callee == "find_all") {
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
            var unionDecl = FindUnion(modName);
            if (unionDecl != null) {
              if (unionDecl.GenericParams != null && unionDecl.GenericParams.Count > 0) {
                if (bin.Right is FunctionCallExpression methodCall) {
                  var variant = unionDecl.Variants.Find(v => v.Identifier == methodCall.Callee);
                  if (variant == null) {
                    throw new Exception($"TYPE CHECK ERROR: Union '{modName}' has no variant '{methodCall.Callee}'.");
                  }
                  var staticCallArgTypes = methodCall.Arguments.Select(arg => InferType(arg)).ToList();
                  string specUnionName = MonomorphizeUnionAccess(unionDecl, methodCall.Callee, staticCallArgTypes, expectedType);
                  modId.Name = specUnionName;
                  return specUnionName;
                } else if (bin.Right is IdentifierExpression methodId) {
                  var variant = unionDecl.Variants.Find(v => v.Identifier == methodId.Name);
                  if (variant == null) {
                    throw new Exception($"TYPE CHECK ERROR: Union '{modName}' has no variant '{methodId.Name}'.");
                  }
                  string specUnionName = MonomorphizeUnionAccess(unionDecl, methodId.Name, new List<string>(), expectedType);
                  modId.Name = specUnionName;
                  return specUnionName;
                }
              } else {
                if (bin.Right is FunctionCallExpression methodCall) {
                  var variant = unionDecl.Variants.Find(v => v.Identifier == methodCall.Callee);
                  if (variant == null) {
                    throw new Exception($"TYPE CHECK ERROR: Union '{modName}' has no variant '{methodCall.Callee}'.");
                  }
                  return modName;
                } else if (bin.Right is IdentifierExpression methodId) {
                  var variant = unionDecl.Variants.Find(v => v.Identifier == methodId.Name);
                  if (variant == null) {
                    throw new Exception($"TYPE CHECK ERROR: Union '{modName}' has no variant '{methodId.Name}'.");
                  }
                  if (variant.AssociatedTypes.Count > 0) {
                    return $"fn({string.Join(", ", variant.AssociatedTypes)}) {modName}";
                  }
                  return modName;
                }
              }
              return "any";
            }
            var structDecl = FindStruct(modName);
            if (structDecl != null) {
              ResolveStructMembers(modName, out var _, out var allMethods);
              if (bin.Right is IdentifierExpression methodId) {
                var method = allMethods.Find(m => m.Identifier == methodId.Name && m.IsStatic);
                if (method != null) return GetFunctionSignatureString(method, parentStructName: modName);
              } else if (bin.Right is FunctionCallExpression methodCall) {
                var method = allMethods.Find(m => m.Identifier == methodCall.Callee && m.IsStatic);
                if (method != null) {
                  if (method.GenericParams != null && method.GenericParams.Count > 0) {
                    MonomorphizeMethodCall(structDecl, methodCall);
                  }
                  var specMethod = structDecl.Methods.Find(m => m.Identifier == methodCall.Callee);
                  return InferFunctionReturnType(specMethod ?? method, modName);
                }
              }
              return "any";
            }
            if (_moduleCheckers.TryGetValue(modName, out var modChecker)) {
              if (bin.Right is StructInstanceExpression modInst) {
                if (!modInst.StructName.StartsWith(modName + "::")) {
                  modInst.StructName = modName + "::" + modInst.StructName;
                }
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

        string leftT = InferType(bin.Left);
        string rightT = InferType(bin.Right);

        if (bin.Operator == OperatorType.Addition) {
          if (leftT == "string" || rightT == "string") {
            return "string";
          }
          if (leftT == "rune" && rightT == "rune") {
            return "string";
          }
          if ((leftT == "rune" && rightT == "int") || (leftT == "int" && rightT == "rune")) {
            return "rune";
          }
        }

        if (bin.Operator == OperatorType.Subtraction) {
          if (leftT == "rune" && rightT == "int") {
            return "rune";
          }
          if (leftT == "rune" && rightT == "rune") {
            return "int";
          }
        }

        if (bin.Operator == OperatorType.Equal || bin.Operator == OperatorType.NotEqual ||
            bin.Operator == OperatorType.LessThan || bin.Operator == OperatorType.LessThanEqual ||
            bin.Operator == OperatorType.GreaterThan || bin.Operator == OperatorType.GreaterThanEqual ||
            bin.Operator == OperatorType.In) {
          return "bool";
        }

        return leftT;

      case TernaryExpression tern:
        return InferType(tern.Consequent);

      case MatchStatement match: {
        string condType = InferType(match.Condition);

        // Exhaustiveness analysis
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
          if (missing.Count > 0 && match.Alternate == null) {
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
            if (missing.Count > 0 && match.Alternate == null) {
              throw new Exception($"TYPE CHECK ERROR: Match statement on enum '{condType}' is not exhaustive. Missing member(s): {string.Join(", ", missing)}.");
            }
          }
        }

        // Branch type checking and inference
        var branchTypes = new List<string>();
        foreach (var branch in match.Branches) {
          string bType = "any";
          if (branch.Body is BlockStatement block) {
            var yields = FindYieldStatements(block);
            string expectedBranchYieldType = !string.IsNullOrEmpty(expectedType) ? expectedType : "";
            if (yields.Count > 0 && string.IsNullOrEmpty(expectedBranchYieldType)) {
              expectedBranchYieldType = InferType(yields[0].Value);
            }
            bType = !string.IsNullOrEmpty(expectedBranchYieldType) ? expectedBranchYieldType : "any";
          } else if (branch.Body is Expression branchBodyExpr) {
            bType = InferType(branchBodyExpr, expectedType);
          } else {
            bType = "any";
          }
          branchTypes.Add(bType);
        }

        if (match.Alternate != null) {
          string altType = "any";
          if (match.Alternate.Body is BlockStatement block) {
            var yields = FindYieldStatements(block);
            string expectedBranchYieldType = !string.IsNullOrEmpty(expectedType) ? expectedType : "";
            if (yields.Count > 0 && string.IsNullOrEmpty(expectedBranchYieldType)) {
              expectedBranchYieldType = InferType(yields[0].Value);
            }
            altType = !string.IsNullOrEmpty(expectedBranchYieldType) ? expectedBranchYieldType : "any";
          } else if (match.Alternate.Body is Expression altBodyExpr) {
            altType = InferType(altBodyExpr, expectedType);
          } else {
            altType = "any";
          }
          branchTypes.Add(altType);
        }

        // Unify branch types
        string unifiedType = "any";
        foreach (var t in branchTypes) {
          if (t != "any") {
            unifiedType = t;
            break;
          }
        }

        foreach (var t in branchTypes) {
          if (t != "any" && !IsCompatible(t, unifiedType) && !IsCompatible(unifiedType, t)) {
            throw new Exception($"TYPE CHECK ERROR: Match branches have incompatible types '{unifiedType}' and '{t}'.");
          }
        }

        return unifiedType;
      }

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
    srcType = NormalizeType(srcType);
    destType = NormalizeType(destType);
    if (destType == "any" || srcType == "any" || string.IsNullOrEmpty(destType)) {
      return true;
    }

    if (srcType == destType) {
      return true;
    }

    if (srcType.StartsWith("@(") && srcType.EndsWith(")") && destType.StartsWith("@(") && destType.EndsWith(")")) {
      if (TryParseTupleType(srcType, out var srcFields) && TryParseTupleType(destType, out var destFields)) {
        if (srcFields.Count != destFields.Count) return false;
        foreach (var destField in destFields) {
          int srcFieldIdx = srcFields.FindIndex(f => f.Label == destField.Label);
          if (srcFieldIdx == -1) return false;
          if (!IsCompatible(srcFields[srcFieldIdx].Type, destField.Type)) return false;
        }
        return true;
      }
    }

    if (srcType.Contains('_') && destType.Contains('_')) {
      var srcParts = srcType.Split('_');
      var destParts = destType.Split('_');
      if (srcParts[0] == destParts[0] && srcParts.Length == destParts.Length) {
        bool match = true;
        for (int i = 1; i < srcParts.Length; i++) {
          if (srcParts[i] != destParts[i] && srcParts[i] != "any" && destParts[i] != "any") {
            match = false;
            break;
          }
        }
        if (match) return true;
      }
    }

    if (srcType.StartsWith("[]") && destType.StartsWith("[]")) {
      return IsCompatible(srcType.Substring(2), destType.Substring(2));
    }

    if (srcType.StartsWith("map[") && destType.StartsWith("map[")) {
      var srcParts = SplitMapTypes(srcType);
      var destParts = SplitMapTypes(destType);
      if (srcParts != null && destParts != null) {
        return IsCompatible(srcParts.Item1, destParts.Item1) &&
               IsCompatible(srcParts.Item2, destParts.Item2);
      }
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
    ResolveStructMembers(structDecl.Identifier, out var allFields, out var allMethods);

    foreach (var reqField in interfaceDecl.Fields) {
      var implField = allFields.Find(f => f.Identifier == reqField.Identifier);
      if (implField == null) {
        return false;
      }
      if (!IsCompatible(implField.Typing, reqField.Typing)) {
        return false;
      }
    }

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
      string implRetType = InferFunctionReturnType(implMethod, structDecl.Identifier);
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

  private void SetupFunctionScope(FunctionDeclaration fn, string? parentStructName) {
    PushScope();
    bool isMethod = parentStructName != null && !fn.IsStatic;
    if (isMethod) {
      DeclareVariable("this", parentStructName!);
      DeclareVariable("self", parentStructName!);
      ResolveStructMembers(parentStructName!, out var fields, out var _);
      foreach (var field in fields) {
        DeclareVariable(field.Identifier, field.Typing);
      }
    }
    foreach (var param in fn.Parameters) {
      DeclareVariable(param.Identifier, string.IsNullOrEmpty(param.Typing) ? "any" : param.Typing);
    }
  }

  private string InferReturnStatementType(ReturnStatement ret, string expectedType, string? parentStructName, FunctionDeclaration fn) {
    if (ret.Argument == null) return "any";

    var oldStruct = _currentStruct;
    var oldStatic = _inStaticMethod;
    bool oldCheckingReturn = _isCheckingReturn;
    if (parentStructName != null) {
      _currentStruct = FindStruct(parentStructName);
      _inStaticMethod = fn.IsStatic;
    }
    _isCheckingReturn = true;
    try {
      return InferType(ret.Argument, expectedType);
    } finally {
      _currentStruct = oldStruct;
      _inStaticMethod = oldStatic;
      _isCheckingReturn = oldCheckingReturn;
    }
  }

  private string InferFunctionReturnType(FunctionDeclaration fn, string? parentStructName = null) {
    string fnKey = fn.Identifier ?? "<lambda>";

    // Cycle guard — handles recursion detection for both explicit and implicit return paths.
    if (_inferringFunctions.Contains(fnKey)) {
      if (!string.IsNullOrEmpty(fn.ReturnType)) {
        return fn.ReturnType;
      }
      throw new Exception(
        $"TYPE CHECK ERROR: Function '{fn.Identifier}' is recursive and requires an explicit return type annotation.\n" +
        $"  Hint: fn {fn.Identifier}({string.Join(" ", fn.Parameters.Select(p => p.Identifier + " " + p.Typing))}) <return_type> {{ ... }}"
      );
    }

    _inferringFunctions.Add(fnKey);
    
    // Temporarily pop scopes except the global/module level so local variables from parent functions do not interfere.
    var savedScopes = new List<Dictionary<string, string>>();
    while (_scopes.Count > 1) {
      savedScopes.Add(_scopes.Pop());
    }

    try {
      // Path A: Function has an explicit return type annotation
      if (!string.IsNullOrEmpty(fn.ReturnType)) {
        if (fn.Body == null) return fn.ReturnType;

        SetupFunctionScope(fn, parentStructName);
        var returns = FindReturnStatements(fn.Body);
        foreach (var ret in returns) {
          string retType = InferReturnStatementType(ret, fn.ReturnType, parentStructName, fn);
          if (!IsCompatible(retType, fn.ReturnType)) {
            throw new Exception($"TYPE CHECK ERROR: Function '{fn.Identifier}' declared return type '{fn.ReturnType}', but returned '{retType}'.");
          }
        }
        PopScope();
        return fn.ReturnType;
      }

      // Path B: Function has no return type annotation (infer return type from returns)
      if (fn.Body == null) return "any";

      SetupFunctionScope(fn, parentStructName);
      var returns2 = FindReturnStatements(fn.Body);
      if (returns2.Count == 0) {
        PopScope();
        return "any";
      }

      string inferredType = "any";
      bool first = true;
      foreach (var ret in returns2) {
        string retType = InferReturnStatementType(ret, "", parentStructName, fn);
        if (first) {
          inferredType = retType;
          first = false;
        } else {
          if (!IsCompatible(retType, inferredType) && !IsCompatible(inferredType, retType)) {
            throw new Exception($"TYPE CHECK ERROR: Function '{fn.Identifier}' has conflicting return types '{inferredType}' and '{retType}'.");
          }
        }
      }

      PopScope();
      return inferredType;
    } finally {
      // Restore saved scopes
      for (int i = savedScopes.Count - 1; i >= 0; i--) {
        _scopes.Push(savedScopes[i]);
      }
      _inferringFunctions.Remove(fnKey);
    }
  }

  private List<YieldStatement> FindYieldStatements(Statement stmt) {
    var list = new List<YieldStatement>();
    FindYieldStatementsRecursive(stmt, list);
    return list;
  }

  private void FindYieldStatementsRecursive(Statement stmt, List<YieldStatement> list) {
    if (stmt == null) return;
    if (stmt is YieldStatement y) {
      list.Add(y);
    } else if (stmt is BlockStatement block) {
      foreach (var s in block.Statements) {
        FindYieldStatementsRecursive(s, list);
      }
    } else if (stmt is IfStatement ifs) {
      FindYieldStatementsRecursive(ifs.Consequent, list);
      if (ifs.Alternate != null) {
        FindYieldStatementsRecursive(ifs.Alternate, list);
      }
    } else if (stmt is ElseStatement els) {
      FindYieldStatementsRecursive(els.Body, list);
    } else if (stmt is WhenStatement when) {
      FindYieldStatementsRecursive(when.Body, list);
    } else if (stmt is LoopStatement loop) {
      FindYieldStatementsRecursive(loop.Body, list);
    }
  }
}
