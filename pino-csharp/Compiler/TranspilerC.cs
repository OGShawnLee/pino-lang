using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Pino;

public class TranspilerC {
    private StringBuilder _sb = new StringBuilder();
    private int _indent = 0;

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
        // Output standard headers
        _sb.AppendLine("#include <stdio.h>");
        _sb.AppendLine("#include \"runtime/runtime.h\"");
        _sb.AppendLine();

        var declarations = new List<Declaration>();
        var topLevelStatements = new List<Statement>();

        foreach (var stmt in program.Statements) {
            if (stmt is Declaration decl) {
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
                Write("return");
                if (ret.Argument != null) {
                    Write(" ");
                    TranspileExpression(ret.Argument);
                }
                _sb.AppendLine(";");
                break;

            case Expression expr: // Since Expression inherits from Statement in AST.cs
                WriteIndent();
                TranspileExpression(expr);
                _sb.AppendLine(";");
                break;

            default:
                throw new NotImplementedException($"Statement type {stmt.GetType().Name} not implemented in Increment 1 Transpiler.");
        }
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
                    Write("pino_println_string(");
                    if (call.Arguments.Count > 0) {
                        TranspileExpression(call.Arguments[0]);
                    } else {
                        Write("\"\"");
                    }
                    Write(")");
                } else {
                    Write($"{call.Callee}(");
                    for (int i = 0; i < call.Arguments.Count; i++) {
                        if (i > 0) Write(", ");
                        TranspileExpression(call.Arguments[i]);
                    }
                    Write(")");
                }
                break;

            default:
                throw new NotImplementedException($"Expression type {expr.GetType().Name} not implemented in Increment 1 Transpiler.");
        }
    }

    private string EscapeString(string value) {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
