# D-105: `self` only in methods (`this` removed)

Status: accepted
Raised by: 013
Feature: spec/features/structs.md

## Question
Should struct methods bind both `self` and `this`, or only `self`?

## Evidence
- Inventory: `.tickets/013-scan-self-host-syntax/inventory.md` item 69 and Question 14.
- Self-host: `projects/pino-compiler/modules/Checker.pino:1049-1054` binds `self` only and does not support `this`.
- Legacy: `pino-csharp/` binds both `self` and `this` (`STRUCT-5`).

## Options
1. `self` only: methods bind `self`, and `this` is not a keyword or binding.
2. Keep both `self` and `this`.

## Decision
Option 1: methods bind `self` only, and `this` is not a keyword or binding, decided by the owner on 2026-11-19. Consequence: `STRUCT-5` and `STRUCT-E3` are updated to `self` only, and conformance cases using `this` are rewritten to `self`.
