# D-148: Arrow arms are only for `match` expressions

Status: accepted
Raised by: owner (2026-11-20)
Feature: spec/features/match.md

## Question
May a `match` statement use arrow arms (`when v -> expr` or `else -> expr`), or are arrow arms restricted to `match` expressions?

## Evidence
- `spec/features/match.md` (Syntax paragraph and `MATCH-7`) previously stated that the same construct is a statement and an expression with either a braced block or `->` in either position.
- Self-host scan (`projects/pino-compiler/**/*.pino`): 0 statement-position `match` constructs use arrow arms; all arrow arms in the self-host appear in expression-position `match`es.
- Owner ruling (2026-11-20): "the arrow syntax is only compatible with match expr, not match statements."

## Options
1. Restrict arrow arms (`when v -> expr`, `else -> expr`) to `match` expressions only, and require braced block arms `{ ... }` in `match` statements.
2. Allow arrow arms in both `match` statements and `match` expressions.

## Decision
Option 1: in a `match` **statement**, every `when` and `else` arm must be a block `{ ... }`; an arm written `when v -> expr` or `else -> expr` there is a parse error. Arrow arms are allowed only in a `match` **expression**.

Definitions:
- A `match` is an **expression** when it is the operand of `return`, the initializer of `val`/`var`, the right side of an assignment, an argument, an operand of an operator, or an element of a collection or struct literal.
- A `match` that begins a statement is a **statement**.
