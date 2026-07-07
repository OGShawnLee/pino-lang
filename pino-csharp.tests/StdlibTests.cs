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

  // --- GLOBAL FUNCTIONS ---

  [Fact]
  public void TestTypeFunction() {
    var code = @"
      println(type(10))
      println(type(3.14))
      println(type(""hello""))
      println(type(true))
      println(type([1, 2]))
      struct User { name string }
      println(type(User { name: ""Test"" }))
    ";
    var output = RunCode(code);
    Assert.Equal("int\nfloat\nstring\nbool\nvector\nstruct\n", output);
  }

  [Fact]
  public void TestStrFunction() {
    var code = @"
      struct User { name string }
      println(str(User { name: ""Test"" }))
    ";
    var output = RunCode(code);
    Assert.Equal("User { name: Test }\n", output);
  }

  [Fact]
  public void TestTimeFunction() {
    var code = @"println(type(time()))";
    var output = RunCode(code);
    Assert.Equal("float\n", output);
  }

  [Fact]
  public void TestRandFunction() {
    var code = @"
      println(type(rand()))
      println(type(rand(100)))
    ";
    var output = RunCode(code);
    Assert.Equal("float\nint\n", output);
  }

  [Fact]
  public void TestSleepFunction() {
    var code = @"
      sleep(10)
      println(""Awake"")
    ";
    var output = RunCode(code);
    Assert.Equal("Awake\n", output);
  }

  // --- STRING PROPERTIES AND METHODS ---

  [Fact]
  public void TestStringLength() {
    var code = @"
      var s = ""Hello""
      println(s:len)
      println(s:length)
    ";
    var output = RunCode(code);
    Assert.Equal("5\n5\n", output);
  }

  [Fact]
  public void TestStringTrim() {
    var code = @"println(""  hello  "":trim())";
    var output = RunCode(code);
    Assert.Equal("hello\n", output);
  }

  [Fact]
  public void TestStringLower() {
    var code = @"println(""Hello"":lower())";
    var output = RunCode(code);
    Assert.Equal("hello\n", output);
  }

  [Fact]
  public void TestStringUpper() {
    var code = @"println(""hello"":upper())";
    var output = RunCode(code);
    Assert.Equal("HELLO\n", output);
  }

  [Fact]
  public void TestStringContains() {
    var code = @"
      var s = ""Hello Pino""
      println(s:contains(""Pino""))
      println(s:contains(""other""))
    ";
    var output = RunCode(code);
    Assert.Equal("True\nFalse\n", output);
  }

  [Fact]
  public void TestStringSplit() {
    var code = @"
      var parts = ""Hello Pino Lang"":split("" "")
      parts:each(println)
    ";
    var output = RunCode(code);
    Assert.Equal("Hello\nPino\nLang\n", output);
  }

  [Fact]
  public void TestStringReplace() {
    var code = @"println(""Hello Pino"":replace(""Hello"", ""Goodbye""))";
    var output = RunCode(code);
    Assert.Equal("Goodbye Pino\n", output);
  }

  [Fact]
  public void TestStringSubstring() {
    var code = @"println(""hello world"":substring(6, 5))";
    var output = RunCode(code);
    Assert.Equal("world\n", output);
  }

  [Fact]
  public void TestStringStartsWith() {
    var code = @"println(""hello"":starts_with(""he""))";
    var output = RunCode(code);
    Assert.Equal("True\n", output);
  }

  [Fact]
  public void TestStringEndsWith() {
    var code = @"println(""hello"":ends_with(""lo""))";
    var output = RunCode(code);
    Assert.Equal("True\n", output);
  }

  [Fact]
  public void TestStringIndexOf() {
    var code = @"println(""hello"":index_of(""ll""))";
    var output = RunCode(code);
    Assert.Equal("2\n", output);
  }

  [Fact]
  public void TestStringTrimStart() {
    var code = @"println(""  hello"":trim_start())";
    var output = RunCode(code);
    Assert.Equal("hello\n", output);
  }

  [Fact]
  public void TestStringTrimEnd() {
    var code = @"println(""hello  "":trim_end())";
    var output = RunCode(code);
    Assert.Equal("hello\n", output);
  }

  // --- REGEX PROPERTIES AND METHODS ---

  [Fact]
  public void TestRegexConstructorAndPattern() {
    var code = @"
      val r = regex(""[0-9]+"")
      println(type(r))
      println(r:pattern)
    ";
    var output = RunCode(code);
    Assert.Equal("regex\n[0-9]+\n", output);
  }

  [Fact]
  public void TestRegexMatchPrefix() {
    var code = @"
      val r = regex(""[0-9]+"")
      println(r:match_prefix(""123abc456""))
    ";
    var output = RunCode(code);
    Assert.Equal("123\n", output);
  }

  [Fact]
  public void TestRegexFind() {
    var code = @"
      val r = regex(""[0-9]+"")
      println(r:find(""abc456def""))
    ";
    var output = RunCode(code);
    Assert.Equal("456\n", output);
  }

  [Fact]
  public void TestRegexFindAll() {
    var code = @"
      val r = regex(""[0-9]+"")
      var matches = r:find_all(""12abc34def56"")
      matches:each(println)
    ";
    var output = RunCode(code);
    Assert.Equal("12\n34\n56\n", output);
  }

  [Fact]
  public void TestRegexHasMatch() {
    var code = @"
      val r = regex(""[0-9]+"")
      println(r:has_match(""abc""))
      println(r:has_match(""abc123def""))
    ";
    var output = RunCode(code);
    Assert.Equal("False\nTrue\n", output);
  }

  [Fact]
  public void TestRegexReplace() {
    var code = @"
      val r = regex(""[0-9]+"")
      println(r:replace(""a12b34c"", ""X""))
    ";
    var output = RunCode(code);
    Assert.Equal("aXbXc\n", output);
  }

  // --- MAP AND IN OPERATOR ---

  [Fact]
  public void TestMapDeclarationAndAccess() {
    var code = @"
      val m = map[string, int] {
        ""James"": 12
        ""Julian"": 32
      }
      println(type(m))
      println(m[""James""])
      m[""James""] = 15
      println(m[""James""])
    ";
    var output = RunCode(code);
    Assert.Equal("map\n12\n15\n", output);
  }

  [Fact]
  public void TestMapCompoundAssignment() {
    var code = @"
      val m = map[string, int] {
        ""James"": 15
      }
      m[""James""] += 5
      println(m[""James""])
    ";
    var output = RunCode(code);
    Assert.Equal("20\n", output);
  }

  [Fact]
  public void TestMapFormatting() {
    var code = @"
      val m = map[string, int] {
        ""James"": 20
        ""Julian"": 32
      }
      println(str(m))
    ";
    var output = RunCode(code);
    Assert.Equal("{\"James\": 20, \"Julian\": 32}\n", output);
  }

  [Fact]
  public void TestMapLength() {
    var code = @"
      val m = map[string, int] {
        ""James"": 20
        ""Julian"": 32
      }
      println(m:len)
      println(m:length)
    ";
    var output = RunCode(code);
    Assert.Equal("2\n2\n", output);
  }

  [Fact]
  public void TestMapKeys() {
    var code = @"
      val m = map[string, int] {
        ""James"": 20
        ""Julian"": 32
      }
      var keysList = m:keys()
      println(type(keysList))
      println(keysList:len)
    ";
    var output = RunCode(code);
    Assert.Equal("vector\n2\n", output);
  }

  [Fact]
  public void TestMapRemove() {
    var code = @"
      val m = map[string, int] {
        ""James"": 20
        ""Julian"": 32
      }
      var removedVal = m:remove(""James"")
      println(removedVal)
      println(m:len)
    ";
    var output = RunCode(code);
    Assert.Equal("20\n1\n", output);
  }

  [Fact]
  public void TestInOperatorMap() {
    var code = @"
      val m = map[string, int] {
        ""Julian"": 32
      }
      println(""Julian"" in m)
      println(""James"" in m)
    ";
    var output = RunCode(code);
    Assert.Equal("True\nFalse\n", output);
  }

  [Fact]
  public void TestInOperatorVector() {
    var code = @"
      var vec = [10, 20, 30]
      println(20 in vec)
      println(40 in vec)
    ";
    var output = RunCode(code);
    Assert.Equal("True\nFalse\n", output);
  }

  [Fact]
  public void TestInOperatorString() {
    var code = @"
      var strVal = ""Pino Language""
      println(""Pino"" in strVal)
      println(""Java"" in strVal)
    ";
    var output = RunCode(code);
    Assert.Equal("True\nFalse\n", output);
  }

  [Fact]
  public void TestShorthandLambdaItResolution() {
    var code = @"
      var list = [""John"", ""Alice"", ""Bob"", ""Alexander""]
      var filtered = list:filter(it:len <= 4)
      filtered:each(println)
    ";
    var output = RunCode(code);
    Assert.Equal("John\nBob\n", output);
  }

  [Fact]
  public void TestCustomGenericShorthandLambda() {
    var code = @"
      struct Array {
        @generic[T]
        static fn where(list []T, predicate fn(T) bool) []T {
          var res = []T
          for x in list {
            if predicate(x) {
              res:push(x)
            }
          }
          return res
        }
      }

      val character_name_list = [""John"", ""Alice"", ""Bob"", ""Alexander""]
      val short_name_list = Array::where[string](character_name_list, it:len <= 4)
      short_name_list:each(println)
    ";
    var output = RunCode(code);
    Assert.Equal("John\nBob\n", output);
  }
}

