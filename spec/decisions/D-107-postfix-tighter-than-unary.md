# D-107: Postfix `[i]`, `:`, and calls bind tighter than unary `-` and `not`

Status: accepted
Raised by: 013
Feature: spec/features/types.md, spec/features/vectors.md, spec/features/structs.md

## Question
Do postfix `[i]`, `:` member access, and calls bind tighter than unary `-` and `not`, or looser?

## Evidence
- Inventory: `.tickets/013-scan-self-host-syntax/inventory.md` item 63 and Question 17.
- Self-host: `projects/pino-compiler/modules/Parser.pino:384-389` calls `parse_factor` (`386`), binding unary `-`/`not` tighter than `[i]` and `:`/`::`.
- Legacy: `pino-csharp/Parser/Parser.Expression.cs:227-231` gives unary operators precedence 8, binding looser than postfix `[i]` and `:`.

## Options
1. Postfix `[i]`, `:` member access, and calls bind tighter than unary `-` and `not`, so `-arr[0]` is `-(arr[0])`.
2. Unary `-` and `not` bind tighter than postfix `[i]` and `:`.

## Decision
Option 1: postfix `[i]`, `:` member access, and calls bind tighter than unary `-` and `not`, so `-arr[0]` is `-(arr[0])`, decided by the owner on 2026-11-19. Consequence: `spec/grammar.ebnf` and `spec/features/types.md` place postfix expressions inside `unary_expr`.
