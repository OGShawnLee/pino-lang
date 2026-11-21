# D-174: `break` and `continue` outside a loop

Status: accepted
Raised by: 031 (OQ-27)
Feature: spec/features/loops.md

## Question
What happens to a `break` or `continue` that is not inside a loop?

## Evidence
- Design 031 §12 OQ-27: audit 028 §4 row 29; LOOP-11 and LOOP-12 only describe the behavior inside loops.

## Options
1. Checker error.
2. Run-time no-op.

## Decision
Option 1, decided by the owner on 2026-11-20 (ticket 031, OQ-27): `break` or `continue` outside a loop is a checker error. Normative rule: LOOP-E1.

**Not decided, so the rule does not say (logged for the owner):** whether a `break` or `continue` inside a lambda or nested function that is itself declared inside a loop counts as inside a loop. The cases use a `break` and a `continue` with no enclosing loop at all.
