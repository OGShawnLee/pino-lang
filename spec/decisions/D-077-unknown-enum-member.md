# D-077: Referencing an enum member that does not exist

Status: accepted
Raised by: 008
Feature: spec/features/enums.md

## Question
Should `Direction::Up`, where `Up` is not a member of `Direction`, be rejected at type-check time on every engine?

## Evidence
- Reference: §7 "Enums" says an enum declares "a closed set of strongly-typed named constants" (`LANGUAGE_REFERENCE.md:593-595`). It does not say what a reference to a missing member does.
- Tests: none. The checker has a diagnostic for a missing member only inside `match` patterns (`pino-csharp/Checker/Checker.Statement.cs:527`).
- Implementation: the evaluator fails at run time (`pino-csharp/Evaluator/Evaluator.Expression.cs:915`); the VM runs the program; the C backend fails in TCC.

```pino
enum Direction { North, South }
val d = Direction::Up
println("reached")
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `RUNTIME ERROR: Enum 'Direction' has no member 'Up'.` |
| VM | `reached` (the bad reference is not noticed) |
| C | TCC error `'Direction_Up' undeclared` |

## Options
1. Reject it in the type checker with one message, so all engines behave alike (checker change).
2. Leave it as a run-time error and specify only that the program fails.

## Decision
Option 1, decided by the owner on 2026-11-20 (ticket 031, OQ-28): naming an enum member that the enum does not declare (`Direction::Up`) is a checker error. Normative rule: ENUM-E2.
