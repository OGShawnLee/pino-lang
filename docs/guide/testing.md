# Testing in Pino

Pino has built-in `test` blocks and a test runner, `pino test`. You don't need an external library or special assertion functions.

Every Pino snippet on this page passes `pino check`.

## Writing a test

Write a `test` block at the top level of a file with a descriptive name and use `assert(...)` inside it:

<!-- check: ok -->
```pino
fn add(a int, b int) int {
  return a + b
}

test "adds two numbers" {
  assert(add(2, 3) == 5)
}
```

Run tests from your project folder:

```bash
pino test
```

You can also pass a single file or a folder:

```bash
pino test test/main_test.pino
pino test test
```

## How Pino finds tests

With no path, `pino test` searches the current folder recursively (skipping `bin/`, `obj/`, `tooling/` and `.git/`) and runs:

- every file named `*_test.pino`,
- every `.pino` file inside any `test/` folder, and
- any other `.pino` file that contains at least one `test` block.

`pino run` and `pino build` ignore `test` blocks, so you can keep small tests right beside the functions they check or place them in `test/`.

## Assertions

When an `assert` fails on a comparison (`==`, `!=`, `<`, `<=`, `>`, `>=`), Pino prints the expression that was false and the runtime value of every non-literal side:

<!-- check: ok -->
```pino
fn add(a int, b int) int {
  return a + b + 1
}

test "math works" {
  assert(add(2, 3) == 5)
}
```

Output:

```text
math_test.pino
  [FAIL] math works  (math_test.pino:6)
         `add(2, 3) == 5` was false.
           add(2, 3) is 6

1 failed (0.01 s)
```

Strings are shown in quotes so whitespace and empty strings are easy to spot. You can also pass an optional message as a second argument:

<!-- check: ok -->
```pino
test "with custom message" {
  val status = "pending"
  assert(status == "ready", "worker must finish first")
}
```

Each side of a comparison is evaluated once, so expressions with side effects behave the same way inside `assert` as outside it.

## Isolation between tests

Each test runs in a fresh program state:

- imported modules and top-level bindings are initialized anew for every test,
- mutations to top-level `var` bindings do not leak from one test to the next, and
- `fn main` is not called.

<!-- check: ok -->
```pino
var count = 0

test "first test changes count" {
  count = 99
  assert(count == 99)
}

test "second test starts fresh" {
  assert(count == 0)
}
```

In a file with `fn main`, top-level `val` bindings are initialized before each test and `fn main` is skipped:

<!-- check: ok -->
```pino
val greeting = "hello"

fn main {
  println("not called by pino test")
}

test "uses top-level val without running main" {
  assert(greeting == "hello")
}
```

If an `assert` fails or a test panics, that test stops immediately and the remaining tests still run. Anything printed with `println` is hidden for passing tests and shown indented under `[FAIL]` for failing tests.

## Testing module internals with `@@Testable`

A module can mark helper functions `@@Testable` instead of `pub`. Files named `*_test.pino` or placed under `test/` can import them; regular code cannot.

## Memory leak warnings and `--strict`

After each passing test finishes, Pino checks whether any heap-allocated objects are still alive (for example, a closure capturing a `var` in a reference cycle). By default, leaks are printed as a `warning:` list under `[PASS]` and the test still passes:

```text
  [PASS] build cache
         warning: 2 objects were still alive when this test ended:
           - captured variable (fn () -> int)
           - closure
```

Pass `--strict` to turn any leak into a test failure (`[FAIL]` with `error:` and exit code `1`):

```bash
pino test --strict
```

## Runner options

| Option | What it does |
| :--- | :--- |
| `--vm` | Run with Pino's built-in runner (the default). |
| `--c` | Compile to a native program with leak checking enabled, then run each test in a fresh process. |
| `--strict` | Treat memory leaks as test failures. |
| `--filter <text>` | Run only tests whose name contains `<text>` (case-insensitive). |
