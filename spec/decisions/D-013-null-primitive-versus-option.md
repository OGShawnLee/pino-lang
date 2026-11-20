# D-013: `null` as a primitive type versus `Option[T]`

Status: accepted
Raised by: 005
Feature: spec/features/types.md

## Question
Is `null` a primitive value of the language (as the primitive-types table says), or is absence modeled only with `Option[T]` (`Option::None`)? Ticket 008 owns the `Option` side of this decision.

## Evidence
- Reference: the §3 primitive-types table lists `null` ("Represents the absence of value or null reference", `LANGUAGE_REFERENCE.md:91`). Builtins return `null` (`sleep`, `clear`) and `pop`, `find` and map `remove` return `null` when nothing is found (`LANGUAGE_REFERENCE.md:918`, `LANGUAGE_REFERENCE.md:1167`, `LANGUAGE_REFERENCE.md:1201`, `LANGUAGE_REFERENCE.md:1268`). The same reference documents `Option[T]` with `Option::Some` and `Option::None` for optional values (`LANGUAGE_REFERENCE.md:670-673`). `type(val)` lists `"null"` as a possible name (`LANGUAGE_REFERENCE.md:962`).
- Tests: none uses a `null` literal; `StdlibTests.TestSleepFunction` ignores the result of `sleep` (`StdlibTests.cs:68-75`).
- Implementation: the lexer has no `null` token (`Lexer.cs:11-38`), so `null` is read as an ordinary identifier that is not declared.

Minimal snippet:

```pino
val x = null
println(x)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `RUNTIME ERROR: Variable 'null' is not declared.` |
| VM | `null` (the VM prints its own null for the undeclared identifier) |
| C | `TCC compilation failed: error: 'null' undeclared` |

## Options
1. Keep `null` as a primitive and implement the literal in the lexer, checker and all engines: matches the reference table; adds a second way (besides `Option[T]`) to model absence.
2. Remove `null` from the language and the reference: absence is only `Option[T]`; builtins such as `pop` and `find` return `Option[T]`. Cleaner, but a breaking change for the builtin signatures.
3. Keep `null` only as the "no value" result of functions without a return value, not as a literal: documents what the engines already do.

## Decision
Option 2, decided by the owner on 2026-11-19 (resolving [D-071](D-071-null-vs-option.md) as a duplicate): `null` is removed from the language; absence is expressed only with `Option[T]`, and `pop`, `find` and `remove` return `Option[T]`. Consequence: `null` is removed from `spec/features/` and `spec/stdlib/`, `pop`/`find`/`remove` return `Option[T]`, and legacy `null` behavior is recorded as known failures.
