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
  }
];

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
