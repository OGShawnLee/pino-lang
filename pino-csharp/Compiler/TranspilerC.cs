using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Pino;

public class TranspilerC {
    private StringBuilder _sb = new StringBuilder();
    private int _indent = 0;
    private Dictionary<string, string> _varTypes = new Dictionary<string, string>();
    private Dictionary<string, List<string>> _structFields = new Dictionary<string, List<string>>();
    private HashSet<string> _currentStructFields = new HashSet<string>();
    private StringBuilder _tupleSb = new StringBuilder();
    private HashSet<string> _declaredTuples = new HashSet<string>();
    private string _currentReturnType = "";

    private void WriteIndent() {
        _sb.Append(new string(' ', _indent * 4));
    }

    private void WriteLine(string text) {
        WriteIndent();
        _sb.AppendLine(text);
    }

    private void Write(string text) {
        _sb.Append(text);
    }

    public string Transpile(ProgramStatement program) {
        _currentReturnType = "";
        _varTypes.Clear();
        _structFields.Clear();
        _currentStructFields.Clear();
        _tupleSb.Clear();
        _declaredTuples.Clear();
        _sb.Clear();

        var declarations = new List<Declaration>();
        var topLevelStatements = new List<Statement>();

        foreach (var stmt in program.Statements) {
            if (stmt is Declaration decl && !(decl is VariableDeclaration)) {
                declarations.Add(decl);
            } else if (stmt is ModuleDeclaration || stmt is ImportStatement || stmt is FromImportStatement) {
                // Ignore module imports
            } else {
                topLevelStatements.Add(stmt);
            }
        }

        // Pass 0: Register all structs and populate their fields dictionary (needed for method translation)
        foreach (var decl in declarations) {
            if (decl is StructDeclaration structDecl) {
                var fields = structDecl.Fields.Select(f => f.Identifier).ToList();
                _structFields[structDecl.Identifier] = fields;
            }
        }

        // Pass 1: Forward declarations of structs, struct methods, and functions
        var structSb = new StringBuilder();
        foreach (var decl in declarations) {
            if (decl is StructDeclaration structDecl) {
                // Compile struct typedef
                structSb.AppendLine($"typedef struct {structDecl.Identifier} {structDecl.Identifier};");
                structSb.AppendLine($"struct {structDecl.Identifier} {{");
                foreach (var field in structDecl.Fields) {
                    structSb.AppendLine($"    {MapType(field.Typing)} {field.Identifier};");
                }
                structSb.AppendLine("};");
                structSb.AppendLine();

                // Forward declare instance methods
                foreach (var method in structDecl.Methods) {
                    var retType = MapType(method.ReturnType);
                    var methodParams = $"struct {structDecl.Identifier}* this";
                    if (method.Parameters.Count > 0) {
                        methodParams += ", " + string.Join(", ", method.Parameters.Select(p => $"{MapType(p.Typing)} {p.Identifier}"));
                    }
                    structSb.AppendLine($"{retType} {structDecl.Identifier}_{method.Identifier}({methodParams});");
                }
                structSb.AppendLine();
            } else if (decl is FunctionDeclaration fnDecl && fnDecl.Identifier != "main") {
                var returnType = MapType(fnDecl.ReturnType);
                var parameters = string.Join(", ", fnDecl.Parameters.Select(p => $"{MapType(p.Typing)} {p.Identifier}"));
                if (string.IsNullOrEmpty(parameters)) parameters = "void";
                _sb.AppendLine($"{returnType} {fnDecl.Identifier}({parameters});");
            }
        }
        _sb.AppendLine();

        // Pass 2: Implementation of struct methods and function declarations
        foreach (var decl in declarations) {
            if (decl is StructDeclaration structDecl) {
                foreach (var method in structDecl.Methods) {
                    TranspileStructMethod(structDecl.Identifier, method);
                }
            } else if (decl is FunctionDeclaration fnDecl && fnDecl.Identifier != "main") {
                TranspileFunction(fnDecl);
            }
        }

        // Pass 3: The C main function carrying the top-level statements and user main call
        _sb.AppendLine("int main(int argc, char** argv) {");
        _indent = 1;

        foreach (var stmt in topLevelStatements) {
            TranspileStatement(stmt);
        }

        var userMain = declarations.FirstOrDefault(d => d is FunctionDeclaration fn && fn.Identifier == "main") as FunctionDeclaration;
        if (userMain != null && userMain.Body != null) {
            TranspileStatement(userMain.Body);
        }

        WriteLine("return 0;");
        _indent = 0;
        _sb.AppendLine("}");

        // Combine everything: Headers + Typedef Structs + Tuple Structs + Main Code
        var finalSb = new StringBuilder();
        finalSb.AppendLine("#include <stdio.h>");
        finalSb.AppendLine("#include \"runtime/runtime.h\"");
        finalSb.AppendLine();
        finalSb.Append(structSb.ToString());
        finalSb.Append(_tupleSb.ToString());
        finalSb.Append(_sb.ToString());

        return finalSb.ToString();
    }

    private void TranspileFunction(FunctionDeclaration fnDecl) {
        var returnType = MapType(fnDecl.ReturnType);
        var identifier = fnDecl.Identifier;

        _currentReturnType = fnDecl.ReturnType;
        _varTypes.Clear();
        foreach (var param in fnDecl.Parameters) {
            _varTypes[param.Identifier] = param.Typing;
        }

        var parameters = string.Join(", ", fnDecl.Parameters.Select(p => $"{MapType(p.Typing)} {p.Identifier}"));
        if (string.IsNullOrEmpty(parameters)) parameters = "void";

        WriteLine($"{returnType} {identifier}({parameters}) {{");
        _indent++;

        if (fnDecl.Body != null) {
            TranspileStatement(fnDecl.Body);
        }

        _indent--;
        WriteLine("}");
        _sb.AppendLine();
        _currentReturnType = "";
    }

    private void TranspileStructMethod(string structName, FunctionDeclaration fnDecl) {
        var returnType = MapType(fnDecl.ReturnType);
        var identifier = $"{structName}_{fnDecl.Identifier}";

        _currentReturnType = fnDecl.ReturnType;
        // Setup method environment
        _varTypes.Clear();
        _currentStructFields.Clear();
        
        // Add fields to current struct fields
        if (_structFields.TryGetValue(structName, out var fields)) {
            foreach (var f in fields) {
                _currentStructFields.Add(f);
            }
        }

        // Add parameters to _varTypes
        _varTypes["this"] = structName;
        _varTypes["self"] = structName;
        foreach (var param in fnDecl.Parameters) {
            _varTypes[param.Identifier] = param.Typing;
        }

        var parameters = $"struct {structName}* this";
        if (fnDecl.Parameters.Count > 0) {
            parameters += ", " + string.Join(", ", fnDecl.Parameters.Select(p => $"{MapType(p.Typing)} {p.Identifier}"));
        }

        WriteLine($"{returnType} {identifier}({parameters}) {{");
        _indent++;

        if (fnDecl.Body != null) {
            TranspileStatement(fnDecl.Body);
        }

        _indent--;
        WriteLine("}");
        _sb.AppendLine();
        
        _currentStructFields.Clear();
        _currentReturnType = "";
    }

    private Dictionary<string, string> ParseTupleFields(string tupleType) {
        var fields = new Dictionary<string, string>();
        var content = tupleType.Substring(2, tupleType.Length - 3);
        var parts = content.Split(',');
        foreach (var part in parts) {
            var subparts = part.Split(':');
            if (subparts.Length == 2) {
                fields[subparts[0].Trim()] = subparts[1].Trim();
            }
        }
        return fields;
    }

    private string MapType(string pinoType) {
        if (string.IsNullOrEmpty(pinoType)) return "void";
        if (pinoType.StartsWith("@(")) {
            var clean = pinoType.Replace("@", "tuple").Replace("(", "").Replace(")", "").Replace(":", "_").Replace(",", "_");
            if (!_declaredTuples.Contains(pinoType)) {
                _declaredTuples.Add(pinoType);
                _tupleSb.AppendLine($"struct {clean} {{");
                var fields = ParseTupleFields(pinoType);
                foreach (var kvp in fields) {
                    _tupleSb.AppendLine($"    {MapType(kvp.Value)} {kvp.Key};");
                }
                _tupleSb.AppendLine("};");
                _tupleSb.AppendLine();
            }
            return "struct " + clean;
        }
        return pinoType switch {
            "int" => "int",
            "float" => "double",
            "bool" => "int",
            "string" => "const char*",
            "void" => "void",
            _ => pinoType
        };
    }

    private void TranspileStatement(Statement stmt) {
        switch (stmt) {
            case BlockStatement block:
                foreach (var child in block.Statements) {
                    TranspileStatement(child);
                }
                break;

            case ReturnStatement ret:
                WriteIndent();
                if (ret.Argument != null && IsStringConcat(ret.Argument)) {
                    Write("char* pino_ret_temp = (char*)pino_malloc(1024);\n");
                    var (format, args) = ProcessStringAddition(ret.Argument);
                    var argsStr = args.Count > 0 ? ", " + string.Join(", ", args) : "";
                    WriteIndent();
                    Write($"snprintf(pino_ret_temp, 1024, \"{EscapeString(format)}\"{argsStr});\n");
                    WriteIndent();
                    Write("return pino_ret_temp;");
                } else {
                    Write("return");
                    if (ret.Argument != null) {
                        Write(" ");
                        TranspileExpression(ret.Argument);
                    }
                }
                _sb.AppendLine(";");
                break;

            case VariableDeclaration varDecl:
                WriteIndent();
                TranspileVariableDeclaration(varDecl);
                _sb.AppendLine(";");
                break;

            case TupleDestructuringDeclaration dest:
                {
                    var tupleType = dest.Value.InferredType!;
                    var cStructType = MapType(tupleType);
                    var tempVar = $"_pino_tup_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                    
                    WriteIndent();
                    Write($"{cStructType} {tempVar} = ");
                    TranspileExpression(dest.Value);
                    _sb.AppendLine(";");

                    var fieldTypes = ParseTupleFields(tupleType);
                    foreach (var field in dest.Fields) {
                        var varType = fieldTypes[field.Label];
                        var isConst = dest.Kind == VariableKind.Constant;
                        var prefix = isConst ? "const " : "";
                        
                        WriteIndent();
                        Write($"{prefix}{MapType(varType)} {field.Identifier} = {tempVar}.{field.Label};\n");
                        _varTypes[field.Identifier] = varType;
                    }
                }
                break;

            case IfStatement ifs:
                TranspileIf(ifs, false);
                _sb.AppendLine();
                break;

            case LoopStatement loop:
                TranspileLoop(loop);
                break;

            case Expression expr: // Since Expression inherits from Statement in AST.cs
                WriteIndent();
                TranspileExpression(expr);
                _sb.AppendLine(";");
                break;

            default:
                throw new NotImplementedException($"Statement type {stmt.GetType().Name} not implemented in Transpiler.");
        }
    }

    private void TranspileVariableDeclaration(VariableDeclaration varDecl) {
        var isConst = varDecl.Kind == VariableKind.Constant;
        var prefix = isConst ? "const " : "";
        
        string typeStr = "void";
        if (!string.IsNullOrEmpty(varDecl.Typing)) {
            typeStr = MapType(varDecl.Typing);
        } else if (varDecl.Value != null && !string.IsNullOrEmpty(varDecl.Value.InferredType)) {
            typeStr = MapType(varDecl.Value.InferredType);
        }

        if (varDecl.Value != null && IsStringConcat(varDecl.Value)) {
            // String interpolation or addition declaration
            Write($"{typeStr} {varDecl.Identifier} = (char*)pino_malloc(1024);\n");
            var (format, args) = ProcessStringAddition(varDecl.Value);
            var argsStr = args.Count > 0 ? ", " + string.Join(", ", args) : "";
            WriteIndent();
            Write($"snprintf((char*){varDecl.Identifier}, 1024, \"{EscapeString(format)}\"{argsStr})");
        } else {
            // Normal declaration
            Write($"{prefix}{typeStr} {varDecl.Identifier}");
            if (varDecl.Value != null) {
                Write(" = ");
                TranspileExpression(varDecl.Value);
            }
        }

        // Store type in _varTypes
        string pinoType = "";
        if (!string.IsNullOrEmpty(varDecl.Typing)) pinoType = varDecl.Typing;
        else if (varDecl.Value != null && !string.IsNullOrEmpty(varDecl.Value.InferredType)) pinoType = varDecl.Value.InferredType;
        _varTypes[varDecl.Identifier] = pinoType;
    }

    private void TranspileExpression(Expression expr) {
        switch (expr) {
            case LiteralExpression lit:
                if (lit.LiteralType == LiteralType.String) {
                    Write($"\"{EscapeString(lit.Value)}\"");
                } else {
                    Write(lit.Value);
                }
                break;

            case FunctionCallExpression call:
                if (call.Callee == "println") {
                    if (call.Arguments.Count > 0) {
                        var arg = call.Arguments[0];
                        if (IsStringConcat(arg)) {
                            var (format, args) = ProcessStringAddition(arg);
                            var argsStr = args.Count > 0 ? ", " + string.Join(", ", args) : "";
                            Write($"printf(\"{EscapeString(format)}\\n\"{argsStr})");
                        } else {
                            var type = arg.InferredType;
                            if (type == "int") {
                                Write("pino_println_int(");
                                TranspileExpression(arg);
                                Write(")");
                            } else if (type == "float") {
                                Write("pino_println_float(");
                                TranspileExpression(arg);
                                Write(")");
                            } else {
                                Write("pino_println_string(");
                                TranspileExpression(arg);
                                Write(")");
                            }
                        }
                    } else {
                        Write("pino_println_string(\"\")");
                    }
                } else {
                    Write($"{call.Callee}(");
                    for (int i = 0; i < call.Arguments.Count; i++) {
                        if (i > 0) Write(", ");
                        TranspileExpression(call.Arguments[i]);
                    }
                    Write(")");
                }
                break;

            case BinaryExpression bin:
                if (bin.Operator == OperatorType.Assignment && IsStringConcat(bin.Right)) {
                    var (format, args) = ProcessStringAddition(bin.Right);
                    var argsStr = args.Count > 0 ? ", " + string.Join(", ", args) : "";
                    Write("snprintf(");
                    TranspileExpression(bin.Left);
                    Write($", 1024, \"{EscapeString(format)}\"{argsStr})");
                } else if (bin.Operator == OperatorType.Addition && bin.InferredType == "string") {
                    var (format, args) = ProcessStringAddition(bin);
                    var argsStr = args.Count > 0 ? ", " + string.Join(", ", args) : "";
                    Write("({ char* temp = (char*)pino_malloc(1024); ");
                    Write($"snprintf(temp, 1024, \"{EscapeString(format)}\"{argsStr}); temp; }})");
                } else if (bin.Operator == OperatorType.MemberAccess) {
                    if (bin.Right is FunctionCallExpression call) {
                        var structName = bin.Left.InferredType!;
                        Write($"{structName}_{call.Callee}(");
                        
                        if (bin.Left is IdentifierExpression id && (id.Name == "this" || id.Name == "self")) {
                            Write("this");
                        } else {
                            Write("&");
                            TranspileExpression(bin.Left);
                        }

                        for (int i = 0; i < call.Arguments.Count; i++) {
                            Write(", ");
                            TranspileExpression(call.Arguments[i]);
                        }
                        Write(")");
                    } else {
                        TranspileExpression(bin.Left);
                        if (bin.Left is IdentifierExpression id && (id.Name == "this" || id.Name == "self")) {
                            Write("->");
                        } else {
                            Write(".");
                        }
                        
                        if (bin.Right is IdentifierExpression rightId) {
                            Write(rightId.Name);
                        } else {
                            TranspileExpression(bin.Right);
                        }
                    }
                } else {
                    Write("(");
                    TranspileExpression(bin.Left);
                    Write($" {MapOperator(bin.Operator)} ");
                    TranspileExpression(bin.Right);
                    Write(")");
                }
                break;

            case UnaryExpression unary:
                Write(MapUnaryOperator(unary.Operator));
                Write("(");
                TranspileExpression(unary.Right);
                Write(")");
                break;

            case IdentifierExpression id:
                if (_currentStructFields.Contains(id.Name) && !_varTypes.ContainsKey(id.Name)) {
                    Write($"this->{id.Name}");
                } else {
                    Write(id.Name);
                }
                break;

            case TernaryExpression tern:
                Write("(");
                TranspileExpression(tern.Condition);
                Write(" ? ");
                TranspileExpression(tern.Consequent);
                Write(" : ");
                TranspileExpression(tern.Alternate);
                Write(")");
                break;

            case StructInstanceExpression inst:
                Write($"({inst.StructName}){{ ");
                for (int i = 0; i < inst.Properties.Count; i++) {
                    if (i > 0) Write(", ");
                    var prop = inst.Properties[i];
                    Write($".{prop.Identifier} = ");
                    TranspileExpression(prop.Value!);
                }
                Write(" }");
                break;

            case TupleLiteralExpression tuple:
                {
                    var tupleType = (!string.IsNullOrEmpty(_currentReturnType) && _currentReturnType.StartsWith("@("))
                        ? _currentReturnType
                        : tuple.InferredType!;
                    var structType = MapType(tupleType);
                    Write($"({structType}){{ ");
                    for (int i = 0; i < tuple.Fields.Count; i++) {
                        if (i > 0) Write(", ");
                        var field = tuple.Fields[i];
                        Write($".{field.Label} = ");
                        TranspileExpression(field.Value);
                    }
                    Write(" }");
                }
                break;

            default:
                throw new NotImplementedException($"Expression type {expr.GetType().Name} not implemented in Transpiler.");
        }
    }

    private bool IsStringConcat(Expression expr) {
        return expr.InferredType == "string" && (
            (expr is BinaryExpression bin && bin.Operator == OperatorType.Addition) ||
            (expr is LiteralExpression lit && lit.Injections != null)
        );
    }

    private void FlattenStringAddition(Expression expr, List<Expression> operands) {
        if (expr is BinaryExpression bin && bin.Operator == OperatorType.Addition &&
            (bin.Left.InferredType == "string" || bin.Right.InferredType == "string")) {
            FlattenStringAddition(bin.Left, operands);
            FlattenStringAddition(bin.Right, operands);
        } else {
            operands.Add(expr);
        }
    }

    private (string formatStr, List<string> args) ProcessStringAddition(Expression expr) {
        var operands = new List<Expression>();
        FlattenStringAddition(expr, operands);

        var formatSb = new StringBuilder();
        var args = new List<string>();

        foreach (var op in operands) {
            if (op is LiteralExpression lit && lit.LiteralType == LiteralType.String) {
                var escapedText = lit.Value.Replace("%", "%%");
                formatSb.Append(escapedText);
            } else {
                var type = op.InferredType;
                var specifier = type switch {
                    "int" => "%d",
                    "float" => "%g",
                    "bool" => "%d",
                    "string" => "%s",
                    _ => "%s"
                };
                formatSb.Append(specifier);
                
                var oldSb = _sb;
                _sb = new StringBuilder();
                TranspileExpression(op);
                args.Add(_sb.ToString());
                _sb = oldSb;
            }
        }

        return (formatSb.ToString(), args);
    }

    private string MapOperator(OperatorType op) {
        return op switch {
            OperatorType.Addition => "+",
            OperatorType.Subtraction => "-",
            OperatorType.Multiplication => "*",
            OperatorType.Division => "/",
            OperatorType.Modulus => "%",
            OperatorType.Assignment => "=",
            OperatorType.AdditionAssignment => "+=",
            OperatorType.SubtractionAssignment => "-=",
            OperatorType.MultiplicationAssignment => "*=",
            OperatorType.DivisionAssignment => "/=",
            OperatorType.ModulusAssignment => "%=",
            OperatorType.LessThan => "<",
            OperatorType.LessThanEqual => "<=",
            OperatorType.GreaterThan => ">",
            OperatorType.GreaterThanEqual => ">=",
            OperatorType.Equal => "==",
            OperatorType.NotEqual => "!=",
            OperatorType.And => "&&",
            OperatorType.Or => "||",
            _ => throw new NotImplementedException($"Operator {op} not implemented in Transpiler.")
        };
    }

    private string MapUnaryOperator(OperatorType op) {
        return op switch {
            OperatorType.Subtraction => "-",
            OperatorType.Not => "!",
            _ => throw new NotImplementedException($"Unary operator {op} not implemented in Transpiler.")
        };
    }

    private void TranspileIf(IfStatement ifs, bool isElseIf) {
        if (!isElseIf) {
            WriteIndent();
        }
        Write("if (");
        TranspileExpression(ifs.Condition);
        Write(") ");
        TranspileBlockOrStatement(ifs.Consequent);

        if (ifs.Alternate != null) {
            Write(" else ");
            if (ifs.Alternate is IfStatement innerIf) {
                TranspileIf(innerIf, true);
            } else {
                TranspileBlockOrStatement(ifs.Alternate);
            }
        }
    }

    private void TranspileBlockOrStatement(Statement stmt) {
        if (stmt is BlockStatement block) {
            Write("{\n");
            _indent++;
            foreach (var child in block.Statements) {
                TranspileStatement(child);
            }
            _indent--;
            WriteIndent();
            Write("}");
        } else {
            Write("{\n");
            _indent++;
            TranspileStatement(stmt);
            _indent--;
            WriteIndent();
            Write("}");
        }
    }

    private void TranspileLoop(LoopStatement loop) {
        switch (loop.Kind) {
            case LoopKind.Infinite:
                WriteIndent();
                Write("while (1) ");
                TranspileBlockOrStatement(loop.Body);
                _sb.AppendLine();
                break;

            case LoopKind.While:
                WriteIndent();
                Write("while (");
                TranspileExpression(loop.Begin!);
                Write(") ");
                TranspileBlockOrStatement(loop.Body);
                _sb.AppendLine();
                break;

            case LoopKind.ForTimes:
                WriteIndent();
                var limitVar = $"_pino_limit_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                Write($"int {limitVar} = ");
                TranspileExpression(loop.Begin!);
                Write(";\n");
                
                WriteIndent();
                Write($"for (int it = 0; it < {limitVar}; it++) ");
                
                bool hadIt = _varTypes.TryGetValue("it", out var oldIt);
                _varTypes["it"] = "int";
                
                TranspileBlockOrStatement(loop.Body);
                
                if (hadIt && oldIt != null) _varTypes["it"] = oldIt;
                else _varTypes.Remove("it");
                
                _sb.AppendLine();
                break;

            case LoopKind.ForIn:
                throw new NotImplementedException("ForIn collection loop is not implemented in Increment 3.");
        }
    }

    private string EscapeString(string value) {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
