# D-005: Identifiers that are C keywords

Status: open
Raised by: 004
Feature: spec/features/loops.md

## Question
How must an identifier that is a C keyword (for example `char`) behave on the C backend: be renamed in the generated C, or be reserved in Pino?

## Evidence
- Reference: the string-iteration example binds the loop variable to `char` (`for char in "🌲Pino"`, `LANGUAGE_REFERENCE.md:405`). §1 only fixes the `snake_case` convention for user identifiers (`LANGUAGE_REFERENCE.md:50`) and lists no reserved words.
- Tests: `DataStructureTests.TestStringLoopSingleVar` and `TestStringLoopDoubleVar` use `char` as a loop variable on tree-walk and VM only (`pino-csharp.tests/Execution/DataStructureTests.cs:249`, `:267`). No C test does.
- Implementation: the C backend emits the Pino name unchanged as a C declaration (`pino-csharp/Compiler/TranspilerC.cs:3860`), so `char` becomes a C declaration named `char` and TCC rejects it.

```pino
var count = 0
for char in "Pino" {
  count = count + 1
}
println(count)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `4` |
| VM | `4` |
| C | TCC error `too many basic types` |

Spec impact: `conformance/loops/in-string-char-keyword.pino` is recorded as a known failure for C. Other cases use `ch`.

## Options
1. Mangle C keywords in the backend (for example `char` to `char_`): the reference example works on all engines. Needs a compiler change.
2. Reserve C keywords in the checker with a clear error: portable, but the reference example must change and tree-walk and VM gain a restriction.
3. Document the limitation for the C backend only: no compiler change, but engines then disagree for a valid program.

## Decision
_Pending: owner decides._
