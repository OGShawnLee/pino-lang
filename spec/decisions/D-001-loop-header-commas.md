# D-001: Commas in the loop header

Status: accepted
Raised by: 004
Feature: spec/features/loops.md

## Question
Are commas optional in the two-variable loop header, so that `for i x in v` is equivalent to `for i, x in v`?

## Evidence
- Reference: §1 "Syntax Guidelines" says commas are "completely optional" in vectors, maps, struct initialization, imports, match `when` cases and "Function/method arguments and parameters" (`LANGUAGE_REFERENCE.md:51-57`). Loop headers are not in that list. §5 "The Universal Loop (`for`)" only shows the comma form `for index, fruit in fruits` (`LANGUAGE_REFERENCE.md:414`).
- Tests: every loop test uses the comma form (`ControlFlowTests.cs:96`, `ControlFlowTests.cs:167`, `DataStructureTests.cs:201`, `DataStructureTests.cs:267`). No test asserts the comma-less form or its rejection.
- Implementation: `ParseLoopStatement` reads the first identifier, and only a `,` makes it the key variable (`pino-csharp/Parser/Parser.Statement.cs:451-461`). Without the comma the next token must be `in`, otherwise parsing fails with `Expected 'in' in loop declaration` (`pino-csharp/Parser/Parser.Statement.cs:464-466`).

Snippet without the comma:

```pino
for idx char in "Pi" {
  println(idx)
}
```

Snippet with the comma:

```pino
for idx, ch in "Pi" {
  println(idx)
}
```

| Engine | Without comma | With comma |
| :--- | :--- | :--- |
| tree-walk | `PARSER: Expected 'in' in loop declaration` | `0` then `1` |
| VM | `PARSER: Expected 'in' in loop declaration` | `0` then `1` |
| C | same parse error (the parser is shared by all engines) | `0` then `1` (`conformance/loops/indexed-string.pino` passes) |

Note: `for idx, char in "Pi"` (with `char` as the variable name) also prints `0` then `1` on tree-walk and VM but does not compile on the C backend; see D-005.

## Options
1. Keep the comma required: the spec says the loop header needs a comma, and the reference gets an explicit note that §1 does not cover loop headers. No compiler change.
2. Make the comma optional: matches the spirit of §1, requires a parser change, and needs a rule plus cases for `for i x in v`. The header stays unambiguous because the first token after the bindings must be `in`.
3. Keep the comma required and make the error message mention the comma, for example `Expected ',' or 'in' in loop declaration`. No change in what is accepted.

## Decision
Option 2: the loop-header comma is optional; `for i x in v` equals `for i, x in v`, decided by the owner on 2026-11-19. Consequence: both `for i, x in coll` and `for i x in coll` are valid two-variable loop headers, and the legacy parser's rejection of the comma-less form is a known failure.
