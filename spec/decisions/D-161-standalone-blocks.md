# D-161: Standalone `{ ... }` blocks

Status: accepted
Raised by: 028
Feature: spec/features/variables.md

## Question
Is a `{ ... }` at the start of a statement a block statement that opens a nested scope?

## Evidence
- `.tickets/020-spec-record-parser-decisions/changelog.md` ("Left Open") lists "standalone `{ ... }` block statements" as undecided.
- Self-host: `projects/pino-compiler/modules/parser.pino:710` returns `Statement::Compound(block)` for a statement-start `{`, and audit 028 section 2.2 lists the standalone `{ ... }` among the scopes where declarations are hoisted.
- Legacy: a standalone block parses but does not scope its declarations:

```pino
val x = 1
{
  val x = 2
  println(x)
}
println(x)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `2`, `2` (the block's `x` leaks) |
| VM | `2`, `2` |
| C | `2`, `2` |

## Options
1. `{ ... }` at statement start is a block statement that opens a nested scope, with the shadowing rules of D-018.
2. Reject a standalone block.

## Decision
Option 1, decided by the owner on 2026-11-20: `{ ... }` at statement start is a block statement that opens a nested scope; shadowing follows D-018 (a nested scope may shadow an outer name, a same-scope redeclaration is an error).
