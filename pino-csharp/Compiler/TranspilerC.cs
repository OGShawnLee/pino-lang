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
    private Dictionary<string, UnionDeclaration> _unions = new Dictionary<string, UnionDeclaration>();
    private Dictionary<string, EnumDeclaration> _enums = new Dictionary<string, EnumDeclaration>();
    private Stack<string> _matchResultVars = new Stack<string>();
    private Dictionary<string, string> _globalVarTypes = new Dictionary<string, string>();
    private StringBuilder _globalDeclSb = new StringBuilder();
    private bool _isGlobalScope = false;
    private int _blockDepth = 0;
    private bool _usesReadFile = false;
    private bool _usesWriteFile = false;
    private bool _usesFileExists = false;
    private string _currentFilePath = "";
    private Checker? _checker;
    private string? _currentModuleName;
    private readonly Dictionary<string, HashSet<string>> _moduleFunctions = new Dictionary<string, HashSet<string>>();
    private Dictionary<string, string> _importedSymbols = new Dictionary<string, string>();
    private Dictionary<string, Checker> _allModuleCheckers = new Dictionary<string, Checker>();
    private readonly StringBuilder _forwardDeclSb = new StringBuilder();
    private readonly StringBuilder _structSb = new StringBuilder();
    private HashSet<string> _emittedStructsAndUnions = new HashSet<string>();
    private HashSet<string> _emittedConstructors = new HashSet<string>();
    private Dictionary<string, bool> _definedTypes = new Dictionary<string, bool>();

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

    private string GetPrefixedName(string name) {
        if (name == "IOError" || name == "Result" || name.StartsWith("Result_") || name.StartsWith("IOError_")) {
            return ResolveTypeName(name);
        }
        if (!string.IsNullOrEmpty(_currentModuleName)) {
            return $"{_currentModuleName}_{name}";
        }
        return name;
    }

    private string GetConstPrefix(bool isConst, string typeStr) {
        if (!isConst) return "";
        if (typeStr.StartsWith("const ")) return "";
        if (typeStr == "int" || typeStr == "double" || typeStr == "bool" || typeStr == "void") {
            return "const ";
        }
        return "";
    }

    private void CollectModules(Checker checker, List<(string Name, Checker Checker)> list, HashSet<string> visited) {
        foreach (var kvp in checker._moduleCheckers) {
            if (visited.Add(kvp.Key)) {
                CollectModules(kvp.Value, list, visited);
                list.Add((kvp.Key, kvp.Value));
            }
        }
    }

    private void TranspilePass0And1(ProgramStatement program) {
        var declarations = new List<Declaration>();
        var topLevelStatements = new List<Statement>();

        var modKey = _currentModuleName ?? "";
        if (!_moduleFunctions.ContainsKey(modKey)) {
            _moduleFunctions[modKey] = new HashSet<string>();
        }
        foreach (var stmt in program.Statements) {
            if (stmt is FunctionDeclaration fn) {
                _moduleFunctions[modKey].Add(fn.Identifier);
            }
        }

        foreach (var stmt in program.Statements) {
            if (stmt is FromImportStatement fromImp) {
                foreach (var name in fromImp.Imports) {
                    _importedSymbols[$"{name}"] = $"{fromImp.ModuleName}_{name}";
                }
            }
        }

        foreach (var stmt in program.Statements) {
            if (stmt is Declaration decl && !(decl is VariableDeclaration)) {
                declarations.Add(decl);
            } else if (stmt is ModuleDeclaration || stmt is ImportStatement || stmt is FromImportStatement) {
                // Ignore
            } else {
                topLevelStatements.Add(stmt);
            }
        }

        foreach (var decl in declarations) {
            if (decl is StructDeclaration structDecl) {
                var fields = structDecl.Fields.Select(f => f.Identifier).ToList();
                _structFields[GetPrefixedName(structDecl.Identifier)] = fields;
            } else if (decl is UnionDeclaration unionDecl) {
                _unions[GetPrefixedName(unionDecl.Identifier)] = unionDecl;
            } else if (decl is EnumDeclaration enumDecl) {
                _enums[GetPrefixedName(enumDecl.Identifier)] = enumDecl;
            }
        }

        foreach (var stmt in topLevelStatements) {
            if (stmt is VariableDeclaration varDecl) {
                string typeStr = "void";
                if (!string.IsNullOrEmpty(varDecl.Typing)) {
                    typeStr = MapType(varDecl.Typing);
                } else if (varDecl.Value != null && !string.IsNullOrEmpty(varDecl.Value.InferredType)) {
                    typeStr = MapType(varDecl.Value.InferredType);
                }
                var varName = GetPrefixedName(varDecl.Identifier);
                _globalDeclSb.AppendLine($"{typeStr} {varName};");
                _globalVarTypes[varName] = !string.IsNullOrEmpty(varDecl.Typing) ? varDecl.Typing : (varDecl.Value != null ? varDecl.Value.InferredType : "any");
            }
        }

        GenerateLambdaEnvironments(program);

        var emittedRefTypes = new HashSet<string>();
        foreach (var kv in _boxedTypes) {
            var pinoType = kv.Value;
            var cType = MapType(pinoType);
            var cleanType = CleanTypeName(cType);
            if (!emittedRefTypes.Contains(cleanType)) {
                emittedRefTypes.Add(cleanType);
                _globalDeclSb.AppendLine($"struct Ref_{cleanType} {{");
                _globalDeclSb.AppendLine($"    {cType} value;");
                _globalDeclSb.AppendLine($"}};");
            }
        }

        foreach (var decl in declarations) {
            if (decl is StructDeclaration structDecl) {
                if (structDecl.GenericParams != null && structDecl.GenericParams.Count > 0) continue;
                var structName = GetPrefixedName(structDecl.Identifier);
                if (!_emittedStructsAndUnions.Add(structName)) continue;
                _forwardDeclSb.AppendLine($"typedef struct {structName} {structName};");
                _structSb.AppendLine($"struct {structName} {{");
                foreach (var field in structDecl.Fields) {
                    _structSb.AppendLine($"    {MapType(field.Typing)} {field.Identifier};");
                }
                _structSb.AppendLine("};");
                _structSb.AppendLine();

                foreach (var method in structDecl.Methods) {
                    var retType = MapType(method.ResolvedReturnType);
                    string methodParams;
                    if (method.IsStatic) {
                        methodParams = string.Join(", ", method.Parameters.Select(p => $"{MapType(p.Typing)} {p.Identifier}"));
                        if (string.IsNullOrEmpty(methodParams)) methodParams = "void";
                    } else {
                        methodParams = $"struct {structName}* this";
                        if (method.Parameters.Count > 0) {
                            methodParams += ", " + string.Join(", ", method.Parameters.Select(p => $"{MapType(p.Typing)} {p.Identifier}"));
                        }
                    }
                    _structSb.AppendLine($"{retType} {structName}_{method.Identifier}({methodParams});");
                }
                GenerateStructToString(structDecl);
                _structSb.AppendLine();
            } else if (decl is EnumDeclaration enumDecl) {
                var enumName = GetPrefixedName(enumDecl.Identifier);
                if (!_emittedStructsAndUnions.Add(enumName)) continue;
                _structSb.AppendLine($"enum {enumName} {{");
                foreach (var member in enumDecl.Members) {
                    _structSb.AppendLine($"    {enumName}_{member},");
                }
                _structSb.AppendLine("};");
                _structSb.AppendLine($"typedef enum {enumName} {enumName};");
                GenerateEnumToString(enumDecl);
                _structSb.AppendLine();
            } else if (decl is UnionDeclaration unionDecl) {
                if (unionDecl.GenericParams != null && unionDecl.GenericParams.Count > 0) continue;
                var unionName = GetPrefixedName(unionDecl.Identifier);
                if (!_emittedStructsAndUnions.Add(unionName)) continue;
                _forwardDeclSb.AppendLine($"typedef struct {unionName} {unionName};");
                _structSb.AppendLine($"enum {unionName}Tag {{");
                foreach (var variant in unionDecl.Variants) {
                    _structSb.AppendLine($"    {unionName}Tag_{variant.Identifier},");
                }
                _structSb.AppendLine("};");
                _structSb.AppendLine();

                foreach (var variant in unionDecl.Variants) {
                    if (variant.AssociatedTypes.Count > 0) {
                        _structSb.AppendLine($"struct {unionName}_{variant.Identifier}_payload {{");
                        for (int i = 0; i < variant.AssociatedTypes.Count; i++) {
                            _structSb.AppendLine($"    {MapType(variant.AssociatedTypes[i])} _{i};");
                        }
                        _structSb.AppendLine("};");
                        _structSb.AppendLine();
                    }
                }

                _structSb.AppendLine($"struct {unionName} {{");
                _structSb.AppendLine($"    enum {unionName}Tag tag;");
                bool hasPayloads = unionDecl.Variants.Any(v => v.AssociatedTypes.Count > 0);
                if (hasPayloads) {
                    _structSb.AppendLine("    union {");
                    foreach (var variant in unionDecl.Variants) {
                        if (variant.AssociatedTypes.Count > 0) {
                            _structSb.AppendLine($"        struct {unionName}_{variant.Identifier}_payload {variant.Identifier};");
                        }
                    }
                    _structSb.AppendLine("    } value;");
                }
                _structSb.AppendLine("};");
                _structSb.AppendLine();

                foreach (var variant in unionDecl.Variants) {
                    if (variant.AssociatedTypes.Count > 0) {
                        var paramsStr = string.Join(", ", variant.AssociatedTypes.Select((t, i) => $"{MapType(t)} _{i}"));
                        _structSb.AppendLine($"{unionName}* {unionName}_{variant.Identifier}_construct({paramsStr});");
                    }
                }
                GenerateUnionToString(unionDecl);
                _structSb.AppendLine();
            } else if (decl is FunctionDeclaration fnDecl && fnDecl.Identifier != "main") {
                if (fnDecl.GenericParams != null && fnDecl.GenericParams.Count > 0) continue;
                var returnType = MapType(fnDecl.ResolvedReturnType);
                var parameters = string.Join(", ", fnDecl.Parameters.Select(p => $"{MapType(p.Typing)} {p.Identifier}"));
                if (string.IsNullOrEmpty(parameters)) parameters = "void";
                var fnName = GetPrefixedName(fnDecl.Identifier);
                _sb.AppendLine($"{returnType} {fnName}({parameters});");
            }
        }
    }

    private void TranspilePass2(ProgramStatement program) {
        var declarations = new List<Declaration>();

        foreach (var stmt in program.Statements) {
            if (stmt is Declaration decl && !(decl is VariableDeclaration)) {
                declarations.Add(decl);
            }
        }

        foreach (var decl in declarations) {
            if (decl is StructDeclaration structDecl) {
                if (structDecl.GenericParams != null && structDecl.GenericParams.Count > 0) continue;
                var structName = GetPrefixedName(structDecl.Identifier);
                foreach (var method in structDecl.Methods) {
                    TranspileStructMethod(structName, method);
                }
            } else if (decl is UnionDeclaration unionDecl) {
                if (unionDecl.GenericParams != null && unionDecl.GenericParams.Count > 0) continue;
                var unionName = GetPrefixedName(unionDecl.Identifier);
                if (!_emittedConstructors.Add(unionName)) continue;
                foreach (var variant in unionDecl.Variants) {
                    if (variant.AssociatedTypes.Count > 0) {
                        var paramsStr = string.Join(", ", variant.AssociatedTypes.Select((t, i) => $"{MapType(t)} _{i}"));
                        WriteLine($"{unionName}* {unionName}_{variant.Identifier}_construct({paramsStr}) {{");
                        _indent++;
                        WriteLine($"{unionName}* res = ({unionName}*)pino_malloc(sizeof({unionName}));");
                        WriteLine($"res->tag = {unionName}Tag_{variant.Identifier};");
                        for (int i = 0; i < variant.AssociatedTypes.Count; i++) {
                            WriteLine($"res->value.{variant.Identifier}._{i} = _{i};");
                        }
                        WriteLine("return res;");
                        _indent--;
                        WriteLine("}");
                        _sb.AppendLine();
                    }
                }
            } else if (decl is FunctionDeclaration fnDecl && fnDecl.Identifier != "main") {
                if (fnDecl.GenericParams != null && fnDecl.GenericParams.Count > 0) continue;
                TranspileFunction(fnDecl);
            }
        }
    }

    public string Transpile(ProgramStatement program, Checker? checker = null) {
        _checker = checker;
        _importedSymbols.Clear();
        _allModuleCheckers.Clear();
        _tests.Clear();
        _moduleFunctions.Clear();

        var allModules = new List<(string Name, Checker Checker)>();
        if (_checker != null) {
            CollectModules(_checker, allModules, new HashSet<string>());
        }
        foreach (var mod in allModules) {
            _allModuleCheckers[mod.Name] = mod.Checker;
        }

        var forwardFuncSb = new StringBuilder();
        _currentFilePath = program.FilePath ?? "";
        
        _currentModuleName = null;
        CollectAndAnalyzeLambdas(program);
        foreach (var mod in allModules) {
            if (mod.Checker.Program != null) {
                _currentModuleName = mod.Name;
                CollectAndAnalyzeLambdas(mod.Checker.Program);
            }
        }
        _currentModuleName = null;

        _currentReturnType = "";
        _varTypes.Clear();
        _structFields.Clear();
        _currentStructFields.Clear();
        _tupleSb.Clear();
        _declaredTuples.Clear();
        _declaredTuples.Add("[]string");
        
        _definedTypes.Clear();
        foreach (var mod in allModules) {
            if (mod.Checker.Program != null) {
                foreach (var stmt in mod.Checker.Program.Statements) {
                    if (stmt is StructDeclaration sd) {
                        _definedTypes[$"{mod.Name}_{sd.Identifier}"] = true;
                    } else if (stmt is UnionDeclaration ud) {
                        _definedTypes[$"{mod.Name}_{ud.Identifier}"] = true;
                    } else if (stmt is EnumDeclaration ed) {
                        _definedTypes[$"{mod.Name}_{ed.Identifier}"] = true;
                    }
                }
            }
        }
        foreach (var stmt in program.Statements) {
            if (stmt is StructDeclaration sd) {
                _definedTypes[$"{sd.Identifier}"] = true;
            } else if (stmt is UnionDeclaration ud) {
                _definedTypes[$"{ud.Identifier}"] = true;
            } else if (stmt is EnumDeclaration ed) {
                _definedTypes[$"{ed.Identifier}"] = true;
            }
        }

        _unions.Clear();
        _enums.Clear();
        _matchResultVars.Clear();
        _globalVarTypes.Clear();
        _globalDeclSb.Clear();
        _isGlobalScope = true;
        _usesReadFile = false;
        _usesWriteFile = false;
        _usesFileExists = false;
        _sb.Clear();
        _forwardDeclSb.Clear();
        _structSb.Clear();
        _emittedStructsAndUnions.Clear();
        _emittedConstructors.Clear();

        foreach (var mod in allModules) {
            if (mod.Checker.Program != null) {
                _currentModuleName = mod.Name;
                TranspilePass0And1(mod.Checker.Program);
            }
        }

        _currentModuleName = null;
        TranspilePass0And1(program);

        foreach (var mod in allModules) {
            if (mod.Checker.Program != null) {
                _currentModuleName = mod.Name;
                TranspilePass2(mod.Checker.Program);
            }
        }

        _currentModuleName = null;
        TranspilePass2(program);

        TranspileLambdas(forwardFuncSb);
        TranspileTests(forwardFuncSb);

        _sb.AppendLine("int main(int argc, char** argv) {");
        _indent = 1;
        _sb.AppendLine("#ifdef PINO_GC");
        _sb.AppendLine("    GC_INIT();");
        _sb.AppendLine("#endif");
        _sb.AppendLine("    srand((unsigned int)time(NULL));");

        _isGlobalScope = true;

        foreach (var mod in allModules) {
            if (mod.Checker.Program != null) {
                _currentModuleName = mod.Name;
                var modTopLevel = new List<Statement>();
                foreach (var stmt in mod.Checker.Program.Statements) {
                    if ((stmt is VariableDeclaration || !(stmt is Declaration)) && !(stmt is ModuleDeclaration || stmt is ImportStatement || stmt is FromImportStatement)) {
                        modTopLevel.Add(stmt);
                    }
                }
                foreach (var stmt in modTopLevel) {
                    TranspileStatement(stmt);
                }
            }
        }

        _currentModuleName = null;
        var mainTopLevel = new List<Statement>();
        foreach (var stmt in program.Statements) {
            if ((stmt is VariableDeclaration || !(stmt is Declaration)) && !(stmt is ModuleDeclaration || stmt is ImportStatement || stmt is FromImportStatement)) {
                mainTopLevel.Add(stmt);
            }
        }
        foreach (var stmt in mainTopLevel) {
            TranspileStatement(stmt);
        }

        if (EnableTests) {
            _sb.AppendLine("    _pino_in_test = 1;");
            _sb.AppendLine("    int passed = 0;");
            _sb.AppendLine("    int failed = 0;");
            _sb.AppendLine($"    printf(\"Running {_tests.Count} tests...\\n\");");
            for (int i = 0; i < _tests.Count; i++) {
                var test = _tests[i].Test;
                _sb.AppendLine("    if (setjmp(_pino_test_jump_env) == 0) {");
                _sb.AppendLine($"        _pino_test_{i}();");
                _sb.AppendLine("        passed++;");
                _sb.AppendLine($"        printf(\"[PASS] test \\\"{test.Description}\\\"\\n\");");
                _sb.AppendLine("    } else {");
                _sb.AppendLine("        failed++;");
                _sb.AppendLine($"        printf(\"[FAIL] test \\\"{test.Description}\\\"\\n\");");
                _sb.AppendLine("    }");
            }
            _sb.AppendLine("    printf(\"\\nTest summary: %d passed, %d failed, %d total.\\n\", passed, failed, passed + failed);");
            _sb.AppendLine("    return failed > 0 ? 1 : 0;");
        } else {
            var userMain = program.Statements.FirstOrDefault(d => d is FunctionDeclaration fn && fn.Identifier == "main") as FunctionDeclaration;
            if (userMain != null && userMain.Body != null) {
                _isGlobalScope = false;
                _varTypes.Clear();
                foreach (var kvp in _globalVarTypes) {
                    _varTypes[kvp.Key] = kvp.Value;
                }
                TranspileStatement(userMain.Body);
            }
            WriteLine("return 0;");
        }
        _indent = 0;
        _sb.AppendLine("}");

        if (_usesReadFile) {
            _sb.AppendLine();
            _sb.AppendLine("Result_string_IOError* pino_read_file(const char* path) {");
            _sb.AppendLine("    FILE* f = fopen(path, \"rb\");");
            _sb.AppendLine("    if (!f) {");
            _sb.AppendLine("        IOError* err;");
            _sb.AppendLine("        if (errno == ENOENT) {");
            _sb.AppendLine("            err = IOError_NotFound_construct(path);");
            _sb.AppendLine("        } else if (errno == EACCES || errno == EPERM) {");
            _sb.AppendLine("            err = IOError_PermissionDenied_construct(path);");
            _sb.AppendLine("        } else if (errno == EEXIST) {");
            _sb.AppendLine("            err = IOError_AlreadyExists_construct(path);");
            _sb.AppendLine("        } else {");
            _sb.AppendLine("            err = IOError_Gremlin_construct(strerror(errno));");
            _sb.AppendLine("        }");
            _sb.AppendLine("        return Result_string_IOError_Failure_construct(err);");
            _sb.AppendLine("    }");
            _sb.AppendLine("    fseek(f, 0, SEEK_END);");
            _sb.AppendLine("    long len = ftell(f);");
            _sb.AppendLine("    fseek(f, 0, SEEK_SET);");
            _sb.AppendLine("    char* buf = (char*)pino_malloc(len + 1);");
            _sb.AppendLine("    size_t read_bytes = fread(buf, 1, len, f);");
            _sb.AppendLine("    buf[read_bytes] = '\\0';");
            _sb.AppendLine("    fclose(f);");
            _sb.AppendLine("    return Result_string_IOError_Success_construct(buf);");
            _sb.AppendLine("}");
        }

        if (_usesWriteFile) {
            _sb.AppendLine();
            _sb.AppendLine("Result_string_IOError* pino_write_file(const char* path, const char* content) {");
            _sb.AppendLine("    FILE* f = fopen(path, \"wb\");");
            _sb.AppendLine("    if (!f) {");
            _sb.AppendLine("        IOError* err;");
            _sb.AppendLine("        if (errno == EACCES || errno == EPERM) {");
            _sb.AppendLine("            err = IOError_PermissionDenied_construct(path);");
            _sb.AppendLine("        } else {");
            _sb.AppendLine("            err = IOError_Gremlin_construct(strerror(errno));");
            _sb.AppendLine("        }");
            _sb.AppendLine("        return Result_string_IOError_Failure_construct(err);");
            _sb.AppendLine("    }");
            _sb.AppendLine("    fputs(content, f);");
            _sb.AppendLine("    fclose(f);");
            _sb.AppendLine("    return Result_string_IOError_Success_construct(path);");
            _sb.AppendLine("}");
        }

        if (_usesFileExists) {
            _sb.AppendLine();
            _sb.AppendLine("bool pino_file_exists(const char* path) {");
            _sb.AppendLine("    FILE* f = fopen(path, \"rb\");");
            _sb.AppendLine("    if (f) {");
            _sb.AppendLine("        fclose(f);");
            _sb.AppendLine("        return true;");
            _sb.AppendLine("    }");
            _sb.AppendLine("    return false;");
            _sb.AppendLine("}");
        }

        if (_usesReadFile) {
            forwardFuncSb.AppendLine("Result_string_IOError* pino_read_file(const char* path);");
        }
        if (_usesWriteFile) {
            forwardFuncSb.AppendLine("Result_string_IOError* pino_write_file(const char* path, const char* content);");
        }
        if (_usesFileExists) {
            forwardFuncSb.AppendLine("bool pino_file_exists(const char* path);");
        }

        var vecTypes = _declaredTuples.Where(t => t.StartsWith("[]")).ToList();
        foreach (var vecType in vecTypes) {
            GenerateVectorToString(vecType);
        }

        var finalSb = new StringBuilder();
        finalSb.AppendLine("#include <stdio.h>");
        finalSb.AppendLine("#include <stdlib.h>");
        finalSb.AppendLine("#include <stdbool.h>");
        finalSb.AppendLine("#include <string.h>");
        finalSb.AppendLine("#include <errno.h>");
        finalSb.AppendLine("#include \"runtime/runtime.h\"");
        finalSb.AppendLine();
        finalSb.Append(_forwardDeclSb.ToString());
        finalSb.Append(_tupleSb.ToString());
        finalSb.Append(_globalDeclSb.ToString());
        finalSb.Append(_structSb.ToString());
        finalSb.Append(forwardFuncSb.ToString());
        finalSb.Append(_sb.ToString());

        return finalSb.ToString();
    }

    private void TranspileFunction(FunctionDeclaration fnDecl) {
        var returnType = MapType(fnDecl.ResolvedReturnType);
        var identifier = GetPrefixedName(fnDecl.Identifier);

        _currentReturnType = fnDecl.ResolvedReturnType;
        _isGlobalScope = false;
        _varTypes.Clear();
        foreach (var kvp in _globalVarTypes) {
            _varTypes[kvp.Key] = kvp.Value;
        }
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
        var returnType = MapType(fnDecl.ResolvedReturnType);
        var identifier = $"{structName}_{fnDecl.Identifier}";

        _currentReturnType = fnDecl.ResolvedReturnType;
        // Setup method environment
        _isGlobalScope = false;
        _varTypes.Clear();
        foreach (var kvp in _globalVarTypes) {
            _varTypes[kvp.Key] = kvp.Value;
        }
        _currentStructFields.Clear();
        
        // Add fields to current struct fields
        if (!fnDecl.IsStatic) {
            if (_structFields.TryGetValue(structName, out var fields)) {
                foreach (var f in fields) {
                    _currentStructFields.Add(f);
                }
            }
            // Add parameters to _varTypes
            _varTypes["this"] = structName;
            _varTypes["self"] = structName;
        }

        foreach (var param in fnDecl.Parameters) {
            _varTypes[param.Identifier] = param.Typing;
        }

        string parameters;
        if (fnDecl.IsStatic) {
            parameters = string.Join(", ", fnDecl.Parameters.Select(p => $"{MapType(p.Typing)} {p.Identifier}"));
            if (string.IsNullOrEmpty(parameters)) parameters = "void";
        } else {
            parameters = $"struct {structName}* this";
            if (fnDecl.Parameters.Count > 0) {
                parameters += ", " + string.Join(", ", fnDecl.Parameters.Select(p => $"{MapType(p.Typing)} {p.Identifier}"));
            }
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

    private string ResolveTypeName(string type) {
        if (string.IsNullOrEmpty(type)) return type;
        type = type.Replace("::", "_");
        foreach (var modName in _allModuleCheckers.Keys) {
            if (type.StartsWith(modName + "_Result_")) {
                type = type.Substring(modName.Length + 1);
                break;
            }
            if (type.StartsWith(modName + "_IOError_")) {
                type = type.Substring(modName.Length + 1);
                break;
            }
        }
        if (_currentModuleName != null) {
            if (type.StartsWith(_currentModuleName + "_Result_")) {
                type = type.Substring(_currentModuleName.Length + 1);
            }
            if (type.StartsWith(_currentModuleName + "_IOError_")) {
                type = type.Substring(_currentModuleName.Length + 1);
            }
        }
        if (_importedSymbols.TryGetValue(type, out var mappedType)) {
            return mappedType;
        }
        if ((type.StartsWith("Result_") || type.StartsWith("IOError_")) && type.Contains("_")) {
            int firstUnderscore = type.IndexOf('_');
            var baseName = type.Substring(0, firstUnderscore);
            var rest = type.Substring(firstUnderscore + 1);
            
            var rawArgs = rest.Split('_');
            var args = new List<string>();
            for (int i = 0; i < rawArgs.Length; i++) {
                if (i < rawArgs.Length - 1 && _allModuleCheckers.ContainsKey(rawArgs[i])) {
                    args.Add(rawArgs[i] + "_" + rawArgs[i+1]);
                    i++;
                } else {
                    args.Add(rawArgs[i]);
                }
            }
            
            var resolvedArgs = args.Select(a => ResolveTypeName(a)).ToList();
            return ResolveAnyInGenericType(baseName + "_" + string.Join("_", resolvedArgs));
        }
        if (_currentModuleName != null) {
            var pref = $"{_currentModuleName}_{type}";
            if (_definedTypes.ContainsKey(pref) || _structFields.ContainsKey(pref) || _unions.ContainsKey(pref) || _enums.ContainsKey(pref)) {
                return pref;
            }
        }
        if (_allModuleCheckers.Count > 0) {
            foreach (var modName in _allModuleCheckers.Keys) {
                var pref = $"{modName}_{type}";
                if (_definedTypes.ContainsKey(pref) || _structFields.ContainsKey(pref) || _unions.ContainsKey(pref) || _enums.ContainsKey(pref)) {
                    return pref;
                }
            }
        }
        return ResolveAnyInGenericType(type);
    }

    private string ResolveAnyInGenericType(string type) {
        if (type.Contains("_any")) {
            var pattern = type.Replace("_any", "_");
            foreach (var key in _unions.Keys) {
                if (key.StartsWith(pattern)) {
                    return key;
                }
            }
            foreach (var key in _structFields.Keys) {
                if (key.StartsWith(pattern)) {
                    return key;
                }
            }
        }
        return type;
    }

    private string GetCleanUnionNameForToString(string typeName) {
        if (typeName.StartsWith("Result_") || typeName == "Result") return "Result";
        if (typeName.StartsWith("IOError_") || typeName == "IOError") return "IOError";
        foreach (var mod in _allModuleCheckers.Keys) {
            if (typeName.StartsWith(mod + "_")) {
                return typeName.Substring(mod.Length + 1);
            }
        }
        return typeName;
    }

    private (string format, string argExpression) GetFormatSpecifierAndArgForType(string pinoType, string valExpr) {
        pinoType = ResolveTypeName(pinoType);
        var cleanType = CleanTypeName(pinoType);
        
        if (pinoType == "string") {
            return ("\\\"%s\\\"", valExpr);
        } else if (pinoType == "int") {
            return ("%d", valExpr);
        } else if (pinoType == "float") {
            return ("%g", valExpr);
        } else if (pinoType == "bool") {
            return ("%s", $"{valExpr} ? \"True\" : \"False\"");
        } else if (pinoType == "rune") {
            return ("%c", valExpr);
        } else if (FindEnum(pinoType) != null) {
            return ("%s", $"{pinoType}_to_string({valExpr})");
        } else if (FindUnion(pinoType) != null) {
            return ("%s", $"{pinoType}_to_string({valExpr})");
        } else if (_structFields.ContainsKey(pinoType)) {
            return ("%s", $"{pinoType}_to_string({valExpr})");
        } else if (pinoType.StartsWith("[]")) {
            var vecClean = CleanTypeName(pinoType);
            return ("%s", $"{vecClean}_to_string({valExpr})");
        } else {
            return ("%s", valExpr);
        }
    }

    private void GenerateEnumToString(EnumDeclaration enumDecl) {
        var enumName = GetPrefixedName(enumDecl.Identifier);
        _structSb.AppendLine($"const char* {enumName}_to_string({enumName} val);");
        
        var body = new StringBuilder();
        body.AppendLine($"const char* {enumName}_to_string({enumName} val) {{");
        body.AppendLine("    switch (val) {");
        foreach (var member in enumDecl.Members) {
            var cleanEnumName = GetCleanUnionNameForToString(enumName);
            body.AppendLine($"        case {enumName}_{member}: return \"{cleanEnumName}::{member}\";");
        }
        body.AppendLine("        default: return \"unknown\";");
        body.AppendLine("    }");
        body.AppendLine("}");
        _sb.AppendLine(body.ToString());
    }

    private void GenerateStructToString(StructDeclaration structDecl) {
        var structName = GetPrefixedName(structDecl.Identifier);
        _structSb.AppendLine($"const char* {structName}_to_string({structName}* val);");
        
        var body = new StringBuilder();
        body.AppendLine($"const char* {structName}_to_string({structName}* val) {{");
        body.AppendLine("    if (!val) return \"null\";");
        body.AppendLine("    char* buf = (char*)pino_malloc(1024);");
        
        var cleanStructName = GetCleanUnionNameForToString(structName);
        var formatStr = $"{cleanStructName} {{ ";
        var argsList = new List<string>();
        
        for (int i = 0; i < structDecl.Fields.Count; i++) {
            var field = structDecl.Fields[i];
            if (i > 0) formatStr += ", ";
            var (fmt, arg) = GetFormatSpecifierAndArgForType(field.Typing, $"val->{field.Identifier}");
            formatStr += $"{field.Identifier}: {fmt}";
            argsList.Add(arg);
        }
        formatStr += " }";
        
        var argsStr = argsList.Count > 0 ? ", " + string.Join(", ", argsList) : "";
        body.AppendLine($"    snprintf(buf, 1024, \"{formatStr}\"{argsStr});");
        body.AppendLine("    return buf;");
        body.AppendLine("}");
        _sb.AppendLine(body.ToString());
    }

    private void GenerateUnionToString(UnionDeclaration unionDecl) {
        var unionName = GetPrefixedName(unionDecl.Identifier);
        _structSb.AppendLine($"const char* {unionName}_to_string({unionName}* val);");
        
        var body = new StringBuilder();
        body.AppendLine($"const char* {unionName}_to_string({unionName}* val) {{");
        body.AppendLine("    if (!val) return \"null\";");
        body.AppendLine("    char* buf = (char*)pino_malloc(1024);");
        body.AppendLine("    switch (val->tag) {");
        
        var cleanUnionName = GetCleanUnionNameForToString(unionName);
        foreach (var variant in unionDecl.Variants) {
            body.AppendLine($"        case {unionName}Tag_{variant.Identifier}: {{");
            if (variant.AssociatedTypes.Count == 0) {
                body.AppendLine($"            return \"{cleanUnionName}::{variant.Identifier}\";");
            } else {
                var formatStr = $"{cleanUnionName}::{variant.Identifier}(";
                var argsList = new List<string>();
                for (int i = 0; i < variant.AssociatedTypes.Count; i++) {
                    if (i > 0) formatStr += ", ";
                    var (fmt, arg) = GetFormatSpecifierAndArgForType(variant.AssociatedTypes[i], $"val->value.{variant.Identifier}._{i}");
                    formatStr += fmt;
                    argsList.Add(arg);
                }
                formatStr += ")";
                var argsStr = string.Join(", ", argsList);
                body.AppendLine($"            snprintf(buf, 1024, \"{formatStr}\", {argsStr});");
                body.AppendLine("            return buf;");
            }
            body.AppendLine("        }");
        }
        body.AppendLine("        default: return \"unknown\";");
        body.AppendLine("    }");
        body.AppendLine("}");
        _sb.AppendLine(body.ToString());
    }

    private void GenerateVectorToString(string pinoType) {
        var clean = CleanTypeName(pinoType);
        var elemType = pinoType.Substring(2);
        
        _structSb.AppendLine($"const char* {clean}_to_string({clean}* val);");
        
        var body = new StringBuilder();
        body.AppendLine($"const char* {clean}_to_string({clean}* val) {{");
        body.AppendLine("    if (!val) return \"[]\";");
        body.AppendLine("    char* buf = (char*)pino_malloc(4096);");
        body.AppendLine("    int len = snprintf(buf, 4096, \"[\");");
        body.AppendLine("    for (int i = 0; i < val->length; i++) {");
        body.AppendLine("        if (i > 0) len += snprintf(buf + len, 4096 - len, \", \");");
        
        var (fmt, arg) = GetFormatSpecifierAndArgForType(elemType, "val->items[i]");
        body.AppendLine($"        len += snprintf(buf + len, 4096 - len, \"{fmt}\", {arg});");
        
        body.AppendLine("    }");
        body.AppendLine("    snprintf(buf + len, 4096 - len, \"]\");");
        body.AppendLine("    return buf;");
        body.AppendLine("}");
        _sb.AppendLine(body.ToString());
    }

    private string MapType(string pinoType) {
        if (string.IsNullOrEmpty(pinoType)) return "void";
        pinoType = ResolveTypeName(pinoType);
        if (pinoType.StartsWith("fn(")) return "PinoClosure";
        if (pinoType.StartsWith("map[")) {
            var clean = CleanTypeName(pinoType);
            if (!_declaredTuples.Contains(pinoType)) {
                _declaredTuples.Add(pinoType);
                
                int commaIdx = pinoType.IndexOf(',');
                var keyType = pinoType.Substring(4, commaIdx - 4).Trim();
                var valType = pinoType.Substring(commaIdx + 1, pinoType.Length - commaIdx - 2).Trim();
                var cKeyType = MapType(keyType);
                var cValType = MapType(valType);
                
                // Force declaration of the vector types
                MapType($"[]{keyType}");
                MapType($"[]{valType}");
                var cKeysVecType = MapType($"[]{keyType}");
                var cValsVecType = MapType($"[]{valType}");
                
                string hashExpr = GetHashFunction(keyType, "key");
                string keyEqExpr = keyType == "string" ? "strcmp(a, b) == 0" : "a == b";
                
                _tupleSb.AppendLine($"struct {clean}_entry;");
                _tupleSb.AppendLine($"typedef struct {clean}_entry {clean}_entry;");
                _tupleSb.AppendLine($"struct {clean}_entry {{");
                _tupleSb.AppendLine($"    int occupied;");
                _tupleSb.AppendLine($"    {cKeyType} key;");
                _tupleSb.AppendLine($"    {cValType} value;");
                _tupleSb.AppendLine($"}};");
                _tupleSb.AppendLine();
                
                _tupleSb.AppendLine($"struct {clean};");
                _tupleSb.AppendLine($"typedef struct {clean} {clean};");
                _tupleSb.AppendLine($"struct {clean} {{");
                _tupleSb.AppendLine($"    {clean}_entry* entries;");
                _tupleSb.AppendLine($"    int size;");
                _tupleSb.AppendLine($"    int capacity;");
                _tupleSb.AppendLine($"}};");
                _tupleSb.AppendLine();
                
                _tupleSb.AppendLine($"static inline {clean}* {clean}_construct() {{");
                _tupleSb.AppendLine($"    {clean}* m = ({clean}*)pino_malloc(sizeof({clean}));");
                _tupleSb.AppendLine($"    m->size = 0;");
                _tupleSb.AppendLine($"    m->capacity = 8;");
                _tupleSb.AppendLine($"    m->entries = ({clean}_entry*)pino_malloc(m->capacity * sizeof({clean}_entry));");
                _tupleSb.AppendLine($"    memset(m->entries, 0, m->capacity * sizeof({clean}_entry));");
                _tupleSb.AppendLine($"    return m;");
                _tupleSb.AppendLine($"}}");
                _tupleSb.AppendLine();
                
                _tupleSb.AppendLine($"static inline void {clean}_set({clean}* map, {cKeyType} key, {cValType} value) {{");
                _tupleSb.AppendLine($"    if (map->size * 2 >= map->capacity) {{");
                _tupleSb.AppendLine($"        int old_cap = map->capacity;");
                _tupleSb.AppendLine($"        {clean}_entry* old_entries = map->entries;");
                _tupleSb.AppendLine($"        map->capacity = old_cap == 0 ? 8 : old_cap * 2;");
                _tupleSb.AppendLine($"        map->entries = ({clean}_entry*)pino_malloc(map->capacity * sizeof({clean}_entry));");
                _tupleSb.AppendLine($"        memset(map->entries, 0, map->capacity * sizeof({clean}_entry));");
                _tupleSb.AppendLine($"        map->size = 0;");
                _tupleSb.AppendLine($"        for (int i = 0; i < old_cap; i++) {{");
                _tupleSb.AppendLine($"            if (old_entries[i].occupied == 1) {{");
                _tupleSb.AppendLine($"                {clean}_set(map, old_entries[i].key, old_entries[i].value);");
                _tupleSb.AppendLine($"            }}");
                _tupleSb.AppendLine($"        }}");
                _tupleSb.AppendLine($"    }}");
                _tupleSb.AppendLine($"    unsigned long hash = {hashExpr};");
                _tupleSb.AppendLine($"    int idx = (int)(hash % map->capacity);");
                _tupleSb.AppendLine($"    int first_tombstone = -1;");
                _tupleSb.AppendLine($"    while (map->entries[idx].occupied != 0) {{");
                _tupleSb.AppendLine($"        if (map->entries[idx].occupied == 1) {{");
                _tupleSb.AppendLine($"            {cKeyType} a = map->entries[idx].key;");
                _tupleSb.AppendLine($"            {cKeyType} b = key;");
                _tupleSb.AppendLine($"            if ({keyEqExpr}) {{");
                _tupleSb.AppendLine($"                map->entries[idx].value = value;");
                _tupleSb.AppendLine($"                return;");
                _tupleSb.AppendLine($"            }}");
                _tupleSb.AppendLine($"        }}");
                _tupleSb.AppendLine($"        if (map->entries[idx].occupied == 2 && first_tombstone == -1) {{");
                _tupleSb.AppendLine($"            first_tombstone = idx;");
                _tupleSb.AppendLine($"        }}");
                _tupleSb.AppendLine($"        idx = (idx + 1) % map->capacity;");
                _tupleSb.AppendLine($"    }}");
                _tupleSb.AppendLine($"    int insert_idx = first_tombstone != -1 ? first_tombstone : idx;");
                _tupleSb.AppendLine($"    map->entries[insert_idx].occupied = 1;");
                _tupleSb.AppendLine($"    map->entries[insert_idx].key = key;");
                _tupleSb.AppendLine($"    map->entries[insert_idx].value = value;");
                _tupleSb.AppendLine($"    map->size++;");
                _tupleSb.AppendLine($"}}");
                _tupleSb.AppendLine();
                
                _tupleSb.AppendLine($"static inline {cValType} {clean}_get({clean}* map, {cKeyType} key) {{");
                _tupleSb.AppendLine($"    if (map->capacity == 0) {{");
                _tupleSb.AppendLine($"        printf(\"RUNTIME ERROR: Key not found in map (empty capacity).\\n\");");
                _tupleSb.AppendLine($"        exit(1);");
                _tupleSb.AppendLine($"    }}");
                _tupleSb.AppendLine($"    unsigned long hash = {hashExpr};");
                _tupleSb.AppendLine($"    int idx = (int)(hash % map->capacity);");
                _tupleSb.AppendLine($"    int start = idx;");
                _tupleSb.AppendLine($"    while (map->entries[idx].occupied != 0) {{");
                _tupleSb.AppendLine($"        if (map->entries[idx].occupied == 1) {{");
                _tupleSb.AppendLine($"            {cKeyType} a = map->entries[idx].key;");
                _tupleSb.AppendLine($"            {cKeyType} b = key;");
                _tupleSb.AppendLine($"            if ({keyEqExpr}) {{");
                _tupleSb.AppendLine($"                return map->entries[idx].value;");
                _tupleSb.AppendLine($"            }}");
                _tupleSb.AppendLine($"        }}");
                _tupleSb.AppendLine($"        idx = (idx + 1) % map->capacity;");
                _tupleSb.AppendLine($"        if (idx == start) break;");
                _tupleSb.AppendLine($"    }}");
                _tupleSb.AppendLine($"    printf(\"RUNTIME ERROR: Key not found in map.\\n\");");
                _tupleSb.AppendLine($"    exit(1);");
                _tupleSb.AppendLine($"}}");
                _tupleSb.AppendLine();
                
                _tupleSb.AppendLine($"static inline bool {clean}_has({clean}* map, {cKeyType} key) {{");
                _tupleSb.AppendLine($"    if (map->capacity == 0) return false;");
                _tupleSb.AppendLine($"    unsigned long hash = {hashExpr};");
                _tupleSb.AppendLine($"    int idx = (int)(hash % map->capacity);");
                _tupleSb.AppendLine($"    int start = idx;");
                _tupleSb.AppendLine($"    while (map->entries[idx].occupied != 0) {{");
                _tupleSb.AppendLine($"        if (map->entries[idx].occupied == 1) {{");
                _tupleSb.AppendLine($"            {cKeyType} a = map->entries[idx].key;");
                _tupleSb.AppendLine($"            {cKeyType} b = key;");
                _tupleSb.AppendLine($"            if ({keyEqExpr}) {{");
                _tupleSb.AppendLine($"                return true;");
                _tupleSb.AppendLine($"            }}");
                _tupleSb.AppendLine($"        }}");
                _tupleSb.AppendLine($"        idx = (idx + 1) % map->capacity;");
                _tupleSb.AppendLine($"        if (idx == start) break;");
                _tupleSb.AppendLine($"    }}");
                _tupleSb.AppendLine($"    return false;");
                _tupleSb.AppendLine($"}}");
                _tupleSb.AppendLine();
                
                _tupleSb.AppendLine($"static inline {cValType} {clean}_remove({clean}* map, {cKeyType} key) {{");
                _tupleSb.AppendLine($"    if (map->capacity == 0) {{");
                _tupleSb.AppendLine($"        {cValType} zero = {{0}};");
                _tupleSb.AppendLine($"        return zero;");
                _tupleSb.AppendLine($"    }}");
                _tupleSb.AppendLine($"    unsigned long hash = {hashExpr};");
                _tupleSb.AppendLine($"    int idx = (int)(hash % map->capacity);");
                _tupleSb.AppendLine($"    int start = idx;");
                _tupleSb.AppendLine($"    while (map->entries[idx].occupied != 0) {{");
                _tupleSb.AppendLine($"        if (map->entries[idx].occupied == 1) {{");
                _tupleSb.AppendLine($"            {cKeyType} a = map->entries[idx].key;");
                _tupleSb.AppendLine($"            {cKeyType} b = key;");
                _tupleSb.AppendLine($"            if ({keyEqExpr}) {{");
                _tupleSb.AppendLine($"                map->entries[idx].occupied = 2;");
                _tupleSb.AppendLine($"                map->size--;");
                _tupleSb.AppendLine($"                return map->entries[idx].value;");
                _tupleSb.AppendLine($"            }}");
                _tupleSb.AppendLine($"        }}");
                _tupleSb.AppendLine($"        idx = (idx + 1) % map->capacity;");
                _tupleSb.AppendLine($"        if (idx == start) break;");
                _tupleSb.AppendLine($"    }}");
                _tupleSb.AppendLine($"    {cValType} zero = {{0}};");
                _tupleSb.AppendLine($"    return zero;");
                _tupleSb.AppendLine($"}}");
                _tupleSb.AppendLine();

                var cleanKeysVecType = CleanTypeName($"[]{keyType}");
                _tupleSb.AppendLine($"static inline {cKeysVecType} {clean}_keys({clean}* map) {{");
                _tupleSb.AppendLine($"    {cKeysVecType} vec = {cleanKeysVecType}_construct(0);");
                _tupleSb.AppendLine($"    for (int i = 0; i < map->capacity; i++) {{");
                _tupleSb.AppendLine($"        if (map->entries[i].occupied == 1) {{");
                _tupleSb.AppendLine($"            vec = {cleanKeysVecType}_push(vec, map->entries[i].key);");
                _tupleSb.AppendLine($"        }}");
                _tupleSb.AppendLine($"    }}");
                _tupleSb.AppendLine($"    return vec;");
                _tupleSb.AppendLine($"}}");
                _tupleSb.AppendLine();

                var cleanValsVecType = CleanTypeName($"[]{valType}");
                _tupleSb.AppendLine($"static inline {cValsVecType} {clean}_values({clean}* map) {{");
                _tupleSb.AppendLine($"    {cValsVecType} vec = {cleanValsVecType}_construct(0);");
                _tupleSb.AppendLine($"    for (int i = 0; i < map->capacity; i++) {{");
                _tupleSb.AppendLine($"        if (map->entries[i].occupied == 1) {{");
                _tupleSb.AppendLine($"            vec = {cleanValsVecType}_push(vec, map->entries[i].value);");
                _tupleSb.AppendLine($"        }}");
                _tupleSb.AppendLine($"    }}");
                _tupleSb.AppendLine($"    return vec;");
                _tupleSb.AppendLine($"}}");
                _tupleSb.AppendLine();
            }
            return clean + "*";
        }
        if (pinoType.StartsWith("[]")) {
            var clean = CleanTypeName(pinoType);
            if (!_declaredTuples.Contains(pinoType)) {
                _declaredTuples.Add(pinoType);
                var elemType = pinoType.Substring(2);
                var cElemType = MapType(elemType);
                
                // Define the struct
                _tupleSb.AppendLine($"struct {clean};");
                _tupleSb.AppendLine($"typedef struct {clean} {clean};");
                _tupleSb.AppendLine($"struct {clean} {{");
                _tupleSb.AppendLine($"    {cElemType}* items;");
                _tupleSb.AppendLine($"    int length;");
                _tupleSb.AppendLine($"    int capacity;");
                _tupleSb.AppendLine($"}};");
                _tupleSb.AppendLine();
                
                // Define constructor and push functions
                _tupleSb.AppendLine($"static inline {clean}* {clean}_construct(int length) {{");
                _tupleSb.AppendLine($"    {clean}* vec = ({clean}*)pino_malloc(sizeof({clean}));");
                _tupleSb.AppendLine($"    vec->length = length;");
                _tupleSb.AppendLine($"    vec->capacity = length > 0 ? length : 4;");
                _tupleSb.AppendLine($"    vec->items = ({cElemType}*)pino_malloc(vec->capacity * sizeof({cElemType}));");
                _tupleSb.AppendLine($"    memset(vec->items, 0, vec->capacity * sizeof({cElemType}));");
                _tupleSb.AppendLine($"    return vec;");
                _tupleSb.AppendLine($"}}");
                _tupleSb.AppendLine();
                
                _tupleSb.AppendLine($"static inline {clean}* {clean}_push({clean}* vec, {cElemType} item) {{");
                _tupleSb.AppendLine($"    if (vec->length >= vec->capacity) {{");
                _tupleSb.AppendLine($"        vec->capacity = vec->capacity == 0 ? 4 : vec->capacity * 2;");
                _tupleSb.AppendLine($"        {cElemType}* new_items = ({cElemType}*)pino_malloc(vec->capacity * sizeof({cElemType}));");
                _tupleSb.AppendLine($"        if (vec->items) {{");
                _tupleSb.AppendLine($"            memcpy(new_items, vec->items, vec->length * sizeof({cElemType}));");
                _tupleSb.AppendLine($"#ifndef PINO_GC");
                _tupleSb.AppendLine($"            free(vec->items);");
                _tupleSb.AppendLine($"#endif");
                _tupleSb.AppendLine($"        }}");
                _tupleSb.AppendLine($"        vec->items = new_items;");
                _tupleSb.AppendLine($"    }}");
                _tupleSb.AppendLine($"    vec->items[vec->length++] = item;");
                _tupleSb.AppendLine($"    return vec;");
                _tupleSb.AppendLine($"}}");
                _tupleSb.AppendLine();

                var elemEqExpr = elemType == "string" ? "strcmp(a, b) == 0" : "a == b";
                _tupleSb.AppendLine($"static inline bool {clean}_has({clean}* vec, {cElemType} item) {{");
                _tupleSb.AppendLine($"    if (!vec) return false;");
                _tupleSb.AppendLine($"    for (int i = 0; i < vec->length; i++) {{");
                _tupleSb.AppendLine($"        {cElemType} a = vec->items[i];");
                _tupleSb.AppendLine($"        {cElemType} b = item;");
                _tupleSb.AppendLine($"        if ({elemEqExpr}) return true;");
                _tupleSb.AppendLine($"    }}");
                _tupleSb.AppendLine($"    return false;");
                _tupleSb.AppendLine($"}}");
                _tupleSb.AppendLine();
            }
            return clean + "*";
        }
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
        if (pinoType.Contains("[") && pinoType.EndsWith("]")) {
            int bracketIdx = pinoType.IndexOf('[');
            var baseName = pinoType.Substring(0, bracketIdx);
            var argsStr = pinoType.Substring(bracketIdx + 1, pinoType.Length - bracketIdx - 2);
            var resolvedBase = ResolveTypeName(baseName);
            var pinoArgs = argsStr.Split(',').Select(a => {
                var arg = a.Trim();
                var resolvedArg = ResolveTypeName(arg);
                return CleanTypeName(resolvedArg);
            }).ToList();
            var cleanTypeName = ResolveTypeName(resolvedBase + "_" + string.Join("_", pinoArgs));
            return cleanTypeName + "*";
        }
        if (_unions.ContainsKey(pinoType)) {
            return pinoType + "*";
        }
        if (_structFields.ContainsKey(pinoType)) {
            return pinoType + "*";
        }
        return pinoType switch {
            "int" => "int",
            "float" => "double",
            "bool" => "int",
            "string" => "const char*",
            "regex" => "regex*",
            "void" => "void",
            "any" => "void*",
            "rune" => "int",
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

            case YieldStatement yield:
                WriteIndent();
                if (_matchResultVars.Count > 0) {
                    Write($"{_matchResultVars.Peek()} = ");
                    TranspileExpression(yield.Value);
                } else {
                    TranspileExpression(yield.Value);
                }
                _sb.AppendLine(";");
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
                        var mappedType = MapType(varType);
                        if (_boxedVariables.Contains(field.Identifier)) {
                            var cleanType = CleanTypeName(mappedType);
                            WriteIndent();
                            Write($"struct Ref_{cleanType}* {field.Identifier} = (struct Ref_{cleanType}*)pino_malloc(sizeof(struct Ref_{cleanType}));\n");
                            WriteIndent();
                            Write($"{field.Identifier}->value = {tempVar}.{field.Label};\n");
                        } else {
                            var isConst = dest.Kind == VariableKind.Constant;
                            var prefix = GetConstPrefix(isConst, mappedType);
                            WriteIndent();
                            Write($"{prefix}{mappedType} {field.Identifier} = {tempVar}.{field.Label};\n");
                        }
                        _varTypes[field.Identifier] = varType;
                    }
                }
                break;

            case ElseStatement elseStmt:
                TranspileStatement(elseStmt.Body);
                break;

            case IfStatement ifs:
                TranspileIf(ifs, false);
                _sb.AppendLine();
                break;

            case LoopStatement loop:
                TranspileLoop(loop);
                break;

            case TestDeclaration testDecl:
                // Ignored in main flow, lifted to static functions
                break;

            case AssertStatement assertStmt:
                WriteIndent();
                Write("pino_assert(");
                TranspileExpression(assertStmt.Expression);
                Write(", ");
                var exprStr = assertStmt.Expression.ToPinoString();
                var escapedExpr = exprStr.Replace("\\", "\\\\").Replace("\"", "\\\"");
                Write($"\"{escapedExpr}\", ");
                var escapedFile = _currentFilePath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                Write($"\"{escapedFile}\", 0)");
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
        if (_isGlobalScope && _blockDepth == 0) {
            if (varDecl.Value != null) {
                if (IsStringConcat(varDecl.Value)) {
                    var prefixedName = GetPrefixedName(varDecl.Identifier);
                    Write($"{prefixedName} = (char*)pino_malloc(1024);\n");
                    var (format, args) = ProcessStringAddition(varDecl.Value);
                    var argsStr = args.Count > 0 ? ", " + string.Join(", ", args) : "";
                    WriteIndent();
                    Write($"snprintf((char*){prefixedName}, 1024, \"{EscapeString(format)}\"{argsStr})");
                } else {
                    Write($"{GetPrefixedName(varDecl.Identifier)} = ");
                    TranspileExpression(varDecl.Value);
                }
            }
            return;
        }

        string typeStr = "void";
        if (!string.IsNullOrEmpty(varDecl.Typing)) {
            typeStr = MapType(varDecl.Typing);
        } else if (varDecl.Value != null && !string.IsNullOrEmpty(varDecl.Value.InferredType)) {
            typeStr = MapType(varDecl.Value.InferredType);
        }

        var isConst = varDecl.Kind == VariableKind.Constant;
        var prefix = GetConstPrefix(isConst, typeStr);

        if (_boxedVariables.Contains(varDecl.Identifier)) {
            var cleanType = CleanTypeName(typeStr);
            Write($"struct Ref_{cleanType}* {varDecl.Identifier} = (struct Ref_{cleanType}*)pino_malloc(sizeof(struct Ref_{cleanType}));\n");
            if (varDecl.Value != null) {
                WriteIndent();
                if (IsStringConcat(varDecl.Value)) {
                    Write($"{varDecl.Identifier}->value = (char*)pino_malloc(1024);\n");
                    var (format, args) = ProcessStringAddition(varDecl.Value);
                    var argsStr = args.Count > 0 ? ", " + string.Join(", ", args) : "";
                    WriteIndent();
                    Write($"snprintf((char*){varDecl.Identifier}->value, 1024, \"{EscapeString(format)}\"{argsStr})");
                } else {
                    Write($"{varDecl.Identifier}->value = ");
                    TranspileExpression(varDecl.Value);
                }
            }
            string pinoType2 = "";
            if (!string.IsNullOrEmpty(varDecl.Typing)) pinoType2 = varDecl.Typing;
            else if (varDecl.Value != null && !string.IsNullOrEmpty(varDecl.Value.InferredType)) pinoType2 = varDecl.Value.InferredType;
            _varTypes[varDecl.Identifier] = pinoType2;
            return;
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
                } else if (lit.LiteralType == LiteralType.Integer || lit.LiteralType == LiteralType.Float) {
                    Write(lit.Value.Replace("_", ""));
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
                            var resolvedType = ResolveTypeName(type);
                            var cleanType = CleanTypeName(resolvedType);
                            
                            if (FindEnum(resolvedType) != null || FindUnion(resolvedType) != null || _structFields.ContainsKey(resolvedType)) {
                                Write("pino_println_string(");
                                Write($"{resolvedType}_to_string(");
                                TranspileExpression(arg);
                                Write("))");
                            } else if (type.StartsWith("[]")) {
                                var vecClean = CleanTypeName(type);
                                Write("pino_println_string(");
                                Write($"{vecClean}_to_string(");
                                TranspileExpression(arg);
                                Write("))");
                            } else if (type == "int" || type == "bool") {
                                if (type == "bool") {
                                    Write("pino_println_string((");
                                    TranspileExpression(arg);
                                    Write(") ? \"True\" : \"False\")");
                                } else {
                                    Write("pino_println_int(");
                                    TranspileExpression(arg);
                                    Write(")");
                                }
                            } else if (type == "float") {
                                Write("pino_println_float(");
                                TranspileExpression(arg);
                                Write(")");
                            } else if (type == "rune") {
                                Write("printf(\"%c\\n\", ");
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
                } else if (call.Callee == "str") {
                    var arg = call.Arguments[0];
                    var type = arg.InferredType;
                    var resolvedType = ResolveTypeName(type);
                    var cleanType = CleanTypeName(resolvedType);
                    
                    if (FindEnum(resolvedType) != null || FindUnion(resolvedType) != null || _structFields.ContainsKey(resolvedType)) {
                        Write($"{resolvedType}_to_string(");
                        TranspileExpression(arg);
                        Write(")");
                    } else if (type == "int" || type == "bool") {
                        if (type == "bool") {
                            Write("((");
                            TranspileExpression(arg);
                            Write(") ? \"True\" : \"False\")");
                        } else {
                            Write("({ char* temp = (char*)pino_malloc(32); snprintf(temp, 32, \"%d\", ");
                            TranspileExpression(arg);
                            Write("); temp; })");
                        }
                    } else if (type == "float") {
                        Write("({ char* temp = (char*)pino_malloc(64); snprintf(temp, 64, \"%g\", ");
                        TranspileExpression(arg);
                        Write("); temp; })");
                    } else if (type == "rune") {
                        Write("({ char* temp = (char*)pino_malloc(8); snprintf(temp, 8, \"%c\", ");
                        TranspileExpression(arg);
                        Write("); temp; })");
                    } else if (type.StartsWith("[]")) {
                        var vecClean = CleanTypeName(type);
                        Write($"{vecClean}_to_string(");
                        TranspileExpression(arg);
                        Write(")");
                    } else {
                        TranspileExpression(arg);
                    }
                } else if (call.Callee == "int") {
                    var arg = call.Arguments[0];
                    if (arg.InferredType == "string") {
                        Write("({ ");
                        Write("const char* temp_str = ");
                        TranspileExpression(arg);
                        Write("; ");
                        Write("int parsed_val = 0; ");
                        Write("bool success = false; ");
                        Write("if (temp_str && *temp_str != '\\0') { ");
                        Write("    char* endptr; ");
                        Write("    parsed_val = (int)strtol(temp_str, &endptr, 10); ");
                        Write("    if (*endptr == '\\0' || *endptr == '.') success = true; ");
                        Write("} ");
                        Write("success ? ");
                        Write("Result_int_string_Success_construct(parsed_val) : ");
                        Write("Result_int_string_Failure_construct(\"Invalid integer format\"); })");
                    } else if (arg.InferredType == "float") {
                        Write("({ ");
                        Write("double temp_f = ");
                        TranspileExpression(arg);
                        Write("; ");
                        Write("Result_int_string_Success_construct((int)temp_f); })");
                    } else {
                        Write("({ ");
                        Write("int temp_i = ");
                        TranspileExpression(arg);
                        Write("; ");
                        Write("Result_int_string_Success_construct(temp_i); })");
                    }
                } else if (call.Callee == "float") {
                    var arg = call.Arguments[0];
                    if (arg.InferredType == "string") {
                        Write("({ ");
                        Write("const char* temp_str = ");
                        TranspileExpression(arg);
                        Write("; ");
                        Write("double parsed_val = 0.0; ");
                        Write("bool success = false; ");
                        Write("if (temp_str && *temp_str != '\\0') { ");
                        Write("    char* endptr; ");
                        Write("    parsed_val = strtod(temp_str, &endptr); ");
                        Write("    if (*endptr == '\\0') success = true; ");
                        Write("} ");
                        Write("success ? ");
                        Write("Result_float_string_Success_construct(parsed_val) : ");
                        Write("Result_float_string_Failure_construct(\"Invalid float format\"); })");
                    } else if (arg.InferredType == "int") {
                        Write("({ ");
                        Write("int temp_i = ");
                        TranspileExpression(arg);
                        Write("; ");
                        Write("Result_float_string_Success_construct((double)temp_i); })");
                    } else {
                        Write("({ ");
                        Write("double temp_f = ");
                        TranspileExpression(arg);
                        Write("; ");
                        Write("Result_float_string_Success_construct(temp_f); })");
                    }
                } else if (call.Callee == "time") {
                    Write("pino_time()");
                } else if (call.Callee == "rand") {
                    if (call.Arguments.Count == 0) {
                        Write("pino_rand_float()");
                    } else {
                        Write("pino_rand_int(");
                        TranspileExpression(call.Arguments[0]);
                        Write(")");
                    }
                } else if (call.Callee == "sleep") {
                    Write("pino_sleep(");
                    TranspileExpression(call.Arguments[0]);
                    Write(")");
                } else if (call.Callee == "clear") {
                    Write("pino_clear()");
                } else if (call.Callee == "regex") {
                    Write("regex_compile(");
                    TranspileExpression(call.Arguments[0]);
                    Write(")");
                } else if (call.Callee == "read_file") {
                    _usesReadFile = true;
                    Write("pino_read_file(");
                    TranspileExpression(call.Arguments[0]);
                    Write(")");
                } else if (call.Callee == "write_file") {
                    _usesWriteFile = true;
                    Write("pino_write_file(");
                    TranspileExpression(call.Arguments[0]);
                    Write(", ");
                    TranspileExpression(call.Arguments[1]);
                    Write(")");
                } else if (call.Callee == "file_exists") {
                    _usesFileExists = true;
                    Write("pino_file_exists(");
                    TranspileExpression(call.Arguments[0]);
                    Write(")");
                } else if (_varTypes.TryGetValue(call.Callee, out var pinoFnType) && pinoFnType.StartsWith("fn(")) {
                    var (paramTypes, retType) = ParseFunctionType(pinoFnType);
                    var cRetType = MapType(retType);
                    var cParamTypes = paramTypes.Select(p => MapType(p)).ToList();
                    
                    Write($"(({cRetType}(*)(void*");
                    foreach (var pt in cParamTypes) {
                        Write($", {pt}");
                    }
                    Write($"))");
                    TranspileClosureRef(call.Callee);
                    Write($".fn_ptr)(");
                    TranspileClosureRef(call.Callee);
                    Write($".env");
                    for (int i = 0; i < call.Arguments.Count; i++) {
                        Write(", ");
                        TranspileExpression(call.Arguments[i]);
                    }
                    Write(")");
                } else {
                    var callee = call.Callee;
                    if (_importedSymbols.TryGetValue(callee, out var mappedCallee)) {
                        Write($"{mappedCallee}(");
                    } else if (_currentModuleName != null && _moduleFunctions.TryGetValue(_currentModuleName, out var fns) && fns.Contains(callee)) {
                        Write($"{_currentModuleName}_{callee}(");
                    } else {
                        Write($"{callee}(");
                    }
                    for (int i = 0; i < call.Arguments.Count; i++) {
                        if (i > 0) Write(", ");
                        TranspileExpression(call.Arguments[i]);
                    }
                    Write(")");
                }
                break;

            case BinaryExpression bin:
                if (bin.Operator == OperatorType.Assignment) {
                    if (bin.Left is IndexAccessExpression idx && idx.Target.InferredType != null && idx.Target.InferredType.StartsWith("map[")) {
                        var cleanType = CleanTypeName(idx.Target.InferredType);
                        Write($"{cleanType}_set(");
                        TranspileExpression(idx.Target);
                        Write(", ");
                        TranspileExpression(idx.Index);
                        Write(", ");
                        TranspileExpression(bin.Right);
                        Write(")");
                    } else if (IsStringConcat(bin.Right)) {
                        var (format, args) = ProcessStringAddition(bin.Right);
                        var argsStr = args.Count > 0 ? ", " + string.Join(", ", args) : "";
                        Write("snprintf(");
                        TranspileExpression(bin.Left);
                        Write($", 1024, \"{EscapeString(format)}\"{argsStr})");
                    } else {
                        Write("(");
                        TranspileExpression(bin.Left);
                        Write(" = ");
                        TranspileExpression(bin.Right);
                        Write(")");
                    }
                } else if (bin.Operator == OperatorType.Addition && bin.InferredType == "string") {
                    var (format, args) = ProcessStringAddition(bin);
                    var argsStr = args.Count > 0 ? ", " + string.Join(", ", args) : "";
                    Write("({ char* temp = (char*)pino_malloc(1024); ");
                    Write($"snprintf(temp, 1024, \"{EscapeString(format)}\"{argsStr}); temp; }})");
                } else if (bin.Operator == OperatorType.StaticMemberAccess) {
                    if (bin.Left is IdentifierExpression structId) {
                        string name = structId.Name;
                        if ((name == "Result" || name == "IOError") && !string.IsNullOrEmpty(bin.InferredType) && (bin.InferredType.StartsWith("Result") || bin.InferredType.StartsWith("IOError"))) {
                            name = CleanTypeName(bin.InferredType);
                        } else if (_importedSymbols.TryGetValue(name, out var mappedName)) {
                            name = mappedName;
                        } else {
                            name = GetPrefixedName(name);
                        }
                        if (FindUnion(name) != null) {
                            if (bin.Right is FunctionCallExpression methodCall) {
                                Write($"{name}_{methodCall.Callee}_construct(");
                                for (int i = 0; i < methodCall.Arguments.Count; i++) {
                                    if (i > 0) Write(", ");
                                    TranspileExpression(methodCall.Arguments[i]);
                                }
                                Write(")");
                            } else if (bin.Right is IdentifierExpression methodId) {
                                var unionDecl = FindUnion(name)!;
                                var variant = unionDecl.Variants.Find(v => v.Identifier == methodId.Name);
                                if (variant != null && variant.AssociatedTypes.Count > 0) {
                                    Write($"{name}_{methodId.Name}_construct");
                                } else {
                                    Write($"({{ {name}* temp = ({name}*)pino_malloc(sizeof({name})); temp->tag = {name}Tag_{methodId.Name}; temp; }})");
                                }
                            }
                        } else if (FindEnum(name) != null) {
                            if (bin.Right is IdentifierExpression memberId) {
                                Write($"{name}_{memberId.Name}");
                            }
                        } else if (_structFields.ContainsKey(name)) {
                            if (bin.Right is FunctionCallExpression call) {
                                Write($"{name}_{call.Callee}(");
                                for (int i = 0; i < call.Arguments.Count; i++) {
                                    if (i > 0) Write(", ");
                                    TranspileExpression(call.Arguments[i]);
                                }
                                Write(")");
                            } else if (bin.Right is IdentifierExpression id) {
                                Write($"{name}_{id.Name}");
                            }
                        } else if (_allModuleCheckers.ContainsKey(name)) {
                            if (bin.Right is FunctionCallExpression call) {
                                Write($"{name}_{call.Callee}(");
                                for (int i = 0; i < call.Arguments.Count; i++) {
                                    if (i > 0) Write(", ");
                                    TranspileExpression(call.Arguments[i]);
                                }
                                Write(")");
                            } else if (bin.Right is IdentifierExpression id) {
                                Write($"{name}_{id.Name}");
                            } else if (bin.Right is StructInstanceExpression structInst) {
                                var cleanType = CleanTypeName(structInst.StructName);
                                Write($"({{ {cleanType}* temp = ({cleanType}*)pino_malloc(sizeof({cleanType})); ");
                                for (int i = 0; i < structInst.Properties.Count; i++) {
                                    var prop = structInst.Properties[i];
                                    Write($"temp->{prop.Identifier} = ");
                                    TranspileExpression(prop.Value!);
                                    Write("; ");
                                }
                                Write("temp; })");
                            }
                        }
                    }
                } else if (bin.Operator == OperatorType.MemberAccess) {
                    if (bin.Left.InferredType == "string" && bin.Right is IdentifierExpression strId && (strId.Name == "len" || strId.Name == "length")) {
                        Write("string_len(");
                        TranspileExpression(bin.Left);
                        Write(")");
                    } else if (bin.Left.InferredType != null && bin.Left.InferredType.StartsWith("[]")) {
                        if (bin.Right is FunctionCallExpression call && (call.Callee == "push" || call.Callee == "add")) {
                            var cleanType = CleanTypeName(bin.Left.InferredType);
                            Write($"{cleanType}_push(");
                            TranspileExpression(bin.Left);
                            Write(", ");
                            TranspileExpression(call.Arguments[0]);
                            Write(")");
                        } else if (bin.Right is IdentifierExpression id && (id.Name == "len" || id.Name == "length")) {
                            TranspileExpression(bin.Left);
                            Write("->length");
                        } else {
                            throw new NotImplementedException($"Member method/property '{bin.Right}' not implemented on vectors.");
                        }
                    } else if (bin.Left.InferredType != null && bin.Left.InferredType.StartsWith("map[")) {
                        if (bin.Right is IdentifierExpression id && (id.Name == "len" || id.Name == "length")) {
                            TranspileExpression(bin.Left);
                            Write("->size");
                        } else if (bin.Right is FunctionCallExpression call) {
                            var structName = CleanTypeName(bin.Left.InferredType);
                            Write($"{structName}_{call.Callee}(");
                            if (bin.Left is IdentifierExpression lid && (lid.Name == "this" || lid.Name == "self")) {
                                Write("this");
                            } else {
                                TranspileExpression(bin.Left);
                            }
                            for (int i = 0; i < call.Arguments.Count; i++) {
                                Write(", ");
                                TranspileExpression(call.Arguments[i]);
                            }
                            Write(")");
                        } else {
                            throw new NotImplementedException($"Member method/property '{bin.Right}' not implemented on maps.");
                        }
                    } else if (bin.Right is FunctionCallExpression call) {
                        var structName = CleanTypeName(bin.Left.InferredType!);
                        if (structName == "string" && call.Callee == "substring") {
                            Write("string_substring(");
                            TranspileExpression(bin.Left);
                            Write(", ");
                            TranspileExpression(call.Arguments[0]);
                            Write(", ");
                            if (call.Arguments.Count > 1) {
                                TranspileExpression(call.Arguments[1]);
                            } else {
                                Write("string_len(");
                                TranspileExpression(bin.Left);
                                Write(") - ");
                                TranspileExpression(call.Arguments[0]);
                            }
                            Write(")");
                        } else {
                            Write($"{structName}_{call.Callee}(");
                            
                            if (bin.Left is IdentifierExpression id && (id.Name == "this" || id.Name == "self")) {
                                Write("this");
                            } else {
                                TranspileExpression(bin.Left);
                            }

                            for (int i = 0; i < call.Arguments.Count; i++) {
                                Write(", ");
                                TranspileExpression(call.Arguments[i]);
                            }
                            Write(")");
                        }
                    } else {
                        TranspileExpression(bin.Left);
                        Write("->");
                        
                        if (bin.Right is IdentifierExpression rightId) {
                            Write(rightId.Name);
                        } else {
                            TranspileExpression(bin.Right);
                        }
                    }
                } else if (bin.Operator == OperatorType.In || bin.Operator == OperatorType.NotIn) {
                    if (bin.Operator == OperatorType.NotIn) {
                        Write("!(");
                    }
                    if (bin.Right.InferredType != null && bin.Right.InferredType.StartsWith("map[")) {
                        var cleanType = CleanTypeName(bin.Right.InferredType);
                        Write($"{cleanType}_has(");
                        TranspileExpression(bin.Right);
                        Write(", ");
                        TranspileExpression(bin.Left);
                        Write(")");
                    } else if (bin.Right.InferredType != null && bin.Right.InferredType.StartsWith("[]")) {
                        var cleanType = CleanTypeName(bin.Right.InferredType);
                        Write($"{cleanType}_has(");
                        TranspileExpression(bin.Right);
                        Write(", ");
                        TranspileExpression(bin.Left);
                        Write(")");
                    } else if (bin.Right.InferredType == "string") {
                        Write("(strstr(");
                        TranspileExpression(bin.Right);
                        Write(", ");
                        TranspileExpression(bin.Left);
                        Write(") != NULL)");
                    } else {
                        throw new NotImplementedException($"Operator {bin.Operator} not supported for type {bin.Right.InferredType}");
                    }
                    if (bin.Operator == OperatorType.NotIn) {
                        Write(")");
                    }
                } else if ((bin.Operator == OperatorType.Equal ||
                            bin.Operator == OperatorType.NotEqual ||
                            bin.Operator == OperatorType.LessThan ||
                            bin.Operator == OperatorType.LessThanEqual ||
                            bin.Operator == OperatorType.GreaterThan ||
                            bin.Operator == OperatorType.GreaterThanEqual) &&
                           (bin.Left.InferredType == "string" || bin.Right.InferredType == "string")) {
                    Write("(");
                    Write("strcmp(");
                    TranspileExpression(bin.Left);
                    Write(", ");
                    TranspileExpression(bin.Right);
                    Write(")");
                    var opStr = bin.Operator switch {
                        OperatorType.Equal => "== 0",
                        OperatorType.NotEqual => "!= 0",
                        OperatorType.LessThan => "< 0",
                        OperatorType.LessThanEqual => "<= 0",
                        OperatorType.GreaterThan => "> 0",
                        OperatorType.GreaterThanEqual => ">= 0",
                        _ => throw new NotImplementedException()
                    };
                    Write($" {opStr}");
                    Write(")");
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

            case BubbleExpression bub:
                {
                    var innerType = bub.Value.InferredType!;
                    var cleanInner = CleanTypeName(innerType);
                    var cleanOuter = CleanTypeName(_currentReturnType);
                    var varName = $"_pino_bub_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

                    Write($"({{ {cleanInner}* {varName} = ");
                    TranspileExpression(bub.Value);
                    Write("; ");

                    if (innerType.StartsWith("Result[") || innerType.StartsWith("Result_")) {
                        Write($"if ({varName}->tag == {cleanInner}Tag_Failure) ");
                        if (cleanInner == cleanOuter) {
                            Write($"return {varName}; ");
                        } else {
                            Write($"return {cleanOuter}_Failure_construct({varName}->value.Failure._0); ");
                        }
                        Write($"{varName}->value.Success._0; }})");
                    } else { // Option
                        Write($"if ({varName}->tag == {cleanInner}Tag_None) ");
                        if (cleanInner == cleanOuter) {
                            Write($"return {varName}; ");
                        } else {
                            Write($"return ({{ {cleanOuter}* temp = ({cleanOuter}*)pino_malloc(sizeof({cleanOuter})); temp->tag = {cleanOuter}Tag_None; temp; }}); ");
                        }
                        Write($"{varName}->value.Some._0; }})");
                    }
                }
                break;

            case IdentifierExpression id:
                if (_importedSymbols.TryGetValue(id.Name, out var mappedId)) {
                    Write(mappedId);
                } else if (_currentStructFields.Contains(id.Name) && !_varTypes.ContainsKey(id.Name)) {
                    Write($"this->{id.Name}");
                } else if (_currentLambda != null && _currentLambda.FreeVars.Contains(id.Name)) {
                    if (_boxedVariables.Contains(id.Name)) {
                        Write($"env->{id.Name}->value");
                    } else {
                        Write($"env->{id.Name}");
                    }
                } else if (_boxedVariables.Contains(id.Name)) {
                    Write($"{id.Name}->value");
                } else {
                    if (_currentModuleName != null && _globalVarTypes.ContainsKey($"{_currentModuleName}_{id.Name}")) {
                        Write($"{_currentModuleName}_{id.Name}");
                    } else {
                        Write(id.Name);
                    }
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
                {
                    var instStructName = inst.StructName;
                    if (_importedSymbols.TryGetValue(instStructName, out var mappedStructName)) {
                        instStructName = mappedStructName;
                    } else if (_currentModuleName != null) {
                        var pref = $"{_currentModuleName}_{instStructName}";
                        if (_structFields.ContainsKey(pref)) {
                            instStructName = pref;
                        }
                    }
                    instStructName = CleanTypeName(instStructName);
                    Write($"({{ {instStructName}* temp = ({instStructName}*)pino_malloc(sizeof({instStructName})); ");
                    for (int i = 0; i < inst.Properties.Count; i++) {
                        var prop = inst.Properties[i];
                        Write($"temp->{prop.Identifier} = ");
                        TranspileExpression(prop.Value!);
                        Write("; ");
                    }
                    Write("temp; })");
                }
                break;

            case FunctionLambdaExpression lambda:
                {
                    if (lambda.FreeVars.Count == 0) {
                        Write($"((PinoClosure){{ .fn_ptr = (void*){lambda.LambdaId}, .env = NULL }})");
                    } else {
                        var envVar = $"_pino_env_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                        Write($"({{ struct {lambda.LambdaId}_env* {envVar} = (struct {lambda.LambdaId}_env*)pino_malloc(sizeof(struct {lambda.LambdaId}_env)); ");
                        foreach (var v in lambda.FreeVars) {
                            if (_boxedVariables.Contains(v)) {
                                if (_lambdaStack.Count > 0) {
                                    Write($"{envVar}->{v} = env->{v}; ");
                                } else {
                                    Write($"{envVar}->{v} = {v}; ");
                                }
                            } else {
                                Write($"{envVar}->{v} = ");
                                if (_lambdaStack.Count > 0 && _lambdaStack.Peek().FreeVars.Contains(v)) {
                                    Write($"env->{v}; ");
                                } else {
                                    Write($"{v}; ");
                                }
                            }
                        }
                        Write($"((PinoClosure){{ .fn_ptr = (void*){lambda.LambdaId}, .env = {envVar} }}); }})");
                    }
                }
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

            case VectorExpression vec:
                {
                    var elemType = vec.InferredType!.Substring(2);
                    var cElemType = MapType(elemType);
                    var cleanType = CleanTypeName(vec.InferredType!);
                    
                    // Force registration
                    MapType(vec.InferredType!);

                    if (vec.Elements != null && vec.Elements.Count > 0) {
                        Write($"({{ {MapType(vec.InferredType!)} temp = {cleanType}_construct({vec.Elements.Count}); ");
                        for (int i = 0; i < vec.Elements.Count; i++) {
                            Write($"temp->items[{i}] = ");
                            TranspileExpression(vec.Elements[i]);
                            Write("; ");
                        }
                        Write("temp; })");
                    } else if (vec.Len != null && vec.Init != null) {
                        var limitVar = $"_pino_limit_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                        Write($"({{ int {limitVar} = ");
                        TranspileExpression(vec.Len);
                        Write($"; {MapType(vec.InferredType!)} temp = {cleanType}_construct({limitVar}); ");
                        
                        bool hadIt = _varTypes.TryGetValue("it", out var oldIt);
                        _varTypes["it"] = "int";

                        Write($"for (int it = 0; it < {limitVar}; it++) {{ temp->items[it] = ");
                        TranspileExpression(vec.Init);
                        Write("; } ");

                        if (hadIt && oldIt != null) _varTypes["it"] = oldIt;
                        else _varTypes.Remove("it");

                        Write("temp; })");
                    } else {
                        Write($"{cleanType}_construct(0)");
                    }
                }
                break;

            case MapExpression map:
                {
                    var cleanType = CleanTypeName(map.InferredType!);
                    MapType(map.InferredType!);
                    Write($"({{ {cleanType}* temp = {cleanType}_construct(); ");
                    foreach (var entry in map.Entries) {
                        Write($"{cleanType}_set(temp, ");
                        TranspileExpression(entry.Key);
                        Write(", ");
                        TranspileExpression(entry.Value);
                        Write("); ");
                    }
                    Write("temp; })");
                }
                break;

            case IndexAccessExpression idx:
                {
                    var targetType = idx.Target.InferredType!;
                    if (targetType.StartsWith("[]")) {
                        TranspileExpression(idx.Target);
                        Write("->items[");
                        TranspileExpression(idx.Index);
                        Write("]");
                    } else if (targetType.StartsWith("map[")) {
                        var cleanType = CleanTypeName(targetType);
                        Write($"{cleanType}_get(");
                        TranspileExpression(idx.Target);
                        Write(", ");
                        TranspileExpression(idx.Index);
                        Write(")");
                    } else {
                        throw new NotImplementedException("Map or custom index access is not implemented in transpilation.");
                    }
                }
                break;

            case MatchStatement match:
                {
                    Write("({ ");
                    var condVar = $"_pino_match_cond_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                    var condType = match.Condition.InferredType!;
                    Write($"{MapType(condType)} {condVar} = ");
                    TranspileExpression(match.Condition);
                    Write("; ");

                    string resVar = "";
                    if (match.InferredType != "void" && match.InferredType != "any" && !string.IsNullOrEmpty(match.InferredType)) {
                        resVar = $"_pino_match_res_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                        Write($"{MapType(match.InferredType)} {resVar}; ");
                        _matchResultVars.Push(resVar);
                    }

                    for (int i = 0; i < match.Branches.Count; i++) {
                        var branch = match.Branches[i];
                        if (i > 0) Write(" else ");
                        Write("if (");

                        var branchConds = new List<string>();
                        var bindingList = new List<string>();
                        foreach (var condPat in branch.Conditions) {
                            var pConds = new List<string>();
                            var pBindings = new List<string>();
                            BuildPatternMatch(condPat, condVar, condType, pConds, pBindings);
                            string cStr = pConds.Count > 0 ? string.Join(" && ", pConds) : "1";
                            branchConds.Add(cStr);
                            bindingList.AddRange(pBindings);
                        }

                        Write(string.Join(" || ", branchConds));
                        Write(") { ");

                        foreach (var bind in bindingList) {
                            Write(bind + " ");
                        }

                        if (branch.Body is BlockStatement block) {
                            foreach (var s in block.Statements) {
                                var oldSb = _sb;
                                _sb = new StringBuilder();
                                TranspileStatement(s);
                                var sStr = _sb.ToString().Trim();
                                _sb = oldSb;
                                Write(sStr + " ");
                            }
                        } else if (branch.Body is Expression branchExpr) {
                            if (!string.IsNullOrEmpty(resVar)) {
                                Write($"{resVar} = ");
                                TranspileExpression(branchExpr);
                                Write("; ");
                            } else {
                                TranspileExpression(branchExpr);
                                Write("; ");
                            }
                        } else {
                            TranspileStatement(branch.Body);
                        }

                        Write("}");
                    }

                    if (match.Alternate != null) {
                        Write(" else { ");
                        if (match.Alternate.Body is BlockStatement block) {
                            foreach (var s in block.Statements) {
                                var oldSb = _sb;
                                _sb = new StringBuilder();
                                TranspileStatement(s);
                                var sStr = _sb.ToString().Trim();
                                _sb = oldSb;
                                Write(sStr + " ");
                            }
                        } else if (match.Alternate.Body is Expression branchExpr) {
                            if (!string.IsNullOrEmpty(resVar)) {
                                Write($"{resVar} = ");
                                TranspileExpression(branchExpr);
                                Write("; ");
                            } else {
                                TranspileExpression(branchExpr);
                                Write("; ");
                            }
                        } else {
                            TranspileStatement(match.Alternate.Body);
                        }
                        Write("}");
                    }

                    if (!string.IsNullOrEmpty(resVar)) {
                        Write($" {resVar}; ");
                        _matchResultVars.Pop();
                    }

                    Write("})");
                }
                break;

            case IsExpression isExpr:
                {
                    var oldSb = _sb;
                    _sb = new StringBuilder();
                    TranspileExpression(isExpr.Value);
                    var lhsTarget = _sb.ToString();
                    _sb = oldSb;

                    var condList = new List<string>();
                    var bindingList = new List<string>();
                    string targetType = isExpr.Value.InferredType ?? "any";
                    BuildPatternMatch(isExpr.Pattern, lhsTarget, targetType, condList, bindingList, isInExpression: true);

                    var condStr = condList.Count > 0 ? string.Join(" && ", condList) : "true";
                    if (isExpr.IsNot) {
                        condStr = $"!({condStr})";
                    }
                    Write(condStr);
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
                var type = op.InferredType ?? "any";
                var resolvedType = ResolveTypeName(type);
                var cleanType = CleanTypeName(resolvedType);
                
                var specifier = "%s";
                if (type == "int") specifier = "%d";
                else if (type == "float") specifier = "%g";
                else if (type == "rune") specifier = "%c";
                
                formatSb.Append(specifier);
                
                var oldSb = _sb;
                _sb = new StringBuilder();
                
                if (FindEnum(resolvedType) != null || FindUnion(resolvedType) != null || _structFields.ContainsKey(resolvedType)) {
                    Write($"{resolvedType}_to_string(");
                    TranspileExpression(op);
                    Write(")");
                } else if (type.StartsWith("[]")) {
                    var vecClean = CleanTypeName(type);
                    Write($"{vecClean}_to_string(");
                    TranspileExpression(op);
                    Write(")");
                } else if (type == "bool") {
                    Write("(");
                    TranspileExpression(op);
                    Write(" ? \"True\" : \"False\")");
                } else {
                    TranspileExpression(op);
                }
                
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

        var boundVars = new List<(string Name, string Type)>();
        ExtractBoundVariables(ifs.Condition, boundVars, true);

        if (boundVars.Count > 0) {
            Write("{\n");
            _indent++;
            foreach (var bv in boundVars) {
                WriteIndent();
                Write($"{MapType(bv.Type)} {bv.Name} = 0;\n");
                _varTypes[bv.Name] = bv.Type;
            }
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

        if (boundVars.Count > 0) {
            Write("\n");
            _indent--;
            WriteIndent();
            Write("}");
        }
    }

    private void TranspileBlockOrStatement(Statement stmt) {
        _blockDepth++;
        try {
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
        } finally {
            _blockDepth--;
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
                {
                    var collExpr = loop.End!;
                    var collType = collExpr.InferredType!;
                    var valId = (loop.Begin as IdentifierExpression)!.Name;
                    var keyId = loop.KeyVar;

                    var collVar = $"_pino_coll_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                    var idxVar = $"_pino_idx_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                    bool isMap = collType.StartsWith("map[");

                    WriteIndent();
                    WriteLine("{");
                    _indent++;

                    if (collType.StartsWith("[]")) {
                        var elemType = collType.Substring(2);
                        var cElemType = MapType(elemType);
                        var cleanType = CleanTypeName(collType);

                        WriteIndent();
                        Write($"{cleanType}* {collVar} = ");
                        TranspileExpression(collExpr);
                        _sb.AppendLine(";");

                        WriteIndent();
                        WriteLine($"for (int {idxVar} = 0; {idxVar} < {collVar}->length; {idxVar}++) {{");
                        _indent++;

                        WriteIndent();
                        WriteLine($"{cElemType} {valId} = {collVar}->items[{idxVar}];");
                        if (!string.IsNullOrEmpty(keyId)) {
                            WriteIndent();
                            WriteLine($"int {keyId} = {idxVar};");
                        }
                    } else if (collType.StartsWith("map[")) {
                        int commaIdx = collType.IndexOf(',');
                        string keyType = collType.Substring(4, commaIdx - 4).Trim();
                        string valType = collType.Substring(commaIdx + 1, collType.Length - commaIdx - 2).Trim();
                        string cKeyType = MapType(keyType);
                        string cValType = MapType(valType);
                        var cleanType = CleanTypeName(collType);

                        WriteIndent();
                        Write($"{cleanType}* {collVar} = ");
                        TranspileExpression(collExpr);
                        _sb.AppendLine(";");

                        WriteIndent();
                        WriteLine($"for (int {idxVar} = 0; {idxVar} < {collVar}->capacity; {idxVar}++) {{");
                        _indent++;
                        WriteIndent();
                        WriteLine($"if ({collVar}->entries[{idxVar}].occupied == 1) {{");
                        _indent++;

                        if (string.IsNullOrEmpty(keyId)) {
                            WriteIndent();
                            WriteLine($"{cKeyType} {valId} = {collVar}->entries[{idxVar}].key;");
                        } else {
                            WriteIndent();
                            WriteLine($"{cValType} {valId} = {collVar}->entries[{idxVar}].value;");
                            WriteIndent();
                            WriteLine($"{cKeyType} {keyId} = {collVar}->entries[{idxVar}].key;");
                        }
                    } else if (collType == "string") {
                        WriteIndent();
                        Write($"const char* {collVar} = ");
                        TranspileExpression(collExpr);
                        _sb.AppendLine(";");

                        var lenVar = $"_pino_len_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                        WriteIndent();
                        WriteLine($"int {lenVar} = strlen({collVar});");

                        WriteIndent();
                        WriteLine($"for (int {idxVar} = 0; {idxVar} < {lenVar}; {idxVar}++) {{");
                        _indent++;

                        WriteIndent();
                        WriteLine($"char {valId} = {collVar}[{idxVar}];");
                        if (!string.IsNullOrEmpty(keyId)) {
                            WriteIndent();
                            WriteLine($"int {keyId} = {idxVar};");
                        }
                    } else {
                        // Integer range fallback
                        WriteIndent();
                        Write($"int {collVar} = (int)(");
                        TranspileExpression(collExpr);
                        _sb.AppendLine(");");

                        WriteIndent();
                        WriteLine($"for (int {idxVar} = 0; {idxVar} < {collVar}; {idxVar}++) {{");
                        _indent++;

                        WriteIndent();
                        WriteLine($"int {valId} = {idxVar};");
                        if (!string.IsNullOrEmpty(keyId)) {
                            WriteIndent();
                            WriteLine($"int {keyId} = {idxVar};");
                        }
                    }

                    // Loop body
                    TranspileStatement(loop.Body);

                    if (isMap) {
                        _indent--;
                        WriteIndent();
                        WriteLine("}"); // Close if
                    }

                    _indent--;
                    WriteIndent();
                    WriteLine("}"); // Close for

                    _indent--;
                    WriteIndent();
                    WriteLine("}"); // Close scope block
                }
                break;
        }
    }

    private string EscapeString(string value) {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private string GetHashFunction(string keyType, string val) {
        if (keyType == "string" || keyType == "const char*") {
            return $"pino_string_hash({val})";
        }
        return $"((unsigned long)({val}))";
    }

    private string CleanTypeName(string pinoType) {
        pinoType = ResolveTypeName(pinoType);
        if (pinoType.StartsWith("[]")) {
            return "Vector_" + CleanTypeName(pinoType.Substring(2));
        }
        if (pinoType.StartsWith("@(")) {
            return pinoType.Replace("@", "tuple").Replace("(", "").Replace(")", "").Replace(":", "_").Replace(",", "_").Replace(" ", "");
        }
        if (pinoType.StartsWith("map[")) {
            return pinoType.Replace("map[", "map_").Replace("]", "").Replace(",", "_").Replace(" ", "");
        }
        if (pinoType.StartsWith("fn(")) {
            return pinoType.Replace("fn(", "fn_").Replace(")", "").Replace(",", "_").Replace(" ", "");
        }
        if (pinoType.Contains("[") && pinoType.EndsWith("]")) {
            int bracketIdx = pinoType.IndexOf('[');
            var baseName = pinoType.Substring(0, bracketIdx);
            var argsStr = pinoType.Substring(bracketIdx + 1, pinoType.Length - bracketIdx - 2);
            var resolvedBase = ResolveTypeName(baseName);
            var pinoArgs = argsStr.Split(',').Select(a => {
                var arg = a.Trim();
                var resolvedArg = ResolveTypeName(arg);
                return CleanTypeName(resolvedArg);
            }).ToList();
            var finalCleanName = resolvedBase + "_" + string.Join("_", pinoArgs);
            return ResolveTypeName(finalCleanName);
        }
        return pinoType;
    }

    public UnionDeclaration? FindUnion(string name) {
        if (_unions.TryGetValue(name, out var u)) return u;
        return null;
    }

    public EnumDeclaration? FindEnum(string name) {
        if (_enums.TryGetValue(name, out var e)) return e;
        return null;
    }

    private void BuildPatternMatch(Pattern pattern, string target, string targetType, List<string> condList, List<string> bindingList, bool isInExpression = false) {
        switch (pattern) {
            case LiteralPattern litPat:
                {
                    var oldSb = _sb;
                    _sb = new StringBuilder();
                    TranspileExpression(litPat.Value);
                    var litStr = _sb.ToString();
                    _sb = oldSb;
                    condList.Add($"({target} == {litStr})");
                }
                break;

            case IdentifierPattern idPat:
                {
                    if (idPat.Name != "_") {
                        if (isInExpression) {
                            condList.Add($"({idPat.Name} = {target}, 1)");
                        } else {
                            bindingList.Add($"const {MapType(targetType)} {idPat.Name} = {target};");
                        }
                    }
                }
                break;

            case VariantPattern varPat:
                {
                    var unionName = ResolveTypeName(varPat.UnionName);
                    var unionDecl = FindUnion(unionName);
                    if (unionDecl == null) {
                        var enumDecl = FindEnum(unionName);
                        if (enumDecl != null) {
                            condList.Add($"({target} == {unionName}_{varPat.VariantName})");
                        }
                        break;
                    }
                    condList.Add($"({target}->tag == {unionName}Tag_{varPat.VariantName})");
                    var variant = unionDecl.Variants.Find(v => v.Identifier == varPat.VariantName);
                    if (variant != null) {
                        for (int i = 0; i < varPat.SubPatterns.Count; i++) {
                            var subPat = varPat.SubPatterns[i];
                            var subTarget = $"{target}->value.{varPat.VariantName}._{i}";
                            var subTargetType = variant.AssociatedTypes[i];
                            BuildPatternMatch(subPat, subTarget, subTargetType, condList, bindingList, isInExpression);
                        }
                    }
                }
                break;
        }
    }

    private void ExtractBoundVariables(Expression expr, List<(string Name, string Type)> boundVars, bool active) {
        if (expr is IsExpression isExpr) {
            if (active && !isExpr.IsNot) {
                string lhsType = isExpr.Value.InferredType ?? "any";
                CollectPatternVariables(isExpr.Pattern, lhsType, boundVars);
            }
        } else if (expr is UnaryExpression un && un.Operator == OperatorType.Not) {
            ExtractBoundVariables(un.Right, boundVars, !active);
        } else if (expr is BinaryExpression bin) {
            if (bin.Operator == OperatorType.And) {
                ExtractBoundVariables(bin.Left, boundVars, active);
                ExtractBoundVariables(bin.Right, boundVars, active);
            }
        }
    }

    private void CollectPatternVariables(Pattern pattern, string targetType, List<(string Name, string Type)> vars) {
        switch (pattern) {
            case IdentifierPattern id:
                if (id.Name != "_") {
                    vars.Add((id.Name, targetType));
                }
                break;
            case VariantPattern varPat:
                var unionDecl = FindUnion(varPat.UnionName);
                if (unionDecl != null) {
                    var variant = unionDecl.Variants.Find(v => v.Identifier == varPat.VariantName);
                    if (variant != null) {
                        for (int i = 0; i < Math.Min(varPat.SubPatterns.Count, variant.AssociatedTypes.Count); i++) {
                            CollectPatternVariables(varPat.SubPatterns[i], variant.AssociatedTypes[i], vars);
                        }
                    }
                }
                break;
        }
    }

    private int _lambdaCounter = 0;
    private List<FunctionLambdaExpression> _lambdas = new List<FunctionLambdaExpression>();
    private HashSet<string> _boxedVariables = new HashSet<string>();
    private Dictionary<string, string> _boxedTypes = new Dictionary<string, string>();
    
    public bool EnableTests { get; set; } = false;
    private List<(TestDeclaration Test, string? ModuleName)> _tests = new List<(TestDeclaration Test, string? ModuleName)>();

    private void CollectAndAnalyzeLambdas(ProgramStatement program) {
        _lambdaCounter = 0;
        _lambdas.Clear();
        _boxedVariables.Clear();
        _boxedTypes.Clear();

        foreach (var stmt in program.Statements) {
            if (stmt is TestDeclaration testDecl) {
                if (_currentModuleName == null) {
                    _tests.Add((testDecl, _currentModuleName));
                }
            }
        }
        _lambdas.Clear();
        _boxedVariables.Clear();
        _boxedTypes.Clear();

        var userFunctions = new HashSet<string>();
        foreach (var stmt in program.Statements) {
            if (stmt is FunctionDeclaration fnDecl) {
                userFunctions.Add(fnDecl.Identifier);
            } else if (stmt is StructDeclaration structDecl) {
                foreach (var method in structDecl.Methods) {
                    userFunctions.Add(method.Identifier);
                }
            }
        }

        // Pass A: Find all lambdas and assign LambdaId
        foreach (var stmt in program.Statements) {
            FindLambdas(stmt);
        }

        // Pass B: Analyze free variables for each lambda
        foreach (var lambda in _lambdas) {
            var localScope = new HashSet<string>(lambda.Parameters.Select(p => p.Identifier));
            var free = new HashSet<string>();
            AnalyzeFreeVars(lambda.Body, localScope, free);
            lambda.FreeVars.Clear();
            foreach (var f in free) {
                // If it is not a global constant or function name
                if (!_globalVarTypes.ContainsKey(f) && !userFunctions.Contains(f) && !BuiltInFunctionNames.Contains(f)) {
                    lambda.FreeVars.Add(f);
                }
            }
        }

        // Pass C: Detect which variables escape and are mutable (require boxing)
        foreach (var lambda in _lambdas) {
            foreach (var v in lambda.FreeVars) {
                if (IsMutableVariable(program, v)) {
                    _boxedVariables.Add(v);
                    var t = FindVariableType(program, v);
                    _boxedTypes[v] = t;
                }
            }
        }
    }

    private string FindVariableType(object node, string name) {
        if (node == null) return "any";
        if (node is VariableDeclaration varDecl && varDecl.Identifier == name) {
            if (!string.IsNullOrEmpty(varDecl.Typing)) return varDecl.Typing;
            if (varDecl.Value != null && !string.IsNullOrEmpty(varDecl.Value.InferredType)) return varDecl.Value.InferredType;
            return "any";
        }
        if (node is TupleDestructuringDeclaration dest) {
            var tupleType = dest.Value.InferredType!;
            var fieldTypes = ParseTupleFields(tupleType);
            foreach (var field in dest.Fields) {
                if (field.Identifier == name) {
                    return fieldTypes[field.Label];
                }
            }
        }

        switch (node) {
            case ProgramStatement prog:
                foreach (var s in prog.Statements) {
                    var r = FindVariableType(s, name);
                    if (r != "any") return r;
                }
                break;
            case BlockStatement block:
                foreach (var s in block.Statements) {
                    var r = FindVariableType(s, name);
                    if (r != "any") return r;
                }
                break;
            case IfStatement ifs:
                var r1 = FindVariableType(ifs.Consequent, name);
                if (r1 != "any") return r1;
                var r2 = FindVariableType(ifs.Alternate, name);
                if (r2 != "any") return r2;
                break;
            case LoopStatement loop:
                var l1 = FindVariableType(loop.Body, name);
                if (l1 != "any") return l1;
                var l2 = FindVariableType(loop.Begin, name);
                if (l2 != "any") return l2;
                var l3 = FindVariableType(loop.End, name);
                if (l3 != "any") return l3;
                break;
            case TestDeclaration testDecl:
                return FindVariableType(testDecl.Body, name);
            case FunctionDeclaration fn:
                foreach (var p in fn.Parameters) {
                    if (p.Identifier == name) {
                        return !string.IsNullOrEmpty(p.Typing) ? p.Typing : "any";
                    }
                }
                return FindVariableType(fn.Body, name);
            case FunctionLambdaExpression lambda:
                foreach (var p in lambda.Parameters) {
                    if (p.Identifier == name) {
                        return !string.IsNullOrEmpty(p.Typing) ? p.Typing : "any";
                    }
                }
                return FindVariableType(lambda.Body, name);
            case StructDeclaration str:
                foreach (var m in str.Methods) {
                    var r = FindVariableType(m, name);
                    if (r != "any") return r;
                }
                break;
            case MatchStatement mat:
                foreach (var branch in mat.Branches) {
                    var r = FindVariableType(branch.Body, name);
                    if (r != "any") return r;
                }
                if (mat.Alternate != null) {
                    var r = FindVariableType(mat.Alternate.Body, name);
                    if (r != "any") return r;
                }
                break;
        }
        return "any";
    }

    private void FindLambdas(object node) {
        if (node == null) return;
        if (node is FunctionLambdaExpression lambda) {
            _lambdaCounter++;
            lambda.LambdaId = $"_pino_lambda_{_lambdaCounter}";
            _lambdas.Add(lambda);
            FindLambdas(lambda.Body);
            return;
        }

        switch (node) {
            case BlockStatement block:
                foreach (var stmt in block.Statements) FindLambdas(stmt);
                break;
            case VariableDeclaration varDecl:
                FindLambdas(varDecl.Value);
                break;
            case ReturnStatement ret:
                FindLambdas(ret.Argument);
                break;
            case IfStatement ifs:
                FindLambdas(ifs.Condition);
                FindLambdas(ifs.Consequent);
                FindLambdas(ifs.Alternate);
                break;
            case LoopStatement loop:
                FindLambdas(loop.Body);
                FindLambdas(loop.Begin);
                FindLambdas(loop.End);
                break;
            case FunctionDeclaration fn:
                FindLambdas(fn.Body);
                break;
            case StructDeclaration str:
                foreach (var m in str.Methods) FindLambdas(m);
                break;
            case MatchStatement mat:
                FindLambdas(mat.Condition);
                foreach (var branch in mat.Branches) {
                    FindLambdas(branch.Body);
                }
                if (mat.Alternate != null) {
                    FindLambdas(mat.Alternate.Body);
                }
                break;
            case TupleDestructuringDeclaration tupleDest:
                FindLambdas(tupleDest.Value);
                break;
            case BinaryExpression bin:
                FindLambdas(bin.Left);
                FindLambdas(bin.Right);
                break;
            case UnaryExpression un:
                FindLambdas(un.Right);
                break;
            case TernaryExpression tern:
                FindLambdas(tern.Condition);
                FindLambdas(tern.Consequent);
                FindLambdas(tern.Alternate);
                break;
            case StructInstanceExpression si:
                foreach (var prop in si.Properties) FindLambdas(prop.Value);
                break;
            case FunctionCallExpression call:
                foreach (var arg in call.Arguments) FindLambdas(arg);
                break;
            case IndexAccessExpression idx:
                FindLambdas(idx.Target);
                FindLambdas(idx.Index);
                break;
            case MapExpression map:
                foreach (var entry in map.Entries) {
                    FindLambdas(entry.Key);
                    FindLambdas(entry.Value);
                }
                break;
            case BubbleExpression bub:
                FindLambdas(bub.Value);
                break;
            case TupleLiteralExpression tupleLit:
                foreach (var f in tupleLit.Fields) FindLambdas(f.Value);
                break;
            case TestDeclaration testDecl:
                FindLambdas(testDecl.Body);
                break;
            case AssertStatement assertStmt:
                FindLambdas(assertStmt.Expression);
                break;
            case Expression expr:
                // Since Expression is Statement, fallback to children inside expression types
                break;
        }
    }

    private void AnalyzeFreeVars(object node, HashSet<string> localScope, HashSet<string> free) {
        if (node == null) return;

        switch (node) {
            case IdentifierExpression id:
                if (!localScope.Contains(id.Name)) {
                    free.Add(id.Name);
                }
                break;

            case BlockStatement block:
                var nestedScope = new HashSet<string>(localScope);
                foreach (var stmt in block.Statements) {
                    if (stmt is VariableDeclaration varDecl) {
                        nestedScope.Add(varDecl.Identifier);
                    }
                    AnalyzeFreeVars(stmt, nestedScope, free);
                }
                break;

            case VariableDeclaration varDecl:
                AnalyzeFreeVars(varDecl.Value, localScope, free);
                break;

            case ReturnStatement ret:
                AnalyzeFreeVars(ret.Argument, localScope, free);
                break;

            case IfStatement ifs:
                AnalyzeFreeVars(ifs.Condition, localScope, free);
                AnalyzeFreeVars(ifs.Consequent, localScope, free);
                AnalyzeFreeVars(ifs.Alternate, localScope, free);
                break;

            case LoopStatement loop:
                AnalyzeFreeVars(loop.Body, localScope, free);
                AnalyzeFreeVars(loop.Begin, localScope, free);
                AnalyzeFreeVars(loop.End, localScope, free);
                break;

            case FunctionLambdaExpression lambda:
                var lambdaScope = new HashSet<string>(localScope);
                foreach (var p in lambda.Parameters) {
                    lambdaScope.Add(p.Identifier);
                }
                AnalyzeFreeVars(lambda.Body, lambdaScope, free);
                break;

            case MatchStatement mat:
                AnalyzeFreeVars(mat.Condition, localScope, free);
                foreach (var branch in mat.Branches) {
                    var matchScope = new HashSet<string>(localScope);
                    var patternVars = new List<(string Name, string Type)>();
                    foreach (var cond in branch.Conditions) {
                        CollectPatternVariables(cond, "any", patternVars);
                    }
                    foreach (var pv in patternVars) {
                        matchScope.Add(pv.Name);
                    }
                    AnalyzeFreeVars(branch.Body, matchScope, free);
                }
                if (mat.Alternate != null) {
                    AnalyzeFreeVars(mat.Alternate.Body, localScope, free);
                }
                break;

            case TupleDestructuringDeclaration tupleDest:
                AnalyzeFreeVars(tupleDest.Value, localScope, free);
                break;

            case BinaryExpression bin:
                AnalyzeFreeVars(bin.Left, localScope, free);
                if (bin.Operator != OperatorType.MemberAccess) {
                    AnalyzeFreeVars(bin.Right, localScope, free);
                }
                break;

            case UnaryExpression un:
                AnalyzeFreeVars(un.Right, localScope, free);
                break;

            case TernaryExpression tern:
                AnalyzeFreeVars(tern.Condition, localScope, free);
                AnalyzeFreeVars(tern.Consequent, localScope, free);
                AnalyzeFreeVars(tern.Alternate, localScope, free);
                break;

            case StructInstanceExpression si:
                foreach (var prop in si.Properties) AnalyzeFreeVars(prop.Value, localScope, free);
                break;

            case FunctionCallExpression call:
                foreach (var arg in call.Arguments) AnalyzeFreeVars(arg, localScope, free);
                break;

            case IndexAccessExpression idx:
                AnalyzeFreeVars(idx.Target, localScope, free);
                AnalyzeFreeVars(idx.Index, localScope, free);
                break;

            case MapExpression map:
                foreach (var entry in map.Entries) {
                    AnalyzeFreeVars(entry.Key, localScope, free);
                    AnalyzeFreeVars(entry.Value, localScope, free);
                }
                break;

            case BubbleExpression bub:
                AnalyzeFreeVars(bub.Value, localScope, free);
                break;

            case TupleLiteralExpression tupleLit:
                foreach (var f in tupleLit.Fields) AnalyzeFreeVars(f.Value, localScope, free);
                break;
            case TestDeclaration testDecl:
                AnalyzeFreeVars(testDecl.Body, localScope, free);
                break;
            case AssertStatement assertStmt:
                AnalyzeFreeVars(assertStmt.Expression, localScope, free);
                break;
        }
    }

    private bool IsMutableVariable(ProgramStatement program, string name) {
        return IsMutableVariableInNode(program, name);
    }

    private bool IsMutableVariableInNode(object node, string name) {
        if (node == null) return false;
        if (node is VariableDeclaration varDecl && varDecl.Identifier == name) {
            return varDecl.Kind == VariableKind.Variable;
        }

        switch (node) {
            case ProgramStatement prog:
                foreach (var s in prog.Statements) {
                    if (IsMutableVariableInNode(s, name)) return true;
                }
                break;
            case BlockStatement block:
                foreach (var s in block.Statements) {
                    if (IsMutableVariableInNode(s, name)) return true;
                }
                break;
            case IfStatement ifs:
                return IsMutableVariableInNode(ifs.Consequent, name) || IsMutableVariableInNode(ifs.Alternate, name);
            case TestDeclaration testDecl:
                return IsMutableVariableInNode(testDecl.Body, name);
            case AssertStatement assertStmt:
                return IsMutableVariableInNode(assertStmt.Expression, name);
            case LoopStatement loop:
                return IsMutableVariableInNode(loop.Body, name) || IsMutableVariableInNode(loop.Begin, name) || IsMutableVariableInNode(loop.End, name);
            case FunctionDeclaration fn:
                return IsMutableVariableInNode(fn.Body, name);
            case StructDeclaration str:
                foreach (var m in str.Methods) {
                    if (IsMutableVariableInNode(m, name)) return true;
                }
                break;
            case MatchStatement mat:
                foreach (var branch in mat.Branches) {
                    if (IsMutableVariableInNode(branch.Body, name)) return true;
                }
                if (mat.Alternate != null) {
                    if (IsMutableVariableInNode(mat.Alternate.Body, name)) return true;
                }
                break;
        }
        return false;
    }

    private Stack<FunctionLambdaExpression> _lambdaStack = new Stack<FunctionLambdaExpression>();

    private void GenerateLambdaEnvironments(ProgramStatement program) {
        foreach (var lambda in _lambdas) {
            if (lambda.FreeVars.Count == 0) continue;
            _globalDeclSb.AppendLine($"struct {lambda.LambdaId}_env {{");
            foreach (var v in lambda.FreeVars) {
                var pinoType = FindVariableType(program, v);
                var cType = MapType(pinoType);
                if (_boxedVariables.Contains(v)) {
                    var clean = CleanTypeName(cType);
                    _globalDeclSb.AppendLine($"    struct Ref_{clean}* {v};");
                } else {
                    _globalDeclSb.AppendLine($"    {cType} {v};");
                }
            }
            _globalDeclSb.AppendLine($"}};");
        }
    }

    private string GetLambdaReturnType(FunctionLambdaExpression lambda) {
        var inf = lambda.InferredType;
        if (string.IsNullOrEmpty(inf)) return "void";
        int idx = inf.LastIndexOf(')');
        if (idx == -1) return "void";
        var ret = inf.Substring(idx + 1).Trim();
        return string.IsNullOrEmpty(ret) ? "void" : ret;
    }

    private void TranspileLambdas(StringBuilder forwardFuncSb) {
        foreach (var lambda in _lambdas) {
            var retType = MapType(GetLambdaReturnType(lambda));
            var paramList = new List<string> { "void* env_ptr" };
            foreach (var p in lambda.Parameters) {
                paramList.Add($"{MapType(p.Typing)} {p.Identifier}");
            }
            
            forwardFuncSb.AppendLine($"static {retType} {lambda.LambdaId}({string.Join(", ", paramList)});");

            _sb.AppendLine();
            _sb.AppendLine($"static {retType} {lambda.LambdaId}({string.Join(", ", paramList)}) {{");
            _indent++;
            
            if (lambda.FreeVars.Count > 0) {
                WriteLine($"struct {lambda.LambdaId}_env* env = (struct {lambda.LambdaId}_env*)env_ptr;");
            }
            
            _lambdaStack.Push(lambda);
            
            var oldVarTypes = new Dictionary<string, string>(_varTypes);
            foreach (var p in lambda.Parameters) {
                _varTypes[p.Identifier] = p.Typing;
            }
            
            var block = (BlockStatement)lambda.Body;
            foreach (var stmt in block.Statements) {
                TranspileStatement(stmt);
            }
            
            _varTypes = oldVarTypes;
            _lambdaStack.Pop();
            _indent--;
            _sb.AppendLine("}");
        }
    }

    private void TranspileClosureRef(string name) {
        if (_importedSymbols.TryGetValue(name, out var mappedName)) {
            Write(mappedName);
        } else if (_currentLambda != null && _currentLambda.FreeVars.Contains(name)) {
            if (_boxedVariables.Contains(name)) {
                Write($"env->{name}->value");
            } else {
                Write($"env->{name}");
            }
        } else if (_boxedVariables.Contains(name)) {
            Write($"{name}->value");
        } else {
            if (_currentModuleName != null && _globalVarTypes.ContainsKey($"{_currentModuleName}_{name}")) {
                Write($"{_currentModuleName}_{name}");
            } else {
                Write(name);
            }
        }
    }

    private static readonly HashSet<string> BuiltInFunctionNames = new HashSet<string> {
        "println", "time", "rand", "sleep", "clear", "regex", "read_file", "write_file", "file_exists"
    };

    private FunctionLambdaExpression? _currentLambda => _lambdaStack.Count > 0 ? _lambdaStack.Peek() : null;

    private void TranspileTests(StringBuilder forwardFuncSb) {
        if (!EnableTests) return;
        var oldGlobalScope = _isGlobalScope;
        var oldVarTypes = new Dictionary<string, string>(_varTypes);
        var oldReturnType = _currentReturnType;


        for (int i = 0; i < _tests.Count; i++) {
            var testInfo = _tests[i];
            var test = testInfo.Test;
            var oldModuleName = _currentModuleName;
            _currentModuleName = testInfo.ModuleName;

            forwardFuncSb.AppendLine($"static void _pino_test_{i}(void);");

            _sb.AppendLine();
            _sb.AppendLine($"static void _pino_test_{i}(void) {{");
            _indent++;
            
            _isGlobalScope = false;
            _currentReturnType = "void";
            _varTypes.Clear();
            foreach (var kvp in _globalVarTypes) {
                _varTypes[kvp.Key] = kvp.Value;
            }

            var block = (BlockStatement)test.Body;
            foreach (var stmt in block.Statements) {
                TranspileStatement(stmt);
            }
            
            _indent--;
            _sb.AppendLine("}");
            _currentModuleName = oldModuleName;
        }

        _isGlobalScope = oldGlobalScope;
        _currentReturnType = oldReturnType;
        _varTypes.Clear();
        foreach (var kvp in oldVarTypes) {
            _varTypes[kvp.Key] = kvp.Value;
        }
    }

    private (List<string> Params, string ReturnType) ParseFunctionType(string typeStr) {
        int openParen = typeStr.IndexOf('(');
        int closeParen = typeStr.LastIndexOf(')');
        if (openParen == -1 || closeParen == -1) {
            return (new List<string>(), "void");
        }
        var paramsStr = typeStr.Substring(openParen + 1, closeParen - openParen - 1);
        var paramTypes = string.IsNullOrWhiteSpace(paramsStr) 
            ? new List<string>() 
            : paramsStr.Split(',').Select(p => p.Trim()).ToList();
        var ret = typeStr.Substring(closeParen + 1).Trim();
        if (string.IsNullOrEmpty(ret)) ret = "void";
        return (paramTypes, ret);
    }
}
