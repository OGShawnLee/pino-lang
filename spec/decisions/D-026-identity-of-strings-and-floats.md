# D-026: `===` on strings and floats

Status: accepted
Raised by: 005
Feature: spec/features/equality.md

## Question
What does `a === b` mean for two strings and two floats: value identity like the other scalars, or comparison of the underlying data pointers?

## Evidence
- Reference: §3 says `===` compares "physical memory addresses (pointers)" and that primitives and enums "evaluate to `true` if their values are identical" (`LANGUAGE_REFERENCE.md:228-252`). A `string` is described as a `{ data, len }` view (`LANGUAGE_REFERENCE.md:90`) that is neither a plain scalar nor a heap object, and `float` is not named in the identity list.
- Tests: none asserts `===` on strings or floats.
- Implementation: the tree-walk interpreter returns `true` for equal strings and equal floats; the C backend cannot compile the comparison of two string structs.

Minimal snippet:

```pino
println("a" === "a")
println(1.5 === 1.5)
val s = "abc"
val t = "abc"
println(s === t)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `True`, `True`, `True` |
| VM | `RUNTIME ERROR: Value is not callable.` |
| C | `TCC compilation failed: error: operation on a struct` |

## Options
1. Treat strings and floats like the other scalars: `===` compares the values, so equal strings are identical; simplest, and the C backend compares `data` and `len`.
2. Compare string identity by `data` pointer and `len` (two literals share storage only when the compiler interns them): faithful to the pointer wording; results depend on interning.
3. Reject `===` on strings and floats with a checker error: removes the ambiguity.

## Decision
Option 1, decided by the owner on 2026-11-19: `===` and `!==` on strings and floats compare values, like the other scalars. Consequence: `EQ-10` in `spec/features/equality.md` applies to all primitive values including `string` and `float`, and VM/C failures are recorded as known failures.
