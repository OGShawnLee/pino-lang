# D-163: Visibility: `pub`, private struct fields and `@@Testable`

Status: accepted
Raised by: 028
Feature: spec/features/modules.md

## Question
What does `pub` apply to, who may access a struct field, and what is the test-visibility marker called?

## Evidence
- `spec/features/modules.md` MOD-5 / MOD-E1 / MOD-E2: top-level declarations are visible to importers only when marked `pub`. MOD-E5 and D-064 describe the marker `@pub_testing`, which cannot be combined with `pub`.
- `.tickets/020-spec-record-parser-decisions/changelog.md` ("Left Open"): "`pub` / `@pub_testing` placement (top-level vs fields)".
- Self-host: struct fields are private by default and `pub` makes them public (`projects/pino-compiler/modules/parser.pino:1077-1089`); external `s:prop` access enforces it (`projects/pino-compiler/modules/Checker.pino:2221-2224`); interface satisfaction requires public fields (`Checker.pino:1709-1717`). Owner's root `main.pino:9-12` writes `pub name string`.
- Legacy: the parser rejects `pub` before a field (`PARSER: Expected Identifier, got Keyword: pub (Pub)`), and the checker has no field privacy. The legacy lexer does not know `@@Testable`.

## Options
1. `pub` on a top-level declaration exports it; struct fields are private by default and module-private (accessible anywhere in the same module, as in Go and Rust); `pub` on a field exports the field; the test marker is renamed `@@Testable`.
2. Fields are public by default and `private` hides them.
3. Fields are private to their struct's methods only.

## Decision
Option 1, decided by the owner on 2026-11-20:
- `pub` on a top-level declaration makes it importable by other modules.
- Struct fields are **private by default**, and privacy is **module-private**: code in the same module may access them. `pub` on a field makes it accessible from other modules.
- The `@pub_testing` marker is renamed **`@@Testable`**, with the same meaning as the previous `@pub_testing` rules in `spec/features/modules.md`: combining `pub` and `@@Testable` is rejected (MOD-E5). What `@@Testable` grants to test code remains D-064.

**Extended on 2026-11-20 (ticket 031, OQ-31), Option 1, decided by the owner:** a struct literal in another module that names a private field is a checker error, as reading or assigning one is. Code outside the module builds such a struct through a `pub` function or static method of the declaring module. Normative rule: STRUCT-E13.
