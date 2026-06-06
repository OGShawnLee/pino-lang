using Xunit;
using System;
using System.IO;
using Pino;

namespace pino_csharp.tests;

public class StdlibTests {
  private string RunCode(string source) {
    var program = Parser.ParseProgramString(source);
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
  public void TestGlobalFunctions() {
    var code = @"
      println(type(10))
      println(type(3.14))
      println(type(""hello""))
      println(type(true))
      println(type([1, 2]))

      struct User { name string }
      var u = User { name: ""Test"" }
      println(type(u))
      println(str(u))

      var t = time()
      println(type(t))

      var r = rand()
      println(type(r))

      var rInt = rand(100)
      println(type(rInt))

      sleep(10)
      println(""Awake"")
    ";
    var output = RunCode(code);
    var expected = "int\nfloat\nstring\nbool\nvector\nstruct\nUser { name: Test }\nint\nfloat\nint\nAwake\n";
    Assert.Equal(expected, output);
  }

  [Fact]
  public void TestStringPropertiesAndMethods() {
    var code = @"
      var s = ""  Hello Pino Lang!  ""
      println(s:len)
      println(s:length)

      var trimmed = s:trim()
      println(trimmed)
      println(trimmed:lower())
      println(trimmed:upper())

      println(trimmed:contains(""Pino""))
      println(trimmed:contains(""other""))

      var parts = trimmed:split("" "")
      parts:each(println)

      var replaced = trimmed:replace(""Hello"", ""Goodbye"")
      println(replaced)
    ";
    var output = RunCode(code);
    var expected = "20\n20\nHello Pino Lang!\nhello pino lang!\nHELLO PINO LANG!\nTrue\nFalse\nHello\nPino\nLang!\nGoodbye Pino Lang!\n";
    Assert.Equal(expected, output);
  }
}
