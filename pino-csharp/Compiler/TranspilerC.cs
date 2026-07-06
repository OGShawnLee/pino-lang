using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Pino;

public class TranspilerC {
    private StringBuilder _sb = new StringBuilder();
    private int _indent = 0;
    private Dictionary<string, string> _varTypes = new Dictionary<string, string>();

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
        _varTypes.Clear();
        // Output standard headers
        _sb.AppendLine("#include <stdio.h>");
        _sb.AppendLine("#include \"runtime/runtime.h\"");
        _sb.AppendLine();

        var declarations = new List<Declaration>();
        var topLevelStatements = new List<Statement>();

        foreach (var stmt in program.Statements) {
            if (stmt is Declaration decl && !(decl is VariableDeclaration)) {
                declarations.Add(decl);
            } else if (stmt is ModuleDeclaration || stmt is ImportStatement || stmt is FromImportStatement) {
                // Ignore module imports for Increment 1
            } else {
                topLevelStatements.Add(stmt);
            }
        }

        // Pass 1: Forward declarations of functions
        foreach (var decl in declarations) {
            if (decl is FunctionDeclaration fnDecl && fnDecl.Identifier != "main") {
                DeclareFunction(fnDecl);
            }
        }
        _sb.AppendLine();

        // Pass 2: Implementation of function declarations
        foreach (var decl in declarations) {
            if (decl is FunctionDeclaration fnDecl && fnDecl.Identifier != "main") {
                TranspileFunction(fnDecl);
            }
        }

        // Pass 3: The C main function carrying the top-level statements and user main call
        _sb.AppendLine("int main(int argc, char** argv) {");
        _indent = 1;

        foreach (var stmt in topLevelStatements) {
            TranspileStatement(stmt);
        }

        // If there was an explicit fn main declaration, inline its body or call it
        var userMain = declarations.FirstOrDefault(d => d is FunctionDeclaration fn && fn.Identifier == "main") as FunctionDeclaration;
        if (userMain != null && userMain.Body != null) {
            TranspileStatement(userMain.Body);
        }

        WriteLine("return 0;");
        _indent = 0;
        _sb.AppendLine("}");

        return _sb.ToString();
    }

    private void DeclareFunction(FunctionDeclaration fnDecl) {
        var returnType = MapType(fnDecl.ReturnType);
        var parameters = string.Join(", ", fnDecl.Parameters.Select(p => $"{MapType(p.Typing)} {p.Identifier}"));
        if (string.IsNullOrEmpty(parameters)) parameters = "void";
        _sb.AppendLine($"{returnType} {fnDecl.Identifier}({parameters});");
    }

    private void TranspileFunction(FunctionDeclaration fnDecl) {
        var returnType = MapType(fnDecl.ReturnType);
        var identifier = fnDecl.Identifier;

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
    }

    private string MapType(string pinoType) {
        return pinoType switch {
            "int" => "int",
            "float" => "double",
            "bool" => "int",
            "string" => "const char*",
            _ => "void"
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
                Write(id.Name);
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

    private string EscapeString(string value) {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
