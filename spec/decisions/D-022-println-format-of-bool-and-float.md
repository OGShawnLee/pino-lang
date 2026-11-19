# D-022: How `println` formats booleans and floats

Status: open
Raised by: 005
Feature: spec/features/types.md

## Question
Should `println(true)` print `true` (the literal's spelling) or `True`, and should `println(1.0)` print `1.0` or `1`?

## Evidence
- Reference: the §3 table writes the boolean literals as `true` and `false` and the float literals as `3.14159` and `-0.005` (`LANGUAGE_REFERENCE.md:86-88`); §9 says `float(42)` returns `42.0` (`LANGUAGE_REFERENCE.md:937`). The reference never shows the printed form of a boolean or of a whole float.
- Tests: `StdlibTests.TestStringContains` asserts the output `"True\nFalse\n"` (`StdlibTests.cs:114-123`), and `TestStringStartsWith` asserts `"True\n"`. No test prints a whole-number float. The spec cases therefore use `True` and `False` where a boolean is printed and avoid printing whole floats.
- Implementation: `println` formats with .NET `ToString`, which yields `True`, `False` and `1` for `1.0` (`Evaluator.BuiltIns.cs`).

Minimal snippet:

```pino
println(true)
println(false)
println(1.0)
println(2.50)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `True`, `False`, `1`, `2.5` |
| VM | `True`, `False`, `1`, `2.5` |
| C | `True`, `False`, `1`, `2.5` |

## Options
1. Keep `True`/`False` and `1` and document them: all engines already agree and the tests assert `True`; the reference examples (`true`, `42.0`) are then corrected.
2. Print `true`/`false` and always keep a fractional part (`1.0`): matches the literals in the reference; needs changes in all three engines and in `StdlibTests`.
3. Keep booleans as they are and print whole floats with `.0`: fixes only the float ambiguity.

## Decision
_Pending: owner decides._
