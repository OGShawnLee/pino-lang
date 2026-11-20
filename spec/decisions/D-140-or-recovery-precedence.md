# D-140: Precedence of `expr or { ... }` recovery blocks

Status: accepted
Raised by: 016
Feature: spec/features/errors.md

## Question
Does `expr or { ... }` bind at logical-`or` precedence (level 2) or as a postfix operator (level 9, like `?`)?

## Evidence
- Design: `.tickets/016-re-parser-design/design.md` sections 2 and 9 (OQ-1).
- Spec: `spec/features/errors.md` (`ERR-10`–`ERR-14`) and `spec/grammar.ebnf` section `err` previously gave no precedence for `or { ... }`.
- Legacy: `pino-csharp/Parser/Parser.Expression.cs:204` handles `or { ... }` at logical-`or` precedence (level 2), so `a + b or { yield 0 }` parses as `(a + b) or { yield 0 }`.

## Options
1. Level 2 (same as logical `or`, matching legacy): `a + b or { yield 0 }` wraps the entire left-hand side.
2. Postfix / Level 9 (same as `?`): `a + b or { yield 0 }` recovers only `b`, matching `a + b?`.

## Decision
Option 2: `or { ... }` is a postfix operator binding at the same precedence level as `?`, so `a + b or { yield 0 }` recovers only `b`, decided by the owner on 2026-11-19 (ticket 016, OQ-1).