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
            var accessInterfaceDecl = FindInterface(leftType);
            if (accessStructDecl != null) {
              ResolveStructMembers(leftType, out var allFields, out var allMethods);
              if (bin.Right is FunctionCallExpression methodCall) {
                var method = allMethods.Find(m => m.Identifier == methodCall.Callee);
                if (method != null) {
                  if (method.IsStatic) {
                    throw new Exception($"TYPE CHECK ERROR: Cannot call static method '{method.Identifier}' of struct '{accessStructDecl.Identifier}' on an instance.");
                  }
                  
                  if (method.GenericParams != null && method.GenericParams.Count > 0) {
                    foreach (var arg in methodCall.Arguments) {
                      CheckExpression(arg);
                    }
                    MonomorphizeMethodCall(accessStructDecl, methodCall);
                    method = accessStructDecl.Methods.Find(m => m.Identifier == methodCall.Callee) ?? method;
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
                  var field = allFields.Find(f => f.Identifier == methodCall.Callee);
                  if (field != null && field.Typing.StartsWith("fn(")) {
                    if (ParseFunctionSignature(field.Typing, out var paramsList, out var returnType)) {
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
              } else if (bin.Right is IdentifierExpression propId) {
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
              if (bin.Right is FunctionCallExpression methodCall) {
                var method = accessInterfaceDecl.Methods.Find(m => m.Identifier == methodCall.Callee);
                if (method != null) {
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
              } else if (bin.Right is IdentifierExpression propId) {
                var field = accessInterfaceDecl.Fields.Find(f => f.Identifier == propId.Name);
                var method = accessInterfaceDecl.Methods.Find(m => m.Identifier == propId.Name);
                if (field == null && method == null) {
                  throw new Exception($"TYPE CHECK ERROR: Interface '{accessInterfaceDecl.Identifier}' does not have property or method '{propId.Name}'.");
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
                  
                  if (method.GenericParams != null && method.GenericParams.Count > 0) {
                    foreach (var arg in methodCall.Arguments) {
                      CheckExpression(arg);
                    }
                    MonomorphizeMethodCall(staticAccessStructDecl, methodCall);
                    method = staticAccessStructDecl.Methods.Find(m => m.Identifier == methodCall.Callee) ?? method;
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
        string monomorphizedName = MonomorphizeStructInstance(inst);
        var structDecl = FindStruct(monomorphizedName);
        if (structDecl == null) {
          throw new Exception($"TYPE CHECK ERROR: Struct '{monomorphizedName}' is not defined.");
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
        if (_functions.TryGetValue(call.Callee, out var genericFn) && genericFn.GenericParams != null && genericFn.GenericParams.Count > 0) {
          foreach (var arg in call.Arguments) {
            CheckExpression(arg);
          }
          MonomorphizeFunctionCall(call);
        } else {
          foreach (var arg in call.Arguments) {
            CheckExpression(arg);
          }
        }

        Resolve(call, call.Callee);
        var argTypes = call.Arguments.Select(InferType).ToList();

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

    try {
      InferType(expr);
    } catch {
      // Ignore failures during early or partial check passes
    }
  }

  private string InferType(Expression expr) {
    string type = InferTypeInternal(expr);
    expr.InferredType = type;
    return type;
  }

  private string InferTypeInternal(Expression expr) {
    switch (expr) {
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

  private string InferFunctionReturnType(FunctionDeclaration fn, string? parentStructName = null) {
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

        var returns = FindReturnStatements(fn.Body);
        foreach (var ret in returns) {
          var oldStruct = _currentStruct;
          var oldStatic = _inStaticMethod;
          if (parentStructName != null) {
            _currentStruct = FindStruct(parentStructName);
            _inStaticMethod = fn.IsStatic;
          }
          string retType = ret.Argument != null ? InferType(ret.Argument) : "any";
          _currentStruct = oldStruct;
          _inStaticMethod = oldStatic;

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
      bool isMethod2 = parentStructName != null && !fn.IsStatic;
      if (isMethod2) {
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

      var returns2 = FindReturnStatements(fn.Body);
      if (returns2.Count == 0) {
        PopScope();
        return "any";
      }

      string firstRetType = "any";
      bool first = true;
      foreach (var ret in returns2) {
        var oldStruct = _currentStruct;
        var oldStatic = _inStaticMethod;
        if (parentStructName != null) {
          _currentStruct = FindStruct(parentStructName);
          _inStaticMethod = fn.IsStatic;
        }
        string retType = ret.Argument != null ? InferType(ret.Argument) : "any";
        _currentStruct = oldStruct;
        _inStaticMethod = oldStatic;

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
}
