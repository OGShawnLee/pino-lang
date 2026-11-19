# D-051: Optional commas in function parameters and arguments

Status: open
Raised by: 007
Feature: spec/features/functions.md

## Question
Commas between function parameters and between call arguments are optional (reference §1). Does that stay true when an argument starts with a unary minus, where `f(x -y)` can mean either two arguments or one subtraction?

## Evidence
- Reference: §1 "Syntax Guidelines" lists "Function/method arguments and parameters" among the lists whose commas are "completely optional" (`LANGUAGE_REFERENCE.md:57`). The README repeats it: "parameters do not require commas" (`README.md:181`).
- Tests: none asserts a comma-less parameter or argument list. Every test call uses commas.
- Implementation: `ConsumeParameters` and `ConsumeArguments` consume a comma only if one is present (`pino-csharp/Parser/Parser.cs:251-275`, `:277-307`). Each argument is parsed with `ParseExpression`, so `x -y` is read as the binary expression `x - y`.
- Related decision: ticket 005 owns the general question "optional commas vs. unary minus" (`val a = 5`, `val b = 2`, `println([a -b])` prints `[3]`, not `[5, -2]`; its decision file is in the D-010 to D-029 range). This file does not duplicate it: the answer to that decision decides what function calls do too. FN-5 and FN-6 only cover argument lists whose elements are not minus-prefixed.

Snippet:

```pino
fn f(a int, b int) int {
  return a - b
}
val x = 5
val y = 2
println(f(x y))
println(f(x -y))
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `TYPE CHECK ERROR: Function 'f' expected 2 arguments, but got 1.` (raised for `f(x -y)`; `f(x y)` alone prints `3`) |
| VM | same type check error |
| C | same type check error (the checker is shared) |

## Options
1. Keep commas optional and document that a leading `-` binds as binary minus: `f(x -y)` is one argument, and `f(x, -y)` is how to pass a negative second argument.
2. Require a comma before an argument that starts with `-`: parser change; the same rule must then hold for vectors, maps and the other lists in §1.
3. Make commas required in function calls only: contradicts the reference.

## Decision
_Pending: owner decides. Follow the 005 decision on commas vs. unary minus._
