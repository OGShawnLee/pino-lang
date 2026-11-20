# D-103: Union variant payloads (named fields and positional types)

Status: accepted
Raised by: 013
Feature: spec/features/unions.md

## Question
Do union variants have named payload fields (`Int(n int)`) or positional payload types (`Int(int)`)?

## Evidence
- Inventory: `.tickets/013-scan-self-host-syntax/inventory.md` item 17 and Question 12.
- Self-host: `projects/pino-compiler/modules/Parser.pino:977-1075` (`998-999`) parses named payload fields `name type`.
- Legacy: `pino-csharp/Parser/Parser.Statement.cs:590-654` (`635`) parses positional payload types `Variant(Type)`.

## Options
1. Named fields only (`Int(n int)`).
2. Positional types only (`Int(int)`).
3. Both forms exist: named fields `Int(n int)` and positional `Int(int)`, with positional payloads allowed only when every variant of that union is positional (mixing the two in one union is a compile error).

## Decision
Option 3: both named fields (`Int(n int)`) and positional (`Int(int)`) forms exist, and positional payloads are allowed only when every variant of that union is positional — mixing the two in one union is a compile error, decided by the owner on 2026-11-19. Consequence: both forms are valid in `spec/features/unions.md` and mixing them in one union declaration is a compile error.
