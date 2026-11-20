# D-050: Function return-type inference

Status: accepted
Raised by: 007
Feature: spec/features/functions.md

## Question
When a function declaration omits its return type, is the return type inferred, and from what (the `return` statements of the body)?

## Evidence
- Reference: no section describes functions. §3 and §8 only show declarations whose return type is written out (`fn divide(a int, b int) Result[int, string]`, `LANGUAGE_REFERENCE.md:788`) or functions without a return value (`fn print_coords()`, `LANGUAGE_REFERENCE.md:512`). The README's function section (not a spec source) shows `fn multiplier(factor int) { return fn (val int) { return val * factor } }` with no return type (`README.md:190-195`).
- Tests: `ParserTests.TestParseFunctionDeclaration` (`ParserTests.cs:24`) parses `fn add(a int, b int) { return a + b }` without a return type and only asserts the parse tree. No test asserts the inferred type or its use at a call site.
- Implementation: the checker collects the type of every `return` in the body; the first one wins unless a later one is a wider compatible type, incompatible ones fail with `Function '<name>' has conflicting return types`, and a body without `return` falls back to `any` (`pino-csharp/Checker/Checker.Statement.cs:121-140`). `InferFunctionReturnType` serves call sites (`pino-csharp/Checker/Checker.cs:374`, `:433`).

Snippet:

```pino
fn add(a int, b int) {
  return a + b
}
println(add(2, 3))
fn multiplier(factor int) {
  return fn (v int) {
    return v * factor
  }
}
println(multiplier(2)(5))
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `5` then `10` |
| VM | `5`, then `RUNTIME ERROR: Value is not callable.` |
| C | `5` then `10` |

## Options
1. Document inference: the return type of an unannotated function is the type of its `return` expressions (`any` when it has none), with the conflict error. Needs rules, cases and an owner decision on what `any` means to callers.
2. Require the return type whenever the function returns a value: unannotated functions return nothing. Needs a checker change and updates to the README.
3. Leave it unspecified and keep annotating return types in the documentation.

## Decision
Option 1, refined and decided by the owner on 2026-11-20. Return-type inference for named functions without a declared return type:
- The return type is inferred from the function's `return` statements.
- A function with no value-returning `return` is `void`.
- Inference **does not depend on the order** of the base case and the recursive call: `if n > 0 { return n * f(n - 1) } return 1` infers `int`, as does the early-base-case form, and mutual recursion works in any declaration order.
- Recursion with no non-recursive `return` at all is a checker error suggesting an explicit return type.
- If any `return` yields `float` and the others `int`, the type is `float`.
- `return`s of different struct types are an error suggesting a declared interface return type.
- A non-`void` function must return on every path (definite return); a checker error otherwise.
- A bare `return` is allowed only in `void` functions.
