# D-034: Meaning of a bare identifier in a `when` arm

Status: accepted
Raised by: 006
Feature: spec/features/match.md

## Question
What does `when name { ... }` mean when `name` is a plain identifier: a comparison with the variable `name`, or a binding that matches any value?

## Evidence
- Reference: `when` arms list values; the examples use only literals and `Enum::Member` (`LANGUAGE_REFERENCE.md:421-459`). Identifiers in arms are not discussed.
- Tests: none for plain identifiers. Binding patterns appear only inside union variants such as `when Shape::Circle(r)`.
- Implementation: `ParsePattern` turns a lone identifier into an `IdentifierPattern`, which binds the subject to a new name (`pino-csharp/Parser/Parser.Statement.cs:716-720`), so the arm matches every value and shadows any outer variable.

```pino
val k = 5
match 3 {
  when k { println("k matched") }
  else { println("else") }
}
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `k matched` (the subject is `3`, `k` is `5`) |
| VM | nothing (the VM does not compile `match`) |
| C | `k matched` |

## Options
1. Keep the binding meaning and document it (an identifier arm is a catch-all that names the subject).
2. Treat an identifier as a value to compare with, like a literal: needs a parser or checker change; variants such as `when x` with an in-scope `x` change meaning.
3. Reject bare identifiers in `when` arms until a meaning is chosen.

## Decision
Option 3, decided by the owner on 2026-11-20 (ticket 031, OQ-24): a bare identifier in a `when` arm (`when k { ... }`) is a checker error, so it can neither compare with a variable nor silently bind a new name. `else` is the catch-all. Normative rule: MATCH-E8.

**Extended on 2026-11-20 (033 follow-up), decided by the owner:** confirmed that `else` is the only catch-all arm in `match` (MATCH-11, D-166).
