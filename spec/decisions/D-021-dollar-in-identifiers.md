# D-021: `$` as an identifier character

Status: accepted
Raised by: 005
Feature: spec/features/lexical.md

## Question
Is `$` a legal character in identifiers, and if so how does `"$x$y"` interpolate?

## Evidence
- Reference: §1 names the naming convention but not the identifier alphabet (`LANGUAGE_REFERENCE.md:50`); §3 uses `$name` only for interpolation (`LANGUAGE_REFERENCE.md:128-140`).
- Tests: none. `LexerTests.TestStringInterpolation` interpolates `$name` and `$room` separated by text only (`LexerTests.cs:118-150`).
- Implementation: `IdentifierRegex` is `^[a-zA-Z_$][a-zA-Z0-9_$]*$` (`Lexer.cs:84`), and `IsIdentifierChar` accepts `$` (`Lexer.cs:319-321`), so the simple interpolation `$x$y` reads one identifier `x$y`.

Minimal snippet:

```pino
val x = 10
val y = 20
println("$x$y")
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `RUNTIME ERROR: Variable 'x$y' is not declared.` |
| VM | an empty line |
| C | `TCC compilation failed: error: ',' expected (got "$")` |

## Options
1. Remove `$` from identifiers: `"$x$y"` is `1020`; identifiers become letters, digits and `_`; matches the C backend's need for valid C names.
2. Keep `$` in identifiers and document it: `"$x$y"` stays an error; `$(x)$(y)` is the workaround.
3. Stop a simple interpolation name at a `$`: keeps `$` legal in declarations but not inside a string.

## Decision
Option 1, decided by the owner on 2026-11-19: `$` is not an identifier character (identifiers use ASCII letters, digits and `_` only), so `"$x$y"` interpolates `x` then `y`.
