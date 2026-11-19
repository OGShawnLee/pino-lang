# D-032: `if` conditions that are not `bool`

Status: open
Raised by: 006
Feature: spec/features/conditionals.md

## Question
Must the condition of an `if` statement or `if ... then ... else` expression have type `bool`?

## Evidence
- Reference: conditionals use "the condition" without a type and show only comparisons (`LANGUAGE_REFERENCE.md:338-363`). Nothing says what a non-bool condition does.
- Tests: none. `ControlFlowTests.TestVMControlFlow` uses `true` and `false` only.
- Implementation: the checker does not constrain the condition type (`pino-csharp/Checker/Checker.Statement.cs:228-232`), and the evaluator treats every value except `null` and `false` as true (`IsTruthy`, `pino-csharp/Evaluator/Evaluator.Expression.cs:1157-1161`).

```pino
if 0 {
  println("zero is truthy")
} else {
  println("zero is falsy")
}
if "" {
  println("empty string is truthy")
} else {
  println("empty string is falsy")
}
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `zero is truthy` / `empty string is truthy` |
| VM | `zero is truthy` / `empty string is truthy` |
| C | TCC error "operation on a struct" (the C backend cannot compile it) |

Spec impact: IF-1 says "when `cond` is true"; it makes no claim about non-bool values.

## Options
1. Require `bool`: add a checker error such as "if condition must be bool"; existing programs that rely on truthiness break.
2. Keep truthiness (everything except `null` and `false` is true) and document it, then fix the C backend to match.
3. Leave undefined: keep the current behavior but say in the reference that only `bool` conditions are supported.

## Decision
_Pending: owner decides._
