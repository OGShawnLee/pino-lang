# D-038: Return type of `int(...)` and `float(...)`

Status: open
Raised by: 006
Feature: spec/stdlib/builtins.md

## Question
Do `int(val)` and `float(val)` return the plain number, as the reference says, or a `Result`?

## Evidence
- Reference: `int` returns `int` and `float` returns `float`; for example `int("123")` returns `123` (`LANGUAGE_REFERENCE.md:927-938`).
- Tests: not asserted in the files read for this ticket. Feature `features/types.md` (ticket 005) owns the conversion semantics.
- Implementation: `IntFunction` and `FloatFunction` return `Result::Success(...)` or `Result::Failure(...)` values (`pino-csharp/Evaluator/Evaluator.BuiltIns.cs:27-69`). The checker types `read_file` as `Result[...]` explicitly (`pino-csharp/Checker/Checker.cs:64`).

```pino
println(int("123"), int(3.99), float("3.14"), float(42))
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `Result::Success(123) Result::Success(3) Result::Success(3.14) Result::Success(42)` |
| VM | same |
| C | not run |

Spec impact: `spec/stdlib/builtins.md` lists the reference's `Returns` for these two entries and writes no rule for them; the owner of `features/types.md` may open an overlapping decision.

## Options
1. Change the reference: `int` and `float` return `Result[int, string]` and `Result[float, string]`.
2. Change the implementation: return the plain number and panic (or return a zero value) on invalid input.
3. Keep both, with `int(x)` returning a plain number for numeric inputs and a `Result` only for strings: needs a new typing rule.

## Decision
_Pending: owner decides._
