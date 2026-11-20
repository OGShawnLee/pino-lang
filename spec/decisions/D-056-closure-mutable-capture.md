# D-056: Closures that mutate a captured `var`

Status: accepted
Raised by: 007
Feature: spec/features/functions.md

## Question
Does a lambda capture an enclosing `var` by reference (mutations persist across calls and are visible to the enclosing function) or by value?

## Evidence
- Reference: silent. Closures are named only in the README ("nested closures", `README.md:181`).
- Tests: `ScopingExecutionTests.TestLambdaCaptureScopingExecution` (`ScopingExecutionTests.cs:51`) only reads captured `val` bindings. No test mutates a captured `var`.
- Implementation: not examined; the observed behavior is below.

Snippet:

```pino
fn make_counter() {
  var count = 0
  return fn () {
    count = count + 1
    return count
  }
}
val c = make_counter()
println(c())
println(c())
val d = make_counter()
println(d())
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `1`, `2`, `1` |
| VM | `RUNTIME ERROR: Value is not callable.` |
| C | `1`, `2`, `1` |

## Options
1. Capture by reference per closure instance: each `make_counter()` call owns its own `count`, mutations persist across calls (what tree-walk and C do). Document it and add cases.
2. Capture by value: document and change the engines.
3. Forbid assigning a captured `var` inside a lambda.

## Decision
Option 1, decided by the owner on 2026-11-19: closures capture enclosing `var`s by reference, per closure instance (each `make_counter()` call gets its own variable; mutations persist across calls). Consequence: `spec/features/functions.md` adds `FN-21` documenting mutable capture by reference per closure instance.
