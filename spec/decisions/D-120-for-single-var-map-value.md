# D-120: Single-variable `for x in map` binds the value

Status: accepted
Raised by: 013
Feature: spec/features/loops.md

## Question
In a single-variable map loop `for x in m { ... }`, does `x` bind the entry's key or its value?

## Evidence
- Inventory: `.tickets/013-scan-self-host-syntax/inventory.md` item 54 and Question 16.
- Self-host: `projects/pino-compiler/modules/Checker.pino:3239-3244` binds `x` over a `map` to the **value**.
- Legacy & Spec: `pino-csharp/Parser/Parser.Statement.cs:464-482` and `LOOP-9` (`spec/features/loops.md:18`) bound `x` to the **key**.

## Options
1. Bind the **value** (`for x in m`), matching the self-host; the two-variable form `for k, v in m` remains unchanged.
2. Bind the **key** (`for x in m`), matching legacy and `LOOP-9`.

## Decision
Option 1, decided by the owner on 2026-11-19: in `for x in m`, the single variable `x` binds the **value** (as the self-host does), replacing `LOOP-9`'s "binds the key"; the two-variable form `for k, v in m` is unchanged (`LOOP-10`). Consequence: `LOOP-9` in `spec/features/loops.md` is reworded to bind the value, `conformance/loops/in-map-keys.pino` is updated, and legacy key-binding is recorded as a known failure.
