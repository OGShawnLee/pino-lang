using Xunit;
using System.Collections.Generic;
using Pino;

namespace pino_csharp.tests;

public class ParserTests {
  [Fact]
  public void TestParseVariableDeclaration() {
    var input = "val name = \"Shawn\"";
    var stmt = Parser.ParseString(input);

    var varDecl = Assert.IsType<VariableDeclaration>(stmt);
    Assert.Equal("name", varDecl.Identifier);
    Assert.Equal(VariableKind.Constant, varDecl.Kind);

    var valExpr = Assert.IsType<LiteralExpression>(varDecl.Value);
    Assert.Equal("Shawn", valExpr.Value);
    Assert.Equal(LiteralType.String, valExpr.LiteralType);
  }

  [Fact]
  public void TestParseFunctionDeclaration() {
    var input = @"fn add(a int, b int) {
      return a + b
    }";
    var stmt = Parser.ParseString(input);

    var fnDecl = Assert.IsType<FunctionDeclaration>(stmt);
    Assert.Equal("add", fnDecl.Identifier);
    Assert.Equal(2, fnDecl.Parameters.Count);
    Assert.Equal("a", fnDecl.Parameters[0].Identifier);
    Assert.Equal("int", fnDecl.Parameters[0].Typing);
    Assert.Equal("b", fnDecl.Parameters[1].Identifier);
    Assert.Equal("int", fnDecl.Parameters[1].Typing);

    var block = Assert.IsType<BlockStatement>(fnDecl.Body);
    Assert.Single(block.Statements);

    var retStmt = Assert.IsType<ReturnStatement>(block.Statements[0]);
    var binExpr = Assert.IsType<BinaryExpression>(retStmt.Argument);
    Assert.Equal(OperatorType.Addition, binExpr.Operator);
    Assert.Equal("a", Assert.IsType<IdentifierExpression>(binExpr.Left).Name);
    Assert.Equal("b", Assert.IsType<IdentifierExpression>(binExpr.Right).Name);
  }

  [Fact]
  public void TestParseStructDeclaration() {
    var input = @"struct Vector2 {
      x int
      y int
      fn magnitude {
        return x
      }
    }";
    var stmt = Parser.ParseString(input);

    var structDecl = Assert.IsType<StructDeclaration>(stmt);
    Assert.Equal("Vector2", structDecl.Identifier);
    Assert.Equal(2, structDecl.Fields.Count);
    Assert.Equal("x", structDecl.Fields[0].Identifier);
    Assert.Equal("y", structDecl.Fields[1].Identifier);

    Assert.Single(structDecl.Methods);
    Assert.Equal("magnitude", structDecl.Methods[0].Identifier);
  }

  [Fact]
  public void TestParseMatchStatement() {
    var input = @"match name {
      when ""Shawn"" { return true }
      else { return false }
    }";
    var stmt = Parser.ParseString(input);

    var matchStmt = Assert.IsType<MatchStatement>(stmt);
    Assert.Equal("name", Assert.IsType<IdentifierExpression>(matchStmt.Condition).Name);
    Assert.Single(matchStmt.Branches);

    var branch = matchStmt.Branches[0];
    Assert.Single(branch.Conditions);
    Assert.Equal("Shawn", Assert.IsType<LiteralExpression>(branch.Conditions[0]).Value);

    var alternate = Assert.IsType<ElseStatement>(matchStmt.Alternate);
    var altBlock = Assert.IsType<BlockStatement>(alternate.Body);
    Assert.IsType<ReturnStatement>(altBlock.Statements[0]);
  }

  [Fact]
  public void TestParseOperatorPrecedence() {
    var input = "1 + 2 * 3";
    var stmt = Parser.ParseString(input);

    var binExpr = Assert.IsType<BinaryExpression>(stmt);
    Assert.Equal(OperatorType.Addition, binExpr.Operator);
    Assert.Equal("1", Assert.IsType<LiteralExpression>(binExpr.Left).Value);

    var rightBin = Assert.IsType<BinaryExpression>(binExpr.Right);
    Assert.Equal(OperatorType.Multiplication, rightBin.Operator);
    Assert.Equal("2", Assert.IsType<LiteralExpression>(rightBin.Left).Value);
    Assert.Equal("3", Assert.IsType<LiteralExpression>(rightBin.Right).Value);
  }

  [Fact]
  public void TestParseStringInterpolationExpression() {
    var input = "\"Base Attack: $(player:attack)\"";
    var stmt = Parser.ParseString(input);

    var binExpr = Assert.IsType<BinaryExpression>(stmt);
    Assert.Equal(OperatorType.Addition, binExpr.Operator);

    var leftLit = Assert.IsType<LiteralExpression>(binExpr.Left);
    Assert.Equal("Base Attack: ", leftLit.Value);
    Assert.Equal(LiteralType.String, leftLit.LiteralType);

    var rightMember = Assert.IsType<BinaryExpression>(binExpr.Right);
    Assert.Equal(OperatorType.MemberAccess, rightMember.Operator);
    Assert.Equal("player", Assert.IsType<IdentifierExpression>(rightMember.Left).Name);
    Assert.Equal("attack", Assert.IsType<IdentifierExpression>(rightMember.Right).Name);
  }

  [Fact]
  public void TestImplicitLambdaArgumentWrapping() {
    var input = "map(it * 3)";
    var stmt = Parser.ParseString(input);

    var call = Assert.IsType<FunctionCallExpression>(stmt);
    Assert.Equal("map", call.Callee);
    Assert.Single(call.Arguments);

    var lambda = Assert.IsType<FunctionLambdaExpression>(call.Arguments[0]);
    Assert.Single(lambda.Parameters);
    Assert.Equal("it", lambda.Parameters[0].Identifier);

    var block = Assert.IsType<BlockStatement>(lambda.Body);
    Assert.Single(block.Statements);
    var ret = Assert.IsType<ReturnStatement>(block.Statements[0]);
    
    var bin = Assert.IsType<BinaryExpression>(ret.Argument);
    Assert.Equal(OperatorType.Multiplication, bin.Operator);
    Assert.Equal("it", Assert.IsType<IdentifierExpression>(bin.Left).Name);
    Assert.Equal("3", Assert.IsType<LiteralExpression>(bin.Right).Value);
  }

  [Fact]
  public void TestNoImplicitLambdaWrappingWhenDeclared() {
    var input = @"fn test(it int) {
      map(it * 3)
    }";
    var stmt = Parser.ParseString(input);
    var fn = Assert.IsType<FunctionDeclaration>(stmt);
    var block = Assert.IsType<BlockStatement>(fn.Body);
    Assert.Single(block.Statements);
    
    var call = Assert.IsType<FunctionCallExpression>(block.Statements[0]);
    Assert.Equal("map", call.Callee);
    Assert.Single(call.Arguments);
    
    var bin = Assert.IsType<BinaryExpression>(call.Arguments[0]);
    Assert.Equal(OperatorType.Multiplication, bin.Operator);
    Assert.Equal("it", Assert.IsType<IdentifierExpression>(bin.Left).Name);
  }

  [Fact]
  public void TestStaticMemberAccessVsStructInstanceAmbiguity() {
    var input = @"if difficulty == Difficulty::Medium {
      
    }";
    var stmt = Parser.ParseString(input);
    var ifStmt = Assert.IsType<IfStatement>(stmt);
    
    var bin = Assert.IsType<BinaryExpression>(ifStmt.Condition);
    Assert.Equal(OperatorType.Equal, bin.Operator);
    
    var rightBin = Assert.IsType<BinaryExpression>(bin.Right);
    Assert.Equal(OperatorType.StaticMemberAccess, rightBin.Operator);
    Assert.Equal("Difficulty", Assert.IsType<IdentifierExpression>(rightBin.Left).Name);
    Assert.Equal("Medium", Assert.IsType<IdentifierExpression>(rightBin.Right).Name);
    
    var body = Assert.IsType<BlockStatement>(ifStmt.Consequent);
    Assert.Empty(body.Statements);
  }

  [Fact]
  public void TestParseModuleAndImports() {
    var inputModule = "module Math";
    var stmtModule = Parser.ParseString(inputModule);
    var modDecl = Assert.IsType<ModuleDeclaration>(stmtModule);
    Assert.Equal("Math", modDecl.Identifier);

    var inputImport = "import Combat";
    var stmtImport = Parser.ParseString(inputImport);
    var impStmt = Assert.IsType<ImportStatement>(stmtImport);
    Assert.Equal("Combat", impStmt.ModuleName);

    var inputFrom = "from Entities import Person, Pet";
    var stmtFrom = Parser.ParseString(inputFrom);
    var fromStmt = Assert.IsType<FromImportStatement>(stmtFrom);
    Assert.Equal("Entities", fromStmt.ModuleName);
    Assert.Equal(2, fromStmt.Imports.Count);
    Assert.Equal("Person", fromStmt.Imports[0]);
    Assert.Equal("Pet", fromStmt.Imports[1]);

    var inputPub = "pub fn add(a int) {}";
    var stmtPub = Parser.ParseString(inputPub);
    var fnDecl = Assert.IsType<FunctionDeclaration>(stmtPub);
    Assert.True(fnDecl.IsPublic);
  }

  [Fact]
  public void TestStaticMemberAccessStructInitialization() {
    var input = "val person = Entities::Person { name: \"Shawn Lee\" }";
    var stmt = Parser.ParseString(input);
    var varDecl = Assert.IsType<VariableDeclaration>(stmt);
    Assert.Equal("person", varDecl.Identifier);

    var staticMember = Assert.IsType<BinaryExpression>(varDecl.Value);
    Assert.Equal(OperatorType.StaticMemberAccess, staticMember.Operator);
    Assert.Equal("Entities", Assert.IsType<IdentifierExpression>(staticMember.Left).Name);

    var structInst = Assert.IsType<StructInstanceExpression>(staticMember.Right);
    Assert.Equal("Person", structInst.StructName);
    Assert.Single(structInst.Properties);
    Assert.Equal("name", structInst.Properties[0].Identifier);
    Assert.Equal("Shawn Lee", Assert.IsType<LiteralExpression>(structInst.Properties[0].Value).Value);
  }

  [Fact]
  public void TestStaticMemberAccessVsMemberAssignmentAmbiguity() {
    var input = @"if enum_value == Test::Easy {
      person:name = ""Pedro""
    }";
    var stmt = Parser.ParseString(input);
    var ifStmt = Assert.IsType<IfStatement>(stmt);
    
    var bin = Assert.IsType<BinaryExpression>(ifStmt.Condition);
    Assert.Equal(OperatorType.Equal, bin.Operator);
    
    var rightBin = Assert.IsType<BinaryExpression>(bin.Right);
    Assert.Equal(OperatorType.StaticMemberAccess, rightBin.Operator);
    
    var body = Assert.IsType<BlockStatement>(ifStmt.Consequent);
    Assert.Single(body.Statements);
    Assert.IsAssignableFrom<Expression>(body.Statements[0]);
  }

  [Fact]
  public void TestIndexAccessParsingAndEvaluation() {
    // 1. Test parsing styles[0]
    var input = "styles[0]";
    var stmt = Parser.ParseString(input);
    var indexAccess = Assert.IsType<IndexAccessExpression>(stmt);
    Assert.Equal("styles", Assert.IsType<IdentifierExpression>(indexAccess.Target).Name);
    Assert.Equal("0", Assert.IsType<LiteralExpression>(indexAccess.Index).Value);

    // 2. Test parsing and evaluation of list indexing
    var programInput = @"
      val arr = [10, 20, 30]
      val first = arr[0]
      val second = arr[1]
      
      var mutableArr = [1, 2]
      mutableArr[1] = 99
      val modified = mutableArr[1]
      
      var cArr = [10]
      cArr[0] += 5
      val compoundVal = cArr[0]

      val text = ""Pino""
      val charP = text[0]

      struct Style {
        name string
      }
      val styles = [
        Style { name: ""Leaping tiger"" },
        Style { name: ""Iron Fist"" }
      ]
      val current_char = styles[1]:name[0]

      val numList = [10, 20, 30, 40]
      val foundVal = numList:find(it > 25)
      val foundIdx = numList:find_index(it > 25)
      val hasAny = numList:any(it == 30)
      val hasNone = numList:any(it == 99)
      val hasAll = numList:all(it >= 10)
      val notAll = numList:all(it > 20)
    ";
    var program = Parser.ParseProgramString(programInput);
    var evaluator = new Evaluator();
    var env = new Pino.Environment();
    evaluator.Execute(program, env);

    Assert.Equal(10L, env.Get("first"));
    Assert.Equal(20L, env.Get("second"));
    Assert.Equal(99L, env.Get("modified"));
    Assert.Equal(15L, env.Get("compoundVal"));
    Assert.Equal("P", env.Get("charP"));
    Assert.Equal("I", env.Get("current_char"));

    Assert.Equal(30L, env.Get("foundVal"));
    Assert.Equal(2L, env.Get("foundIdx"));
    Assert.True((bool)env.Get("hasAny")!);
    Assert.False((bool)env.Get("hasNone")!);
    Assert.True((bool)env.Get("hasAll")!);
    Assert.False((bool)env.Get("notAll")!);
  }

  [Fact]
  public void TestParseMapAndIn() {
    var input = "val m = map[string, int] { \"a\": 1, \"b\": 2 }";
    var stmt = Parser.ParseString(input);
    var varDecl = Assert.IsType<VariableDeclaration>(stmt);
    Assert.Equal("m", varDecl.Identifier);

    var mapExpr = Assert.IsType<MapExpression>(varDecl.Value);
    Assert.Equal("string", mapExpr.KeyType);
    Assert.Equal("int", mapExpr.ValueType);
    Assert.Equal(2, mapExpr.Entries.Count);

    Assert.Equal("a", Assert.IsType<LiteralExpression>(mapExpr.Entries[0].Key).Value);
    Assert.Equal("1", Assert.IsType<LiteralExpression>(mapExpr.Entries[0].Value).Value);

    var inInput = "1 in [1, 2]";
    var inStmt = Parser.ParseString(inInput);
    var binExpr = Assert.IsType<BinaryExpression>(inStmt);
    Assert.Equal(OperatorType.In, binExpr.Operator);
    Assert.Equal("1", Assert.IsType<LiteralExpression>(binExpr.Left).Value);
    Assert.IsType<VectorExpression>(binExpr.Right);
  }

  [Fact]
  public void TestParseMapTypeSignature() {
    var structInput = @"struct State {
      state map[int, string]
    }";
    var structStmt = Parser.ParseString(structInput);
    var structDecl = Assert.IsType<StructDeclaration>(structStmt);
    Assert.Equal("State", structDecl.Identifier);
    Assert.Single(structDecl.Fields);
    Assert.Equal("state", structDecl.Fields[0].Identifier);
    Assert.Equal("map[int, string]", structDecl.Fields[0].Typing);

    var fnInput = @"fn print(dict map[string, int]) {}";
    var fnStmt = Parser.ParseString(fnInput);
    var fnDecl = Assert.IsType<FunctionDeclaration>(fnStmt);
    Assert.Equal("print", fnDecl.Identifier);
    Assert.Single(fnDecl.Parameters);
    Assert.Equal("dict", fnDecl.Parameters[0].Identifier);
    Assert.Equal("map[string, int]", fnDecl.Parameters[0].Typing);
  }

  [Fact]
  public void TestParseInterfaceDeclaration() {
    var input = @"interface State {
      fn execute_state(context Context)
    }";
    var stmt = Parser.ParseString(input);
    var interfaceDecl = Assert.IsType<InterfaceDeclaration>(stmt);
    Assert.Equal("State", interfaceDecl.Identifier);
    Assert.Single(interfaceDecl.Methods);
    
    var method = interfaceDecl.Methods[0];
    Assert.Equal("execute_state", method.Identifier);
    Assert.Single(method.Parameters);
    Assert.Equal("context", method.Parameters[0].Identifier);
    Assert.Equal("Context", method.Parameters[0].Typing);
    Assert.Null(method.Body);
  }

  [Fact]
  public void TestTypeCheckerValidInterface() {
    var input = @"
      interface Greeter {
        fn greet(name string)
      }
      
      struct User {
        fn greet(name string) {
          println(""Hello, "" + name)
        }
      }
      
      fn run_greet(g Greeter) {
        g:greet(""Shawn"")
      }
      
      val u = User {}
      run_greet(u)
    ";
    var program = Parser.ParseProgramString(input);
    var checker = new TypeChecker();
    
    checker.Check(program);
  }

  [Fact]
  public void TestTypeCheckerInvalidInterfaceThrows() {
    var input = @"
      interface Greeter {
        fn greet(name string)
      }
      
      struct User {
        fn other() {}
      }
      
      fn run_greet(g Greeter) {}
      
      val u = User {}
      run_greet(u)
    ";
    var program = Parser.ParseProgramString(input);
    var checker = new TypeChecker();
    
    Assert.ThrowsAny<Exception>(() => checker.Check(program));
  }

  [Fact]
  public void TestTypeCheckerVectorMapInferenceError() {
    var input = @"
      val list = []int { len: 3, init: it * 3 }
      val list_str = list:map(""$it is a string"")

      fn print_list(list []int) {}

      print_list(list_str)
    ";
    var program = Parser.ParseProgramString(input);
    var checker = new TypeChecker();
    
    Assert.ThrowsAny<Exception>(() => checker.Check(program));
  }

  [Fact]
  public void TestTypeCheckerFunctionSignatureScopingAndCompatibility() {
    var input = @"
      val list = []int { len: 3, init: it * 3 }
      
      fn print_list(list []int, on_each fn (int)) {
        list:each(on_each)
      }
      
      print_list(list, it * 2)
    ";
    var program = Parser.ParseProgramString(input);
    var checker = new TypeChecker();
    
    checker.Check(program);
  }

  [Fact]
  public void TestTypeCheckerDeclaredReturnType() {
    var input = @"
      struct Product {
        price int

        fn get_double_price() int {
          return price * 2
        }
      }

      fn another_get_double(n int) int {
        return n * 2
      }
    ";
    var program = Parser.ParseProgramString(input);
    var checker = new TypeChecker();
    
    checker.Check(program);
  }

  [Fact]
  public void TestTypeCheckerDeclaredReturnTypeInvalid() {
    var input = @"
      fn get_name() string {
        return 42
      }
    ";
    var program = Parser.ParseProgramString(input);
    var checker = new TypeChecker();
    
    Assert.ThrowsAny<Exception>(() => checker.Check(program));
  }
}


