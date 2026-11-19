# D-035: `else => expr` after an `if` statement

Status: open
Raised by: 006
Feature: spec/features/conditionals.md

## Question
Is the arrow form `else => expression` valid after an `if` block, or only inside `match`?

## Evidence
- Reference: the `if` statement shows only `else { ... }` and `else if` (`LANGUAGE_REFERENCE.md:338-352`). The arrow form is documented only for `match` arms (`LANGUAGE_REFERENCE.md:440-459`).
- Tests: none for `if`.
- Implementation: `ParseIfStatement` and `ParseMatchStatement` share `ParseElseStatement`, which accepts `else => expression` as well as `else { ... }` (`pino-csharp/Parser/Parser.Statement.cs:485-519`). Proposal 006 documents the arrow form for match arms only.

```pino
if 1 > 2 {
  println("no")
} else => println("arrow else")
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `arrow else` |
| VM | `arrow else` |
| C | `arrow else` |

## Options
1. Document `else => expr` for `if` statements too.
2. Restrict the arrow form to `match` and make the parser reject it after an `if` block.

## Decision
_Pending: owner decides._
