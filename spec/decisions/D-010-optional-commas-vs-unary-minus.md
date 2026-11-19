# D-010: Optional commas versus unary minus

Status: open
Raised by: 005
Feature: spec/features/lexical.md

## Question
When commas between list elements are optional, should `[a -b]` mean the two elements `a` and `-b`, or the single subtraction `a - b`?

## Evidence
- Reference: §1 "Syntax Guidelines" says commas are "completely optional when elements are separated by whitespace or newlines" in vectors, maps, struct initialization, imports, `when` cases and function arguments (`LANGUAGE_REFERENCE.md:51-57`). It never mentions how a leading `-` is read. §3 shows negative literals such as `-10` and `-0.005` (`LANGUAGE_REFERENCE.md:86-88`).
- Tests: `ArithmeticTests.TestUnaryOperators` asserts `-2 * 3`, `-2 + 3` and `-(2 + 3)` (`ArithmeticTests.cs:128-150`). No test combines a comma-less list with a leading `-`.
- Implementation: the lexer emits `-` as an operator token (`Lexer.cs:60`), and after each element the expression parser keeps consuming binary operators, so `-` always continues the previous expression (`Parser.Expression.cs:157-166`). The comma is consumed only when present.

Minimal snippet:

```pino
val a = 5
val b = 2
println([a -b])
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `[3]` (the subtraction `a - b`; the reading suggested by the reference would be `[5, -2]`) |
| VM | `null` (the VM does not support vector literals; the parser is shared, so it also reads `a - b`) |
| C | `[3]` |

## Options
1. Keep the current reading (binary minus wins): simple and predictable; the spec states that a comma-less list needs a comma, or parentheses, before a negative element, and the reference gets a note.
2. Read `-` as unary minus when whitespace precedes it and none follows it (`a -b`): matches the intent of optional commas, but makes `a -b` and `a - b` differ, which is surprising.
3. Make commas required before a negative element and report an error for `[a -b]`: removes the ambiguity at the cost of rejecting a legal-looking program.

## Decision
_Pending: owner decides._
