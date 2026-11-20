# D-019: Mixing `int` and `float`, and integer division

Status: accepted
Raised by: 005
Feature: spec/features/variables.md

## Question
How do `int` and `float` interact: may a `var` of one type receive the other, what is the type of `1 + 2.5`, what does `5 / 2` return, and is `1 == 1.0` true?

## Evidence
- Reference: §2 says a `var` can be updated "with new values of the same type" (`LANGUAGE_REFERENCE.md:65`). §3 lists `int` and `float` as separate types and says nothing about promotion, division of integers or cross-type comparison (`LANGUAGE_REFERENCE.md:84-90`).
- Tests: `ArithmeticTests.TestBasicIntegerArithmetic` only divides evenly (`10 / 2 - 1`); no test mixes the two types.
- Implementation: the checker accepts `i = 2.5` for an `int` variable and `1 + 2.5`; the tree-walk evaluator promotes to `double` and compares numerically (`Evaluator.Expression.cs:981-995`).

Minimal snippet:

```pino
var i = 1
i = 2.5
println(i)
println(1 + 2.5)
println(5 / 2)
println(1 == 1.0)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `2.5`, `3.5`, `2`, `True` |
| VM | `2.5`, `3.5`, `2`, `True` |
| C | `2`, `3`, `2`, `True` (the C backend keeps `i` an `int` and truncates) |

## Options
1. Strict typing: `i = 2.5` and `1 + 2.5` are checker errors, conversions are explicit (`float(i)`); `5 / 2` is `2` for two `int`s. Matches "the same type" in §2; needs checker work.
2. Implicit widening `int` to `float` in arithmetic and comparison, but never narrowing in assignment: `1 + 2.5` is `3.5`, `i = 2.5` is an error. Common in typed languages.
3. Keep today's behavior and document it: the C backend must then promote like the interpreters.

## Decision
Option 2, decided by the owner on 2026-11-19: `int` widens implicitly to `float` in arithmetic and comparison (`1 + 2.5` is `3.5`; `1 == 1.0` is `true`); `/` always returns a `float` (`5 / 2` is `2.5`); assigning a `float` to an `int` variable is an error. **Still open:** how integer division is spelled remains open and no spec rule is written for it. Consequence: `spec/features/types.md` and `spec/features/variables.md` add widening, float `/` and narrowing-assignment error rules, and legacy behavior is recorded as known failures.
