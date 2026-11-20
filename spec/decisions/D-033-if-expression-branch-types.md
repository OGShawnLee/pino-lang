# D-033: Branch types of `if ... then ... else`

Status: accepted
Raised by: 006
Feature: spec/features/conditionals.md

## Question
Must the two branches of `if cond then a else b` have compatible types, as the arms of a `match` expression must?

## Evidence
- Reference: the expression form is "similar to a ternary operator" and returns "conditional values" (`LANGUAGE_REFERENCE.md:354-361`); it says nothing about branch types.
- Tests: `MatchExpressionTests.TestMatchExpressionCheckerTypeMismatch` requires match arms to agree. No test covers `if` expressions with different branch types.
- Implementation: the checker infers the type of the expression from the consequent only and never compares it with the alternate (`pino-csharp/Checker/Checker.Expression.cs:1067-1068`). A `match` expression with incompatible arms is rejected with "Match branches have incompatible types" (`pino-csharp/Checker/Checker.Expression.cs:1167`).

```pino
val r = if true then 1 else "x"
println(r)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `1` |
| VM | `1` |
| C | TCC error "cannot cast 'struct <anonymous>' to 'extern int'" |

## Options
1. Require compatible branch types with a checker error similar to the match one: consistent; may break programs that mix types.
2. Allow mixed types and give the expression a union type: needs new typing rules.
3. Leave it: document that branch types must agree but do not enforce it.

## Decision
Option 1, decided by the owner on 2026-11-20: the two branches of `if c then a else b` must have compatible types, and a checker error otherwise. An `int` branch and a `float` branch widen to `float` per D-019.
