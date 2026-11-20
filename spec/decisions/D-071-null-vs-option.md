# D-071: `null` versus `Option[T]` for absence of a value

Status: accepted
Raised by: 008
Feature: spec/features/structs.md, spec/features/errors.md, spec/features/unions.md

## Question
Should "no value" be expressed with `null` or with `Option[T]`, and should `null` keep existing at all?

Cross-reference: ticket 005 opens the decision on `null` as a primitive type (see [D-013](D-013-null-primitive-versus-option.md) and `spec/features/types.md`). This decision covers the other half: what the language should use for absence, given that `Option[T]` exists (`spec/features/errors.md`).

## Evidence
- Reference: `null` is listed as a primitive that "represents the absence of value or null reference" (`LANGUAGE_REFERENCE.md:91`); `pop` on an empty vector, `find` without a match and `remove` of a missing map key return `null` (`LANGUAGE_REFERENCE.md:1167`, `:1201`, `:1268`). Section 8 introduces `Option[T]` for the same job (`LANGUAGE_REFERENCE.md:778`, `:808`).
- Tests: `DataStructureTests.TestMapEvaluatorRuntimeBehavior` expects reading an uninitialised struct field used as a map key to throw (`pino-csharp.tests/Execution/DataStructureTests.cs:104-110`).
- Proposal `proposals/001-null-safety-and-default-struct-fields.md` (`Draft`, so evidence only) proposes default values for omitted fields and an explicit nullable type `T?`. `.routing/PROGRESS.md` lists "`null` deprecation vs `Option[T]`" as an open question.
- Implementation: the evaluator has no `null` literal (see the second snippet), but library functions return a null value.

```pino
val e = []int
println(e:pop())
val m = map[string, int] { "a": 1 }
println(m:remove("zz"))
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `null`, `null` |
| VM | `RUNTIME ERROR: Value is not callable.` |
| C | `exit code 101`, stderr `thread 'main' panicked at 'RUNTIME ERROR: Pop on empty vector'` |

```pino
val x = null
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `RUNTIME ERROR: Variable 'null' is not declared.` |
| VM | `null` printed, then `RUNTIME ERROR: Value is not callable.` |
| C | TCC error `'null' undeclared` |

## Options
1. Keep `null` as is and document the library functions that return it; leave `Option[T]` for user code.
2. Deprecate `null` in favour of `Option[T]`: `pop`, `find` and `remove` would return `Option[T]`. This changes the documented stdlib signatures.
3. Add nullable types (`T?`) as proposal 001 describes, keeping `null` for explicitly nullable fields.

## Decision
Resolved together with [D-013](D-013-null-primitive-versus-option.md) (Option 2), decided by the owner on 2026-11-19: `null` is removed from the language; absence is expressed only with `Option[T]`, and `pop`, `find` and `remove` return `Option[T]`. Consequence: `spec/stdlib/vector.md` and `spec/stdlib/map.md` update `pop`, `find` and `remove` to return `Option[T]`.
