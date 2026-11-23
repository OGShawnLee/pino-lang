# D-187: Semicolons are ignored, with a hint

Status: accepted
Raised by: 071
Feature: spec/features/lexical.md

## Question
What does Pino do when a program contains a `;`?

## Evidence
- Before this decision the `-re` lexer rejected `;` with `error[PN1001]: Unexpected character ';'.`; the legacy engines also reject it (`PARSER: Expected expression, got Illegal`).
- The parser already accepts two statements on one line without a separator (`val a = 1 val b = 2`), so dropping the token is enough.
- Owner, 2026-11-23: Pino was made for someone who once failed a university course over a missing semicolon in C++.

## Options
1. Keep rejecting `;`.
2. Ignore `;` silently.
3. Ignore `;` and show a hint, with a longer explanation one step away in `pino-re explain`.

## Decision
Option 3, decided by the owner on 2026-11-23: a semicolon must never be the reason a Pino program fails.
- A `;` outside a string, rune or comment produces no token; lexing continues as if it were a space (`LEX-23`).
- Each `;` reports one hint, `PN1018` (`SemicolonIgnored`), with the message `Pino doesn't use semicolons, so this one was ignored.` and the help `you can delete it; run 'pino-re explain PN1018' to see why`.
- A hint is not an error or a warning; `pino-re check` still exits `0`.
- The story behind the decision stays out of the message and lives in `pino-re explain PN1018`.
- `pino-re explain <code>` prints a longer explanation for a code (only `PN1018` has one so far).
- The `PN3411` return messages are reworded in the same ticket to `'<fn>' should return '<declared>', but this returns '<actual>'.`, since the old text said the returned type twice.
