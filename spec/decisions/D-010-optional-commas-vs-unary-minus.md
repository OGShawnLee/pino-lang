# D-010: Optional commas versus unary minus

Status: accepted
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
Option 3, decided by the owner on 2026-11-19 ("ambiguous form is an error"):
- Contexts: only where commas are optional and whitespace separates elements — vector literals, map literal entries, call argument lists, and `when` value lists.
- In those contexts, a `-` with whitespace on exactly one side (`a -b` or `a- b`) is a compile error whose message must contain `"ambiguous '-'"` and suggest `"write 'a - b' to subtract or 'a, -b' for two elements"`.
- `a - b` and `a-b` subtract; `a, -b` is two elements; a leading element such as `[-b]` or `f(-1)` is a plain unary minus and is not ambiguous.
- Outside those contexts nothing changes: `val x = a -b` is still subtraction.
- Consequence: parsers must inspect token leading-whitespace flags around `-` in comma-optional element lists to reject asymmetric spacing while leaving subtraction and explicit-comma forms intact.
