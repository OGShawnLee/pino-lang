const { Lexer, Parser, Interpreter, runPinoCode } = require('../interpreter.js');

const tests = [
  {
    name: "Struct member assignment and access (Issue)",
    code: `
      struct Player {
        name string
      }
      var hero = Player { name: "Marcus" }
      hero:name = "Shawn"
      println("Name: $(hero:name)")
    `,
    expectedOutput: "Name: Shawn\n"
  },
  {
    name: "Lambda function expression with no parameters",
    code: `
      val get_hero_name = fn {
        return "Shawn Lee"
      }
      println(get_hero_name())
    `,
    expectedOutput: "Shawn Lee\n"
  },
  {
    name: "Lambda function expression with parameters",
    code: `
      val add = fn(a number, b number) {
        return a + b
      }
      println(add(3, 4))
    `,
    expectedOutput: "7\n"
  },
  {
    name: "Basic arithmetic and string interpolation",
    code: `
      val x = 10
      val y = 20
      println("Sum: $(x + y)")
    `,
    expectedOutput: "Sum: 30\n"
  },
  {
    name: "Conditionals (if-else)",
    code: `
      val score = 85
      if score >= 90 {
        println("A")
      } else if score >= 80 {
        println("B")
      } else {
        println("C")
      }
    `,
    expectedOutput: "B\n"
  },
  {
    name: "For loop iteration over range",
    code: `
      for i in 3 {
        println(i)
      }
    `,
    expectedOutput: "0\n1\n2\n"
  },
  {
    name: "Complex type signatures (arrays, maps, and function parameters)",
    code: `
      struct State {
        state map[int, string]
      }
      struct Mock {
        data []int
      }
      fn each(arr []int, fun fn (int) int) {
        for it in arr {
          fun(it)
        }
      }
      fn print(dict map[string, int]) {
        println(dict)
      }
      println("Type signatures parsed successfully!")
    `,
    expectedOutput: "Type signatures parsed successfully!\n"
  },
  {
    name: "Array/Vector utility methods (len, each, map, filter, push, pop)",
    code: `
      var arr = [1, 2, 3]
      println(arr:len)
      
      arr:push(4)
      println(arr:length)

      arr:each(println)
      
      var doubleArr = arr:map(fn (it number) {
        return it * 2
      })
      doubleArr:each(println)
      
      var filtered = arr:filter(fn (it number) {
        return it > 2
      })
      filtered:each(println)
      
      var popped = arr:pop()
      println(popped)
      println(arr:len)
    `,
    expectedOutput: "3\n4\n1\n2\n3\n4\n2\n4\n6\n8\n3\n4\n4\n3\n"
  },
  {
    name: "Single-line arrow lambda syntax (=> desugaring)",
    code: `
      var numbers = [1, 2, 3, 4]
      var doubles = numbers:map(fn (it number) => it * 2)
      doubles:each(println)
      var evens = numbers:filter(fn (it number) => it % 2 == 0)
      evens:each(println)
    `,
    expectedOutput: "2\n4\n6\n8\n2\n4\n"
  },
  {
    name: "Vector initialization with callable reference",
    code: `
      val addone = fn (it number) => it + 1
      val numbers = []number { len: 5, init: addone }
      numbers:each(println)
    `,
    expectedOutput: "1\n2\n3\n4\n5\n"
  },
  {
    name: "Standard library global functions (type, str, rand, time)",
    code: `
      println(type(10))
      println(type(3.14))
      println(type("hello"))
      println(type(true))
      println(type([1, 2]))
      
      struct User { name string }
      var u = User { name: "Test" }
      println(type(u))
      println(str(u))
      
      var t = time()
      println(type(t))
      
      var r = rand()
      println(type(r))
      
      var rInt = rand(100)
      println(type(rInt))
      
      sleep(10)
      println("Awake")
    `,
    expectedOutput: "int\nfloat\nstring\nbool\nvector\nstruct\nUser { name: Test }\nint\nfloat\nint\nAwake\n"
  },
  {
    name: "String native properties and methods",
    code: `
      var s = "  Hello Pino Lang!  "
      println(s:len)
      println(s:length)
      
      var trimmed = s:trim()
      println(trimmed)
      println(trimmed:lower())
      println(trimmed:upper())
      
      println(trimmed:contains("Pino"))
      println(trimmed:contains("other"))
      
      var parts = trimmed:split(" ")
      parts:each(println)
      
      var replaced = trimmed:replace("Hello", "Goodbye")
      println(replaced)
    `,
    expectedOutput: "20\n20\nHello Pino Lang!\nhello pino lang!\nHELLO PINO LANG!\ntrue\nfalse\nHello\nPino\nLang!\nGoodbye Pino Lang!\n"
  },
  {
    name: "Implicit lambda argument wrapping containing 'it'",
    code: `
      val numbers = [1, 2, 3, 4]
      var tripled = numbers:map(it * 3)
      tripled:each(println)
    `,
    expectedOutput: "3\n6\n9\n12\n"
  },
  {
    name: "No implicit lambda wrapping when 'it' is declared",
    code: `
      val numbers = [1, 2, 3, 4]
      fn process(it number) {
        println(it * 3)
      }
      process(5)
    `,
    expectedOutput: "15\n"
  },
  {
    name: "Static member access vs empty block condition ambiguity",
    code: `
      enum Difficulty {
        Medium
      }
      val difficulty = Difficulty::Medium
      var n = true
      if difficulty == Difficulty::Medium {
        
      }
      println(n)
    `,
    expectedOutput: "true\n"
  },
  {
    name: "Module Namespace Imports (import Combat)",
    code: `
      import Combat
      Combat::execute_strike(150)
    `,
    expectedOutput: "Strike executed with power: 150\n"
  },
  {
    name: "Module Destructured Imports (from Entities import Hero, max_level)",
    code: `
      from Entities import Hero, max_level
      val h = Hero { name: "Marcus" }
      println("Hero name: $(h:name), Max Level: $max_level")
    `,
    expectedOutput: "Hero name: Marcus, Max Level: 99\n"
  },
  {
    name: "Module Namespace Struct Initialization (Entities::Hero)",
    code: `
      import Entities
      val h = Entities::Hero { name: "Marcus" }
      println("Hero name: $(h:name)")
    `,
    expectedOutput: "Hero name: Marcus\n"
  },
  {
    name: "Static member access vs member assignment ambiguity",
    code: `
      struct Person {
        name string
      }
      enum Test {
        Easy
      }
      val enum_value = Test::Easy
      var person = Person { name: "James" }
      if enum_value == Test::Easy {
        person:name = "Pedro"
      }
      println(person:name)
    `,
    expectedOutput: "Pedro\n"
  },
  {
    name: "Vector and string index access and modification",
    code: `
      val arr = [10, 20, 30]
      println(arr[0])
      println(arr[1])
      
      var mutableArr = [1, 2]
      mutableArr[1] = 99
      println(mutableArr[1])
      
      var cArr = [10]
      cArr[0] += 5
      println(cArr[0])

      val text = "Pino"
      println(text[0])

      struct Style {
        name string
      }
      val styles = [
        Style { name: "Leaping tiger" },
        Style { name: "Iron Fist" }
      ]
      println(styles[1]:name[0])

      val numList = [10, 20, 30, 40]
      println(numList:find(it > 25))
      println(numList:find_index(it > 25))
      println(numList:any(it == 30))
      println(numList:any(it == 99))
      println(numList:all(it >= 10))
      println(numList:all(it > 20))
    `,
    expectedOutput: "10\n20\n99\n15\nP\nI\n30\n2\ntrue\nfalse\ntrue\nfalse\n"
  },
  {
    name: "Map literal creation, operations, and 'in' operator",
    code: `
      var m = map[string, int] {
        "James": 12
        "Julian": 32
      }
      println(type(m))
      println(m["James"])

      m["James"] = 15
      println(m["James"])

      m["James"] += 5
      println(m["James"])

      println(str(m))

      println(m:len)
      println(m:length)

      var keys = m:keys()
      println(type(keys))
      println(keys:len)

      var removed = m:remove("James")
      println(removed)
      println(m:len)

      println("Julian" in m)
      println("James" in m)

      var vec = [10, 20, 30]
      println(20 in vec)
      println(40 in vec)

      var strVal = "Pino Language"
      println("Pino" in strVal)
      println("Java" in strVal)
    `,
    expectedOutput: "map\n12\n15\n20\n{\"James\": 20, \"Julian\": 32}\n2\n2\nvector\n2\n20\n1\ntrue\nfalse\ntrue\nfalse\ntrue\nfalse\n"
  },
  {
    name: "TypeChecker - Valid Interface Assignment",
    code: `
      interface Greeter {
        fn greet(name string)
      }
      struct User {
        fn greet(name string) {
          println("Hello, " + name)
        }
      }
      fn run_greet(g Greeter) {
        g:greet("Shawn")
      }
      val u = User {}
      run_greet(u)
    `,
    expectedOutput: "Hello, Shawn\n"
  },
  {
    name: "TypeChecker - Invalid Interface Assignment",
    code: `
      interface Greeter {
        fn greet(name string)
      }
      struct User {
        fn other() {}
      }
      fn run_greet(g Greeter) {}
      val u = User {}
      run_greet(u)
    `,
    expectedOutput: "[ERROR] TYPE CHECK ERROR: Argument 1 for function 'run_greet' expected type 'Greeter', but got 'User'.\n"
  },
  {
    name: "TypeChecker - Vector map() returns typed array",
    code: `
      val list = []int { len: 3, init: it * 3 }
      val list_str = list:map("$it is a string")
      fn print_list(list []int) {}
      print_list(list_str)
    `,
    expectedOutput: "[ERROR] TYPE CHECK ERROR: Argument 1 for function 'print_list' expected type '[]int', but got '[]string'.\n"
  },
  {
    name: "TypeChecker - Function Signature Scoping and Compatibility (Valid)",
    code: `
      val list = []int { len: 3, init: it * 3 }
      fn print_list(list []int, on_each fn (int)) {
        list:each(on_each)
      }
      print_list(list, it * 2)
      println("Success")
    `,
    expectedOutput: "Success\n"
  },
  {
    name: "TypeChecker - Function Signature Incompatibility (Invalid)",
    code: `
      fn process(callback fn (int) string) {}
      process(fn (it number) => it * 2)
    `,
    expectedOutput: "[ERROR] TYPE CHECK ERROR: Argument 1 for function 'process' expected type 'fn(int) string', but got 'fn(number) number'.\n"
  },
  {
    name: "TypeChecker - Declared Return Type (Valid)",
    code: `
      struct Product {
        price int
        fn get_double_price() int {
          return price * 2
        }
      }
      fn another_get_double(n int) int {
        return n * 2
      }
      println("Success")
    `,
    expectedOutput: "Success\n"
  },
  {
    name: "TypeChecker - Declared Return Type (Invalid)",
    code: `
      fn get_name() string {
        return 42
      }
    `,
    expectedOutput: "[ERROR] TYPE CHECK ERROR: Function 'get_name' declared return type 'string', but returned 'number'.\n"
  },
  {
    name: "TypeChecker - Struct Embedding (Valid)",
    code: `
      struct Shape {
        x int
        y int
      }
      struct Circle {
        Shape
        radius int
      }
      val c = Circle {
        x: 10
        y: 20
        radius: 5
      }
      println(c:x)
      println(c:y)
      println(c:radius)
    `,
    expectedOutput: "10\n20\n5\n"
  },
  {
    name: "TypeChecker - Struct Embedding Shadowing and Methods (Valid)",
    code: `
      struct Parent {
        a int
        fn hello() string {
          return "hello"
        }
      }
      struct Child {
        Parent
        b int
        fn hello() string {
          return "world"
        }
      }
      val obj = Child {
        a: 100
        b: 200
      }
      println(obj:a)
      println(obj:b)
      println(obj:hello())
    `,
    expectedOutput: "100\n200\nworld\n"
  }
];

globalThis.pinoModules = {
  combat: `
    module Combat
    pub fn execute_strike(power int) {
      println("Strike executed with power: $power")
    }
  `,
  entities: `
    module Entities
    pub struct Hero {
      name string
    }
    pub val max_level = 99
  `
};

let failed = 0;

console.log("Running Pino JS Interpreter Regression Tests...\n");

tests.forEach((test, index) => {
  let output = '';
  const onOutput = (text) => { output += text; };
  const onInput = () => '';

  try {
    runPinoCode(test.code, onOutput, onInput);
    if (output === test.expectedOutput) {
      console.log(`[PASS] Test ${index + 1}: ${test.name}`);
    } else {
      console.error(`[FAIL] Test ${index + 1}: ${test.name}`);
      console.error(`  Expected: ${JSON.stringify(test.expectedOutput)}`);
      console.error(`  Got:      ${JSON.stringify(output)}`);
      failed++;
    }
  } catch (err) {
    console.error(`[ERROR] Test ${index + 1}: ${test.name} threw an unexpected error:`);
    console.error(err);
    failed++;
  }
});

console.log("\n--------------------------------");
if (failed === 0) {
  console.log("All tests passed successfully!");
  process.exit(0);
} else {
  console.error(`${failed} test(s) failed.`);
  process.exit(1);
}
