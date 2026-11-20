# D-108: Trailing commas disallowed in all comma-separated lists

Status: accepted
Raised by: 013
Feature: spec/features/lexical.md

## Question
Is a trailing comma after the last element of a comma-separated list allowed or an error?

## Evidence
- Inventory: `.tickets/013-scan-self-host-syntax/inventory.md` item 76 and Question 10.
- Self-host: `projects/pino-compiler/modules/Parser.pino:123-129, 193-195, 233-235, 297-303, 351-357, 541-543, 754-756, 908-910, 990-992, 1113-1115, 1235-1237` explicitly rejects trailing and consecutive commas.
- Legacy: `pino-csharp/Parser/` loops accept trailing commas (e.g. `Parser.cs:271`, `Parser.Expression.cs:475`, `Parser.Statement.cs:855`).

## Options
1. Trailing commas are an error in every comma-separated list (`[1, 2,]`, `f(a,)`, `map[K, V] { 1 -> 2, }`, parameter lists, struct literals, union variants, generic lists).
2. Allow trailing commas.

## Decision
Option 1: a comma after the last element of any comma-separated list is an error (`[1, 2,]`, `f(a,)`, `map[K, V] { 1 -> 2, }`, parameter lists, struct literals, union variants, generic lists), decided by the owner on 2026-11-19. Consequence: `spec/features/lexical.md` adds a rule and error rule forbidding trailing commas, and legacy acceptance is recorded as a known failure.
