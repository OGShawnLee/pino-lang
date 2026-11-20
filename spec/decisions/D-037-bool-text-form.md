# D-037: Text form of `bool` in `println` and `str`

Status: accepted
Raised by: 006
Feature: spec/stdlib/builtins.md

## Question
Must `println(true)` and `str(true)` print `true` or `True`?

## Evidence
- Reference: `bool` literals are written `true` and `false` (`LANGUAGE_REFERENCE.md:86`); the built-in section says `println` prints expressions and `str` returns "the formatted textual representation of any object" without showing a bool (`LANGUAGE_REFERENCE.md:915-920`, `LANGUAGE_REFERENCE.md:973-977`).
- Tests: `StdlibTests` asserts `True` and `False` for printed bool results (for example `pino-csharp.tests/StdlibTests.cs:120`).
- Implementation: all engines print the .NET-style capitalized form.

```pino
println(true)
println(false)
println(str(true))
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `True` / `False` / `True` |
| VM | `True` / `False` / `True` |
| C | `True` / `False` / `True` |

Spec impact: no conformance case in this ticket prints a `bool` directly; cases branch with `if` instead.

## Options
1. Standardize on `true` / `false` (the source spelling) and update the tests and the engines.
2. Standardize on `True` / `False` and document it in the reference.

## Decision
Resolved together with [D-022](D-022-println-format-of-bool-and-float.md) (Option 1 here / Option 2 in D-022), decided by the owner on 2026-11-19: `println` and `str` print booleans as `true`/`false`, and whole floats keep a fractional part (`1.0`, `42.0`). Consequence: `spec/stdlib/builtins.md` specifies `true`/`false` output, `.out` files are updated, and legacy `True`/`False` output is recorded as known failures.
