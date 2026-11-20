# D-061: Null map keys

Status: accepted
Raised by: 007
Feature: spec/features/maps.md

## Question
The reference says map keys cannot be null. How is a null key detected, and what error does it raise?

## Evidence
- Reference: "Keys cannot be null." (`LANGUAGE_REFERENCE.md:320`). Nothing says when or how this is enforced.
- Tests: `DataStructureTests.TestMapEvaluatorRuntimeBehavior` (`DataStructureTests.cs:103-110`) is labeled "Exception on null key" and expects an exception from `m[w:key]`, where `w = Wrapper {}` has an unset `string` field. The exception raised is not about a null key: tree-walk reports `Struct 'Wrapper' has no property or instance method 'key'`, so the test passes without ever reaching a null-key check.
- Implementation: `null` is not usable as a variable value in the snippets below, so a null key cannot be constructed from a plain variable.

Snippets:

```pino
struct Wrapper { key string }
val w = Wrapper {}
val m = map[string, int] { "a": 1 }
val x = m[w:key]
```

```pino
val m = map[string, int] { "a": 1 }
var k = null
println(m[k])
```

| Engine | First snippet | Second snippet |
| :--- | :--- | :--- |
| tree-walk | `RUNTIME ERROR: Struct 'Wrapper' has no property or instance method 'key'.` | `RUNTIME ERROR: Variable 'null' is not declared.` |
| VM | no output, no error | prints `null` |
| C | `RUNTIME ERROR: Key not found in map.` (exit code 1) | TCC error: `'null' undeclared` |

## Options
1. Specify a run-time error for a null key lookup or insertion, and give a way to produce a null value (see the 005 decision on `null` vs. `Option`) so a case can exercise it.
2. Specify it as a static check (key expressions must not be of a nullable type).
3. Drop the sentence from the reference until null values exist.

## Decision
Moot now that `null` is removed ([D-013](D-013-null-primitive-versus-option.md)), decided by the owner on 2026-11-19: no rule; map keys cannot be `null` because `null` does not exist. Consequence: `D-061` is removed from `spec/features/maps.md` open questions with no new rule added.
