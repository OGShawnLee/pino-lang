using Xunit;
using System;
using Pino;

namespace pino_csharp.tests.Execution;

public class DataStructureTests {
  [Fact]
  public void TestStructEmbeddingEvaluation() {
    var input = @"
      struct Parent {
        a int
        fn hello() string {
          return ""hello""
        }
      }

      struct Child {
        Parent
        b int
        fn hello() string {
          return ""world""
        }
      }

      val obj = Child {
        a: 100,
        b: 200
      }

      val valA = obj:a
      val valB = obj:b
      val greeting = obj:hello()
    ";
    var env = PinoTestRunner.Execute(input, ExecutionEngine.TreeWalk);
    Assert.Equal(100L, env.Get("valA"));
    Assert.Equal(200L, env.Get("valB"));
    Assert.Equal("world", env.Get("greeting"));
  }

  [Fact]
  public void TestStaticMethodEvaluation() {
    var input = @"
      struct MathUtils {
        factor int
        static fn multiply(a int, b int) int {
          return a * b
        }
        fn get_factor() int {
          return factor
        }
      }
      val res = MathUtils::multiply(6, 7)
    ";
    var env = PinoTestRunner.Execute(input, ExecutionEngine.TreeWalk);
    Assert.Equal(42L, env.Get("res"));
  }

  [Fact]
  public void TestMapEvaluatorRuntimeBehavior() {
    // 1. Basic operations: insertion, lookup, updates
    var code = @"
      val m = map[string, int] { ""a"": 1 }
      m[""b""] = 2
      m[""a""] = 10
      m[""a""] += 5
      
      val val_a = m[""a""]
      val val_b = m[""b""]
      
      # Properties and methods
      val len_val = m:len
      val keys_len = m:keys():len
      val values_len = m:values():len
      
      # Remove
      val removed = m:remove(""b"")
      val len_after_remove = m:len
      
      # In operator
      val in_a = ""a"" in m
      val in_b = ""b"" in m
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);

    Assert.Equal(15L, env.Get("val_a"));
    Assert.Equal(2L, env.Get("val_b"));
    Assert.Equal(2L, env.Get("len_val"));
    Assert.Equal(2L, env.Get("keys_len"));
    Assert.Equal(2L, env.Get("values_len"));
    Assert.Equal(2L, env.Get("removed"));
    Assert.Equal(1L, env.Get("len_after_remove"));
    Assert.Equal(true, env.Get("in_a"));
    Assert.Equal(false, env.Get("in_b"));

    // 2. Exception on missing key
    var missingKeyCode = @"
      val m = map[string, int] { ""a"": 1 }
      val x = m[""b""]
    ";
    Assert.ThrowsAny<Exception>(() => PinoTestRunner.Execute(missingKeyCode, ExecutionEngine.TreeWalk));

    // 3. Exception on null key
    var nullKeyCode = @"
      struct Wrapper { key string }
      val w = Wrapper {}
      val m = map[string, int] { ""a"": 1 }
      val x = m[w:key]
    ";
    Assert.ThrowsAny<Exception>(() => PinoTestRunner.Execute(nullKeyCode, ExecutionEngine.TreeWalk));
  }

  [Fact]
  public void TestEnumComparisonAndEvaluation() {
    var code = @"
      enum LogLevel { Info, Warning, Error }
      val eq1 = LogLevel::Info == LogLevel::Info
      val eq2 = LogLevel::Info == LogLevel::Warning
      val neq = LogLevel::Info != LogLevel::Error
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(true, env.Get("eq1"));
    Assert.Equal(false, env.Get("eq2"));
    Assert.Equal(true, env.Get("neq"));
  }

  [Fact]
  public void TestEnumBuiltInFunctions() {
    var code = @"
      enum Direction { North, South }
      val valType = type(Direction::North)
      val valStr = str(Direction::North)
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal("enum", env.Get("valType"));
    Assert.Equal("Direction::North", env.Get("valStr"));
  }

  [Fact]
  public void TestVectorEmptyInitialization() {
    var code = @"
      val items = []int

      items:push(0)

      val n = items[0]
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(0L, env.Get("n"));
  }

  [Fact]
  public void TestVectorEmptyInitializationComplex() {
    var code = @"
      val matrix = [][]int
      val int_list = []int

      matrix:push(int_list)

      val is_in_matrix = int_list in matrix

      val map_list = []map[int, int]
      val str_int_map = map[string, int] { ""one"": 1 }
      
      map_list:push(str_int_map)

      val one = map_list[0][""one""]

      val fn_list = []fn (int) int
      val int_fn = fn (n int) => n

      fn_list:push(int_fn)

      val is_fn_in_list = int_fn in fn_list
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(true, env.Get("is_in_matrix"));
    Assert.Equal(1L, env.Get("one"));
    Assert.Equal(true, env.Get("is_fn_in_list"));
  }

  [Fact]
  public void TestVectorLoopSingleVar() {
    var code = @"
      val items = [10, 20, 30]
      var sum = 0
      for item in items {
        sum = sum + item
      }
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(60L, env.Get("sum"));
  }

  [Fact]
  public void TestVectorLoopDoubleVar() {
    var code = @"
      val items = [10, 20, 30]
      var sumIdx = 0
      var sumItem = 0
      for idx, item in items {
        sumIdx = sumIdx + idx
        sumItem = sumItem + item
      }
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(3L, env.Get("sumIdx"));
    Assert.Equal(60L, env.Get("sumItem"));
  }

  [Fact]
  public void TestMapLoopSingleVar() {
    var code = @"
      val m = map[string, int] { ""a"": 1, ""b"": 2 }
      var keysConcat = """"
      for k in m {
        keysConcat = keysConcat + k
      }
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    var result = (string)env.Get("keysConcat")!;
    Assert.Contains("a", result);
    Assert.Contains("b", result);
  }

  [Fact]
  public void TestMapLoopDoubleVar() {
    var code = @"
      val m = map[string, int] { ""a"": 1, ""b"": 2 }
      var pairsConcat = """"
      for k, v in m {
        pairsConcat = pairsConcat + k + str(v)
      }
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    var result = (string)env.Get("pairsConcat")!;
    Assert.Contains("a1", result);
    Assert.Contains("b2", result);
  }

  [Fact]
  public void TestStringLoopSingleVar() {
    var code = @"
      val name = ""Shawn""
      var charsConcat = """"
      for char in name {
        charsConcat = charsConcat + char
      }
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal("Shawn", env.Get("charsConcat"));
  }

  [Fact]
  public void TestStringLoopDoubleVar() {
    var code = @"
      val name = ""Shawn""
      var charsConcat = """"
      var indexSum = 0
      for index, char in name {
        indexSum = indexSum + index
        charsConcat = charsConcat + char
      }
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal("Shawn", env.Get("charsConcat"));
    Assert.Equal(10L, env.Get("indexSum")); // 0 + 1 + 2 + 3 + 4 = 10
  }

  [Fact]
  public void TestRuneInOperator() {
    var code = @"
      val in_str = 'a' in ""abc""
      val not_in_str = 'z' in ""abc""

      val list = ['a', 'b', 'c']
      val in_list = 'b' in list
      val not_in_list = 'x' in list
    ";
    var env = PinoTestRunner.Execute(code, ExecutionEngine.TreeWalk);
    Assert.Equal(true, env.Get("in_str"));
    Assert.Equal(false, env.Get("not_in_str"));
    Assert.Equal(true, env.Get("in_list"));
    Assert.Equal(false, env.Get("not_in_list"));
  }
}
