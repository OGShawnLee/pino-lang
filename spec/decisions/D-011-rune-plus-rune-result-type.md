# D-011: `rune + rune` produces a string

Status: accepted
Raised by: 005
Feature: spec/features/runes.md

## Question
Should the operators on runes give three different result types (`rune + rune` is a `string`, `rune + int` is a `rune`, and `rune - rune` is an `int`)?

## Evidence
- Reference: §3 "Ergonomic Rune Arithmetic" documents all three: adding two runes "is automatically promoted to a `string`" (`LANGUAGE_REFERENCE.md:102-106`), adding an integer yields a shifted rune (`LANGUAGE_REFERENCE.md:107-114`), and subtracting two runes yields an `int` distance (`LANGUAGE_REFERENCE.md:115-119`).
- Tests: `ArithmeticTests.TestRuneArithmeticShift` asserts `'a' + 'b' == "ab"`, `'a' + 1 == 'b'` and `'b' - 'a' == 1` on tree-walk and VM (`ArithmeticTests.cs:26-47`).
- Implementation: the checker types `rune + rune` as `string` and `rune + int` as `rune` (`Checker.Expression.cs:1036-1052`); the evaluator mirrors it (`Evaluator.Expression.cs:997-1006`). The C backend crashes on the concatenation.

Minimal snippet:

```pino
println('a' + 'b')
println('A' + 32)
println('c' - 'a')
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `ab`, `a`, `2` |
| VM | `ab`, `a`, `2` |
| C | `Stack overflow.` (the process crashes on `'a' + 'b'`); run on their own, `'A' + 32` prints `a` and `'c' - 'a'` prints `2` |

## Options
1. Keep all three behaviors and document them: the reference and the tests agree, and text building with runes stays terse; the C backend must be fixed. The spec then gains a rule for `rune + rune`.
2. Make `rune + rune` an error and require `str(a) + str(b)`: one result type per operator, at the cost of the documented ergonomics.
3. Keep `rune + rune` but restrict it to concatenation with a string operand (`"x" + 'a'`): removes the mixed result types for two runes only.

## Decision
Option 2, decided by the owner on 2026-11-19: `rune + int` gives a `rune`; `rune - rune` gives an `int`; `rune + rune` is a compile error whose message suggests `"$a$b"` or `str(a) + str(b)`. Consequence: `spec/features/runes.md` adds `RUNE-E2` forbidding `rune + rune`, and legacy `rune + rune` concatenation is recorded as a known failure.
