using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pino;

public class Transpiler {
  private readonly StringBuilder _code = new();
  private readonly StringBuilder _classes = new();
  private readonly Dictionary<string, Stack<string>> _scopes = new();
  private readonly Stack<Scope> _scopeStack = new();
  private int _varCounter = 0;
  private int _indent = 0;

  private class Scope {
    public List<string> Variables { get; } = new();
  }

  private void EnterScope() {
    _scopeStack.Push(new Scope());
  }

  private void ExitScope() {
    if (_scopeStack.Count == 0) return;
    var scope = _scopeStack.Pop();
    foreach (var varName in scope.Variables) {
      if (_scopes.TryGetValue(varName, out var stack)) {
        stack.Pop();
        if (stack.Count == 0) {
          _scopes.Remove(varName);
        }
      }
    }
  }

  private string DefineVariable(string name) {
    _varCounter++;
    string genName = $"{name}_{_varCounter}";
    if (!_scopes.TryGetValue(name, out var stack)) {
      stack = new Stack<string>();
      _scopes[name] = stack;
    }
    stack.Push(genName);
    if (_scopeStack.Count > 0) {
      _scopeStack.Peek().Variables.Add(name);
    }
    return genName;
  }

  private string ResolveVariable(string name) {
    if (name == "println") return "Program.println";
    if (name == "readline") return "Program.readline";
    if (name == "rand") return "Program.rand";
    if (name == "time") return "Program.time";
    if (name == "sleep") return "Program.sleep";
    if (name == "type") return "Program.type";
    if (name == "str") return "Program.str";
    if (name == "int") return "Program.@int";
    if (name == "float") return "Program.@float";

    if (_scopes.TryGetValue(name, out var stack) && stack.Count > 0) {
      return stack.Peek();
    }
    return name;
  }

  private void Indent() {
    _code.Append(new string(' ', _indent * 2));
  }

  private void Line(string text = "") {
    Indent();
    _code.AppendLine(text);
  }

  public static string Transpile(ProgramStatement program) {
    var transpiler = new Transpiler();
    return transpiler.Generate(program);
  }

  private string Generate(ProgramStatement program) {
    // Collect classes and enums at namespace level
    foreach (var stmt in program.Statements) {
      if (stmt is StructDeclaration structDecl) {
        GenerateStruct(structDecl);
      } else if (stmt is EnumDeclaration enumDecl) {
        GenerateEnum(enumDecl);
      }
    }

    // Main template
    var template = new StringBuilder();
    template.AppendLine("using System;");
    template.AppendLine("using System.Collections.Generic;");
    template.AppendLine("using System.Linq;");
    template.AppendLine();
    template.AppendLine("namespace PinoGenerated;");
    template.AppendLine();
    template.Append(_classes.ToString());
    template.AppendLine();
    template.AppendLine("public class Program {");
    template.AppendLine("  private static readonly Random _rand = new();");
    template.AppendLine();
    template.AppendLine("  public static dynamic rand() => _rand.NextDouble();");
    template.AppendLine("  public static dynamic rand(dynamic max) => (long)_rand.Next(0, (int)max);");
    template.AppendLine("  public static dynamic time() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();");
    template.AppendLine("  public static dynamic sleep(dynamic ms) {");
    template.AppendLine("    System.Threading.Thread.Sleep((int)ms);");
    template.AppendLine("    return null;");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static dynamic @int(dynamic val) {");
    template.AppendLine("    if (val is double d) return (long)d;");
    template.AppendLine("    if (val is bool b) return b ? 1L : 0L;");
    template.AppendLine("    return long.Parse(val.ToString());");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static dynamic @float(dynamic val) {");
    template.AppendLine("    return double.Parse(val.ToString(), System.Globalization.CultureInfo.InvariantCulture);");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static dynamic type(dynamic val) {");
    template.AppendLine("    if (val == null) return \"null\";");
    template.AppendLine("    if (val is bool) return \"bool\";");
    template.AppendLine("    if (val is long || val is int) return \"int\";");
    template.AppendLine("    if (val is double || val is float) return \"float\";");
    template.AppendLine("    if (val is string) return \"string\";");
    template.AppendLine("    if (val is System.Collections.IList) return \"vector\";");
    template.AppendLine("    var type = val.GetType();");
    template.AppendLine("    if (type.GetProperty(\"StructName\") != null) return \"struct\";");
    template.AppendLine("    if (type.IsEnum) return \"enum\";");
    template.AppendLine("    if (val is Delegate) return \"function\";");
    template.AppendLine("    return type.Name.ToLower();");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static string str(dynamic val) {");
    template.AppendLine("    if (val == null) return \"null\";");
    template.AppendLine("    if (val is bool b) return b ? \"True\" : \"False\";");
    template.AppendLine("    if (val is System.Collections.IList list) {");
    template.AppendLine("      var parts = new List<string>();");
    template.AppendLine("      foreach (var item in list) parts.Add(str(item));");
    template.AppendLine("      return \"[\" + string.Join(\", \", parts) + \"]\";");
    template.AppendLine("    }");
    template.AppendLine("    return val.ToString();");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static void println(params dynamic[] args) {");
    template.AppendLine("    Console.WriteLine(string.Join(\" \", args.Select(a => str(a))));");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static void print(params dynamic[] args) {");
    template.AppendLine("    Console.Write(string.Join(\" \", args.Select(a => str(a))));");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static dynamic readline() => Console.ReadLine();");
    template.AppendLine("  public static dynamic readline(dynamic prompt) {");
    template.AppendLine("    Console.Write(prompt);");
    template.AppendLine("    return Console.ReadLine();");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static bool IsTruthy(dynamic val) {");
    template.AppendLine("    if (val == null) return false;");
    template.AppendLine("    if (val is bool b) return b;");
    template.AppendLine("    return true;");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static dynamic CreateVector(dynamic lenVal, dynamic initVal) {");
    template.AppendLine("    long length = Convert.ToInt64(lenVal);");
    template.AppendLine("    var list = new List<dynamic>();");
    template.AppendLine("    for (long i = 0; i < length; i++) {");
    template.AppendLine("      if (initVal is Delegate del) {");
    template.AppendLine("        var method = del.Method;");
    template.AppendLine("        if (method.GetParameters().Length == 1) {");
    template.AppendLine("          list.Add(del.DynamicInvoke(i));");
    template.AppendLine("        } else {");
    template.AppendLine("          list.Add(del.DynamicInvoke());");
    template.AppendLine("        }");
    template.AppendLine("      } else {");
    template.AppendLine("        list.Add(initVal);");
    template.AppendLine("      }");
    template.AppendLine("    }");
    template.AppendLine("    return list;");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static dynamic CallMethod(dynamic target, string name, params dynamic[] args) {");
    template.AppendLine("    if (target is string str) {");
    template.AppendLine("      if (name == \"lower\") return str.ToLowerInvariant();");
    template.AppendLine("      if (name == \"upper\") return str.ToUpperInvariant();");
    template.AppendLine("      if (name == \"trim\") return str.Trim();");
    template.AppendLine("      if (name == \"contains\") return str.Contains((string)args[0]);");
    template.AppendLine("      if (name == \"split\") return str.Split(new[] { (string)args[0] }, StringSplitOptions.None).Cast<dynamic>().ToList();");
    template.AppendLine("      if (name == \"replace\") return str.Replace((string)args[0], (string)args[1]);");
    template.AppendLine("    }");
    template.AppendLine("    if (target is System.Collections.IList list) {");
    template.AppendLine("      if (name == \"map\") return PinoVectorExtensions.map(list, args[0]);");
    template.AppendLine("      if (name == \"filter\") return PinoVectorExtensions.filter(list, args[0]);");
    template.AppendLine("      if (name == \"each\") return PinoVectorExtensions.each(list, args[0]);");
    template.AppendLine("      if (name == \"push\" || name == \"add\") return PinoVectorExtensions.push(list, args[0]);");
    template.AppendLine("      if (name == \"pop\") return PinoVectorExtensions.pop(list);");
    template.AppendLine("    }");
    template.AppendLine("    var type = target.GetType();");
    template.AppendLine("    var method = type.GetMethod(name);");
    template.AppendLine("    if (method != null) {");
    template.AppendLine("      return method.Invoke(target, args);");
    template.AppendLine("    }");
    template.AppendLine("    throw new Exception($\"RUNTIME ERROR: Method '{name}' not found on target.\");");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static dynamic GetMember(dynamic target, string name) {");
    template.AppendLine("    if (target is System.Collections.IList list) {");
    template.AppendLine("      if (name == \"len\" || name == \"length\") return (long)list.Count;");
    template.AppendLine("    }");
    template.AppendLine("    if (target is string str) {");
    template.AppendLine("      if (name == \"len\" || name == \"length\") return (long)str.Length;");
    template.AppendLine("    }");
    template.AppendLine("    var type = target.GetType();");
    template.AppendLine("    var prop = type.GetProperty(name);");
    template.AppendLine("    if (prop != null) return prop.GetValue(target);");
    template.AppendLine("    throw new Exception($\"RUNTIME ERROR: Property '{name}' not found on target.\");");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static dynamic SetMember(dynamic target, string name, dynamic val) {");
    template.AppendLine("    var type = target.GetType();");
    template.AppendLine("    var prop = type.GetProperty(name);");
    template.AppendLine("    if (prop != null) {");
    template.AppendLine("      prop.SetValue(target, val);");
    template.AppendLine("      return val;");
    template.AppendLine("    }");
    template.AppendLine("    throw new Exception($\"RUNTIME ERROR: Property '{name}' not found on target.\");");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static void Main(string[] args) {");
    template.AppendLine("    try {");
    template.AppendLine("      Run();");
    template.AppendLine("    } catch (Exception ex) {");
    template.AppendLine("      Console.WriteLine(\"RUNTIME ERROR: \" + ex.Message);");
    template.AppendLine("    }");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static void Run() {");

    _indent = 2;
    EnterScope();

    // Generate non-declaration statements inside Run()
    foreach (var stmt in program.Statements) {
      if (stmt is not StructDeclaration && stmt is not EnumDeclaration) {
        TranspileStatement(stmt);
      }
    }

    ExitScope();
    template.Append(_code.ToString());
    template.AppendLine("  }");
    template.AppendLine("}");
    template.AppendLine();

    // Append Vector Extensions at the very end
    template.AppendLine("public static class PinoVectorExtensions {");
    template.AppendLine("  public static dynamic map(this System.Collections.IList list, dynamic func) {");
    template.AppendLine("    var result = new List<dynamic>();");
    template.AppendLine("    foreach (var item in list) result.Add(func(item));");
    template.AppendLine("    return result;");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static dynamic filter(this System.Collections.IList list, dynamic func) {");
    template.AppendLine("    var result = new List<dynamic>();");
    template.AppendLine("    foreach (var item in list) {");
    template.AppendLine("      if (Program.IsTruthy(func(item))) result.Add(item);");
    template.AppendLine("    }");
    template.AppendLine("    return result;");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static dynamic each(this System.Collections.IList list, dynamic func) {");
    template.AppendLine("    foreach (var item in list) func(item);");
    template.AppendLine("    return null;");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static dynamic push(this System.Collections.IList list, dynamic item) {");
    template.AppendLine("    list.Add(item);");
    template.AppendLine("    return list;");
    template.AppendLine("  }");
    template.AppendLine();
    template.AppendLine("  public static dynamic pop(this System.Collections.IList list) {");
    template.AppendLine("    if (list.Count == 0) return null;");
    template.AppendLine("    var last = list[list.Count - 1];");
    template.AppendLine("    list.RemoveAt(list.Count - 1);");
    template.AppendLine("    return last;");
    template.AppendLine("  }");
    template.AppendLine("}");

    return template.ToString();
  }

  private void GenerateStruct(StructDeclaration structDecl) {
    _classes.AppendLine($"public class {structDecl.Identifier} {{");
    _classes.AppendLine($"  public string StructName => \"{structDecl.Identifier}\";");
    
    // Fields
    foreach (var field in structDecl.Fields) {
      _classes.AppendLine($"  public dynamic {field.Identifier} {{ get; set; }}");
    }
    _classes.AppendLine();

    // Constructor
    var parametersStr = string.Join(", ", structDecl.Fields.Select(f => $"dynamic {f.Identifier} = null"));
    _classes.AppendLine($"  public {structDecl.Identifier}({parametersStr}) {{");
    foreach (var field in structDecl.Fields) {
      _classes.AppendLine($"    this.{field.Identifier} = {field.Identifier};");
    }
    _classes.AppendLine("  }");
    _classes.AppendLine();

    // Methods
    foreach (var method in structDecl.Methods) {
      EnterScope();
      // Define 'this' and 'self'
      DefineVariable("this");
      DefineVariable("self");

      var paramsList = new List<string>();
      foreach (var p in method.Parameters) {
        paramsList.Add($"dynamic {DefineVariable(p.Identifier)}");
      }
      var paramsStr = string.Join(", ", paramsList);

      _classes.AppendLine($"  public dynamic {method.Identifier}({paramsStr}) {{");

      // Method body - we transpile statements
      // We temporarily redirect compilation output to a separate builder for struct methods
      var origCode = _code.ToString();
      _code.Clear();
      var origIndent = _indent;
      _indent = 2;

      TranspileStatement(method.Body);
      Line("return null;");
      ExitScope();

      var methodBody = _code.ToString();
      _classes.Append(methodBody);

      // Restore main builder
      _code.Clear();
      _code.Append(origCode);
      _indent = origIndent;
      _classes.AppendLine("  }");
    }

    // ToString override
    var parts = new List<string>();
    foreach (var field in structDecl.Fields) {
      parts.Add($"\"{field.Identifier}: \" + Program.str({field.Identifier})");
    }
    var fieldsConcat = string.Join(" + \", \" + ", parts);
    _classes.AppendLine("  public override string ToString() {");
    if (structDecl.Fields.Count > 0) {
      _classes.AppendLine($"    return \"{structDecl.Identifier} {{ \" + {fieldsConcat} + \" }}\";");
    } else {
      _classes.AppendLine($"    return \"{structDecl.Identifier} {{ }}\";");
    }
    _classes.AppendLine("  }");

    _classes.AppendLine("}");
    _classes.AppendLine();
  }

  private void GenerateEnum(EnumDeclaration enumDecl) {
    _classes.AppendLine($"public enum {enumDecl.Identifier} {{");
    _classes.AppendLine(string.Join(", ", enumDecl.Members));
    _classes.AppendLine("}");
    _classes.AppendLine();
  }

  private void TranspileStatement(Statement stmt) {
    switch (stmt) {
      case BlockStatement block:
        Line("{");
        _indent++;
        EnterScope();
        foreach (var s in block.Statements) {
          TranspileStatement(s);
        }
        ExitScope();
        _indent--;
        Line("}");
        break;

      case ReturnStatement ret:
        if (ret.Argument != null) {
          Line($"return {TranspileExpression(ret.Argument)};");
        } else {
          Line("return null;");
        }
        break;

      case VariableDeclaration varDecl:
        string genVarName = DefineVariable(varDecl.Identifier);
        string initialValue = varDecl.Value != null ? TranspileExpression(varDecl.Value) : "null";
        Line($"dynamic {genVarName} = {initialValue};");
        break;

      case FunctionDeclaration fnDecl:
        string genFnName = DefineVariable(fnDecl.Identifier);

        EnterScope();
        var paramsList = new List<string>();
        foreach (var p in fnDecl.Parameters) {
          paramsList.Add($"dynamic {DefineVariable(p.Identifier)}");
        }
        var paramsStr = string.Join(", ", paramsList);

        Line($"dynamic {genFnName}({paramsStr}) {{");
        _indent++;
        TranspileStatement(fnDecl.Body);
        Line("return null;");
        ExitScope();
        _indent--;
        Line("}");
        break;

      case IfStatement ifs:
        Line($"if (Program.IsTruthy({TranspileExpression(ifs.Condition)}))");
        TranspileStatement(ifs.Consequent);
        if (ifs.Alternate != null) {
          Line("else");
          TranspileStatement(ifs.Alternate);
        }
        break;

      case ElseStatement elseStmt:
        TranspileStatement(elseStmt.Body);
        break;

      case LoopStatement loop:
        if (loop.Kind == LoopKind.Infinite) {
          Line("while (true)");
          TranspileStatement(loop.Body);
        } else if (loop.Kind == LoopKind.ForTimes) {
          Line("{");
          _indent++;
          string limitVar = DefineVariable("limit");
          string indexVar = DefineVariable("it");
          Line($"long {limitVar} = Convert.ToInt64({TranspileExpression(loop.Begin!)});");
          Line($"for (long {indexVar} = 0; {indexVar} < {limitVar}; {indexVar}++)");
          Line("{");
          _indent++;
          EnterScope();
          TranspileStatement(loop.Body);
          ExitScope();
          _indent--;
          Line("}");
          _indent--;
          Line("}");
        } else if (loop.Kind == LoopKind.ForIn) {
          Line("{");
          _indent++;
          string collVar = DefineVariable("collection");
          Line($"dynamic {collVar} = {TranspileExpression(loop.End!)};");
          
          string iterVarName = (loop.Begin as IdentifierExpression)?.Name ?? "it";
          EnterScope();
          string genIterVar = DefineVariable(iterVarName);

          Line($"if ({collVar} is System.Collections.IEnumerable && {collVar} is not string)");
          Line("{");
          _indent++;
          Line($"foreach (dynamic {genIterVar} in {collVar})");
          TranspileStatement(loop.Body);
          _indent--;
          Line("}");
          Line("else");
          Line("{");
          _indent++;
          string limitVar = DefineVariable("limit");
          Line($"long {limitVar} = Convert.ToInt64({collVar});");
          Line($"for (long {genIterVar} = 0; {genIterVar} < {limitVar}; {genIterVar}++)");
          TranspileStatement(loop.Body);
          _indent--;
          Line("}");
          
          ExitScope();
          _indent--;
          Line("}");
        }
        break;

      case MatchStatement match:
        Line("{");
        _indent++;
        string matchVal = DefineVariable("matchVal");
        string matchedFlag = DefineVariable("matched");
        Line($"dynamic {matchVal} = {TranspileExpression(match.Condition)};");
        Line($"bool {matchedFlag} = false;");

        foreach (var branch in match.Branches) {
          var checks = string.Join(" || ", branch.Conditions.Select(c => $"Equals({matchVal}, {TranspileExpression(c)})"));
          Line($"if (!{matchedFlag} && ({checks}))");
          Line("{");
          _indent++;
          Line($"{matchedFlag} = true;");
          TranspileStatement(branch.Body);
          _indent--;
          Line("}");
        }

        if (match.Alternate != null) {
          Line($"if (!{matchedFlag})");
          TranspileStatement(match.Alternate);
        }
        _indent--;
        Line("}");
        break;

      case Expression expr:
        Line($"{TranspileExpression(expr)};");
        break;

      default:
        throw new NotImplementedException($"Transpiler: Statement of type '{stmt.GetType().Name}' is not implemented.");
    }
  }

  private string TranspileExpression(Expression expr) {
    switch (expr) {
      case LiteralExpression lit:
        if (lit.LiteralType == LiteralType.Boolean) {
          return lit.Value.ToLower();
        }
        if (lit.LiteralType == LiteralType.Integer) {
          return lit.Value.Replace("_", "") + "L";
        }
        if (lit.LiteralType == LiteralType.Float) {
          return lit.Value.Replace("_", "");
        }
        if (lit.LiteralType == LiteralType.String) {
          // Escape quotes and newlines
          var escaped = lit.Value.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
          return $"\"{escaped}\"";
        }
        return lit.Value;

      case IdentifierExpression id:
        if (id.Name == "break") return "break";
        if (id.Name == "continue") return "continue";
        return ResolveVariable(id.Name);

      case BinaryExpression bin:
        // Handle member assignment Left:Prop = Right
        if (bin.Operator == OperatorType.Assignment && bin.Left is BinaryExpression mem && mem.Operator == OperatorType.MemberAccess) {
          var targetStr = TranspileExpression(mem.Left);
          var propName = (mem.Right as IdentifierExpression)?.Name ?? throw new Exception("Expected property name for assignment");
          return $"Program.SetMember({targetStr}, \"{propName}\", {TranspileExpression(bin.Right)})";
        }

        // Handle compound assignments Left:Prop += Right
        if ((bin.Operator == OperatorType.AdditionAssignment || bin.Operator == OperatorType.SubtractionAssignment ||
             bin.Operator == OperatorType.MultiplicationAssignment || bin.Operator == OperatorType.DivisionAssignment ||
             bin.Operator == OperatorType.ModulusAssignment) && bin.Left is BinaryExpression memComp && memComp.Operator == OperatorType.MemberAccess) {
          var targetStr = TranspileExpression(memComp.Left);
          var propName = (memComp.Right as IdentifierExpression)?.Name ?? throw new Exception("Expected property name for assignment");
          var opChar = bin.Operator switch {
            OperatorType.AdditionAssignment => "+",
            OperatorType.SubtractionAssignment => "-",
            OperatorType.MultiplicationAssignment => "*",
            OperatorType.DivisionAssignment => "/",
            OperatorType.ModulusAssignment => "%",
            _ => throw new NotImplementedException()
          };
          return $"Program.SetMember({targetStr}, \"{propName}\", Program.GetMember({targetStr}, \"{propName}\") {opChar} {TranspileExpression(bin.Right)})";
        }

        if (bin.Operator == OperatorType.MemberAccess) {
          // Left:Right where Right is either a property or a method call
          var leftStr = TranspileExpression(bin.Left);
          if (bin.Right is FunctionCallExpression methodCall) {
            var methodArgs = string.Join(", ", methodCall.Arguments.Select(TranspileExpression));
            var comma = methodCall.Arguments.Count > 0 ? ", " : "";
            return $"Program.CallMethod({leftStr}, \"{methodCall.Callee}\"{comma}{methodArgs})";
          } else if (bin.Right is IdentifierExpression propId) {
            return $"Program.GetMember({leftStr}, \"{propId.Name}\")";
          }
          throw new Exception("Transpiler: Invalid right side of member access operator.");
        }

        if (bin.Operator == OperatorType.StaticMemberAccess) {
          var enumName = (bin.Left as IdentifierExpression)?.Name ?? throw new Exception("Expected Enum Name");
          var memberName = (bin.Right as IdentifierExpression)?.Name ?? throw new Exception("Expected Enum Member");
          return $"{enumName}.{memberName}";
        }

        bool isAssignment = bin.Operator == OperatorType.Assignment ||
                            bin.Operator == OperatorType.AdditionAssignment ||
                            bin.Operator == OperatorType.SubtractionAssignment ||
                            bin.Operator == OperatorType.MultiplicationAssignment ||
                            bin.Operator == OperatorType.DivisionAssignment ||
                            bin.Operator == OperatorType.ModulusAssignment;

        var left = TranspileExpression(bin.Left);
        var right = TranspileExpression(bin.Right);
        var op = bin.Operator switch {
          OperatorType.Addition => "+",
          OperatorType.Subtraction => "-",
          OperatorType.Multiplication => "*",
          OperatorType.Division => "/",
          OperatorType.Modulus => "%",
          OperatorType.LessThan => "<",
          OperatorType.LessThanEqual => "<=",
          OperatorType.GreaterThan => ">",
          OperatorType.GreaterThanEqual => ">=",
          OperatorType.Equal => "==",
          OperatorType.NotEqual => "!=",
          OperatorType.And => "&&",
          OperatorType.Or => "||",
          OperatorType.Assignment => "=",
          OperatorType.AdditionAssignment => "+=",
          OperatorType.SubtractionAssignment => "-=",
          OperatorType.MultiplicationAssignment => "*=",
          OperatorType.DivisionAssignment => "/=",
          OperatorType.ModulusAssignment => "%=",
          _ => throw new NotImplementedException($"Operator {bin.Operator} not supported")
        };
        if (isAssignment) {
          return $"{left} {op} {right}";
        } else {
          return $"({left} {op} {right})";
        }

      case TernaryExpression tern:
        var cond = TranspileExpression(tern.Condition);
        var cons = TranspileExpression(tern.Consequent);
        var alt = TranspileExpression(tern.Alternate);
        return $"(Program.IsTruthy({cond}) ? {cons} : {alt})";

      case VectorExpression vec:
        if (vec.Elements != null) {
          var elems = string.Join(", ", vec.Elements.Select(TranspileExpression));
          return $"new List<dynamic> {{ {elems} }}";
        } else {
          // constructor initialization
          string lenExpr = TranspileExpression(vec.Len!);
          string initExpr;
          if (vec.Init is IdentifierExpression id && id.Name != "it") {
            initExpr = ResolveVariable(id.Name);
          } else {
            initExpr = $"(Func<dynamic, dynamic>)((dynamic it) => {TranspileExpression(vec.Init!)})";
          }
          return $"Program.CreateVector({lenExpr}, {initExpr})";
        }

      case StructInstanceExpression inst:
        var fieldsStr = string.Join(", ", inst.Properties.Select(p => $"{p.Identifier}: {TranspileExpression(p.Value!)}"));
        return $"new {inst.StructName}({fieldsStr})";

      case FunctionCallExpression call:
        var callee = ResolveVariable(call.Callee);
        var args = string.Join(", ", call.Arguments.Select(TranspileExpression));
        return $"{callee}({args})";

      case FunctionLambdaExpression lambda:
        var lambdaParams = string.Join(", ", lambda.Parameters.Select(p => $"dynamic {DefineVariable(p.Identifier)}"));
        var types = Enumerable.Repeat("dynamic", lambda.Parameters.Count + 1);
        var funcType = $"Func<{string.Join(", ", types)}>";
        
        // Lambda body
        var origCode = _code.ToString();
        _code.Clear();
        var origIndent = _indent;
        _indent = 0;
        
        EnterScope();
        TranspileStatement(lambda.Body);
        ExitScope();

        var lambdaBody = _code.ToString().Trim();
        _code.Clear();
        _code.Append(origCode);
        _indent = origIndent;

        return $"({funcType})(({lambdaParams}) => {lambdaBody})";

      default:
        throw new NotImplementedException($"Transpiler: Expression of type '{expr.GetType().Name}' is not implemented.");
    }
  }
}
