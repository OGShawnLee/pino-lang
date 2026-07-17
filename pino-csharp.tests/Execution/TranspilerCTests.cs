using Xunit;
using System;
using Pino;

namespace pino_csharp.tests.Execution;

public class TranspilerCTests {
  [Fact]
  public void TestTranspilerModuleCompilation() {
    var lexerSource = @"
      module Lexer
      pub struct Point {
        x int
        y int
      }
      pub fn add_coords(p Point) int {
        return p:x + p:y
      }
    ";
    var mainSource = @"
      import Lexer
      fn main {
        val p = Lexer::Point { x: 10, y: 20 }
        val sum = Lexer::add_coords(p)
      }
    ";

    var lexerProgram = Parser.ParseProgramString(lexerSource);
    var lexerChecker = new Checker { IsModule = true };
    lexerChecker.Check(lexerProgram);

    var mainProgram = Parser.ParseProgramString(mainSource);
    var mainChecker = new Checker();
    mainChecker._moduleCheckers["Lexer"] = lexerChecker;
    mainChecker.Check(mainProgram);

    var transpiler = new TranspilerC();
    var cCode = transpiler.Transpile(mainProgram, mainChecker);

    // Verify generated C code contains the module structures and functions!
    Assert.Contains("struct Lexer_Point {", cCode);
    Assert.Contains("int Lexer_add_coords(Lexer_Point* p);", cCode);
    Assert.Contains("int Lexer_add_coords(Lexer_Point* p) {", cCode);
    
    // Verify that struct instantiation outside module translates to cleaned name
    Assert.Contains("Lexer_Point* temp = (Lexer_Point*)pino_malloc(sizeof(Lexer_Point));", cCode);
    
    // Verify function call translates to namespaced function call
    Assert.Contains("Lexer_add_coords(p)", cCode);
  }

  [Fact]
  public void TestTranspilerFromImport() {
    var mathSource = @"
      module Math
      pub struct Point {
        x int
        y int
      }
      pub fn add(a int, b int) int {
        return a + b
      }
    ";
    var mainSource = @"
      from Math import Point, add
      fn main {
        val p = Point { x: 5, y: 10 }
        val res = add(p:x, p:y)
      }
    ";

    var mathProgram = Parser.ParseProgramString(mathSource);
    var mathChecker = new Checker { IsModule = true };
    mathChecker.Check(mathProgram);

    var mainProgram = Parser.ParseProgramString(mainSource);
    var mainChecker = new Checker();
    mainChecker._moduleCheckers["Math"] = mathChecker;
    mainChecker.Check(mainProgram);

    var transpiler = new TranspilerC();
    var cCode = transpiler.Transpile(mainProgram, mainChecker);

    // Verify generated C code contains the module structures and functions!
    Assert.Contains("struct Math_Point {", cCode);
    Assert.Contains("int Math_add(int a, int b);", cCode);

    // Verify that struct instantiation outside module translates to mapped name
    Assert.Contains("Math_Point* temp = (Math_Point*)pino_malloc(sizeof(Math_Point));", cCode);

    // Verify function call translates to mapped function call
    Assert.Contains("Math_add(p->x, p->y)", cCode);
  }

  [Fact]
  public void TestTranspilerStaticMethod() {
    var source = @"
      struct Calculator {
        static fn multiply(a int, b int) int {
          return a * b
        }
      }
      fn main {
        val calc_result = Calculator::multiply(6, 7)
      }
    ";

    var program = Parser.ParseProgramString(source);
    var checker = new Checker();
    checker.Check(program);

    var transpiler = new TranspilerC();
    var cCode = transpiler.Transpile(program, checker);

    // Verify static method prototype has no 'this' parameter
    Assert.Contains("int Calculator_multiply(int a, int b);", cCode);
    // Verify static method definition has no 'this' parameter
    Assert.Contains("int Calculator_multiply(int a, int b) {", cCode);
    // Verify call site has no 'this' parameter passed
    Assert.Contains("Calculator_multiply(6, 7)", cCode);
  }
}
