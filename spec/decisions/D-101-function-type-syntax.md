# D-101: Function type syntax (`fn (T) -> R`, optional `()`, omitted return = `void`)

Status: accepted
Raised by: 013
Feature: spec/features/functions.md

## Question
How should function-type return types and zero-argument or omitted-return function types be written?

## Evidence
- Inventory: `.tickets/013-scan-self-host-syntax/inventory.md` item 30 and Question 2.
- Self-host: `projects/pino-compiler/modules/Parser.pino:808-841, 851-854` makes `()` optional when empty, requires `->` before the return type (`Parser.pino:835`), and defaults to `Type::Void` when `->` is omitted (`Parser.pino:833`).
- Legacy: `pino-csharp/Parser/Parser.Type.cs:33-76` requires `()`, takes no `->`, and defaults omitted return to `any`.

## Options
1. `fn (int, int) -> int`: parentheses are optional when there are no parameters, and an omitted return type means `void`, so `fn` alone is `fn () -> void`.
2. `fn (int, int) int`: required `()`, omitted return = `any`.

## Decision
Option 1: function types are written `fn (int, int) -> int`, parentheses are optional when there are no parameters, and an omitted return type means `void` (so `fn` alone is `fn () -> void`), decided by the owner on 2026-11-19. Consequence: function types in `spec/` and `conformance/` use `->` before the return type, and `fn` / `fn (T)` denote `void`-returning function types.
