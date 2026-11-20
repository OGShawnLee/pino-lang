# D-102: Set type/literal syntax and brace-less empty sets and maps

Status: accepted
Raised by: 013
Feature: spec/features/sets.md, spec/features/maps.md

## Question
How are set types and literals written, and are empty sets and maps written without braces?

## Evidence
- Inventory: `.tickets/013-scan-self-host-syntax/inventory.md` items 43, 44, 45 and Questions 6 & 7.
- Self-host: `projects/pino-compiler/modules/Parser.pino:867-874` parses `set[T]`, `Parser.pino:328-375` parses `set[int] { 1, 2, 3 }` and brace-less `set[T]` (`372-374`), and `Parser.pino:284, 323-325` parses brace-less `map[K, V]`; bare `{ 1, 2, 3 }` is rejected at `Parser.pino:395-397`.
- Legacy: `pino-csharp/` has no `set` type or literal and requires `{}` for empty `map[K, V]` (`Parser.Expression.cs:550-552`).

## Options
1. `set[T]` type, `set[int] { 1, 2, 3 }` literal, and brace-less empty `set[int]` and `map[K, V]` (no bare `{ 1, 2, 3 }`).
2. Bare `{ 1, 2, 3 }` for set literals, with `set[int]` for empty declarations.

## Decision
Option 1: the set type is `set[T]`, a non-empty set literal is `set[int] { 1, 2, 3 }`, an empty set is `set[int]` with no braces (a bare `{ 1, 2, 3 }` is not a set literal), and empty maps are likewise `map[K, V]` with no braces, decided by the owner on 2026-11-19. Consequence: `spec/features/sets.md` is added, empty map cases drop `{}`, and legacy failures on `set[T]` and brace-less `map[K, V]` are recorded as known failures.
