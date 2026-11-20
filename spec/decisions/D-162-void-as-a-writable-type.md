# D-162: `void` as a writable type

Status: accepted
Raised by: 028
Feature: spec/features/types.md

## Question
Where may the type `void` be written?

## Evidence
- `.tickets/014-spec-record-syntax-decisions/changelog.md` and `.tickets/020-spec-record-parser-decisions/changelog.md` list `void` as a writable type among the items left open.
- `spec/features/functions.md` FN-20: an omitted function-type return means `void`; `println` returns `void` (BUILTIN-4).
- Audit 028 section 4, row 27: the self-host parses `void` (`projects/pino-compiler/modules/parser.pino:886`) and rejects a `void` initializer, but does not reject `void` as a parameter, field or collection element type.
- Legacy: `void` is accepted in every position at check time; the C backend only fails on a `void` parameter ("parameter declared as void").

## Options
1. `void` is writable as a function return type, as the return type of a function type and as the success type of `Result`; it is a checker error as a parameter, variable, field or collection element type.
2. `void` is never writable; absence of a return type means `void`.

## Decision
Option 1, decided by the owner on 2026-11-20: `void` is a writable type. It is allowed as a function return type, as the return type of a function type (`fn (int) -> void`), and as the success type of `Result` (`Result[void, E]`, a function that can only fail). It is a checker error as a parameter, variable, field or collection element type.
