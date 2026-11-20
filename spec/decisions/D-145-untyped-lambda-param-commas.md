# D-145: Commas required between untyped lambda parameters

Status: accepted
Raised by: 016
Feature: spec/features/functions.md

## Question
How should `fn (n m) -> n + m` (two untyped lambda parameters without a comma) vs `fn (n MyType) -> ...` (one typed parameter) be disambiguated?

## Evidence
- Design: `.tickets/016-re-parser-design/design.md` sections 4 (item 5) and 9 (OQ-5).
- Self-host: `projects/pino-compiler/modules/Parser.pino:925-936` parses `fn (n m)` as one parameter `n` of type `m`.

## Options
1. Require commas between untyped lambda parameters (`fn (n, m) -> n + m`), while typed parameters remain comma-optional.
2. Disambiguate by casing (`D-014`).
3. Make all lambda parameters untyped.

## Decision
Option 1: untyped lambda parameters must be separated by commas (`fn (n, m) -> n + m`), while typed parameters stay comma-optional, so `fn (n m)` means one parameter `n` of type `m`, decided by the owner on 2026-11-19 (ticket 016, OQ-5).