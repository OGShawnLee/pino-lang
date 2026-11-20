# D-144: Line break before `(` ends the expression

Status: accepted
Raised by: 016
Feature: spec/features/functions.md

## Question
Should a line break before `(` end the preceding expression (like `[` in `D-058`), or continue as a function call?

## Evidence
- Design: `.tickets/016-re-parser-design/design.md` sections 3.3 and 9 (OQ-4).
- Companion rule: `spec/decisions/D-058-bracket-starts-line-after-expression.md` ends the expression when a line break precedes `[`.

## Options
1. End the expression on a line break before `(` (consistent with `D-058`; a call's `(` must be on the callee's line).
2. Only `[` is newline-sensitive (`(` continues as a call across newlines).

## Decision
Option 1: a line break before `(` ends the expression (the companion of `D-058` for `[`), so a call's `(` must be on the same line as its callee, decided by the owner on 2026-11-19 (ticket 016, OQ-4).