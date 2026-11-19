# D-015: `int(v)` and `float(v)` return a `Result`

Status: open
Raised by: 005
Feature: spec/features/types.md

## Question
Should the conversion builtins `int(v)` and `float(v)` return a plain `int` and `float` (as the reference says) or a `Result[int, string]` and `Result[float, string]` (as every engine does)?

## Evidence
- Reference: §9 says `int(val)` returns `int` ("`int("123")` returns `123`. `int(3.99)` returns `3`") and `float(val)` returns `float` ("`float(42)` returns `42.0`") (`LANGUAGE_REFERENCE.md:927-937`).
- Tests: no test calls `int()` or `float()`.
- Implementation: `IntFunction` and `FloatFunction` wrap every result in `Result::Success(...)` and report bad input as `Result::Failure("Invalid integer format: ...")` (`Evaluator.BuiltIns.cs:27-47`).

Minimal snippet:

```pino
println(int("123"))
println(int(3.99))
println(float(42))
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `Result::Success(123)`, `Result::Success(3)`, `Result::Success(42)` |
| VM | `Result::Success(123)`, `Result::Success(3)`, `Result::Success(42)` |
| C | `Result::Success(123)`, `Result::Success(3)`, `Result::Success(42)` |

## Options
1. Return `Result` (current behavior) and fix the reference: conversion of a string can fail, so `Result` is honest; callers need `?` or `or { ... }` for every conversion, including the infallible `int(3.99)`.
2. Return plain `int`/`float` for numeric arguments and `Result` only for string arguments: matches the examples for numbers; the return type then depends on the argument type.
3. Return plain `int`/`float` and panic on invalid strings: matches the reference text; a failing conversion cannot be handled by the program.

## Decision
_Pending: owner decides._ No spec rule describes `int(v)` or `float(v)` until then.
