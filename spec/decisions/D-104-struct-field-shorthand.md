# D-104: Struct literal field shorthand (`Point { x }`)

Status: accepted
Raised by: 013
Feature: spec/features/structs.md

## Question
Should struct instance property shorthand (`Point { x }` meaning `Point { x: x }`) be part of the language?

## Evidence
- Inventory: `.tickets/013-scan-self-host-syntax/inventory.md` item 47 and Question 9.
- Self-host: `projects/pino-compiler/modules/Parser.pino:247-253, 267` supports `Point { x, y }`.
- Legacy: `pino-csharp/Parser/Parser.Statement.cs:848-850` requires `IDENT ":" expr`.

## Options
1. Standardize `Point { x }` as shorthand for `Point { x: x }`.
2. Require explicit `x: expr` in struct literals.

## Decision
Option 1: `Point { x }` is valid shorthand for `Point { x: x }`, decided by the owner on 2026-11-19. Consequence: struct literals accept bare field names whose values come from same-named variables in scope, and legacy parser rejection is recorded as a known failure.
