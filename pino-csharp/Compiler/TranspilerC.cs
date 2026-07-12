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
        _declaredTuples.Add("[]string");
        _unions.Clear();
        _enums.Clear();
        _matchResultVars.Clear();
        _globalVarTypes.Clear();
        _globalDeclSb.Clear();
        _isGlobalScope = true;
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

        // Pass 0: Register all structs, enums and unions and populate their fields dictionary (needed for method translation)
        foreach (var decl in declarations) {
            if (decl is StructDeclaration structDecl) {
                var fields = structDecl.Fields.Select(f => f.Identifier).ToList();
                _structFields[structDecl.Identifier] = fields;
            } else if (decl is UnionDeclaration unionDecl) {
                _unions[unionDecl.Identifier] = unionDecl;
            } else if (decl is EnumDeclaration enumDecl) {
                _enums[enumDecl.Identifier] = enumDecl;
            }
        }

        // Register global variables (after Pass 0 so structs/unions/enums are known)
        foreach (var stmt in topLevelStatements) {
            if (stmt is VariableDeclaration varDecl) {
                string typeStr = "void";
                if (!string.IsNullOrEmpty(varDecl.Typing)) {
                    typeStr = MapType(varDecl.Typing);
                } else if (varDecl.Value != null && !string.IsNullOrEmpty(varDecl.Value.InferredType)) {
                    typeStr = MapType(varDecl.Value.InferredType);
                }
                _globalDeclSb.AppendLine($"{typeStr} {varDecl.Identifier};");
                _globalVarTypes[varDecl.Identifier] = !string.IsNullOrEmpty(varDecl.Typing) ? varDecl.Typing : (varDecl.Value != null ? varDecl.Value.InferredType : "any");
            }
        }

        // Pass 1: Forward declarations of structs, struct methods, and functions
        var forwardDeclSb = new StringBuilder();
        var structSb = new StringBuilder();
        foreach (var decl in declarations) {
            if (decl is StructDeclaration structDecl) {
                if (structDecl.GenericParams != null && structDecl.GenericParams.Count > 0) continue;
                // Compile struct typedef
                forwardDeclSb.AppendLine($"typedef struct {structDecl.Identifier} {structDecl.Identifier};");
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
            } else if (decl is EnumDeclaration enumDecl) {
                structSb.AppendLine($"enum {enumDecl.Identifier} {{");
                foreach (var member in enumDecl.Members) {
                    structSb.AppendLine($"    {enumDecl.Identifier}_{member},");
                }
                structSb.AppendLine("};");
                structSb.AppendLine($"typedef enum {enumDecl.Identifier} {enumDecl.Identifier};");
                structSb.AppendLine();
            } else if (decl is UnionDeclaration unionDecl) {
                if (unionDecl.GenericParams != null && unionDecl.GenericParams.Count > 0) continue;
                var unionName = unionDecl.Identifier;
                forwardDeclSb.AppendLine($"typedef struct {unionName} {unionName};");
                structSb.AppendLine($"enum {unionName}Tag {{");
                foreach (var variant in unionDecl.Variants) {
                    structSb.AppendLine($"    {unionName}Tag_{variant.Identifier},");
                }
                structSb.AppendLine("};");
                structSb.AppendLine();

                foreach (var variant in unionDecl.Variants) {
                    if (variant.AssociatedTypes.Count > 0) {
                        structSb.AppendLine($"struct {unionName}_{variant.Identifier}_payload {{");
                        for (int i = 0; i < variant.AssociatedTypes.Count; i++) {
                            structSb.AppendLine($"    {MapType(variant.AssociatedTypes[i])} _{i};");
                        }
                        structSb.AppendLine("};");
                        structSb.AppendLine();
                    }
                }

                structSb.AppendLine($"struct {unionName} {{");
                structSb.AppendLine($"    enum {unionName}Tag tag;");
                bool hasPayloads = unionDecl.Variants.Any(v => v.AssociatedTypes.Count > 0);
                if (hasPayloads) {
                    structSb.AppendLine("    union {");
                    foreach (var variant in unionDecl.Variants) {
                        if (variant.AssociatedTypes.Count > 0) {
                            structSb.AppendLine($"        struct {unionName}_{variant.Identifier}_payload {variant.Identifier};");
                        }
                    }
                    structSb.AppendLine("    } value;");
                }
                structSb.AppendLine("};");
                structSb.AppendLine();

                foreach (var variant in unionDecl.Variants) {
                    if (variant.AssociatedTypes.Count > 0) {
                        var paramsStr = string.Join(", ", variant.AssociatedTypes.Select((t, i) => $"{MapType(t)} _{i}"));
                        structSb.AppendLine($"{unionName}* {unionName}_{variant.Identifier}_construct({paramsStr});");
                    }
                }
                structSb.AppendLine();
            } else if (decl is FunctionDeclaration fnDecl && fnDecl.Identifier != "main") {
                if (fnDecl.GenericParams != null && fnDecl.GenericParams.Count > 0) continue;
                var returnType = MapType(fnDecl.ReturnType);
                var parameters = string.Join(", ", fnDecl.Parameters.Select(p => $"{MapType(p.Typing)} {p.Identifier}"));
                if (string.IsNullOrEmpty(parameters)) parameters = "void";
                _sb.AppendLine($"{returnType} {fnDecl.Identifier}({parameters});");
            }
        }
        _sb.AppendLine();

        // Pass 2: Implementation of struct methods, union variant constructors, and function declarations
        foreach (var decl in declarations) {
            if (decl is StructDeclaration structDecl) {
                if (structDecl.GenericParams != null && structDecl.GenericParams.Count > 0) continue;
                foreach (var method in structDecl.Methods) {
                    TranspileStructMethod(structDecl.Identifier, method);
                }
            } else if (decl is UnionDeclaration unionDecl) {
                if (unionDecl.GenericParams != null && unionDecl.GenericParams.Count > 0) continue;
                var unionName = unionDecl.Identifier;
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

        // Pass 3: The C main function carrying the top-level statements and user main call
        _sb.AppendLine("int main(int argc, char** argv) {");
        _indent = 1;
        _sb.AppendLine("#ifdef PINO_GC");
        _sb.AppendLine("    GC_INIT();");
        _sb.AppendLine("#endif");
        _sb.AppendLine("    srand((unsigned int)time(NULL));");

        _isGlobalScope = true;
        foreach (var stmt in topLevelStatements) {
            TranspileStatement(stmt);
        }

        var userMain = declarations.FirstOrDefault(d => d is FunctionDeclaration fn && fn.Identifier == "main") as FunctionDeclaration;
        if (userMain != null && userMain.Body != null) {
            _isGlobalScope = false;
            _varTypes.Clear();
            foreach (var kvp in _globalVarTypes) {
                _varTypes[kvp.Key] = kvp.Value;
            }
            TranspileStatement(userMain.Body);
        }

        WriteLine("return 0;");
        _indent = 0;
        _sb.AppendLine("}");

        // Combine everything: Headers + Typedef Structs + Tuple Structs + Main Code
        var finalSb = new StringBuilder();
        finalSb.AppendLine("#include <stdio.h>");
        finalSb.AppendLine("#include <stdlib.h>");
        finalSb.AppendLine("#include <stdbool.h>");
        finalSb.AppendLine("#include <string.h>");
        finalSb.AppendLine("#include \"runtime/runtime.h\"");
        finalSb.AppendLine();
        finalSb.Append(forwardDeclSb.ToString());
        finalSb.Append(_tupleSb.ToString());
        finalSb.Append(_globalDeclSb.ToString());
        finalSb.Append(structSb.ToString());
        finalSb.Append(_sb.ToString());

        return finalSb.ToString();
    }

    private void TranspileFunction(FunctionDeclaration fnDecl) {
        var returnType = MapType(fnDecl.ReturnType);
        var identifier = fnDecl.Identifier;

        _currentReturnType = fnDecl.ReturnType;
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
        var returnType = MapType(fnDecl.ReturnType);
        var identifier = $"{structName}_{fnDecl.Identifier}";

        _currentReturnType = fnDecl.ReturnType;
        // Setup method environment
        _isGlobalScope = false;
        _varTypes.Clear();
        foreach (var kvp in _globalVarTypes) {
            _varTypes[kvp.Key] = kvp.Value;
        }
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
            var pinoArgs = argsStr.Split(',').Select(a => a.Trim().Replace(" ", "_").Replace("[]", "Vector_").Replace("[", "_").Replace("]", "_").Replace(",", "_")).ToList();
            var cleanTypeName = baseName + "_" + string.Join("_", pinoArgs);
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
                        var isConst = dest.Kind == VariableKind.Constant;
                        var prefix = isConst ? "const " : "";
                        
                        WriteIndent();
                        Write($"{prefix}{MapType(varType)} {field.Identifier} = {tempVar}.{field.Label};\n");
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
                    Write($"{varDecl.Identifier} = (char*)pino_malloc(1024);\n");
                    var (format, args) = ProcessStringAddition(varDecl.Value);
                    var argsStr = args.Count > 0 ? ", " + string.Join(", ", args) : "";
                    WriteIndent();
                    Write($"snprintf((char*){varDecl.Identifier}, 1024, \"{EscapeString(format)}\"{argsStr})");
                } else {
                    Write($"{varDecl.Identifier} = ");
                    TranspileExpression(varDecl.Value);
                }
            }
            return;
        }

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
                             if (type == "int" || type == "bool") {
                                Write("pino_println_int(");
                                TranspileExpression(arg);
                                Write(")");
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
                Write($"({{ {inst.StructName}* temp = ({inst.StructName}*)pino_malloc(sizeof({inst.StructName})); ");
                for (int i = 0; i < inst.Properties.Count; i++) {
                    var prop = inst.Properties[i];
                    Write($"temp->{prop.Identifier} = ");
                    TranspileExpression(prop.Value!);
                    Write("; ");
                }
                Write("temp; })");
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
                    var unionDecl = FindUnion(varPat.UnionName);
                    if (unionDecl == null) {
                        var enumDecl = FindEnum(varPat.UnionName);
                        if (enumDecl != null) {
                            condList.Add($"({target} == {varPat.UnionName}_{varPat.VariantName})");
                        }
                        break;
                    }
                    condList.Add($"({target}->tag == {varPat.UnionName}Tag_{varPat.VariantName})");
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
}
