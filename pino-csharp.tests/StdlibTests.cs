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

  [Fact]
  public void TestMapAndInOperator() {
    var code = @"
      # Declaration without commas
      val m = map[string, int] {
        ""James"": 12
        ""Julian"": 32
      }
      println(type(m))
      println(m[""James""])
      
      # Index write
      m[""James""] = 15
      println(m[""James""])
      
      # Compound assignment
      m[""James""] += 5
      println(m[""James""])

      # Map formatting string
      println(str(m))

      # length properties
      println(m:len)
      println(m:length)

      # keys and values
      var keysList = m:keys()
      println(type(keysList))
      println(keysList:len)

      # remove
      var removedVal = m:remove(""James"")
      println(removedVal)
      println(m:len)

      # in operator for maps
      println(""Julian"" in m)
      println(""James"" in m)

      # in operator for vectors
      var vec = [10, 20, 30]
      println(20 in vec)
      println(40 in vec)

      # in operator for strings
      var strVal = ""Pino Language""
      println(""Pino"" in strVal)
      println(""Java"" in strVal)
    ";
    var output = RunCode(code);
    var expected = "map\n12\n15\n20\n{\"James\": 20, \"Julian\": 32}\n2\n2\nvector\n2\n20\n1\nTrue\nFalse\nTrue\nFalse\nTrue\nFalse\n";
    Assert.Equal(expected, output);
  }
}
