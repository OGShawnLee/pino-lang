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

**Extended on 2026-11-21 (AD-003 / 043, MQ-3, MQ-11), decided by the owner:** interior mutation of a captured `var` (e.g. field assignment, index write, or mutating method call) counts as mutation for `D-056` capture-by-reference (`FN-21`), and capturing a `mut` parameter (or mutably capturing `self` inside a `mut fn`) inside a closure is a checker error (`FN-E17`). Under value semantics with copy-on-write (`D-177`), `D-056` mutable `var` captures remain the sole source of reference cycles in pure Pino code. See `D-177-value-semantics-cow-and-mut.md` and `D-180-mut-parameters-calls-and-closures.md`.
