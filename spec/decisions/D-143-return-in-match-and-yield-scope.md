# D-143: `return` inside a `match` expression and `yield` scope

Status: accepted
Raised by: 016
Feature: spec/features/match.md, spec/features/errors.md

## Question
May an arm of a `match` expression execute `return` to exit the enclosing function, and how does `ERR-E5` interact with `MATCH-8`?

## Evidence
- Design: `.tickets/016-re-parser-design/design.md` sections 2, 9 (OQ-3), and Owner answers (2026-11-20).
- Spec: `ERR-13` allows `return` inside an `or` recovery block without `yield`. `MATCH-8` allows `yield` in `match` expression block arms, which contradicted `ERR-E5`'s wording ("`'yield'` is only allowed inside a recovery block").

## Options
1. Allow `return` in `match` expression block arms (exiting the enclosing function without needing `yield`), reject `when X -> return` as a parse error, and reword `ERR-E5` to allow `yield` in both `or` blocks and `match` expression block arms.
2. Forbid `return` inside `match` expressions.

## Decision
Option 1: a block arm of a `match` expression may execute `return` to exit the enclosing function without producing a value for the `match` (needing no `yield`), `->` arms take only an expression so `when X -> return` is a parse error, and `ERR-E5` is reworded to allow `yield` in both `or` recovery blocks and `match` expression block arms, decided by the owner on 2026-11-20 (ticket 016).