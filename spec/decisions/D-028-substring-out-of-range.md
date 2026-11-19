# D-028: `substring` with a range outside the string

Status: open
Raised by: 005
Feature: spec/stdlib/string.md

## Question
What does `s:substring(start, len)` do when `start + len` exceeds the length of `s` (or `start` is negative)?

## Evidence
- Reference: §10 describes `substring(start int, len int)` only for valid ranges (`LANGUAGE_REFERENCE.md:1064-1067`). Proposal 013 (implemented) describes the zero-copy slice but not bounds handling.
- Tests: `StdlibTests.TestStringSubstring` uses the valid range `(6, 5)` (`StdlibTests.cs:141-146`).
- Implementation: the tree-walk interpreter throws a range error (`Evaluator.Expression.cs:739`); the C runtime returns the part that exists.

Minimal snippet:

```pino
println("hello":substring(3, 10))
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `RUNTIME ERROR: Substring range [3, 13] out of range for string of length 5.` |
| VM | `RUNTIME ERROR: Value is not callable.` (the VM does not support string methods) |
| C | `lo` (the range is clamped) |

## Options
1. Runtime error (panic) for an out-of-range slice: safe and explicit; matches the tree-walk interpreter, and the C runtime must check the bounds.
2. Clamp the range: never fails; hides off-by-one errors.
3. Return a `Result`/`Option`: explicit in the type; changes the documented return type `string`.

## Decision
_Pending: owner decides._
