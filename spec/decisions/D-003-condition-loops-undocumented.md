# D-003: Condition loops are not in the reference

Status: accepted
Raised by: 004
Feature: spec/features/loops.md

## Question
Should `LANGUAGE_REFERENCE.md` §5 document the condition form `for cond { ... }` that tests already rely on?

## Evidence
- Reference: §5 says the `for` keyword offers "4 clear syntactic variants" (infinite, fixed range, iterator, index and value) and shows no `for <condition>` form (`LANGUAGE_REFERENCE.md:366-417`).
- Tests: `ControlFlowTests.TestWhileLoopSimpleCondition`, `TestWhileLoopBooleanVariable` and `TestWhileLoopNested` assert `for i <= 10 { ... }`, `for active { ... }` and nested forms on both engines (`pino-csharp.tests/Execution/ControlFlowTests.While.cs:8-66`).
- Implementation: the parser reads `for <expr> {` as the count form (`pino-csharp/Parser/Parser.Statement.cs:445-448`), and the checker silently switches it to a while loop when the expression type is `bool` (`pino-csharp/Checker/Checker.Statement.cs:337-341`). The same head therefore means "N times" for an `int` and "while true" for a `bool`.

Spec impact: LOOP-13 is written from the tests alone (source rule (b)). Cases `while-condition.pino`, `while-bool-variable.pino` and `nested-loops.pino` pass on all three engines.

## Options
1. Document it in the reference as a fifth variant and keep LOOP-13 as is.
2. Leave the reference as is and keep LOOP-13 as a test-backed rule only.
3. Remove the form from the language and migrate callers to `for { if !cond { break } ... }`: needs a compiler change and a rewrite of the three tests.

## Decision
Option 1, decided by the owner on 2026-11-20 (ticket 031, OQ-25): the condition loop `for cond { ... }` stays in the language and is documented as one of its loop forms; LOOP-13 is unchanged. `LANGUAGE_REFERENCE.md` §5 should list it as a fifth variant; the reference was not edited by ticket 033 (hand-off to the owner).
