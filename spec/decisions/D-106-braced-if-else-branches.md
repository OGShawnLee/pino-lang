# D-106: Braced `if`/`else` branches (`IF-E1` stands)

Status: accepted
Raised by: 013
Feature: spec/features/conditionals.md

## Question
Should `if`/`else` statement branches always require braces (`IF-E1`), or allow unbraced single-statement branches?

## Evidence
- Inventory: `.tickets/013-scan-self-host-syntax/inventory.md` item 49 and Question 15.
- Self-host: `projects/pino-compiler/modules/Parser.pino:681-693` (`684, 688`) accepts any `parse_stmt` (allowing unbraced branches).
- Legacy: `pino-csharp/Parser/Parser.Statement.cs:485-519` (`491`) requires `{ ... }` per `IF-E1`.

## Options
1. Always require braces on `if`/`else` branches (`IF-E1` stands).
2. Allow unbraced single-statement branches.

## Decision
Option 1: `if`/`else` branches always require braces (`IF-E1` stands), decided by the owner on 2026-11-19. Consequence: unbraced `if`/`else` branches remain a parse error.
