using Xunit;
using System;
using System.IO;
using Pino;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace pino_csharp.tests;

public class LoopTests {
  private string RunCode(string source) {
    var program = Parser.ParseProgramString(source);
    var checker = new Checker();
    checker.Check(program);

    var evaluator = new Evaluator();
    
    using var sw = new StringWriter();
    var originalOut = Console.Out;
    Console.SetOut(sw);
    try {
      evaluator.Execute(program);
    } finally {
      Console.SetOut(originalOut);
    }
    return sw.ToString().Replace("\r\n", "\n");
  }

  [Fact]
  public void TestRangeLoopSingleVar() {
    var code = @"
      for i in 3 {
        println(i)
      }
    ";
    var output = RunCode(code);
    var expected = "0\n1\n2\n";
    Assert.Equal(expected, output);
  }

  [Fact]
  public void TestRangeLoopDoubleVar() {
    var code = @"
      for idx, v in 3 {
        println(""$(idx):$(v)"")
      }
    ";
    var output = RunCode(code);
    var expected = "0:0\n1:1\n2:2\n";
    Assert.Equal(expected, output);
  }

  [Fact]
  public void TestVectorLoopSingleVar() {
    var code = @"
      val items = [10, 20, 30]
      for item in items {
        println(item)
      }
    ";
    var output = RunCode(code);
    var expected = "10\n20\n30\n";
    Assert.Equal(expected, output);
  }

  [Fact]
  public void TestVectorLoopDoubleVar() {
    var code = @"
      val items = [10, 20, 30]
      for idx, item in items {
        println(""$(idx):$(item)"")
      }
    ";
    var output = RunCode(code);
    var expected = "0:10\n1:20\n2:30\n";
    Assert.Equal(expected, output);
  }

  [Fact]
  public void TestMapLoopSingleVar() {
    var code = @"
      val m = map[string, int] { ""a"": 1, ""b"": 2 }
      for k in m {
        println(k)
      }
    ";
    var output = RunCode(code);
    Assert.Contains("a", output);
    Assert.Contains("b", output);
  }

  [Fact]
  public void TestMapLoopDoubleVar() {
    var code = @"
      val m = map[string, int] { ""a"": 1, ""b"": 2 }
      for k, v in m {
        println(""$(k):$(v)"")
      }
    ";
    var output = RunCode(code);
    Assert.Contains("a:1", output);
    Assert.Contains("b:2", output);
  }
}
