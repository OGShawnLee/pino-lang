# D-079: `to_string()` on values (reference example `err:to_string()`)

Status: open
Raised by: 008
Feature: spec/features/errors.md

## Question
Does a `to_string()` method exist on built-in values such as `int`? The reference's error-handling example calls `err:to_string()` on an `int` error.

Note: built-in value methods are a standard-library matter (tickets 005 to 007); the evidence appeared in the error-handling example, so the decision is opened here.

## Evidence
- Reference: Tactic 1 builds a message with `"Database error occurred with status: " + err:to_string()` where `err` is an `int` (`LANGUAGE_REFERENCE.md:834-836`). No section documents a `to_string` method; `str(value)` is the documented conversion.
- Tests: none uses `to_string`; `str(...)` is used instead (`UnionTests.TestGenericUnionMatchExecution`).
- Implementation: no `to_string` member exists on `int` values in the evaluator; the C backend emits a call to an undefined `int_to_string`.

```pino
val n = 500
println("status: " + n:to_string())
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `RUNTIME ERROR: Invalid member access target.` |
| VM | `RUNTIME ERROR: Value is not callable.` |
| C | TCC error `undefined symbol 'int_to_string'` |

`conformance/errors/or-return.pino` uses string interpolation (`"... $err"`) with the same meaning.

## Options
1. Add `to_string()` to the built-in types (and document it in the stdlib), keeping the reference example.
2. Change the reference example to `str(err)` or interpolation.

## Decision
_Pending: owner decides._
