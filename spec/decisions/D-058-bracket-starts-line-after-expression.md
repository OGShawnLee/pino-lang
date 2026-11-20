# D-058: A line starting with `[` after an expression statement

Status: accepted
Raised by: 007
Feature: spec/features/vectors.md

## Question
Should a `[` at the start of a line begin a new statement (a vector literal) instead of indexing the expression on the previous line?

## Evidence
- Reference: silent. §1 says collection elements may be separated by newlines (`LANGUAGE_REFERENCE.md:51-54`), but says nothing about statements that begin with `[`.
- Tests: none.
- Implementation: the postfix loop of `ParsePrimaryExpression` treats any `[` after an expression as index access, whatever the line break (`pino-csharp/Parser/Parser.Expression.cs:286-292`). A vector literal can therefore never start a statement that follows an expression statement.

Snippet:

```pino
println("a")
[3, 4]:each(println)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `PARSER: Expected ']' to close index access` |
| VM | same parse error |
| C | same parse error |

The reference's own examples, such as `[10, 20]:each(...)` (`LANGUAGE_REFERENCE.md:1177`), start a statement; they work only as the first statement or after a declaration.

## Options
1. Keep it and document that a leading `[` continues the previous expression; assign the literal first.
2. End the expression at a line break before `[` (as many newline-sensitive languages do): parser change.

## Decision
Option 2: a line break before `[` ends the expression, so a `[` at the start of a line begins a new statement (a vector literal), decided by the owner on 2026-11-19. Consequence: indexing must be on the same line as its operand, and a statement starting with `[` on a new line is parsed as a vector literal rather than index access.
