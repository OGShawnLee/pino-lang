# D-100: Canonical arrow token (`->` vs `=>`) and map entry separator (`->` vs `:`)

Status: accepted
Raised by: 013
Feature: spec/features/functions.md, spec/features/maps.md, spec/features/match.md, spec/features/lexical.md

## Question
Should `->` or `=>` be the canonical arrow token for lambdas, match arms, function types, and map entries?

## Evidence
- Inventory: `.tickets/013-scan-self-host-syntax/inventory.md` items 27, 30, 42, 57 and Questions 1 & 2.
- Self-host: uses `->` in lambdas (`projects/pino-compiler/modules/Parser.pino:420`), map entries (`Parser.pino:310`), and function types (`Parser.pino:835`); `Lexer.pino:10-34` does not lex `=>`.
- Legacy: `pino-csharp/` uses `=>` for lambdas (`Parser.Expression.cs:425`) and match arms (`Parser.Statement.cs:581`), and `:` for map entries (`Parser.Expression.cs:567`).

## Options
1. `->` everywhere: lambdas `fn (x) -> expr`, map entries `k -> v`, function-type returns, `when ... -> expr` and `else -> expr` match arms; `=>` is removed.
2. `=>` for lambdas/match/`fn`, `:` for maps (legacy spelling).
3. Split by construct (`->` for lambdas/maps and `=>` for match/declarations).

## Decision
Option 1 (`->` everywhere): `->` is the only arrow — used for lambdas (`fn (x) -> expr`), map entries (`k -> v`), function-type returns, and `when ... -> expr` / `else -> expr` match arms — while `=>` is no longer a token of the language and map entries no longer use `k: v`, decided by the owner on 2026-11-19. Consequence: all `=>` and `k: v` map-entry syntax in `spec/` and `conformance/` is rewritten to `->`, and legacy failures on `->` are recorded as known failures.
