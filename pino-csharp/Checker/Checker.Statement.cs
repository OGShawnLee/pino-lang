using System;
using System.Collections.Generic;

namespace Pino;

public partial class Checker {
  private void CheckStatement(Statement statement) {
    switch (statement) {
      case VariableDeclaration varDecl:
        if (varDecl.Kind == VariableKind.Constant || varDecl.Kind == VariableKind.Variable) {
          string valType = varDecl.Value != null ? InferType(varDecl.Value) : "any";
          string expectedType = NormalizeType(varDecl.Typing);

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

        if (fnDecl.GenericParams != null && fnDecl.GenericParams.Count > 0) {
          foreach (var param in fnDecl.GenericParams) {
            if (FindStruct(param.Name) != null || FindInterface(param.Name) != null || FindEnum(param.Name) != null || IsPrimitiveType(param.Name)) {
              throw new Exception($"TYPE CHECK ERROR: Generic parameter '{param.Name}' in function '{fnDecl.Identifier}' conflicts with an existing defined type name.");
            }
          }
          break;
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
        PushScope();
        foreach (var param in fnDecl.Parameters) {
          DeclareVariable(param.Identifier, param.Typing);
        }
        if (fnDecl.Body != null) {
          CheckStatement(fnDecl.Body);
        }
        PopScope();
        if (isMethod) {
          PopScope();
        }
        InferFunctionReturnType(fnDecl, isMethod ? _currentStruct!.Identifier : null);
        break;

      case StructDeclaration structDecl:
        if (structDecl.GenericParams != null && structDecl.GenericParams.Count > 0) {
          foreach (var param in structDecl.GenericParams) {
            if (FindStruct(param.Name) != null || FindInterface(param.Name) != null || FindEnum(param.Name) != null || IsPrimitiveType(param.Name)) {
              throw new Exception($"TYPE CHECK ERROR: Generic parameter '{param.Name}' in struct '{structDecl.Identifier}' conflicts with an existing defined type name.");
            }
          }
          break;
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
          CheckStatement(method);
        }
        _currentStruct = oldStruct;
        _inStaticMethod = oldStatic;
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
    return name == "bool" || name == "int" || name == "float" || name == "string" || name == "rune" || name == "any";
  }
}
