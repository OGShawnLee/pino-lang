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
    name: "Complex type signatures (arrays and function parameters)",
    code: `
      struct Mock {
        data []int
      }
      fn each(arr []int, fun fn (int) int) {
        for it in arr {
          fun(it)
        }
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
