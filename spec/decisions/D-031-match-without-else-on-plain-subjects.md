# D-031: `match` statement without `else` on a non-enum, non-union subject

Status: open
Raised by: 006
Feature: spec/features/match.md

## Question
Is `else` optional for a `match` statement whose subject is not an enum or a union, and what happens at runtime when no arm matches?

## Evidence
- Reference: "The `else` branch is optional, and it is not mandatory to cover all cases exhaustively (unless the value is an enum or union type...)" (`LANGUAGE_REFERENCE.md:423`). The reference defines no behavior for the case where no arm matches.
- Tests: all non-enum statement matches in the tests have an `else` arm (`ControlFlowTests.TestMatchStatementExecution`, `pino-csharp.tests/Execution/ControlFlowTests.cs`). No test covers a plain subject without `else`.
- Proposal 006 (Implemented): a `match` used as an expression "must be verified as exhaustive (either covering all enum/union variants or providing an `else` branch)" (section 4.3), and "if no branch matches and there is no `else` ... throw a runtime panic" (section 4.4). The proposal does not discuss the statement form.
- Implementation: the checker rejects every `match` (statement or expression) over a non-enum, non-union subject without `else` with "Match expression on type '<T>' is not exhaustive. Provide an 'else' fallback branch." (`pino-csharp/Checker/Checker.Expression.cs:1110-1112`). If a match ever got past the checker, the evaluator would return `null` silently, not panic (`pino-csharp/Evaluator/Evaluator.Expression.cs:1187-1226`).

Spec impact: MATCH-E5 is written for the expression form only (backed by the proposal). No rule is written for the statement form.

```pino
var x = 3
match x {
  when 1 { println("one") }
  when 2 { println("two") }
}
println("after")
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `TYPE CHECK ERROR: Match expression on type 'int' is not exhaustive. Provide an 'else' fallback branch.` |
| VM | same checker error |
| C | same checker error (the checker runs before the transpiler) |

## Options
1. Keep the checker; change the reference so `else` is mandatory unless the match is exhaustive over an enum or union: no compiler change; the reference sentence is rewritten.
2. Follow the reference for statements: allow a missing `else`, do nothing when no arm matches, and keep the exhaustiveness requirement for expressions only: needs a checker change (the check must know whether the match is a statement).
3. As option 2, but panic at runtime when no arm matches a statement: stricter, matches the proposal's runtime wording.

## Decision
_Pending: owner decides._
